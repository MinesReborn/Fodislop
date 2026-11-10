#include <filesystem>
#include <fstream>
// Клетка кодируется здесь по раскладке TerrainCellFormat.hlsl, а не рабочим
// C#: так проверяется, что шейдер читает ту же раскладку. Клетка — байт,
// по четыре в слове, как в TerrainCellBuffers.
static const int RingSize = 4;
static void setCell(int gridX, int unityY, uint bits)
{
    int x = ((gridX % RingSize) + RingSize) % RingSize;
    int y = ((unityY % RingSize) + RingSize) % RingSize;
    uint index = (uint)(y * RingSize + x);
    uint& word = _TerrainCells.data.at(index / KERN_TERRAIN_CELLS_PER_WORD);
    uint shift = (index % KERN_TERRAIN_CELLS_PER_WORD) * KERN_TERRAIN_CELL_BITS;
    uint mask = KERN_TERRAIN_CELL_TYPE_MASK;
    word = (word & ~(mask << shift)) | ((bits & mask) << shift);
}
// Передний план (бит фона 0) лежит на подложке, тип 2 этого сценария.
static const uint onUnderlay = KERN_TERRAIN_OUTLINE_PLIANT << KERN_TERRAIN_TYPE_OUTLINE_SHIFT;
static const uint wavyOnUnderlay =
    (KERN_TERRAIN_OUTLINE_WAVY << KERN_TERRAIN_TYPE_OUTLINE_SHIFT) |
    (KERN_TERRAIN_TEXTURE_ANCHOR_WORLD << KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_SHIFT);
static void resetCells()
{
    _TerrainCells.reset(RingSize * RingSize / KERN_TERRAIN_CELLS_PER_WORD);
    _TerrainTypes.reset(256);
    _TerrainCellGridSize = {RingSize, RingSize, 1, 0};
    _TerrainCellOrigin = {0, 0, 1, 1};
    _TerrainCellViewOffset = {0, 0, 0, 0};
    _TerrainDistortionEnabled = 0;
    _TerrainUnderlayType = 2;
    _TerrainTileDescriptors.reset(64);
    // Передний план — тип 1 (слот атласа 0, искажает), под ним подложка: типы
    // разные, поэтому фон рисуется под каждой клеткой.
    for (int y = 0; y < RingSize; ++y) for (int x = 0; x < RingSize; ++x) setCell(x, y, 1u);
    _TerrainTypes.data[1].look = wavyOnUnderlay;
}

// Independent triangle rasterization, followed by the production fragment mask.
// Sample on both sides of every logical pixel center: checking centers alone
// cannot distinguish a staircase from an ordinary diagonal edge.
float cross2(float2 a, float2 b) { return a.x*b.y-a.y*b.x; }
bool triangle(float2 p, float2 a, float2 b, float2 c, float3& weights)
{
    float area = cross2(b-a,c-a);
    weights.y = cross2(p-a,c-a)/area;
    weights.z = cross2(b-a,p-a)/area;
    weights.x = 1-weights.y-weights.z;
    return weights.x >= -1e-6f && weights.y >= -1e-6f && weights.z >= -1e-6f;
}
// Independent final-grid oracle: round integer half-steps to the even pixel.
static float4 expectedPixelCorners(float4 corners)
{
    for (int i = 0; i < 4; ++i)
        corners[i] = std::nearbyint(corners[i] * 32.0f) / 32.0f;
    return corners;
}
bool oracle(float2 p, float4 xs, float4 ys)
{
    // All generated quadrilaterals are convex and counter-clockwise.
    for (int i=0; i<4; ++i)
    {
        int j=(i+1)%4;
        if (cross2(float2{xs[j]-xs[i],ys[j]-ys[i]},p-float2{xs[i],ys[i]}) < -1e-6f)
            return false;
    }
    return true;
}
float oracleSignedDistance(float2 p, float4 xs, float4 ys)
{
    float nearestSquared = 1.0e20f;
    for (int side = 0; side < 4; ++side)
    {
        int next = (side + 1) & 3;
        float2 start{xs[side], ys[side]};
        float2 edge{xs[next] - xs[side], ys[next] - ys[side]};
        float t = std::clamp(
            ((p.x - start.x) * edge.x + (p.y - start.y) * edge.y) /
                (edge.x * edge.x + edge.y * edge.y),
            0.0f, 1.0f);
        float2 delta = p - (start + edge * t);
        nearestSquared = std::min(nearestSquared, delta.x * delta.x + delta.y * delta.y);
    }
    float distance = std::sqrt(nearestSquared);
    return oracle(p, xs, ys) ? distance : -distance;
}
bool rendered(float2 p, const TerrainCellVertex* vertices, float4 xs, float4 ys)
{
    for(int tri=0;tri<2;++tri)
    {
        int a=0,b=tri+1,c=tri+2;
        float3 w;
        if(!triangle(p,vertices[a].positionOS.xy,vertices[b].positionOS.xy,vertices[c].positionOS.xy,w)) continue;
        float2 sample=vertices[a].packedData.yz*w.x+vertices[b].packedData.yz*w.y+vertices[c].packedData.yz*w.z;
        return TerrainGeometryCoverage(sample,xs,ys,1)>.5f;
    }
    return false;
}
bool expectedRendered(float2 p, const TerrainCellVertex* vertices, float4 xs, float4 ys)
{
    for(int tri=0;tri<2;++tri)
    {
        int a=0,b=tri+1,c=tri+2;
        float3 w;
        if(!triangle(p,vertices[a].positionOS.xy,vertices[b].positionOS.xy,vertices[c].positionOS.xy,w)) continue;
        float2 sample=vertices[a].packedData.yz*w.x+vertices[b].packedData.yz*w.y+vertices[c].packedData.yz*w.z;
        return oracle(sample,xs,ys);
    }
    return false;
}

