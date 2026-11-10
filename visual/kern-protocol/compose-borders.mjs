// Lower independent CSS edges into existing Canvas + Line packets during authoring.
// The runtime renderer receives no CSS roles, custom properties or new protocol fields.
const zero=()=>({Top:0,Right:0,Bottom:0,Left:0});
const common=Type=>({Type,Style:null,OnClickContext:'',AttachedProperties:[]});
const geometry=(x,y,width,height)=>Object.entries({X:x,Y:y,Width:Math.max(.01,width),Height:Math.max(.01,height)}).map(([key,value])=>({Key:'Canvas.'+key,Value:String(Math.round(value*100)/100)}));
export function composeBorders(payload,box,edges){
 const signature=edge=>edge.Width===0?'none':edge.Width+':'+edge.Color;
 if(new Set(Object.values(edges).map(signature)).size<=1||!Object.values(edges).some(edge=>edge.Width>0&&edge.Stroke==='solid'))return {node:payload,lines:[]};
 const padding=payload.Style.Padding;
 if(!payload.Children&&Object.keys(padding).some(side=>padding[side]+Math.round(edges[side].Width)>255))throw Error('Border composition exceeds packet padding range');
 const outer={...common('CanvasPacket'),Style:{...payload.Style,BorderWidth:0,Border:'#00000000',Margin:zero(),Padding:zero()},AttachedProperties:payload.AttachedProperties,Children:[]};
 const originalStyle=payload.Style;payload.Style={...originalStyle,Background:'#00000000',BorderWidth:0,Border:'#00000000',Padding:{...padding}};
 if(payload.Children){for(const child of payload.Children)for(const [axis,side]of [['X','Left'],['Y','Top']]){const property=child.AttachedProperties.find(p=>p.Key==='Canvas.'+axis);if(property)property.Value=String(Math.round((Number(property.Value)+edges[side].Width)*100)/100);}}
 else for(const side of Object.keys(padding))payload.Style.Padding[side]+=Math.round(edges[side].Width);
 payload.AttachedProperties=geometry(0,0,box.Width,box.Height);
 const lines=[];for(const [side,edge]of Object.entries(edges)){
  if(!edge.Width||edge.Stroke!=='solid')continue;
  const horizontal=side==='Top'||side==='Bottom',width=horizontal?box.Width:edge.Width,height=horizontal?edge.Width:box.Height;
  const line={...common('LinePacket'),Direction:horizontal?'Horizontal':'Vertical',Style:{Background:edge.Color,Border:'#00000000',BorderWidth:Math.round(edge.Width),Margin:zero(),Padding:zero()},AttachedProperties:geometry(side==='Right'?box.Width-edge.Width:0,side==='Bottom'?box.Height-edge.Width:0,width,height)};
  lines.push({node:line,side});
 }
 // The original widget stays on top, so its borders remain clickable as part of the widget.
 outer.Children=[...lines.map(line=>line.node),payload];return {node:outer,payload,lines};
}
