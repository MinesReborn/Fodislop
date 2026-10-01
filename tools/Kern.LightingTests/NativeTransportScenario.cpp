int checks=0;
void near(float got,float expected,float tolerance,const char* label) {
    ++checks;
    if(!std::isfinite(got) || std::abs(got-expected)>tolerance)
        throw std::runtime_error(std::string(label)+": got "+std::to_string(got)+", expected "+std::to_string(expected));
}
void setup(int w,int h,int scale=1) {
    _FieldSize={w*scale,h*scale}; _LightSize=_FieldSize; _FieldTexelsPerLightTexel=1; _WorldRect={0,0,(float)w,(float)h};
    _MaterialField.reset(w*scale,h*scale); _EmissionField.reset(w*scale,h*scale);
    _LightingCounters.assign(3, 0);
    _DirtyRegions.assign(1, int4{0,0,0,0});
    _CascadeChangedMask.clear();
    _DirtyRegionCount=0; _CascadeMaskEnabled=0;
    _MaterialYFlip=0; _EmptyExtinctionRGB={.2f,.1f,.4f,0}; _SolidExtinctionRGB={4.f,8.f,16.f,0};
    _DynamicPolarScalarExtinction=0;
    _NeutralExtinction=0;
    _DynamicTilesScalarRadiance=0;
}
// Rebuilds the per-cell solid mask from the current material fixture, as the
// engine does whenever the material field is redrawn.
void buildMask() {
    _CellGridSize={(int)(_WorldRect.z/_CellSize),(int)(_WorldRect.w/_CellSize)};
    _CellSolidMaskOutput.reset(_CellGridSize.x,_CellGridSize.y);
    for(int y=0;y<_CellGridSize.y;y++)for(int x=0;x<_CellGridSize.x;x++)BuildCellSolidMask(uint3{(uint)x,(uint)y,0});
    _CellSolidMask=_CellSolidMaskOutput;
}
// Rebuilds the per-texel surface air cache from the current material fixture,
// as the engine does whenever the material field is redrawn.
void buildAirCache() {
    buildMask();
    _SurfaceAirCacheOutput.reset(_FieldSize.x,_FieldSize.y);
    for(int y=0;y<_FieldSize.y;y++)for(int x=0;x<_FieldSize.x;x++)BuildSurfaceAirCache(uint3{(uint)x,(uint)y,0});
    _SurfaceAirCache=_SurfaceAirCacheOutput;
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
void solveDynamicLights(bool writeDirect=false,
    int2 receiverOrigin={0,0},int2 receiverSize={0,0}) {
    int w=_FieldSize.x,h=_FieldSize.y,count=std::max(_DynamicLightCount,1);
    _DirectTexture.reset(w,h);
    _DynamicDispatchOrigin=receiverOrigin;
    _DynamicDispatchSize=receiverSize.x>0 && receiverSize.y>0 ? receiverSize : _FieldSize;
    _WriteDynamicDirect=(writeDirect && _DynamicLightCount==1) ? 1 : 0;
    if (_WriteDynamicDirect!=0) _DynamicTiles.reset(1,1);
    else _DynamicTiles.reset(w,h,count);
    _DynamicTileInfos.clear();
    for(_DynamicLightIndex=0;_DynamicLightIndex<_DynamicLightCount;_DynamicLightIndex++) {
        // Model persistent slots independently of the uploaded source order.
        _DynamicReachIndex = (_DynamicLightIndex + 1) % count;
        _DynamicPolarLayerOffset = 0;
        int radii=(int)std::ceil(std::sqrt((float)(w*w+h*h)))+8;
        // Match the authored production fan; angular density does not depend
        // on the ray length or this supplementary fixture's resolution.
        int angles=64;
        int layers=_DynamicEmitterPointsPerAxis*_DynamicEmitterPointsPerAxis;
        // Полярная текстура несёт по одной обёрточной колонке с каждого края
        // (WriteDynamicPolar), поэтому её ширина на две колонки больше числа лучей.
        _DynamicPolar.reset(angles+2,radii,layers); _DynamicPolarSize={angles,radii}; _DynamicPolarTextureSize={angles+2,radii};
        for(int point=0;point<_DynamicEmitterPointsPerAxis*_DynamicEmitterPointsPerAxis;point++)
            for(int a=0;a<angles;a++)TraceDynamicPolar(uint3{(uint)a,(uint)point,0});
        _DynamicPolarInput=_DynamicPolar;
        _DynamicTileOffset={0,0};
        for(int y=0;y<_DynamicDispatchSize.y;y++)for(int x=0;x<_DynamicDispatchSize.x;x++)
            SolveDynamicLighting(uint3{(uint)x,(uint)y,0});
        _DynamicTileInfos.push_back({_DynamicDispatchOrigin,_DynamicDispatchSize,_DynamicTileOffset,_DynamicReachIndex,0});
    }
    _DynamicTilesInput=_DynamicTiles; _DynamicTileCount=_DynamicLightCount;
    if(_WriteDynamicDirect==0) {
        _ComposeOrigin=_DynamicDispatchOrigin; _ComposeSize=_DynamicDispatchSize;
        for(int y=0;y<_ComposeSize.y;y++)for(int x=0;x<_ComposeSize.x;x++)
            ComposeDynamicLighting(uint3{(uint)x,(uint)y,0});
    }
    _WriteDynamicDirect=0;
}
float direct(int x,int y) {
    float value=0;for(int d=0;d<4;d++)value+=UnpackRadiance(_RadianceAtlas[(y*_FieldSize.x+x)*4+d].xy).x*.25f;
    return value;
}
// Independent continuous-square integral in uniform air. The receiver is
// horizontally to the right of the emitter; neither field clipping nor the
// production gather is used to define the expected result.
float airSquareRadiance(double sourceX,double receiverX,double extinction,double radiance) {
    double separation=receiverX-sourceX;
    double halfAngle=std::atan(.5/(separation-.5));
    double sum=0;
    for(int sample=0;sample<8;sample++) {
        double angle=(sample+.5)*2*halfAngle/8-halfAngle;
        double dx=std::cos(angle),dy=std::abs(std::sin(angle));
        double entry=(separation-.5)/dx;
        double exit=std::min((separation+.5)/dx,.5/dy);
        if(exit>entry) {
            double weight=extinction==0 ? exit-entry :
                -std::expm1(-extinction*(exit-entry))/-std::expm1(-extinction);
            sum+=std::exp(-extinction*entry)*radiance*weight;
        }
    }
    return float(sum*halfAngle/(8*std::acos(-1.0)));
}
int main() {
    try {
        // Independent double-precision attenuation/integral, with colored HDR
        // radiance. Sharing neutral-medium math must not share source color.
        for(int scalar: {0,1})for(float extinction: {0.f,.0001f,.2f,4.f})for(float distance: {.03125f,.5f,3.f}) {
            _NeutralExtinction=scalar;
            float3 source={16.f,4.f,.5f};
            float3 actual=OpticalDepthTransmission(float3{extinction,extinction,extinction}*distance)*source;
            double transmission=std::exp(-double(extinction)*distance);
            float3 weight=MediumEmissionWeight(float3{extinction,extinction,extinction},distance);
            double expectedWeight=extinction==0 ? distance :
                -std::expm1(-double(extinction)*distance)/-std::expm1(-double(extinction));
            for(int channel=0;channel<3;channel++) {
                near(actual[channel],float(transmission*source[channel]),2e-6f,"neutral extinction preserves colored HDR");
                near(weight[channel],float(expectedWeight),2e-6f,"neutral emission has independent continuous integral");
            }
        }
        _DynamicPolarScalarExtinction=0;
        _NeutralExtinction=0;
        for(float sourceX: {-.75f,-.25f,0.f,.25f,.75f}) {
            setup(8,4,32); _EmissionScale=12;
            buildMask();
            DynamicLight source={{sourceX,1.515625f,0,0},{1,.5f,.25f,1}};
            _DynamicLights={source}; _DynamicLightCount=1;
            for(int pixelX: {48,240}) {
                float receiverX=(pixelX+.5f)/32;
                float3 result=GatherDynamicSource(float2{pixelX+.5f,48.5f},source,8);
                for(int channel=0;channel<3;channel++) {
                    float expected=airSquareRadiance(sourceX,receiverX,_EmptyExtinctionRGB[channel],
                        12*source.colorIntensity[channel]);
                    near(result[channel],expected,.0003f,"field boundary never clips a continuous dynamic emitter");
                }
                solveDynamicLights(false,int2{pixelX,48},int2{1,1});
                float3 dynamic=_DirectTexture.Load(int3{pixelX,48,0}).xyz;
                for(int channel=0;channel<3;channel++) {
                    float expected=airSquareRadiance(sourceX,receiverX,_EmptyExtinctionRGB[channel],
                        12*source.colorIntensity[channel]);
                    near(dynamic[channel],expected,expected*.03f+1e-7f,
                        "production near/polar receiver preserves an outside-field emitter");
                }
            }
        }
        _EmissionScale=1;
        setup(12,4,4); _EmptyExtinctionRGB={.2f,.2f,.2f,0}; _SolidExtinctionRGB={1,1,1,0}; _EmissionScale=12;
        buildMask();
        _DynamicLights={
            DynamicLight{{1.5f,1.375f,0,0},{1,.3f,.11f,.8f}},
            DynamicLight{{3.125f,1.375f,0,0},{0,.5f,1,3}},
            DynamicLight{{5.25f,1.375f,0,0},{.2f,1,0,1.3f}}};
        _DynamicLightCount=3;
        for(int scalar: {0,1}) {
            _DynamicTilesScalarRadiance=scalar;
            solveDynamicLights();
            float3 result=_DirectTexture.Load(int3{25,5,0}).xyz;
            for(int channel=0;channel<3;channel++) {
                double expected=0;
                for(auto source:_DynamicLights)
                    expected+=airSquareRadiance(source.positionRadius.x,6.375,.2,
                        12*source.colorIntensity[channel]*source.colorIntensity.w);
                near(result[channel],float(expected),.0003f,"scalar RGB cache preserves independent colored HDR sum");
            }
        }
        _EmissionScale=1;
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
        {
            DynamicLight before={{12.02f,8.5f,0,0},{1,1,1,1}};
            DynamicLight after={{12.04f,8.5f,0,0},{1,1,1,1}};
            float2 beforePoint, afterPoint;
            float beforeArea, afterArea;
            bool beforeEmits, afterEmits;
            DynamicEmitterPoint(before,4,beforePoint,beforeArea,beforeEmits);
            DynamicEmitterPoint(after,4,afterPoint,afterArea,afterEmits);
            float expectedTexelDelta = (after.positionRadius.x - before.positionRadius.x) *
                _FieldSize.x / _WorldRect.z;
            near(afterPoint.x - beforePoint.x, expectedTexelDelta, 1e-5f,
                "dynamic emitter follows sub-texel source movement");
            if(!beforeEmits || !afterEmits)
                throw std::runtime_error("Moving dynamic emitter unexpectedly left the field");
        }
        DynamicLight light={{12.5f,8.5f,0,0},{16,16,16,1}};
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_EmissionField.data[y*64+x]={16,16,16,0};
        textureReads=0;solveField(4);long cascadeReads=textureReads;
        _DynamicLights={light};_DynamicLightCount=1;
        textureReads=0;
        // Production applies dynamic radiance to the visible receiver window,
        // retaining the full stable field for transport. Ten visible cells in
        // this 16-cell field correspond to this 40-texel supplementary fixture.
        solveDynamicLights(false,int2{12,12},int2{40,40});
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
        near(_DynamicTiles.Load(int3{0,0,0}).w,0,0,"single light never writes its one-texel binding");
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
        // Supplementary arithmetic proof using the actual HLSL gather and an
        // independent continuous-square integral. This does not replace GPU
        // production-camera verification.
        setup(4,4,32); _EmptyExtinctionRGB={.2f,.2f,.2f,0}; _EmissionScale=12;
        buildMask();
        for(float offset: {-.375f,-.25f,-.125f,-.0625f,0.f,.0625f,.125f,.25f,.375f}) {
            DynamicLight source={{1.515625f+offset,1.515625f,0,0},{1,0,0,1}};
            double expected=0;
            for(int sample=0;sample<8;sample++) {
                double angle=-std::acos(-1.0)+(sample+.5)*2*std::acos(-1.0)/8;
                double dx=std::cos(angle),dy=std::sin(angle);
                double exitX=(dx>0 ? offset+.5 : offset-.5)/dx;
                double distance=std::min(exitX,.5/std::abs(dy));
                expected+=12*(1-std::exp(-.2*distance))/(1-std::exp(-.2))/8;
            }
            near(GatherDynamicSource(float2{48.5f,48.5f},source,8).x,float(expected),.0003f,
                "dense continuous source preserves HDR at its center and between texels");
        }
        _EmissionScale=1;
        // Independent Beer-Lambert checks for exhaustive constant-occupancy
        // proofs. Dynamic transport must ignore unrelated static emitters;
        // static transport must still visit even one emissive base texel.
        for(float alpha: {0.f, 64.f/255.f, 204.f/255.f, 1.f}) {
            setup(4,2,32);
            for(auto& m:_MaterialField.data)m.w=alpha;
            _EmissionField.data[32*128+64]={8,0,0,0};
            buildMask();
            near(_CellSolidMask.Load(int3{2,1,0}).w,1,0,"dynamic uniform proof tolerates static emission");
            near(_CellSolidMask.Load(int3{2,1,0}).y,0,0,"one emissive texel excludes static jump");
            float3 r,transmission;
            _LightingCountersEnabled=1;
            _LightingCounters.assign(3,0);
            TraceLightSegment(float2{.5f,32.5f},float2{127.5f,32.5f},false,true,
                float4{},float3{},r,transmission);
            float expected=std::exp(-(.2f+(4.f-.2f)*alpha)*127.f/32.f);
            near(transmission.x,expected,2e-6f,"uniform occupancy keeps original UNorm alpha");
            if(_LightingCounters[1]>4)throw std::runtime_error("constant extinction still marched individual texels");
            auto optimized=transmission;
            _UniformCellTraversalEnabled=0;
            TraceLightSegment(float2{.5f,32.5f},float2{127.5f,32.5f},false,true,
                float4{},float3{},r,transmission);
            near(transmission.x,optimized.x,3e-6f,"constant cell equals exhaustive texel reference");
            _UniformCellTraversalEnabled=1;
            _LightingCountersEnabled=0;
        }
        setup(4,2,32); _SolidExtinctionRGB={1600,1600,1600,0};
        _MaterialField.data[32*128+64].w=1;
        buildMask();
        near(_CellSolidMask.Load(int3{2,1,0}).w,0,0,"one texel blocker excludes cell jump");
        near(trace(float2{.5f,32.5f},float2{127.5f,32.5f}).x,0,1e-20f,
            "one of 1024 texels still casts its full shadow");
        // Surface reflection follows the light along the face texel by texel,
        // never one flat value per block. Its authored half-cell depth fades
        // per receiver; it must not copy a face value through a whole cell.
        // It now reads the cached first-air texel per direction and takes the
        // centre incident light from the caller.
        setup(8,4,4); _EmptyExtinctionRGB={.2f,.2f,.2f,0};
        for(int y=0;y<16;y++)for(int x=12;x<24;x++)_MaterialField.data[y*32+x].w=1;
        _DirectInput.reset(32,16); _StaticDirectInput.reset(32,16);
        for(int y=0;y<16;y++)for(int x=0;x<12;x++)_StaticDirectInput.data[y*32+x]={(float)y,(float)y,(float)y,0};
        buildAirCache();
        // At density four the first solid receiver is 1/4 cell from air:
        // halfway through the half-cell reflection support, with weight 1/2.
        float faceTransmission=.5f*std::exp(-.2f*.25f);
        for(int y: {1,6,13}) {
            near(SurfaceIncidentLighting(int2{12,y},float3{0,0,0}).x,y*faceTransmission,1e-5f,"surface light follows the face per texel");
            near(SurfaceIncidentLighting(int2{15,y},float3{0,0,0}).x,0,0,"receiver outside authored surface depth stays dark");
            near(SurfaceIncidentLighting(int2{16,y},float3{0,0,0}).x,0,0,"surface light stays within authored depth");
        }
        setup(4,2,32); _EmptyExtinctionRGB={.2f,.2f,.2f,0};
        for(int y=0;y<64;y++)for(int x=64;x<128;x++)_MaterialField.data[y*128+x].w=1;
        _DirectInput.reset(128,64); _StaticDirectInput.reset(128,64);
        for(int y=0;y<64;y++)for(int x=0;x<64;x++)_StaticDirectInput.data[y*128+x]={8,8,8,0};
        buildAirCache();
        float previousReflection=8;
        for(int depth=1;depth<=32;depth++) {
            double position=depth/32.0;
            double t=std::min(position/.5,1.0);
            float expected=float(8*(1-3*t*t+2*t*t*t)*std::exp(-.2*position));
            float actual=SurfaceIncidentLighting(int2{63+depth,32},float3{0,0,0}).x;
            near(actual,expected,1e-5f,"dense surface depth has independent per-texel falloff");
            if(actual>previousReflection)throw std::runtime_error("surface reflection increased behind the exposed face");
            previousReflection=actual;
        }
        std::cout<<checks<<" transport checks passed (actual HLSL functions, float32).\n";
    } catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}
}
