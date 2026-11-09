import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const browser=await chromium.launch();
try{
 const page=await browser.newPage({viewport:{width:1700,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#builder-properties');
 const base=Type=>({Type,Style:null,OnClickContext:'',AttachedProperties:[]}),zero=()=>({Top:0,Right:0,Bottom:0,Left:0}),pos=(x,y,w,h)=>Object.entries({X:x,Y:y,Width:w,Height:h}).map(([k,v])=>({Key:'Canvas.'+k,Value:String(v)}));
 const children=['Horizontal','Vertical','Diagonal','ReverseDiagonal'].map((Direction,i)=>({...base('LinePacket'),Direction,Style:{Background:'#aabbcc',Border:'#ff0000',BorderWidth:3,Margin:zero(),Padding:zero()},AttachedProperties:pos(20+i*120,200,100,40)}));
 children.push({...base('ScrollViewerPacket'),Name:'viewport',HorizontalScrollBar:'Hidden',VerticalScrollBar:'Visible',AttachedProperties:pos(20,20,140,80),Children:[{...base('CanvasPacket'),AttachedProperties:pos(0,0,300,240),Children:[]}]});
 const packet={WindowTag:'layout_contract',Width:640,Height:480,Content:{...base('CanvasPacket'),Children:children}};
 await page.locator('details').filter({has:page.locator('#importData')}).evaluate(el=>el.open=true);await page.locator('#importData').fill(JSON.stringify(packet));await page.click('#import');
 const lines=page.locator('#artboard svg[data-type=LinePacket]');assert.equal(await lines.count(),4);
 for(let i=0;i<4;i++){assert.equal(await lines.nth(i).locator('line').getAttribute('stroke'),'#aabbcc');assert.equal(await lines.nth(i).locator('line').getAttribute('stroke-width'),'3');const css=await lines.nth(i).evaluate(el=>({fill:getComputedStyle(el).backgroundColor,border:getComputedStyle(el).borderTopWidth}));assert.equal(css.fill,'rgba(0, 0, 0, 0)');assert.equal(css.border,'0px');}
 assert.equal(await lines.nth(0).locator('line').getAttribute('y1'),'50%');assert.equal(await lines.nth(1).locator('line').getAttribute('x1'),'50%');
 const scroll=page.locator('#artboard [data-type=ScrollViewerPacket]');assert.equal(await scroll.evaluate(el=>getComputedStyle(el).overflowY),'scroll');await scroll.evaluate(el=>{el.scrollTop=60;el.scrollLeft=40;});assert.equal(await scroll.evaluate(el=>el.scrollTop),60);assert.equal(await scroll.getAttribute('data-hscroll'),'Hidden');assert.deepEqual(errors,[]);
 console.log('Window content: line colour/thickness/directions, no duplicate fill, and scrolling contract passed');
}finally{await browser.close();}
