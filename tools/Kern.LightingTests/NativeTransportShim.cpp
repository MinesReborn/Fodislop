#include <algorithm>
#include <cmath>
#include <cstring>
#include <iostream>
#include <random>
#include <stdexcept>
#include <vector>
using float2 = float __attribute__((ext_vector_type(2)));
using float3 = float __attribute__((ext_vector_type(3)));
using float4 = float __attribute__((ext_vector_type(4)));
using int2 = int __attribute__((ext_vector_type(2)));
using int3 = int __attribute__((ext_vector_type(3)));
using int4 = int __attribute__((ext_vector_type(4)));
using uint = unsigned;
using uint2 = unsigned __attribute__((ext_vector_type(2)));
using uint3 = unsigned __attribute__((ext_vector_type(3)));
uint2 make_uint2(uint a,uint b) { return {a,b}; }
uint3 make_uint3(uint a,uint b,uint c) { return {a,b,c}; }
uint f32tof16(float f) { _Float16 h=f; unsigned short bits; std::memcpy(&bits,&h,2); return bits; }
float f16tof32(uint u) { unsigned short bits=u; _Float16 h; std::memcpy(&h,&bits,2); return h; }
void sincos(float a,float& s,float& c) { s=std::sin(a); c=std::cos(a); }
// HLSL does not define atan2 at the origin. Make any reliance on the C++
// library's zero result fail numerically instead of hiding a GPU singularity.
float hlslAtan2(float y,float x) {
    return x==0.f && y==0.f ? std::nanf("") : std::atan2(y,x);
}
constexpr float PI=3.14159265359f;
constexpr float PI2=6.28318530718f;
std::vector<uint3> _RadianceAtlas;
std::vector<int4> _DirtyRegions;
std::vector<uint> _CascadeChangedMask;
int _DirtyRegionCount=0;
int _CascadeMaskEnabled=0;
int _CascadeReanchorEnabled=0, _CascadePhaseMatches=1, _ReanchorFarPhaseMatches=1;
int2 _ReanchorDeltaTexels={0,0}, _ReanchorFarDeltaProbes={0,0};
std::vector<uint> _ReanchorChanges(1,0);
std::vector<uint> _LightingCounters(3);
int _LightingCountersEnabled=0;
int _UniformCellTraversalEnabled=1;
int _DynamicPolarScalarExtinction=0;
int _NeutralExtinction=0;
int _DynamicTilesScalarRadiance=0;
void InterlockedAdd(uint& target, uint value) { target += value; }
void InterlockedMax(uint& target, uint value) { target = std::max(target, value); }
int _CascadeOffset,_CascadeProbeSpacing,_CascadeDirectionCount,_FarCascadeOffset;
int _FarCascadeProbeSpacing,_FarCascadeDirectionCount,_HasFarCascade,_CascadeEntryCount,_CascadeDispatchRowWidth;
int2 _CascadeProbeSize,_FarCascadeProbeSize,_CascadeDispatchOrigin,_CascadeDispatchSize;
float2 _CascadeInterval,_FarCascadeInterval;
float2 make_float2(float a, float b) { return {a,b}; }
float2 make_float2(float a) { return {a,a}; }
float2 make_float2(int2 a) { return __builtin_convertvector(a, float2); }
int2 make_int2(int a, int b) { return {a,b}; }
int2 make_int2(float2 a) { return __builtin_convertvector(a, int2); }
int2 make_int2(uint2 a) { return __builtin_convertvector(a, int2); }
int3 make_int3(int2 a, int b) { return {a.x,a.y,b}; }
int3 make_int3(int a, int b, int c) { return {a,b,c}; }
int4 make_int4(int2 a, int b, int c) { return {a.x,a.y,b,c}; }
float3 make_float3(float a, float b, float c) { return {a,b,c}; }
float3 make_float3(float2 a, float b) { return {a.x,a.y,b}; }
float4 make_float4(float2 a,float2 b) { return {a.x,a.y,b.x,b.y}; }
float4 make_float4(float3 a,float b) { return {a.x,a.y,a.z,b}; }
float4 make_float4(float a,float b,float c,float d) { return {a,b,c,d}; }
float min(float a,float b) { return std::min(a,b); }
int2 min(int2 a,int2 b) { return {std::min(a.x,b.x),std::min(a.y,b.y)}; }
float max(float a,float b) { return std::max(a,b); }
float2 min(float2 a,float2 b) { return {min(a.x,b.x),min(a.y,b.y)}; }
float2 max(float2 a,float2 b) { return {max(a.x,b.x),max(a.y,b.y)}; }
float3 max(float3 a,float3 b) { return {max(a.x,b.x),max(a.y,b.y),max(a.z,b.z)}; }
float3 min(float3 a,float b) { return {min(a.x,b),min(a.y,b),min(a.z,b)}; }
float3 max(float3 a,float b) { return {max(a.x,b),max(a.y,b),max(a.z,b)}; }
float2 abs(float2 a) { return {abs(a.x),abs(a.y)}; }
float2 floor(float2 a) { return {floor(a.x),floor(a.y)}; }
float2 round(float2 a) { return {std::round(a.x),std::round(a.y)}; }
float2 ceil(float2 a) { return {std::ceil(a.x),std::ceil(a.y)}; }
float2 frac(float2 a) { return a-floor(a); }
float frac(float a) { return a-floor(a); }
float3 exp(float3 a) { return {exp(a.x),exp(a.y),exp(a.z)}; }
float3 log(float3 a) { return {log(a.x),log(a.y),log(a.z)}; }
float dot(float2 a,float2 b) { return a.x*b.x+a.y*b.y; }
float length(float2 a) { return std::sqrt(a.x*a.x+a.y*a.y); }
float2 sign(float2 a) { return {(float)((a.x>0)-(a.x<0)),(float)((a.y>0)-(a.y<0))}; }
int clamp(int a,int lo,int hi) { return std::clamp(a,lo,hi); }
int2 clamp(int2 a,int2 lo,int2 hi) { return {clamp(a.x,lo.x,hi.x),clamp(a.y,lo.y,hi.y)}; }
float step(float edge,float x) { return x>=edge ? 1.f : 0.f; }
float saturate(float a) { return std::clamp(a,0.f,1.f); }
float smoothstep(float lo,float hi,float value) {
    float t=saturate((value-lo)/(hi-lo));
    return t*t*(3.f-2.f*t);
}
float3 saturate(float3 a) { return {saturate(a.x),saturate(a.y),saturate(a.z)}; }
float2 saturate(float2 a) { return {saturate(a.x),saturate(a.y)}; }
float3 lerp(float3 a,float3 b,float t) { return a+(b-a)*t; }
bool all(int2 a) { return a.x && a.y; }
bool all(int3 a) { return a.x && a.y && a.z; }
bool any(int2 a) { return a.x || a.y; }
bool any(uint2 a) { return a.x || a.y; }
bool any(uint3 a) { return a.x || a.y || a.z; }
bool any(int3 a) { return a.x || a.y || a.z; }
long textureReads=0;
struct Texture {
    int width=1,height=1,depth=1;
    std::vector<float4> data;
    void reset(int w,int h,int layers=1) { width=w; height=h; depth=layers; data.assign(w*h*layers,float4{}); }
    float4& operator[](int2 p) { return data.at(p.y*width+p.x); }
    float4& operator[](int3 p) { return data.at((p.z*height+p.y)*width+p.x); }
    float4 Load(int3 p) const {
        ++textureReads;
        if(p.x<0 || p.y<0 || p.z<0 || p.x>=width || p.y>=height || p.z>=depth)
            throw std::runtime_error("out-of-bounds texture read "+std::to_string(p.x)+","+std::to_string(p.y));
        return data[(p.z*height+p.y)*width+p.x];
    }
    float4 Load(int4 p) const {
        if (p.w!=0) throw std::runtime_error("fixture has no mip levels");
        return Load(int3{p.x,p.y,p.z});
    }
    float4 SampleLevel(int,float2 uv,float) const {
        if(width<=0 || height<=0) return float4{};
        float2 p=uv*float2{(float)width,(float)height}-.5f;
        int2 b=make_int2(floor(p)); float2 f=p-floor(p);
        auto at=[&](int x,int y){return Load(int3{clamp(x,0,width-1),clamp(y,0,height-1),0});};
        return (at(b.x,b.y)*(1-f.x)+at(b.x+1,b.y)*f.x)*(1-f.y)
             +(at(b.x,b.y+1)*(1-f.x)+at(b.x+1,b.y+1)*f.x)*f.y;
    }
    float4 SampleLevel(int,float3 uv,float) const {
        int layer=clamp(int(std::round(uv.z)),0,depth-1);
        float2 p=uv.xy*float2{(float)width,(float)height}-.5f;
        int2 b=make_int2(floor(p)); float2 f=p-floor(p);
        auto at=[&](int x,int y){return Load(int3{clamp(x,0,width-1),clamp(y,0,height-1),layer});};
        return (at(b.x,b.y)*(1-f.x)+at(b.x+1,b.y)*f.x)*(1-f.y)
             +(at(b.x,b.y+1)*(1-f.x)+at(b.x+1,b.y+1)*f.x)*f.y;
    }
};
Texture _MaterialField, _EmissionField, _DirectInput, _StaticDirectInput;
int2 _CompositeDispatchOrigin, _CompositeDispatchSize;
Texture _Result,_DirectTexture;
struct DynamicLight { float4 positionRadius,colorIntensity; };
std::vector<DynamicLight> _DynamicLights;
int _DynamicLightCount=0;
float4 _AmbientColor={.25f,.25f,.25f,0};
int _DebugView=0,_EnableBilinearFix=1;
int sampler_LinearClamp=0, _MaterialYFlip=0;
int2 _FieldSize;
int2 _LightSize;
int _FieldTexelsPerLightTexel=1;
float4 _WorldRect, _EmptyExtinctionRGB, _SolidExtinctionRGB;
float _CellSize=1, _EmissionScale=1;
static const float InvisibleDynamicRadiance = 1e-6f;
float _SolidOccupancyThreshold = 0.5f;
float _TransportSolidThreshold = 0.4f;
int2 _DynamicDispatchOrigin,_DynamicDispatchSize;
int _DynamicLightIndex=0;
int _WriteDynamicDirect=0;
struct DynamicTileInfo { int2 fieldOrigin,size,tileOffset; int reachIndex=0,reserved=0; };
std::vector<DynamicTileInfo> _DynamicTileInfos;
Texture _DynamicTiles,_DynamicTilesInput;
int2 _DynamicTileOffset,_ComposeOrigin,_ComposeSize;
int _DynamicTileCount=0;
// Плечо отражения поверхности и параметры динамического света — юниформы
// WorldLighting.compute; авторские значения живут в VisualTuning.cs.
float _SurfaceReflectionReachCells = 0.5f;
float _DynamicNearCells = 6.0f;
int _DynamicAngularSampleCount = 8;
int _DynamicEmitterPointsPerAxis = 3;
int2 _DynamicPolarSize;
int2 _DynamicPolarTextureSize;
int _DynamicPolarLayerOffset = 0;
int _DynamicReachIndex = 0;
Texture _DynamicPolar,_DynamicPolarInput;
Texture _CellSolidMask,_CellSolidMaskOutput;
Texture _SurfaceAirCache,_SurfaceAirCacheOutput;
int2 _CellGridSize;
bool sameAtlas(const std::vector<uint3>& left,const std::vector<uint3>& right) {
    if(left.size()!=right.size()) return false;
    for(size_t i=0;i<left.size();++i)
        if(left[i].x!=right[i].x || left[i].y!=right[i].y || left[i].z!=right[i].z) return false;
    return true;
}
