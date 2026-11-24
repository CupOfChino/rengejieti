import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {setTimeout as wait} from 'node:timers/promises';
import {parseArgs} from 'node:util';
import {getCatalog, locate} from './catalog.mjs';

const here=path.dirname(fileURLToPath(import.meta.url));
const repo=path.resolve(here,'../..');
const cache=path.join(repo,'mods','可视化攻击目标','temp','特效编辑器缓存');
const usage=[
  'node tools/vfx-editor/check-scene.mjs --list 素材关键词',
  'node tools/vfx-editor/check-scene.mjs 工程.vfx.json [--time 秒] [--times 秒,秒,...] [--reference 工程.vfx.json或图片] [--guides] [--json]',
].join('\n');
const imageMime={'.png':'image/png','.webp':'image/webp','.jpg':'image/jpeg','.jpeg':'image/jpeg'};

function timestamp(text){
  const number=Number(text);
  if(!String(text).trim()||!Number.isFinite(number)||number<0||number>90)
    throw new Error('时间必须是 0 到 90 之间的秒数');
  return number;
}

async function openProject(input){
  const file=path.resolve(input);
  const stats=await fs.stat(file);
  if(!stats.isFile()||stats.size>80*1024*1024)throw new Error('工程文件无效或过大：'+file);
  const data=JSON.parse((await fs.readFile(file,'utf8')).replace(/^\uFEFF/,''));
  if(data?.version!==1||!Array.isArray(data.layers))throw new Error('工程格式不受支持：'+file);
  return {file,data};
}

async function openReference(input){
  const file=path.resolve(input);
  const suffix=path.extname(file).toLowerCase();
  if(suffix==='.json')return {project:await openProject(file)};
  const mime=imageMime[suffix];
  if(!mime)throw new Error('参考须为工程、PNG、WebP 或 JPEG');
  const stats=await fs.stat(file);
  if(!stats.isFile()||stats.size>8*1024*1024)throw new Error('参考图须小于等于 8 MB：'+file);
  return {image:{name:path.basename(file).slice(0,120),data:'data:'+mime+';base64,'+(await fs.readFile(file)).toString('base64')}};
}

async function listAssets(input){
  const search=input.trim().toLocaleLowerCase();
  if(!search)throw new Error('请提供素材关键词');
  let catalog;
  await getCatalog(await locate(repo),cache,snapshot=>{catalog=snapshot;});
  const matches=catalog.assets.filter(asset=>[asset.label,asset.displayLabel,asset.source,asset.id]
    .some(text=>text?.toLocaleLowerCase().includes(search)));
  const assets=matches.slice(0,80).map(asset=>({id:asset.id,kind:asset.kind,
    name:asset.displayLabel??asset.label,source:asset.source,frames:asset.frames.length,
    actions:Object.keys(asset.clips??{})}));
  console.log(JSON.stringify({total:matches.length,assets}));
}

async function poll(label,fn,limit=20000){
  const deadline=Date.now()+limit;
  while(Date.now()<deadline){
    const value=await fn();
    if(value)return value;
    await wait(180);
  }
  throw new Error(label+'超时');
}

async function browserExecutable(){
  const roots=[process.env['ProgramFiles(x86)'],process.env.ProgramFiles].filter(Boolean);
  const candidates=[process.env.VFX_BROWSER,
    ...roots.map(root=>path.join(root,'Google','Chrome','Application','chrome.exe')),
    ...roots.map(root=>path.join(root,'Microsoft','Edge','Application','msedge.exe'))];
  for(const candidate of candidates.filter(Boolean))
    if((await fs.stat(candidate).catch(()=>null))?.isFile())return candidate;
  throw new Error('找不到 Edge 或 Chrome。可以用 VFX_BROWSER 指定浏览器程序。');
}

