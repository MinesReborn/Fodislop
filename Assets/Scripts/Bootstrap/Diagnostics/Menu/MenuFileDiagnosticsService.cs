#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

using Kern.Networking.Offline;
using Kern.Core;


namespace Kern.Bootstrap.Diagnostics;

/// <summary>
/// Реализация IMenuFileDiagnosticsService для окна «Диагностика и целостность
/// файлов»: FSB5-сигнатура и MD5 FMOD-банков, штамп и структура дискового кэша
/// офлайн-карты, грузимость и актуальность коллекции шейдерных состояний.
/// Чистая диагностика — ничего не пишет и не чинит; REPORT кэшируется на
/// короткое время, чтобы переоткрытия модалки не пересчитывали MD5 зря.
/// </summary>
public sealed class MenuFileDiagnosticsService : IMenuFileDiagnosticsService
{
    private const string Fsb5Magic = "FSB5";

    private static readonly byte[] s_fsb5MagicBytes = { 0x46, 0x53, 0x42, 0x35 }; // "FSB5"

    private static readonly TimeSpan s_reportCacheTtl = TimeSpan.FromSeconds(30);

    // Порядок ключей = порядок строк в окне и порядок проверок в отчёте.
    private static readonly string[] s_checkLabelKeys =
    [
        "mainmenu.repair_item_bank_master",
        "mainmenu.repair_item_bank_strings",
        "mainmenu.repair_item_map_cache",
        "mainmenu.repair_item_shader_states",
    ];

    private MenuFileDiagnosticsReport? _cachedReport;
    private DateTime _cachedReportAt;

    public IReadOnlyList<string> CheckLabelKeys => s_checkLabelKeys;

    public async UniTask<MenuFileDiagnosticsReport> RunAllAsync(
        Action<int>? activeCheck,
        Action<float>? fraction,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cachedReport is { } cached && DateTime.UtcNow - _cachedReportAt < s_reportCacheTtl)
        {
            return cached;
        }

        // Application.* читаются только на главном потоке — снимаем пути до ухода в пул.
        string persistentRoot = Application.persistentDataPath;
        string streamingRoot = Application.streamingAssetsPath;
        string? packagedStamp = IsLocalStreamingAssets(streamingRoot)
            ? null
            : $"package:{Application.buildGUID}";

        float mapFraction = 2f / s_checkLabelKeys.Length;
        float shaderFraction = 3f / s_checkLabelKeys.Length;

        fraction?.Invoke(0f);
        activeCheck?.Invoke(0);
        var masterBank = await UniTask.RunOnThreadPool(
            () => CheckBankFile(s_checkLabelKeys[0], "Master.bank", persistentRoot, cancellationToken),
            cancellationToken: cancellationToken);

        fraction?.Invoke(1f / s_checkLabelKeys.Length);
        activeCheck?.Invoke(1);
        var stringsBank = await UniTask.RunOnThreadPool(
            () => CheckBankFile(s_checkLabelKeys[1], "Master.strings.bank", persistentRoot, cancellationToken),
            cancellationToken: cancellationToken);

        fraction?.Invoke(mapFraction);
        activeCheck?.Invoke(2);
        var mapCache = await UniTask.RunOnThreadPool(
            () => CheckMapCache(s_checkLabelKeys[2], streamingRoot, persistentRoot, packagedStamp, cancellationToken),
            cancellationToken: cancellationToken);

        fraction?.Invoke(shaderFraction);
        activeCheck?.Invoke(3);
        // Resources.Load допустим только на главном потоке — шейдеры проверяем здесь.
        var shaderStates = CheckShaderStates(s_checkLabelKeys[3]);

