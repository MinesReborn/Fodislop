// Authoring diagnostics: report overflow, never change the server window implicitly.
export function mountLayoutOverflow({packet,fit}){
 const root=document.getElementById('artboard'),bar=document.createElement('div');bar.className='window-overflow';bar.hidden=true;
 const message=document.createElement('span'),button=document.createElement('button');button.textContent='Увеличить окно до содержимого';bar.append(message,button);document.querySelector('.workspace').insertBefore(bar,document.querySelector('.workspace footer'));
 let requested=null;
 function refresh(){
  if(!root.firstElementChild){bar.hidden=true;return;}
  const frame=root.getBoundingClientRect(),scale=frame.width/root.offsetWidth,border=getComputedStyle(root);let left=frame.left,top=frame.top,right=frame.right,bottom=frame.bottom,unfitText=false;
  function include(rect,element){let r={left:rect.left,top:rect.top,right:rect.right,bottom:rect.bottom};for(let parent=element.parentElement;parent&&parent!==root;parent=parent.parentElement){if(parent.dataset.type==='ScrollViewerPacket'){const clip=parent.getBoundingClientRect();r.left=Math.max(r.left,clip.left);r.top=Math.max(r.top,clip.top);r.right=Math.min(r.right,clip.right);r.bottom=Math.min(r.bottom,clip.bottom);}}if(r.right<r.left||r.bottom<r.top)return;left=Math.min(left,r.left);top=Math.min(top,r.top);right=Math.max(right,r.right);bottom=Math.max(bottom,r.bottom);}
  for(const element of root.querySelectorAll('.node')){include(element.getBoundingClientRect(),element);if(element.dataset.type==='TextPacket'){const block=element.firstElementChild,style=getComputedStyle(element);if(block&&(block.getBoundingClientRect().height>(element.clientHeight-parseFloat(style.paddingTop)-parseFloat(style.paddingBottom))*scale+1.1||block.scrollWidth>block.clientWidth+1.1||block.scrollHeight>block.clientHeight+Math.max(1,(parseFloat(getComputedStyle(block).lineHeight)||parseFloat(getComputedStyle(block).fontSize))*.9)))unfitText=true;const walker=document.createTreeWalker(element,NodeFilter.SHOW_TEXT);let text;while(text=walker.nextNode()){const range=document.createRange();range.selectNodeContents(text);include(range.getBoundingClientRect(),element);}}}
  const horizontal=(right-frame.right)/scale,vertical=(bottom-frame.bottom)/scale,negative=(frame.left-left)/scale>1.1||(frame.top-top)/scale>1.1;
  bar.hidden=horizontal<=1.1&&vertical<=1.1&&!negative&&!unfitText;requested={Width:Math.max(packet().Width,Math.ceil((right-frame.left)/scale+parseFloat(border.borderRightWidth))),Height:Math.max(packet().Height,Math.ceil((bottom-frame.top)/scale+parseFloat(border.borderBottomWidth)))};
  message.textContent=unfitText&&horizontal<=1.1&&vertical<=1.1?'Текст не помещается в ячейке. Измените компоновку или размер группы.':negative?'Содержимое выходит за левый или верхний край окна. Исправьте положение слоя.':'Содержимое выходит за окно'+(horizontal>1.1?' по ширине на '+Math.ceil(horizontal)+' px':'')+(vertical>1.1?' по высоте на '+Math.ceil(vertical)+' px':'')+'.';button.hidden=negative||horizontal<=1.1&&vertical<=1.1;
 }
 button.onclick=()=>{if(requested)fit(requested);};document.fonts.addEventListener('loadingdone',refresh);window.addEventListener('resize',refresh);
 return {refresh};
}
