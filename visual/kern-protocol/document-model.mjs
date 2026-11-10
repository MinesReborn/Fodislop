import {validatePacket} from './validate.mjs';
export const atPath=(packet,path=[])=>path.reduce((n,i)=>n.Children?.[i],packet.Content);
export function entries(packet){const result=[];function walk(node,path=[],parent=null){result.push({node,path,parent});(node.Children||[]).forEach((child,i)=>walk(child,[...path,i],node));}walk(packet.Content);return result;}
export function resolveTarget(packet,node,context){let current=context.startsWith('/')?packet.Content:node;const list=entries(packet);for(const part of context.split('/')){if(!part||part==='.')continue;if(part==='..')current=list.find(e=>e.node===current)?.parent;else if(/^[+-]?\d+$/.test(part.trim()))current=current?.Children?.[Number(part)];else return undefined;if(!current)return undefined;}return current;}
export function editTree(packet,path,operation,destination){
 const next=structuredClone(packet),list=entries(next),selected=atPath(next,path),entry=list.find(e=>e.node===selected);if(!selected)throw Error('Элемент не найден');
 const bindings=list.filter(e=>e.node.OnClickContext).map(e=>({node:e.node,target:resolveTarget(next,e.node,e.node.OnClickContext)}));
 if(bindings.some(b=>!b.target))throw Error('Сначала исправьте недопустимый OnClickContext');
 let result=selected;const copies=[];
 if(operation==='delete'){if(!entry.parent)throw Error('Корень окна удалить нельзя');entry.parent.Children.splice(path.at(-1),1);result=entry.parent;}
 if(operation==='duplicate'){
  if(!entry.parent)throw Error('Для копии корня нужен контейнер');const copy=structuredClone(selected),before=[];function pairs(a,b){before.push([a,b]);(a.Children||[]).forEach((c,i)=>pairs(c,b.Children[i]));}pairs(selected,copy);copies.push(...before);const mapped=new Map(before),names=new Set(list.map(e=>e.node.Name).filter(Boolean));
  for(const [original,cloned]of before){if(cloned.Name){let suffix=1,name=cloned.Name;while(names.has(name))name=cloned.Name+'_copy'+suffix++;cloned.Name=name;names.add(name);}const binding=bindings.find(b=>b.node===original);if(binding)bindings.push({node:cloned,target:mapped.get(binding.target)||binding.target});}
  entry.parent.Children.splice(path.at(-1)+1,0,copy);result=copy;
 }
 if(operation==='up'||operation==='down'){if(!entry.parent)throw Error('Выберите дочерний элемент');const i=path.at(-1),j=i+(operation==='up'?-1:1);if(j<0||j>=entry.parent.Children.length)return {packet:next,path};[entry.parent.Children[i],entry.parent.Children[j]]=[entry.parent.Children[j],entry.parent.Children[i]];}
 if(operation==='insert'){
  const target=atPath(next,destination.parent);if(target?.Type!=='DockPanelPacket')throw Error('Вставка в поток доступна в DockPanel');if(!entry.parent)throw Error('Корень перемещать нельзя');if(entries({Content:selected}).some(e=>e.node===target))throw Error('Контейнер нельзя поместить внутрь себя');
  let index=destination.index;if(!Number.isInteger(index)||index<0||index>target.Children.length)throw Error('Недопустимая позиция вставки');
  const reference=target.Children[Math.min(index,target.Children.length-1)],dock=reference?.AttachedProperties.find(p=>p.Key==='DockPanel.Dock')?.Value||'Top';
  if(entry.parent===target&&path.at(-1)<index)index--;entry.parent.Children.splice(path.at(-1),1);target.Children.splice(index,0,selected);
  selected.AttachedProperties=selected.AttachedProperties.filter(p=>!p.Key.startsWith('Canvas.')&&p.Key!=='DockPanel.Dock'&&!p.Key.startsWith('Grid.'));selected.AttachedProperties.push({Key:'DockPanel.Dock',Value:dock});
 }
 if(operation==='move'){
  const target=atPath(next,destination);if(!target?.Children)throw Error('Переносить можно в контейнер');if(!entry.parent)throw Error('Корень перемещать нельзя');if(selected===target||entries({Content:selected}).some(e=>e.node===target))throw Error('Контейнер нельзя поместить внутрь себя');entry.parent.Children.splice(path.at(-1),1);target.Children.push(selected);
 }
 const after=entries(next),paths=new Map(after.map(e=>[e.node,e.path]));for(const binding of bindings){if(!paths.has(binding.node))continue;if(!paths.has(binding.target))throw Error('Контейнер используется в OnClickContext. Сначала измените действие.');binding.node.OnClickContext='/'+paths.get(binding.target).join('/');}
 const errors=validatePacket(next);if(errors.length)throw Error(errors.join('\n'));return {packet:next,path:paths.get(result)||[],relocations:list.filter(e=>paths.has(e.node)).map(e=>({from:e.path,to:paths.get(e.node)})),copies:copies.map(([original,copy])=>({from:list.find(e=>e.node===original).path,to:paths.get(copy)}))};
}
export class DocumentHistory{
 constructor(){this.items=[];this.index=-1;}
 record(packet,path=[],editor=null){const value=JSON.stringify(packet);if(this.items[this.index]?.value===value&&JSON.stringify(this.items[this.index]?.editor?.layerNames)===JSON.stringify(editor?.layerNames))return;this.items.splice(this.index+1);this.items.push({value,path:path===null?null:[...path],editor:structuredClone(editor)});if(this.items.length>50)this.items.shift();this.index=this.items.length-1;}
 step(delta){const i=this.index+delta;if(i<0||i>=this.items.length)return null;this.index=i;const state=this.items[i];return {packet:JSON.parse(state.value),path:state.path,editor:structuredClone(state.editor)};}
}

