#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Kern.Core.Interfaces;
public interface IAtlasDescriptor
{
    Texture2D? Texture { get; }

    int Size { get; }

    bool ContainsCell(CellType cellType);

    // Текстура клетки непрозрачна во всех пикселях (всех кадрах и вариантах).
    // Неизвестно — false: фон под такой клеткой рисуется как обычно.
    bool IsFullyOpaque(CellType cellType);
}
