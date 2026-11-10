import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const browser=await chromium.launch();
try{
 const page=await browser.newPage({viewport:{width:1700,height:1100}});await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#builder-properties');
 const original=await page.evaluate(async()=>{const f=await fetch('./design-packets.json').then(r=>r.json()),packet=structuredClone(f.resp.Packet);packet.WindowTag='saved_resp_custom';const project={kind:'kern-ui-project',active:'design/resp',documents:[{id:'design/resp',packet,layers:{}}]};localStorage.setItem('kern.protocol.builder.autosave',JSON.stringify(project));return JSON.stringify(packet);});
 await page.reload();await page.locator('.sample-update-notice').waitFor({state:'visible'});await page.locator('.sample-update-notice button').click();
 assert.notEqual(await page.locator('#scene').inputValue(),'resp');assert.equal(await page.locator('#scene option:checked').getAttribute('data-origin'),'sample');await page.reload();await page.waitForSelector('#builder-properties');assert.equal(await page.locator('#scene option:checked').getAttribute('data-origin'),'sample');assert.equal(await page.locator('#tree [data-node-path]').filter({hasText:'Шапка'}).count(),1);
 await page.locator('#scene').selectOption('resp',{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');await page.locator('.builder-raw').evaluate(el=>el.open=true);await page.locator('#tree [data-node-path]').first().click();assert.equal(JSON.stringify(JSON.parse(await page.locator('#properties').inputValue())),JSON.stringify(JSON.parse(original).Content));
 console.log('Updated sample opens as a copy; saved packet is unchanged');
}finally{await browser.close();}
