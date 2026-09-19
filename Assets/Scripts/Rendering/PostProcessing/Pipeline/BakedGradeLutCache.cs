#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;

// Ключ запекания творческого грейда.
//
// Прежний вариант собирал ключ из 112 векторов заново на каждом кадре:
// stackalloc, копирование пяти массивов кривых и массива оттенков
// квалификатора, затем поэлементное сравнение — и всё только ради ответа
// «менялся ли грейд». Этот ответ уже есть: PostProcessRuntimeState.SetColorGrade
// сравнивает снимок по содержимому и увеличивает поколение конвейера ровно
// тогда, когда содержимое изменилось.
//
// Остаются входы, которых в снимке нет: экспозиция, контраст, насыщенность и
// светофильтр приходят из VolumeComponent, мимо снимка. Их и держим явно —
// два вектора вместо ста двенадцати.
internal sealed class BakedGradeLutCache
{
    private bool _valid;
    private uint _generation;
    private Vector4 _volumeGrade;
    private Vector4 _colorFilter;

    public bool Matches(PostProcessPassData data)
    {
        // Vector4.operator== сравнивает с допуском. Для ключа нужны точные
        // значения, иначе небольшое изменение грейда потеряется.
        return _valid &&
            _generation == data.GradeGeneration &&
            _volumeGrade.Equals(VolumeGrade(data)) &&
            _colorFilter.Equals(ColorFilter(data));
    }

    public void Store(PostProcessPassData data)
    {
        _generation = data.GradeGeneration;
        _volumeGrade = VolumeGrade(data);
        _colorFilter = ColorFilter(data);
        _valid = true;
    }

    public void Invalidate()
    {
        _valid = false;
    }

    // Те же нейтральные значения, что в привязке параметров: выключенная
    // цветокоррекция обязана давать тот же ключ, что и нейтральная.
    private static Vector4 VolumeGrade(PostProcessPassData data) => new(
        data.CgActive ? data.Exposure : 0f,
        data.CgActive ? data.Contrast : 0f,
        data.CgActive ? data.Saturation : 1f,
        data.CdlSaturation);

    private static Vector4 ColorFilter(PostProcessPassData data) =>
        data.CgActive ? data.ColorFilter : (Vector4)Color.white;
}
