#ifndef KERN_TERRAIN_CELL_FORMAT_INCLUDED
#define KERN_TERRAIN_CELL_FORMAT_INCLUDED

// ═══ ФОРМАТ ТЕРРЕЙНА НА GPU — только данные ══════════════════════════════
//
// Источник правды для всех упакованных данных террейна: структуры, буферы,
// глобалы, позиции и маски битов. Ни одной функции — доступ к полям в
// TerrainCellData.hlsl (и декодеры вершины в TerrainLightingData.hlsl,
// TerrainSampling.hlsl, TerrainDecals.hlsl) берёт сдвиги только отсюда.
// C#-зеркало констант: Assets/Scripts/World/Terrain/GPU/TerrainCellFormat.cs
// (TerrainCellFormatMirrorTests сверяет его с этим файлом); упаковка —
// TerrainCellData.cs.
//
// ═══ КАРТА: ЯВНОЕ И НЕЯВНОЕ ══════════════════════════════════════════════
//
// ЯВНОЕ — лежит в памяти.
//
//   на клетку, 8 бит
//     тип            8
//
//   на тип, 128 бит × 256 — строка TerrainTypeRow
//     из cells.json (BlockDefinition, имена и порядок те же):
//       drawLayer 1 (бит фона), outline 3, textureAnchor 1,
//       animationType 4, frameAnimationSpeed 16, animationPalette 3,
//       decalTextureIndex 8, reliefGroup 8, glow 8
//     из текстуры: slot 3, x, y, w, h кадра 4 × 12, frameCount 8,
//       opaqueOwn 1, opaqueAny 1
//     с сервера: tileGroup 8
//
//   на кадр
//     дескрипторы автотайла   256 × 8 бит
//     размер атласа по слоту  8 × float4 (_TerrainAtlasN_TexelSize)
//     искажение               8 × float4, режим, 2 зерна рёбер
//     atlas info/effects for decal texture indices
//     окно: размер кольца, начало, смещение вида, запас носителя
//
// НЕЯВНОЕ — шейдер считает на лету, нигде не хранится. Каждое — функция
// одного поля строки или факт текстуры; ни одно поле cells.json не
// выводится из другого.
//
//   из строки типа
//     прямоугольник в UV = пиксели × тексель атласа слота
//     размер тайла       = 32 / размер атласа слота
//     высота кадра       = h / размер тайла (1 без текстуры)
//     есть ли текстура   = w > 0
//     фон                = drawLayer Background или Underlay: лежит сам на себе
//     что под клеткой    = фон — он сам, передний план — подложка
//                          (_TerrainUnderlayType, drawLayer: Underlay в cells.json)
//     светится           = glow > 0
//     цвет блика граней  = альбедо текселя (фрагмент), смешанное с белым
//     есть ли тайлгруппа = код тайлгруппы ≠ 0
//
//   из клетки и восьми соседей
//     слой               фон — фоном, остальное — передним планом
//     закрытие фона      фон под непрозрачным передним планом не рисуется
//     узлы сетки         4 угла клетки: outline клеток вокруг узла + хэш
//     дескриптор         маска соседей той же тайлгруппы → колонка, отражения
//     вариант стены      по соседним углам пака
//     UV углов           из дескриптора
//     стороны переднего плана 4 бита: какие соседи — передний план
//     кайма              стороны с той же reliefGroup + вогнутые углы
//     масса              передний план
//     декаль             текстура декали из строки типа + хэш клетки
//     фаза профиля       правила профиля анимации или палитра
//     мировые координаты из адреса квада и начала окна
//
// ВЕРШИНА (TerrainCellVertex) — тоже неявное: собирается заново на каждом
// кваде и живёт только до фрагмента.

// ─── КЛЕТКА: 8 бит, четыре в uint (младшая — индекс кольца, кратный 4) ───
//
// typedef u8 Cell;     // тип, 0 — клетка не загружена
static const uint KERN_TERRAIN_CELL_BITS = 8u;
static const uint KERN_TERRAIN_CELLS_PER_WORD = 4u;
static const uint KERN_TERRAIN_CELL_TYPE_MASK = 0xFFu;

