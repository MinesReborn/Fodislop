#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Core.Interfaces.WorldLighting;

public interface ILightingGeometryContributor
{
    /// <summary>Monotonic revision of the committed geometry visible to lighting.</summary>
    ulong LightingGeometryRevision { get; }

    /// <summary>
    /// Records albedo/occupancy and emission fields for this call. Texture
    /// handles and command-buffer state are borrowed and MUST NOT be retained or
    /// changed outside the supplied render phase.
    /// </summary>
    void RenderMaterialEmissionFields(CommandBuffer commandBuffer, in LightingMaterialEmissionContext context);

    /// <summary>
    /// Records displaced geometry occupancy only. The target is a single-channel
    /// semantic field in alpha of an ARGB32 texture; coordinates use Unity world units.
    /// </summary>
    void RenderAmbientOcclusionField(CommandBuffer commandBuffer, in LightingAmbientOcclusionContext context);
}

/// <summary>Material/emission targets borrowed for one call; WorldRect is a positive extent in Unity world units.</summary>
public readonly record struct LightingMaterialEmissionContext(
    RenderTexture MaterialField,
    RenderTexture EmissionField,
    Vector4 WorldRect);

/// <summary>Single AO occupancy target borrowed for one call; WorldRect is a positive extent in Unity world units.</summary>
public readonly record struct LightingAmbientOcclusionContext(
    RenderTexture AmbientOcclusionField,
    Vector4 WorldRect);

/// <summary>
/// Lighting's immutable padding policy. Cell counts are in world-cell units;
/// the value is copied into the exchange and remains valid until superseded.
/// </summary>
public readonly record struct LightingTerrainRequirements(
    /// <summary>Non-zero monotonic policy version; advances only when either padding changes.</summary>
    ulong PolicyRevision,
    /// <summary>Required terrain window margin in world cells.</summary>
    int RequiredTerrainPaddingCells,
    /// <summary>Lighting cache stable-region margin in world cells.</summary>
    int StableLightingPaddingCells);

public enum TerrainLightingFrameState
{
    Ready,
    HoldingPublishedView,
}

/// <summary>
/// Committed Terrain view for one Unity frame. Rectangles are half-open world
/// cell coordinates (Unity Y-up). The snapshot is copied into the exchange;
/// Camera and contributor are borrowed references valid through the end of the
/// publishing Unity frame. Publication and reads occur on Unity's main thread.
/// </summary>
public readonly record struct TerrainLightingFrameSnapshot(
    /// <summary>Non-zero identity of the active world residency generation.</summary>
    ulong WorldGeneration,
    /// <summary>Non-zero, contiguous frame publication sequence within the active world generation.</summary>
    ulong FrameSequence,
    /// <summary>Whether terrain committed a new view or retains the old published view.</summary>
    TerrainLightingFrameState State,
    /// <summary>Terrain's monotonic revision for the committed geometry.</summary>
    ulong TerrainGeometryRevision,
    /// <summary>Half-open camera viewport in Unity world-cell coordinates.</summary>
    RectInt CameraViewportCells,
    /// <summary>Half-open lighting viewport in Unity world-cell coordinates.</summary>
    RectInt LightingViewportCells,
    /// <summary>Borrowed camera, valid through the end of the publishing Unity frame.</summary>
    Camera Camera,
    /// <summary>Borrowed geometry contribution, valid through the end of the publishing Unity frame.</summary>
    ILightingGeometryContributor GeometryContributor);

[System.Flags]
public enum TerrainLightingChannels
{
    None = 0,
    Occupancy = 1 << 0,
    Material = 1 << 1,
    Emission = 1 << 2,
    All = Occupancy | Material | Emission,
}

public enum TerrainLightingChangeKind
{
    Region,
    FullReset,
}

public enum TerrainLightingFullResetReason
{
    WorldReplaced,
    TerrainConfigurationChanged,
    LightingVisibleTextureChanged,
    UnboundedGeometryChange,
}

/// <summary>
/// A committed geometry invalidation in half-open Unity world-cell coordinates.
/// This value is copied into the exchange; publication, read, and acknowledgement
/// happen on Unity's main thread. Lighting acknowledges only after durable transfer.
/// </summary>
public readonly record struct TerrainLightingChange(
    /// <summary>Non-zero identity of the active world residency generation.</summary>
    ulong WorldGeneration,
    /// <summary>Non-zero contiguous change sequence within the world generation.</summary>
    ulong Sequence,
    /// <summary>Terrain's monotonic geometry revision after this committed change.</summary>
    ulong TerrainGeometryRevision,
    /// <summary>Region invalidation or a named whole-world reset.</summary>
    TerrainLightingChangeKind Kind,
    /// <summary>Geometry channels affected by the change.</summary>
    TerrainLightingChannels Channels,
    /// <summary>Half-open world-cell region; used only for <see cref="TerrainLightingChangeKind.Region"/>.</summary>
    RectInt Region,
    /// <summary>Explicit reason; used only for <see cref="TerrainLightingChangeKind.FullReset"/>.</summary>
    TerrainLightingFullResetReason FullResetReason);

public enum LightingOutputState
{
    Published,
    Disabled,
}

/// <summary>
/// Lighting output state published on Unity's main thread after its render
/// commands complete. Rectangles use half-open Unity world-cell coordinates;
/// this copied value remains valid until superseded.
/// </summary>
public readonly record struct LightingOutputSnapshot(
    /// <summary>Non-zero monotonic lighting-output publication generation.</summary>
    ulong OutputGeneration,
    /// <summary>Non-zero world generation represented by this output.</summary>
    ulong WorldGeneration,
    /// <summary>Published texture state or explicit disabled state.</summary>
    LightingOutputState State,
    /// <summary>Half-open world-cell rectangle represented by the published texture.</summary>
    RectInt WorldRectCells);

/// <summary>
/// Main-thread, data-only exchange. It never invokes either domain.
/// Terrain owns frame/change publication; Lighting owns requirements/output
/// publication and both acknowledgement watermarks.
/// </summary>
public interface ITerrainLightingExchange
{
    void PublishLightingRequirements(in LightingTerrainRequirements requirements);

    bool TryReadLightingRequirements(out LightingTerrainRequirements requirements);

    void PublishTerrainFrame(in TerrainLightingFrameSnapshot frame);

    bool TryReadLatestTerrainFrame(
        ulong afterFrameSequence,
        out TerrainLightingFrameSnapshot frame);

    void AcknowledgeTerrainFrame(ulong throughFrameSequence);

    void PublishTerrainChange(in TerrainLightingChange change);

    bool TryReadNextTerrainChange(
        ulong worldGeneration,
        ulong afterSequence,
        out TerrainLightingChange change);

    void AcknowledgeTerrainChanges(ulong worldGeneration, ulong throughSequence);

    void PublishLightingOutput(in LightingOutputSnapshot output);

    bool TryReadLightingOutput(out LightingOutputSnapshot output);
}