        fraction?.Invoke(1f);
        var results = new[] { masterBank, stringsBank, mapCache, shaderStates };
        var report = new MenuFileDiagnosticsReport(
            results,
            AllOk: Array.TrueForAll(results, check => check.Status == MenuFileCheckStatus.Ok));
        _cachedReport = report;
        _cachedReportAt = DateTime.UtcNow;
        return report;
    }

    /// <summary>
    /// Банк FMOD: наличие в persistent-кэше Assets/Audio, размер,
    /// сигнатура FSB5 и контрольная сумма MD5 всей файла.
    /// </summary>
    internal static MenuFileCheckResult CheckBankFile(
        string labelKey,
        string fileName,
        string persistentRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(persistentRoot, "Assets", "Audio", fileName);
            if (!File.Exists(path))
            {
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Failed,
                    $"нет файла: persistentDataPath/Assets/Audio/{fileName}");
            }

            var info = new FileInfo(path);
            if (info.Length < Fsb5Magic.Length)
            {
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Failed,
                    $"{info.Length} байт — меньше сигнатуры FSB5");
            }

            using (var stream = File.OpenRead(path))
            {
                var magic = new byte[s_fsb5MagicBytes.Length];
                if (stream.Read(magic, 0, magic.Length) < magic.Length ||
                    !magic.AsSpan().SequenceEqual(s_fsb5MagicBytes))
                {
                    return new MenuFileCheckResult(
                        labelKey,
                        MenuFileCheckStatus.Failed,
                        "нет сигнатуры FSB5 — файл не FMOD-банк");
                }
            }

            string hash = ComputeMd5(path, cancellationToken);
            return new MenuFileCheckResult(
                labelKey,
                MenuFileCheckStatus.Ok,
                $"FSB5 · {FormatBytes(info.Length)} · MD5 {hash[..16]}…");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new MenuFileCheckResult(labelKey, MenuFileCheckStatus.Failed, exception.Message);
        }
    }

    /// <summary>Дисковый кэш офлайн-карты: наличие источника и кэша, отметка, заголовок.</summary>
    internal static MenuFileCheckResult CheckMapCache(
        string labelKey,
        string streamingRoot,
        string persistentRoot,
        string? packagedStamp,
        CancellationToken cancellationToken)
    {
        try
        {
            DummyMapCacheSnapshot snapshot = DummyWorldMapDiagnostics.InspectLocalCache(
                DummyConnection.DefaultWorldCodeName,
                streamingRoot,
                persistentRoot,
                packagedStamp);
            return BuildMapCacheCheck(labelKey, snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new MenuFileCheckResult(labelKey, MenuFileCheckStatus.Failed, exception.Message);
        }
    }

    /// <summary>Маппинг снимка карты на строку отчёта. Отдельно от файлового чтения — ради тестов.</summary>
    internal static MenuFileCheckResult BuildMapCacheCheck(string labelKey, DummyMapCacheSnapshot snapshot)
    {
        string sizeText = FormatBytes(snapshot.CachedMapBytes);
        if (snapshot.CachedMapPresent)
        {
            if (snapshot.HeaderCorrupted)
            {
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Failed,
                    $"{sizeText} · заголовок карты повреждён");
            }

            if (!snapshot.HeaderValid)
            {
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Warning,
                    $"{sizeText} · файл занят — прочитать не удалось");
            }

            return snapshot.Stamp switch
            {
                DummyMapCacheStampState.Stale => new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Warning,
                    $"{sizeText} · кэш устарел — перепакуется при входе"),
                // Current, а также Unknown (источник внутри пакета): структура цела.
                _ => new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Ok,
                    $"{sizeText} · карта {snapshot.WorldWidthCells}×{snapshot.WorldHeightCells}"),
            };
        }

        if (snapshot.SourcePresent)
        {
            return new MenuFileCheckResult(
                labelKey,
                MenuFileCheckStatus.Warning,
                $"кэш не распакован — распакуется при первом входе (источник: {FormatBytes(snapshot.SourceBytes)})");
        }

        if (snapshot.SourceInsidePackage)
        {
            return new MenuFileCheckResult(
                labelKey,
                MenuFileCheckStatus.Warning,
                "кэш не распакован — распакуется при первом входе");
        }

        return new MenuFileCheckResult(
            labelKey,
            MenuFileCheckStatus.Failed,
            $"нет ни источника ({snapshot.WorldCodeName}_cells.zip), ни кэша");
    }

    private static MenuFileCheckResult CheckShaderStates(string labelKey)
    {
        try
        {
            var collection = Resources.Load<GraphicsStateCollection>(
                ResourcePaths.GraphicsStateCollection);
            if (collection == null)
            {
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Failed,
                    $"нет ассета Resources/{ResourcePaths.GraphicsStateCollection}");
            }

            int total = collection.totalGraphicsStateCount;
            if (total == 0)
            {
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Failed,
                    "коллекция пуста: 0 состояний");
            }

            string recorded = collection.qualityLevelName;
            string[] qualityNames = QualitySettings.names;
            int qualityIndex = QualitySettings.GetQualityLevel();
            string current = qualityIndex >= 0 && qualityIndex < qualityNames.Length
                ? qualityNames[qualityIndex]
                : string.Empty;
            if (!string.IsNullOrEmpty(recorded) &&
                !string.Equals(recorded, current, StringComparison.Ordinal))
            {
                // Тот же критерий, которым ShaderWarmupService отвергает прогрев:
                // коллекция не с того качества даёт холодные состояния в кадре.
                return new MenuFileCheckResult(
                    labelKey,
                    MenuFileCheckStatus.Warning,
                    $"{total} состояний · записана для «{recorded}», активна «{current}»");
            }

            return new MenuFileCheckResult(labelKey, MenuFileCheckStatus.Ok, $"{total} состояний");
        }
        catch (Exception exception)
        {
            return new MenuFileCheckResult(labelKey, MenuFileCheckStatus.Failed, exception.Message);
        }
    }

    private static string ComputeMd5(string path, CancellationToken cancellationToken)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var stream = File.OpenRead(path);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            incrementalHash.AppendData(buffer, 0, read);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return BitConverter.ToString(incrementalHash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static bool IsLocalStreamingAssets(string streamingRoot) =>
        !streamingRoot.Contains("://", StringComparison.Ordinal);

    internal static string FormatBytes(long bytes)
    {
        double value = bytes;
        string unit = "B";
        if (value >= 1024)
        {
            value /= 1024;
            unit = "KB";
        }

        if (value >= 1024)
        {
            value /= 1024;
            unit = "MB";
        }

        if (value >= 1024)
        {
            value /= 1024;
            unit = "GB";
        }

        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + unit;
    }
}
