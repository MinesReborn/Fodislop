#nullable enable

using System;
using System.Runtime.CompilerServices;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Владеет секцией освещения в конфиге и отложенной записью на диск.
    /// LightingEngine делегирует сюда все Set* и получает обратно признак
    /// «значение действительно изменилось».
    /// </summary>
    /// <remarks>
    /// ЧТО ОТСЮДА УШЛО. Класс держал восемнадцать свойств-зеркал секции,
    /// семнадцать почти одинаковых <c>SetXxx</c> с диапазонами-литералами, три
    /// блока копирования полей между снимком, конфигом и рантайм-копией, и
    /// собственный дебаунс. Рантайм-копии (<c>LightingRuntimeConfig</c> и
    /// маппера к ней) больше нет: живое состояние света — это
    /// <c>ClientConfig.Lighting</c>, и второй экземпляр тех же двадцати полей
    /// был не кэшем, а вторым источником истины, который приходилось
    /// синхронизировать вручную.
    ///
    /// Диапазоны теперь объявлены над полями секции, поэтому клампинг делает
    /// один общий <c>Set</c> вместо семнадцати.
    /// </remarks>
    internal sealed class LightingConfigHolder
    {
        private readonly IClientConfigManager _clientConfig;

        public LightingConfigHolder(IClientConfigManager clientConfig)
        {
            _clientConfig = clientConfig ?? throw new ArgumentNullException(nameof(clientConfig));
        }

        /// <summary>Живая секция освещения. Другого состояния света нет.</summary>
        public WorldLightingSettings Lighting =>
            (_clientConfig.Config ?? throw new InvalidOperationException(
                "LightingEngine requires an initialized ClientConfig.")).Lighting;

        public string ConfigFilePath => _clientConfig.ConfigFilePath;

        public bool DiffuseBounceEnabled => Lighting.DiffuseBounceEnabled;
        public float EmissionScale => Lighting.EmissionScale;
        public Color EmptyExtinctionRgb => Lighting.EmptyExtinctionRgb;
        public Color SolidExtinctionRgb => Lighting.SolidExtinctionRgb;
        public float EmptyExtinctionMultiplier => Lighting.EmptyExtinctionMultiplier;
        public float SolidExtinctionMultiplier => Lighting.SolidExtinctionMultiplier;
        public float BounceStrength => Lighting.BounceStrength;
        public float MaximumLightMultiplier => Lighting.MaximumLightMultiplier;
        public bool EnableFinalLightingClamp => Lighting.EnableFinalLightingClamp;
        public float TransmittanceDebugDistanceCells => Lighting.TransmittanceDebugDistanceCells;
        public float MinimumTransmission => Lighting.MinimumTransmission;
        public int LightSafeBorder => Lighting.LightSafeBorder;
        public float DynamicLightIntensity => Lighting.DynamicLightIntensity;
        public Color DynamicLightColor => Lighting.DynamicLightColor;
        public float DynamicLightUpdatesPerSecond => Lighting.DynamicLightUpdatesPerSecond;

        /// <summary>
        /// Вне режима игры тёмная сцена подменяется различимой.
        /// </summary>
        /// <remarks>
        /// Авторский ambient — 0.85 при почти чёрном цвете, и в окне Scene это
        /// даёт чёрный прямоугольник: там нет ни игрока, ни динамических
        /// источников. Подмена касается только того, что уходит в шейдер, и
        /// никогда не попадает в конфиг.
        /// </remarks>
        public float AmbientIntensity =>
            !Application.isPlaying && Lighting.AmbientIntensity < 0.4f
                ? 0.4f
                : Lighting.AmbientIntensity;

        public Color AmbientColor
        {
            get
            {
                Color authored = Lighting.AmbientColor;
                bool nearlyBlack = authored.r + authored.g + authored.b < 0.2f;
                return !Application.isPlaying && nearlyBlack
                    ? new Color(0.8f, 0.85f, 0.95f, 1f)
                    : authored;
            }
        }

        /// <summary>
        /// Откладывает запись конфига.
        /// </summary>
        /// <remarks>
        /// Собственного планировщика здесь больше нет. Он обслуживал только
        /// свет, а тик к нему приходилось пробрасывать через
        /// <c>LightingEngine.Update</c> — освещение крутило чужую
        /// ответственность. Дебаунс теперь один на весь конфиг и живёт у
        /// владельца файла.
        /// </remarks>
        public void QueueSave() => _clientConfig.SaveDeferred();

        /// <summary>Возвращает секцию к авторским значениям.</summary>
        public void ResetToDefaults()
        {
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "LightingEngine requires an initialized ClientConfig.");
            config.Lighting = new WorldLightingSettings();
        }

        public bool SetDiffuseBounceEnabled(bool enabled) =>
            Set(ref Lighting.DiffuseBounceEnabled, enabled);

        public bool SetFinalLightingClampEnabled(bool enabled) =>
            Set(ref Lighting.EnableFinalLightingClamp, enabled);

        public bool SetAmbientIntensity(float value) =>
            Set(ref Lighting.AmbientIntensity, value);

        public bool SetEmissionScale(float value) =>
            Set(ref Lighting.EmissionScale, value);

        public bool SetEmptyExtinctionMultiplier(float value) =>
            Set(ref Lighting.EmptyExtinctionMultiplier, value);

        public bool SetSolidExtinctionMultiplier(float value) =>
            Set(ref Lighting.SolidExtinctionMultiplier, value);

        public bool SetBounceStrength(float value) =>
            Set(ref Lighting.BounceStrength, value);

        public bool SetMaximumLightMultiplier(float value) =>
            Set(ref Lighting.MaximumLightMultiplier, value);

        public bool SetTransmittanceDebugDistance(float value) =>
            Set(
                ref Lighting.TransmittanceDebugDistanceCells,
                value,
                nameof(WorldLightingSettings.TransmittanceDebugDistanceCells));

        public bool SetMinimumTransmission(float value) =>
            Set(ref Lighting.MinimumTransmission, value);

        public bool SetDynamicLightUpdatesPerSecond(float value) =>
            Set(ref Lighting.DynamicLightUpdatesPerSecond, value);

        public bool SetAmbientColor(Color value) =>
            SetColor(ref Lighting.AmbientColor, value);

        public bool SetEmptyExtinctionColor(Color value) =>
            SetColor(ref Lighting.EmptyExtinctionRgb, value);

        public bool SetSolidExtinctionColor(Color value) =>
            SetColor(ref Lighting.SolidExtinctionRgb, value);

        public bool SetLightSafeBorder(float value)
        {
            SettingRangeAttribute range =
                SettingSchema.RangeOf<WorldLightingSettings>(nameof(WorldLightingSettings.LightSafeBorder));
            int border = Mathf.RoundToInt(Mathf.Clamp(value, range.Minimum, range.Maximum));
            if (Lighting.LightSafeBorder == border)
            {
                return false;
            }

            Lighting.LightSafeBorder = border;
            QueueSave();
            return true;
        }

        public bool SetDynamicLightSettings(float intensity, Color color)
        {
            bool changed = Set(
                ref Lighting.DynamicLightIntensity,
                intensity,
                nameof(WorldLightingSettings.DynamicLightIntensity));

            // Альфа динамического света всегда единица: это яркость источника,
            // а не полупрозрачный цвет. Общий SetColor сохранил бы присланную
            // альфу, поэтому здесь отдельная ветка, а не вызов.
            Color sanitized = new(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b),
                1f);
            if (Lighting.DynamicLightColor != sanitized)
            {
                Lighting.DynamicLightColor = sanitized;
                QueueSave();
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Записывает значение, загнав его в объявленный над полем диапазон.
        /// Возвращает <c>true</c>, только если после клампа оно отличается от
        /// текущего — иначе движение ползунка внутри одного шага порождало бы
        /// перерасчёт освещения и запись на диск.
        /// </summary>
        private bool Set(ref float field, float value, [CallerMemberName] string setter = "")
        {
            string fieldName = FieldNameOf(setter);
            SettingRangeAttribute range = SettingSchema.RangeOf<WorldLightingSettings>(fieldName);
            float clamped = Mathf.Clamp(value, range.Minimum, range.Maximum);
            if (Mathf.Approximately(field, clamped))
            {
                return false;
            }

            field = clamped;
            QueueSave();
            return true;
        }

        private bool Set(ref bool field, bool value)
        {
            if (field == value)
            {
                return false;
            }

            field = value;
            QueueSave();
            return true;
        }

        private bool SetColor(ref Color field, Color value)
        {
            // Отрицательная компонента — это отрицательная энергия: решатель
            // каскадов на ней расходится, а не просто темнеет.
            Color sanitized = new(
                Mathf.Max(0f, value.r),
                Mathf.Max(0f, value.g),
                Mathf.Max(0f, value.b),
                Mathf.Max(0f, value.a));
            if (field == sanitized)
            {
                return false;
            }

            field = sanitized;
            QueueSave();
            return true;
        }

        /// <summary>
        /// Имя поля секции по имени сеттера: <c>SetAmbientIntensity</c> →
        /// <c>AmbientIntensity</c>. Несовпадение падает в
        /// <see cref="SettingSchema.RangeOf{TSection}"/> при первом же вызове,
        /// а не показывает игроку неверный диапазон.
        /// </summary>
        private static string FieldNameOf(string setter)
        {
            const string prefix = "Set";
            return setter.StartsWith(prefix, StringComparison.Ordinal)
                ? setter[prefix.Length..]
                : setter;
        }
    }
}