async function connect(url){
  const socket=new WebSocket(url);
  await new Promise((resolve,reject)=>{
    socket.addEventListener('open',resolve,{once:true});
    socket.addEventListener('error',()=>reject(new Error('无法连接无窗口浏览器')),{once:true});
  });
  let next=0;
  const pending=new Map();
  socket.addEventListener('close',()=>{
    for(const callback of pending.values()){
      clearTimeout(callback.timer);
      callback.reject(new Error('无窗口浏览器已断开'));
    }
    pending.clear();
  });
  socket.addEventListener('message',event=>{
    const message=JSON.parse(event.data);
    const callback=pending.get(message.id);
    if(!callback)return;
    pending.delete(message.id);
    clearTimeout(callback.timer);
    if(message.error)callback.reject(new Error(message.error.message));
    else callback.resolve(message.result);
  });
  const call=(method,params={})=>new Promise((resolve,reject)=>{
    const id=++next;
    const timer=setTimeout(()=>{pending.delete(id);reject(new Error(method+' 超时'));},10000);
    pending.set(id,{resolve,reject,timer});
    socket.send(JSON.stringify({id,method,params}));
  });
  const evaluate=async expression=>{
    const result=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
    if(result.exceptionDetails)throw new Error(result.exceptionDetails.exception?.description??'页面执行失败');
    return result.result.value;
  };
  return {socket,call,evaluate};
}

async function stop(child){
  if(!child||child.exitCode!==null||child.signalCode!==null)return;
  const finished=new Promise(resolve=>child.once('exit',resolve));
  child.kill();
  await Promise.race([finished,wait(2800)]);
}

