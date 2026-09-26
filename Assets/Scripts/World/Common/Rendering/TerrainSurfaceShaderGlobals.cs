#nullable enable

using Kern.World.Terrain;
using UnityEngine;

namespace Kern.World.Common.Rendering;

public static class TerrainSurfaceShaderGlobals
{
    // Пол, ниже которого контактное затенение не опускает поверхность.
    //
    // Без него множитель уходил в ноль: у клетки пола вплотную к массиву
    // занятость в выборке близка к единице, и пол становился чёрным, а
    // не затенённым. След выборки в полклетки размазывал это в тёмную
    // полосу вдоль каждой границы массива, и полоса ездила вместе с
    // искажением — поле занятости пишется из смещённого силуэта.
    //
    // 0.51 — не подбор на глаз. В оригинале тень на полу это 1 - z² при
    // z = 0.7, то есть ровно 0.51, и глубже пол там не темнеет никогда.
    // Обе величины живут в TerrainConfigHolder.
    private static readonly int _AmbientOcclusionStrengthID =
        Shader.PropertyToID("_TerrainAmbientOcclusionStrength");

    private static readonly int _AmbientOcclusionFloorID =
        Shader.PropertyToID("_TerrainAmbientOcclusionFloor");

    private static readonly int _AmbientOcclusionDistanceID =
        Shader.PropertyToID("_TerrainAmbientOcclusionDistance");

    // Кайма включена по умолчанию. Публикуется на старте, потому что
    // глобаль живёт в нативной части: до первого ApplyClientConfig она
    // была бы нулём, и кайма молча не рисовалась бы.
    private static readonly int _ReliefRimEnabledID =
        Shader.PropertyToID("_TerrainReliefRimEnabled");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void ApplyShaderGlobals()
    {
        Shader.SetGlobalFloat(_AmbientOcclusionStrengthID, TerrainConfigHolder.AmbientOcclusionStrength);
        Shader.SetGlobalFloat(_AmbientOcclusionFloorID, TerrainConfigHolder.AmbientOcclusionFloor);
        Shader.SetGlobalFloat(
            _AmbientOcclusionDistanceID,
            TerrainConfigHolder.AmbientOcclusionDistanceCells);
        Shader.SetGlobalFloat(_ReliefRimEnabledID, 1f);
    }
}
