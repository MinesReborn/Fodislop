import assert from 'node:assert/strict';
import { chromium } from '@playwright/test';
const browser=await chromium.launch({headless:true});
const page=await browser.newPage({viewport:{width:1600,height:1000}});
const errors=[];page.on('pageerror',e=>errors.push(e.message));
await page.goto('http://127.0.0.1:8791/visual/kern-protocol/');
await page.waitForSelector('#artboard .node');await page.locator('.theme-menu').evaluate(el=>el.open=true);await page.locator('.source-menu').evaluate(el=>el.open=true);
await page.evaluate(()=>document.fonts.ready);
console.log('Initial validation:',await page.locator('#validation').innerText());
await page.screenshot({path:'/tmp/kern-protocol-canvas.png',fullPage:true});
for(const composition of ['structure','design']){await page.selectOption('#composition',composition);for(const scene of ['resp','gun','clan_list','box','donate','clan_info','market']){await page.locator('#scene').selectOption(scene,{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');for(const mode of ['base','design','uss']){await page.click('#'+mode);await page.waitForTimeout(30);assert.ok(await page.locator('#artboard>.node').count());}console.log(composition+'/'+scene+': rendered in all three modes');}}
await page.click('#interact');
await page.locator('#artboard').getByRole('button',{name:'Продать',exact:true}).first().click();
const event=JSON.parse(await page.locator('#events').innerText());assert.equal(event.WindowTag,'market');assert.equal(event.Context.length,6);assert.equal(event.Context[0].Key,'input_0');console.log('Click context: six named inputs collected');
const field=page.locator('#artboard input').first();
if(await field.count()){await field.evaluate(el=>{el.value='73';el.dispatchEvent(new Event('input',{bubbles:true}));});await page.click('#uss');assert.equal(await field.inputValue(),'73');await page.click('#plus');assert.equal(await field.inputValue(),'73');}
await page.click('#uss');
assert.ok(await page.locator('#artboard').evaluate(el=>getComputedStyle(el).backgroundColor!=='rgba(0, 0, 0, 0)'));
assert.ok(await page.locator('#validation').innerText().then(text=>text.includes('USS:')));
await page.screenshot({path:'/tmp/kern-protocol-uss.png',fullPage:true});
await page.click('#base');await page.click('#design');
if(await field.count())assert.equal(await field.inputValue(),'73');
await page.locator('#scene').selectOption('resp',{force:true});await page.waitForFunction(()=>document.getElementById('artboard').dataset.layoutReady==='true');
const selectable=page.locator('#artboard [data-type=SelectablePacket]').first();
if(await selectable.count()){await selectable.click();const action=page.locator('#artboard button[data-type=TextPacket]').first();await action.click();const data=JSON.parse(await page.locator('#events').innerText());assert.ok(data.Context.some(x=>['True','False'].includes(x.Value)));}
console.log('Errors:',errors);
await browser.close();
if(errors.length)process.exitCode=1;