export function groupNodes(packet,paths,bounds){
 const next=structuredClone(packet),list=entries(next),bindings=list.filter(e=>e.node.OnClickContext).map(e=>({node:e.node,target:resolveTarget(next,e.node,e.node.OnClickContext)}));
 const nodes=paths.map(path=>atPath(next,path));if(!nodes.length)throw Error('Выберите элементы для группы');const parent=list.find(e=>e.node===nodes[0])?.parent;
 if(parent?.Type!=='CanvasPacket'||nodes.some(n=>list.find(e=>e.node===n)?.parent!==parent))throw Error('Для группы выберите элементы одного Canvas. Сначала перенесите их в общий контейнер.');
 const set=(node,key,value)=>{node.AttachedProperties=node.AttachedProperties.filter(p=>p.Key!=='Canvas.'+key);node.AttachedProperties.push({Key:'Canvas.'+key,Value:String(Math.round(value*100)/100)});};
 const group={Type:'CanvasPacket',Style:null,OnClickContext:'',AttachedProperties:[],Children:parent.Children.filter(n=>nodes.includes(n))};const first=Math.min(...nodes.map(n=>parent.Children.indexOf(n)));
 for(const [key,value]of Object.entries({X:bounds.x,Y:bounds.y,Width:Math.max(1,bounds.width),Height:Math.max(1,bounds.height)}))set(group,key,value);
 for(const node of group.Children)for(const [key,offset]of [['X',bounds.x],['Y',bounds.y]])set(node,key,Number(node.AttachedProperties.find(p=>p.Key==='Canvas.'+key)?.Value||0)-offset);
 parent.Children=parent.Children.filter(n=>!nodes.includes(n));parent.Children.splice(first,0,group);
 return finishEdit(next,bindings,group,list);
}
export function ungroupNode(packet,path){
 const next=structuredClone(packet),list=entries(next),node=atPath(next,path),parent=list.find(e=>e.node===node)?.parent;
 if(node?.Type!=='CanvasPacket'||parent?.Type!=='CanvasPacket')throw Error('Разгруппировка доступна для Canvas внутри Canvas');
 const bindings=list.filter(e=>e.node.OnClickContext).map(e=>({node:e.node,target:resolveTarget(next,e.node,e.node.OnClickContext)}));if(bindings.some(b=>b.target===node&&b.node!==node))throw Error('На группу ссылается OnClickContext. Сначала измените действие.');
 for(const child of node.Children)for(const [key,side]of [['X','Left'],['Y','Top']]){const value=Number(child.AttachedProperties.find(p=>p.Key==='Canvas.'+key)?.Value||0)+Number(node.AttachedProperties.find(p=>p.Key==='Canvas.'+key)?.Value||0)+(node.Style?.Margin?.[side]||0)+(node.Style?.BorderWidth||0);child.AttachedProperties=child.AttachedProperties.filter(p=>p.Key!=='Canvas.'+key);child.AttachedProperties.push({Key:'Canvas.'+key,Value:String(Math.round(value*100)/100)});}
 parent.Children.splice(path.at(-1),1,...node.Children);return finishEdit(next,bindings,parent,list);
}
function finishEdit(packet,bindings,selected,before){const list=entries(packet),paths=new Map(list.map(e=>[e.node,e.path]));for(const binding of bindings){if(!paths.has(binding.node))continue;if(!paths.has(binding.target))throw Error('Недопустимый OnClickContext');binding.node.OnClickContext='/'+paths.get(binding.target).join('/');}const errors=validatePacket(packet);if(errors.length)throw Error(errors.join('\n'));return {packet,path:paths.get(selected),relocations:before.filter(e=>paths.has(e.node)).map(e=>({from:e.path,to:paths.get(e.node)}))};}
