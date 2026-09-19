#nullable enable

using UnityEngine;

namespace Kern.UI;
internal static class MenuSceneryFraming
{
    private const float RestDiscWidthFraction = 860f / 1440f;

    private const float RestCentreFraction = 1f - (370f / 1440f);

    private const float MinRestDistanceInRadii = 1.6f;

    private const float DescentZoom = 1.18f;

    private const float SweepDegrees = 38f;

    public const float FieldOfView = 36f;

    internal readonly struct Placement
    {
        public Placement(Vector3 localPosition, Quaternion localRotation)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public Vector3 LocalPosition { get; }

        public Quaternion LocalRotation { get; }
    }

    public static Placement Solve(
        float progress,
        Vector3 landingLocalDirection,
        float planetRadius,
        float aspect)
    {
        float t = Mathf.Clamp01(progress);

        // Сглаживание на концах: линейный подъезд читается как рывок на
        // старте и обрыв на финише.
        float eased = t * t * (3f - (2f * t));

        Vector3 restDirection = Vector3.back;
        Vector3 landingDirection = landingLocalDirection.sqrMagnitude > 0.0001f
            ? landingLocalDirection.normalized
            : Vector3.back;

        float restDistance = RestDistance(planetRadius, aspect);

        // Ближняя точка отсчитывается от радиуса планеты, а не задаётся
        // числом: масштаб шара в сцене менялся, и зашитая дистанция
        // однажды окажется внутри поверхности.
        float closeDistance = Mathf.Max(restDistance / DescentZoom, planetRadius + 0.35f);

        // Облёт, а не подъезд по прямой.
        //
        // Точка высадки лежит почти напротив обзорной позиции — прямая дуга
        // между ними всего около 29 градусов, и движение читается как
        // простой зум. Поэтому путь выгибается влево промежуточной точкой:
        // камера сперва уходит в сторону, показывая планету сбоку, и только
        // потом заходит на точку. Это две последовательные сферические
        // интерполяции — построение Безье, перенесённое на сферу.
        Vector3 sweepMid = Quaternion.AngleAxis(-SweepDegrees, Vector3.up)
            * Vector3.Slerp(restDirection, landingDirection, 0.5f);

        Vector3 direction = Vector3.Slerp(
            Vector3.Slerp(restDirection, sweepMid, eased),
            Vector3.Slerp(sweepMid, landingDirection, eased),
            eased);

        // Дистанция идёт своей интерполяцией: если гнать её тем же Slerp по
        // векторам, скорость подхода зависит от кривизны дуги и на выгибе
        // камера подтормаживает.
        Vector3 local = direction * Mathf.Lerp(restDistance, closeDistance, eased);

        // В обзоре камера смотрит мимо планеты — тем и достигается её
        // положение справа. К точке высадки она доворачивается точно на
        // центр, иначе на подлёте цель уезжала бы за край кадра.
        Quaternion aimAtCentre = Quaternion.LookRotation(-local.normalized, Vector3.up);
        Quaternion rotation = aimAtCentre * Quaternion.Euler(
            0f,
            Mathf.Lerp(-RestYaw(aspect), 0f, eased),
            Mathf.Lerp(RestRoll, 0f, eased));

        return new Placement(local, rotation);
    }

    public static float RestDistance(float planetRadius, float aspect)
    {
        float tanHalfVertical = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
        float safeAspect = Mathf.Max(aspect, 0.1f);

        // Диск занимает 2r из ширины кадра 2 * d * tan(halfV) * aspect,
        // отсюда d = r / (доля * tan(halfV) * aspect).
        float distance = planetRadius
            / Mathf.Max(RestDiscWidthFraction * tanHalfVertical * safeAspect, 1e-4f);

        return Mathf.Max(distance, planetRadius * MinRestDistanceInRadii);
    }

    private static float RestYaw(float aspect)
    {
        float tanHalfVertical = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHalfHorizontal = tanHalfVertical * Mathf.Max(aspect, 0.1f);

        // Из доли ширины в нормализованную координату кадра:
        // 0.5 — центр, 1 — правый край.
        float normalized = (RestCentreFraction * 2f) - 1f;

        return Mathf.Atan(normalized * tanHalfHorizontal) * Mathf.Rad2Deg;
    }

    private const float RestRoll = -1.8f;
}
