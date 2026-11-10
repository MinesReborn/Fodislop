import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const browser=await chromium.launch();
try{
 const page=await browser.newPage({viewport:{width:1700,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#builder-properties');
 const header=()=>page.locator('#tree [data-node-path]').filter({hasText:/^Шапка$/});
 const original=await page.locator('#tree [data-node-path]').count();
 await page.getByRole('button',{name:'Свернуть: Шапка',exact:true}).click();
 assert.equal(await header().getAttribute('aria-expanded'),'false');assert.ok(await page.locator('#tree [data-node-path]').count()<original);
 await header().click();assert.equal(await header().getAttribute('aria-expanded'),'false');assert.equal(await page.locator('#selected').innerText(),'Шапка');
 await page.locator('[data-edit=down]').click();assert.equal(await header().getAttribute('aria-expanded'),'false');await page.click('#undo-edit');assert.equal(await header().getAttribute('aria-expanded'),'false');
 await page.getByLabel('Имя слоя',{exact:true}).fill('Заголовок здания');await page.getByLabel('Имя слоя',{exact:true}).press('Tab');
 const renamed=()=>page.locator('#tree [data-node-path]').filter({hasText:/^Заголовок здания$/});assert.equal(await renamed().getAttribute('aria-expanded'),'false');
 await page.reload();await page.waitForSelector('#builder-properties');assert.equal(await renamed().getAttribute('aria-expanded'),'false');
 await renamed().focus();await page.keyboard.press('ArrowRight');assert.equal(await renamed().getAttribute('aria-expanded'),'true');
 await page.keyboard.press('ArrowRight');assert.equal(await page.locator('#selected').innerText(),'Изображение');
 await page.keyboard.press('ArrowLeft');assert.equal(await page.locator('#selected').innerText(),'Заголовок здания');
 await page.keyboard.press('ArrowLeft');assert.equal(await renamed().getAttribute('aria-expanded'),'false');
 await page.locator('#scene').selectOption('resp',{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');assert.equal(await header().getAttribute('aria-expanded'),'true');
 await page.locator('#scene').selectOption('market',{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');assert.equal(await renamed().getAttribute('aria-expanded'),'false');
 await page.getByRole('button',{name:'Свернуть всё',exact:true}).click();assert.equal(await page.locator('#tree [data-node-path]').count(),1);
 await page.getByRole('button',{name:'Развернуть всё',exact:true}).click();assert.equal(await page.locator('#tree [data-node-path]').count(),original);
 await page.getByRole('button',{name:'Свернуть: Таблица',exact:true}).click();
 await page.locator('#artboard [data-type=TextPacket]').filter({hasText:/^Зелёные$/}).dblclick();
 assert.equal(await page.locator('#tree [data-node-path]').filter({hasText:/^Таблица$/}).getAttribute('aria-expanded'),'true');
 assert.deepEqual(errors,[]);console.log('Tree: single-click collapse, selection/editing, reload, per-window state, keyboard, reveal and bulk controls passed');
}finally{await browser.close();}
