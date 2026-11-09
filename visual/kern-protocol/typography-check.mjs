import fs from 'node:fs';
import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const fixtures=JSON.parse(fs.readFileSync(new URL('./design-packets.json',import.meta.url)));
const browser=await chromium.launch();
try{
 const page=await browser.newPage({viewport:{width:1700,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#builder-properties');
 for(const [name,fixture]of Object.entries(fixtures)){
  await page.locator('#scene').selectOption(name,{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');await page.evaluate(()=>document.fonts.ready);
  const failures=await page.evaluate(expected=>{
   const failures=[];for(const [path,runs]of Object.entries(expected)){
    const element=[...document.querySelectorAll('#artboard .node')].find(e=>e.dataset.packetPath===path);const walker=document.createTreeWalker(element,NodeFilter.SHOW_TEXT);const actual=[];let text;
    while(text=walker.nextNode()){const s=getComputedStyle(text.parentElement);actual.push({Text:text.textContent,Family:s.fontFamily.split(',')[0].trim().replaceAll('"',''),Size:parseFloat(s.fontSize),Weight:Number(s.fontWeight),LineHeight:parseFloat(s.lineHeight)||null,LetterSpacing:parseFloat(s.letterSpacing)||0});}
    if(actual.length!==runs.length){failures.push(path+': text run count '+actual.length+' expected '+runs.length);continue;}
    runs.forEach((run,i)=>{for(const key of Object.keys(run)){const value=actual[i][key],wanted=run[key];if(typeof wanted==='number'&&typeof value==='number'?Math.abs(value-wanted)>.1:value!==wanted)failures.push(path+' '+key+': '+value+' expected '+wanted);}});
   }return failures;
  },fixture.Typography);assert.deepEqual(failures,[],name);console.log(name+': source text, font family, size, weight and spacing match');
 }
 await page.locator('#scene').selectOption('market',{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:'/tmp/kern-typography-fixed.png',fullPage:true});assert.deepEqual(errors,[]);
}finally{await browser.close();}
