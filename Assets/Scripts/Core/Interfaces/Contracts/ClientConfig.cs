#nullable enable

using System;
using Fodinae.Rendering;
using UnityEngine.Serialization;

namespace Fodinae.Core
{
    /// <summary>
    /// Клиентский конфиг: девять секций и выбор пресета графики.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ ТАК. Раньше здесь рядом с шестью аккуратными секциями лежали
    /// сорок пять плоских полей света, террейна и эффектов, приклеенных прямо
    /// к корню. Каждое из них приходилось перечислять руками ещё в пяти
    /// местах: в снимке ProjectDefaults, дважды в ClientConfigDefaults, в
    /// валидаторе и в цепочке сравнений «пресет не тронут». Добавление одной
    /// настройки было правкой семи файлов, а диапазоны в этих файлах молча
    /// расходились.
    ///
    /// Теперь настройка объявлена ровно один раз — полем своей секции, с
    /// инициализатором вместо отдельного источника значений по умолчанию и с
    /// <see cref="SettingRangeAttribute"/> вместо диапазона-литерала. Дефолты,
    /// валидация, сброс и границы ползунков выводятся из объявления через
    /// <see cref="SettingSchema"/>.
    ///
    /// Класс остаётся полевым <c>[Serializable]</c>: его пишет и читает
    /// <c>JsonUtility</c>, свойства ему недоступны.
    /// </remarks>
    [Serializable]
    public class ClientConfig
    {
        public const int CurrentSchemaVersion = 27;

        public int SchemaVersion;
        public AudioSettings Audio = new();
        public DisplaySettings Display = new();
        public InterfaceSettings Interface = new();
        public AccessibilitySettings Accessibility = new();
        public ConnectionSettings Connection = new();
        public PostProcessSettings PostProcess = new();
        public WorldLightingSettings Lighting = new();
        public TerrainSettings Terrain = new();
        public EffectSettings Effects = new();

        [FormerlySerializedAs("GraphicsQuality")]
        public GraphicsPreset GraphicsPreset = GraphicsPreset.High;
        public GraphicsQualitySettings GraphicsQualitySettings;
    }
}