// ─── СТРОКА ТИПА: 128 бит, таблица на 256 типов ─────────────────────────
//
// Строка 0 — тип Unloaded из cells.json, как любая другая.
struct TerrainTypeRow
{
    uint atlasXY;      // x:12 | y:12 | frameCount:8 — кадр в пикселях атласа
    uint atlasWH;      // w:12 | h:12 | reliefGroup:8
    uint look;         // slot:3 | background:1 | opaqueOwn:1 | opaqueAny:1 |
                       // textureAnchor:1 | outline:3 | :4 | animationPalette:3 |
                       // :2 | animationType:4 | decalTextureIndex:8 | :1
    uint speedGlowTile; // frameAnimationSpeed:16 (half) | glow:8 (0..255 → 0..1) |
                        // tileGroup+1:8 (0 — нет группы)
};

// Сторона текстуры клетки в текселях атласа.
static const float KERN_TERRAIN_CELL_TEXELS = 32.0;

// atlasXY, atlasWH
static const uint KERN_TERRAIN_TYPE_PIXEL_MASK = 0xFFFu;
static const uint KERN_TERRAIN_TYPE_PIXEL_HIGH_SHIFT = 12u;
static const uint KERN_TERRAIN_TYPE_BYTE_SHIFT = 24u;   // frameCount, reliefGroup, tileGroup+1

// look
static const uint KERN_TERRAIN_TYPE_SLOT_MASK = 7u;
static const uint KERN_TERRAIN_TYPE_BACKGROUND = 1u << 3;
static const uint KERN_TERRAIN_TYPE_OPAQUE_OWN = 1u << 4;
static const uint KERN_TERRAIN_TYPE_OPAQUE_ANY = 1u << 5;
static const uint KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_SHIFT = 6u;
static const uint KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_MASK = 1u;
static const uint KERN_TERRAIN_TYPE_OUTLINE_SHIFT = 7u;
static const uint KERN_TERRAIN_TYPE_OUTLINE_MASK = 7u;
static const uint KERN_TERRAIN_TYPE_ANIMATION_TYPE_SHIFT = 19u;
static const uint KERN_TERRAIN_TYPE_ANIMATION_TYPE_MASK = 15u;
static const uint KERN_TERRAIN_TYPE_ANIMATION_PALETTE_SHIFT = 14u;
static const uint KERN_TERRAIN_TYPE_ANIMATION_PALETTE_MASK = 7u;
static const uint KERN_TERRAIN_TYPE_DECAL_TEXTURE_INDEX_SHIFT = 23u;
static const uint KERN_TERRAIN_TYPE_DECAL_TEXTURE_INDEX_MASK = 255u;

// speedGlowTile
static const uint KERN_TERRAIN_TYPE_FRAME_SPEED_MASK = 0xFFFFu;
static const uint KERN_TERRAIN_TYPE_GLOW_SHIFT = 16u;
static const uint KERN_TERRAIN_TYPE_GLOW_MASK = 0xFFu;

// outline (CellOutline): край клетки; Pliant и Wavy гнутся, остальные держат узлы.
static const uint KERN_TERRAIN_OUTLINE_RIGID = 0u;
static const uint KERN_TERRAIN_OUTLINE_WAVY = 1u;
static const uint KERN_TERRAIN_OUTLINE_ROUND = 2u;
static const uint KERN_TERRAIN_OUTLINE_PLIANT = 3u;
static const uint KERN_TERRAIN_OUTLINE_WALL = 4u;
static const uint KERN_TERRAIN_OUTLINE_CORNER = 5u;
static const uint KERN_TERRAIN_OUTLINE_DOOR = 6u;

// decalTextureIndex (client GPU texture-array slice; 0 means none).

// animationType (CellAnimationType): вид анимации текстуры.
static const uint KERN_TERRAIN_ANIMATION_TYPE_NONE = 0u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_BLINKING = 1u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_SHIMMER = 2u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_RAINBOW = 3u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_MOLTEN = 4u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_FACETED = 5u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_PRISMATIC = 6u;

