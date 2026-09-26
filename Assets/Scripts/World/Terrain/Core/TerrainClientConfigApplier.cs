#nullable enable

using Kern.Core;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Применение пользовательских настроек террейна к окну и материалам.
/// </summary>
///
/// Настройки приходят одним ClientConfig, а расходятся по трём адресатам:
/// тумблер искажения живёт в конвейере, кайма рельефа — глобалью шейдера,
/// остальное уходит в материалы. Renderer здесь только повод: он держит
/// конфиг, а раскладку по адресатам делает этот тип.
public sealed class TerrainClientConfigApplier
{
    private static readonly int _reliefRimEnabledID =
        Shader.PropertyToID("_TerrainReliefRimEnabled");

    private readonly TerrainWindow _window;

    public TerrainClientConfigApplier(TerrainWindow window) => _window = window;

    public void Apply(ClientConfig config)
    {
        bool enableDistortion = config.Terrain.EnableDistortion;
        if (_window.Driver.Pipeline.EnableDistortion != enableDistortion)
        {
            // Предрасчёт может принадлежать фоновому шагу: флаг ляжет в
            // следующий шаг, и тот соберёт окно целиком.
            _window.RequestDistortion(enableDistortion);
        }

        TerrainDistortionStyle distortionStyle = config.Terrain.DistortionStyle;
        if (_window.Driver.Pipeline.DistortionStyle != distortionStyle)
        {
            _window.RequestDistortionStyle(distortionStyle);
        }

        // Кайма живёт глобалью шейдера: маска и транспорт от тумблера не
        // зависят, выключенная кайма просто перестаёт умножать кадр.
        bool enableReliefRim = config.Terrain.EnableReliefRim;
        Shader.SetGlobalFloat(_reliefRimEnabledID, enableReliefRim ? 1f : 0f);

        _window.Driver.Presentation.ApplyClientConfig(config);
        Debug.Log(
            $"[TerrainRenderer] ApplyClientConfig: distortion={enableDistortion}, " +
            $"distortionStyle={distortionStyle}, " +
            $"reliefRim={enableReliefRim}");
    }
}