// Затенение не имеет права гасить поверхность в ноль: полная занятость
// вокруг обязана оставить ровно пол, иначе тень читается дырой. Число
// совпадает с оригиналом (1 - z² при z = 0.7).
static void checkAmbientOcclusionFloor()
{
    _KernFieldRowsTopDown=0;
    _TerrainAmbientOcclusionStrength=1;
    _TerrainAmbientOcclusionFloor=0.51f;
    Texture occupied;
    occupied.reset(64,64);
    for(int i=0;i<64*64;++i) occupied.data[i].x=1;
    _WorldAmbientOcclusionTexture.generate(std::move(occupied));
    float darkest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(darkest-0.51f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion does not stop at the floor: " + std::to_string(darkest));

    Texture empty;
    empty.reset(64,64);
    for(int i=0;i<64*64;++i) empty.data[i].x=0;
    _WorldAmbientOcclusionTexture.generate(std::move(empty));
    float brightest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(brightest-1.0f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion darkens an empty neighbourhood");
}

// Упаковка слова контура — та же арифметика, что в TerrainLightingData.Pack:
// бит 0 — флаг контура, 1-4 — диагональные соседи, 5+ — код каймы. Хендмейдный
// rimCode*32 проверял только шейдер и молчал о том, переживает ли код
// соседство с занятыми младшими битами.
static float packContourFull(int rimCode, int contourFlags, int foregroundDiagonal)
{
    return float(contourFlags + foregroundDiagonal * 2 + rimCode * 32);
}

static float packContour(int rimCode) { return packContourFull(rimCode, 0, 0); }

// Код каймы из маски своих соседей — как это делает TerrainQuadBuilder:
// маска + 1, ноль оставлен под «клетка без каймы».
static float packRimMask(int rimMask, int contourFlags, int foregroundDiagonal)
{
    return packContourFull((rimMask & 0x0F) + 1, contourFlags, foregroundDiagonal);
}

// Канонические углы: несмещённая клетка.
static float rim(float2 p, float packed)
{
    TerrainSurfaceInputs surface{};
    surface.cellSample=p;
    surface.cornersX=float4{0,1,1,0};
    surface.cornersY=float4{0,0,1,1};
    surface.packedContour=packed;
    return TerrainRim(surface);
}

// Смещённая клетка: падение обязано растянуться на её реальные границы, а
// не упереться в дно на всём выступе.
static float displacedRim(float2 p, float4 xs, float4 ys, float packed)
{
    TerrainSurfaceInputs surface{};
    surface.cellSample=p;
    surface.cornersX=xs;
    surface.cornersY=ys;
    surface.packedContour=packed;
    return TerrainRim(surface);
}

// Кайма: три стороны, верх не затемняется никогда, дно падения 0.125, и
// координата несущего прямоугольника не должна выводить её из диапазона.
// Круглый блок: координата контура — UV тайла, соседи-блоки — флаги света.
static float roundRim(float2 p, float packed, int foregroundSides)
{
    TerrainSurfaceInputs surface{};
    surface.cellSample=p;
    surface.contourUV=p;
    surface.cornersX=float4{0,1,1,0};
    surface.cornersY=float4{0,0,1,1};
    surface.packedContour=packed;
    surface.packedLightingFlags=(float)foregroundSides;
    return TerrainRim(surface);
}

// Кайма круглого блока идёт по дуге силуэта, а не по сторонам клетки: точка
// у дуги на диагонали далеко от сторон, но обязана темнеть; сторона той же
// поверхности, к которой силуэт прирастает, края не имеет.
static void checkRoundRim()
{
    const float saved = _RimDistanceScale;
    _RimDistanceScale = 8.0f;
    const int roundable = 1;
    float allForeign = packRimMask(0, roundable, 0);
    if(roundRim(float2{0.846f, 0.846f}, allForeign, 0) > 0.55f)
        throw std::runtime_error("Rim does not follow the round block silhouette");
    if(roundRim(float2{0.5f, 0.5f}, allForeign, 0) < 0.99f)
        throw std::runtime_error("Rim darkens the middle of a round block");
    float sameTop = packRimMask(1, roundable, 0);
    if(roundRim(float2{0.5f, 0.98f}, sameTop, 1) < 0.99f)
        throw std::runtime_error("Rim darkens a round block side merged with the same surface");
    float foreignRoundTop = roundRim(float2{0.5f, 0.98f}, allForeign, 0);
    if(foreignRoundTop > 0.625f)
        throw std::runtime_error("Rim missing on a round block side facing a foreign surface: " +
            std::to_string(foreignRoundTop));
    _RimDistanceScale = saved;
}

static void checkRim()
{
    _TerrainRimEnabled = 1.0f;
    const float2 nearTop{0.5f, 0.99f};
    const float2 nearBottom{0.5f, 0.01f};
    const float2 nearLeft{0.01f, 0.5f};
    const float2 nearRight{0.99f, 0.5f};
    const float2 centre{0.5f, 0.5f};

    // Нет массы каймы — кайма не трогает ничего.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(rim(p, packContour(0)) != 1.0f)
            throw std::runtime_error("Rim darkened a cell without a rim mass");

    // Вся семья вокруг: код 16 — маска 15 со сдвигом.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(rim(p, packContour(16)) != 1.0f)
            throw std::runtime_error("Rim darkened the interior of a block mass");

    // Середина клетки не трогается ни при каких чужих сторонах.
    if(rim(centre, packContour(1)) < 0.99f)
        throw std::runtime_error("Rim reached the centre of the cell");

    // Все четыре соседа чужие: маска 0, код 1. Низ, лево и право темнеют,
    // верх обязан остаться нетронутым.
    float allForeign = packContour(1);
    for(float2 p : {nearTop, nearBottom, nearLeft, nearRight})
    {
        float v = rim(p, allForeign);
        if(v > 0.55f)
            throw std::runtime_error("Rim missing on a foreign side");
        if(v <= 0.1f)
            throw std::runtime_error("Rim is darker than the original scale allows");
    }

    // Ровно одна сторона чужая — темнеет только её сектор.
    for(int side = 0; side < 4; ++side)
    {
        int mask = (~(1 << side)) & 0x0F;
        float one = packContour(mask + 1);
        const float2 probes[4] = {nearTop, nearLeft, nearBottom, nearRight};
        for(int other = 0; other < 4; ++other)
        {
            float v = rim(probes[other], one);
            bool expectDark = other == side;
            if(expectDark && v > 0.55f)
                throw std::runtime_error("Rim missing on the single foreign side");
            // Не ровно единица: квантование сдвигает середину клетки на
            // полтексела, и противоположная грань даёт 0.9985. Утечкой это
            // не является, а вот заметное затемнение — является.
            if(!expectDark && v < 0.99f)
                throw std::runtime_error("Rim leaked onto a side of the same family");
        }
    }
    if(rim(centre, allForeign) < 0.99f)
        throw std::runtime_error("Rim reached the centre of the cell");

    // Смещённая клетка. Выступающая грань не имеет права темнеть сильнее
    // такой же грани ровной клетки: падение нормируется по границам
    // полигона, а не зажимается в единичный квадрат.
    {
        float4 xs{-0.1875f, 1.1875f, 1.1875f, -0.1875f};
        float4 ys{-0.1875f, -0.1875f, 1.1875f, 1.1875f};
        float2 span{1.375f, 1.375f};
        float2 polygonCentre{-0.1875f + span.x * 0.5f, -0.1875f + span.y * 0.5f};
        // Четверть высоты полигона: у самого края оба варианта упираются в
        // дно и разницы не видно, а здесь зажим уже расходится с нормировкой.
        float2 quarterUp{polygonCentre.x, -0.1875f + span.y * 0.25f};

        float flat = rim(float2{0.5f, 0.25f}, allForeign);
        float displaced = displacedRim(quarterUp, xs, ys, allForeign);
        // The vertices are snapped once. Fragment positions remain continuous,
        // so the expected distances are exactly a quarter of each side span.
        auto sideFalloff=[](float distance)
        {
            float s=std::clamp(1.0f-2.0f*distance,0.0f,1.0f);
            float fall=1.0f-0.5f*s;
            return fall;
        };
        if(std::fabs(flat - sideFalloff(.25f)) > 1e-4f ||
            std::fabs(displaced - sideFalloff(span.y*.25f)) > 1e-4f)
        {
            throw std::runtime_error(
                "Rim does not follow the displaced edge distance: flat=" +
                std::to_string(flat) + " displaced=" + std::to_string(displaced));
        }

        if(displacedRim(polygonCentre, xs, ys, allForeign) < 0.99f)
            throw std::runtime_error("Rim reached the centre of a displaced cell");
    }

    // Кайма обязана тайлиться. Вдоль чужой грани значение постоянно по всей
    // длине, включая углы клетки: пока затухание резалось диагональю сектора,
    // полоса сходила на нет у каждого стыка с соседней клеткой, и граница
    // массива читалась пунктиром.
    {
        float bottomForeign = packContour((((~4) & 0x0F)) + 1);
        float reference = rim(float2{0.5f, 0.02f}, bottomForeign);
        for(int i = 0; i <= 20; ++i)
        {
            // Compare tile-pixel centers: exact UV 0/1 lie beyond the center
            // of the first/last texel and are correctly outside the segment.
            float along = (i + 0.5f) / 21.0f;
            float v = rim(float2{along, 0.02f}, bottomForeign);
            if(std::fabs(v - reference) > 1e-4f)
                throw std::runtime_error(
                    "Rim is not constant along a foreign edge: at " +
                    std::to_string(along) + " it is " + std::to_string(v) +
                    " against " + std::to_string(reference));
        }
    }

    // Угол двух чужих граней темнее каждой из них по отдельности.
    {
        float bottomOnly = packContour((((~4) & 0x0F)) + 1);
        float leftOnly = packContour((((~2) & 0x0F)) + 1);
        float both = packContour((((~6) & 0x0F)) + 1);
        float2 corner{0.02f, 0.02f};
        float a = rim(corner, bottomOnly);
        float b = rim(corner, leftOnly);
        float c = rim(corner, both);
        if(c > a || c > b || c >= 0.99f)
            throw std::runtime_error("A corner of two foreign edges does not preserve either edge bevel");
    }

    // Полная упаковка: младшие биты заняты, код обязан дойти целым.
    //
    // Это и есть шов, на котором кайма могла бы включаться через клетку.
    // Флаги контура и маска диагональных соседей меняются от клетки к клетке,
    // и если бы код каймы стоял не на своём месте, кайма то появлялась бы,
    // то исчезала по соседству, которое к ней отношения не имеет. Здесь
    // прогоняются все 16 масок против всех 16 масок диагональных соседей.
    // Флаг контура сюда не входит: круглый блок кладёт кайму по своему
    // силуэту, и это проверяет checkRoundRim.
    {
        const float2 sideProbes[4] = {nearTop, nearLeft, nearBottom, nearRight};
        for(int rimMask = 0; rimMask <= 0x0F; ++rimMask)
        {
            float clean = packRimMask(rimMask, 0, 0);
            for(int foregroundDiagonal = 0; foregroundDiagonal <= 0x0F; ++foregroundDiagonal)
            {
                float packed = packRimMask(rimMask, 0, foregroundDiagonal);
                for(int side = 0; side < 4; ++side)
                {
                    float expected = rim(sideProbes[side], clean);
                    float actual = rim(sideProbes[side], packed);
                    if(std::fabs(expected - actual) > 1e-5f)
                        throw std::runtime_error(
                            "Rim code does not survive the packed word: mask=" +
                            std::to_string(rimMask) + " diagonal=" +
                            std::to_string(foregroundDiagonal) + " side=" +
                            std::to_string(side) + " expected=" +
                            std::to_string(expected) + " actual=" +
                            std::to_string(actual));
                }

                // И содержательно: своя сторона не темнеет, чужая темнеет.
                for(int side = 0; side < 4; ++side)
                {
                    bool sameFamily = (rimMask & (1 << side)) != 0;
                    float v = rim(sideProbes[side], packed);
                    if(sameFamily && v < 0.99f)
                        throw std::runtime_error(
                            "Rim darkens a side whose neighbour is the same family: mask=" +
                            std::to_string(rimMask) + " side=" + std::to_string(side));
                    if(!sameFamily && v > 0.55f)
                        throw std::runtime_error(
                            "Rim missing on a foreign side: mask=" +
                            std::to_string(rimMask) + " side=" + std::to_string(side));
                }
            }
        }
    }

    // Выключатель снимает кайму целиком.
    _TerrainRimEnabled = 0.0f;
    if(rim(nearBottom, packContour(1)) != 1.0f)
        throw std::runtime_error("Disabled rim still darkens the frame");
    _TerrainRimEnabled = 1.0f;

    // Вырожденные углы: путь вершин без геометрии кладёт нули, и нормировка
    // по размаху обязана это пережить. Пока размах зажимался в эпсилон,
    // клеточная координата становилась (1,1), и оверлей дверей гасился
    // целиком в 1/8 яркости.
    {
        float4 zero{0, 0, 0, 0};
        for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
            if(displacedRim(p, zero, zero, allForeign) != 1.0f)
                throw std::runtime_error("Degenerate polygon bounds still darken the cell");
    }

    // Координата несущего прямоугольника выходит за клетку у смещённых
    // клеток; кайма обязана остаться в своём диапазоне.
    for(float over : {-0.1875f, 1.1875f})
        for(float along : {-0.1875f, 0.5f, 1.1875f})
        {
            float a = rim(float2{along, over}, allForeign);
            float b = rim(float2{over, along}, allForeign);
            // Нижняя граница — квадрат дна одной грани: на углу двух чужих
            // граней множители перемножаются, и 0.125² законны. Смысл
            // проверки в том, что координата несущего прямоугольника не
            // выбрасывает результат за пределы вовсе.
            if(a < 0.015f || a > 1.0f || b < 0.015f || b > 1.0f)
                throw std::runtime_error("Rim leaves its range on an anchored carrier sample");
        }
}

void checkAo()
{
    _KernFieldRowsTopDown=0;
    _TerrainAmbientOcclusionStrength=1;
    // Тот же пол, что в TerrainLook: множитель обязан останавливаться на нём.
    _TerrainAmbientOcclusionFloor=0.51f;
    for(int density : {8,16,32,64})
    {
        float samples[2];
        for(int shape=0;shape<2;++shape)
        {
            float4 xs={0,1,shape ? .5f : 1.f,0},ys={0,0,1,1};
            Texture field;
            field.reset(8*density,8*density);
            for(int y=0;y<field.height;++y) for(int x=0;x<field.width;++x)
            {
                float2 p={(x+.5f)/density,(y+.5f)/density};
                float signedDistance = TerrainGeometrySignedDistance(
                    p-float2{3,3},xs,ys);
                float contact = 1.0f-smoothstep(0.0f,0.5f,max(-signedDistance,0.0f));
                float expectedDistance = oracleSignedDistance(p-float2{3,3},xs,ys);
                float expectedContact = 1.0f-smoothstep(
                    0.0f,0.5f,max(-expectedDistance,0.0f));
                if (std::fabs(contact - expectedContact) > 1e-4f)
                    throw std::runtime_error("AO contact field differs from independent polygon-distance oracle");
                field.data[y*field.width+x].x = contact;
            }
            _WorldAmbientOcclusionTexture.generate(std::move(field));
            samples[shape]=KernSampleTerrainAmbientOcclusion(float2{4.0625f,3.875f},float4{0,0,8,8});
            float far=KernSampleTerrainAmbientOcclusion(float2{6,6},float4{0,0,8,8});
            if(far!=0) throw std::runtime_error("Isolated block AO leaks beyond the contact neighbourhood");
            float mass=KernTerrainAmbientOcclusionMultiplier(32,float2{4.0625f,3.875f},float4{0,0,8,8});
            if(mass!=1) throw std::runtime_error("Physical foreground self-darkens");
        }
        float difference=samples[0]-samples[1];
        if(difference<.025f)
            throw std::runtime_error("AO lost the sloped silhouette: square="+std::to_string(samples[0])+" slope="+std::to_string(samples[1]));
    }
    std::cout << "AO field sampling passed: shape sensitivity, density 8/16/32/64, empty distance and foreground receiver.\n";
}

void checkContinuousFieldSilhouette()
{
    auto checkEdge=[](float4 xs,float4 ys,float2 midpoint,float2 outwardNormal,
                      float sampleRange,const char* label)
    {
        float previous=1.0f;
        int fractionalSamples=0;
        for(int i=0;i<=16;++i)
        {
            float signedOffset=sampleRange*(1.0f-i/8.0f);
            float2 p=midpoint-outwardNormal*signedOffset;
            float actualDistance=TerrainGeometrySignedDistance(p,xs,ys);
            float expectedDistance=oracleSignedDistance(p,xs,ys);
            if(std::fabs(actualDistance-expectedDistance)>1e-4f)
                throw std::runtime_error(
                    std::string("AO ")+label+" distance disagrees with independent polygon oracle");

            float actualExterior=std::max(-actualDistance,0.0f);
            float expectedExterior=std::max(-expectedDistance,0.0f);
            float actual=1.0f-smoothstep(0.0f,_TerrainAmbientOcclusionDistance,actualExterior);
            float expected=1.0f-smoothstep(0.0f,_TerrainAmbientOcclusionDistance,expectedExterior);
            if(std::fabs(actual-expected)>1e-4f)
                throw std::runtime_error(
                    std::string("AO ")+label+" contact disagrees with independent distance oracle");
            if(actual>previous+1e-5f)
                throw std::runtime_error(std::string("AO ")+label+" contact edge is not monotonic");
            if(actual>0.0f && actual<1.0f)
                ++fractionalSamples;
            previous=actual;
        }
        if(fractionalSamples<2)
            throw std::runtime_error(std::string("AO ")+label+" contact falloff is not continuous");
    };

    float4 squareX={0,1,1,0};
    float4 squareY={0,0,1,1};
    checkEdge(squareX,squareY,float2{1,.5f},float2{1,0},
        _TerrainAmbientOcclusionDistance*2.0f,"axis-aligned");

    // A diagonal uses Euclidean signed distance, so its falloff remains
    // isotropic instead of being stretched by the larger coordinate derivative.
    float4 diamondX={.5f,1,.5f,0};
    float4 diamondY={0,.5f,1,.5f};
    checkEdge(diamondX,diamondY,float2{.75f,.75f},
        float2{.70710678f,.70710678f},_TerrainAmbientOcclusionDistance*2.0f,
        "diagonal");
}

void checkFlatCellDistance()
{
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    for (int y=-32; y<=96; ++y)
    for (int x=-32; x<=96; ++x)
    {
        float2 sample{(x+.37f)/64.0f,(y+.19f)/64.0f};
        float polygonDistance=TerrainGeometrySignedDistance(sample,xs,ys);
        float boxDistance=-TerrainSignedDistanceToBox(
            sample,float2{.5f,.5f},float2{.5f,.5f});
        if (std::fabs(polygonDistance-boxDistance)>1e-4f)
            throw std::runtime_error("Flat-cell AO shortcut differs from polygon distance");
    }
}

static void checkDistortedAutotileUvSeam()
{
    // The right edge of the left cell and left edge of the right cell share
    // the same displaced endpoints. Their tile-local UVs must stay on the
    // matching authored edges instead of wrapping into each cell's interior.
    float4 leftX{0.0f, 1.125f, 1.0625f, 0.0625f};
    float4 leftY{0.0f, 0.0f, 1.125f, 1.0f};
    float4 rightX{leftX.y, leftX.y + 1.0f, leftX.z + 1.0f, leftX.z};
    float4 rightY{leftY.y, leftY.y, leftY.z, leftY.z};
    const float edgeT = 0.375f;
    float2 sharedPoint{
        leftX.y + (leftX.z - leftX.y) * edgeT,
        leftY.y + (leftY.z - leftY.y) * edgeT};

    // PackCornerUvs identity mapping: corners (0,0), (1,0), (1,1), (0,1).
    float2 leftUv = TerrainResolveGeometryTileUV(
        float2{0.5f, edgeT}, sharedPoint, leftX, leftY, 180.0f, 1.0f);
    float2 rightUv = TerrainResolveGeometryTileUV(
        float2{0.5f, edgeT}, sharedPoint, rightX, rightY, 180.0f, 1.0f);
    if (std::fabs(leftUv.x - 1.0f) > 1e-4f ||
        std::fabs(rightUv.x) > 1e-4f ||
        std::fabs(leftUv.y - edgeT) > 1e-4f ||
        std::fabs(rightUv.y - edgeT) > 1e-4f)
    {
        throw std::runtime_error(
            "Distorted adjacent autotiles do not meet at their authored UV edges");
    }
}

static void checkWorldAnchoredPackTiles()
{
    const float unit = 1.0f / 32.0f;
    const float2 texel{1.0f / 1024.0f, 1.0f / 1024.0f};
    const float4 squareX{0, 1, 1, 0};
    const float4 squareY{0, 0, 1, 1};
    long samples = 0;
    for (int columns : {14, 16})
    for (int descriptor = 0; descriptor < 256; ++descriptor)
    for (int worldX : {-32769, -1, 0, 1, 1024})
    for (int pixel = 0; pixel < 32; ++pixel)
    {
        float2 local{(pixel + 0.5f) / 32.0f, 0.375f};
        float2 transformed = TerrainResolveGeometryTileUV(
            local, local, squareX, squareY,
            (float)TerrainCornerUvBits((uint)descriptor, true), 1.0f);
        TerrainTileUvResult tile = ResolveTerrainTileUV(
            transformed, local, float4{0.125f, 0.25f, columns * unit, unit},
            float4{unit, unit, 1, 1},
            float4{(float)worldX, 7, (float)((descriptor & 31) | 32), 1},
            float4{0, 0, 0, 0}, float4{0, 0, 0, 0}, 0.0f, texel);

        // Independent descriptor oracle: quarter-turn, then mirror axes.
        float expectedU = (descriptor & 128) ? 1.0f - local.y : local.x;
        float expectedV = (descriptor & 128) ? local.x : local.y;
        if (descriptor & 64) expectedU = 1.0f - expectedU;
        if (descriptor & 32) expectedV = 1.0f - expectedV;
        int column = std::min(descriptor & 31, columns - 1);
        float2 expected{
            0.125f + (column + expectedU) * unit,
            0.25f + expectedV * unit};
        if (!tile.isValid ||
            std::fabs(tile.finalUV.x - expected.x) > 1e-6f ||
            std::fabs(tile.finalUV.y - expected.y) > 1e-6f ||
            std::fabs(tile.minTileUV.x - (0.125f + column * unit + texel.x * 0.5f)) > 1e-6f ||
            std::fabs(tile.maxTileUV.x - (0.125f + (column + 1) * unit - texel.x * 0.5f)) > 1e-6f)
        {
            throw std::runtime_error("World anchor lost the pack variant, transform or tile sampling bounds: columns=" +
                std::to_string(columns) + " descriptor=" + std::to_string(descriptor) +
                " pixel=" + std::to_string(pixel) + " actual=" + std::to_string(tile.finalUV.x) +
                "," + std::to_string(tile.finalUV.y) + " expected=" + std::to_string(expected.x) +
                "," + std::to_string(expected.y));
        }
        ++samples;
    }

    // No tile group: the complete sheet must keep world-position addressing,
    // independent of the supplied corner UV and variant column.
    struct SheetCase { int x, y, column, row; };
    const SheetCase sheets[] = {{17, 11, 7, 8}, {-13, -8, 7, 7}, {0, 0, 0, 9}};
    for (const auto& sample : sheets)
    {
        TerrainTileUvResult tile = ResolveTerrainTileUV(
            float2{0.9f, 0.1f}, float2{0.375f, 0.625f},
            float4{0.125f, 0.25f, 10 * unit, 10 * unit},
            float4{unit, unit, 1, 1},
            float4{(float)sample.x, (float)sample.y, 47, 0},
            float4{0, 0, 0, 0}, float4{0, 0, 0, 0}, 0.0f, texel);
        if (std::fabs(tile.finalUV.x - (0.125f + (sample.column + 0.375f) * unit)) > 1e-6f ||
            std::fabs(tile.finalUV.y - (0.25f + (sample.row + 0.625f) * unit)) > 1e-6f)
        {
            throw std::runtime_error("Ungrouped World sheet no longer follows world-position addressing");
        }
    }

    TerrainTileUvResult animated = ResolveTerrainTileUV(
        float2{0.25f, 0.75f}, float2{0.25f, 0.75f},
        float4{0.125f, 0.25f, 14 * unit, unit}, float4{unit, unit, 4, 1},
        float4{1, 7, 37, 1}, float4{0, 6, 0, 0}, float4{0, 0, 0, 0}, 0.5f, texel);
    if (std::fabs(animated.finalUV.x - (0.125f + 5.25f * unit)) > 1e-6f ||
        std::fabs(animated.finalUV.y - (0.25f + 3.75f * unit)) > 1e-6f)
    {
        throw std::runtime_error("World-anchored pack animation changed its variant or frame offset");
    }
    std::cout << "World pack UVs: " << samples
        << " samples, 256 descriptors, 14/16 columns, 32 texel centers; world sheets and animation passed.\n";
}

static void checkAffineGeometryUv()
{
    // Independent forward construction from known UVs; non-axis-aligned,
    // displaced parallelogram corners lie exactly on the production 1/32 grid.
    float2 origin{-0.125f, 0.0625f};
    float2 u{1.0f, 0.125f};
    float2 v{0.0625f, 1.0f};
    float4 xs{origin.x, origin.x+u.x, origin.x+u.x+v.x, origin.x+v.x};
    float4 ys{origin.y, origin.y+u.y, origin.y+u.y+v.y, origin.y+v.y};
    for (int y=-2; y<=34; ++y)
    for (int x=-2; x<=34; ++x)
    {
        float2 known{float(x)/32.0f, float(y)/32.0f};
        float2 point=origin+u*known.x+v*known.y;
        float2 actual=TerrainResolveGeometryLocalUv(point,xs,ys);
        float expectedX=std::clamp(known.x,0.0f,1.0f);
        float expectedY=std::clamp(known.y,0.0f,1.0f);
        if(std::fabs(actual.x-expectedX)>1e-5f || std::fabs(actual.y-expectedY)>1e-5f)
            throw std::runtime_error("Affine terrain UV inversion changed authored coordinates");
    }
}

// Независимая копия TerrainVertex.H: обрезка float до half, 11 значащих
// бит, всё меньше 2^-14 — ноль.
static float truncatedHalf(float value)
{
    if (value == 0.0f) return 0.0f;
    int exponent;
    float mantissa = std::frexp(value, &exponent);
    if (exponent - 1 < -14) return 0.0f;
    return std::ldexp(std::floor(mantissa * 2048.0f) / 2048.0f, exponent);
}

// Фаза k/1000 считается на GPU целыми: обязана дать ровно H(k / 1000f) для
// всего диапазона хэша (k < 6283).
static void checkThousandthsPhase()
{
    for (uint k = 0; k < 6283u; ++k)
    {
        float expected = truncatedHalf((float)k / 1000.0f);
        float actual = TerrainThousandthsAsHalf(k);
        if (actual != expected)
            throw std::runtime_error("phase k/1000 differs at k=" + std::to_string(k) +
                ": " + std::to_string(actual) + " != " + std::to_string(expected));
    }
    for (uint k = 0; k < 65536u; ++k)
    {
        float value = (float)k * (1.0f / 65536.0f);
        if (TerrainTruncateToHalf(value) != truncatedHalf(value))
            throw std::runtime_error("faceted phase truncation differs at " + std::to_string(k));
    }
}

// Миры стенда (TerrainCellEquivalenceHarnessTests.ExportWorldsForHlslShim):
// пёстрые типы, автотайл, стены пака, кайма, органика, перекрытие фона.
// Настоящий LoadTerrainCellVertex на каждом кваде обязан дать ровно
// атрибуты вершин TerrainQuadBuilder.FillQuad — эталона прежнего вида.
template <typename T> static T readValue(std::ifstream& in)
{
    T value{};
    in.read(reinterpret_cast<char*>(&value), sizeof(T));
    if (!in) throw std::runtime_error("truncated shim world fixture");
    return value;
}
static void expectAttribute(float actual, float expected, const std::string& what)
{
    if (actual != expected)
        throw std::runtime_error(what + ": " + std::to_string(actual) + " != " + std::to_string(expected));
}
struct WorldCell { int x, y; };
static std::vector<WorldCell> shapes;
static void checkWorldShapes(const std::string& path);
static long checkWorld(const std::string& path)
{
    shapes.clear();
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("cannot open " + path);
    int worldWidth = readValue<int>(in);
    int worldHeight = readValue<int>(in);
    _TerrainDistortionEnabled = readValue<int>(in);
    for (float4& vector : _TerrainDistortion)
        vector = float4{readValue<float>(in), readValue<float>(in), readValue<float>(in), readValue<float>(in)};
    int originX = readValue<int>(in);
    int originY = readValue<int>(in);
    int ringWidth = readValue<int>(in);
    int ringHeight = readValue<int>(in);
    _TerrainTileDescriptors.reset(64);
    for (uint& word : _TerrainTileDescriptors.data) word = readValue<uint>(in);
    for (int slot = 0; slot < 8; ++slot)
    {
        float size = (float)readValue<int>(in);
        _ShimAtlasTexelSize[slot] = {1.0f / size, 1.0f / size, size, size};
    }
    _TerrainTypes.reset(256);
    for (TerrainTypeRow& row : _TerrainTypes.data)
    {
        row.atlasXY = readValue<uint>(in);
        row.atlasWH = readValue<uint>(in);
        row.look = readValue<uint>(in);
        row.speedGlowTile = readValue<uint>(in);
    }
    _TerrainUnderlayType = readValue<int>(in);
    int cells = ringWidth * ringHeight;
    _TerrainCells.reset((cells + KERN_TERRAIN_CELLS_PER_WORD - 1) / KERN_TERRAIN_CELLS_PER_WORD);
    for (int i = 0; i < cells; ++i)
        _TerrainCells.data[i / KERN_TERRAIN_CELLS_PER_WORD] |=
            (uint)readValue<uint8_t>(in) << ((i % KERN_TERRAIN_CELLS_PER_WORD) * KERN_TERRAIN_CELL_BITS);
    _TerrainCellGridSize = {(float)ringWidth, (float)ringHeight, 1, 0};
    _TerrainCellOrigin = {(float)originX, (float)originY, (float)worldHeight, (float)worldWidth};
    _TerrainCellViewOffset = {0, 0, 0, 0};

    float2 corners[] = {{0,0},{1,0},{1,1},{0,1}};
    int quads = readValue<int>(in);
    long compared = 0;
    for (int q = 0; q < quads; ++q)
    {
        int x = readValue<int>(in), y = readValue<int>(in), layer = readValue<int>(in), atlas = readValue<int>(in);
        float expected[4][26];
        for (auto& vertex : expected) for (float& value : vertex) value = readValue<float>(in);
        std::string where = path + " (" + std::to_string(x) + "," + std::to_string(y) + ") layer " + std::to_string(layer);
        for (int corner = 0; corner < 4; ++corner)
        {
            TerrainCellVertex v = LoadTerrainCellVertex(float3{(float)x, (float)y, (float)layer}, corners[corner]);
            // The production shader intentionally draws the unloaded-cell
            // diagnostic on the foreground carrier, whereas the legacy CPU
            // quad oracle has no quad for unloaded cells.
            int worldX = originX + x, worldY = originY + y;
            if (TerrainCellType(TerrainLoadCell(worldX, worldY)) == 0u && layer == 1)
            {
                expectAttribute(v.atlasIndex, 0.0f, where + ": unloaded carrier slot");
                expectAttribute(v.subAtlasRect.z, 0.0f, where + ": unloaded carrier texture width");
                expectAttribute(v.positionOS.x, x + corners[corner].x, where + ": unloaded carrier x");
                expectAttribute(v.positionOS.y, y + corners[corner].y, where + ": unloaded carrier y");
                ++compared;
                continue;
            }
            expectAttribute(v.atlasIndex, (float)atlas, where + ": atlas slot");
            if (atlas < 0) break;
            const float* e = expected[corner];
            std::string at = where + " corner " + std::to_string(corner);
            const float actual[26] = {
                v.uv.x, v.uv.y,
                v.subAtlasRect.x, v.subAtlasRect.y, v.subAtlasRect.z, v.subAtlasRect.w,
                v.tileSizeUV.x, v.tileSizeUV.y, v.tileSizeUV.z, v.tileSizeUV.w,
                v.worldPos.x, v.worldPos.y, v.worldPos.z, v.worldPos.w,
                v.animData.x, v.animData.y, v.animData.z, v.animData.w,
                v.packedData.x, v.geometryCornersX[corner], v.geometryCornersY[corner], v.packedData.w,
                v.lightContourDecal.x, v.lightContourDecal.y, v.lightContourDecal.z, v.lightContourDecal.w,
            };
            static const char* names[26] = {
                "u", "v", "rect x", "rect y", "rect z", "rect w", "tile x", "tile y", "frames", "frame height",
                "world x", "server y", "column", "autotile", "animation", "speed", "phase", "profile",
                "anchored", "corner x", "corner y", "distorted", "light colour", "light flags", "contour", "decalAtlas",
            };
            for (int i = 0; i < 26; ++i) expectAttribute(actual[i], e[i], at + ": " + names[i]);
            ++compared;
            if (layer == 0 &&
                (v.positionOS.x != x + corners[corner].x || v.positionOS.y != y + corners[corner].y))
                throw std::runtime_error(at + ": background was distorted");

            // Тот же передний план видимым проходом со смещением окна и
            // накладкой дверей (слой 2, адрес в сетке) — та же вершина.
            if (layer == 1)
            {
                _TerrainCellViewOffset = {3, 2, 0, 0};
                TerrainCellVertex visible = LoadTerrainCellVertex(
                    float3{(float)(x - 3), (float)(y - 2), 1.0f}, corners[corner]);
                TerrainCellVertex overlay = LoadTerrainCellVertex(
                    float3{(float)x, (float)y, 2.0f}, corners[corner]);
                _TerrainCellViewOffset = {0, 0, 0, 0};
                for (const TerrainCellVertex* other : {&visible, &overlay})
                {
                    if (other->atlasIndex != v.atlasIndex ||
                        other->positionOS.x != v.positionOS.x || other->positionOS.y != v.positionOS.y ||
                        other->positionOS.z != v.positionOS.z ||
                        other->lightContourDecal.z != v.lightContourDecal.z || other->uvBits != v.uvBits)
                        throw std::runtime_error(at + ": view offset or door overlay address differs");
                }
            }
        }
        if (layer == 1 && atlas >= 0) shapes.push_back({x, y});
    }
    checkWorldShapes(path);
    return compared;
}
// Растеризация настоящих смещённых клеток мира (классика: прямые рёбра)
// против независимого полуплоскостного оракула, швы с соседями по общим
// узлам и носитель AO. Узлы — из шейдерного правила, не из фикстуры.
static long shapeSamples = 0;
static long seamSamples = 0;
static bool loadQuad(WorldCell cell, TerrainCellVertex* vertices)
{
    static const float2 corners[] = {{0,0},{1,0},{1,1},{0,1}};
    for (int i = 0; i < 4; ++i)
        vertices[i] = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, corners[i]);
    return vertices[0].atlasIndex >= 0 && vertices[0].packedData.x == 1 && vertices[0].packedData.w == 0;
}
static bool drawn(int x, int y)
{
    for (const WorldCell& cell : shapes) if (cell.x == x && cell.y == y) return true;
    return false;
}
static void checkWorldShapes(const std::string& path)
{
    int rasterised = 0;
    bool paddingChecked = false;
    bool canonicalChecked = false;
    for (const WorldCell& cell : shapes)
    {
        TerrainCellVertex v[4];
        bool straight = loadQuad(cell, v);
        if (!canonicalChecked && v[0].atlasIndex >= 0 && v[0].packedData.x == 0)
        {
            // Несмещённая клетка: расширенный носитель AO вокруг квадрата,
            // силуэт за квадрат не выходит.
            _TerrainCellGridSize.z = 2;
            _TerrainGeometryCarrierPaddingWorld = {.125f, .25f};
            TerrainCellVertex lower = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{0,0});
            TerrainCellVertex upper = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{1,1});
            _TerrainGeometryCarrierPaddingWorld = {0, 0};
            _TerrainCellGridSize.z = 1;
            if (std::fabs(lower.packedData.y + .0625f) > 1e-6f || std::fabs(lower.packedData.z + .125f) > 1e-6f ||
                std::fabs(upper.packedData.y - 1.0625f) > 1e-6f || std::fabs(upper.packedData.z - 1.125f) > 1e-6f ||
                oracle(float2{-.01f, .5f}, lower.geometryCornersX, lower.geometryCornersY))
                throw std::runtime_error(path + ": expanded unanchored AO carrier mismatch");
            canonicalChecked = true;
        }
        if (!straight) continue;
        float4 xs = expectedPixelCorners(v[0].geometryCornersX);
        float4 ys = expectedPixelCorners(v[0].geometryCornersY);
        // Предпосылка oracle(): четырёхугольник выпуклый.
        for (int i = 0; i < 4; ++i)
        {
            int j = (i + 1) % 4, k = (i + 2) % 4;
            if (cross2(float2{xs[j]-xs[i], ys[j]-ys[i]}, float2{xs[k]-xs[j], ys[k]-ys[j]}) <= 0)
                throw std::runtime_error(path + ": displaced cell is not convex");
        }
        if (!paddingChecked)
        {
            // Носитель AO: на полтексела поля вокруг крайних углов силуэта.
            _TerrainCellGridSize.z = 2;
            _TerrainGeometryCarrierPaddingWorld = {.125f, .25f};
            TerrainCellVertex lower = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{0,0});
            TerrainCellVertex upper = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{1,1});
            _TerrainGeometryCarrierPaddingWorld = {0, 0};
            _TerrainCellGridSize.z = 1;
            float minX = std::min({xs.x, xs.y, xs.z, xs.w}), maxX = std::max({xs.x, xs.y, xs.z, xs.w});
            float minY = std::min({ys.x, ys.y, ys.z, ys.w}), maxY = std::max({ys.x, ys.y, ys.z, ys.w});
            if (std::fabs(lower.packedData.y - (minX - .0625f)) > 1e-6f ||
                std::fabs(lower.packedData.z - (minY - .125f)) > 1e-6f ||
                std::fabs(upper.packedData.y - (maxX + .0625f)) > 1e-6f ||
                std::fabs(upper.packedData.z - (maxY + .125f)) > 1e-6f)
                throw std::runtime_error(path + ": AO carrier does not enclose the silhouette with padding");
            paddingChecked = true;
        }
        if (rasterised++ >= 96) continue;
        // Поклеточная растеризация: по обе стороны каждого центра пикселя.
        for (int y = -8; y < 40; ++y) for (int x = -8; x < 40; ++x)
            for (int sy = 0; sy < 4; ++sy) for (int sx = 0; sx < 4; ++sx)
            {
                float2 p = {(float)cell.x + (x + (sx + .5f) / 4) / 32, (float)cell.y + (y + (sy + .5f) / 4) / 32};
                float2 local = p - float2{(float)cell.x, (float)cell.y};
                TerrainCellVertex shifted[4];
                for (int i = 0; i < 4; ++i)
                {
                    shifted[i] = v[i];
                    shifted[i].positionOS.x -= cell.x;
                    shifted[i].positionOS.y -= cell.y;
                }
                if (rendered(local, shifted, xs, ys) != expectedRendered(local, shifted, xs, ys))
                    throw std::runtime_error(path + ": raster coverage differs from polygon oracle at cell " +
                        std::to_string(cell.x) + "," + std::to_string(cell.y));
                ++shapeSamples;
            }
        // Швы с правым и верхним соседом: общие узлы совпадают, и точка у
        // ребра внутри любого из двух силуэтов покрыта хотя бы одной клеткой.
        for (int side = 0; side < 2; ++side)
        {
            WorldCell other = side == 0 ? WorldCell{cell.x + 1, cell.y} : WorldCell{cell.x, cell.y + 1};
            TerrainCellVertex n[4];
            if (!drawn(other.x, other.y) || !loadQuad(other, n)) continue;
            float2 offset = side == 0 ? float2{1, 0} : float2{0, 1};
            float4 nx = expectedPixelCorners(n[0].geometryCornersX);
            float4 ny = expectedPixelCorners(n[0].geometryCornersY);
            int ownA = side == 0 ? 1 : 3, ownB = 2;
            int theirA = 0, theirB = side == 0 ? 3 : 1;
            float2 a = float2{xs[ownA], ys[ownA]}, b = float2{xs[ownB], ys[ownB]};
            if (a.x != nx[theirA] + offset.x || a.y != ny[theirA] + offset.y ||
                b.x != nx[theirB] + offset.x || b.y != ny[theirB] + offset.y)
                throw std::runtime_error(path + ": neighbour does not share the edge nodes");
            for (int t = 0; t < 64; ++t)
            {
                float2 edge = a + (b - a) * ((t + .5f) / 64);
                for (int o = -2; o <= 2; ++o)
                {
                    float2 p = edge + offset * (o / 512.0f);
                    float2 q = p - offset;
                    bool inside = oracle(p, xs, ys) || oracle(q, nx, ny);
                    bool covered = TerrainGeometryCoverage(p, xs, ys, 1) > .5f ||
                        TerrainGeometryCoverage(q, nx, ny, 1) > .5f;
                    if (inside && !covered)
                        throw std::runtime_error(path + ": uncovered shared edge");
                    ++seamSamples;
                }
            }
        }
    }
    if (rasterised == 0 && path.find("Classic") != std::string::npos)
        throw std::runtime_error(path + ": classic world has no displaced cells to rasterise");
}

