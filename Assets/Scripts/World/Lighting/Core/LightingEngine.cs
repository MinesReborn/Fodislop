#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Pipeline;
using Fodinae.World.Lighting.Pipeline.Stages;
using Fodinae.World.Lighting.Quality;
using Fodinae.World.Terrain;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace Fodinae.World.Lighting
{
    [DisallowMultipleComponent]
    public class LightingEngine : MonoBehaviour
    {
        public enum DebugView
        {
            FinalLighting,
            Occupancy,
            Albedo,
            Emission,
            Transmission,
            DirectRadiance,
            DiffuseBounce,
        }

        private const int DynamicLightStride = sizeof(float) * 8;
        private const int RadianceStride = sizeof(uint) * 3;
        private const int MaximumDispatchGroupsPerDimension = 65535;
        private const string WorldLightingKeyword = "FODINAE_WORLD_LIGHTING";
        public const float DynamicLightInfluenceRadiusCells = 32f;

        private static readonly int MaterialFieldId = Shader.PropertyToID("_MaterialField");
        private static readonly int EmissionFieldId = Shader.PropertyToID("_EmissionField");
        private static readonly int AutomaticNormalInputId =
            Shader.PropertyToID("_AutomaticNormalInput");
        // _DynamicLightCount is gone from the compute shader along with its light
        // loop; the count now only decides how many instances the emission pass
        // draws, which DrawProcedural takes directly.
        private static readonly int RadianceAtlasId = Shader.PropertyToID("_RadianceAtlas");
        private static readonly int DirectTextureId = Shader.PropertyToID("_DirectTexture");
        private static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
        private static readonly int StaticDirectInputId =
            Shader.PropertyToID("_StaticDirectInput");
        private static readonly int BounceTextureId = Shader.PropertyToID("_BounceTexture");
        private static readonly int BounceInputId = Shader.PropertyToID("_BounceInput");
        private static readonly int ResultId = Shader.PropertyToID("_Result");
        private static readonly int FieldSizeId = Shader.PropertyToID("_FieldSize");
        private static readonly int BounceSizeId = Shader.PropertyToID("_BounceSize");
        private static readonly int WorldRectId = Shader.PropertyToID("_WorldRect");
        private static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");
        private static readonly int EmptyExtinctionRgbId = Shader.PropertyToID("_EmptyExtinctionRgb");
        private static readonly int SolidExtinctionRgbId = Shader.PropertyToID("_SolidExtinctionRgb");
        private static readonly int MinimumTransmissionId = Shader.PropertyToID("_MinimumTransmission");
        private static readonly int BounceStrengthId = Shader.PropertyToID("_BounceStrength");
        private static readonly int EmissionScaleId = Shader.PropertyToID("_EmissionScale");
        private static readonly int MaximumLightMultiplierId =
            Shader.PropertyToID("_MaximumLightMultiplier");
        private static readonly int EnableFinalLightingClampId =
            Shader.PropertyToID("_EnableFinalLightingClamp");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int TransmittanceDebugDistanceCellsId =
            Shader.PropertyToID("_TransmittanceDebugDistanceCells");
        private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
        private static readonly int MaterialYFlipId = Shader.PropertyToID("_MaterialYFlip");
        private static readonly int MaximumIntervalStepsId =
            Shader.PropertyToID("_MaximumIntervalSteps");
        private static readonly int EnableDiffuseBounceId =
            Shader.PropertyToID("_EnableDiffuseBounce");
        private static readonly int CascadeOffsetId = Shader.PropertyToID("_CascadeOffset");
        private static readonly int CascadeProbeSizeId = Shader.PropertyToID("_CascadeProbeSize");
        private static readonly int CascadeProbeSpacingId = Shader.PropertyToID("_CascadeProbeSpacing");
        private static readonly int CascadeDirectionCountId = Shader.PropertyToID("_CascadeDirectionCount");
        private static readonly int CascadeIntervalId = Shader.PropertyToID("_CascadeInterval");
        private static readonly int FarCascadeIntervalId = Shader.PropertyToID("_FarCascadeInterval");
        private static readonly int FarCascadeOffsetId = Shader.PropertyToID("_FarCascadeOffset");
        private static readonly int FarCascadeProbeSizeId = Shader.PropertyToID("_FarCascadeProbeSize");
        private static readonly int FarCascadeProbeSpacingId = Shader.PropertyToID("_FarCascadeProbeSpacing");
        private static readonly int FarCascadeDirectionCountId = Shader.PropertyToID("_FarCascadeDirectionCount");
        private static readonly int HasFarCascadeId = Shader.PropertyToID("_HasFarCascade");
        private static readonly int EnableBilinearFixId = Shader.PropertyToID("_EnableBilinearFix");
        private static readonly int CascadeEntryCountId = Shader.PropertyToID("_CascadeEntryCount");
        private static readonly int CascadeDispatchRowWidthId =
            Shader.PropertyToID("_CascadeDispatchRowWidth");
        private static readonly int WorldLightTextureId = Shader.PropertyToID("_WorldLightTexture");
        private static readonly int WorldLightRectId = Shader.PropertyToID("_WorldLightRect");
        private static readonly int WorldLightDebugViewId =
            Shader.PropertyToID("_WorldLightDebugView");
        private static readonly int WorldLightTextureSizeId =
            Shader.PropertyToID("_WorldLightTextureSize");
        private static readonly int WorldEmissionScaleId =
            Shader.PropertyToID("_WorldEmissionScale");
        private static readonly int BlockAveragedId = Shader.PropertyToID("_BlockAveraged");
        private static readonly ProfilerMarker LightingUpdateMarker =
            new("Fodinae.Lighting.UpdateLighting.CPU");
        private static readonly ProfilerMarker BuildCommandsMarker =
            new("Fodinae.Lighting.BuildCommands.CPU");
        private static readonly ProfilerMarker ExecuteCommandsMarker =
            new("Fodinae.Lighting.ExecuteCommands.CPU");
        private static readonly ProfilerMarker DynamicUploadMarker =
            new("Fodinae.Lighting.DynamicLights.Upload.CPU");
        private static readonly ProfilerMarker CascadeMarker =
            new("Fodinae.Lighting.Cascades.Record.CPU");
        private static readonly ProfilerMarker ResolveMarker =
            new("Fodinae.Lighting.Resolve.Record.CPU");
        private static readonly ProfilerMarker CompositeMarker =
            new("Fodinae.Lighting.Composite.Record.CPU");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Shader.DisableKeyword(WorldLightingKeyword);
        }

        [Header("Quality")]

        // Quality is selected by ClientConfig.GraphicsPreset at runtime.
        private GraphicsPreset _graphicsPreset;
        private LightingQualityMode _lightingQualityMode = LightingQualityMode.PerBlock;
        private TerrainRenderer? _activeTerrainRenderer;

        private LightingConfigHolder _configHolder = null!;

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Debug view для проверки отдельных lighting-слоёв без скрытого AO/exposure влияния.")]
        private DebugView _debugView;

        private readonly LightingResourceManager _resources = new();
        private readonly DynamicLightManager _dynamicLightManager = new();
        private GraphicsQualitySettings _qualitySettings;

        private List<CascadeLayout> _cascades => _resources.Cascades;
        private ComputeShader? _lightingCompute => _resources.LightingCompute;
        private ComputeBuffer? _dynamicLightBuffer => _resources.DynamicLightBuffer;
        private ComputeBuffer? _radianceAtlas => _resources.RadianceAtlas;
        private CommandBuffer? _lightingCommandBuffer => _resources.LightingCommandBuffer;
        private RenderTexture? _materialField => _resources.MaterialField;
        private RenderTexture? _staticEmissionField => _resources.StaticEmissionField;
        private RenderTexture? _dynamicEmissionField => _resources.DynamicEmissionField;
        private Material? _dynamicEmissionMaterial => _resources.DynamicEmissionMaterial;
        private RenderTexture? _automaticNormalField => _resources.AutomaticNormalField;
        private RenderTexture? _directTexture => _resources.DirectTexture;
        private RenderTexture? _staticDirectTexture => _resources.StaticDirectTexture;
        private RenderTexture? _bounceTexture => _resources.BounceTexture;
        private RenderTexture? _lightmapTexture => _resources.LightmapTexture;
        private int _solveCascadeKernel => _resources.SolveCascadeKernel;
        private int _solveAutomaticNormalsKernel => _resources.SolveAutomaticNormalsKernel;
        private int _resolveDirectKernel => _resources.ResolveDirectKernel;
        private int _solveDiffuseBounceKernel => _resources.SolveDiffuseBounceKernel;
        private int _compositeLightingKernel => _resources.CompositeLightingKernel;
        private int _fieldWidth => _resources.FieldWidth;
        private int _fieldHeight => _resources.FieldHeight;
        private int _bounceWidth => _resources.BounceWidth;
        private int _bounceHeight => _resources.BounceHeight;
        private int _atlasCapacity => _resources.AtlasCapacity;
        private int _atlasEntryCount => _resources.AtlasEntryCount;
        private LightingPipeline? _compositePipeline => _resources.CompositePipeline;
        private LightingPipeline? _automaticNormalsPipeline => _resources.AutomaticNormalsPipeline;
        private LightingPipeline? _diffuseBouncePipeline => _resources.DiffuseBouncePipeline;
        private LightingPipeline? _dynamicEmissionCompositionPipeline => _resources.DynamicEmissionCompositionPipeline;
        private LightingPipeline? _materialFieldPipeline => _resources.MaterialFieldPipeline;
        private bool _gpuPipelineInitialized => _resources.GpuPipelineInitialized;

        private float _requestedPixelsPerCell;
        private float _effectivePixelsPerCell;
        private bool _textureDimensionLimited;
        private bool _cascadeBudgetLimited;
        private bool _fieldDirty = true;
        private bool _compositeDirty = true;
        private bool _bounceDirty = true;


        private float _nextLightingUpdateTime;
        private float _nextDynamicLightingUpdateTime;
        private ulong _solveCount;
        private ulong _lastTerrainGeometryRevision;
        private ulong _lastContributorGeometryRevision;
        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        private Vector4 _lastVisibleRegion = new(float.NaN, float.NaN, float.NaN, float.NaN);

        private bool _hasRenderedLightState;
        private bool _initialized;
        private bool _lightingDisabledStatePublished;

        /// <summary>
        /// True once EnsureInitialized has completed. Runtime lighting getters (and UI built
        /// on top of them) must not be touched before this flag is set — _runtimeConfig is
        /// only created during initialization.
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Одна детерминированная точка готовности освещения: срабатывает один раз
        /// после завершения <see cref="EnsureInitialized"/>. Вьюхи, которым нужен
        /// runtime-конфиг (PauseMenu), строятся по этому событию, а не ретраем из Update.
        /// </summary>
        public event Action? OnInitialized;
        private bool _hasStaticRadianceState;
        private bool _hasDynamicRadianceState;
        private bool _dynamicSolveInProgress;

        public bool BypassLightingCompute
        {
            get => _debugSettings.BypassLightingCompute;
            set => _debugSettings.BypassLightingCompute = value;
        }

        public GraphicsPreset ActiveGraphicsPreset => _graphicsPreset;

        public LightingQualityMode ActiveLightingQuality => _lightingQualityMode;

        public DebugView ActiveDebugView => _debugView;

        public bool DiffuseBounceEnabled => _configHolder.DiffuseBounceEnabled;

        public float AmbientIntensity => _configHolder.AmbientIntensity;

        public Color AmbientColor => _configHolder.AmbientColor;

        public float EmissionScale => _configHolder.EmissionScale;

        public Color EmptyExtinctionRgb => _configHolder.EmptyExtinctionRgb;

        public Color SolidExtinctionRgb => _configHolder.SolidExtinctionRgb;

        public float EmptyExtinctionMultiplier => _configHolder.EmptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => _configHolder.SolidExtinctionMultiplier;

        public float BounceStrength => _configHolder.BounceStrength;

        public float MaximumLightMultiplier => _configHolder.MaximumLightMultiplier;

        public float TransmittanceDebugDistanceCells => _configHolder.TransmittanceDebugDistanceCells;

        public float MinimumTransmission => _configHolder.MinimumTransmission;

        public bool EnableFinalLightingClamp => _configHolder.EnableFinalLightingClamp;

        public float DynamicLightIntensity => _configHolder.DynamicLightIntensity;

        public Color DynamicLightColor => _configHolder.DynamicLightColor;

        public float DynamicLightUpdatesPerSecond => _configHolder.DynamicLightUpdatesPerSecond;

        public bool IsRuntimeConfigReady => _configHolder != null;

        public string RuntimeConfigFilePath => _configHolder.ConfigFilePath;

        public int LightSafeBorder => _configHolder.LightSafeBorder;

        public int DynamicLightCount => _dynamicLightManager.Count;

        public uint DynamicLightGeneration => _dynamicLightManager.Generation;

        public int UploadedDynamicLightCount => _dynamicLightManager.UploadedCount;

        public int DroppedDynamicLightCount => _dynamicLightManager.DroppedCount;

        public IReadOnlyList<int> DroppedDynamicLightIds => _dynamicLightManager.DroppedLightIds;

        public ulong SolveCount => _solveCount;

        public int FieldWidth => _fieldWidth;

        public int FieldHeight => _fieldHeight;

        public float RequestedPixelsPerCell => _requestedPixelsPerCell;

        public float EffectivePixelsPerCell => _effectivePixelsPerCell;

        public bool TextureDimensionLimited => _textureDimensionLimited;

        public bool CascadeBudgetLimited => _cascadeBudgetLimited;

        public int BounceWidth => _bounceWidth;

        public int BounceHeight => _bounceHeight;

        public int CascadeCount => _cascades.Count;

        public int MaximumIntervalSteps =>
            Mathf.Clamp(_qualitySettings.LightingMaximumRaySteps, 1, 64);

        /// <summary>
        /// Per-cascade cost of one full radiance solve, in the units that
        /// actually decide how long the GPU spends on it.
        /// </summary>
        /// <remarks>
        /// Entry count alone is misleading: every cascade in this layout holds
        /// roughly the same number of entries (probe count divides by four while
        /// the direction count multiplies by four), so the atlas looks evenly
        /// balanced. The march does not. <c>SolveCascade</c> derives its step
        /// count from the interval length, and the interval quadruples per
        /// <summary>
        /// Rays, ray-march steps and far-cascade atlas taps one full solve
        /// issues. Mirrors the arithmetic in <c>WorldLighting.compute</c>.
        /// </summary>
        public void CollectCascadeCosts(List<CascadeCostSample> destination)
        {
            CascadeCostCalculator.CollectCascadeCosts(_cascades, MaximumIntervalSteps, destination);
        }

        /// <summary>
        /// Entries the configured atlas limit allows. The field resolution is
        /// fitted down to this, so it — not pixels-per-cell — is what caps
        /// lighting resolution once the requested density exceeds it.
        /// </summary>
        public long CascadeAtlasBudgetEntries =>
            (long)_qualitySettings.LightingCascadeAtlasLimit *
            _qualitySettings.LightingCascadeAtlasLimit * 4;

        public int MaterialYFlip => SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

        public float CellSize => ProjectRuntimeContracts.World.CellSize;

        public Vector4 WorldRect => new(
            _lastVisibleRegion.x * ProjectRuntimeContracts.World.CellSize,
            _lastVisibleRegion.y * ProjectRuntimeContracts.World.CellSize,
            _lastVisibleRegion.z * ProjectRuntimeContracts.World.CellSize,
            _lastVisibleRegion.w * ProjectRuntimeContracts.World.CellSize);

        public IReadOnlyList<string> GetCascadeUniformSummaries()
        {
            var summaries = new List<string>(_cascades.Count);
            for (int index = 0; index < _cascades.Count; index++)
            {
                CascadeLayout cascade = _cascades[index];
                summaries.Add(
                    $"Cascade {index}: offset={cascade.Offset}, entries={cascade.EntryCount}, " +
                    $"probe={cascade.ProbeWidth}x{cascade.ProbeHeight}, spacing={cascade.ProbeSpacing}, " +
                    $"directions={cascade.DirectionCount}, interval={cascade.IntervalStart:F2}..{cascade.IntervalEnd:F2}");
            }

            return summaries;
        }

        public int AtlasEntryCount => _atlasEntryCount;

        public Color ComputeAmbientColor => _configHolder.AmbientColor * _configHolder.AmbientIntensity;

        public Color ComputeEmptyExtinction =>
            _configHolder.EmptyExtinctionRgb * _configHolder.EmptyExtinctionMultiplier;

        public Color ComputeSolidExtinction =>
            _configHolder.SolidExtinctionRgb * _configHolder.SolidExtinctionMultiplier;

        public int StableRegionPaddingCells => LightingRegionCalculator.LightingRegionPaddingCells;

        public int RequiredTerrainPadding
        {
            get
            {
                // Dynamic sources are rasterized as one-cell emitters. Their
                // propagation distance is solved by the same extinction and
                // cascade intervals as terrain emission, not by a source halo.
                return Mathf.Max(1, 1 + _configHolder.LightSafeBorder);
            }
        }

        private void Awake()
        {
        }

        private void Start()
        {
            // Scene instances run Start before GameBootstrap injects them. The
            // explicit PostStart resolution below performs the authoritative
            // initialization; do not throw every frame while that hand-off is
            // still pending.
            if (DependenciesReady)
            {
                TryInitialize();
            }
        }

        private bool DependenciesReady =>
            _clientConfig?.Config != null &&
            _lightingGeometryRegistry != null;

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (!DependenciesReady)
            {
                throw new InvalidOperationException(
                    "LightingEngine requires all DI dependencies before initialization.");
            }

            _configHolder = new LightingConfigHolder(_clientConfig);
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);

            _initialized = true;
            OnInitialized?.Invoke();

            if (_lightingQualityMode == LightingQualityMode.Off)
            {
                DisableGpuLighting();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            EnsureInitialized();
        }

        private void OnDestroy()
        {

            ReleaseGpuPipeline();
            Shader.DisableKeyword(WorldLightingKeyword);
        }

        private void Update()
        {
            if (!_initialized)
            {
                if (DependenciesReady)
                {
                    TryInitialize();
                }

                return;
            }

        }

        private void OnApplicationQuit()
        {
        }

        public void SetDynamicLight(
            int id,
            Vector2 position,
            Color color,
            float intensity)
        {
            _dynamicLightManager.SetDynamicLight(id, position, color, intensity, _effectivePixelsPerCell);
        }

        public void RemoveDynamicLight(int id)
        {
            _dynamicLightManager.RemoveDynamicLight(id);
        }

        public void ClearDynamicLights()
        {
            _dynamicLightManager.ClearDynamicLights();
        }

        public void InvalidateStaticCache()
        {
            _fieldDirty = true;
        }

        public void InvalidateRegion(int worldX, int worldY, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int regionMaxX = worldX + width - 1;
            int regionMaxY = worldY + height - 1;
            if (float.IsNaN(_lastVisibleRegion.x) ||
                (regionMaxX >= _lastVisibleRegion.x - 1f &&
                worldX <= _lastVisibleRegion.x + _lastVisibleRegion.z + 1f &&
                regionMaxY >= _lastVisibleRegion.y - 1f &&
                worldY <= _lastVisibleRegion.y + _lastVisibleRegion.w + 1f))
            {
                _telemetry.LightingRegionInvalidationCount++;
                _fieldDirty = true;
            }
        }

        public void InvalidateCell(int worldX, int worldY)
        {
            InvalidateRegion(worldX, worldY, 1, 1);
        }

        public void ApplyClientConfig()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _fieldDirty = true;
            _bounceDirty = true;
            _compositeDirty = true;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
            _hasRenderedLightState = false;
            _dynamicLightManager.IncrementGeneration();
            _dynamicLightManager.MarkDirty();
            Debug.Log($"[LightingEngine] Applied client config (Preset={_clientConfig.Config.GraphicsPreset})");
        }

        public void SetDebugView(DebugView debugView)
        {
            if (_debugView == debugView)
            {
                return;
            }

            _debugView = debugView;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
            _compositeDirty = true;
            Debug.Log($"[LightingEngine] SetDebugView: {debugView}");
        }

        public void SetDiffuseBounceEnabled(bool enabled)
        {
            if (_configHolder.SetDiffuseBounceEnabled(enabled))
            {
                _bounceDirty = true;
                _compositeDirty = true;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
                Debug.Log($"[LightingEngine] SetDiffuseBounceEnabled: {enabled}");
            }
        }

        public void SetAmbientIntensity(float value)
        {
            if (_configHolder.SetAmbientIntensity(value))
            {
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetAmbientIntensity: {value}");
            }
        }

        public void SetAmbientColor(Color value)
        {
            if (_configHolder.SetAmbientColor(value))
            {
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetAmbientColor: {value}");
            }
        }

        public void SetEmissionScale(float value)
        {
            if (_configHolder.SetEmissionScale(value))
            {
                _fieldDirty = true;
                _hasStaticRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetEmissionScale: {value}");
            }
        }

        public void SetEmptyExtinctionColor(Color value)
        {
            if (_configHolder.SetEmptyExtinctionColor(value))
            {
                _fieldDirty = true;
                _hasStaticRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetEmptyExtinctionColor: {value}");
            }
        }

        public void SetSolidExtinctionColor(Color value)
        {
            if (_configHolder.SetSolidExtinctionColor(value))
            {
                _fieldDirty = true;
                _hasStaticRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetSolidExtinctionColor: {value}");
            }
        }

        public void SetFinalLightingClampEnabled(bool enabled)
        {
            if (_configHolder.SetFinalLightingClampEnabled(enabled))
            {
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetFinalLightingClampEnabled: {enabled}");
            }
        }

        public void SetEmptyExtinctionMultiplier(float value)
        {
            if (_configHolder.SetEmptyExtinctionMultiplier(value))
            {
                _fieldDirty = true;
                _hasStaticRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetEmptyExtinctionMultiplier: {value}");
            }
        }

        public void SetSolidExtinctionMultiplier(float value)
        {
            if (_configHolder.SetSolidExtinctionMultiplier(value))
            {
                _fieldDirty = true;
                _hasStaticRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetSolidExtinctionMultiplier: {value}");
            }
        }

        public void SetBounceStrength(float value)
        {
            if (_configHolder.SetBounceStrength(value))
            {
                _bounceDirty = true;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetBounceStrength: {value}");
            }
        }

        public void SetMaximumLightMultiplier(float value)
        {
            if (_configHolder.SetMaximumLightMultiplier(value))
            {
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetMaximumLightMultiplier: {value}");
            }
        }

        public void SetTransmittanceDebugDistance(float value)
        {
            if (_configHolder.SetTransmittanceDebugDistance(value))
            {
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetTransmittanceDebugDistance: {value}");
            }
        }

        public void SetMinimumTransmission(float value)
        {
            if (_configHolder.SetMinimumTransmission(value))
            {
                _fieldDirty = true;
                _hasStaticRadianceState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetMinimumTransmission: {value}");
            }
        }

        public void SetLightSafeBorder(float value)
        {
            if (_configHolder.SetLightSafeBorder(value))
            {
                _fieldDirty = true;
                _hasRenderedLightState = false;
                _compositeDirty = true;
                Debug.Log($"[LightingEngine] SetLightSafeBorder: {value}");
            }
        }

        public void ResetRuntimeLightingPreferences()
        {
            _configHolder.ResetToDefaults();
            _clientConfig.Save();
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _fieldDirty = true;
            _compositeDirty = true;
            _bounceDirty = true;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }

        public void UpdateLighting(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight,
            Camera camera,
            IWorldDataStorage? storage,
            MapManager? mapManager,
            TerrainRenderer terrainRenderer)
        {
            using var lightingUpdateMarker = LightingUpdateMarker.Auto();
            if (visibleWidth <= 0 || visibleHeight <= 0 || camera == null ||
                storage == null || mapManager == null)
            {
                return;
            }

            _activeTerrainRenderer = terrainRenderer ??
                throw new ArgumentNullException(nameof(terrainRenderer));

            if (camera == null || !camera.orthographic)
            {
                return;
            }

            if (_lightingQualityMode == LightingQualityMode.Off)
            {
                PublishLightingDisabledState();
                return;
            }

            // The MUTE toggle must short-circuit before any region tracking or
            // resource allocation. GetStableLightingRegion + EnsureResources
            // run every frame even when the solve is bypassed, so crossing a
            // 32-cell region boundary used to re-allocate the entire light field
            // on the GPU (a hard hitch) while the cascade solve was muted. Keep
            // publishing the white identity texture so no other global ends up
            // stale, but do none of the per-frame field work.
            if (BypassLightingCompute)
            {
                Shader.SetGlobalTexture(WorldLightTextureId, Texture2D.whiteTexture);
                return;
            }

            EnsureGpuPipelineInitialized();

            Vector4 lightingRegion = GetStableLightingRegion(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight);


            bool regionChanged = lightingRegion != _lastVisibleRegion;
            _lastVisibleRegion = lightingRegion;

            int gridWidth = Mathf.RoundToInt(lightingRegion.z);
            int gridHeight = Mathf.RoundToInt(lightingRegion.w);
            EnsureResources(gridWidth, gridHeight, camera);

            bool dynamicLightsDirty = HasDynamicLightsChanged();
            ulong contributorGeometryRevision =
                _lightingGeometryRegistry.GeometryRevision;
            bool geometryChanged =
                _lastTerrainGeometryRevision != terrainRenderer.LightingGeometryRevision ||
                _lastContributorGeometryRevision != contributorGeometryRevision;
            if (!_fieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
                !_compositeDirty && !_bounceDirty)
            {
                return;
            }

            bool geometryUpdateRequired = _fieldDirty || regionChanged || geometryChanged;
            bool dynamicOnlyUpdate = dynamicLightsDirty &&
                !geometryUpdateRequired &&
                !_bounceDirty &&
                !_compositeDirty;
            bool continueDynamicSolve = _dynamicSolveInProgress &&
                !geometryUpdateRequired &&
                !_bounceDirty &&
                !_compositeDirty;
            float nextAllowedUpdateTime = dynamicOnlyUpdate
                ? _nextDynamicLightingUpdateTime
                : _nextLightingUpdateTime;

            if (!continueDynamicSolve &&
                Time.unscaledTime < nextAllowedUpdateTime &&
                !geometryUpdateRequired &&
                !_compositeDirty &&
                !_bounceDirty &&
                _hasStaticRadianceState)
            {
                return;
            }

            if (geometryUpdateRequired || _bounceDirty || _compositeDirty)
            {
                _dynamicSolveInProgress = false;
            }

            const float cellSize = ProjectRuntimeContracts.World.CellSize;
            Vector4 worldRect = new(
                lightingRegion.x * cellSize,
                lightingRegion.y * cellSize,
                lightingRegion.z * cellSize,
                lightingRegion.w * cellSize);
            CommandBuffer commandBuffer = _lightingCommandBuffer ??
                throw new InvalidOperationException("Radiance Cascades command buffer is not initialized.");
                commandBuffer.Clear();
                int dynamicLightCount;
                bool dynamicLightsChanged;
                try
                {
                    long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (BuildCommandsMarker.Auto())
                    {
                commandBuffer.BeginSample("Fodinae.RadianceCascades");
                bool rebuildFields = _fieldDirty || regionChanged || geometryChanged;
                if (rebuildFields)
                {
                    _materialFieldPipeline!.Record(
                        commandBuffer,
                        BuildFrameContext() with { WorldRect = worldRect });
                 }

                if (_dynamicSolveInProgress)
                {
                    dynamicLightCount = _dynamicLightManager.UploadedCount;
                    dynamicLightsChanged = false;
                }
                else
                {
                    dynamicLightCount = UploadDynamicLights(
                        commandBuffer,
                        worldRect,
                        cellSize,
                        out dynamicLightsChanged);
                }

                if (!rebuildFields && !dynamicLightsChanged &&
                    !_compositeDirty && !_bounceDirty)
                {
                    commandBuffer.EndSample("Fodinae.RadianceCascades");
                    RememberDynamicLightState();
                    return;
                }

                _dynamicEmissionCompositionPipeline!.Record(
                    commandBuffer,
                    BuildFrameContext() with
                    {
                        WorldRect = worldRect,
                        CellSize = cellSize,
                        DynamicLightCount = dynamicLightCount,
                    });

                // Bound with the static field as the default. SolveRadianceHalf
                // rebinds the cascade and resolve kernels per half; everything
                // else - bounce, the composite's emission
                // debug view - wants the terrain's emission, not the lamps'.
                ConfigureSharedComputeParameters(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    _staticEmissionField!);
                if (rebuildFields)
                {
                    DispatchAutomaticNormals(commandBuffer);
                }

                // Terrain emitters are re-solved only when the geometry they
                // depend on changes - explicitly NOT when a lamp moves. That
                // dependency was the whole reason walking cost a full solve per
                // frame; the split below is what removes it.
                bool staticRadianceChanged = rebuildFields || !_hasStaticRadianceState;

                if (staticRadianceChanged)
                {
                    _telemetry.LightingStaticSolveCount++;
                    SolveRadianceHalf(
                        commandBuffer,
                        _staticEmissionField!,
                        _staticDirectTexture!,
                        "Fodinae.Lighting.StaticRadiance");
                    _hasStaticRadianceState = true;
                }

                bool dynamicRadianceNeeded = dynamicLightCount > 0 &&
                    (dynamicLightsChanged || staticRadianceChanged || !_hasDynamicRadianceState);

                if (dynamicRadianceNeeded)
                {
                    _telemetry.LightingDynamicSolveCount++;
                    SolveRadianceHalf(
                        commandBuffer,
                        _dynamicEmissionField!,
                        _directTexture!,
                        "Fodinae.Lighting.DynamicRadiance",
                        maxCascades: Mathf.Min(3, _cascades.Count));
                    _hasDynamicRadianceState = true;
                }
                else if (dynamicLightCount == 0 && (dynamicLightsChanged || staticRadianceChanged || _hasDynamicRadianceState))
                {
                    ClearDynamicDirect(commandBuffer);
                    _hasDynamicRadianceState = false;
                }

                // Diffuse bounce: direct radiance in _directTexture is scattered
                // by surface albedo into the receiver hemisphere (SolveDiffuseBounce),
                // then CompositeLighting adds it to ambient + direct.
                if (_configHolder.DiffuseBounceEnabled && _configHolder.BounceStrength > 0f)
                {
                    _diffuseBouncePipeline!.Record(commandBuffer, BuildFrameContext());
                }

                // Final composite of ambient + direct radiance + diffuse bounce.
                DispatchComposite(commandBuffer);

                commandBuffer.EndSample("Fodinae.RadianceCascades");
                }
                _telemetry.LightingBuildCommandsTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - buildStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _telemetry.LightingCommandBufferBytes = commandBuffer.sizeInBytes;
                _telemetry.ActiveDynamicLights = dynamicLightCount;
                long executeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                using (ExecuteCommandsMarker.Auto())
                {
                    Graphics.ExecuteCommandBuffer(commandBuffer);
                }
                _telemetry.LightingExecuteCommandsTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - executeStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                PublishLightingGlobals();
                _solveCount++;

                _fieldDirty = false;
                _compositeDirty = false;
                _bounceDirty = false;
                _nextLightingUpdateTime = Time.unscaledTime +
                    (1f / Mathf.Max(_qualitySettings.LightingUpdatesPerSecond, 1f));
                _nextDynamicLightingUpdateTime = Time.unscaledTime +
                    (1f / Mathf.Max(_configHolder.DynamicLightUpdatesPerSecond, 1f));
                _lastTerrainGeometryRevision = terrainRenderer.LightingGeometryRevision;
                _lastContributorGeometryRevision = contributorGeometryRevision;
                RememberDynamicLightState();
            }
            finally
            {
                commandBuffer.Clear();
            }
        }

        /// <summary>
        /// Publishes the explicit identity state selected by
        /// <see cref="LightingQualityMode.Off"/>. This is not an alternate
        /// lighting implementation: the terrain shader keyword is disabled,
        /// so the compiled fragment variant returns unit light without a
        /// texture lookup.
        /// </summary>
        private void PublishLightingDisabledState()
        {
            if (_lightingDisabledStatePublished)
            {
                return;
            }

            Shader.DisableKeyword(WorldLightingKeyword);
            Shader.SetGlobalTexture(WorldLightTextureId, Texture2D.whiteTexture);
            Shader.SetGlobalVector(WorldLightRectId, new Vector4(-1000f, -1000f, 2000f, 2000f));
            Shader.SetGlobalVector(WorldLightTextureSizeId, new Vector4(1, 1, 1, 1));
            Shader.SetGlobalInteger(WorldLightDebugViewId, 0);
            Shader.SetGlobalFloat(WorldEmissionScaleId, _configHolder.EmissionScale);
            _lightingDisabledStatePublished = true;
        }

        private void PublishLightingGlobals()
        {
            if (_lightmapTexture == null || float.IsNaN(_lastVisibleRegion.x))
            {
                throw new InvalidOperationException(
                    "Enabled world lighting cannot publish before its lightmap and region exist.");
            }

            const float cellSize = ProjectRuntimeContracts.World.CellSize;
            Shader.EnableKeyword(WorldLightingKeyword);
            _lightingDisabledStatePublished = false;
            Shader.SetGlobalTexture(WorldLightTextureId, _lightmapTexture);
            Shader.SetGlobalInteger(WorldLightDebugViewId, (int)_debugView);
            Shader.SetGlobalFloat(WorldEmissionScaleId, _configHolder.EmissionScale);
            Shader.SetGlobalVector(
                WorldLightTextureSizeId,
                new Vector4(
                    _lightmapTexture.width,
                    _lightmapTexture.height,
                    1f / _lightmapTexture.width,
                    1f / _lightmapTexture.height));
            Shader.SetGlobalVector(
                WorldLightRectId,
                new Vector4(
                    _lastVisibleRegion.x * cellSize,
                    _lastVisibleRegion.y * cellSize,
                    _lastVisibleRegion.z * cellSize,
                    _lastVisibleRegion.w * cellSize));
        }

        private void ConfigureSharedComputeParameters(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize,
            RenderTexture emissionField)
        {
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeIntParams(compute, FieldSizeId, _fieldWidth, _fieldHeight);
            commandBuffer.SetComputeIntParams(compute, BounceSizeId, _bounceWidth, _bounceHeight);
            commandBuffer.SetComputeVectorParam(compute, WorldRectId, worldRect);
            commandBuffer.SetComputeVectorParam(
                compute,
                AmbientColorId,
                _configHolder.AmbientColor * _configHolder.AmbientIntensity);
            commandBuffer.SetComputeVectorParam(
                compute,
                EmptyExtinctionRgbId,
                _configHolder.EmptyExtinctionRgb * _configHolder.EmptyExtinctionMultiplier);
            commandBuffer.SetComputeVectorParam(
                compute,
                SolidExtinctionRgbId,
                _configHolder.SolidExtinctionRgb * _configHolder.SolidExtinctionMultiplier);
            commandBuffer.SetComputeFloatParam(compute, MinimumTransmissionId, _configHolder.MinimumTransmission);
            commandBuffer.SetComputeFloatParam(
                compute,
                BounceStrengthId,
                _configHolder.BounceStrength);
            commandBuffer.SetComputeFloatParam(compute, EmissionScaleId, _configHolder.EmissionScale);
            commandBuffer.SetComputeFloatParam(
                compute,
                MaximumLightMultiplierId,
                _configHolder.MaximumLightMultiplier);
            commandBuffer.SetComputeIntParam(
                compute,
                EnableFinalLightingClampId,
                _configHolder.EnableFinalLightingClamp ? 1 : 0);
            commandBuffer.SetComputeFloatParam(compute, CellSizeId, cellSize);
            commandBuffer.SetComputeFloatParam(
                compute,
                TransmittanceDebugDistanceCellsId,
                _configHolder.TransmittanceDebugDistanceCells);
            commandBuffer.SetComputeIntParam(compute, DebugViewId, (int)_debugView);
            commandBuffer.SetComputeIntParam(
                compute,
                MaterialYFlipId,
                SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                MaximumIntervalStepsId,
                Mathf.Clamp(_qualitySettings.LightingMaximumRaySteps, 1, 64));
            commandBuffer.SetComputeIntParam(
                compute,
                EnableDiffuseBounceId,
                _configHolder.DiffuseBounceEnabled ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                BlockAveragedId,
                _lightingQualityMode == LightingQualityMode.PerBlock ? 1 : 0);
            BindFieldTextures(commandBuffer, _solveCascadeKernel, emissionField);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _solveAutomaticNormalsKernel,
                MaterialFieldId,
                _materialField!);
            BindFieldTextures(commandBuffer, _resolveDirectKernel, emissionField);
            BindFieldTextures(commandBuffer, _solveDiffuseBounceKernel, emissionField);
            BindFieldTextures(commandBuffer, _compositeLightingKernel, emissionField);
            BindAutomaticNormalInput(commandBuffer, _resolveDirectKernel);
            BindAutomaticNormalInput(commandBuffer, _solveDiffuseBounceKernel);
            // The dynamic light buffer is no longer bound to the compute shader.
            // It is consumed once per solve by ComposeEmissionField, which
            // rasterizes the sources into the emission field; the ray march then
            // reads them as ordinary emission like any other emitter.
        }

        private void BindFieldTextures(
            CommandBuffer commandBuffer,
            int kernel,
            RenderTexture emissionField)
        {
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                kernel,
                MaterialFieldId,
                _materialField!);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                kernel,
                EmissionFieldId,
                emissionField);
        }

        private void BindAutomaticNormalInput(CommandBuffer commandBuffer, int kernel)
        {
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                kernel,
                AutomaticNormalInputId,
                _automaticNormalField!);
        }

        private void DispatchAutomaticNormals(CommandBuffer commandBuffer)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.AutomaticNormals");
            _automaticNormalsPipeline!.Record(commandBuffer, BuildFrameContext());
            commandBuffer.EndSample("Fodinae.Lighting.AutomaticNormals");
        }

        /// <summary>
        /// Resources the extracted pipeline stages need this frame. Built on
        /// demand rather than cached - the underlying render textures can be
        /// reallocated by <see cref="ReleaseFieldTextures"/> between calls.
        /// </summary>
        private LightingFrameContext BuildFrameContext()
        {
            return new LightingFrameContext(
                _lightingCompute!,
                _fieldWidth,
                _fieldHeight,
                _bounceWidth,
                _bounceHeight,
                _directTexture!,
                _staticDirectTexture!,
                _bounceTexture!,
                _lightmapTexture!,
                _automaticNormalField!,
                _materialField!,
                _staticEmissionField!,
                _dynamicEmissionField!,
                _dynamicEmissionMaterial!,
                _dynamicLightBuffer,
                _activeTerrainRenderer ??
                    throw new InvalidOperationException(
                        "Radiance Cascades requires an active TerrainRenderer."),
                _lightingGeometryRegistry);
        }

        private int UploadDynamicLights(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize,
            out bool uploadedLightsChanged)
        {
            using var dynamicUploadMarker = DynamicUploadMarker.Auto();
            return _dynamicLightManager.UploadDynamicLights(
                commandBuffer,
                _dynamicLightBuffer,
                worldRect,
                cellSize,
                out uploadedLightsChanged);
        }

        private void DispatchRadianceCascades(CommandBuffer commandBuffer, int maxCascades = -1)
        {
            using var cascadeMarker = CascadeMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.RadianceCascades");
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                RadianceAtlasId,
                _radianceAtlas!);
            int cascadeCount = (maxCascades > 0 && maxCascades <= _cascades.Count)
                ? maxCascades
                : _cascades.Count;
            for (int cascadeIndex = cascadeCount - 1; cascadeIndex >= 0; cascadeIndex--)
            {
                DispatchRadianceCascade(commandBuffer, cascadeIndex);
            }

            commandBuffer.EndSample("Fodinae.Lighting.RadianceCascades");
        }

        private void DispatchRadianceCascade(
            CommandBuffer commandBuffer,
            int cascadeIndex)
        {
            string sampleName = cascadeIndex switch
            {
                3 => "Fodinae.Lighting.Cascade_3",
                2 => "Fodinae.Lighting.Cascade_2",
                1 => "Fodinae.Lighting.Cascade_1",
                _ => "Fodinae.Lighting.Cascade_0",
            };
            commandBuffer.BeginSample(sampleName);
            ComputeShader compute = _lightingCompute!;
            CascadeLayout cascade = _cascades[cascadeIndex];
            bool hasFarCascade = cascadeIndex + 1 < _cascades.Count;
            CascadeLayout farCascade = hasFarCascade
                ? _cascades[cascadeIndex + 1]
                : cascade;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                RadianceAtlasId,
                _radianceAtlas!);
            commandBuffer.SetComputeIntParam(compute, CascadeOffsetId, cascade.Offset);
            commandBuffer.SetComputeIntParams(
                compute,
                CascadeProbeSizeId,
                cascade.ProbeWidth,
                cascade.ProbeHeight);
            commandBuffer.SetComputeIntParam(
                compute,
                CascadeProbeSpacingId,
                cascade.ProbeSpacing);
            commandBuffer.SetComputeIntParam(
                compute,
                CascadeDirectionCountId,
                cascade.DirectionCount);
            commandBuffer.SetComputeVectorParam(
                compute,
                CascadeIntervalId,
                new Vector4(cascade.IntervalStart, cascade.IntervalEnd, 0f, 0f));
            commandBuffer.SetComputeIntParam(compute, FarCascadeOffsetId, farCascade.Offset);
            commandBuffer.SetComputeIntParams(
                compute,
                FarCascadeProbeSizeId,
                farCascade.ProbeWidth,
                farCascade.ProbeHeight);
            commandBuffer.SetComputeIntParam(
                compute,
                FarCascadeProbeSpacingId,
                farCascade.ProbeSpacing);
            commandBuffer.SetComputeIntParam(
                compute,
                FarCascadeDirectionCountId,
                farCascade.DirectionCount);
            commandBuffer.SetComputeVectorParam(
                compute,
                FarCascadeIntervalId,
                new Vector4(
                    farCascade.IntervalStart,
                    farCascade.IntervalEnd,
                    0f,
                    0f));
            commandBuffer.SetComputeIntParam(compute, HasFarCascadeId, hasFarCascade ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                EnableBilinearFixId,
                _lightingQualityMode == LightingQualityMode.PerPixelBilinearFix ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                CascadeEntryCountId,
                cascade.EntryCount);
            int totalGroupCount = Mathf.CeilToInt(cascade.EntryCount / 64f);
            int groupCountX = Mathf.Min(
                MaximumDispatchGroupsPerDimension,
                totalGroupCount);
            int groupCountY = Mathf.CeilToInt(totalGroupCount / (float)groupCountX);
            commandBuffer.SetComputeIntParam(
                compute,
                CascadeDispatchRowWidthId,
                groupCountX * 64);
            commandBuffer.DispatchCompute(
                compute,
                _solveCascadeKernel,
                groupCountX,
                groupCountY,
                1);
            commandBuffer.EndSample(sampleName);
        }

        /// <summary>
        /// Solves one half of the split — cascades from a single emission field,
        /// resolved into its own direct-radiance target.
        /// </summary>
        /// <remarks>
        /// Both halves share the atlas, used one after the other in the same
        /// command buffer. That is deliberate: at four pixels per cell the atlas
        /// is about 170 MB, and a second copy purely to keep the two halves
        /// apart would cost more memory than the whole rest of the lighting
        /// system. The resolve reads cascade 0 out of the atlas immediately
        /// after the solve writes it, so nothing needs to survive between the
        /// two calls.
        /// </remarks>
        private void SolveRadianceHalf(
            CommandBuffer commandBuffer,
            RenderTexture emissionField,
            RenderTexture directTarget,
            string sampleName,
            int maxCascades = -1)
        {
            commandBuffer.BeginSample(sampleName);
            BindFieldTextures(commandBuffer, _solveCascadeKernel, emissionField);
            BindFieldTextures(commandBuffer, _resolveDirectKernel, emissionField);
            DispatchRadianceCascades(commandBuffer, maxCascades);
            DispatchResolveDirect(commandBuffer, directTarget);
            commandBuffer.EndSample(sampleName);
        }

        private void DispatchResolveDirect(CommandBuffer commandBuffer, RenderTexture directTarget)
        {
            using var resolveMarker = ResolveMarker.Auto();
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeIntParam(compute, CascadeOffsetId, _cascades[0].Offset);
            commandBuffer.SetComputeBufferParam(
                compute,
                _resolveDirectKernel,
                RadianceAtlasId,
                _radianceAtlas!);
            commandBuffer.SetComputeTextureParam(
                compute,
                _resolveDirectKernel,
                DirectTextureId,
                directTarget);
            commandBuffer.DispatchCompute(
                compute,
                _resolveDirectKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
        }

        /// <summary>
        /// Zeroes the dynamic half so the composite stops adding a light that no
        /// longer exists.
        /// </summary>
        private void ClearDynamicDirect(CommandBuffer commandBuffer)
        {
            commandBuffer.SetRenderTarget(_directTexture!);
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);
        }

        private void DispatchComposite(CommandBuffer commandBuffer)
        {
            using var compositeMarker = CompositeMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.Composite");
            _compositePipeline!.Record(commandBuffer, BuildFrameContext());
            commandBuffer.EndSample("Fodinae.Lighting.Composite");
        }

        private bool HasDynamicLightsChanged()
        {
            return !_hasRenderedLightState || _dynamicLightManager.IsDirty;
        }

        public void SetDynamicLightSettings(float intensity, Color color)
        {
            if (_configHolder.SetDynamicLightSettings(intensity, color))
            {
                _dynamicLightManager.MarkDirty();
                _compositeDirty = true;
                _hasDynamicRadianceState = false;
                _hasRenderedLightState = false;
                Debug.Log($"[LightingEngine] SetDynamicLightSettings: intensity={intensity}, color={color}");
            }
        }

        public void SetDynamicLightUpdatesPerSecond(float value)
        {
            if (_configHolder.SetDynamicLightUpdatesPerSecond(value))
            {
                _nextDynamicLightingUpdateTime = 0f;
                Debug.Log($"[LightingEngine] SetDynamicLightUpdatesPerSecond: {value}");
            }
        }

        private void RememberDynamicLightState()
        {
            _hasRenderedLightState = true;
            _dynamicLightManager.ClearDirty();
        }

        private Vector4 GetStableLightingRegion(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight)
        {
            return LightingRegionCalculator.GetStableLightingRegion(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight,
                _lastVisibleRegion);
        }

        private void EnsureResources(int gridWidth, int gridHeight, Camera camera)
        {
            int oldFieldWidth = _resources.FieldWidth;
            int oldFieldHeight = _resources.FieldHeight;

            _requestedPixelsPerCell = _lightingQualityMode == LightingQualityMode.PerBlock
                ? 1
                : Mathf.Clamp(_qualitySettings.LightingMinimumPixelsPerCell, 1, 16);

            _resources.EnsureResources(
                gridWidth,
                gridHeight,
                camera,
                in _qualitySettings,
                _lightingQualityMode,
                out _textureDimensionLimited,
                out _cascadeBudgetLimited,
                out int effectivePixelsPerCell);

            _effectivePixelsPerCell = effectivePixelsPerCell;

            _dynamicLightManager.EnsureCapacity(
                Mathf.Max(1, _qualitySettings.LightingMaximumLightCount));

            if (oldFieldWidth != _resources.FieldWidth || oldFieldHeight != _resources.FieldHeight)
            {
                _fieldDirty = true;
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
            }
        }

        private void EnsureGpuPipelineInitialized()
        {
            _resources.EnsureGpuPipelineInitialized();
        }

        private void DisableGpuLighting()
        {
            ReleaseGpuPipeline();
            PublishLightingDisabledState();
        }

        private void ReleaseGpuPipeline()
        {
            _resources.ReleaseGpuPipeline();
            _dynamicLightManager.ResetUploadState();
        }

        private void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings)
        {
            GraphicsQualityProfile.ValidateSettings(settings, preset.ToString());
            bool technicalSettingsChanged = _qualitySettings != settings;
            LightingQualityMode previousQuality = _lightingQualityMode;
            if (technicalSettingsChanged && _gpuPipelineInitialized)
            {
                ReleaseResources();
            }

            _graphicsPreset = preset;
            ApplyUnityQualityLevel(preset);
            _qualitySettings = settings;
            LightingQualityMode resolvedQuality = LightingQualityResolver.Resolve(
                preset,
                settings.LightingQuality);
            if (resolvedQuality != _lightingQualityMode)
            {
                _lightingQualityMode = resolvedQuality;
            }

            if (resolvedQuality == LightingQualityMode.Off)
            {
                DisableGpuLighting();
            }
            else
            {
                _lightingDisabledStatePublished = false;
                Shader.EnableKeyword(WorldLightingKeyword);
            }

            ApplyUnityRenderingSettings(_qualitySettings);
            if (!technicalSettingsChanged && previousQuality == resolvedQuality)
            {
                return;
            }

            _lastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            _fieldDirty = true;
            _nextLightingUpdateTime = 0f;
            _nextDynamicLightingUpdateTime = 0f;
            _dynamicSolveInProgress = false;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }

        private static void ApplyUnityQualityLevel(GraphicsPreset preset)
        {
            if (!GraphicsQualityProfile.IsStandard(preset))
            {
                return;
            }

            string targetName = preset.ToString();
            string[] qualityNames = UnityEngine.QualitySettings.names;
            int qualityIndex = Array.IndexOf(qualityNames, targetName);
            if (qualityIndex < 0)
            {
                int presetIndex = (int)preset;
                if (presetIndex >= 0 && presetIndex < qualityNames.Length)
                {
                    qualityIndex = presetIndex;
                }
                else
                {
                    for (int i = 0; i < qualityNames.Length; i++)
                    {
                        if (string.Equals(qualityNames[i].Replace(" ", string.Empty), targetName, StringComparison.OrdinalIgnoreCase))
                        {
                            qualityIndex = i;
                            break;
                        }
                    }
                }
            }

            if (qualityIndex >= 0 && UnityEngine.QualitySettings.GetQualityLevel() != qualityIndex)
            {
                UnityEngine.QualitySettings.SetQualityLevel(qualityIndex, applyExpensiveChanges: true);
                Debug.Log($"[LightingEngine] Applied Unity QualityLevel: {qualityNames[qualityIndex]} ({qualityIndex})");
            }
        }

        /// <summary>
        /// Applies the parts of a graphics preset this engine actually owns.
        /// </summary>
        /// <remarks>
        /// VSync is deliberately not among them. Frame pacing belongs to one
        /// owner, and that owner is DisplayManager.
        /// </remarks>
        private static void ApplyUnityRenderingSettings(GraphicsQualitySettings settings)
        {
            UnityEngine.QualitySettings.antiAliasing = Mathf.Clamp(settings.AntiAliasing, 0, 8);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                // Масштаб приводится к обратной величине целого: только на
                // них апскейл до окна остаётся целократным и не размазывает
                // выровненную сетку текселей. Авторские 0.65, 0.8 и 0.9 ей
                // не являются, поэтому подмена называется вслух — иначе
                // расхождение профиля и картинки пришлось бы искать глазами.
                float requested = Mathf.Clamp(settings.RenderScale, 0.5f, 1f);
                float quantized = PixelGrid.QuantizeRenderScale(requested, 0.5f, 1f);
                if (!Mathf.Approximately(requested, quantized))
                {
                    Debug.Log(
                        $"[LightingEngine] Масштаб рендера {requested:F2} приведён к {quantized:F2}: " +
                        "промежуточные значения дают дробный апскейл и муар на пиксель-арте.");
                }

                urp.renderScale = quantized;
                urp.msaaSampleCount = Mathf.Max(1, settings.AntiAliasing);
            }

            Debug.Log($"[LightingEngine] ApplyUnityRenderingSettings: AA={settings.AntiAliasing}, RenderScale={settings.RenderScale}");
        }

        private void ReleaseResources()
        {
            _resources.ReleaseResources();
            _dynamicLightManager.ResetUploadState();
            _dynamicSolveInProgress = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }
    }
}
