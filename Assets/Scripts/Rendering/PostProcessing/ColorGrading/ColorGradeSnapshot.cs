#nullable enable

using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public readonly record struct ColorGradeSnapshot
{
    public int EnabledMask { get; init; }

    public DisplayTransform Transform { get; init; }

    public float Exposure { get; init; }

    public float Contrast { get; init; }

    public float WhitePoint { get; init; }

    public float Temperature { get; init; }

    public float Tint { get; init; }

    public Vector3 Slope { get; init; }

    public Vector3 Offset { get; init; }

    public Vector3 Power { get; init; }

    public Vector3 PrimaryLift { get; init; }

    public Vector3 PrimaryGamma { get; init; }

    public Vector3 PrimaryGain { get; init; }

    public Vector3 PrimaryOffset { get; init; }

    public Vector4 PrimaryMaster { get; init; }

    public Vector3 CdlMaster { get; init; }

    public float Vibrance { get; init; }

    public float Saturation { get; init; }

    public float CdlSaturation { get; init; }

    public float Hue { get; init; }

    public float Pivot { get; init; }
    public float Shadows { get; init; }
    public float Highlights { get; init; }
    public float Blacks { get; init; }
    public float Whites { get; init; }
    public float Toe { get; init; }
    public float Shoulder { get; init; }

    public float GreyOut { get; init; }

    public float ShoulderPower { get; init; }

    public float ToePower { get; init; }

    public float ToeStops { get; init; }

    public bool GamutCompressionEnabled { get; init; }

    public float GamutCompressionStrength { get; init; }

    public ColorGradeCurve MasterCurve { get; init; }

    public ColorGradeCurve RedCurve { get; init; }

    public ColorGradeCurve GreenCurve { get; init; }

    public ColorGradeCurve BlueCurve { get; init; }

    public ColorGradeCurve HueVsHueCurve { get; init; }

    public ColorGradeCurve HueVsSaturationCurve { get; init; }

    public ColorGradeCurve HueVsLuminanceCurve { get; init; }

    public ColorGradeCurve LuminanceVsSaturationCurve { get; init; }

    public ColorGradeCurve SaturationVsSaturationCurve { get; init; }

    public ColorGradeQualifier Qualifier { get; init; }

    public ColorGradeCubeLut? Lut { get; init; }

    public float LutIntensity { get; init; }

    public ColorGradeLutColorSpace LutColorSpace { get; init; }

    public ColorGradeSnapshot()
    {
        EnabledMask = (1 << 6) - 1;
        Contrast = 0f;
        Slope = Vector3.one;
        Offset = Vector3.zero;
        Power = Vector3.one;
        WhitePoint = PostProcessLook.Grade.WhitePoint;
        PrimaryLift = Vector3.zero;
        PrimaryGamma = Vector3.one;
        PrimaryGain = Vector3.one;
        PrimaryOffset = Vector3.zero;
        PrimaryMaster = new Vector4(0f, 1f, 1f, 0f);
        CdlMaster = new Vector3(1f, 0f, 1f);
        CdlSaturation = 1f;
        Saturation = PostProcessLook.ColorGrading.Saturation;
        Pivot = 0.5f;
        GreyOut = PostProcessLook.Grade.GreyOut;
        ShoulderPower = PostProcessLook.Grade.ShoulderPower;
        ToePower = PostProcessLook.Grade.ToePower;
        ToeStops = PostProcessLook.Grade.ToeStops;
        GamutCompressionEnabled = PostProcessLook.Grade.GamutCompressionEnabled;
        GamutCompressionStrength = PostProcessLook.Grade.GamutCompressionStrength;
        // Общие нейтральные экземпляры, а не новые. Этот конструктор вызывает
        // каждый инициализатор `new ColorGradeSnapshot { ... }` (Sanitized,
        // BlendTo, сборка снимка состояния), и десять объектов создавались,
        // чтобы тут же быть затёртыми. Кривые снимка не меняются никем:
        // редактируются только собственные кривые ColorGradeState, а
        // санитизация работает на клонах.
        MasterCurve = Neutral.Tone;
        RedCurve = Neutral.Tone;
        GreenCurve = Neutral.Tone;
        BlueCurve = Neutral.Tone;
        HueVsHueCurve = Neutral.Hue;
        HueVsSaturationCurve = Neutral.Hue;
        HueVsLuminanceCurve = Neutral.Hue;
        LuminanceVsSaturationCurve = Neutral.Range;
        SaturationVsSaturationCurve = Neutral.Range;
        Qualifier = Neutral.Qualifier;
    }

    // Отдельный класс-держатель по той же причине, что и LookDefaults ниже.
    private static class Neutral
    {
        internal static readonly ColorGradeCurve Tone = new();
        internal static readonly ColorGradeCurve Hue = new(ColorGradeCurveKind.Hue);
        internal static readonly ColorGradeCurve Range = new(ColorGradeCurveKind.Range);
        internal static readonly ColorGradeQualifier Qualifier = new();
    }

    public static ColorGradeSnapshot FromLook() => new()
    {
        Transform = PostProcessLook.Grade.Transform,
        Exposure = PostProcessLook.ColorGrading.Exposure,
        Contrast = PostProcessLook.ColorGrading.Contrast,
        WhitePoint = PostProcessLook.Grade.WhitePoint,
        Pivot = 0.5f,
        Shadows = 0f,
        Highlights = 0f,
        Blacks = 0f,
        Whites = 0f,
        Toe = 0f,
        Shoulder = 0f,
        Temperature = PostProcessLook.Grade.Temperature,
        Tint = PostProcessLook.Grade.Tint,
        Slope = PostProcessLook.Grade.Slope,
        Offset = PostProcessLook.Grade.Offset,
        Power = PostProcessLook.Grade.Power,
        PrimaryLift = Vector3.zero,
        PrimaryGamma = Vector3.one,
        PrimaryGain = Vector3.one,
        PrimaryOffset = Vector3.zero,
        PrimaryMaster = new Vector4(0f, 1f, 1f, 0f),
        Vibrance = 0f,
        Saturation = PostProcessLook.ColorGrading.Saturation,
        CdlSaturation = 1f,
        Hue = 0f,
        CdlMaster = new Vector3(1f, 0f, 1f),
        GreyOut = PostProcessLook.Grade.GreyOut,
        ShoulderPower = PostProcessLook.Grade.ShoulderPower,
        ToePower = PostProcessLook.Grade.ToePower,
        ToeStops = PostProcessLook.Grade.ToeStops,
        GamutCompressionEnabled = PostProcessLook.Grade.GamutCompressionEnabled,
        GamutCompressionStrength = PostProcessLook.Grade.GamutCompressionStrength,
        LutIntensity = 0f,
        LutColorSpace = ColorGradeLutColorSpace.LinearRec709,
    };

    public ColorGradeSnapshot BlendTo(ColorGradeSnapshot other, float weight)
    {
        float t = float.IsNaN(weight) ? 0f : Mathf.Clamp01(weight);
        if (t <= 0f)
        {
            return this;
        }

        if (t >= 1f)
        {
            return other;
        }

        return new ColorGradeSnapshot
        {
            EnabledMask = t > 0.5f ? other.EnabledMask : EnabledMask,
            Transform = t > 0.5f ? other.Transform : Transform,
            Exposure = Mathf.Lerp(Exposure, other.Exposure, t),
            Contrast = Mathf.Lerp(Contrast, other.Contrast, t),
            WhitePoint = Mathf.Lerp(WhitePoint, other.WhitePoint, t),
            Pivot = Mathf.Lerp(Pivot, other.Pivot, t),
            Shadows = Mathf.Lerp(Shadows, other.Shadows, t),
            Highlights = Mathf.Lerp(Highlights, other.Highlights, t),
            Blacks = Mathf.Lerp(Blacks, other.Blacks, t),
            Whites = Mathf.Lerp(Whites, other.Whites, t),
            Toe = Mathf.Lerp(Toe, other.Toe, t),
            Shoulder = Mathf.Lerp(Shoulder, other.Shoulder, t),
            Temperature = Mathf.Lerp(Temperature, other.Temperature, t),
            Tint = Mathf.Lerp(Tint, other.Tint, t),
            Slope = Vector3.Lerp(Slope, other.Slope, t),
            Offset = Vector3.Lerp(Offset, other.Offset, t),
            Power = Vector3.Lerp(Power, other.Power, t),
            PrimaryLift = Vector3.Lerp(PrimaryLift, other.PrimaryLift, t),
            PrimaryGamma = Vector3.Lerp(PrimaryGamma, other.PrimaryGamma, t),
            PrimaryGain = Vector3.Lerp(PrimaryGain, other.PrimaryGain, t),
            PrimaryOffset = Vector3.Lerp(PrimaryOffset, other.PrimaryOffset, t),
            PrimaryMaster = Vector4.Lerp(PrimaryMaster, other.PrimaryMaster, t),
            Vibrance = Mathf.Lerp(Vibrance, other.Vibrance, t),
            Saturation = Mathf.Lerp(Saturation, other.Saturation, t),
            CdlSaturation = Mathf.Lerp(CdlSaturation, other.CdlSaturation, t),
            Hue = Mathf.Lerp(Hue, other.Hue, t),
            CdlMaster = Vector3.Lerp(CdlMaster, other.CdlMaster, t),
            GreyOut = Mathf.Lerp(GreyOut, other.GreyOut, t),
            ShoulderPower = Mathf.Lerp(ShoulderPower, other.ShoulderPower, t),
            ToePower = Mathf.Lerp(ToePower, other.ToePower, t),
            ToeStops = Mathf.Lerp(ToeStops, other.ToeStops, t),
            GamutCompressionEnabled = t > 0.5f ? other.GamutCompressionEnabled : GamutCompressionEnabled,
            GamutCompressionStrength = Mathf.Lerp(GamutCompressionStrength, other.GamutCompressionStrength, t),
            // Ссылки, а не клоны — как и на краях t<=0 и t>=1 выше. Получатель
            // (SetColorGrade) сам клонирует при санитизации, а клон здесь
            // давал девять новых кривых на каждый кадр в переходе между зонами.
            MasterCurve = t > 0.5f ? other.MasterCurve : MasterCurve,
            RedCurve = t > 0.5f ? other.RedCurve : RedCurve,
            GreenCurve = t > 0.5f ? other.GreenCurve : GreenCurve,
            BlueCurve = t > 0.5f ? other.BlueCurve : BlueCurve,
            HueVsHueCurve = t > 0.5f ? other.HueVsHueCurve : HueVsHueCurve,
            HueVsSaturationCurve = t > 0.5f ? other.HueVsSaturationCurve : HueVsSaturationCurve,
            HueVsLuminanceCurve = t > 0.5f ? other.HueVsLuminanceCurve : HueVsLuminanceCurve,
            LuminanceVsSaturationCurve = t > 0.5f
                ? other.LuminanceVsSaturationCurve
                : LuminanceVsSaturationCurve,
            SaturationVsSaturationCurve = t > 0.5f
                ? other.SaturationVsSaturationCurve
                : SaturationVsSaturationCurve,
            Qualifier = t > 0.5f ? other.Qualifier : Qualifier,
            Lut = t > 0.5f ? other.Lut : Lut,
            LutIntensity = Mathf.Lerp(LutIntensity, other.LutIntensity, t),
            LutColorSpace = t > 0.5f ? other.LutColorSpace : LutColorSpace,
        };
    }

    // Значения по умолчанию постоянны: собирать их на каждый Sanitized() незачем.
    // Кривые-запасные отсюда только клонируются, общий экземпляр не меняется.
    // Держатель — отдельный класс: статическое поле типа Nullable<себя> внутри
    // самой структуры некоторые рантаймы отказываются загружать.
    private static class LookDefaults
    {
        internal static readonly ColorGradeSnapshot Value = FromLook();
    }

    // Равенство record struct сравнивает кривые и квалификатор по ссылке, а
    // каждый источник грейда отдаёт их свежими клонами: тот же грейд не
    // узнавался никогда. Скаляры сравниваются тем же равенством после
    // подстановки чужих ссылок, объекты — по содержимому.
    public bool ContentEquals(in ColorGradeSnapshot other)
    {
        ColorGradeSnapshot sameReferences = this with
        {
            MasterCurve = other.MasterCurve,
            RedCurve = other.RedCurve,
            GreenCurve = other.GreenCurve,
            BlueCurve = other.BlueCurve,
            HueVsHueCurve = other.HueVsHueCurve,
            HueVsSaturationCurve = other.HueVsSaturationCurve,
            HueVsLuminanceCurve = other.HueVsLuminanceCurve,
            LuminanceVsSaturationCurve = other.LuminanceVsSaturationCurve,
            SaturationVsSaturationCurve = other.SaturationVsSaturationCurve,
            Qualifier = other.Qualifier,
        };

        return sameReferences == other &&
            CurveEquals(MasterCurve, other.MasterCurve) &&
            CurveEquals(RedCurve, other.RedCurve) &&
            CurveEquals(GreenCurve, other.GreenCurve) &&
            CurveEquals(BlueCurve, other.BlueCurve) &&
            CurveEquals(HueVsHueCurve, other.HueVsHueCurve) &&
            CurveEquals(HueVsSaturationCurve, other.HueVsSaturationCurve) &&
            CurveEquals(HueVsLuminanceCurve, other.HueVsLuminanceCurve) &&
            CurveEquals(LuminanceVsSaturationCurve, other.LuminanceVsSaturationCurve) &&
            CurveEquals(SaturationVsSaturationCurve, other.SaturationVsSaturationCurve) &&
            (ReferenceEquals(Qualifier, other.Qualifier) ||
                (Qualifier != null && other.Qualifier != null && Qualifier.ContentEquals(other.Qualifier)));
    }

    private static bool CurveEquals(ColorGradeCurve? left, ColorGradeCurve? right) =>
        ReferenceEquals(left, right) ||
        (left != null && right != null && left.ContentEquals(right));

    // Общий неизменяемый экземпляр вида по умолчанию. FromLook() собирает
    // девять новых кривых и квалификатор при каждом вызове.
    public static ColorGradeSnapshot Look => LookDefaults.Value;

    // Нейтральность грейда живёт здесь, рядом с полями, а не в проходе.
    //
    // Раньше эти два списка полей стояли в PostProcessRenderPass, третий их
    // список — в ключе запекания, и все три обязаны были согласовываться
    // вручную. Они уже разошлись: ключ учитывал интерполяцию кривых, проверка
    // нейтральности — нет. Добавление поля в снимок тихо ломало либо кэш,
    // либо ранний выход прохода, и компилятор об этом молчал.

    // Входы творческого прохода (всё, что запекается в таблицу) в нейтрали:
    // композит тогда возвращает тот же цвет, и проход не нужен.
    public bool IsCreativeNeutral =>
        Temperature == 0f && Tint == 0f &&
        Slope == Vector3.one && Offset == Vector3.zero && Power == Vector3.one &&
        CdlMaster == new Vector3(1f, 0f, 1f) && CdlSaturation == 1f &&
        PrimaryLift == Vector3.zero && PrimaryGamma == Vector3.one &&
        PrimaryGain == Vector3.one && PrimaryOffset == Vector3.zero &&
        PrimaryMaster == new Vector4(0f, 1f, 1f, 0f) &&
        Vibrance == 0f && Hue == 0f &&
        Shadows == 0f && Highlights == 0f && Blacks == 0f &&
        Whites == 0f && Toe == 0f && Shoulder == 0f &&
        !Qualifier.Enabled &&
        HueVsHueCurve.IsNeutral && HueVsSaturationCurve.IsNeutral &&
        HueVsLuminanceCurve.IsNeutral && LuminanceVsSaturationCurve.IsNeutral &&
        SaturationVsSaturationCurve.IsNeutral;

    // Точечные операции прохода дисплея в нейтрали.
    public bool IsDisplayNeutral =>
        Transform == DisplayTransform.None &&
        (Lut == null || LutIntensity <= 0f) &&
        (!GamutCompressionEnabled || GamutCompressionStrength <= 0f) &&
        MasterCurve.IsNeutral && RedCurve.IsNeutral &&
        GreenCurve.IsNeutral && BlueCurve.IsNeutral;

    public ColorGradeSnapshot Sanitized() => SanitizedReusing(null);

    public ColorGradeSnapshot SanitizedReusing(ColorGradeSnapshot? previous) =>
        ColorGradeSnapshotSanitizer.Sanitize(this, LookDefaults.Value, previous);
}
