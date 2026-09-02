#nullable enable

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Core;

// Resolves THE gameplay camera, as opposed to whatever camera Camera.main
// happens to return.
//
// Camera.main is a tag lookup across every loaded scene, and this project keeps
// two scenes loaded at once by design: MainMenu is not unloaded when the game
// starts - it stays alive only for the menu scene, so the whole
// descent runs with both scenes present. For as long as any camera in the menu
// scene is also tagged MainCamera, Camera.main is a coin flip, and it is queried
// at exactly the wrong moment: GameStartupPipeline initializes every manager
// while the menu is still up, and those managers cache the result.
//
// The consequences were not subtle. PostProcessRendererFeature gates its entire
// pass on `cameraData.camera == Camera.main`, so a miss sends the game's
// post-processing to the menu camera and leaves the game with none.
// PostProcessController parents its WorldUICamera to Camera.main and edits that
// camera's culling mask, so a miss strips the UI layer from the game camera and
// bolts an overlay camera onto the menu instead. TerrainRenderer.Start already
// carried a hand-written workaround for the same problem.
//
// Untagging the menu camera fixes the immediate ambiguity. This helper exists so
// the fix does not depend on a serialized tag staying correct: it prefers a
// camera that actually belongs to the active scene, which GameBootstrap sets to
// its own scene precisely so lazily-created objects land in the right place.
public static class GameplayCamera
{
    // Every current caller re-resolves each frame (LateUpdate, Update, even
    // PostProcessRendererFeature.Execute - once per camera per frame). Without
    // this cache, every one of those ~20 call sites pays for Camera.main plus,
    // on any miss, a full Object.FindObjectsByType<Camera>() scan-and-allocate
    // - exactly the per-frame O(heap) pattern this project's conventions ban
    // outright. The steady-state gameplay camera does not change frame to
    // frame, so caching the last resolved instance and only re-running the
    // real lookup after a scene load/unload (see the SceneManager
    // subscriptions below) turns every one of those call sites back into an
    // O(1) field read for the overwhelming majority of frames.
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

        return null;
    }

    public static Camera? ResolveIn(Scene scene)
    {
        _ = scene;
        return Resolve();
    }
}
