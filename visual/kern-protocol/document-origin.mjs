// Editor provenance only; never infer ownership from a server WindowTag.
export function documentMetadata(document,samples,titles){
 const id=document.id.split('/')[1],saved=document.meta;
 if(saved&&['sample','user'].includes(saved.origin)&&typeof saved.title==='string')return {origin:saved.origin,title:saved.title,source:saved.source};
 let source=Object.hasOwn(samples,id)?id:null;
 if(!source)source=Object.keys(samples).find(key=>id==='sample_'+key||new RegExp('^sample_'+key+'_\\d+$').test(id));
 if(source)return {origin:'sample',source,title:(titles[source]||source)+(id===source?'':' · копия образца')};
 return {origin:'user',title:document.packet.WindowTag||'Окно'};
}