// Легаси тестовые типы остаются в конце.
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY2TEST = 7u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY3TEST = 8u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY4TEST = 9u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY5TEST = 10u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY6TEST = 11u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY7TEST = 12u;
static const uint KERN_TERRAIN_ANIMATION_TYPE_LEGACY8TEST = 13u;

// textureAnchor (CellTextureAnchor).
static const uint KERN_TERRAIN_TEXTURE_ANCHOR_CELL = 0u;
static const uint KERN_TERRAIN_TEXTURE_ANCHOR_WORLD = 1u;

// ─── ДЕСКРИПТОР АВТОТАЙЛА: 8 бит (TileBitmaskConverter) ──────────────────
//
// struct TileDescriptor { column:5; flipV:1; flipU:1; turn:1; };
static const uint KERN_TERRAIN_TILE_COLUMN_MASK = 0x1Fu;
static const uint KERN_TERRAIN_TILE_FLIP_V_SHIFT = 5u;
static const uint KERN_TERRAIN_TILE_FLIP_U_SHIFT = 6u;
static const uint KERN_TERRAIN_TILE_TURN_SHIFT = 7u;
static const uint KERN_TERRAIN_TILE_TRANSFORM_MASK = 0xE0u;
static const uint KERN_TERRAIN_WALL_BASE_COLUMN = 8u;

// ─── ФИКСИРОВАННАЯ ЧАСТОТА РАЗМЕЩЕНИЯ ДЕКАЛИ ────────────────────────────
static const uint KERN_TERRAIN_DECAL_PLACEMENT_PERCENT = 20u;

// ─── ДЕКАЛЬ КЛЕТКИ: код в вершине, 0 — нет декали ────────────────────────
//
// placement = (variant:4 | rotation:2 | mirror:1 | offsetX:2 | offsetY:2) + 1.
// Texture-array slice is carried in lightContourDecal.x; placement is in .w.
// All values up to 2048 are exactly representable in float16.
static const uint KERN_TERRAIN_DECAL_VARIANTS = 16u;
static const uint KERN_TERRAIN_DECAL_VARIANT_MASK = 15u;
static const uint KERN_TERRAIN_DECAL_ROTATION_SHIFT = 4u;
static const uint KERN_TERRAIN_DECAL_MIRROR_SHIFT = 6u;
static const uint KERN_TERRAIN_DECAL_OFFSET_X_SHIFT = 7u;
static const uint KERN_TERRAIN_DECAL_OFFSET_Y_SHIFT = 9u;

// ─── ФЛАГИ СВЕТА: lightContourDecal.y — целая часть, дробная — glow ──────
//
// struct LightingFlags { foregroundSides:4 (top, left, bottom, right);
//                        glow:1; physicalMass:1; };
static const uint KERN_TERRAIN_FOREGROUND_SIDES_MASK = 0x0Fu;
static const uint KERN_TERRAIN_GLOW_FLAG = 0x10u;
static const uint KERN_TERRAIN_PHYSICAL_MASS_FLAG = 0x20u;
// Доля свечения хранится умноженной на это: 0..1 → 0..0.25 (< 1).
static const float KERN_TERRAIN_GLOW_FRACTION_SCALE = 0.25;

// ─── КОНТУР: lightContourDecal.z ─────────────────────────────────────────
//
// struct Contour { roundable:1; :4; rim:5 (0 — нет, иначе маска + 1);
//                  rimCorners:4; };
static const uint KERN_TERRAIN_ROUNDABLE_CONTOUR_FLAG = 0x01u;
static const uint KERN_TERRAIN_RIM_SHIFT = 5u;
static const uint KERN_TERRAIN_RIM_MASK = 31u;
static const uint KERN_TERRAIN_RIM_SIDES_MASK = 0x0Fu;
static const uint KERN_TERRAIN_RIM_CORNERS_SHIFT = 10u;
static const uint KERN_TERRAIN_RIM_CORNERS_MASK = 15u;

