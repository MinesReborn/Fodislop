static void resetCells();
static void setCell(int gridX, int unityY, uint bits);

static void requireDepth(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

static void checkDepth()
{
    const float4 cornersX = {0,1,1,0};
    const float4 cornersY = {0,0,1,1};
    const float4 open = {0,0,0,0};
    float2 source;
    _TerrainDepthPixels = 16;
    // Scan the whole expanded raster carrier, not only the original face.
    // Independent square/disk tests reject out-of-cell samples before UV
    // clamping can turn atlas edges into horizontal/vertical stripes.
    for (int rounded = 0; rounded < 2; rounded++)
    for (int x = -32; x < 64; x++)
    for (int y = -32; y < 64; y++)
    {
        float2 p = {(x+.5f)/32.f,(y+.5f)/32.f};
        TerrainSurfaceInputs surface{};
        surface.cellSample = p;
        surface.contourUV = p;
        surface.cornersX = cornersX;
        surface.cornersY = cornersY;
        surface.anchored = 0;
        surface.packedContour = rounded ? (float)KERN_TERRAIN_ROUNDABLE_CONTOUR_FLAG : 0;
        float px = p.x-.5f, py = p.y-.5f;
        bool inCell = p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1;
        bool inContour = !rounded || (px*px+py*py<=.51f*.51f) ||
            std::fabs(std::fabs(px)-std::fabs(py))>=.4f;
        bool expected = inCell && inContour;
        requireDepth((TerrainDepthFaceCoverage(surface)>.5f)==expected,
            "Depth: expanded foreground carrier paints outside square/round face");
    }
    // Independent wire values shared with the C# encoder test, not generated
    // by calling the decoder itself. POSITION has no TEXCOORD dependency.
    float4 decodedEdges;
    auto decoded = TerrainVolumeDecodeAddress(float4{924,708,3.501953125f,2},decodedEdges);
    requireDepth(decoded.x == 7 && decoded.y == 5 && decoded.z == 2 && decoded.w == 2,
        "Volume: POSITION-only cell/corner decoder");
    requireDepth(decodedEdges.x == -.125f && decodedEdges.y == 1.125f &&
        decodedEdges.z == 1 && decodedEdges.w == 0,"Volume: POSITION-only contour endpoints");
    // Independent forward pinhole oracle: H=32, h=.5, factor=32/31.5.
    // Real vertices at ground/face height must not be two translated images.
    auto base = TerrainVolumeProjectPoint(float2{16,.5f},float2{0,.5f},0,16);
    auto face = TerrainVolumeProjectPoint(float2{16,.5f},float2{0,.5f},1,16);
    requireDepth(base.x == 16 && face.x == 16.25f && face.y == .5f,
        "Volume: fixed base / raised vertex perspective incorrect");
    auto moved = TerrainVolumeProjectPoint(float2{16,.5f},float2{8,.5f},1,16);
    requireDepth(moved.x == 16.125f,"Volume: vertex projection must follow camera");
    auto opposite = TerrainVolumeProjectPoint(float2{16,.5f},float2{32,.5f},1,16);
    requireDepth(opposite.x == 15.75f,"Volume: opposite camera vertex projection");
    auto disabled = TerrainVolumeProjectPoint(float2{16,.5f},float2{0,.5f},1,0);
    requireDepth(disabled.x == 16 && disabled.y == .5f,"Volume: zero height moves vertices");
    auto leftNormal = TerrainVolumeOutwardNormal(float2{0,1},float2{0,0});
    auto topNormal = TerrainVolumeOutwardNormal(float2{1,1},float2{0,1});
    requireDepth(leftNormal.x == -1 && leftNormal.y == 0 && topNormal.x == 0 && topNormal.y == 1,
        "Volume: physical side outward normal reversed");
    auto leftTop = TerrainVolumeProjectPoint(float2{0,1},float2{-16,1},1,16);
    auto rightTop = TerrainVolumeProjectPoint(float2{1,1},float2{-16,1},1,16);
    requireDepth(leftTop.x == .25f && rightTop.x == 1.28125f,
        "Volume: face must change perspective width, not be a translated copy");
    // Physical side UVs run from the edge towards the material interior as
    // height advances: the two planes cannot share a stretched edge texel.
    float2 edge = {0,.5f};
    float2 bottomUv = TerrainVolumeMaterialPoint(edge,leftNormal,0,16);
    float2 topUv = TerrainVolumeMaterialPoint(edge,leftNormal,1,16);
    requireDepth(bottomUv.x == .015625f && topUv.x == .515625f && topUv.y == .5f,
        "Volume: wall material depth coordinate");
    resetCells();
    _TerrainTypes.data[1].look = KERN_TERRAIN_TYPE_OPAQUE_OWN;
    _TerrainTypes.data[1].atlasWH = 32u | (32u << 12);
    _TerrainTypes.data[2].look = KERN_TERRAIN_TYPE_BACKGROUND;
    auto interiorGround = LoadTerrainCellVertex(float3{1,1,0},float2{0,0},true);
    requireDepth(interiorGround.atlasIndex < 0,"Volume: interior mass must retain background culling");
    setCell(2,1,2u);
    auto physicalGround = LoadTerrainCellVertex(float3{1,1,0},float2{0,0});
    auto visibleGround = LoadTerrainCellVertex(float3{1,1,0},float2{0,0},true);
    requireDepth(physicalGround.atlasIndex < 0 && visibleGround.atlasIndex >= 0,
        "Volume: stationary ground retention changed physical culling");

    for (int depth = 0; depth <= 20; depth++)
    for (int x = -23; x <= 23; x++)
    for (int y = -23; y <= 23; y++)
    {
        float2 delta = {x*11.17f,y*13.19f};
        float2 offset = TerrainDepthOffset(delta,(float)depth);
        float px = offset.x*32, py = offset.y*32;
        requireDepth(std::fabs(px-std::nearbyint(px)) < 1e-6f &&
            std::fabs(py-std::nearbyint(py)) < 1e-6f,"Depth: offset off 1/32 grid");
        requireDepth(px*px+py*py <= 256.00001f,"Depth: half-cell cap exceeded");
        requireDepth(offset.x*delta.x >= 0 && offset.y*delta.y >= 0,
            "Depth: raised face must project away from camera axis");
    }
    resetCells();
    std::cout << "Volume vertex planes, perspective width, camera, normals, wall UV, face bounds and ground isolation passed.\n";
}
