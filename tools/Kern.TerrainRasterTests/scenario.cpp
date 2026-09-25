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
    _WorldAmbientOcclusionYFlip=0;
    _TerrainAmbientOcclusionStrength=1;
    _TerrainAmbientOcclusionFloor=0.51f;
    Texture solid;
    solid.reset(64,64);
    for(int i=0;i<64*64;++i) solid.data[i].a=1;
    _WorldAmbientOcclusionTexture.generate(std::move(solid));
    float darkest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(darkest-0.51f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion does not stop at the floor: " + std::to_string(darkest));

    Texture empty;
    empty.reset(64,64);
    for(int i=0;i<64*64;++i) empty.data[i].a=0;
    _WorldAmbientOcclusionTexture.generate(std::move(empty));
    float brightest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(brightest-1.0f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion darkens an empty neighbourhood");
}

// Упаковка слова контура — та же арифметика, что в TerrainLightingData.Pack:
// бит 0 — флаг контура, 1-4 — диагональные соседи, 5+ — код рельефа. Хендмейдный
// reliefCode*32 проверял только шейдер и молчал о том, переживает ли код
// соседство с занятыми младшими битами.
static float packContourFull(int reliefCode, int contourFlags, int solidDiagonal)
{
    return float(contourFlags + solidDiagonal * 2 + reliefCode * 32);
}

static float packContour(int reliefCode) { return packContourFull(reliefCode, 0, 0); }

// Код рельефа из маски своих соседей — как это делает TerrainQuadBuilder:
// маска + 1, ноль оставлен под «клетка без рельефа».
static float packReliefMask(int reliefMask, int contourFlags, int solidDiagonal)
{
    return packContourFull((reliefMask & 0x0F) + 1, contourFlags, solidDiagonal);
}

// Канонические углы: несмещённая клетка.
static float rim(float2 p, float packed)
{
    return TerrainReliefRimRaw(
        float4{1,p.x,p.y,0}, float4{0,1,1,0}, float4{0,0,1,1}, packed);
}

// Смещённая клетка: падение обязано растянуться на её реальные границы, а
// не упереться в дно на всём выступе.
static float displacedRim(float2 p, float4 xs, float4 ys, float packed)
{
    return TerrainReliefRimRaw(float4{1,p.x,p.y,0}, xs, ys, packed);
}

// Кайма: три стороны, верх не затемняется никогда, дно падения 0.125, и
// координата несущего прямоугольника не должна выводить её из диапазона.
static void checkReliefRim()
{
    _TerrainReliefRimEnabled = 1.0f;
    const float2 nearTop{0.5f, 0.99f};
    const float2 nearBottom{0.5f, 0.01f};
    const float2 nearLeft{0.01f, 0.5f};
    const float2 nearRight{0.99f, 0.5f};
    const float2 centre{0.5f, 0.5f};

    // Нет рельефной группы — кайма не трогает ничего.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(rim(p, packContour(0)) != 1.0f)
            throw std::runtime_error("Relief rim darkened a cell without a relief group");

    // Вся семья вокруг: код 16 — маска 15 со сдвигом.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(rim(p, packContour(16)) != 1.0f)
            throw std::runtime_error("Relief rim darkened the interior of a solid mass");

    // Середина клетки не трогается ни при каких чужих сторонах.
    if(rim(centre, packContour(1)) < 0.99f)
        throw std::runtime_error("Relief rim reached the centre of the cell");

    // Все четыре соседа чужие: маска 0, код 1. Низ, лево и право темнеют,
    // верх обязан остаться нетронутым.
    float allForeign = packContour(1);
    for(float2 p : {nearTop, nearBottom, nearLeft, nearRight})
    {
        float v = rim(p, allForeign);
        if(v >= 0.25f)
            throw std::runtime_error("Relief rim missing on a foreign side");
        if(v <= 0.1f)
            throw std::runtime_error("Relief rim is darker than the original scale allows");
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
            if(expectDark && v >= 0.25f)
                throw std::runtime_error("Relief rim missing on the single foreign side");
            // Не ровно единица: квантование сдвигает середину клетки на
            // полтексела, и противоположная грань даёт 0.9985. Утечкой это
            // не является, а вот заметное затемнение — является.
            if(!expectDark && v < 0.99f)
                throw std::runtime_error("Relief rim leaked onto a side of the same family");
        }
    }
    if(rim(centre, allForeign) < 0.99f)
        throw std::runtime_error("Relief rim reached the centre of the cell");

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
            float fall=1.0f-0.5f*s*s;
            return fall*fall*fall;
        };
        if(std::fabs(flat - sideFalloff(.25f)) > 1e-4f ||
            std::fabs(displaced - sideFalloff(span.y*.25f)) > 1e-4f)
        {
            throw std::runtime_error(
                "Relief rim does not follow the displaced edge distance: flat=" +
                std::to_string(flat) + " displaced=" + std::to_string(displaced));
        }

        if(displacedRim(polygonCentre, xs, ys, allForeign) < 0.99f)
            throw std::runtime_error("Relief rim reached the centre of a displaced cell");
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
                    "Relief rim is not constant along a foreign edge: at " +
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
        if(c >= a || c >= b)
            throw std::runtime_error("A corner of two foreign edges is not darker than either");
    }

    // Полная упаковка: младшие биты заняты, код обязан дойти целым.
    //
    // Это и есть шов, на котором кайма могла бы включаться через клетку.
    // Флаги контура и маска диагональных соседей меняются от клетки к клетке,
    // и если бы код рельефа стоял не на своём месте, кайма то появлялась бы,
    // то исчезала по соседству, которое к ней отношения не имеет. Здесь
    // прогоняются все 16 масок против всех 32 комбинаций младших битов.
    {
        const float2 sideProbes[4] = {nearTop, nearLeft, nearBottom, nearRight};
        for(int reliefMask = 0; reliefMask <= 0x0F; ++reliefMask)
        {
            float clean = packReliefMask(reliefMask, 0, 0);
            for(int contourFlags = 0; contourFlags <= 1; ++contourFlags)
            for(int solidDiagonal = 0; solidDiagonal <= 0x0F; ++solidDiagonal)
            {
                float packed = packReliefMask(reliefMask, contourFlags, solidDiagonal);
                for(int side = 0; side < 4; ++side)
                {
                    float expected = rim(sideProbes[side], clean);
                    float actual = rim(sideProbes[side], packed);
                    if(std::fabs(expected - actual) > 1e-5f)
                        throw std::runtime_error(
                            "Relief code does not survive the packed word: mask=" +
                            std::to_string(reliefMask) + " flags=" +
                            std::to_string(contourFlags) + " diagonal=" +
                            std::to_string(solidDiagonal) + " side=" +
                            std::to_string(side) + " expected=" +
                            std::to_string(expected) + " actual=" +
                            std::to_string(actual));
                }

                // И содержательно: своя сторона не темнеет, чужая темнеет.
                for(int side = 0; side < 4; ++side)
                {
                    bool sameFamily = (reliefMask & (1 << side)) != 0;
                    float v = rim(sideProbes[side], packed);
                    if(sameFamily && v < 0.99f)
                        throw std::runtime_error(
                            "Relief rim darkens a side whose neighbour is the same family: mask=" +
                            std::to_string(reliefMask) + " side=" + std::to_string(side));
                    if(!sameFamily && v >= 0.25f)
                        throw std::runtime_error(
                            "Relief rim missing on a foreign side: mask=" +
                            std::to_string(reliefMask) + " side=" + std::to_string(side));
                }
            }
        }
    }

    // Выключатель снимает кайму целиком.
    _TerrainReliefRimEnabled = 0.0f;
    if(rim(nearBottom, packContour(1)) != 1.0f)
        throw std::runtime_error("Disabled relief rim still darkens the frame");
    _TerrainReliefRimEnabled = 1.0f;

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
                throw std::runtime_error("Relief rim leaves its range on an anchored carrier sample");
        }
}

