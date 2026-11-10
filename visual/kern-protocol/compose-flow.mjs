// Geometry-based authoring conversion. No window names or CSS roles choose layout.
const zero=()=>({Top:0,Right:0,Bottom:0,Left:0});
const clearGeometry=child=>{child.AttachedProperties=child.AttachedProperties.filter(p=>!p.Key.startsWith('Canvas.')&&!p.Key.startsWith('Grid.')&&p.Key!=='DockPanel.Dock');};
const setStyle=node=>node.Style||(node.Style={Background:'#00000000',Border:'#00000000',BorderWidth:0,Margin:zero(),Padding:zero()});
const encodable=value=>value>=-.1&&value<=255&&Math.abs(value-Math.round(value))<=.4;
export function composeTextFlow(node,box,boxes,allocated=new Map()){
 if(node.Type!=='CanvasPacket'||node.Children?.length<2||node.Children.some(child=>!['TextPacket','GridPacket','DockPanelPacket'].includes(child.Type)||child.OnClickContext||child.Type!=='TextPacket'&&child.AttachedProperties.some(p=>p.Key==='Canvas.Height')))return null;
 if(node.Style?.BorderWidth||Object.values(node.Style?.Padding||{}).some(value=>value!==0))return null;
 const children=node.Children,rects=children.map(child=>boxes.get(child));if(rects.some(rect=>!rect))return null;
 for(const child of children)if(child.Style?.BorderWidth||Object.values(child.Style?.Padding||{}).some(value=>value!==0))return null;
 const vertical=rects.every(rect=>Math.abs(rect.X-rects[0].X)<.1&&Math.abs(rect.Width-rects[0].Width)<.1);
 const horizontal=children.every(child=>child.Type==='TextPacket')&&rects.every(rect=>Math.abs(rect.Y+rect.Height/2-(rects[0].Y+rects[0].Height/2))<.1);
 if(!vertical&&!horizontal)return null;
 const insets={Top:Math.min(...rects.map(r=>r.Y))-box.Y,Left:Math.min(...rects.map(r=>r.X))-box.X,Right:box.X+box.Width-Math.max(...rects.map(r=>r.X+r.Width)),Bottom:box.Y+box.Height-Math.max(...rects.map(r=>r.Y+r.Height))};
 const gaps=rects.slice(1).map((rect,i)=>vertical?rect.Y-rects[i].Y-rects[i].Height:rect.X-rects[i].X-rects[i].Width);
 if(![...Object.values(insets),...gaps].every(encodable))return null;
 setStyle(node).Padding=Object.fromEntries(Object.entries(insets).map(([key,value])=>[key,Math.max(0,Math.round(value))]));node.AttachedProperties=node.AttachedProperties.filter(p=>p.Key!=='Canvas.Height');
 if(vertical){node.Type='DockPanelPacket';children.forEach((child,index)=>{clearGeometry(child);child.AttachedProperties.push({Key:'DockPanel.Dock',Value:'Top'});setStyle(child).Margin={...zero(),Bottom:index<gaps.length?Math.max(0,Math.round(gaps[index])):0};});}
 else{
  node.Type='GridPacket';node.Rows=[0];node.Columns=[];const spacers=[],largest=Math.max(...rects.map(rect=>rect.Width)),equal=rects.every(rect=>Math.abs(rect.Width-rects[0].Width)<.1);
  children.forEach((child,index)=>{allocated.set(child,{...rects[index],Y:box.Y+Math.max(0,Math.round(insets.Top)),Height:Math.max(...rects.map(r=>r.Height))});clearGeometry(child);child.AttachedProperties.push({Key:'Grid.Row',Value:'0'},{Key:'Grid.Column',Value:String(node.Columns.length)});setStyle(child).Margin=zero();node.Columns.push(equal?1:Math.max(1,Math.round(rects[index].Width/largest*255)));const gap=Math.max(0,Math.round(gaps[index]||0));if(gap){spacers.push({Type:'ImagePacket',URI:'',Width:gap,Height:0,Style:{Background:'#00000000',Border:'#00000000',BorderWidth:0,Margin:zero(),Padding:zero()},OnClickContext:'',AttachedProperties:[{Key:'Grid.Row',Value:'0'},{Key:'Grid.Column',Value:String(node.Columns.length)}]});node.Columns.push(0);}});node.Children=[...children,...spacers];
 }
 return node;
}
