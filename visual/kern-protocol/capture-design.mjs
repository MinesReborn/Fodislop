import {composeTextFlow} from './compose-flow.mjs';
import {composeBorders} from './compose-borders.mjs';
import {plainText} from './rich-text.mjs';
// Author desktop packet fixtures from measured reference geometry. No role or window-name dispatch.
import fs from 'node:fs';
import {chromium} from '@playwright/test';
import {validatePacket} from './validate.mjs';
const fonts=JSON.parse(fs.readFileSync(new URL('./font-catalog.json',import.meta.url)));
const fixtures=JSON.parse(fs.readFileSync(new URL('./reference-packets.json',import.meta.url)));
const browser=await chromium.launch({headless:true});
try {
const page=await browser.newPage({viewport:{width:1600,height:1000}});
await page.route('**/js/srvgui.js',async route=>{
 const response=await route.fetch();let source=await response.text();
 source=source.replace('content.replaceChildren(render(def.root));',`(function mark(n,path='Content'){n.__path=path;(n.kids||[]).forEach((c,i)=>mark(c,path+'/Children/'+i));})(def.root);content.replaceChildren(render(def.root));`);
 source=source.replace('    return e;\n  }\n\n  // DockPanel',"    e.dataset.packetPath=n.__path; e.__reference=n; return e;\n  }\n\n  // DockPanel");
 await route.fulfill({response,body:source});
});
await page.goto('http://127.0.0.1:8791/visual/kern-ui/?srvwin=market');
await page.waitForSelector('[data-packet-path]');await page.evaluate(()=>document.fonts.ready);
const output={};
for(const [name,fixture]of Object.entries(fixtures)){
 await page.evaluate(name=>window.openSrvWin(name),name);await page.evaluate(()=>document.fonts.ready);
 const capture=await page.evaluate(fonts=>{
 const frame=document.querySelector('.srv-pk'),content=frame.querySelector('.srv-pk-content');
 const frameRect=frame.getBoundingClientRect(),scaleX=frameRect.width/frame.offsetWidth,scaleY=frameRect.height/frame.offsetHeight;
 const box=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return {X:r.x/scaleX,Y:r.y/scaleY,Width:r.width/scaleX,Height:r.height/scaleY,BorderLeft:parseFloat(s.borderLeftWidth),BorderTop:parseFloat(s.borderTopWidth)};};
 function pseudo(el,name){
  const probe=document.createElement('div'),computed=getComputedStyle(el);probe.style.cssText='position:absolute;visibility:hidden;min-width:0;min-height:0;padding:0;border:0;box-sizing:border-box';el.parentElement.append(probe);
  for(const key of computed)if(key.startsWith('--'))probe.style.setProperty(key,computed.getPropertyValue(key));
  function visit(rules){for(const rule of rules){if(rule.selectorText?.includes(name)&&el.matches(rule.selectorText.split(name).join(''))){probe.style.cssText+=';'+rule.style.cssText;}else if(rule.styleSheet)visit(rule.styleSheet.cssRules);else if(rule.cssRules&&(!rule.conditionText||!rule.media||matchMedia(rule.conditionText).matches))visit(rule.cssRules);}}
  for(const sheet of document.styleSheets)visit(sheet.cssRules);
  const s=getComputedStyle(probe),result={Width:parseFloat(s.width),Height:parseFloat(s.height),Background:s.backgroundColor,Border:s.borderTopColor,BorderWidth:parseFloat(s.borderTopWidth),ClipPath:s.clipPath};probe.remove();return result;
 }
 const nodes={};
 for(const el of content.querySelectorAll('[data-packet-path]')){
  const n=el.__reference,s=getComputedStyle(el),gaps=[];
  const sides=['Top','Right','Bottom','Left'];
  const style={Background:s.backgroundColor,Border:s.borderTopColor,BorderWidth:parseFloat(s.borderTopWidth),Edges:Object.fromEntries(sides.map(side=>[side,{Width:parseFloat(s['border'+side+'Width']),Color:s['border'+side+'Color'],Stroke:s['border'+side+'Style']}])),Margin:Object.fromEntries(sides.map(k=>[k,parseFloat(s['margin'+k])])),Padding:Object.fromEntries(sides.map(k=>[k,parseFloat(s['padding'+k])]))};
  for(const [key,value,empty]of [['backgroundImage',s.backgroundImage,'none'],['boxShadow',s.boxShadow,'none'],['clipPath',s.clipPath,'none'],['transform',s.transform,'none'],['borderRadius',s.borderRadius,'0px']])if(value!==empty)gaps.push(key+': '+value);

  for(const pseudo of ['::before','::after']){const ps=getComputedStyle(el,pseudo);if(!['none','normal'].includes(ps.content)&&ps.display!=='none')gaps.push('Псевдоэлемент '+pseudo+' не является компонентом пакета.');}
  let text;const textRuns=[],parts=[];
  if(n.t==='Text'||n.t==='Selectable'){
   const target=n.t==='Text'?el:el.querySelector('span');
   function rich(e){return [...e.childNodes].map(c=>{if(c.nodeType===3){let value=c.textContent;const cs=getComputedStyle(e),family=cs.fontFamily.split(',')[0].trim().replaceAll('"',''),font=Object.entries(fonts).find(([,asset])=>asset.Family===family);if(!font)throw Error('Font asset unavailable: '+family);if(cs.textTransform==='uppercase')value=value.toUpperCase();const hex=cs.color.match(/[\d.]+/g).slice(0,3).map(x=>Math.round(+x).toString(16).padStart(2,'0')).join('');textRuns.push({Text:value,Family:family,Size:parseFloat(cs.fontSize),Weight:Number(cs.fontWeight),LineHeight:parseFloat(cs.lineHeight)||null,LetterSpacing:parseFloat(cs.letterSpacing)||0});let result='<size='+parseFloat(cs.fontSize)+'><color=#'+hex+'>'+value+'</color></size>';result='<font-weight='+cs.fontWeight+'>'+result+'</font-weight>';if(cs.letterSpacing!=='normal')result='<cspace='+parseFloat(cs.letterSpacing)+'>'+result+'</cspace>';if(cs.whiteSpace==='nowrap')result='<nobr>'+result+'</nobr>';if(cs.lineHeight!=='normal')result='<line-height='+parseFloat(cs.lineHeight)+'>'+result+'</line-height>';if(cs.fontStyle==='italic')result='<i>'+result+'</i>';result='<font='+font[0]+'>'+result+'</font>';if(value.trim()){const range=document.createRange();range.selectNodeContents(c);const r=range.getBoundingClientRect();parts.push({Text:'<align=left>'+result+'</align>',TextRuns:[textRuns.at(-1)],Box:{X:r.x/scaleX,Y:r.y/scaleY,Width:r.width/scaleX,Height:r.height/scaleY,BorderLeft:0,BorderTop:0}});}return result;}return c.nodeType===1?(c.tagName==='BR'?'\n':rich(c)):'';}).join('');}
   text='<align='+(['left','center','right'].includes(s.textAlign)?s.textAlign:'left')+'>'+rich(target)+'</align>';
  }
  nodes[el.dataset.packetPath]={Box:box(el),Style:style,Text:text,TextRuns:textRuns,TextParts:parts,Disabled:!!el.disabled,Parts:n.t==='Selectable'?[...el.children].map(box):null,Knob:n.t==='Slider'?pseudo(el,'::-webkit-slider-thumb'):null,Gaps:gaps};
 }
 return {Frame:box(frame),Content:box(content),Nodes:nodes};
 },fonts);
 const gaps=[],measured=new Map(),layerNames=new Map(),typography=new Map(),allocated=new Map(),glyphs=new Map();const gap=(Path,Feature,Reason)=>gaps.push({Path,Feature,Reason});
 const zero=()=>({Top:0,Right:0,Bottom:0,Left:0});
 const color=value=>{const parts=value.match(/[\d.]+/g).map(Number);return '#'+parts.slice(0,3).map(x=>Math.round(x).toString(16).padStart(2,'0')).join('')+Math.round((parts[3]??1)*255).toString(16).padStart(2,'0');};
 const common=Type=>({Type,Style:null,OnClickContext:'',AttachedProperties:[]});
 const pos=(p,rect,parent)=>{p.AttachedProperties=['X','Y','Width','Height'].map(key=>({Key:'Canvas.'+key,Value:String(Math.round((key==='X'?rect.X-parent.X-parent.BorderLeft:key==='Y'?rect.Y-parent.Y-parent.BorderTop:Math.max(.01,rect[key]))*100)/100)}));};
 function convert(p,path,parent){
  const source=capture.Nodes[path];if(!source)throw Error('Missing measured node '+name+':'+path);
  const out=structuredClone(p);pos(out,source.Box,parent);measured.set(out,source.Box);if(source.TextRuns?.length)typography.set(out,source.TextRuns);if(out.Type==='TextPacket')glyphs.set(out,(source.TextParts||[]).map(part=>part.Box));if(fixture.EditorLayers?.[path])layerNames.set(out,fixture.EditorLayers[path]);
  const s=source.Style;out.Style={Background:color(s.Background),Border:color(s.Border),BorderWidth:Math.round(s.BorderWidth),Margin:zero(),Padding:zero()};
  // Geometry already includes authored margins. Padding remains on leaves; container child positions include it.
  if(!out.Children)for(const side of Object.keys(out.Style.Padding)){const value=s.Padding[side];if(value>=0&&value<=255)out.Style.Padding[side]=Math.round(value);else gap(path,'Padding',String(value)+' cannot fit byte');}
  for(const reason of source.Gaps)gap(path,'CSS',reason);for(const [side,edge]of Object.entries(s.Edges))if(edge.Width&&edge.Stroke!=='solid')gap(path,'Border.'+side,'Декоративная линия '+edge.Stroke+' отложена; новые поля протокола не добавлены.');
  if(out.Type==='SliderPacket'){const k=source.Knob;out.Knob={...common('ImagePacket'),URI:'',Width:Math.round(k.Width),Height:Math.round(k.Height),Style:{Background:color(k.Background),Border:color(k.Border),BorderWidth:Math.round(k.BorderWidth),Margin:zero(),Padding:zero()}};if(k.ClipPath!=='none')gap(path,'Knob clip-path',k.ClipPath);gap(path,'Slider track','Сервер задаёт Knob, а дорожка принадлежит теме клиента. Градиент заливки и риски исходного CSS требуют решения в USS, а не новых полей пакета.');}
  if(out.Type==='TextPacket'){out.Text=source.Text;if(source.Disabled){out.OnClickContext='';gap(path,'Disabled Text','Действие удалено у недоступного текста. В TextPacket нет IsEnabled; вид недоступности передан цветом.');}}
  if(out.Children){out.Type=out.Type==='ScrollViewerPacket'?'ScrollViewerPacket':'CanvasPacket';delete out.Columns;delete out.Rows;out.Children=p.Children.map((child,i)=>convert(child,path+'/Children/'+i,source.Box));}
  if(p.Type==='GridPacket'&&out.Children.length){
   const groups=new Map();p.Children.forEach((child,i)=>{const row=Number(child.AttachedProperties.find(a=>a.Key==='Grid.Row')?.Value||0);if(!groups.has(row))groups.set(row,[]);groups.get(row).push(out.Children[i]);});
   out.Children=[...groups].map(([index,cells])=>{
    const boxes=cells.map(cell=>measured.get(cell)),x=Math.min(...boxes.map(b=>b.X)),y=Math.min(...boxes.map(b=>b.Y)),width=Math.max(...boxes.map(b=>b.X+b.Width))-x,height=Math.max(...boxes.map(b=>b.Y+b.Height))-y;
    const row={...common('CanvasPacket'),Children:cells},box={X:x,Y:y,Width:width,Height:height,BorderLeft:0,BorderTop:0};pos(row,box,source.Box);measured.set(row,box);
    for(const cell of cells){const b=measured.get(cell);pos(cell,b,box);}
    function firstText(node){if(node.Type==='TextPacket'&&plainText(node.Text).trim())return node;for(const child of node.Children||[]){const text=firstText(child);if(text)return text;}}const label=cells.map(firstText).find(Boolean);layerNames.set(row,'Строка '+(index+1)+(label?' · '+plainText(label.Text).trim().slice(0,28):''));return row;
   });
  }
  // Selectable conversion contains an extra label sibling absent from source fixture paths.
  if(p.Type==='DockPanelPacket'&&p.Children?.[0]?.Type==='SelectablePacket'){
   out.Children=[structuredClone(p.Children[0]),structuredClone(p.Children[1])];out.Type='CanvasPacket';
   const [control,label]=out.Children;label.Text=source.Text;typography.set(label,source.TextRuns);glyphs.set(label,source.TextParts.map(part=>part.Box));pos(control,source.Parts[0],source.Box);pos(label,source.Parts[1],source.Box);control.Style={Background:'#00000000',Border:'#00000000',BorderWidth:0,Margin:zero(),Padding:zero()};label.Style=structuredClone(control.Style);for(const visual of [control.Checked,control.Unchecked]){visual.Width=Math.round(source.Parts[0].Width);visual.Height=Math.round(source.Parts[0].Height);}measured.set(control,source.Parts[0]);measured.set(label,source.Parts[1]);layerNames.set(out,'Переключатель с подписью');gap(path,'Selectable artwork','Геометрия измерена; форма состояний использует доступные игровые ImagePacket, а не CSS-псевдоэлемент исходного checkbox.');
  }
  if(out.Type==='TextPacket'&&!out.OnClickContext&&source.TextParts?.length>1){
   const caption=plainText(out.Text).trim();out.Type='CanvasPacket';delete out.Text;typography.delete(out);glyphs.delete(out);out.Style.Padding=zero();out.Children=source.TextParts.map(part=>{const child={...common('TextPacket'),Text:part.Text,Style:{Background:'#00000000',Border:'#00000000',BorderWidth:0,Margin:zero(),Padding:zero()}};pos(child,part.Box,source.Box);measured.set(child,part.Box);typography.set(child,part.TextRuns);glyphs.set(child,[part.Box]);return child;});if(!layerNames.has(out))layerNames.set(out,'Значение · '+caption.slice(0,28));
  }
  const composed=composeBorders(out,source.Box,Object.fromEntries(Object.entries(s.Edges).map(([side,edge])=>[side,{...edge,Color:color(edge.Color)}])));
  if(composed.node!==out){measured.set(composed.node,source.Box);const title=layerNames.get(out)||(out.Type==='TextPacket'?'Ячейка · '+plainText(out.Text).trim().slice(0,28):'Область');layerNames.set(composed.node,title);layerNames.delete(out);for(const line of composed.lines)layerNames.set(line.node,'Разделитель · '+line.side);}
  if(composed.payload)composeTextFlow(composed.payload,source.Box,measured,allocated);composeTextFlow(composed.node,source.Box,measured,allocated);return composed.node;
 }
 // Selectable has synthetic children; measure their real input/span in the reference as source nodes.
 // Build synthetic measured nodes before recursive conversion.
 for(const [path,source]of Object.entries(capture.Nodes)){
  let n=fixture.Packet.Content;for(const segment of path.split('/').slice(1)){if(segment==='Children')continue;n=n.Children?.[Number(segment)];}
  if(n?.Type==='DockPanelPacket'&&n.Children?.[0]?.Type==='SelectablePacket')for(let i=0;i<2;i++)capture.Nodes[path+'/Children/'+i]={...source,Text:n.Children[i].Text,Box:{...source.Parts[i]}};
 }
 const root=common('CanvasPacket');layerNames.set(root,'Окно');root.Children=[convert(fixture.Packet.Content,'Content',capture.Frame)];
 function flow(node){for(const child of node.Children||[])flow(child);const box=measured.get(node);if(box)composeTextFlow(node,box,measured,allocated);}flow(root);
 const packet={WindowTag:fixture.Packet.WindowTag,Width:Math.round(capture.Frame.Width),Height:Math.round(capture.Frame.Height),Content:root};
 const errors=validatePacket(packet);if(errors.length)throw Error(name+': '+errors.join('\n'));
 const geometry={},editorLayers={},textRuns={},textGeometry={};function describe(node,path='Content'){const box=allocated.get(node)||measured.get(node);if(glyphs.has(node))textGeometry[path]=glyphs.get(node).map(r=>({X:r.X-capture.Frame.X,Y:r.Y-capture.Frame.Y,Width:r.Width,Height:r.Height}));if(box)geometry[path]={X:box.X-capture.Frame.X,Y:box.Y-capture.Frame.Y,Width:box.Width,Height:box.Height};if(typography.has(node))textRuns[path]=typography.get(node);if(layerNames.has(node))editorLayers[path]=layerNames.get(node);(node.Children||[]).forEach((child,i)=>describe(child,path+'/Children/'+i));}describe(root);
 output[name]={Packet:packet,Gaps:gaps,Geometry:geometry,EditorLayers:editorLayers,Typography:textRuns,TextGeometry:textGeometry};
}
fs.writeFileSync(new URL('./design-packets.json',import.meta.url),JSON.stringify(output,null,2)+'\n');
console.log('Measured protocol fixtures:',Object.keys(output).join(', '));
}finally{await browser.close();}
