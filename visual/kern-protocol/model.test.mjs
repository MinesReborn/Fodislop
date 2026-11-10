import {test} from 'node:test';import assert from 'node:assert/strict';import {validatePacket,types} from './validate.mjs';
const packet=()=>({WindowTag:'test',Width:600,Height:400,Content:{Type:'TextPacket',Text:'Текст {без вычислений}',Style:null,OnClickContext:'',AttachedProperties:[]}});
test('13 component types',()=>assert.equal(types.length,13));
test('literal text is valid',()=>assert.deepEqual(validatePacket(packet()),[]));
test('unknown properties rejected',()=>{const p=packet();p.Content.Shadow=10;assert.ok(validatePacket(p).length);});
test('ushort window boundary',()=>{const p=packet();p.Width=65536;assert.ok(validatePacket(p).length);p.Width=65535;assert.deepEqual(validatePacket(p),[]);});
test('unicode tags rejected',()=>{const p=packet();p.WindowTag='окно';assert.ok(validatePacket(p).length);});
test('Panel is not a container',()=>{const p=packet();p.Content={...p.Content,Type:'PanelPacket',Children:[]};delete p.Content.Text;assert.ok(validatePacket(p).length);});
test('byte margins enforced',()=>{const p=packet();p.Content.Style={Background:'#000000',Border:'#ffffff',BorderWidth:1,Margin:{Left:256,Top:0,Right:0,Bottom:0},Padding:{Left:0,Top:0,Right:0,Bottom:0}};assert.ok(validatePacket(p).length);});
import fs from 'node:fs';
const reference=JSON.parse(fs.readFileSync(new URL('./reference-packets.json',import.meta.url)));
for(const [name,fixture]of Object.entries(reference))test('Kern UI reference / '+name,()=>{assert.deepEqual(validatePacket(fixture.Packet),[]);assert.ok(fixture.Gaps.length>0);});
test('catalog matches actual C# registered component types',()=>{const source=fs.readFileSync(new URL('../../Packages/MinesServerNetworking/MinesServer.Networking/Server/Packets/GUI/GUIComponentPacketCodeProvider.cs',import.meta.url),'utf8');assert.deepEqual([...source.matchAll(/Register<(\w+)>/g)].map(m=>m[1]).sort(),[...types].sort());});
import {parseRichText} from './rich-text.mjs';
test('Unity size and color preserve nested text',()=>assert.deepEqual(parseRichText('<size=28><color=#ff5a1f><b>Рынок</b></color></size>').Children,[{Tag:'size',Value:'28',Children:[{Tag:'color',Value:'#ff5a1f',Children:[{Tag:'b',Value:undefined,Children:['Рынок']}]}]}]));
test('HTML is not interpreted as Unity text markup',()=>assert.deepEqual(parseRichText('<img src=x onerror=alert(1)>').Children,['<img src=x onerror=alert(1)>']));

import {measureGrid} from './grid-layout.mjs';
test('Grid allocates remaining space after Auto tracks',()=>{const items=[{Row:0,Column:0,RowSpan:1,ColumnSpan:1,Width:80,Height:20},{Row:0,Column:1,RowSpan:1,ColumnSpan:1,Width:10,Height:20},{Row:0,Column:2,RowSpan:1,ColumnSpan:1,Width:10,Height:20}];assert.deepEqual(measureGrid([0,1,3],[0],items,400,100).map(r=>r.Width),[80,80,240]);});
test('Grid distributes a spanning Auto cell across its tracks',()=>{assert.deepEqual(measureGrid([0,0],[0],[{Row:0,Column:0,RowSpan:1,ColumnSpan:2,Width:100,Height:20}],400,100),[{Left:0,Top:0,Width:100,Height:20}]);});
for(const [name,fixture]of Object.entries(JSON.parse(fs.readFileSync(new URL('./design-packets.json',import.meta.url)))))test('Measured protocol fixture / '+name,()=>assert.deepEqual(validatePacket(fixture.Packet),[]));
test('Typography is expressed by existing rich text tags',()=>{const parsed=parseRichText('<align=right><nobr><font-weight=700><cspace=1><line-height=24>123</line-height></cspace></font-weight></nobr></align>');assert.equal(parsed.Children[0].Tag,'align');assert.equal(parsed.Children[0].Children[0].Tag,'nobr');assert.equal(parsed.Children[0].Children[0].Children[0].Tag,'font-weight');});

import {plainText,replaceText,setTextProperty} from './rich-text.mjs';
test('plain text editing keeps existing formatting tags',()=>assert.equal(replaceText('<size=28><color=#ff0000>Old</color></size>','New'),'<size=28><color=#ff0000>New</color></size>'));
test('text property updates preserve content',()=>{const value=setTextProperty('<size=28>Title</size>','size',32);assert.equal(plainText(value),'Title');assert.equal(value,'<size=32>Title</size>');});

