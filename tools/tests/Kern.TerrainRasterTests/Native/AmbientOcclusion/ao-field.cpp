// CPU model of mip-zero occupancy texture sampling.
// Only sampling is emulated here; AO response comes from production HLSL.
struct AoTexture
{
    Texture level;
    void generate(Texture base)
    {
        level=std::move(base);
    }
    float SampleLevel(int sampler, float2 uv, float) const
    {
        return level.SampleLevel(sampler,uv,0).x;
    }
};
