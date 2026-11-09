let fontCatalog={};
export function setFontCatalog(catalog){fontCatalog=catalog;}
export function getFontCatalog(){return fontCatalog;}
// Supported Unity text markup only. Unknown tags remain visible, never execute HTML.
export function parseRichText(value){const root={Tag:'root',Children:[]},stack=[root];const pattern=/<\/?(?:b|i|u|s|size|color|nobr|align|cspace|line-height|font-weight|font)(?:=(?:"[^"]*"|'[^']*'|[^>]*))?>/gi;let last=0;const text=s=>{if(s)stack.at(-1).Children.push(s);};for(const match of value.matchAll(pattern)){text(value.slice(last,match.index));last=match.index+match[0].length;const tag=/^<(\/?)([\w-]+)(?:=(.*))?>$/s.exec(match[0]);const name=tag[2].toLowerCase();if(tag[1]){if(stack.length>1&&stack.at(-1).Tag===name)stack.pop();else text(match[0]);continue;}const raw=tag[3]?.replace(/^["']|["']$/g,'');if(name==='font'&&!/^[\w ./-]+$/.test(raw||'')){text(match[0]);continue;}if(name==='size'&&!/^\d+(?:\.\d+)?(?:px|%|em)?$/.test(raw||'')){text(match[0]);continue;}if(['cspace','line-height'].includes(name)&&!/^[-+]?\d+(?:\.\d+)?(?:px|%|em)?$/.test(raw||'')){text(match[0]);continue;}if(name==='align'&&!['left','right','center','justified','flush'].includes(raw)){text(match[0]);continue;}if(name==='font-weight'&&!/^[1-9]00$/.test(raw||'')){text(match[0]);continue;}if(name==='color'&&!/^(?:#[\da-f]{3,8}|[a-z]+)$/i.test(raw||'')){text(match[0]);continue;}const node={Tag:name,Value:raw,Children:[]};stack.at(-1).Children.push(node);stack.push(node);}text(value.slice(last));return root;}
export function renderRichText(element,value){
 const css=getComputedStyle(element),base={fontSize:css.fontSize,fontWeight:css.fontWeight,lineHeight:css.lineHeight};
 const units=value=>/^[-+]?\d+(\.\d+)?$/.test(value)?value+'px':value;
 const seeded=new WeakSet();
 function append(node,parent,state){
  if(typeof node==='string'){
   if(!seeded.has(parent)){parent.style.fontSize=state.fontSize;parent.style.lineHeight=state.lineHeight;seeded.add(parent);}
   const run=document.createElement('span');Object.assign(run.style,state);run.textContent=node;parent.append(run);return;
  }
  const next={...state};
  if(node.Tag==='font'){const face=fontCatalog[node.Value];if(!face){parent.append(document.createTextNode('<font='+node.Value+'>'));for(const child of node.Children)append(child,parent,state);parent.append(document.createTextNode('</font>'));return;}next.fontFamily='"'+face.Family+'"';}
  if(node.Tag==='size')next.fontSize=units(node.Value);
  if(node.Tag==='color')next.color=node.Value;
  if(node.Tag==='font-weight')next.fontWeight=node.Value;
  if(node.Tag==='b')next.fontWeight='700';
  if(node.Tag==='i')next.fontStyle='italic';
  if(node.Tag==='u')next.textDecoration='underline';
  if(node.Tag==='s')next.textDecoration='line-through';
  if(node.Tag==='cspace')next.letterSpacing=units(node.Value);
  if(node.Tag==='nobr')next.whiteSpace='pre';
  if(node.Tag==='line-height')next.lineHeight=units(node.Value);
  let target=parent;
  if(node.Tag==='align'){target=document.createElement('span');target.style.display='block';target.style.textAlign={justified:'justify',flush:'justify'}[node.Value]||node.Value;parent.append(target);}
  for(const child of node.Children)append(child,target,next);
 }
 for(const child of parseRichText(value).Children)append(child,element,base);
}
export function plainText(value){const text=n=>typeof n==='string'?n:n.Children.map(text).join('');return text(parseRichText(value));}
function serialize(node){if(typeof node==='string')return node;if(node.Tag==='root')return node.Children.map(serialize).join('');return '<'+node.Tag+(node.Value===undefined?'':'='+node.Value)+'>'+node.Children.map(serialize).join('')+'</'+node.Tag+'>';}
export function replaceText(value,text){const root=parseRichText(value);let replaced=false;function visit(node){if(typeof node==='string'){if(replaced)return '';replaced=true;return text;}node.Children=node.Children.map(visit);return node;}visit(root);if(!replaced)root.Children.push(text);return serialize(root);}
export function textProperty(value,tag,fallback){let found;function visit(node){if(typeof node==='string')return;if(node.Tag===tag&&found===undefined)found=node.Value;node.Children.forEach(visit);}visit(parseRichText(value));return found??fallback;}
export function setTextProperty(value,tag,property){const root=parseRichText(value);let found=false;function visit(node){if(typeof node==='string')return;if(node.Tag===tag){node.Value=String(property);found=true;}node.Children.forEach(visit);}visit(root);if(!found)root.Children=[{Tag:tag,Value:String(property),Children:root.Children}];return serialize(root);}
export function clearTextProperty(value,tag){function unwrap(node){if(typeof node==='string')return [node];node.Children=node.Children.flatMap(unwrap);return node.Tag===tag?node.Children:[node];}return unwrap(parseRichText(value)).map(serialize).join('');}
