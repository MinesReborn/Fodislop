#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    /// <summary>
    /// Где стоит и куда смотрит камера сцены меню.
    ///
    /// Вынесено из MenuSceneryController отдельным типом, а не частью того же
    /// класса: контроллер владеет текстурами рендера и жизненным циклом, а
    /// здесь нет ни того, ни другого — только геометрия кадра. Смешанные в
    /// одном файле, эти две ответственности росли вместе и перевалили за предел
    /// размера, после чего любая правка кадрирования требовала читать заодно и
    /// работу с RenderTexture.
    ///
    /// Класс без состояния и без ссылок на сцену: на вход — доля пройденного
    /// спуска, направление на точку высадки, радиус планеты и соотношение
    /// сторон, на выход — положение и поворот. Это же делает его проверяемым
    /// без Unity.
    /// </summary>
    internal static class MenuSceneryFraming
    {
        /// <summary>
        /// Какую долю ШИРИНЫ кадра занимает диск в обзорном положении.
        ///
        /// Выведено из макета visual/fodinae-ui-lab: --planet-disc: 860px при
        /// вьюпорте 1440 (правило действует от 900 до 1599). Доля высоты для
        /// этого не годится: макет задаёт размер в пикселях от ширины, и на
        /// широком мониторе привязка к высоте раздувала бы планету.
        /// </summary>
        private const float RestDiscWidthFraction = 860f / 1440f;

        /// <summary>
        /// Где стоит центр планеты по ширине кадра в обзорном положении.
        ///
        /// Тоже из макета: контейнер шириной disc * 1.2 стоит на right: -146px,
        /// диск центрирован в нём, значит центр диска отстоит от правого края
        /// на 146 + 516 - 860 = 370px. При вьюпорте 1440 это 1 - 370/1440.
        /// Диск при этом свисает за край примерно на 4% ширины — тот самый
        /// подрез, ради которого планета и «не помещается».
        /// </summary>
        private const float RestCentreFraction = 1f - (370f / 1440f);

        /// <summary>
        /// Ближе этого камера в обзорном положении не подходит.
        ///
        /// Дистанция считается из соотношения сторон, а на очень узком кадре
        /// требуемая доля ширины загоняла бы камеру внутрь сферы.
        /// </summary>
        private const float MinRestDistanceInRadii = 1.6f;

        /// <summary>
        /// Во сколько раз планета крупнее на подлёте.
        ///
        /// Из макета: в состояниях loading и descent там стоит ровно
        /// transform: scale(1.18) без изменения угла обзора. При постоянном FOV
        /// та же кратность получается делением дистанции, поэтому множитель
        /// хранится как есть, а не пересчитанный в радиусы: обзорная дистанция
        /// зависит от соотношения сторон, и зашитое число радиусов
        /// рассогласовалось бы с ней на любом другом окне.
        /// </summary>
        private const float DescentZoom = 1.18f;

        /// <summary>
        /// Насколько путь камеры выгибается влево. Ноль превратил бы облёт
        /// обратно в подъезд по прямой.
        /// </summary>
        private const float SweepDegrees = 38f;

        /// <summary>Угол обзора камеры сцены. Постоянен на всём спуске.</summary>
        public const float FieldOfView = 36f;

        /// <summary>Положение и поворот камеры в локальных координатах сцены.</summary>
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

        /// <summary>
        /// Кадрирование спуска: камера подъезжает от обзорной точки к точке
        /// высадки. Прогресс — доля пройденной загрузки, 0 = обзор, 1 = вплотную.
        ///
        /// Планету при этом никто не вращает: точка высадки закреплена за
        /// поверхностью, и разворачивать шар под камеру означало бы, что метка
        /// на поверхности переезжает вместе с ним. Двигается камера — как и
        /// должно быть при подлёте.
        /// </summary>
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

        /// <summary>
        /// Обзорная дистанция, выведенная из желаемой доли ширины кадра.
        ///
        /// Раньше она была константой 6.9, подобранной под одно соотношение
        /// сторон: доля ширины, которую занимает диск, зависит и от поля зрения,
        /// и от аспекта, поэтому на другом окне размер планеты уезжал от макета.
        /// Здесь она считается обратно из RestDiscWidthFraction — тем же
        /// способом, что и RestYaw из RestCentreFraction.
        /// </summary>
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

        /// <summary>
        /// Угол отворота камеры в обзоре, посчитанный из текущего соотношения
        /// сторон.
        ///
        /// Зашивать его числом нельзя: угол постоянен, а горизонтальное поле
        /// зрения растёт вместе с шириной кадра — на широком экране планета
        /// поехала бы к центру, на узком ушла бы за край целиком. Считаем из
        /// доли ширины, и композиция держится на любом экране.
        /// </summary>
        private static float RestYaw(float aspect)
        {
            float tanHalfVertical = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHalfHorizontal = tanHalfVertical * Mathf.Max(aspect, 0.1f);

            // Из доли ширины в нормализованную координату кадра:
            // 0.5 — центр, 1 — правый край.
            float normalized = (RestCentreFraction * 2f) - 1f;

            return Mathf.Atan(normalized * tanHalfHorizontal) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Лёгкий крен камеры в обзорном положении.
        ///
        /// Наклон строго нулевой делает кадр выстроенным по линейке и выдаёт
        /// постановку; пара градусов в обзоре снимает эту нарочитость. Крен
        /// не зависит от соотношения сторон (это поворот вокруг оси взгляда,
        /// а не смещение центра) и сходится к нулю на подлёте, чтобы точка
        /// высадки оставалась ровной.
        /// </summary>
        private const float RestRoll = -1.8f;
    }
}