async function run(){
  const {positionals,values}=parseArgs({allowPositionals:true,options:{
    time:{type:'string'},times:{type:'string'},reference:{type:'string'},guides:{type:'boolean'},
    json:{type:'boolean'},help:{type:'boolean'},list:{type:'string'},
  }});
  if(values.help){console.log(usage);return;}
  if(values.list!==undefined){
    if(positionals.length||Object.keys(values).length!==1)throw new Error(usage);
    await listAssets(values.list);
    return;
  }
  if(positionals.length!==1)throw new Error(usage);
  const times=values.times===undefined?undefined:[...new Set(values.times.split(',').map(timestamp))].sort((a,b)=>a-b);
  if(times?.length>8)throw new Error('一次最多选择 8 个不同的时间点');
  const current=values.time===undefined?times?.[0]:timestamp(values.time);
  const candidate=await openProject(positionals[0]);
  const reference=values.reference===undefined?{}:await openReference(values.reference);
  const executable=await browserExecutable();
  await fs.mkdir(cache,{recursive:true});
  const profile=await fs.mkdtemp(path.join(cache,'无窗口检查-'));
  let editor,browser,client;
  try{
    let startup='', failure=null;
    editor=spawn(process.execPath,[path.join(here,'server.mjs'),'--no-browser'],
      {cwd:repo,env:{...process.env,VFX_PORT:'0'},windowsHide:true,stdio:['ignore','pipe','ignore']});
    editor.on('error',error=>{failure=error;});
    editor.stdout.on('data',data=>{startup=(startup+data.toString()).slice(-8192);});
    const url=await poll('启动本机编辑器',()=>{
      if(failure||editor.exitCode!==null)throw failure??new Error('本机编辑器提前退出');
      return startup.match(/http:\/\/127\.0\.0\.1:\d+\//)?.[0];
    });
    const needed=new Set([reference.project,candidate].filter(Boolean).flatMap(({data})=>
      [data.model,data.rightModel,...data.layers.map(layer=>layer?.asset)])
      .filter(id=>typeof id==='string'&&id&&id!=='glow'&&!id.startsWith('import-')));
    await poll('定位工程所需素材',async()=>{
      const data=await fetch(url+'catalog').then(response=>response.json());
      const known=new Set(data.assets.map(asset=>asset.id));
      const missing=[...needed].filter(id=>!known.has(id));
      if(missing.length&&!data.loading)throw new Error('本机缺少工程引用的素材 ID：'+missing.join('、'));
      return !missing.length;
    },120000);
    const reviewUrl=url+'?agent-review=1';
    let browserErrors='';
    browser=spawn(executable,['--headless=new','--disable-gpu','--disable-background-networking','--no-first-run',
      '--no-default-browser-check','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1',
      '--user-data-dir='+profile,reviewUrl],{windowsHide:true,stdio:['ignore','ignore','pipe']});
    browser.on('error',error=>{failure=error;});
    browser.stderr.on('data',data=>{browserErrors=(browserErrors+data.toString()).slice(-1800);});
    const port=await poll('启动无窗口浏览器',async()=>{
      if(failure||browser.exitCode!==null)throw failure??
        new Error('无窗口浏览器提前退出（'+browser.exitCode+'） '+browserErrors.trim());
      const text=await fs.readFile(path.join(profile,'DevToolsActivePort'),'utf8').catch(()=>'');
      return /^\d+/.exec(text)?.[0];
    });
    const tab=await poll('打开检查画布',async()=>{
      const pages=await fetch('http://127.0.0.1:'+port+'/json/list').then(response=>response.json()).catch(()=>[]);
      return pages.find(page=>page.type==='page'&&page.url===reviewUrl);
    });
    client=await connect(tab.webSocketDebuggerUrl);
    await poll('读取页面控件',()=>client.evaluate("document.readyState==='complete' && !!window.vfxAgentReview"));
    const {root}=await client.call('DOM.getDocument');
    const {nodeId}=await client.call('DOM.querySelector',{nodeId:root.nodeId,selector:'#project-file'});
    async function loadProject({file}){
      const previous=await client.evaluate('window.vfxAgentReview.loaded');
      await client.call('DOM.setFileInputFiles',{nodeId,files:[file]});
      await poll('载入工程 '+path.basename(file),()=>client.evaluate(`window.vfxAgentReview.loaded>${previous}
        ? document.querySelector('#status').textContent : ''`).then(status=>{
        if(status.startsWith('打开失败'))throw new Error(status);
        return status;
      }));
    }
    if(reference.project){
      await loadProject(reference.project);
      await client.evaluate('window.vfxAgentReview.rememberReference()');
    }
    await loadProject(candidate);
    const options={current,times,guides:values.guides,referenceImage:reference.image};
    await client.evaluate(`window.vfxAgentReview.save(${JSON.stringify(options)})`);
    const id=await poll('生成检查画面',()=>client.evaluate(`(() => {
      const button=document.querySelector('#review');
      const status=document.querySelector('#status').textContent;
      return button.dataset.result || (!button.disabled && status.startsWith('检查图保存失败') ? status : '');
    })()`).then(result=>{if(result?.startsWith('检查图保存失败'))throw new Error(result);return result;}),45000);
    const directory=path.join(cache,'检查记录',id);
    if(values.json){
      const manifest=path.join(directory,'manifest.json');
      const result=JSON.parse(await fs.readFile(manifest,'utf8'));
      console.log(JSON.stringify({directory,manifest,current:path.join(directory,'current.png'),
        timeline:path.join(directory,'timeline.png'),
        ...(result.reference?{comparison:path.join(directory,'comparison.png'),reference:result.reference}:{}),
        times:result.times,roles:result.roles}));
    }else console.log(directory);
  }finally{
    if(client?.socket.readyState===WebSocket.OPEN){
      await client.call('Browser.close').catch(()=>{});
      client.socket.close();
    }
    await Promise.all([stop(browser),stop(editor)]);
    const parent=await fs.realpath(cache);
    const resolved=await fs.realpath(profile).catch(()=>null);
    if(resolved&&path.dirname(resolved)===parent&&path.basename(resolved).startsWith('无窗口检查-'))
      await fs.rm(resolved,{force:true,recursive:true,maxRetries:3,retryDelay:200}).catch(()=>{});
  }
}

run().catch(error=>{
  if(error.cause?.code==='EACCES')console.error('无法访问本机编辑器。当前环境禁止连接 127.0.0.1。');
  else console.error(error.message);
  process.exitCode=1;
});
