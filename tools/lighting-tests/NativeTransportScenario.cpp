int checks=0;
void near(float got,float expected,float tolerance,const char* label) {
    ++checks;
    if(!std::isfinite(got) || std::abs(got-expected)>tolerance)
        throw std::runtime_error(std::string(label)+": got "+std::to_string(got)+", expected "+std::to_string(expected));
}
void setup(int w,int h,int scale=1) {
    _FieldSize={w*scale,h*scale}; _WorldRect={0,0,(float)w,(float)h};
    _MaterialField.reset(w*scale,h*scale); _EmissionField.reset(w*scale,h*scale);
    _LightingCounters.assign(3, 0);
    _DirtyRegions.assign(1, int4{0,0,0,0});
    _CascadeChangedMask.clear();
    _DirtyRegionCount=0; _CascadeMaskEnabled=0;
    _MaterialYFlip=0; _EmptyExtinctionRGB={.2f,.1f,.4f,0}; _SolidExtinctionRGB={4.f,8.f,16.f,0};
}
// Rebuilds the per-cell solid mask from the current material fixture, as the
// engine does whenever the material field is redrawn.
void buildMask() {
    _CellGridSize={(int)(_WorldRect.z/_CellSize),(int)(_WorldRect.w/_CellSize)};
    _CellSolidMaskOutput.reset(_CellGridSize.x,_CellGridSize.y);
    for(int y=0;y<_CellGridSize.y;y++)for(int x=0;x<_CellGridSize.x;x++)BuildCellSolidMask(uint3{(uint)x,(uint)y,0});
    _CellSolidMask=_CellSolidMaskOutput;
}
float3 trace(float2 a,float2 b,float3* radiance=nullptr) {
    buildMask();
    float3 r,t;
    try { TraceRadianceSegment(a,b,true,r,t); }
    catch(...) { std::cerr<<"ray "<<a.x<<","<<a.y<<" -> "<<b.x<<","<<b.y<<"\n"; throw; }
    if(radiance)*radiance=r; return t;
}
struct Cascade {int offset,w,h,spacing,dirs;float start,end;};
void solveField(int count=3, bool dependencyMask=false, int4 dirty={0,0,0,0}) {
    buildMask();
    std::vector<Cascade> levels; int offset=0,spacing=1,dirs=4; float start=0,end=1;
    for(int i=0;i<count;i++) {
        int w=(_FieldSize.x+spacing-1)/spacing,h=(_FieldSize.y+spacing-1)/spacing;
        levels.push_back({offset,w,h,spacing,dirs,start,i==count-1?length(make_float2(_FieldSize)):end});
        offset+=w*h*dirs; spacing*=2; dirs=std::min(64,dirs*4); start=end; end*=4;
    }
    if (!dependencyMask || (int)_RadianceAtlas.size() != offset)
        _RadianceAtlas.assign(offset,uint3{});
    _CascadeChangedMask.assign(offset, 0);
    _DirtyRegions.assign(1, dirty);
    _DirtyRegionCount=dependencyMask ? 1 : 0;
    _CascadeMaskEnabled=dependencyMask ? 1 : 0;
    for(int i=count-1;i>=0;i--) {
        auto c=levels[i],f=levels[std::min(i+1,count-1)];
        _CascadeOffset=c.offset; _CascadeProbeSize={c.w,c.h}; _CascadeProbeSpacing=c.spacing;
        _CascadeDirectionCount=c.dirs; _CascadeInterval={c.start,c.end}; _CascadeEntryCount=c.w*c.h*c.dirs;
        _CascadeDispatchOrigin={0,0}; _CascadeDispatchSize={c.w,c.h};
        _FarCascadeOffset=f.offset; _FarCascadeProbeSize={f.w,f.h}; _FarCascadeProbeSpacing=f.spacing;
        _FarCascadeDirectionCount=f.dirs; _FarCascadeInterval={f.start,f.end}; _HasFarCascade=i<count-1;
        _CascadeDispatchRowWidth=_CascadeEntryCount;
        for(int n=0;n<_CascadeEntryCount;n++) SolveCascade(uint3{(uint)n,0,0});
    }
    _CascadeMaskEnabled=0;
    _DirtyRegionCount=0;
}
// Dynamic lights as the engine dispatches them: each dynamic light traced into its own tile
// (here a whole-field tile stacked vertically), then the tiles composed.
void solveDynamicLights(bool writeDirect=false) {
    int w=_FieldSize.x,h=_FieldSize.y,count=std::max(_DynamicLightCount,1);
    _DynamicTiles.reset(w,h*count); _DynamicTileInfos.clear();
    _DirectTexture.reset(w,h);
    _DynamicDispatchOrigin={0,0}; _DynamicDispatchSize=_FieldSize;
    _WriteDynamicDirect=(writeDirect && _DynamicLightCount==1) ? 1 : 0;
    for(_DynamicLightIndex=0;_DynamicLightIndex<_DynamicLightCount;_DynamicLightIndex++) {
        int radii=(int)std::ceil(std::sqrt((float)(w*w+h*h)))+8;
        int angles=std::max(64,(int)std::ceil(2*PI*radii));
        _DynamicPolar.reset(angles,radii*DynamicEmitterPointsPerAxis*DynamicEmitterPointsPerAxis); _DynamicPolarSize={angles,radii};
        for(_DynamicPolarPoint=0;_DynamicPolarPoint<DynamicEmitterPointsPerAxis*DynamicEmitterPointsPerAxis;_DynamicPolarPoint++)
            for(int a=0;a<angles;a++)TraceDynamicPolar(uint3{(uint)a,0,0});
        _DynamicPolarInput=_DynamicPolar;
        _DynamicTileOffset={0,h*_DynamicLightIndex};
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)SolveDynamicLighting(uint3{(uint)x,(uint)y,0});
        _DynamicTileInfos.push_back({{0,0},_FieldSize,_DynamicTileOffset});
    }
    _DynamicTilesInput=_DynamicTiles; _DynamicTileCount=_DynamicLightCount;
    if(_WriteDynamicDirect==0) {
        _ComposeOrigin={0,0}; _ComposeSize=_FieldSize;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)ComposeDynamicLighting(uint3{(uint)x,(uint)y,0});
    }
    _WriteDynamicDirect=0;
}
float direct(int x,int y) {
    float value=0;for(int d=0;d<4;d++)value+=UnpackRadiance(_RadianceAtlas[(y*_FieldSize.x+x)*4+d].xy).x*.25f;
    return value;
}
int main() {
    try {
        for(int scale: {1,2,3,4}) {
            setup(32,8,scale);
            float2 a=float2{2.5f,3.5f}*scale,b=float2{28.5f,3.5f}*scale;
            auto t=trace(a,b);
            near(t.x,std::exp(-.2f*26),2e-6f,"empty distance / resolution");
            near(t.y,std::exp(-.1f*26),2e-6f,"RGB independence");
            for(int y=0;y<_FieldSize.y;y++)for(int x=12*scale;x<13*scale;x++)
                _MaterialField.data[y*_FieldSize.x+x].w=1;
            t=trace(a,b);
            near(t.x,std::exp(-.2f*25-4),2e-6f,"one-cell wall");
            auto reverse=trace(b,a); near(t.x,reverse.x,2e-6f,"reciprocity");
            _SolidExtinctionRGB={1600,1600,1600,0};
            t=trace(a,b); near(t.x,0,1e-30f,"solid=1600 wall blocks");
            _EmissionField.data[(3*scale)*_FieldSize.x+20*scale]={16,16,16,0};
            float3 light; trace(a,b,&light); near(light.x,0,1e-30f,"source behind wall cannot bypass absorption");
        }
        setup(32,8); _EmptyExtinctionRGB={0,0,0,0};
        near(trace(float2{0,4},float2{32,4}).x,1,0,"zero absorption");
        for(float sigma: {0.f,1e-8f,1e-4f,.2f,4.f,1600.f}) {
            setup(8,8); _EmptyExtinctionRGB={sigma,sigma,sigma,0};
            for(auto& e:_EmissionField.data)e={1,1,1,0};
            float3 whole,left,right;
            auto tw=trace(float2{2,4},float2{3,4},&whole);
            auto tl=trace(float2{2,4},float2{2.37f,4},&left);
            auto tr=trace(float2{2.37f,4},float2{3,4},&right);
            near(whole.x,1,2e-5f,"one-cell source normalization");
            near(whole.x,left.x+tl.x*right.x,3e-5f,"emission split invariance");
            near(tw.x,tl.x*tr.x,2e-6f,"transmission split invariance");
        }
        setup(8,8); _MaterialField.data[2*8+3].w=1; _SolidExtinctionRGB={20,20,20,0};
        auto t=trace(float2{1.5f,2.5f},float2{6.5f,2.5f});
        _MaterialYFlip=1;
        auto flipped=trace(float2{1.5f,5.5f},float2{6.5f,5.5f});
        near(t.x,flipped.x,1e-9f,"material Y flip");
        // Independent line/box intersection oracle for non-axis-aligned rays.
        std::mt19937 random(7103); std::uniform_real_distribution<float> pos(.01f,31.99f);
        for(int i=0;i<3000;i++) {
            setup(32,32); _SolidExtinctionRGB={2,2,2,0};
            for(int y=0;y<32;y++)_MaterialField.data[y*32+16].w=1;
            float2 a={pos(random),pos(random)},b={pos(random),pos(random)};
            float d=length(b-a), solidLength=0;
            if(std::abs(b.x-a.x)>1e-8f) {
                float t0=(16-a.x)/(b.x-a.x),t1=(17-a.x)/(b.x-a.x);
                solidLength=max(0.f,min(1.f,max(t0,t1))-max(0.f,min(t0,t1)))*d;
            } else if(a.x>=16&&a.x<17) solidLength=d;
            near(trace(a,b).x,std::exp(-.2f*(d-solidLength)-2*solidLength),3e-5f,"random wall intersection");
        }
        // Clipped rays, exact grid boundaries, and near-axis directions must terminate safely.
        setup(8,8);
        for(float2 a: {float2{-3,4},float2{0,4},float2{8,4},float2{4,0},float2{4,8},float2{-2,-2}})
        for(float2 b: {float2{10,4},float2{0,4},float2{8,4},float2{4,0},float2{4,8},float2{10,10}})
            near(trace(a,b).x,std::exp(-.2f*length(b-a)),2e-6f,"clipped ray");
        // Entire production cascade merge and half-float atlas, not just exp().
        setup(24,24); _EmptyExtinctionRGB={.2f,.2f,.2f,0};
        _EmissionField.data[12*24+18]={16,16,16,0};
        solveField(); float open=direct(8,12);
        if(open<=0)throw std::runtime_error("cascade source missing in open air");
        for(int y=0;y<24;y++)_MaterialField.data[y*24+12].w=1;
        _SolidExtinctionRGB={1600,1600,1600,0}; solveField();
        float blocked=direct(8,12);
        _DirectInput.reset(24,24); _StaticDirectInput.reset(24,24);
        for(int y=0;y<24;y++)for(int x=0;x<24;x++) {
            float light=direct(x,y);_StaticDirectInput.data[y*24+x]={light,light,light,0};
        }
        _BounceSize={12,12}; _BounceTexture.reset(12,12);
        _BounceTaps.assign(12*12*16,float4{});
        for(int y=0;y<12;y++)for(int x=0;x<12;x++) BuildBounceTaps(uint3{(uint)x,(uint)y,0});
        for(int y=0;y<12;y++)for(int x=0;x<12;x++) SolveDiffuseBounce(uint3{(uint)x,(uint)y,0});
        _BounceInput=_BounceTexture;
        _BounceFilterWeights.assign(24*24*4,float4{});
        for(int y=0;y<24;y++)for(int x=0;x<24;x++) BuildBounceFilter(uint3{(uint)x,(uint)y,0});
        near(SampleBounceFiltered(int2{8,12},float2{8.5f/24,12.5f/24}).x,0,1e-6f,"bounce does not cross the wall");
        near(blocked,0,1e-6f,"full cascade wall occlusion");
        std::cout<<"Cascade fixture: open="<<open<<", solid1600="<<blocked<<"\n";
        _SolidExtinctionRGB={0,0,0,0}; solveField();
        if(direct(8,12)<open*.9f)throw std::runtime_error("solid coefficient has no effect");
        ++checks;
        float previous=direct(8,12);
        for(float solid: {.5f,2.f,8.f,1600.f}) {
            _SolidExtinctionRGB={solid,solid,solid,0};solveField();float value=direct(8,12);
            if(value>previous+1e-6f)throw std::runtime_error("solid sweep is not monotonic");
            previous=value;++checks;
        }
        for(auto& m:_MaterialField.data)m.w=0;
        previous=100;
        for(float empty: {.05f,.2f,.5f,1.f}) {
            _EmptyExtinctionRGB={empty,empty,empty,0};solveField();float value=direct(8,12);
            if(value<=0 || value>previous+1e-6f)throw std::runtime_error("empty sweep has a cutoff or is not monotonic");
            previous=value;++checks;
        }
        // Full four-cascade PerPixel fixture at four texels per world cell.
        setup(16,16,4);_SolidExtinctionRGB={1600,1600,1600,0};
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_EmissionField.data[y*64+x]={16,16,16,0};
        solveField(4);float pixelOpen=direct(18,34);
        if(pixelOpen<=0)throw std::runtime_error("four-cascade source missing");
        for(int y=0;y<64;y++)for(int x=32;x<36;x++)_MaterialField.data[y*64+x].w=1;
        solveField(4);near(direct(18,34),0,1e-6f,"four-cascade wall at four texels per cell");
        for(auto& m:_MaterialField.data)m.w=0;
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_MaterialField.data[y*64+x].w=1;
        solveField(4);
        if(direct(18,34)<pixelOpen*.25f)throw std::runtime_error("glowing solid suppressed by own extinction");
        ++checks;
        // Dependency mask must produce the exact same packed atlas as a full
        // solve after a localized geometry change.
        setup(16,16,4); _SolidExtinctionRGB={1600,1600,1600,0};
        for(int y=0;y<64;y++)for(int x=48;x<52;x++)_EmissionField.data[y*64+x]={16,16,16,0};
        solveField(4); auto atlasBefore=_RadianceAtlas;
        for(int y=0;y<64;y++)for(int x=32;x<36;x++)_MaterialField.data[y*64+x].w=1;
        solveField(4); auto atlasFull=_RadianceAtlas;
        _RadianceAtlas=atlasBefore;
        solveField(4,true,int4{32,0,36,64});
        if(!sameAtlas(_RadianceAtlas, atlasFull))
            throw std::runtime_error("dependency mask differs from full cascade solve");
        ++checks;
        setup(16,16,4);_SolidExtinctionRGB={1600,1600,1600,0};
        DynamicLight light={{12.5f,8.5f,0,0},{16,16,16,1}};
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_EmissionField.data[y*64+x]={16,16,16,0};
        textureReads=0;solveField(4);long cascadeReads=textureReads;
        _DynamicLights={light};_DynamicLightCount=1;
        textureReads=0;
        solveDynamicLights();
        long targetedReads=textureReads;
        std::cout<<"Moving light: cascade reads="<<cascadeReads<<", targeted reads="<<targetedReads
                 <<", reduction="<<(double)cascadeReads/targetedReads<<"x\n";
        if(targetedReads*5>=cascadeReads)throw std::runtime_error("targeted lighting did not remove cascade work");
        ++checks;
        for(float2 receiver: {float2{18.5f,34.5f},float2{18.5f,18.5f},float2{50.5f,18.5f}}) {
            float fast=GatherDynamicSource(receiver,light,8).x;
            float reference=GatherDynamicSource(receiver,light,1024).x;
            near(fast,reference,reference*.06f,"targeted quadrature against dense angular integration");
        }
        for(int y=0;y<64;y++)for(int x=32;x<36;x++)_MaterialField.data[y*64+x].w=1;
        buildMask();
        near(GatherDynamicSource(float2{18.5f,34.5f},light,8).x,0,1e-30f,"targeted light blocked by wall");
        for(auto& m:_MaterialField.data)m.w=0;
        buildMask();
        _DynamicLights={light};_DynamicLightCount=1;
        solveDynamicLights();
        float singleLight=_DirectTexture.Load(int3{18,34,0}).x;
        solveDynamicLights(true);
        near(_DirectTexture.Load(int3{18,34,0}).x,singleLight,1e-6f,"single light direct fast path");
        // Dynamic-centred rays against the exact per-pixel gather, beyond DynamicNearCells.
        for(int2 receiver: {int2{18,34},int2{18,18},int2{50,4},int2{8,60}}) {
            float reference=GatherDynamicSource(float2{receiver.x+.5f,receiver.y+.5f},light,8).x;
            near(_DirectTexture.Load(int3{receiver.x,receiver.y,0}).x,reference,reference*.03f+1e-7f,"emitter-centred rays agree with per-pixel gather");
        }
        _DynamicLights={light,light};_DynamicLightCount=2;
        solveDynamicLights();
        near(_DirectTexture.Load(int3{18,34,0}).x,2*singleLight,1e-6f,"two lights add without counting the other light along the ray");
        for(int y=0;y<64;y++)for(int x=32;x<36;x++)_MaterialField.data[y*64+x].w=1;
        buildMask();_DynamicLights={light};_DynamicLightCount=1;solveDynamicLights();
        near(_DirectTexture.Load(int3{18,34,0}).x,0,1e-30f,"emitter-centred rays blocked by wall");
        for(auto& m:_MaterialField.data)m.w=0;
        buildMask();_DynamicLights={light,light};_DynamicLightCount=2;
        _DynamicLights.clear();_DynamicLightCount=0;
        solveDynamicLights();near(_DirectTexture.Load(int3{18,34,0}).x,0,0,"removed light clears output");
        setup(2,2);_SolidExtinctionRGB={1600,1600,1600,0};
        _MaterialField.data[1].w=1;_MaterialField.data[2].w=1;
        _EmissionField.data[3]={16,16,16,0};
        float3 cornerLight;trace(float2{.5f,.5f},float2{1.5f,1.5f},&cornerLight);
        near(cornerLight.x,0,1e-30f,"emitter behind closed diagonal corner");
        // Surface reflection follows the light along the face texel by texel,
        // never one flat value per block, and still reaches only one cell deep.
        setup(8,4,4); _EmptyExtinctionRGB={.2f,.2f,.2f,0};
        for(int y=0;y<16;y++)for(int x=12;x<24;x++)_MaterialField.data[y*32+x].w=1;
        _DirectInput.reset(32,16); _StaticDirectInput.reset(32,16);
        for(int y=0;y<16;y++)for(int x=0;x<12;x++)_StaticDirectInput.data[y*32+x]={(float)y,(float)y,(float)y,0};
        float faceTransmission=std::exp(-.2f*.5f);
        for(int y: {1,6,13}) {
            near(SurfaceReflection(float2{12.5f,y+.5f},float3{1,1,1}).x,y*faceTransmission,1e-5f,"surface light follows the face per texel");
            near(SurfaceReflection(float2{15.5f,y+.5f},float3{1,1,1}).x,y*faceTransmission,1e-5f,"whole first cell reads its own row at the face");
            near(SurfaceReflection(float2{16.5f,y+.5f},float3{1,1,1}).x,0,0,"surface light stays one cell deep");
        }
        std::cout<<checks<<" transport checks passed (actual HLSL functions, float32).\n";
    } catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}
}
