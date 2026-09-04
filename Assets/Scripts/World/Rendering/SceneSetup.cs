#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.World
{
    /// <summary>
    /// Scene setup manager that ensures the world background renderer is properly configured.
    /// This script should be added to a persistent GameObject in the scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // Run before other scripts
    public class SceneSetup : MonoBehaviour
    {
        [Inject]
        private ITextureStorageService _textureStorage = null!;
        [Inject]
        private SurfaceRenderer _surfaceRenderer = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        private bool _surfaceRendererSetupStarted;
        private bool _surfaceRendererSetupSucceeded;
        private bool _surfaceSetupFailureLogged;

        protected void Start()
        {
            TryInitialize();
        }

        protected void Update()
        {
            // The success latch keeps _surfaceRendererSetupStarted set, so the
            // started guard alone already covers the completed case.
            if (!_surfaceRendererSetupStarted)
            {
                TryInitialize();
            }
        }

        private void TryInitialize()
        {
            TryStartSurfaceRendererSetup();
        }

        private void TryStartSurfaceRendererSetup()
        {
            if (_surfaceRendererSetupStarted)
            {
                return;
            }

            if (_textureStorage == null || _operations == null)
            {
                return;
            }

            _surfaceRendererSetupStarted = true;
            _operations.Run("surface_renderer_setup", SetupSurfaceRendererAsync);
        }

        private async UniTask SetupSurfaceRendererAsync(
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                destroyCancellationToken);
            CancellationToken cancellationToken = linkedCancellation.Token;
            ITextureStorageService textureStorage = _textureStorage;

            try
            {
                Texture2D transitTexture = await textureStorage.GetTextureAsync(
                    "transit.png",
                    cancellationToken) ??
                    throw new InvalidOperationException(
                        "Required local surface texture 'transit.png' could not be decoded.");
                Texture2D perspectiveTexture = await textureStorage.GetTextureAsync(
                    "perspective.png",
                    cancellationToken) ??
                    throw new InvalidOperationException(
                        "Required local surface texture 'perspective.png' could not be decoded.");
                Texture2D redRockTexture = await textureStorage.GetTextureAsync(
                    "Cells/117.png",
                    cancellationToken) ??
                    throw new InvalidOperationException(
                        "Required local surface texture 'Cells/117.png' could not be decoded.");
                cancellationToken.ThrowIfCancellationRequested();

                RuntimeTextureFactory.ApplySampling(
                    transitTexture,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);
                RuntimeTextureFactory.ApplySampling(
                    perspectiveTexture,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);
                RuntimeTextureFactory.ApplySampling(
                    redRockTexture,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);

                SurfaceRenderer surfaceRenderer =
                    _surfaceRenderer ??
                    throw new InvalidOperationException(
                        "SceneSetup requires the injected SurfaceRenderer.");
                surfaceRenderer.SetLocalAssets(
                    transitTexture,
                    perspectiveTexture,
                    redRockTexture);
                _surfaceSetupFailureLogged = false;
                _surfaceRendererSetupSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the expected teardown path during a domain reload.
            }
            catch (Exception ex)
            {
                if (!_surfaceSetupFailureLogged)
                {
                    Debug.LogWarning($"[SceneSetup] SurfaceRenderer setup deferred: {ex.Message}");
                    _surfaceSetupFailureLogged = true;
                }
            }
            finally
            {
                // Retry only while the surface has not been applied. Plain fields
                // reset to defaults on domain reload (scene serialization round
                // trip), so the success latch clears itself when re-setup is needed.
                if (!_surfaceRendererSetupSucceeded)
                {
                    _surfaceRendererSetupStarted = false;
                }
            }
        }

    }
}