// ─── КОЛОНКА ЛИСТА: worldPos.z; worldPos.w — автотайлинг 0/1 ─────────────
//
// struct PackedColumn { column:5; worldTextureAnchor:1; };
static const uint KERN_TERRAIN_COLUMN_MASK = 0x1Fu;
static const uint KERN_TERRAIN_COLUMN_WORLD_TEXTURE_ANCHOR = 1u << 5;

// ─── UV УГЛОВ: uvBits, по два бита на угол (u, v), углы 0..3 ─────────────
static const uint KERN_TERRAIN_UV_BITS_PER_CORNER = 2u;

// ─── ВЕРШИНА: что клетка отдаёт проходам ─────────────────────────────────
struct TerrainCellVertex
{
    float3 positionOS;
    float2 uv;
    float4 subAtlasRect;      // TerrainTypeRow.atlasXY/atlasWH
    float4 tileSizeUV;        // tileSize, tileSize, frameCount, frameHeight
    float4 worldPos;          // gridX, serverY, PackedColumn, autotiling
    float4 animData;          // animationType, frameAnimationSpeed, phase, reserved
    float4 packedData;        // anchored, carrierCorner.xy, reserved
    float4 lightContourDecal; // DecalTextureIndex, LightingFlags + glow, Contour, DecalCode
    float4 geometryCornersX;
    float4 geometryCornersY;
    float uvBits;
    float atlasIndex;         // < 0 — квад не рисуется
    float layer;              // 0 фон, 1 передний план
    uint reliefGroup;          // TerrainTypeRow.reliefGroup для диагностики
};

// ─── БУФЕРЫ ──────────────────────────────────────────────────────────────
StructuredBuffer<uint> _TerrainCells;               // Cell, по четыре в uint
StructuredBuffer<TerrainTypeRow> _TerrainTypes;     // 256 строк
StructuredBuffer<uint> _TerrainTileDescriptors;     // маска соседей → дескриптор, 4 на uint

// ─── СОСЕДИ: порядок битов маски автотайла (L, BL, B, BR, R, TR, T, TL) ──
static const int KERN_TERRAIN_LEFT = 0;
static const int KERN_TERRAIN_BOTTOM_LEFT = 1;
static const int KERN_TERRAIN_BOTTOM = 2;
static const int KERN_TERRAIN_BOTTOM_RIGHT = 3;
static const int KERN_TERRAIN_RIGHT = 4;
static const int KERN_TERRAIN_TOP_RIGHT = 5;
static const int KERN_TERRAIN_TOP = 6;
static const int KERN_TERRAIN_TOP_LEFT = 7;
static const int KERN_TERRAIN_NEIGHBOUR_X[8] = { -1, -1, 0, 1, 1, 1, 0, -1 };
static const int KERN_TERRAIN_NEIGHBOUR_Y[8] = { 0, -1, -1, -1, 0, 1, 1, 1 };

// ─── ГЛОБАЛЫ КАДРА ───────────────────────────────────────────────────────
// x, y — размер кольца клеток (окно и кайма по клетке с каждой стороны);
// z — размер клетки в мире.
float4 _TerrainCellGridSize;

// Debug view: 1 forces every cell to render as Unloaded without touching data.
float _TerrainForceUnloaded;

// x, y — мировая клетка локального (0, 0) окна; z, w — высота и ширина мира
// в клетках. Клетки лежат по кольцевому адресу: координата по модулю кольца.
float4 _TerrainCellOrigin;

int _TerrainDistortionEnabled;
float4 _TerrainDistortion[4];

// Подложка — тип с drawLayer: Underlay в cells.json; лежит под каждым
// передним планом.
int _TerrainUnderlayType;

// Правила декалей по серверному ID.
float4 _TerrainDecalAtlasInfo[256];
float4 _TerrainDecalEffects[256];

// Начало рисуемого окна внутри сетки. Экран рисует меш размером с видимое
// окно, поле материалов — меш всей сетки со смещением ноль.
float4 _TerrainCellViewOffset;
float2 _TerrainGeometryCarrierPaddingWorld;
int _TerrainDebugBackgroundTileIdentity;

#endif
