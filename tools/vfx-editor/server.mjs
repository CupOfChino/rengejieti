import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { getCatalog, locate } from './catalog.mjs';
import { previewBridge } from './bridge.mjs';
import { reviewRoutes } from './reviews.mjs';
import { readSkillProject } from './skill-projects.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here,'../..');
const temp = path.join(repo,'mods','可视化攻击目标','temp','特效编辑器缓存');
const review = reviewRoutes(path.join(temp,'检查记录'));
const pages = new Map([
  ['/',['index.html','text/html; charset=utf-8']],
  ['/style.css',['style.css','text/css; charset=utf-8']],
  ['/app.mjs',['app.mjs','text/javascript; charset=utf-8']],
  ['/audio.mjs',['audio.mjs','text/javascript; charset=utf-8']],
  ['/live',['live.html','text/html; charset=utf-8']],
  ['/live.mjs',['live.mjs','text/javascript; charset=utf-8']],
  ['/animation.mjs',['animation.mjs','text/javascript; charset=utf-8']],
  ['/scene.mjs',['scene.mjs','text/javascript; charset=utf-8']],
  ['/zip.mjs',['zip.mjs','text/javascript; charset=utf-8']]
]);
const contentTypes = {'.png':'image/png','.webp':'image/webp','.jpg':'image/jpeg','.jpeg':'image/jpeg','.gif':'image/gif'};
const audioTypes = {'.ogg':'audio/ogg','.wav':'audio/wav','.mp3':'audio/mpeg','.m4a':'audio/mp4',
  '.aac':'audio/aac','.opus':'audio/ogg','.flac':'audio/flac'};
const sounds = new Map();
const valennaRoot=path.join(repo,'mods','boss战 瓦伦希娜');
const soundRoots = [
  ['瓦伦希娜',path.join(valennaRoot,'resource','sound')],
  ['共用音效',path.join(repo,'resource','sound')]
];
async function scanSounds(label,root,directory=root) {
  const entries=await fs.readdir(directory,{withFileTypes:true}).catch(()=>[]);
  entries.sort((a,b)=>a.name.localeCompare(b.name,'zh-Hans',{numeric:true}));
  for (const entry of entries) {
    const filename=path.join(directory,entry.name);
    if (entry.isDirectory()) {
      await scanSounds(label,root,filename);
    } else if (entry.isFile() && Object.hasOwn(audioTypes,path.extname(entry.name).toLowerCase())) {
      const parts=path.relative(root,filename).split(path.sep);
      const display=label+' / '+parts.join(' / ');
      const id=createHash('sha256').update(display).digest('hex').slice(0,20);
      const installable=label==='瓦伦希娜' && path.extname(filename).toLowerCase()==='.ogg';
      sounds.set(id,{id,label:display,filename,valenna:installable?parts.join('/'):null});
    }
  }
}
for (const [label,root] of soundRoots) await scanSounds(label,root);
const defaultSubtitles=new Map();
try{
  const data=JSON.parse(await fs.readFile(path.join(valennaRoot,'resource','voice-subtitles.json'),'utf8'));
  for(const line of data.lines){
    if(typeof line.file==='string'&&typeof line.text==='string')defaultSubtitles.set(line.file,line.text);
  }
}catch(error){if(error.code!=='ENOENT')console.warn('读取默认台词失败：'+error.message);}
let catalog = {assets:[],notes:[],loading:true};
let media = new Map();
const paths = await locate(repo);
const handlePreview = await previewBridge(paths);
const valennaSounds=new Map([...sounds.values()].filter(sound=>sound.valenna).map(sound=>[sound.valenna,sound.id]));
const skillAudioFilename=(root,id)=>path.join(root,'resource','skill-audio',id+'.json');

async function writeSkillAudio(filename,body) {
  const token=randomUUID();
  const backup=path.join(valennaRoot,'temp','skill-audio-'+token+'.bak');
  const pending=filename+'.'+token+'.tmp';
  await fs.mkdir(path.dirname(filename),{recursive:true});
  await fs.mkdir(path.dirname(backup),{recursive:true});
  const backedUp=await fs.copyFile(filename,backup).then(()=>true,error=>{
    if(error.code==='ENOENT')return false;
    throw error;
  });
  try {
    await fs.writeFile(pending,body,'utf8');
    await fs.rename(pending,filename);
    if(await fs.readFile(filename,'utf8')!==body)throw new Error('配置写入复核失败');
    if(backedUp)await fs.unlink(backup);
  } finally {
    await fs.rm(pending,{force:true});
  }
}

