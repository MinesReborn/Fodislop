#nullable enable

using System;
using System.IO;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Effekseer;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player.Logic;
using Fodinae.UI;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using VContainer;

namespace Fodinae
{
    public class DiagnosticRunner : MonoBehaviour
    {
        // Диагностика пишется в persistentDataPath (Application.dataPath/.. — каталог
        // установки: на Windows в Program Files это UnauthorizedAccessException каждые
        // 5 секунд). Файлы живут только в dev-сборках/редакторе.
        private static string LogPath =>
            Path.Combine(Application.persistentDataPath, "diagnostic.txt");
        private static string MemoryLogPath =>
            Path.Combine(Application.persistentDataPath, "memory_growth.txt");
        private static readonly object MemoryLogWriteLock = new();
        private float _nextMemorySampleTime;
        private Camera? _mainCamera;

        private MapStorage _mapStorage = null!;
        private LightingEngine _lighting = null!;
        private INetworkService _networkService = null!;
        private IConnectionService _connection = null!;
        private IMapDataProvider _mapDataProvider = null!;
        private IAssetLoader _assetLoader = null!;
        private IInputBlocker _inputBlocker = null!;
        private IRobotService _robotService = null!;
        private MapManager _mapManager = null!;
        private GameManager _gameManager = null!;
        private RobotManager _robotManager = null!;
        private BuildingManager _buildingManager = null!;
        private PacketHandler _packetHandler = null!;
        private TerrainRenderer _terrain = null!;
        private ILocalPlayerState _localPlayer = null!;
        private IGameplayCamera _gameplayCamera = null!;
        private IFrameTelemetry _telemetry = null!;
        private bool _dependenciesInjected;

        [Inject]
        private void Construct(
            MapStorage mapStorage,
            LightingEngine lighting,
            INetworkService networkService,
            IConnectionService connection,
            IMapDataProvider mapDataProvider,
            IAssetLoader assetLoader,
            IInputBlocker inputBlocker,
            IRobotService robotService,
            MapManager mapManager,
            GameManager gameManager,
            RobotManager robotManager,
            BuildingManager buildingManager,
            PacketHandler packetHandler,
            TerrainRenderer terrain,
            ILocalPlayerState localPlayer,
            IGameplayCamera gameplayCamera,
            IFrameTelemetry telemetry)
        {
            _mapStorage = mapStorage;
            _lighting = lighting;
            _networkService = networkService;
            _connection = connection;
            _mapDataProvider = mapDataProvider;
            _assetLoader = assetLoader;
            _inputBlocker = inputBlocker;
            _robotService = robotService;
            _mapManager = mapManager;
            _gameManager = gameManager;
            _robotManager = robotManager;
            _buildingManager = buildingManager;
            _packetHandler = packetHandler;
            _terrain = terrain;
            _localPlayer = localPlayer;
            _gameplayCamera = gameplayCamera;
            _telemetry = telemetry;
            _dependenciesInjected = true;
        }

        protected void Awake()
        {
            _mainCamera = _gameplayCamera?.Camera;
        }

        protected void Update()
        {
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            if (!_dependenciesInjected)
            {
                return;
            }

            if (Time.unscaledTime >= _nextMemorySampleTime)
            {
                _nextMemorySampleTime = Time.unscaledTime + 5f;
                WriteMemorySample();
            }

            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            {
                WriteSnapshot();
            }
#endif
        }

        private void WriteMemorySample()
        {
            MapStorage ms = _mapStorage;
            LightingEngine lighting = _lighting;
            string line =
                $"t={Time.unscaledTime:F1}s frame={Time.frameCount} " +
                $"allocated={Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f):F1}MB " +
                $"reserved={Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f):F1}MB " +
                $"graphics={Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f):F1}MB " +
                $"mono={Profiler.GetMonoUsedSizeLong() / (1024f * 1024f):F1}MB " +
                $"gc={System.GC.GetTotalMemory(false) / (1024f * 1024f):F1}MB " +
                $"allocRate={_telemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f):F2}MB/s " +
                $"collections={_telemetry.GcCollectionCount} " +
                $"runtimeEffects={RuntimeEffekseerLoader.ActiveRuntimeEffectCount} " +
                $"chunks={ms.CellLayer?.GetLoadedCount() ?? 0} " +
                $"lightingSolves={lighting.SolveCount} " +
                $"dynamicLights={lighting.DynamicLightCount} " +
                $"dynamicUploaded={lighting.UploadedDynamicLightCount} " +
                $"dynamicDropped={lighting.DroppedDynamicLightCount} " +
                $"lightingField={lighting.FieldWidth}x{lighting.FieldHeight} " +
                $"lightingAtlas={lighting.AtlasEntryCount}\n";

            // Off the main thread. File.AppendAllText opens, writes and closes
            // the file synchronously; on a five-second timer that is a periodic
            // main-thread stall in exactly the builds this component runs in -
            // the editor and development builds, which is where anyone is
            // looking at a frame graph. The line is already fully built, so
            // nothing Unity-thread-affine crosses over.
            string sampleLine = line;
            string logPath = MemoryLogPath;
            System.Threading.Tasks.Task.Run(() =>
            {
                lock (MemoryLogWriteLock)
                {
                    File.AppendAllText(logPath, sampleLine);
                }
            });

        }

        private void WriteSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SNAPSHOT frame={Time.frameCount} time={Time.time:F2}s ===");