static void checkHarnessWorlds(const char* directory)
{
    if (directory == nullptr) throw std::runtime_error("shim world fixture directory was not passed");
    long compared = 0;
    int worlds = 0;
    for (const auto& entry : std::filesystem::directory_iterator(directory))
    {
        compared += checkWorld(entry.path().string());
        ++worlds;
    }
    if (worlds != 3) throw std::runtime_error("expected 3 shim worlds, found " + std::to_string(worlds));
    resetCells();
    std::cout << "HLSL cell decode matches FillQuad vertices: " << worlds << " worlds, " << compared
        << " vertices; displaced raster " << shapeSamples << " subpixels, seams " << seamSamples << " samples.\n";
}

// Номер угла из меша идентификаторов (TerrainCellIdVertex) обязан дать тот
// же обход квада, что и углы, которыми шим зовёт LoadTerrainCellVertex.
static void checkCornerBase()
{
    const float2 expected[4] = {{0,0},{1,0},{1,1},{0,1}};
    for (int corner = 0; corner < 4; corner++)
    {
        float2 base = TerrainCornerBase((float)corner);
        if (base.x != expected[corner].x || base.y != expected[corner].y)
            throw std::runtime_error("TerrainCornerBase walks the quad in the wrong order");
    }
}

// Чтения на вершину: клетка, восемь соседей, их признаки по разу и строка
// типа (фон — ещё признаки фоновых типов соседей). Число
// печатается, чтобы рост цены вершины был виден, и ограничено сверху.
static void checkBufferReads()
{
    resetCells();
    _TerrainCellOrigin = {5, 5, 100, 100};
    _TerrainTypes.data[1].look = onUnderlay;
    bufferReads = 0;
    LoadTerrainCellVertex(float3{0,0,1},float2{0,0});
    long flat = bufferReads;
    _TerrainTypes.data[1].look = wavyOnUnderlay;
    _TerrainDistortionEnabled = 1;
    bufferReads = 0;
    LoadTerrainCellVertex(float3{0,0,1},float2{0,0});
    long distorted = bufferReads;
    bufferReads = 0;
    LoadTerrainCellVertex(float3{0,0,0},float2{0,0});
    long background = bufferReads;
    std::cout << "Terrain cell reads per vertex: flat foreground=" << flat
        << " distorted foreground=" << distorted << " background=" << background << '\n';
    if (flat > 28 || distorted > 28 || background > 28)
        throw std::runtime_error("Terrain cell vertex reads exceed 28");
    resetCells();
}


int runChecks(const char* worldDirectory)
{
    checkDepth();
    checkAffineGeometryUv();
    checkDistortedAutotileUvSeam();
    checkWorldAnchoredPackTiles();
    checkThousandthsPhase();
    checkHarnessWorlds(worldDirectory);
    checkBufferReads();
    checkCornerBase();
    checkAo();
    checkContinuousFieldSilhouette();
    checkFlatCellDistance();
    checkRim();
    checkRoundRim();
    checkAmbientOcclusionFloor();
    std::cout << "HLSL shim displaced autotile UV continuity, geometry quantization, phase and AO geometry edge passed.\n";
    return 0;
}

int main(int argc, char** argv)
{
    try
    {
        return runChecks(argc > 1 ? argv[1] : nullptr);
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