let savingSkillAudio=false;
async function skillAudio(request,response,host,id) {
  const post=request.method==='POST';
  function reply(code,result){
    response.writeHead(code,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
    response.end(JSON.stringify(result));
  }
  if(!post&&request.method!=='GET')return reply(405,{error:'请求方式不支持'});
  if(post&&request.headers.origin!=='http://'+host)return reply(403,{error:'来源不受信任'});
  const filename=skillAudioFilename(valennaRoot,id);
  if(!post){
    try{
      const data=JSON.parse((await fs.readFile(filename,'utf8')).replace(/^\uFEFF/,''));
      reply(200,{cues:data.cues.map(({file,at,volume,speed=1,group=0,subtitle})=>({
        sound:valennaSounds.get(file)??createHash('sha256').update('瓦伦希娜 / '+file.split('/').join(' / ')).digest('hex').slice(0,20),
        at,volume,speed,group,subtitle:subtitle??defaultSubtitles.get(file)??''
      }))});
    }catch(error){
      reply(error.code==='ENOENT'?404:500,{error:error.code==='ENOENT'?'这项技能还没有音效配置':'技能音效配置读取失败'});
    }
    return;
  }
  if(savingSkillAudio)return reply(409,{error:'正在保存另一项技能音效'});
  savingSkillAudio=true;
  try{
    let size=0;
    const chunks=[];
    for await(const chunk of request){
      size+=chunk.length;
      if(size>256*1024)return reply(413,{error:'音效配置过大'});
      chunks.push(chunk);
    }
    let input;
    try {
      input=JSON.parse(Buffer.concat(chunks).toString('utf8'));
    } catch {
      return reply(400,{error:'音效配置 JSON 无效'});
    }
    const cues=input?.cues;
    if(!Array.isArray(cues)||cues.length>64||cues.some(cue=>
      !sounds.get(cue?.sound)?.valenna||!Number.isFinite(cue.at)||cue.at<0||cue.at>60||
      !Number.isFinite(cue.volume)||cue.volume<0||cue.volume>100)){
      return reply(400,{error:'技能只支持瓦伦希娜音效库中的 OGG，请检查缺失素材、时间和音量'});
    }
    if(cues.some(({speed=1,group=0,subtitle=''})=>!Number.isFinite(speed)||speed<0.5||speed>2||
      !Number.isInteger(group)||group<0||group>32||typeof subtitle!=='string'||subtitle.length>240)){
      return reply(400,{error:'请检查语速、随机组和字幕长度（最多 240 字）'});
    }
    const body=JSON.stringify({cues:cues.map(({sound,at,volume,speed=1,group=0,subtitle})=>({
      file:sounds.get(sound).valenna,at,volume,speed,group,subtitle
    }))},null,2)+'\n';
    if(Buffer.byteLength(body)>256*1024)return reply(413,{error:'音效配置过大'});
    await writeSkillAudio(filename,body);
    const hosts=paths.workshop.map(directory=>path.join(directory,'3813306323'));
    let synced=false;
    try{
      for(const root of hosts){
        const plugin=await fs.stat(path.join(root,'plugins','ValennaBattle.dll')).catch(()=>null);
        if(!plugin?.isFile())continue;
        await writeSkillAudio(skillAudioFilename(root,id),body);
        synced=true;
      }
    }catch{
      return reply(200,{synced,warning:'模组工程已应用，但有测试宿主的同步失败'});
    }
    reply(200,{synced});
  }catch(error){
    reply(500,{error:'应用失败：'+error.message});
  }finally{
    savingSkillAudio=false;
  }
}

const server = http.createServer(async (request,response)=>{
  response.setHeader('X-Content-Type-Options','nosniff');
  response.setHeader('Referrer-Policy','no-referrer');
  response.setHeader('Content-Security-Policy', "default-src 'self'; img-src 'self' data: blob:; style-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'");
  const host = '127.0.0.1:'+server.address().port;
  if (request.headers.host !== host || request.headers['sec-fetch-site'] === 'cross-site') {
    response.writeHead(403).end();
    return;
  }
  const route = (request.url ?? '').split('?')[0];
  const previewSkill=/^\/skill-preview\/(\d+)$/.exec(route);
  if(previewSkill && request.method==='GET'){
    try{
      const project=await readSkillProject(path.join(valennaRoot,'temp','特效浏览器'),previewSkill[1]);
      if(!project){response.writeHead(404).end();return;}
      response.writeHead(200,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
      response.end(JSON.stringify(project));
    }catch(error){
      response.writeHead(error.code==='ENOENT'?404:500).end('无法读取该技能的本地分镜工程');
    }
    return;
  }
  const skill= /^\/skill-audio\/(88512[1456789])$/.exec(route);
  if(skill){await skillAudio(request,response,host,skill[1]);return;}
  if (route.startsWith('/live/')) {
    if (request.method !== 'GET' && !(request.method === 'POST' && request.headers.origin === 'http://'+host)) {
      response.writeHead(403).end();
      return;
    }
    await handlePreview(request,response,new URL(request.url,'http://'+host));
    return;
  }
  if (route==='/review'||route.startsWith('/review/')){
    await review(request,response,route,host);
    return;
  }
  if (request.method !== 'GET') { response.writeHead(405).end(); return; }
  if (route === '/sounds') {
    response.writeHead(200,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
    response.end(JSON.stringify([...sounds.values()].map(({id,label,valenna})=>({id,label,subtitle:defaultSubtitles.get(valenna)}))));
    return;
  }
  if (route === '/catalog') {
    response.writeHead(200,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
    response.end(JSON.stringify(catalog));
    return;
  }
  const page = pages.get(route);
  const isMedia = /^\/media\/[a-f0-9]{20}$/.test(route);
  const sound = /^\/sound\/[a-f0-9]{20}$/.test(route) ? sounds.get(route.slice(7)) : undefined;
  const filename = page ? path.join(here,page[0]) : isMedia ? media.get(route.slice(7)) : sound?.filename;
  if (!filename) { response.writeHead(404).end(); return; }
  const info = await fs.stat(filename).catch(()=>null);
  if (!info?.isFile()) { response.writeHead(404).end(); return; }
  const ext=path.extname(filename).toLowerCase();
  const headers={'Content-Type':page?.[1] ?? (sound ? audioTypes[ext] : contentTypes[ext]),
    'Content-Length':info.size,'Cache-Control':'no-cache'};
  let range;
  if (sound) {
    headers['Accept-Ranges']='bytes';
    if (request.headers.range) {
      const match=/^bytes=(\d*)-(\d*)$/.exec(request.headers.range);
      const start=match?.[1] ? Number(match[1]) : Math.max(0,info.size-Number(match?.[2]));
      const end=match?.[1]&&match[2] ? Math.min(Number(match[2]),info.size-1) : info.size-1;
      if (!match || (!match[1]&&!match[2]) || start>end) {
        response.writeHead(416,{'Content-Range':'bytes */'+info.size}).end();
        return;
      }
      range={start,end};
      headers['Content-Range']='bytes '+start+'-'+end+'/'+info.size;
      headers['Content-Length']=end-start+1;
    }
  }
  response.writeHead(range ? 206 : 200,headers);
  const stream = createReadStream(filename,range);
  stream.on('error',()=>response.destroy());
  response.on('close',()=>stream.destroy());
  stream.pipe(response);
});

server.listen(Number(process.env.VFX_PORT || 0),'127.0.0.1', async ()=>{
  const url = 'http://127.0.0.1:'+server.address().port+'/';
  console.log(url);
  if (!process.argv.includes('--no-browser') && process.platform === 'win32') {
    // URL 来自本地数字端口，不接受用户传入的命令片段。
    const child = spawn('rundll32.exe',['url.dll,FileProtocolHandler',url],{stdio:'ignore',windowsHide:true});
    child.once('error',()=>console.log('请在浏览器打开上方地址。'));
  }
  console.log('正在查找游戏和模组素材。关闭本窗口会退出编辑器。');
  try {
    await getCatalog(paths,temp,(next,index)=>{
      catalog=next;
      media=index;
    });
    console.log('素材库完成，共 '+catalog.assets.length+' 个可用素材。');
  } catch (error) {
    catalog={...catalog, loading:false, notes:[...catalog.notes,'素材扫描中断：'+error.message]};
    console.error('素材扫描中断：'+error.message);
  }
});