            sb.AppendLine("\n[MEMORY]");
            sb.AppendLine($"  TotalAllocated={Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  TotalReserved={Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  GraphicsDriver={Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  MonoUsed={Profiler.GetMonoUsedSizeLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  MonoHeap={Profiler.GetMonoHeapSizeLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  GCHeap={System.GC.GetTotalMemory(false) / (1024f * 1024f):F1} MB");
            sb.AppendLine("  Unity resource object counts omitted; diagnostics do not scan the heap.");
            sb.AppendLine($"  ActiveRuntimeEffects={RuntimeEffekseerLoader.ActiveRuntimeEffectCount}");

            sb.AppendLine("\n[SERVICES]");
            W(sb, "IWorldDataStorage", _mapStorage);
            W(sb, "INetworkService", _networkService);
            W(sb, "IConnectionService", _connection);
            W(sb, "IMapDataProvider", _mapDataProvider);
            W(sb, "IAssetLoader", _assetLoader);
            W(sb, "IInputBlocker", _inputBlocker);
            W(sb, "IRobotService", _robotService);
            W(sb, "MapManager", _mapManager);
            W(sb, "GameManager", _gameManager);
            W(sb, "RobotManager", _robotManager);
            W(sb, "BuildingManager", _buildingManager);
            W(sb, "PacketHandler", _packetHandler);

            sb.AppendLine("\n[MAP]");
            MapStorage ms = _mapStorage;
            if (ms != null)
            {
                sb.AppendLine(
                    $"  Ready={ms.IsReady} Disposed={ms.IsDisposed} Hash={ms.GetHashCode()}");
                if (ms.CellLayer != null)
                {
                    sb.AppendLine($"  CellChunks loaded={ms.CellLayer.GetLoadedCount()} dirty={ms.CellLayer.GetDirtyCount()} max={ms.CellLayer.MaxChunksInMemory}");
                }
            }
            else
            {
                sb.AppendLine("  NULL (not in world scene)");
            }

            MapManager mm = _mapManager;
            sb.AppendLine(
                $"  Initialized={mm.IsWorldInitialized} '{mm.WorldCodeName}' {mm.WorldWidth}x{mm.WorldHeight} Hash={mm.GetHashCode()}");

            sb.AppendLine("\n[PLAYER]");
            var p = _localPlayer.Current;
            if (p == null)
            {
                sb.AppendLine("  NULL");
            }
            else
            {
                var go = p.gameObject;
                sb.AppendLine($"  BotId={p.BotId} Pos={p.Position}");
                sb.AppendLine($"  GO={go.name} activeInHierarchy={go.activeInHierarchy} isActiveAndEnabled={p.isActiveAndEnabled}");
                sb.AppendLine($"  Transform local={go.transform.localPosition} world={go.transform.position}");
                sb.AppendLine($"  GO.layer={go.layer} GO.tag={go.tag}");
                var rb = go.GetComponent<Rigidbody2D>();
                sb.AppendLine($"  Rigidbody2D: {(rb != null ? $"bodyType={rb.bodyType} simulating={rb.simulated}" : "NONE")}");
            }

            sb.AppendLine("\n[INPUT]");
            sb.AppendLine($"  IInputBlocker: IsInputBlocked={_inputBlocker.IsInputBlocked}");
            sb.AppendLine($"  Keyboard.current: {(Keyboard.current != null ? "OK" : "NULL")}");

            sb.AppendLine("\n[GAME]");
            sb.AppendLine($"  State={_gameManager.CurrentState} Authorized={_gameManager.IsUIAuthorized}");

            sb.AppendLine("\n[TERRAIN]");
            TerrainRenderer terrain = _terrain;
            if (terrain != null)
            {
                sb.AppendLine($"  activeInHierarchy={terrain.gameObject.activeInHierarchy} enabled={terrain.enabled}");
                var mf = terrain.GetComponent<MeshFilter>();
                sb.AppendLine($"  MeshFilter: {(mf != null && mf.sharedMesh != null ? $"verts={mf.sharedMesh.vertexCount}" : "NO MESH")}");
                var mr = terrain.GetComponent<MeshRenderer>();
                sb.AppendLine($"  MeshRenderer: {(mr != null ? $"enabled={mr.enabled} materials={mr.sharedMaterials.Length} sortingOrder={mr.sortingOrder}" : "NONE")}");
            }
            else
            {
                sb.AppendLine("  NOT FOUND");
            }

            sb.AppendLine("\n[CAMERA]");
            var cam = _mainCamera;
            sb.AppendLine(cam != null
                ? $"  pos={cam.transform.position} ortho={cam.orthographic} size={cam.orthographicSize} active={cam.gameObject.activeInHierarchy}"
                : "  NULL");

            sb.AppendLine("\n[ENTITIES]");
            foreach (var r in FindObjectsByType<Robot>(FindObjectsInactive.Exclude))
            {
                var rgo = r.gameObject;
                sb.AppendLine($"  #{r.BotId} local={r.IsLocalPlayer} GO={rgo.name} active={rgo.activeInHierarchy} pos={r.transform.position}");
            }

            foreach (var pk in FindObjectsByType<Fodinae.Game.Building>(FindObjectsInactive.Exclude))
            {
                sb.AppendLine($"  Building {pk.name} pos={pk.transform.position}");
            }

            sb.AppendLine("\n[TIME]");
            sb.AppendLine($"  timeScale={Time.timeScale} deltaTime={Time.deltaTime:F4} frame={Time.frameCount}");

            sb.AppendLine("=== END ===\n");
            File.WriteAllText(LogPath, sb.ToString());
            Debug.Log($"[Diagnostic] Snapshot -> {LogPath}");
        }

        private static void W(StringBuilder sb, string name, object? obj)
        {
            sb.AppendLine(obj != null
                ? $"  {name}: OK [{obj.GetType().Name} #{obj.GetHashCode()}]"
                : $"  {name}: NULL");
        }
    }
}
