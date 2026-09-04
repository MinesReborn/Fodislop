#nullable enable

using System;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Fodinae.Rendering
{
    /// <summary>
    /// Keeps the operating system's HDR surface in step with the saved
    /// preference for the whole life of the application.
    /// </summary>
    /// <remarks>
    /// Reconciliation cannot be a one-shot call at startup: a display reports
    /// its HDR capability late, the user can swap monitors, and the OS can drop
    /// HDR mode on its own. It also cannot live in <see cref="DisplayManager"/>,
    /// which exists only inside the MainGame scene — the menu and the loading
    /// screens are on the persistent scope and need the same surface.
    ///
    /// It used to sit in BootstrapLifetimeScope's own Update. That put a
    /// per-frame rendering concern on the composition root, whose job is
    /// building the container and nothing else.
    /// </remarks>
    public sealed class HDROutputReconciler : IStartable, ITickable, IDisposable
    {
        // Probing every frame is pointless: HDR availability changes on the
        // scale of plugging in a monitor, not of a frame.
        private const float ProbeIntervalSeconds = 1f;

        // Через именованный контракт, а не через сырую Camera: тот же объект,
        // но бутстрап заводил IGameplayCamera именно для потребителей DI.
        private readonly IGameplayCamera _camera;
        private float _nextProbeTime;

        public HDROutputReconciler(IGameplayCamera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        public void Tick()
        {
            if (Time.unscaledTime < _nextProbeTime)
            {
                return;
            }

            _nextProbeTime = Time.unscaledTime + ProbeIntervalSeconds;
            Apply();
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        private void Apply()
        {
            Camera camera = _camera.Camera;
            if (camera == null)
            {
                return;
            }

            DisplayManager.HDROutput.Reconcile();
            DisplayManager.HDROutput.ConfigureCamera(camera);
        }
    }
}