const authored=JSON.parse(fs.readFileSync(new URL('./design-packets.json',import.meta.url)));
for(const [name,fixture]of Object.entries(authored))test('Authoring hierarchy / '+name,()=>{assert.ok(Object.values(fixture.EditorLayers).includes('Шапка'));assert.ok(Object.values(fixture.EditorLayers).includes('Подвал')||name==='clan_list');assert.ok(fixture.EditorLayers.Content==='Окно');});
test('Market table contains actual row containers',()=>{const f=authored.market;const path=Object.entries(f.EditorLayers).find(([,label])=>label==='Таблица')[0];let n=f.Packet.Content;for(const i of path.split('/').filter(x=>/^\d+$/.test(x)))n=n.Children[+i];assert.equal(n.Children.length,7);assert.ok(n.Children.every(row=>row.Type==='CanvasPacket'&&row.Children.length===7));});

import {clearTextProperty} from './rich-text.mjs';
test('font tags preserve the actual asset identity',()=>assert.equal(parseRichText('<font=Exo2_SDF>Title</font>').Children[0].Value,'Exo2_SDF'));
test('clearing an inherited font keeps the other text properties',()=>assert.equal(clearTextProperty('<font=Exo2_SDF><size=28>Title</size></font>','font'),'<size=28>Title</size>'));
import {composeBorders} from './compose-borders.mjs';
test('one-sided solid borders become ordinary packet composition',()=>{const p={Type:'TextPacket',Text:'Label',Style:{Background:'#112233',Border:'#ffffff',BorderWidth:1,Margin:{Top:0,Right:0,Bottom:0,Left:0},Padding:{Top:2,Right:2,Bottom:2,Left:2}},OnClickContext:'/',AttachedProperties:[]};const none={Width:0,Color:'#00000000',Stroke:'none'};const result=composeBorders(p,{Width:100,Height:30},{Top:{Width:1,Color:'#ffffff',Stroke:'solid'},Right:none,Bottom:none,Left:none});assert.equal(result.node.Type,'CanvasPacket');assert.equal(result.lines.length,1);assert.equal(result.lines[0].node.Direction,'Horizontal');assert.equal(result.payload.OnClickContext,'/');assert.equal(result.payload.Style.BorderWidth,0);assert.equal(result.payload.Style.Padding.Top,3);assert.deepEqual(validatePacket({WindowTag:'border',Width:100,Height:30,Content:result.node}),[]);});

import {documentMetadata} from './document-origin.mjs';
test('legacy sample copies retain sample ownership, independent of WindowTag',()=>assert.equal(documentMetadata({id:'design/sample_market_2',packet:{WindowTag:'renamed'}},{market:{}},{market:'Рынок'}).origin,'sample'));
test('a user window named market stays user-owned',()=>assert.equal(documentMetadata({id:'design/local_1',packet:{WindowTag:'market'}},{market:{}},{market:'Рынок'}).origin,'user'));

import {composeTextFlow} from './compose-flow.mjs';
test('text flow conversion produces native DockPanel without child positions',()=>{const style=()=>({Background:'#00000000',Border:'#00000000',BorderWidth:0,Margin:{Top:0,Right:0,Bottom:0,Left:0},Padding:{Top:0,Right:0,Bottom:0,Left:0}});const child=()=>({Type:'TextPacket',Text:'Text',Style:style(),OnClickContext:'',AttachedProperties:[{Key:'Canvas.Y',Value:'0'}]});const a=child(),b=child(),group={Type:'CanvasPacket',Style:style(),OnClickContext:'',AttachedProperties:[{Key:'Canvas.Height',Value:'35'}],Children:[a,b]},boxes=new Map([[a,{X:0,Y:0,Width:100,Height:10}],[b,{X:0,Y:15,Width:100,Height:20}]]);composeTextFlow(group,{X:0,Y:0,Width:100,Height:35},boxes);assert.equal(group.Type,'DockPanelPacket');assert.equal(a.Style.Margin.Bottom,5);assert.ok(!group.AttachedProperties.some(p=>p.Key==='Canvas.Height'));assert.ok(group.Children.every(c=>c.AttachedProperties.every(p=>!p.Key.startsWith('Canvas.'))));});
test('intentional text overlays are not converted into flow',()=>{const a={Type:'TextPacket',Text:'A',Style:null,OnClickContext:'',AttachedProperties:[]},b=structuredClone(a),group={Type:'CanvasPacket',Style:null,OnClickContext:'',AttachedProperties:[],Children:[a,b]},box={X:0,Y:0,Width:100,Height:20};assert.equal(composeTextFlow(group,box,new Map([[a,box],[b,box]])),null);assert.equal(group.Type,'CanvasPacket');});
