// Browser counterpart of PacketGridLayout.Measure. Positive tracks are fractions, zero is Auto.
export function measureGrid(columns,rows,items,width,height){
 function tracks(definitions,available,index,span,size){
  const result=definitions.map(()=>0);let used=0,total=0;
  definitions.forEach((value,i)=>{if(value>0){total+=value;return;}result[i]=Math.max(0,...items.filter(item=>item[index]===i&&item[span]===1).map(item=>item[size]));used+=result[i];});
  for(const item of items){if(item[span]<=1)continue;const first=item[index],last=Math.min(first+item[span],result.length);let allocated=0,auto=0;for(let i=first;i<last;i++){allocated+=result[i];if(definitions[i]===0)auto++;}const deficit=item[size]-allocated;if(auto!==item[span]||deficit<=0)continue;for(let i=first;i<last;i++)if(definitions[i]===0){result[i]+=deficit/auto;used+=deficit/auto;}}
  if(total>0)definitions.forEach((value,i)=>{if(value>0)result[i]=value/total*Math.max(0,available-used);});return result;
 }
 const starts=values=>values.reduce((all,v)=>[...all,all.at(-1)+v],[0]);
 const x=starts(tracks(columns,width,'Column','ColumnSpan','Width')),y=starts(tracks(rows,height,'Row','RowSpan','Height'));
 return items.map(item=>({Left:x[item.Column],Top:y[item.Row],Width:x[item.Column+item.ColumnSpan]-x[item.Column],Height:y[item.Row+item.RowSpan]-y[item.Row]}));
}
export function layoutGrid(element,packet){
 const children=[...element.children];if(element.style.position!=='absolute')element.style.position='relative';
 const value=(n,key,fallback)=>Number(n.AttachedProperties?.find(p=>p.Key===key)?.Value??fallback);
 const items=children.map((el,i)=>{const s=getComputedStyle(el),rect=el.getBoundingClientRect(),zoom=document.getElementById('artboard').getBoundingClientRect().width/document.getElementById('artboard').offsetWidth;return {Row:value(packet.Children[i],'Grid.Row',0),Column:value(packet.Children[i],'Grid.Column',0),RowSpan:value(packet.Children[i],'Grid.RowSpan',1),ColumnSpan:value(packet.Children[i],'Grid.ColumnSpan',1),Width:rect.width/zoom+parseFloat(s.marginLeft)+parseFloat(s.marginRight),Height:rect.height/zoom+parseFloat(s.marginTop)+parseFloat(s.marginBottom)};});
 const s=getComputedStyle(element),rects=measureGrid(packet.Columns,packet.Rows,items,element.clientWidth-parseFloat(s.paddingLeft)-parseFloat(s.paddingRight),element.clientHeight-parseFloat(s.paddingTop)-parseFloat(s.paddingBottom));
 rects.forEach((r,i)=>{const child=children[i],s=getComputedStyle(child);Object.assign(child.style,{position:'absolute',left:r.Left+'px',top:r.Top+'px',width:Math.max(0,r.Width-parseFloat(s.marginLeft)-parseFloat(s.marginRight))+'px',height:Math.max(0,r.Height-parseFloat(s.marginTop)-parseFloat(s.marginBottom))+'px'});});
 element.style.minHeight=Math.max(0,...rects.map(r=>r.Top+r.Height))+'px';
}
