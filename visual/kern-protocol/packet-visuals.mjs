// Match LinePacketBuilder and StyleApplicator: Background is stroke colour, not a fill.
export function createPacketLine(packet){
 const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'),line=document.createElementNS(svg.namespaceURI,'line'),thickness=packet.Style?.BorderWidth||1;
 const coordinates={Horizontal:['0%','50%','100%','50%'],Vertical:['50%','0%','50%','100%'],Diagonal:['0%','0%','100%','100%'],ReverseDiagonal:['0%','100%','100%','0%']}[packet.Direction];
 if(!coordinates)throw Error('Unknown LinePacket direction');
 ['x1','y1','x2','y2'].forEach((key,i)=>line.setAttribute(key,coordinates[i]));line.setAttribute('stroke',packet.Style?.Background||'#ffffff');line.setAttribute('stroke-width',String(thickness));
 svg.style.width=packet.Direction==='Vertical'?thickness+'px':'100%';svg.style.height=packet.Direction==='Horizontal'?thickness+'px':'100%';svg.style.flexShrink='0';svg.append(line);return svg;
}
export function configurePacketScroll(element,packet){
 element.style.overflowX=packet.HorizontalScrollBar==='Visible'?'scroll':'auto';element.style.overflowY=packet.VerticalScrollBar==='Visible'?'scroll':'auto';
 element.dataset.hscroll=packet.HorizontalScrollBar;element.dataset.vscroll=packet.VerticalScrollBar;
 if(packet.HorizontalScrollBar==='Hidden'&&packet.VerticalScrollBar==='Hidden')element.style.scrollbarWidth='none';
}
