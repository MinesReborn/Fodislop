import fs from 'node:fs';
import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const fixtures=JSON.parse(fs.readFileSync(new URL('./design-packets.json',import.meta.url)));
const browser=await chromium.launch({headless:true});
try{
 const page=await browser.newPage({viewport:{width:1600,height:1000}});
 await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#artboard>.node');
 for(const [name,fixture]of Object.entries(fixtures)){
  await page.locator('#scene').selectOption(name,{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');
  const failures=await page.evaluate(({packet,expected})=>{
   const root=document.getElementById('artboard'),frame=root.getBoundingClientRect(),failures=[];
   function walk(n,path){const selected=[...root.querySelectorAll('.node')].find(el=>el.dataset.packetPath===path);if(!selected){failures.push(path+': absent');return;}const r=selected.getBoundingClientRect(),e=expected[path];if(e)for(const [key,value]of Object.entries({X:r.x-frame.x,Y:r.y-frame.y,Width:r.width,Height:r.height})){if(Math.abs(value-e[key])>1.1)failures.push(path+' '+key+': '+value+' expected '+e[key]);}for(let i=0;i<(n.Children||[]).length;i++)walk(n.Children[i],path+'/Children/'+i);}
   walk(packet.Content,'Content');return failures;
  },{packet:fixture.Packet,expected:fixture.Geometry});
  assert.deepEqual(failures,[],name);console.log(name+': measured geometry matches within 1.1 px');
 }
}finally{await browser.close();}
