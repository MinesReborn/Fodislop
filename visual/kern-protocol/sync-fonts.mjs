import fs from 'node:fs';
import path from 'node:path';
const root=new URL('../../Assets/Resources/Fonts/',import.meta.url),catalog={};
for(const dir of fs.readdirSync(root,{withFileTypes:true}).filter(d=>d.isDirectory())){
 const folder=new URL(dir.name+'/',root);for(const file of fs.readdirSync(folder).filter(f=>f.endsWith('.asset'))){const source=fs.readFileSync(new URL(file,folder),'utf8'),family=/m_FamilyName: (.+)/.exec(source)?.[1]?.trim(),name=path.basename(file,'.asset');if(!family||!source.includes('  m_Name: '+name+'\n'))continue;const guid=/m_SourceFontFileGUID: (\w+)/.exec(source)?.[1];const font=fs.readdirSync(folder).filter(f=>/\.(ttf|otf)$/.test(f)).find(f=>fs.readFileSync(new URL(f+'.meta',folder),'utf8').includes('guid: '+guid));if(!font)throw Error('Missing source font for '+file);catalog[name]={Family:family,Asset:'Assets/Resources/Fonts/'+dir.name+'/'+file,Resource:'Fonts/'+dir.name+'/'+name,Source:'../../Assets/Resources/Fonts/'+dir.name+'/'+font};}}
fs.writeFileSync(new URL('./font-catalog.json',import.meta.url),JSON.stringify(catalog,null,2)+'\n');console.log('Font assets:',Object.keys(catalog).join(', '));
