#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Audio.Backend
{
    /// <summary>
    /// Владеет банками FMOD: где взять файл, когда он загружен, когда его
    /// сэмплы действительно лежат в памяти.
    /// </summary>
    /// <remarks>
    /// Выделено из <see cref="FmodAudioBackend"/>, который совмещал две
    /// несвязанные работы: добычу банков с диска или из кэша и собственно
    /// воспроизведение голосов через шины. Первая асинхронная и знает про
    /// файловую систему и загрузчик ассетов, вторая синхронная и знает про
    /// шины и позиции в мире.
    /// </remarks>
    internal sealed class FmodBankLibrary
    {
        private readonly IAssetLoader _assetLoader;
        private readonly IPersistentAssetCache _persistentCache;

        public FmodBankLibrary(IAssetLoader assetLoader, IPersistentAssetCache persistentCache)
        {
            _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
            _persistentCache = persistentCache ??
                throw new ArgumentNullException(nameof(persistentCache));
        }

        private readonly ConcurrentDictionary<string, FMOD.Studio.Bank> _loadedBanks =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _unavailableBanks = new(StringComparer.OrdinalIgnoreCase);
        private bool _requiredBanksDegraded;
        private bool _requiredBanksSettled;

        // Завершается, когда проход по обязательным банкам осел: они
        // загружены либо принят поддерживаемый режим без звука. На это
        // обещание опираются переходы сцен, поэтому первые звуки сцены не
        // теряются из-за ещё грузящегося банка.
        private readonly UniTaskCompletionSource _banksReady = new();

        private const string BANK_PATH = "banks";

        private static readonly string[] _requiredBanks =
        {
            "Master.strings",
            "Master",
        };

        public bool IsDegraded => _requiredBanksSettled && _requiredBanksDegraded;

        public UniTask WaitUntilBanksReadyAsync(CancellationToken cancellationToken = default)
            => _banksReady.Task.AttachExternalCancellation(cancellationToken);

        public async UniTask LoadRequiredBanksAsync(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var bankName in _requiredBanks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (await EnsureBankLoadedAsync(bankName))
                        {
                            if (!await RequestSampleDataAsync(bankName))
                            {
                                _requiredBanksDegraded = true;
                            }
                        }
                        else
                        {
                            _requiredBanksDegraded = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        _requiredBanksDegraded = true;
                        Debug.LogWarning(
                            $"[FmodBankLibrary] FMOD bank '{bankName}' could not be loaded; " +
                            $"continuing with the remaining banks: {exception.Message}");
                    }
                }

                MapBuses();

                // Missing FMOD content is a supported no-audio state. Do not
                // surface it as a gameplay warning or make callers wait for it.
            }
            catch (OperationCanceledException)
            {
                // Audio initialization may be interrupted during domain reload
                // or teardown; no audio service needs to be kept alive then.
            }
            catch (Exception exception)
            {
                _requiredBanksDegraded = true;
                // FMOD is optional presentation. A failed bank must not become
                // an unobserved UniTaskVoid exception or block gameplay startup.
                Debug.LogWarning(
                    $"[FmodBankLibrary] Audio initialization skipped: {exception.Message}");
            }
            finally
            {
                _requiredBanksSettled = true;
                _banksReady.TrySetResult();
            }
        }

        private async UniTask<bool> RequestSampleDataAsync(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (cleanBankName.Equals("Master.strings", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!_loadedBanks.TryGetValue(cleanBankName, out var bank))
            {
                return false;
            }

            const int maxBankWaitFrames = 300;
            bool bankLoaded = false;
            for (int frame = 0; frame < maxBankWaitFrames; frame++)
            {
                bank.getLoadingState(out FMOD.Studio.LOADING_STATE bankState);
                if (bankState == FMOD.Studio.LOADING_STATE.LOADED)
                {
                    bankLoaded = true;
                    break;
                }

                if (bankState == FMOD.Studio.LOADING_STATE.ERROR)
                {
                    Debug.LogWarning(
                        $"[FmodBankLibrary] FMOD bank '{cleanBankName}' entered an error state before sample loading.");
                    return false;
                }

                await UniTask.Yield();
            }

            if (!bankLoaded)
            {
                Debug.LogWarning(
                    $"[FmodBankLibrary] Timed out waiting for bank '{cleanBankName}' to finish loading.");
                return false;
            }

            FMOD.RESULT result = bank.loadSampleData();
            if (result != FMOD.RESULT.OK && result != FMOD.RESULT.ERR_EVENT_ALREADY_LOADED)
            {
                Debug.LogWarning(
                    $"[FmodBankLibrary] FMOD sample data request failed for '{cleanBankName}': {result}.");
                return false;
            }

            // Ждать окончания загрузки сэмплов, а не только её заказа.
            // loadSampleData возвращает управление немедленно и грузит в
            // фоне, поэтому раньше метод сообщал «готово», когда сэмплов
            // ещё не было. На этом обещании стоит WaitUntilBanksReadyAsync,
            // а на нём — переходы сцен. Первый же звук, заказанный сразу
            // после «готово», отбрасывался в CreateVoice по состоянию
            // сэмплов и молча не звучал. Для одноразового SFX это незаметно
            // — его закажут снова; для музыки, которую заказывают один раз
            // за вход в мир, это тишина до конца сессии.
            const int maxSampleWaitFrames = 600;
            for (int frame = 0; frame < maxSampleWaitFrames; frame++)
            {
                bank.getSampleLoadingState(out FMOD.Studio.LOADING_STATE sampleState);
                if (sampleState == FMOD.Studio.LOADING_STATE.LOADED)
                {
                    return true;
                }

                if (sampleState == FMOD.Studio.LOADING_STATE.ERROR)
                {
                    Debug.LogWarning(
                        $"[FmodBankLibrary] Сэмплы банка '{cleanBankName}' перешли в состояние ошибки.");
                    return false;
                }

                await UniTask.Yield();
            }

            Debug.LogWarning(
                $"[FmodBankLibrary] Истекло ожидание загрузки сэмплов банка '{cleanBankName}'.");
            return false;
        }

        /// <summary>
        /// Гарантирует наличие и загрузку .bank файла с диска (CDN cache -> StreamingAssets fallback).
        /// </summary>
        public async UniTask<bool> EnsureBankLoadedAsync(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (_loadedBanks.ContainsKey(cleanBankName))
            {
                return true;
            }

            if (_unavailableBanks.Contains(cleanBankName))
            {
                return false;
            }

            string? bankFilePath = null;
            var localPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Audio", $"{cleanBankName}.bank");

            if (System.IO.File.Exists(localPath))
            {
                bankFilePath = localPath;
            }
            else
            {
                var relativeRemotePath = $"{BANK_PATH}/{cleanBankName}.bank";
                if (_assetLoader.IsKnownMissing(relativeRemotePath))
                {
                    _unavailableBanks.Add(cleanBankName);
                    return false;
                }

                try
                {
                    bankFilePath = await _assetLoader.GetAssetPathAsync(relativeRemotePath);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(bankFilePath) || !System.IO.File.Exists(bankFilePath))
            {
                _unavailableBanks.Add(cleanBankName);
                return false;
            }

            if (!IsFmodBank(bankFilePath))
            {
                Debug.LogWarning(
                    $"[FmodBankLibrary] Ignoring invalid audio bank '{bankFilePath}'.");
                if (bankFilePath.Equals(
                    _persistentCache.GetAssetPath($"banks/{cleanBankName}.bank"),
                    StringComparison.OrdinalIgnoreCase))
                {
                    _persistentCache.RemoveAsset($"banks/{cleanBankName}.bank");
                }

                _unavailableBanks.Add(cleanBankName);
                return false;
            }

            // Load bank metadata synchronously. Sample data is requested separately
            // below, so event playback still rejects samples that are not resident.
            FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.loadBankFile(
                bankFilePath,
                FMOD.Studio.LOAD_BANK_FLAGS.NORMAL,
                out var bank);

            if (result == FMOD.RESULT.OK)
            {
                _loadedBanks[cleanBankName] = bank;
                Debug.Log($"[FmodBankLibrary] Успешно загружен банк '{cleanBankName}' из: {bankFilePath}");
                return true;
            }
            else if (result == FMOD.RESULT.ERR_EVENT_ALREADY_LOADED)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBank(
                    $"bank:/{cleanBankName}",
                    out var existingBank) == FMOD.RESULT.OK)
                {
                    _loadedBanks[cleanBankName] = existingBank;
                    return true;
                }

                Debug.LogWarning(
                    $"[FmodBankLibrary] FMOD bank '{cleanBankName}' is already loaded, but its handle could not be resolved.");
                return false;
            }

            Debug.LogWarning(
                $"[FmodBankLibrary] FMOD loadBankFile failed for '{cleanBankName}': {result}.");
            _unavailableBanks.Add(cleanBankName);
            return false;
        }

        private static bool IsFmodBank(string path)
        {
            Span<byte> header = stackalloc byte[12];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = stream.Read(header);
            return read == header.Length &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header[8..12].SequenceEqual("FEV "u8);
        }

        public void UnloadBank(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (_loadedBanks.TryGetValue(cleanBankName, out var bank))
            {
                bank.unload();
                _loadedBanks.TryRemove(cleanBankName, out _);
                Debug.Log($"[FmodBankLibrary] Банк '{cleanBankName}' выгружен из памяти.");
            }
        }

        /// <summary>Выгружает все банки.</summary>
        public void UnloadAll()
        {
            foreach (var bank in _loadedBanks.Values)
            {
                bank.unload();
            }

            _loadedBanks.Clear();
        }
    }
}
