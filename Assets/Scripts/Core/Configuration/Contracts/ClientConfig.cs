#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;
[Serializable]
public class ClientConfig
{
    // 32: TerrainSettings.EnableReliefRim.
    // 33: TerrainSettings.DistortionStyle.
    // 34: две ступени качества (Стандарт/Overdrive) вместо шести, без Custom.
    // Схемы 31–33 мигрируются штатным загрузчиком с созданием backup.
    public const int CurrentSchemaVersion = 35;

    public int SchemaVersion;
    public AudioSettings Audio = new();
    public DisplaySettings Display = new();
    public InterfaceSettings Interface = new();
    public ConnectionSettings Connection = new();
    public TerrainSettings Terrain = new();
    public EffectSettings Effects = new();

    public GraphicsPreset GraphicsPreset = GraphicsPreset.Standard;
    public GraphicsQualitySettings GraphicsQualitySettings;
}
