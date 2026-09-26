#ifndef KERN_TERRAIN_ATLAS_SAMPLING_INCLUDED
#define KERN_TERRAIN_ATLAS_SAMPLING_INCLUDED

#if defined(KERN_TERRAIN_CELLS)
TEXTURE2D(_TerrainAtlas0);
TEXTURE2D(_TerrainAtlas1);
TEXTURE2D(_TerrainAtlas2);
TEXTURE2D(_TerrainAtlas3);
TEXTURE2D(_TerrainAtlas4);
TEXTURE2D(_TerrainAtlas5);
TEXTURE2D(_TerrainAtlas6);
TEXTURE2D(_TerrainAtlas7);

float4 TerrainAtlasTexelSize(
    int slot,
    float4 size0,
    float4 size1,
    float4 size2,
    float4 size3,
    float4 size4,
    float4 size5,
    float4 size6,
    float4 size7)
{
    switch (slot)
    {
        case 1: return size1;
        case 2: return size2;
        case 3: return size3;
        case 4: return size4;
        case 5: return size5;
        case 6: return size6;
        case 7: return size7;
        default: return size0;
    }
}

half4 TerrainSampleAtlas(int slot, SamplerState atlasSampler, float2 uv)
{
    [branch]
    switch (slot)
    {
        case 1: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas1, atlasSampler, uv, 0);
        case 2: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas2, atlasSampler, uv, 0);
        case 3: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas3, atlasSampler, uv, 0);
        case 4: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas4, atlasSampler, uv, 0);
        case 5: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas5, atlasSampler, uv, 0);
        case 6: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas6, atlasSampler, uv, 0);
        case 7: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas7, atlasSampler, uv, 0);
        default: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas0, atlasSampler, uv, 0);
    }
}
#endif

#endif
