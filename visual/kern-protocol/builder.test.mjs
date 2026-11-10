import {test} from 'node:test';
import assert from 'node:assert/strict';
import {editTree,DocumentHistory,groupNodes,ungroupNode} from './document-model.mjs';
import {snapRect} from './snapping.mjs';
const node=(text,context='')=>({Type:'TextPacket',Text:text,Style:null,OnClickContext:context,AttachedProperties:[]});
const packet=()=>({WindowTag:'test',Width:640,Height:480,Content:{Type:'CanvasPacket',Style:null,OnClickContext:'',AttachedProperties:[],Children:[node('Target'),node('Action','/0')]}});
test('reordering preserves the target of OnClickContext',()=>{const result=editTree(packet(),[0],'down');assert.equal(result.packet.Content.Children[0].OnClickContext,'/1');assert.deepEqual(result.path,[1]);});
test('deletion of a referenced target is blocked without changing input',()=>{const p=packet(),before=JSON.stringify(p);assert.throws(()=>editTree(p,[0],'delete'),/OnClickContext/);assert.equal(JSON.stringify(p),before);});
test('duplicating a subtree preserves its internal targets',()=>{const p=packet();p.Content.Children=[{...p.Content,Children:[node('Target'),node('Action','../0')]}];const copy=editTree(p,[0],'duplicate');assert.equal(copy.packet.Content.Children[1].Children[1].OnClickContext,'/1/0');});
test('moving a container into itself is blocked',()=>{const p=packet();assert.throws(()=>editTree(p,[],'move',[]));});
test('history preserves a cleared selection',()=>{const h=new DocumentHistory(),p=packet();h.record(p,null);p.Width=800;h.record(p,[]);assert.equal(h.step(-1).path,null);});
test('grid snapping handles negative coordinates',()=>assert.equal(snapRect({x:-13,y:19,width:20,height:20},{mode:'grid',step:8}).rect.x,-16));
test('object snapping aligns centres within tolerance',()=>{const result=snapRect({x:47,y:50,width:10,height:10},{mode:'objects',targets:[{x:0,y:0,width:100,height:100}],tolerance:3});assert.equal(result.rect.x,45);assert.equal(result.guides[0].value,50);});
test('combined snapping prefers a nearby object over grid',()=>{const result=snapRect({x:13,y:9,width:20,height:20},{mode:'both',step:8,targets:[{x:14,y:100,width:50,height:50}],tolerance:2});assert.equal(result.rect.x,14);assert.equal(result.rect.y,8);});
test('resize snaps the far edge without moving its origin',()=>{const result=snapRect({x:3,y:5,width:20,height:20},{mode:'grid',step:8,resize:true});assert.equal(result.rect.x,3);assert.equal(result.rect.width,21);});
test('disabled snapping preserves geometry',()=>{const rect={x:13.5,y:2.7,width:21,height:7};assert.deepEqual(snapRect(rect,{mode:'none'}).rect,rect);});

test('flow insertion reorders children and removes free coordinates',()=>{const p=packet();p.Content.Type='DockPanelPacket';p.Content.Children[1].AttachedProperties=[{Key:'Canvas.X',Value:'80'}];const result=editTree(p,[1],'insert',{parent:[],index:0});assert.equal(result.packet.Content.Children[0].Text,'Action');assert.equal(result.packet.Content.Children[0].OnClickContext,'/1');assert.ok(!result.packet.Content.Children[0].AttachedProperties.some(p=>p.Key.startsWith('Canvas.')));});

test('grouping keeps coordinates and click targets when ungrouped',()=>{const p=packet();p.Content.Children[0].AttachedProperties=[{Key:'Canvas.X',Value:'40'},{Key:'Canvas.Y',Value:'50'}];p.Content.Children[1].AttachedProperties=[{Key:'Canvas.X',Value:'80'},{Key:'Canvas.Y',Value:'70'}];const grouped=groupNodes(p,[[0],[1]],{x:40,y:50,width:100,height:50});assert.equal(grouped.packet.Content.Children[0].Children[1].OnClickContext,'/0/0');const result=ungroupNode(grouped.packet,[0]);assert.equal(result.packet.Content.Children[1].AttachedProperties.find(p=>p.Key==='Canvas.X').Value,'80');assert.equal(result.packet.Content.Children[1].OnClickContext,'/0');});
