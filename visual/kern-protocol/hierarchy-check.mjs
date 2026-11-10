import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const browser=await chromium.launch();
try{
const page=await browser.newPage({viewport:{width:1700,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#builder-properties');
const padding=await page.locator('#tree .layer-tree-row').evaluateAll(buttons=>buttons.map(button=>({depth:Number(button.dataset.depth),padding:parseFloat(getComputedStyle(button).paddingLeft)})));assert.ok(padding.some(p=>p.depth>3));assert.ok(padding.every(p=>p.padding===p.depth*18));
const green=page.locator('#artboard [data-type=TextPacket]').filter({hasText:/^Зелёные$/});
await green.click();assert.equal(await page.locator('#selected').innerText(),'Таблица');
await green.dblclick();assert.equal(await page.locator('#selected').innerText(),'Строка 2 · Зелёные');
await green.dblclick();assert.equal(await page.locator('#selected').innerText(),'Зелёные');
await page.locator('#tree [data-node-path]').filter({hasText:'Строка 2 · Зелёные'}).click();
await page.getByLabel('Имя слоя',{exact:true}).fill('Зелёные кристаллы');await page.getByLabel('Имя слоя',{exact:true}).press('Tab');
assert.equal(await page.locator('#selected').innerText(),'Зелёные кристаллы');
await page.click('#undo-edit');assert.equal(await page.locator('#selected').innerText(),'Строка 2 · Зелёные');
await page.click('#redo-edit');assert.equal(await page.locator('#selected').innerText(),'Зелёные кристаллы');
await page.reload();await page.waitForSelector('#builder-properties');assert.equal(await page.locator('#tree [data-node-path]').filter({hasText:'Зелёные кристаллы'}).count(),1);
await page.locator('#tree [data-node-path]').filter({hasText:'Зелёные кристаллы'}).click();await page.locator('.builder-raw').evaluate(el=>el.open=true);const packet=JSON.parse(await page.locator('#properties').inputValue());assert.equal(packet.Type,'CanvasPacket');assert.equal(packet.Children.length,7);assert.ok(!('Name' in packet));
await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:'/tmp/kern-hierarchy.png',fullPage:true});assert.deepEqual(errors,[]);console.log('Hierarchy: group selection, drilling, layer names, undo/redo and reload passed');
}finally{await browser.close();}
