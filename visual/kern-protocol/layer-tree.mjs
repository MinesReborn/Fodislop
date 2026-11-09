// Editor view state is separate from the server packet and its undo history.
export function createLayerTree({root,documentKey,packet,selected,label,onSelect,onChange}){
 const folded=new Map(),rows=new Map();let lastDocument=null,visible=[];
 root.setAttribute('role','tree');root.setAttribute('aria-label','Слои интерфейса');
 const key=path=>JSON.stringify(path);
 const state=()=>{const id=documentKey();if(!folded.has(id))folded.set(id,new Set());return folded.get(id);};
 const lookup=path=>path.reduce((node,index)=>node?.Children?.[index],packet().Content);
 function toggle(path,expand){const set=state(),id=key(path);if(expand??set.has(id))set.delete(id);else set.add(id);render();onChange?.();}
 function focus(path){rows.get(key(path))?.button.focus({preventScroll:true});}
 function choose(record,event){onSelect(record.node,event);focus(record.path);}
 function createRow(){
  const element=document.createElement('div');element.className='layer-tree-row';element.setAttribute('role','presentation');
  const disclosure=document.createElement('button');disclosure.className='layer-tree-toggle';disclosure.type='button';disclosure.tabIndex=-1;
  const button=document.createElement('button');button.className='layer-tree-label';button.type='button';button.draggable=true;button.setAttribute('role','treeitem');
  const title=document.createElement('span');title.className='layer-tree-title';button.append(title);element.append(disclosure,button);
  const record={element,disclosure,button,title,node:null,path:[]};
  disclosure.onclick=event=>{event.stopPropagation();toggle(record.path);disclosure.focus({preventScroll:true});};
  button.onclick=event=>choose(record,event);
  button.onkeydown=event=>{
   const index=visible.findIndex(item=>item===record),children=record.node.Children||[];
   if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key))return;
   event.preventDefault();event.stopPropagation();
   if(event.key==='ArrowRight'){if(!children.length)return;if(state().has(key(record.path)))toggle(record.path,true);else{const child=rows.get(key([...record.path,0]));if(child)choose(child,event);}}
   if(event.key==='ArrowLeft'){if(children.length&&!state().has(key(record.path)))toggle(record.path,false);else if(record.path.length){const parent=rows.get(key(record.path.slice(0,-1)));if(parent)choose(parent,event);}}
   if(event.key==='ArrowUp'&&index>0)choose(visible[index-1],event);
   if(event.key==='ArrowDown'&&index<visible.length-1)choose(visible[index+1],event);
   if(event.key==='Home'&&visible.length)choose(visible[0],event);
   if(event.key==='End'&&visible.length)choose(visible.at(-1),event);
  };
  return record;
 }
 function render(){
  if(lastDocument!==documentKey()){rows.clear();root.replaceChildren();lastDocument=documentKey();}
  const next=[],set=state();
  function visit(node,path=[]){
   const id=key(path);let record=rows.get(id);if(!record){record=createRow();rows.set(id,record);}
   record.node=node;record.path=path;record.title.textContent=label(node);record.button.title=label(node);record.button.dataset.nodePath=id;record.button.dataset.depth=String(path.length);record.element.dataset.layerPath=id;record.element.dataset.depth=String(path.length);record.element.style.paddingLeft=path.length*18+'px';
   const children=node.Children||[],expanded=!set.has(id),isSelected=node===selected();
   record.disclosure.hidden=!children.length;record.disclosure.textContent=expanded?'▾':'▸';record.disclosure.setAttribute('aria-label',(expanded?'Свернуть: ':'Развернуть: ')+label(node));
   record.button.setAttribute('aria-level',String(path.length+1));record.button.setAttribute('aria-selected',String(isSelected));record.button.tabIndex=isSelected||!path.length?0:-1;
   if(children.length)record.button.setAttribute('aria-expanded',String(expanded));else record.button.removeAttribute('aria-expanded');
   record.button.classList.toggle('chosen',isSelected);record.element.classList.toggle('is-selected',isSelected);next.push(record);
   if(expanded)children.forEach((child,index)=>visit(child,[...path,index]));
  }
  visit(packet().Content);visible=next;
  // Keep rows mounted when selection/properties change so focus and clicks survive.
  next.forEach((record,index)=>{if(root.children[index]!==record.element)root.insertBefore(record.element,root.children[index]||null);});
  while(root.children.length>next.length)root.lastElementChild.remove();
 }
 function reveal(node){let path=null;function find(current,currentPath=[]){if(current===node)path=currentPath;(current.Children||[]).forEach((child,index)=>find(child,[...currentPath,index]));}find(packet().Content);if(!path)return;for(let depth=0;depth<path.length;depth++)state().delete(key(path.slice(0,depth)));}
 function all(expand){function visit(node,path=[]){if(node.Children?.length){if(expand)state().delete(key(path));else state().add(key(path));node.Children.forEach((child,index)=>visit(child,[...path,index]));}}visit(packet().Content);render();onChange?.();}
 function snapshot(){return Object.fromEntries([...folded].map(([id,paths])=>[id,[...paths]]));}
 function restore(data){folded.clear();for(const [id,paths]of Object.entries(data||{})){if(!Array.isArray(paths))continue;const valid=paths.filter(value=>{try{const path=JSON.parse(value);return Array.isArray(path)&&path.every(i=>Number.isInteger(i)&&i>=0);}catch{return false;}});folded.set(id,new Set(valid));}}
 const tools=document.createElement('div');tools.className='layer-tree-tools';for(const [name,expand]of [['Развернуть всё',true],['Свернуть всё',false]]){const button=document.createElement('button');button.textContent=name;button.type='button';button.onclick=()=>all(expand);tools.append(button);}root.before(tools);
 function relocate(moves,copies=[]){const before=new Set(state()),after=new Set();for(const move of [...moves,...copies])if(before.has(key(move.from)))after.add(key(move.to));folded.set(documentKey(),after);}
 return {render,reveal,snapshot,restore,all,relocate,snapshotDocument:()=>[...state()],restoreDocument:paths=>folded.set(documentKey(),new Set(paths||[]))};
}
