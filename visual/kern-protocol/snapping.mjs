// Editor geometry only. No snapping metadata is serialized into protocol packets.
export function snapRect(rect,{mode='none',step=8,tolerance=6,targets=[],resize=false}={}){
 const result={...rect},guides=[];const grid=mode==='grid'||mode==='both',objects=mode==='objects'||mode==='both';
 for(const axis of ['x','y']){
  const dimension=axis==='x'?'width':'height',origin=rect[axis],size=rect[dimension];
  const moving=resize?[origin+size]:[origin,origin+size/2,origin+size];let best=null;
  if(objects)for(const target of targets){const anchors=[target[axis],target[axis]+target[dimension]/2,target[axis]+target[dimension]];for(const anchor of anchors)for(const value of moving){const delta=anchor-value;if(Math.abs(delta)<=tolerance&&(!best||Math.abs(delta)<Math.abs(best.delta)))best={delta,anchor};}}
  if(best){if(resize)result[dimension]=Math.max(1,size+best.delta);else result[axis]+=best.delta;guides.push({axis,value:best.anchor});}
  else if(grid&&Number.isFinite(step)&&step>0){const anchor=Math.round((resize?origin+size:origin)/step)*step;if(resize)result[dimension]=Math.max(1,anchor-origin);else result[axis]=anchor;guides.push({axis,value:anchor});}
 }
 return {rect:result,guides};
}
