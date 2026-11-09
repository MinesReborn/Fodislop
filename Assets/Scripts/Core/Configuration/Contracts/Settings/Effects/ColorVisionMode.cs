#nullable enable

namespace Kern.Core;

// Режим для дальтоников: какой вид цветового зрения корректирует выходной проход
public enum ColorVisionMode
{
    Off = 0,
    Protanopia = 1,
    Deuteranopia = 2,
    Tritanopia = 3,
}
