#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

/// <summary>Тип клетки из cells.json: каждое поле — один ключ.</summary>
///
/// Имена и порядок совпадают с ключами cells.json: проходимость, задержка
/// движения, слой, контур и визуальные параметры. Виртуальное
/// (тень и масса — от переднего плана, цвет блика — из альбедо) считает шейдер.
public readonly record struct BlockDefinition(
    // Можно ли пройти через клетку; не зависит от слоя отрисовки.
    bool Passable,
    // Серверная задержка перемещения в миллисекундах.
    ushort MovementCooldownMs,
    // Слой отрисовки: передний план, фон или подложка под передним планом.
    CellDrawLayer DrawLayer,
    // Контур переднего плана; фон остаётся плоским и не участвует в дисторшне.
    CellOutline Outline,
    // Имя основной текстуры клетки; пустое имя означает запасной цвет.
    string TextureName,
    // Откуда координаты текстуры: своя на клетку или общая по миру.
    CellTextureAnchor TextureAnchor,
    // Имя группы клеток для автотайлинга; null означает, что клетка не участвует.
    string? TileGroup,
    // Имя текстуры декали поверх клетки; null означает, что декали нет.
    string? DecalTextureName,
    // Масса для тёмной каймы по краю: 0 — без каймы; соседи одной ненулевой
    // массы — одно тело, между ними каймы нет.
    byte ReliefGroup,
    // Полный режим визуального эффекта клетки: анимация цвета или спецматериал.
    CellAnimationType AnimationType,
    // Палитра эффекта, если выбран режим Prismatic.
    byte AnimationPalette,
    // Кадров ленты текстуры в секунду.
    float FrameAnimationSpeed,
    // Светится ли клетка.
    bool Emission,
    // Цвет клетки на карте.
    Color32 MapColor);
