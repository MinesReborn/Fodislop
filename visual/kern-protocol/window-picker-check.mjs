import assert from 'node:assert/strict';
import {chromium} from '@playwright/test';
const browser=await chromium.launch();
try{
 const page=await browser.newPage({viewport:{width:1700,height:1100}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');await page.waitForSelector('#builder-properties');assert.ok(await page.locator('#scene').isHidden());
 await page.click('#window-picker');assert.equal((await page.locator('#window-picker-popup').getByRole('option',{selected:true}).innerText()).replace(/\s+/g,' ').trim(),'✓ Рынок');
 await page.getByLabel('Найти окно',{exact:true}).fill('Респаун');await page.getByLabel('Найти окно',{exact:true}).press('Enter');assert.equal(await page.locator('#scene').inputValue(),'resp');assert.equal(await page.locator('#window-picker span').first().innerText(),'Респаун');
 await page.locator('#tree [data-node-path]').first().click();const selected=await page.locator('#selected').innerText();await page.click('#window-picker');await page.keyboard.press('Escape');assert.equal(await page.locator('#selected').innerText(),selected);assert.ok(await page.locator('#window-picker-popup').isHidden());
 await page.click('#new-window');await page.click('#window-picker');assert.equal(await page.locator('.window-picker-heading').allTextContents().then(a=>a.includes('Мои окна')),true);assert.equal(await page.locator('#window-picker-popup [aria-selected=true]').count(),1);
 await page.getByLabel('Найти окно',{exact:true}).fill('невозможноеимя');assert.equal(await page.locator('#window-picker-popup').getByRole('option').count(),0);assert.ok(await page.getByText('Окна не найдены',{exact:true}).isVisible());await page.keyboard.press('Escape');
 assert.deepEqual(errors,[]);console.log('Window picker: Kern popup, search, selection, own windows, Escape and empty state passed');
}finally{await browser.close();}