void checkAo()
{
    _WorldAmbientOcclusionYFlip=0;
    _TerrainAmbientOcclusionStrength=1;
    // Тот же пол, что в TerrainLook: множитель обязан останавливаться на нём.
    _TerrainAmbientOcclusionFloor=0.51f;
    float2 corners[]={{0,0},{1,0},{1,1},{0,1}};
    for(int density : {8,16,32,64})
    {
        float samples[2];
        for(int shape=0;shape<2;++shape)
        {
            float4 xs={0,1,shape ? .5f : 1.f,0},ys={0,0,1,1};
            _TerrainCellGeometryX.data[1]=xs;
            _TerrainCellGeometryY.data[1]=ys;
            TerrainCellVertex vertices[4];
            for(int i=0;i<4;++i)
                vertices[i]=LoadTerrainCellVertex(
                    float3{3,3,1},corners[i],_OrganicBendStrength,_OrganicBendPivot);
            Texture field;
            field.reset(8*density,8*density);
            for(int y=0;y<field.height;++y) for(int x=0;x<field.width;++x)
            {
                float2 p={(x+.5f)/density,(y+.5f)/density};
                float signedDistance = TerrainGeometrySignedDistance(
                    p-float2{3,3},xs,ys,0,false);
                float contact = 1.0f-smoothstep(0.0f,0.5f,max(-signedDistance,0.0f));
                float expectedDistance = oracleSignedDistance(p-float2{3,3},xs,ys);
                float expectedContact = 1.0f-smoothstep(
                    0.0f,0.5f,max(-expectedDistance,0.0f));
                if (std::fabs(contact - expectedContact) > 1e-4f)
                    throw std::runtime_error("AO contact field differs from independent polygon-distance oracle");
                field.data[y*field.width+x].a = contact;
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

void checkAntialiasedFieldSilhouette()
{
    const float texelFootprint=1.0f/64.0f;
    auto checkEdge=[&](float4 xs,float4 ys,float2 midpoint,float2 outwardNormal,
                       float derivativeFootprint,const char* label)
    {
        _TestFwidth=derivativeFootprint;
        float previous=1.0f;
        int fractionalSamples=0;
        for(int i=0;i<=16;++i)
        {
            float signedDistance=derivativeFootprint*(1.0f-i/8.0f);
            float2 p=midpoint-outwardNormal*signedDistance;
            float actual=TerrainGeometryCoverageForField(p,xs,ys,0);
            float expected=smoothstep(
                -derivativeFootprint*.5f,
                derivativeFootprint*.5f,
                signedDistance);
            if(std::fabs(actual-expected)>1e-4f)
                throw std::runtime_error(
                    std::string("AO ")+label+" coverage disagrees with independent distance oracle");
            if(actual>previous+1e-5f)
                throw std::runtime_error(std::string("AO ")+label+" edge is not monotonic");
            if(actual>0.0f && actual<1.0f)
                ++fractionalSamples;
            previous=actual;
        }
        if(fractionalSamples<2)
            throw std::runtime_error(std::string("AO ")+label+" edge still has a binary staircase");
    };

    float4 squareX={0,1,1,0};
    float4 squareY={0,0,1,1};
    checkEdge(squareX,squareY,float2{1,.5f},float2{1,0},
        texelFootprint,"axis-aligned");

    // A diamond side has equal X/Y normal components. Its screen-space
    // signed-distance derivative is sqrt(2) larger than either axis alone;
    // max(fwidth(cellSample.x), fwidth(cellSample.y)) under-filters this edge.
    float4 diamondX={.5f,1,.5f,0};
    float4 diamondY={0,.5f,1,.5f};
    const float diagonalFootprint=texelFootprint*std::sqrt(2.0f);
    checkEdge(diamondX,diamondY,float2{.75f,.75f},
        float2{.70710678f,.70710678f},diagonalFootprint,"diagonal");
}

void checkOrganicVerticesUseGeometryGrid()
{
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    float4 bends={.03125f,-.0625f,.0625f,-.03125f};
    for(int index=0;index<8;++index)
    {
        float2 point=TerrainOrganicGeometryPoint(xs,ys,bends,index);
        if(std::fabs(point.x*32.0f-std::round(point.x*32.0f))>1e-5f ||
            std::fabs(point.y*32.0f-std::round(point.y*32.0f))>1e-5f)
            throw std::runtime_error("Organic bend vertex was not quantized before rasterization");
    }
}

void checkOrganicSignedDistance()
{
    // Independent eight-vertex contour for packed bends (+2,-1,+2,-2).
    // Midpoints are snapped once to the 1/32-cell geometry grid.
    float2 vertices[8] = {
        {0,0}, {.34375f,.0625f}, {1,0}, {.96875f,.65625f},
        {1,1}, {.34375f,1.0625f}, {0,1}, {-.0625f,.65625f}
    };
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    for (int y=0; y<96; ++y)
    for (int x=0; x<96; ++x)
    {
        float2 sample{(x+.37f)/64.0f-.25f,(y+.19f)/64.0f-.25f};
        bool inside=false;
        float nearestSquared=1.0e20f;
        float2 expectedNearest=sample;
        for (int side=0; side<8; ++side)
        {
            float2 start=vertices[side];
            float2 end=vertices[(side+1)&7];
            float2 edge=end-start;
            if ((start.y>sample.y)!=(end.y>sample.y) &&
                sample.x<start.x+(sample.y-start.y)*edge.x/edge.y)
                inside=!inside;
            float projection=std::clamp(
                ((sample.x-start.x)*edge.x+(sample.y-start.y)*edge.y)/
                    (edge.x*edge.x+edge.y*edge.y),0.0f,1.0f);
            float2 delta=sample-(start+edge*projection);
            float distanceSquared=delta.x*delta.x+delta.y*delta.y;
            if (distanceSquared<nearestSquared)
            {
                nearestSquared=distanceSquared;
                expectedNearest=start+edge*projection;
            }
        }
        float expected=std::sqrt(nearestSquared)*(inside?1.0f:-1.0f);
        float2 actualNearest;
        float actual=TerrainGeometrySignedDistance(
            sample,xs,ys,110.0f,true,actualNearest);
        if (std::fabs(actual-expected)>1e-4f ||
            length(actualNearest-expectedNearest)>1e-4f)
            throw std::runtime_error("Organic AO distance differs from independent contour");
    }
}

void checkFlatCellDistance()
{
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    for (int y=-32; y<=96; ++y)
    for (int x=-32; x<=96; ++x)
    {
        float2 sample{(x+.37f)/64.0f,(y+.19f)/64.0f};
        float polygonDistance=TerrainGeometrySignedDistance(sample,xs,ys,0.0f,false);
        float boxDistance=-TerrainSignedDistanceToBox(
            sample,float2{.5f,.5f},float2{.5f,.5f});
        if (std::fabs(polygonDistance-boxDistance)>1e-4f)
            throw std::runtime_error("Flat-cell AO shortcut differs from polygon distance");
    }
}

void checkAoCarrierPadding()
{
    _TerrainCellGridSize = {1,1,2,0};
    _TerrainGeometryCarrierPaddingWorld = {.125f,.25f};
    float4 xs={.25f,.75f,.75f,.25f};
    float4 ys={.25f,.25f,.75f,.75f};
    _TerrainCellGeometryX.data[1]=xs;
    _TerrainCellGeometryY.data[1]=ys;
    TerrainCellVertex lowerLeft=LoadTerrainCellVertex(
        float3{0,0,1},float2{0,0},_OrganicBendStrength,_OrganicBendPivot);
    TerrainCellVertex upperRight=LoadTerrainCellVertex(
        float3{0,0,1},float2{1,1},_OrganicBendStrength,_OrganicBendPivot);
    if(std::fabs(lowerLeft.packedData.y-.1875f)>1e-6f ||
        std::fabs(lowerLeft.packedData.z-.125f)>1e-6f ||
        std::fabs(upperRight.packedData.y-.8125f)>1e-6f ||
        std::fabs(upperRight.packedData.z-.875f)>1e-6f)
        throw std::runtime_error("AO carrier does not include half a field texel around silhouette corners");

    // Unanchored cells still need their canonical square tested against the
    // expanded carrier. Otherwise the new carrier margin becomes solid AO.
    _TerrainCellMeta.data[1].a=.5f;
    TerrainCellVertex canonicalLowerLeft=LoadTerrainCellVertex(
        float3{0,0,1},float2{0,0},_OrganicBendStrength,_OrganicBendPivot);
    TerrainCellVertex canonicalUpperRight=LoadTerrainCellVertex(
        float3{0,0,1},float2{1,1},_OrganicBendStrength,_OrganicBendPivot);
    if(std::fabs(canonicalLowerLeft.packedData.y+.0625f)>1e-6f ||
        std::fabs(canonicalLowerLeft.packedData.z+.125f)>1e-6f ||
        std::fabs(canonicalUpperRight.packedData.y-1.0625f)>1e-6f ||
        std::fabs(canonicalUpperRight.packedData.z-1.125f)>1e-6f ||
        TerrainGeometryCoverageForField(
            float2{-.01f,.5f},
            canonicalLowerLeft.geometryCornersX,
            canonicalLowerLeft.geometryCornersY,
            0.0f)>0.0f)
        throw std::runtime_error("Expanded unanchored AO carrier escaped its canonical cell silhouette");

    _TerrainCellMeta.data[1].a=1.0f;
    _TerrainGeometryCarrierPaddingWorld={0,0};
    _TerrainCellGridSize={1,1,1,0};
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

int runChecks()
{
    checkDistortedAutotileUvSeam();
    Texture* channels[] = {&_TerrainCellColor, &_TerrainCellMeta,
        &_TerrainCellAtlasRect, &_TerrainCellTileSize, &_TerrainCellWorld,
        &_TerrainCellAnimation, &_TerrainCellGlow, &_TerrainCellGeometryX,
        &_TerrainCellGeometryY};
    for (Texture* channel : channels) channel->reset(1,2);
    _TerrainCellGridSize = {1,1,1,0};
    _TerrainCellOrigin = {0,0,0,0};
    _TerrainCellViewOffset = {0,0,0,0};
    _TerrainCellMeta.data[1] = {1.f/255,228.f/255,0,1};
    _TerrainCellMeta.data[0] = {1.f/255,228.f/255,0,1}; // stale anchor must not move background
    std::mt19937 rng(0x32AABB);
    float2 corners[] = {{0,0},{1,0},{1,1},{0,1}};
    long checked=0;
    for(int shape=0;shape<128;++shape)
    {
        float4 xs,ys;
        for(int i=0;i<4;++i)
        {
            // Диапазон обязан покрывать продакшен целиком. Свободный джиттер
            // внутри массива породы даёт +-3*DistortionStrengthSteps шагов,
            // то есть +-6/32; при +-4/32 две самые сильные ступени не
            // проверялись вовсе.
            xs[i]=corners[i].x+(int(rng()%13)-6)/32.f;
            ys[i]=corners[i].y+(int(rng()%13)-6)/32.f;
        }
        // Предпосылка oracle(): четырёхугольник выпуклый. При смещении до
        // 6/32 это выполняется с запасом — чтобы стать невыпуклым, углу надо
        // пересечь диагональ соседей, а это больше половины клетки. Проверяем,
        // а не предполагаем: поднимут амплитуду — падёт здесь, а не в виде
        // молчаливого расхождения с оракулом.
        for(int i=0;i<4;++i)
        {
            int j=(i+1)%4, k=(i+2)%4;
            float turn=cross2(float2{xs[j]-xs[i],ys[j]-ys[i]},
                              float2{xs[k]-xs[j],ys[k]-ys[j]});
            if(turn<=0) throw std::runtime_error(
                "Generated quad is not convex; oracle() precondition broken");
        }
        _TerrainCellGeometryX.data[1]=xs;
        _TerrainCellGeometryY.data[1]=ys;
        _TerrainCellGeometryX.data[0]=xs;
        _TerrainCellGeometryY.data[0]=ys;
        TerrainCellVertex vertices[4];
        for(int i=0;i<4;++i)
        {
            vertices[i]=LoadTerrainCellVertex(
                float3{0,0,1},corners[i],_OrganicBendStrength,_OrganicBendPivot);
            auto background=LoadTerrainCellVertex(
                float3{0,0,0},corners[i],_OrganicBendStrength,_OrganicBendPivot);
            if(background.positionOS.x!=corners[i].x || background.positionOS.y!=corners[i].y)
                throw std::runtime_error("Background was distorted");
        }
        for(int y=-5;y<37;++y) for(int x=-5;x<37;++x)
        {
            for(int sy=0;sy<4;++sy) for(int sx=0;sx<4;++sx)
            {
                float2 p={(x+(sx+.5f)/4)/32,(y+(sy+.5f)/4)/32};
                bool actual=rendered(p,vertices,xs,ys);
                ++checked;
                bool expected=expectedRendered(p,vertices,xs,ys);
                if(actual!=expected)
                {
                    std::cerr << "shape=" << shape << " pixel=" << x << "," << y
                        << " subpixel=" << sx << "," << sy << " expected=" << expected << " actual=" << actual << '\n';
                    return 1;
                }
            }
        }
        // The right neighbour shares both endpoints of the displaced edge.
        // Rasterize both independently; their union must have no black seam.
        float4 nx={xs.y-1,1,1,xs.z-1}, ny={ys.y,0,1,ys.z};
        _TerrainCellGeometryX.data[1]=nx;
        _TerrainCellGeometryY.data[1]=ny;
        TerrainCellVertex neighbour[4];
        for(int i=0;i<4;++i)
            neighbour[i]=LoadTerrainCellVertex(
                float3{1,0,1},corners[i],_OrganicBendStrength,_OrganicBendPivot);
        for(int y=32;y<96;++y) for(int x=96;x<160;++x)
        {
            float2 p={(x+.5f)/128,(y+.5f)/128};
            if(!rendered(p,vertices,xs,ys) && !rendered(p,neighbour,nx,ny))
                throw std::runtime_error("Uncovered shared edge between adjacent cells");
        }

        // Верхний сосед. Проверялся только правый, то есть только
        // вертикальный стык; горизонтальный не проверял никто, а в кадре
        // именно он и читается — тонкой тёмной чертой поперёк породы там,
        // где не достаётся ни одной клетке и наружу смотрит фон мира.
        //
        // Нижняя грань соседа — это наша верхняя, сдвинутая на клетку вниз:
        // углы общие, так их строит TerrainCellGeometry.FromOffsets из одного
        // и того же GridVertexOffsets.
        float4 tx={xs.w,xs.z,1,0}, ty={ys.w-1,ys.z-1,1,1};
        _TerrainCellGeometryX.data[1]=tx;
        _TerrainCellGeometryY.data[1]=ty;
        TerrainCellVertex above[4];
        for(int i=0;i<4;++i)
            above[i]=LoadTerrainCellVertex(
                float3{0,1,1},corners[i],_OrganicBendStrength,_OrganicBendPivot);
        for(int y=96;y<160;++y) for(int x=32;x<96;++x)
        {
            float2 p={(x+.5f)/128,(y+.5f)/128};
            if(!rendered(p,vertices,xs,ys) && !rendered(p,above,tx,ty))
                throw std::runtime_error(
                    "Uncovered shared edge between vertically adjacent cells at " +
                    std::to_string(p.x) + "," + std::to_string(p.y));
        }
    }
    checkAo();
    checkAoCarrierPadding();
    checkAntialiasedFieldSilhouette();
    checkOrganicVerticesUseGeometryGrid();
    checkOrganicSignedDistance();
    checkFlatCellDistance();
    checkReliefRim();
    checkAmbientOcclusionFloor();
    std::cout << "HLSL shim displaced autotile UV continuity, carrier/mask, geometry quantization and AO geometry edge passed: " << checked
        << " subpixels; 512 background corners and 524288 adjacent-edge samples.\n";
    return 0;
}

int main()
{
    try
    {
        return runChecks();
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
