#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;

using Kern.Game.Rendering;

namespace Kern.Game.Entities.Buildings
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Game", sourceAssembly: null, sourceClassName: "Building")]
    public class Building : MonoBehaviour
    {
        private Transform? _clanTransform;
        private Transform? _visualTransform;
        private BuildingType? _buildingType;
        private byte _variant;
        private byte _linkedClan;
        private CancellationTokenSource? _cts;
        private bool _watchingAssets;
        private Sprite? _buildingSprite;
        private Sprite? _clanSprite;
        private WorldEntityBatchRenderer.SpriteHandle? _buildingBatchHandle;
        private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;

        [Inject]
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        protected void Awake()
        {
            Transform? existingVisual = transform.Find("BuildingVisual");
            GameObject visualObject = existingVisual != null
                ? existingVisual.gameObject
                : (_sceneObjects != null
                    ? _sceneObjects.Create("BuildingVisual", RuntimeOwner.Buildings)
                    : throw new InvalidOperationException(
                        "Building requires injected ISceneObjectFactory before creating its visual."));
            visualObject.transform.SetParent(transform, worldPositionStays: false);
            visualObject.transform.localPosition = Vector3.zero;
            _visualTransform = visualObject.transform;

            Transform? existingClan = transform.Find("ClanIcon");
            GameObject clanGo = existingClan != null
                ? existingClan.gameObject
                : (_sceneObjects != null
                    ? _sceneObjects.Create("ClanIcon", RuntimeOwner.Buildings)
                    : throw new InvalidOperationException(
                        "Building requires injected ISceneObjectFactory before creating ClanIcon."));
            clanGo.transform.SetParent(transform, worldPositionStays: false);
            clanGo.transform.localPosition = new Vector3(0.6f, -0.5f, 0);
            _clanTransform = clanGo.transform;
        }

        public void Initialize(BuildingType buildingType, byte variant, byte linkedClan)
        {
            if (_buildingType == buildingType && _variant == variant && _linkedClan == linkedClan && _cts != null)
            {
                return;
            }

            _buildingType = buildingType;
            _variant = variant;
            _linkedClan = linkedClan;

            LoadAssets();
        }

        private static string BuildingPath(BuildingType buildingType, byte variant) =>
            $"Visual/Pack/{buildingType}/{variant}.png";

        private static string ClanPath(byte linkedClan) => $"Visual/Clan/{linkedClan}.png";

        // Сервер заменил файлы посреди игры: перезагружаем картинки, только если среди них наши
        private void OnAssetsChanged(IReadOnlyCollection<string> changed)
        {
            if (_cts == null || _buildingType is not BuildingType buildingType)
            {
                return;
            }

            string buildingPath = BuildingPath(buildingType, _variant);
            string? clanPath = _linkedClan != 0 ? ClanPath(_linkedClan) : null;
            foreach (string filename in changed)
            {
                if (string.Equals(filename, buildingPath, StringComparison.Ordinal) ||
                    string.Equals(filename, clanPath, StringComparison.Ordinal))
                {
                    LoadAssets();
                    return;
                }
            }
        }

        private void LoadAssets()
        {
            if (!_watchingAssets)
            {
                _assetLoader.AssetsChanged += OnAssetsChanged;
                _watchingAssets = true;
            }

            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            CancellationToken entityToken = _cts.Token;

            _operations.Run(
                "load_building_visual",
                supervisorToken => RunWithLinkedCancellationAsync(
                    LoadBuildingAsync,
                    entityToken,
                    supervisorToken));
            _operations.Run(
                "load_building_clan_badge",
                supervisorToken => RunWithLinkedCancellationAsync(
                    LoadClanAsync,
                    entityToken,
                    supervisorToken));
        }

        private async UniTask LoadBuildingAsync(CancellationToken token)
        {
            if (_buildingType is not BuildingType buildingType)
            {
                return;
            }

            string buildingPath = BuildingPath(buildingType, _variant);

            Texture2D? buildingTexture = await TryLoadOptionalTextureAsync(
                _assetLoader,
                buildingPath,
                token);
            if (token.IsCancellationRequested || buildingTexture == null)
            {
                return;
            }

            if (_buildingSprite != null)
            {
                Destroy(_buildingSprite);
            }

            _buildingSprite = Sprite.Create(
                buildingTexture,
                new Rect(0, 0, buildingTexture.width, buildingTexture.height),
                new Vector2(0.5f, 0.5f),
                RenderingConstants.CELL_SIZE);
            EnsureBatchHandles();
            _entityBatchRenderer.SetSprite(_buildingBatchHandle!, _buildingSprite);
            _buildingBatchHandle!.SetEnabled(true);

            UpdateClanPosition();
        }

        private async UniTask LoadClanAsync(CancellationToken token)
        {
            if (_linkedClan == 0)
            {
                if (_clanBatchHandle != null)
                {
                    _entityBatchRenderer.SetSprite(_clanBatchHandle, null);
                }

                return;
            }

            Texture2D? clanTexture = await TryLoadOptionalTextureAsync(
                _assetLoader,
                ClanPath(_linkedClan),
                token);
            if (token.IsCancellationRequested || clanTexture == null || _clanTransform == null)
            {
                return;
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanTransform.localScale = Vector3.one * 0.8f;
            EnsureBatchHandles();
            _entityBatchRenderer.SetSprite(_clanBatchHandle!, _clanSprite);
            _clanBatchHandle!.SetEnabled(true);

            UpdateClanPosition();
        }

        // Таймаута у активного соединения нет: ответ обязан прийти, а его
        // отсутствие фейлит запрос разрывом транспорта. Здесь остаётся
        // только мягкая трактовка провала опциональной текстуры.
        private static async UniTask<Texture2D?> TryLoadOptionalTextureAsync(
            IAssetLoader loader,
            string filename,
            CancellationToken cancellationToken)
        {
            try
            {
                return await loader.GetTextureAsync(filename, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Building] Optional texture '{filename}' was skipped: {exception.Message}");
                return null;
            }
        }

        private static async UniTask RunWithLinkedCancellationAsync(
            Func<CancellationToken, UniTask> operation,
            CancellationToken entityToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                entityToken,
                supervisorToken);
            await operation(linkedCancellation.Token);
        }

        private void UpdateClanPosition()
        {
            if (_clanTransform == null)
            {
                return;
            }

            // Position to the right and slightly below the center
            float packWidth = _buildingSprite != null
                ? _buildingSprite.texture.width
                : RenderingConstants.CELL_SIZE;
            float xOffset = (packWidth / (RenderingConstants.CELL_SIZE * 2f)) + 0.1f;
            _clanTransform.localPosition = new Vector3(xOffset, -0.5f, 0);
            _clanBatchHandle?.MarkTransformDirty();
        }

        private void EnsureBatchHandles()
        {
            if (_entityBatchRenderer == null)
            {
                return;
            }

            if (_visualTransform != null)
            {
                _buildingBatchHandle ??= _entityBatchRenderer.RegisterSprite(
                    _visualTransform,
                    RenderingConstants.BUILDING_ROOF_SORTING_ORDER,
                    isStatic: true,
                    emitsLight: true);
            }

            if (_clanTransform != null)
            {
                _clanBatchHandle ??=
                    _entityBatchRenderer.RegisterSprite(
                        _clanTransform,
                        RenderingConstants.BUILDING_ROOF_SORTING_ORDER + 10,
                        isStatic: true);
            }
        }

        protected void OnDestroy()
        {
            if (_watchingAssets && _assetLoader != null)
            {
                _assetLoader.AssetsChanged -= OnAssetsChanged;
            }

            _cts?.Cancel();
            _cts?.Dispose();

            _entityBatchRenderer?.UnregisterSprite(_buildingBatchHandle);
            _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);

            if (_buildingSprite != null)
            {
                Destroy(_buildingSprite);
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }
        }
    }
}
