// Project navigation is editor chrome, not part of an exported server window.
export function mountWindowPicker(select){
 const trigger=document.createElement('button');trigger.type='button';trigger.className='window-picker';trigger.id='window-picker';trigger.setAttribute('aria-haspopup','listbox');trigger.setAttribute('aria-expanded','false');
 const title=document.createElement('span'),arrow=document.createElement('span');arrow.textContent='▾';trigger.append(title,arrow);select.after(trigger);select.hidden=true;
 const popup=document.createElement('section');popup.className='window-picker-popup';popup.hidden=true;popup.id='window-picker-popup';trigger.setAttribute('aria-controls',popup.id);
 const search=document.createElement('input');search.type='search';search.placeholder='Найти окно…';search.setAttribute('aria-label','Найти окно');
 const list=document.createElement('div');list.setAttribute('role','listbox');list.setAttribute('aria-label','Окна проекта');popup.append(search,list);document.body.append(popup);
 const label=option=>option.textContent.replace(/^Kern UI\s*\/\s*/,'').trim();let choices=[];
 function position(){const rect=trigger.getBoundingClientRect(),width=Math.min(Math.max(rect.width,300),innerWidth-24);popup.style.width=width+'px';popup.style.left=Math.max(12,Math.min(rect.left,innerWidth-width-12))+'px';popup.style.top=Math.min(rect.bottom+5,Math.max(12,innerHeight-350))+'px';popup.style.maxHeight=Math.max(120,innerHeight-parseFloat(popup.style.top)-12)+'px';}
 function render(){list.replaceChildren();choices=[];const query=search.value.toLocaleLowerCase();
  const groups=[['Образцы',option=>option.dataset.origin==='sample'],['Мои окна',option=>option.dataset.origin!=='sample']];
  for(const [name,match]of groups){const options=[...select.options].filter(option=>match(option)&&(label(option)+' '+option.value+' '+(option.dataset.tag||'')).toLocaleLowerCase().includes(query));if(!options.length)continue;const heading=document.createElement('div');heading.className='window-picker-heading';heading.textContent=name;list.append(heading);
   for(const option of options){const button=document.createElement('button');button.type='button';button.setAttribute('role','option');button.setAttribute('aria-selected',String(option.selected));button.dataset.windowId=option.value;const check=document.createElement('span');check.textContent=option.selected?'✓':'';const text=document.createElement('span');text.textContent=label(option);button.append(check,text);button.onclick=()=>{select.value=option.value;select.dispatchEvent(new Event('change',{bubbles:true}));refresh();close(true);};button.onkeydown=event=>{if(['ArrowUp','ArrowDown','Home','End'].includes(event.key)){event.preventDefault();const index=choices.indexOf(button);choices[event.key==='Home'?0:event.key==='End'?choices.length-1:Math.max(0,Math.min(choices.length-1,index+(event.key==='ArrowDown'?1:-1)))].focus();}};choices.push(button);list.append(button);}
  }
  if(!choices.length){const empty=document.createElement('p');empty.className='window-picker-empty';empty.textContent='Окна не найдены';list.append(empty);}
 }
 function refresh(){const option=select.selectedOptions[0];title.textContent=option?label(option):'Выбрать окно';trigger.title=title.textContent;if(!popup.hidden)render();}
 function close(focus=false){popup.hidden=true;trigger.setAttribute('aria-expanded','false');if(focus)trigger.focus();}
 function open(){search.value='';popup.hidden=false;trigger.setAttribute('aria-expanded','true');render();position();search.focus();}
 trigger.onclick=()=>popup.hidden?open():close(true);trigger.onkeydown=event=>{if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();open();(choices.find(button=>button.getAttribute('aria-selected')==='true')||choices[0])?.focus();}};
 search.oninput=render;search.onkeydown=event=>{if(event.key==='ArrowDown'){event.preventDefault();choices[0]?.focus();}if(event.key==='Enter'&&choices.length===1){event.preventDefault();choices[0].click();}};
 document.addEventListener('keydown',event=>{if(!popup.hidden&&event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();close(true);}else if(!popup.hidden&&event.key==='Tab')close();},true);
 document.addEventListener('pointerdown',event=>{if(!popup.hidden&&!popup.contains(event.target)&&!trigger.contains(event.target))close();},true);
 document.addEventListener('scroll',()=>{if(!popup.hidden)position();},true);window.addEventListener('resize',()=>{if(!popup.hidden)position();});
 select.addEventListener('change',refresh);refresh();return {refresh};
}
