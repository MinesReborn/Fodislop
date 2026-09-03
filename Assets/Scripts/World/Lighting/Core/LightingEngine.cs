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
            ContactOcclusion,
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
        private static readonly int ContactOcclusionTextureId = Shader.PropertyToID("_ContactOcclusionTexture");
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
        private static readonly int AmbientOcclusionRadiusCellsId =
            Shader.PropertyToID("_AmbientOcclusionRadiusCells");
        private static readonly int AmbientOcclusionStrengthId =
            Shader.PropertyToID("_AmbientOcclusionStrength");
        private static readonly int TransmittanceDebugDistanceCellsId =
            Shader.PropertyToID("_TransmittanceDebugDistanceCells");
        private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
        private static readonly int MaterialYFlipId = Shader.PropertyToID("_MaterialYFlip");
        private static readonly int MaximumIntervalStepsId =
            Shader.PropertyToID("_MaximumIntervalSteps");
        private static readonly int EnableContactOcclusionId =
            Shader.PropertyToID("_EnableContactOcclusion");
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

        private static readonly string[] RequiredKernels =
        [
            ProjectRuntimeContracts.ComputeKernelNames.SolveCascade,
            ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals,
            ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion,
            ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect,
            ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce,
            ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting,
            ProjectRuntimeContracts.ComputeKernelNames.ResolveAndComposite,
        ];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Shader.DisableKeyword(WorldLightingKeyword);
        }

        [Header("Quality")]

        // Quality is selected by ClientConfig.GraphicsPreset at runtime.
        private GraphicsPreset _graphicsPreset;
        private LightingQualityMode _lightingQualityMode = LightingQualityMode.PerBlock;
        private LightingPipeline? _contactOcclusionPipeline;
        private LightingPipeline? _compositePipeline;
        private LightingPipeline? _automaticNormalsPipeline;
        private LightingPipeline? _diffuseBouncePipeline;
        private LightingPipeline? _dynamicEmissionCompositionPipeline;
        private LightingPipeline? _materialFieldPipeline;
        private TerrainRenderer? _activeTerrainRenderer;

        private LightingConfigHolder _configHolder = null!;

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Debug view для проверки отдельных lighting-слоёв без скрытого AO/exposure влияния.")]
        private DebugView _debugView;

        private readonly List<CascadeLayout> _cascades = new();
        private readonly DynamicLightManager _dynamicLightManager = new();
        private GraphicsQualitySettings _qualitySettings;
        private ComputeShader? _lightingCompute;
        private ComputeBuffer? _dynamicLightBuffer;
        private ComputeBuffer? _radianceAtlas;
        private CommandBuffer? _lightingCommandBuffer;
        private RenderTexture? _materialField;
        private RenderTexture? _staticEmissionField;
        private RenderTexture? _dynamicEmissionField;
        private Material? _dynamicEmissionMaterial;
        private RenderTexture? _automaticNormalField;
        private RenderTexture? _directTexture;

        // Resolved radiance from terrain emitters only. Survives until geometry
        // changes; the per-frame solve writes _directTexture and the composite
        // adds the two.
        private RenderTexture? _staticDirectTexture;
        private RenderTexture? _ambientOcclusionTexture;
        private RenderTexture? _bounceTexture;
        private RenderTexture? _lightmapTexture;
        private int _solveCascadeKernel;
        private int _solveAutomaticNormalsKernel;
        private int _solveContactOcclusionKernel;
        private int _resolveDirectKernel;
        private int _solveDiffuseBounceKernel;
        private int _compositeLightingKernel;
        private int _resolveAndCompositeKernel;
        private int _fieldWidth;
        private int _fieldHeight;
        private float _requestedPixelsPerCell;
        private float _effectivePixelsPerCell;
        private bool _textureDimensionLimited;
        private bool _cascadeBudgetLimited;
        private int _bounceWidth;
        private int _bounceHeight;
        private int _atlasCapacity;
        private int _atlasEntryCount;
        private bool _fieldDirty = true;
        private bool _ambientOcclusionDirty = true;
        private bool _compositeDirty = true;
        private bool _bounceDirty = true;


        private float _nextLightingUpdateTime;
        private float _nextDynamicLightingUpdateTime;
        private ulong _solveCount;
        private ulong _contactOcclusionSolveCount;
        private ulong _lastTerrainGeometryRevision;
        private ulong _lastContributorGeometryRevision;
        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IProjectDefaults _projectDefaults = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        private Vector4 _lastVisibleRegion = new(float.NaN, float.NaN, float.NaN, float.NaN);

        private bool _hasRenderedLightState;
        private bool _initialized;
        private bool _gpuPipelineInitialized;
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

        public bool AmbientOcclusionEnabled => _configHolder.AmbientOcclusionEnabled;

        public bool DiffuseBounceEnabled => _configHolder.DiffuseBounceEnabled;

        public float AmbientIntensity => _configHolder.AmbientIntensity;

        public Color AmbientColor => _configHolder.AmbientColor;

        public float EmissionScale => _configHolder.EmissionScale;

        public Color EmptyExtinctionRgb => _configHolder.EmptyExtinctionRgb;

        public Color SolidExtinctionRgb => _configHolder.SolidExtinctionRgb;

        public float EmptyExtinctionMultiplier => _configHolder.EmptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => _configHolder.SolidExtinctionMultiplier;

        public float BounceStrength => _configHolder.BounceStrength;

        public float AmbientOcclusionRadiusCells => _configHolder.AmbientOcclusionRadiusCells;

        public float AmbientOcclusionStrength => _configHolder.AmbientOcclusionStrength;

        public float MaximumLightMultiplier => _configHolder.MaximumLightMultiplier;

        public float TransmittanceDebugDistanceCells => _configHolder.TransmittanceDebugDistanceCells;

        public float MinimumTransmission => _configHolder.MinimumTransmission;

        public bool EnableFinalLightingClamp => _configHolder.EnableFinalLightingClamp;

        public float DynamicLightIntensity => _configHolder.RuntimeConfig.DynamicLightIntensity;

        public Color DynamicLightColor => _configHolder.RuntimeConfig.DynamicLightColor;

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

        public ulong ContactOcclusionSolveCount => _contactOcclusionSolveCount;

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
            _projectDefaults != null &&
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
            _configHolder.ApplyProjectDefaults(_projectDefaults.Lighting);
            LoadRuntimeConfig();
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
            _configHolder?.ForceSave();

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

            if (_configHolder != null && _configHolder.TrySave(Time.unscaledTime))
            {
                _clientConfig.Save();
            }
        }

        private void OnApplicationQuit()
        {
            _configHolder?.ForceSave();
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
            _configHolder.ApplyClientConfig();
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _ambientOcclusionDirty = true;
            _bounceDirty = true;
            _compositeDirty = true;
            _dynamicLightManager.IncrementGeneration();
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
        }

        public void SetAmbientOcclusionEnabled(bool enabled)
        {
            if (_configHolder.SetAmbientOcclusionEnabled(enabled))
            {
                _ambientOcclusionDirty = true;
                _compositeDirty = true;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
            }
        }

        public void SetDiffuseBounceEnabled(bool enabled)
        {
            if (_configHolder.SetDiffuseBounceEnabled(enabled))
            {
                _bounceDirty = true;
                _compositeDirty = true;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
            }
        }

        public void SetAmbientIntensity(float value)
        {
            _configHolder.SetAmbientIntensity(value);
        }

        public void SetAmbientColor(Color value)
        {
            bool changed = _configHolder.SetAmbientColor(value);
            if (changed)
            {
                _compositeDirty = true;
            }
        }

        public void SetEmissionScale(float value)
        {
            _configHolder.SetEmissionScale(value);
        }

        public void SetEmptyExtinctionColor(Color value)
        {
            bool changed = _configHolder.SetEmptyExtinctionColor(value);
            if (changed)
            {
                _compositeDirty = true;
            }
        }

        public void SetSolidExtinctionColor(Color value)
        {
            bool changed = _configHolder.SetSolidExtinctionColor(value);
            if (changed)
            {
                _compositeDirty = true;
            }
        }

        public void SetFinalLightingClampEnabled(bool enabled)
        {
            if (_configHolder.SetFinalLightingClampEnabled(enabled))
            {
                _compositeDirty = true;
            }
        }

        public void SetEmptyExtinctionMultiplier(float value)
        {
            _configHolder.SetEmptyExtinctionMultiplier(value);
        }

        public void SetSolidExtinctionMultiplier(float value)
        {
            _configHolder.SetSolidExtinctionMultiplier(value);
        }

        public void SetBounceStrength(float value)
        {
            if (_configHolder.SetBounceStrength(value))
            {
                _bounceDirty = true;
            }
        }

        public void SetAmbientOcclusionRadius(float value)
        {
            if (_configHolder.SetAmbientOcclusionRadius(value))
            {
                _ambientOcclusionDirty = true;
            }
        }

        public void SetAmbientOcclusionStrength(float value)
        {
            if (_configHolder.SetAmbientOcclusionStrength(value))
            {
                _ambientOcclusionDirty = true;
            }
        }

        public void SetMaximumLightMultiplier(float value)
        {
            _configHolder.SetMaximumLightMultiplier(value);
        }

        public void SetTransmittanceDebugDistance(float value)
        {
            if (_configHolder.SetTransmittanceDebugDistance(value))
            {
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
                _hasDynamicRadianceState = false;
                _compositeDirty = true;
            }
        }

        public void SetMinimumTransmission(float value)
        {
            _configHolder.SetMinimumTransmission(value);
        }

        public void SetLightSafeBorder(float value)
        {
            if (_configHolder.SetLightSafeBorder(value))
            {
                _fieldDirty = true;
                _hasRenderedLightState = false;
            }
        }

        public void ResetRuntimeLightingPreferences()
        {
            _configHolder.ApplyLightingDefaultsToClientConfig(_projectDefaults.Lighting);
            _configHolder.Load();
            _clientConfig.Save();
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _fieldDirty = true;
            _ambientOcclusionDirty = true;
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
            bool ambientOcclusionChanged = _ambientOcclusionDirty;
            if (!_fieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
                !ambientOcclusionChanged && !_compositeDirty && !_bounceDirty)
            {
                return;
            }

            bool geometryUpdateRequired = _fieldDirty || regionChanged || geometryChanged;
            bool dynamicOnlyUpdate = dynamicLightsDirty &&
                !geometryUpdateRequired &&
                !ambientOcclusionChanged &&
                !_bounceDirty &&
                !_compositeDirty;
            bool continueDynamicSolve = _dynamicSolveInProgress &&
                !geometryUpdateRequired &&
                !ambientOcclusionChanged &&
                !_bounceDirty &&
                !_compositeDirty;
            float nextAllowedUpdateTime = dynamicOnlyUpdate
                ? _nextDynamicLightingUpdateTime
                : _nextLightingUpdateTime;

            if (!continueDynamicSolve &&
                Time.unscaledTime < nextAllowedUpdateTime &&
                !geometryUpdateRequired &&
                !ambientOcclusionChanged &&
                !_compositeDirty &&
                !_bounceDirty &&
                _hasStaticRadianceState)
            {
                return;
            }

            if (geometryUpdateRequired || ambientOcclusionChanged || _bounceDirty || _compositeDirty)
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
                    !ambientOcclusionChanged && !_compositeDirty && !_bounceDirty)
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
                // else - contact occlusion, bounce, the composite's emission
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

                if (ShouldDispatchContactOcclusion(
                    _configHolder.AmbientOcclusionEnabled,
                    rebuildFields,
                    _ambientOcclusionDirty))
                {
                    DispatchContactOcclusion(commandBuffer);
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

                // CompositeLighting, not the fused ResolveAndComposite: the fused
                // kernel resolves the atlas itself and so can only ever see one
                // half. It handles the field debug views (occupancy, albedo,
                // emission) identically, so nothing is lost by always taking this
                // path.
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
                _ambientOcclusionDirty = false;
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
                AmbientOcclusionRadiusCellsId,
                _configHolder.AmbientOcclusionRadiusCells);
            commandBuffer.SetComputeFloatParam(
                compute,
                AmbientOcclusionStrengthId,
                _configHolder.AmbientOcclusionStrength);
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
                EnableContactOcclusionId,
                _configHolder.AmbientOcclusionEnabled ? 1 : 0);
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
            BindFieldTextures(commandBuffer, _solveContactOcclusionKernel, emissionField);
            BindFieldTextures(commandBuffer, _resolveDirectKernel, emissionField);
            BindFieldTextures(commandBuffer, _solveDiffuseBounceKernel, emissionField);
            BindFieldTextures(commandBuffer, _compositeLightingKernel, emissionField);
            BindFieldTextures(commandBuffer, _resolveAndCompositeKernel, emissionField);
            BindAutomaticNormalInput(commandBuffer, _resolveDirectKernel);
            BindAutomaticNormalInput(commandBuffer, _solveDiffuseBounceKernel);
            BindAutomaticNormalInput(commandBuffer, _resolveAndCompositeKernel);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _compositeLightingKernel,
                ContactOcclusionTextureId,
                _ambientOcclusionTexture!);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _resolveAndCompositeKernel,
                ContactOcclusionTextureId,
                _ambientOcclusionTexture!);
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

        private void DispatchContactOcclusion(CommandBuffer commandBuffer)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.ContactOcclusion");
            _contactOcclusionPipeline!.Record(commandBuffer, BuildFrameContext());
            commandBuffer.EndSample("Fodinae.Lighting.ContactOcclusion");
            _contactOcclusionSolveCount++;
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
                _ambientOcclusionTexture!,
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

        public static bool ShouldDispatchContactOcclusion(
            bool ambientOcclusionEnabled,
            bool geometryOrRegionChanged,
            bool ambientOcclusionSettingsChanged)
        {
            return ambientOcclusionEnabled &&
                (geometryOrRegionChanged || ambientOcclusionSettingsChanged);
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

        private void LoadRuntimeConfig()
        {
            _configHolder.Load();
        }

        public void SetDynamicLightSettings(float intensity, Color color)
        {
            _configHolder.SetDynamicLightSettings(intensity, color);
        }

        public void SetDynamicLightUpdatesPerSecond(float value)
        {
            if (_configHolder.SetDynamicLightUpdatesPerSecond(value))
            {
                _nextDynamicLightingUpdateTime = 0f;
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
            if (!camera.orthographic)
            {
                throw new InvalidOperationException(
                    "Radiance Cascades requires an orthographic base camera.");
            }

            if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0 ||
                camera.orthographicSize <= 0f || camera.aspect <= 0f)
            {
                throw new InvalidOperationException(
                    $"Radiance Cascades received invalid camera metrics: " +
                    $"pixels={camera.pixelWidth}x{camera.pixelHeight}, " +
                    $"orthographicSize={camera.orthographicSize}, aspect={camera.aspect}.");
            }

            // PerBlock forces exactly one field texel per world cell: the
            // resolve kernels already flatten shading to one value per cell
            // via _BlockAveraged, so a denser field would only cost more GPU
            // time for a result that gets thrown away at the sample-snap step.
            _requestedPixelsPerCell = _lightingQualityMode == LightingQualityMode.PerBlock
                ? 1
                : Mathf.Clamp(_qualitySettings.LightingMinimumPixelsPerCell, 1, 16);

            int requestedScale = Mathf.Max(1, Mathf.FloorToInt(_requestedPixelsPerCell));
            int scale = SelectStablePixelsPerCell(gridWidth, gridHeight, requestedScale);
            int maximumTextureScale = Mathf.Max(
                0,
                Mathf.Min(
                    _qualitySettings.LightingMaximumTextureDimension / gridWidth,
                    _qualitySettings.LightingMaximumTextureDimension / gridHeight));
            _textureDimensionLimited = maximumTextureScale < requestedScale;
            _cascadeBudgetLimited = scale < Mathf.Min(requestedScale, maximumTextureScale);
            int fieldWidth = gridWidth * scale;
            int fieldHeight = gridHeight * scale;
            _effectivePixelsPerCell = scale;
            if (_fieldWidth == fieldWidth && _fieldHeight == fieldHeight &&
                _materialField != null && _ambientOcclusionTexture != null &&
                _radianceAtlas != null)
            {
                return;
            }

            ReleaseFieldTextures();
            _fieldWidth = fieldWidth;
            _fieldHeight = fieldHeight;
            _bounceWidth = Mathf.Max(1, Mathf.CeilToInt(fieldWidth * 0.5f));
            _bounceHeight = Mathf.Max(1, Mathf.CeilToInt(fieldHeight * 0.5f));
            _materialField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGB32,
                randomWrite: false,
                FilterMode.Bilinear,
                "_LightingMaterialField",
                useMipMap: true);
            _staticEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_StaticEmissionField",
                useMipMap: false);

            // Holds the dynamic sources and nothing else. The march binds either
            // this or the static field, depending on which half of the split it
            // is solving.
            //
            // No mip chain, deliberately. Giving emission one and letting the
            // march sample it at log2(stepLength) looked like the obvious way to
            // let cascades take longer steps. It is not: emission is sparse and
            // bright, so a coarse mip spreads a lamp across everything near it,
            // and on a low preset - where the atlas budget shrinks the field to a
            // couple of hundred texels to begin with - mip 2 is a fiftyish-texel
            // image. Occupancy is the field that wants prefiltering; emission
            // wants to stay where it is.
            _dynamicEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_DynamicEmissionField",
                useMipMap: false);
            _automaticNormalField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Point,
                "_AutomaticNormalField");
            _directTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirect");
            _staticDirectTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirectStatic");
            _ambientOcclusionTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.RHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_ContactOcclusion");
            _bounceTexture = CreateTexture(
                _bounceWidth,
                _bounceHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceBounce");
            _lightmapTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_WorldLightTexture");

            BuildCascadeLayouts(fieldWidth, fieldHeight);
            _atlasEntryCount = _cascades[^1].Offset + _cascades[^1].EntryCount;
            EnsurePersistentBuffers();
            _fieldDirty = true;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }

        private int SelectStablePixelsPerCell(
            int gridWidth,
            int gridHeight,
            int requestedScale)
        {
            int maximumTextureDimension =
                _qualitySettings.LightingMaximumTextureDimension;
            long atlasDimension = _qualitySettings.LightingCascadeAtlasLimit;
            long maximumEntryCount = atlasDimension * atlasDimension * 4;
            for (int scale = requestedScale; scale >= 1; scale--)
            {
                int width = checked(gridWidth * scale);
                int height = checked(gridHeight * scale);
                if (width > maximumTextureDimension ||
                    height > maximumTextureDimension)
                {
                    continue;
                }

                int maximumCascadeCount = CascadeLayoutBuilder.GetMaximumCascadeCount(atlasDimension);
                long requiredEntryCount = CascadeLayoutBuilder.CalculateCascadeEntryCount(
                    width,
                    height,
                    maximumCascadeCount);
                if (requiredEntryCount <= maximumEntryCount)
                {
                    return scale;
                }
            }

            throw new InvalidOperationException(
                $"Radiance cascade region {gridWidth}x{gridHeight} cannot fit at " +
                $"one texel per cell within texture limit {maximumTextureDimension} " +
                $"and atlas limit {atlasDimension}.");
        }

        private void EnsurePersistentBuffers()
        {
            long atlasDimension = _qualitySettings.LightingCascadeAtlasLimit;
            long maximumCapacity = atlasDimension * atlasDimension * 4;
            if (maximumCapacity <= 0 || maximumCapacity > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Radiance cascade atlas capacity exceeds the supported structured-buffer size.");
            }

            if (_atlasEntryCount > maximumCapacity)
            {
                throw new InvalidOperationException(
                    "Radiance cascade layout exceeds the configured atlas capacity.");
            }

            int requiredCapacity = Mathf.Max(1, _atlasEntryCount);
            if (_radianceAtlas == null || _atlasCapacity < requiredCapacity)
            {
                _radianceAtlas?.Release();
                _radianceAtlas = new ComputeBuffer(
                    requiredCapacity,
                    RadianceStride,
                    ComputeBufferType.Structured);
                _atlasCapacity = requiredCapacity;
            }

            int maximumLightCount = Mathf.Max(
                1,
                _qualitySettings.LightingMaximumLightCount);
            if (_dynamicLightBuffer == null || _dynamicLightBuffer.count != maximumLightCount)
            {
                _dynamicLightBuffer?.Release();
                _dynamicLightBuffer = new ComputeBuffer(
                    maximumLightCount,
                    DynamicLightStride,
                    ComputeBufferType.Structured);
            }

            _dynamicLightManager.EnsureCapacity(maximumLightCount);
        }

        private void BuildCascadeLayouts(int width, int height)
        {
            CascadeLayoutBuilder.BuildCascadeLayouts(
                width,
                height,
                _qualitySettings.LightingCascadeAtlasLimit,
                _cascades);
        }

        private static RenderTexture CreateTexture(
            int width,
            int height,
            RenderTextureFormat format,
            bool randomWrite,
            FilterMode filterMode,
            string name,
            bool useMipMap = false)
        {
            var texture = new RenderTexture(
                width,
                height,
                0,
                format,
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = randomWrite,
                useMipMap = useMipMap,
                autoGenerateMips = false,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            if (!texture.Create())
            {
                DestroyLightingObject(texture);
                throw new InvalidOperationException($"Failed to create required lighting target '{name}'.");
            }

            return texture;
        }

        private void LoadComputeShaderOrThrow()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException("Radiance Cascades requires compute shader support.");
            }

            _lightingCompute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute) ??
                throw new InvalidOperationException(
                    $"Required compute shader Resources/{ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute} is missing.");
            foreach (string kernelName in RequiredKernels)
            {
                if (!_lightingCompute.HasKernel(kernelName))
                {
                    throw new InvalidOperationException(
                        $"Radiance Cascades compute shader is missing kernel '{kernelName}'.");
                }
            }

            _solveCascadeKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveCascade);
            _solveAutomaticNormalsKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals);
            _solveContactOcclusionKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion);
            _resolveDirectKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect);
            _solveDiffuseBounceKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce);
            _compositeLightingKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting);
            _resolveAndCompositeKernel = _lightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveAndComposite);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveCascade,
                _solveCascadeKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals,
                _solveAutomaticNormalsKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion,
                _solveContactOcclusionKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect,
                _resolveDirectKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce,
                _solveDiffuseBounceKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting,
                _compositeLightingKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveAndComposite,
                _resolveAndCompositeKernel);
            _contactOcclusionPipeline = new LightingPipeline(
                new ContactOcclusionStage(_solveContactOcclusionKernel));
            _compositePipeline = new LightingPipeline(
                new CompositeStage(_compositeLightingKernel));
            _automaticNormalsPipeline = new LightingPipeline(
                new AutomaticNormalsStage(_solveAutomaticNormalsKernel));
            _diffuseBouncePipeline = new LightingPipeline(
                new DiffuseBounceStage(_solveDiffuseBounceKernel));
            _dynamicEmissionCompositionPipeline = new LightingPipeline(
                new DynamicEmissionCompositionStage());
            _materialFieldPipeline = new LightingPipeline(
                new MaterialFieldStage());
            LoadDynamicEmissionMaterialOrThrow();
        }

        private void EnsureGpuPipelineInitialized()
        {
            if (_gpuPipelineInitialized)
            {
                return;
            }

            LoadComputeShaderOrThrow();
            ValidateGpuRequirements();
            ValidateMaterialFieldPass();
            _lightingCommandBuffer = new CommandBuffer
            {
                name = "Fodinae Radiance Cascades",
            };
            _gpuPipelineInitialized = true;
            _lightingDisabledStatePublished = false;
            Shader.EnableKeyword(WorldLightingKeyword);
        }

        private void DisableGpuLighting()
        {
            ReleaseGpuPipeline();
            PublishLightingDisabledState();
        }

        private void ReleaseGpuPipeline()
        {
            ReleaseResources();
            if (_dynamicEmissionMaterial != null)
            {
                DestroyLightingObject(_dynamicEmissionMaterial);
                _dynamicEmissionMaterial = null;
            }

            _lightingCommandBuffer?.Release();
            _lightingCommandBuffer = null;
            _lightingCompute = null;
            _contactOcclusionPipeline = null;
            _compositePipeline = null;
            _automaticNormalsPipeline = null;
            _diffuseBouncePipeline = null;
            _dynamicEmissionCompositionPipeline = null;
            _materialFieldPipeline = null;
            _gpuPipelineInitialized = false;
        }

        private void LoadDynamicEmissionMaterialOrThrow()
        {
            if (_dynamicEmissionMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find(ProjectRuntimeContracts.ShaderNames.DynamicEmission) ??
                throw new InvalidOperationException(
                    $"Required shader '{ProjectRuntimeContracts.ShaderNames.DynamicEmission}' is missing. " +
                    "Dynamic light sources cannot be rasterized into the emission field.");
            _dynamicEmissionMaterial = new Material(shader)
            {
                name = "FodinaeDynamicEmission",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private void ValidateKernelSupportOrThrow(string kernelName, int kernelIndex)
        {
            if (_lightingCompute?.IsSupported(kernelIndex) != true)
            {
                throw new InvalidOperationException(
                    $"Radiance Cascades kernel '{kernelName}' failed to compile for {SystemInfo.graphicsDeviceType}.");
            }
        }

        private static void ValidateGpuRequirements()
        {
            if (SystemInfo.supportedRenderTargetCount < 2 ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new NotSupportedException(
                    "Radiance Cascades requires two MRTs, RGBA8 material, R16F contact AO, and random-write lighting targets.");
            }
        }

        private static void ValidateMaterialFieldPass()
        {
            Shader terrainShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Terrain) ??
                throw new InvalidOperationException("The terrain shader required by lighting is missing.");
            var validationMaterial = new Material(terrainShader);
            try
            {
                if (validationMaterial.FindPass(
                        ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0)
                {
                    throw new InvalidOperationException(
                        "The terrain shader is missing the LightingMaterialField pass.");
                }
            }
            finally
            {
                DestroyLightingObject(validationMaterial);
            }
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
            if (qualityIndex >= 0 && UnityEngine.QualitySettings.GetQualityLevel() != qualityIndex)
            {
                UnityEngine.QualitySettings.SetQualityLevel(qualityIndex, applyExpensiveChanges: true);
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
                urp.renderScale = Mathf.Clamp(settings.RenderScale, 0.5f, 1f);
            }
        }

        private void ReleaseResources()
        {
            _dynamicLightBuffer?.Release();
            _dynamicLightBuffer = null;
            _dynamicLightManager.ResetUploadState();
            _radianceAtlas?.Release();
            _radianceAtlas = null;
            _atlasCapacity = 0;
            _atlasEntryCount = 0;
            ReleaseFieldTextures();
        }

        private void ReleaseFieldTextures()
        {
            ReleaseTexture(ref _materialField);
            ReleaseTexture(ref _staticEmissionField);
            ReleaseTexture(ref _dynamicEmissionField);
            ReleaseTexture(ref _automaticNormalField);
            ReleaseTexture(ref _directTexture);
            ReleaseTexture(ref _staticDirectTexture);
            ReleaseTexture(ref _ambientOcclusionTexture);
            ReleaseTexture(ref _bounceTexture);
            ReleaseTexture(ref _lightmapTexture);
            _fieldWidth = 0;
            _fieldHeight = 0;
            _bounceWidth = 0;
            _bounceHeight = 0;
            _cascades.Clear();
            _dynamicSolveInProgress = false;
            _hasStaticRadianceState = false;
            _hasDynamicRadianceState = false;
        }

        private static void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            DestroyLightingObject(texture);
            texture = null;
        }

        private static void DestroyLightingObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
