#nullable enable

using UnityEngine;

namespace Kern.Core;

// Resolves THE gameplay camera, as opposed to an arbitrary tag-selected camera.
//
// Tag-based lookup scans every loaded scene, and this project keeps
// two scenes loaded at once by design: MainMenu is not unloaded when the game
// starts - it stays alive only for the menu scene, so the whole
// descent runs with both scenes present. For as long as any camera in the menu
// scene is also tagged MainCamera, the result is a coin flip, and it is queried
// at exactly the wrong moment: GameStartupPipeline initializes every manager
// while the menu is still up, and those managers cache the result.
//
// The consequences were not subtle. PostProcessRendererFeature gates its entire
// pass on the resolved gameplay camera, so a miss sends the game's
// post-processing to the menu camera and leaves the game with none.
// PostProcessController pairs its game-scoped WorldUICamera overlay with this
// persistent camera and edits the base camera's culling mask, so a miss strips
// the UI layer from the game camera and configures the overlay for the wrong
// view. TerrainRenderer.Start already carried a hand-written workaround for
// the same problem.
//
// Bootstrap binds the authored application camera once. This helper keeps that
// binding available to non-DI render callbacks without performing scene lookup.
public static class GameplayCamera
{
    // Every current caller re-resolves each frame (LateUpdate, Update, even
    // PostProcessRendererFeature.Execute - once per camera per frame). Without
    // this cache, every one of those ~20 call sites pays for a tag lookup plus,
    // on any miss, a full Object.FindObjectsByType<Camera>() scan-and-allocate
    // - exactly the per-frame O(heap) pattern this project's conventions ban
    // outright. The steady-state gameplay camera does not change frame to
    // frame, so the bound reference remains an O(1) field read.
    private static Camera? _cachedCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        _cachedCamera = null;
    }

    public static void BindPersistent(Camera camera)
    {
        if (camera == null)
        {
            throw new System.ArgumentNullException(nameof(camera));
        }

        _cachedCamera = camera;
    }

    // Returns null rather than guessing when no gameplay camera exists yet -
    // which is the normal state while only the menu is loaded. Callers are
    // expected to retry.
    public static Camera? Resolve()
    {
        if (_cachedCamera != null && _cachedCamera.isActiveAndEnabled)
        {
            return _cachedCamera;
        }

        // The application camera is authored and bound by Bootstrap. Do not
        // guess through the engine's tag lookup: multiple loaded scenes may contain tagged
        // cameras, and selecting one here silently routes rendering to the
        // wrong scene.
        return null;
    }
}
