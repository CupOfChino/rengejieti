const byId = id=>document.getElementById(id);
const ids=['preset','layer','image','placeholder','connect','launch','play','state','save','open','file','color','reset-color','reset-layer','disconnect','settings','values'];
const controls=Object.fromEntries(ids.map(id=>[id,byId(id)]));
const settings = {preset:'blade-hit',placement:{x:0,y:0,scale:1,angle:0},edits:new Map()};
const blank = index=>({index,size:1,lifetime:1,speed:1,density:1,color:''});
const option = (label,value)=>new Option(label,value);
const notice = message=>{controls.state.value=message;};
const placementSchema = {x:['X',-1.5,1.5,.05],y:['Y',-1.2,1.2,.05],scale:['缩放',.2,4,.05],angle:['角度',-180,180,1]};
const particleSchema = {size:['大小',.1,4,.05],lifetime:['寿命',.1,4,.05],speed:['速度',.1,4,.05],density:['发射量',0,4,.05]};
let list=[];
let layers=[];
let revision=0;
let appliedRevision=0;
let phase='';
let installed=false;
let busy=false;
let clip=[];
let clipTimer=null;

function currentEdit() {
  const index = Number(controls.layer.value);
  if (!layers.some(layer=>layer.index===index)) return null;
  if (!settings.edits.has(index)) settings.edits.set(index,blank(index));
  return settings.edits.get(index);
}

function buildFields(element,schema,read) {
  const inputs=new Map();
  for (const [key,[label,min,max,step]] of Object.entries(schema)) {
    const row=document.createElement('label'), input=document.createElement('input');
    row.append(label,input);
    Object.assign(input,{id:element.id+'-'+key,type:'number',min,max,step});
    input.addEventListener('input',()=>{
      const values=read();
      if (values && Number.isFinite(input.valueAsNumber)) values[key]=Math.max(min,Math.min(max,input.valueAsNumber));
    });
    input.addEventListener('change',()=>{const values=read();if(values)input.value=values[key];});
    element.append(row);
    inputs.set(key,input);
  }
  return inputs;
}

const placements=buildFields(byId('placement'),placementSchema,()=>settings.placement);
const particles=buildFields(byId('controls'),particleSchema,currentEdit);
const numberText=number=>typeof number==='number'?String(+number.toFixed(4)):number??'—';

function fillFields() {
  for (const [key,input] of placements) input.value=settings.placement[key];
  const current=currentEdit();
  controls.settings.hidden=!current;
  if (!current) return;
  for (const [key,input] of particles) input.value=current[key];
  const layer=layers.find(item=>item.index===current.index);
  controls.color.value=current.color||layer.before.color||'#ffffff';
  const rows=[
    ['大小', 'size'],['寿命','lifetime'],['播放速度','speed'],
    ['每秒发射量','rate'],['爆发粒子数','bursts'],['初始色','color']
  ];
  controls.values.replaceChildren(...rows.map(([label,key])=>{
    const tr=document.createElement('tr');
    const name=document.createElement('th'); name.scope='row';name.textContent=label;
    tr.append(name);
    for (const state of ['before','after']) {
      const td=document.createElement('td');
      td.textContent=numberText(layer[state][key]);
      tr.append(td);
    }
    if (layer.before[key]!==layer.after[key]) tr.className='changed';
    return tr;
  }));
}

function fillLayers() {
  const old=controls.layer.value;
  controls.layer.replaceChildren(...layers.map(l=>option((l.index+1)+'. '+l.name,String(l.index))));
  if (layers.some(l=>String(l.index)===old)) controls.layer.value=old;
  controls.layer.disabled=!layers.length;
  fillFields();
}

function syncButtons() {
  controls.connect.disabled=!installed||busy||!list.length;
  controls.play.disabled=!installed||busy||!layers.length||phase==='rendering';
  controls.disconnect.disabled=!revision||busy||phase==='closed';
  controls.save.disabled=!list.length;
}

function clearClip() {
  clearInterval(clipTimer);
  for (const url of clip) URL.revokeObjectURL(url);
  clip=[];
  controls.image.hidden=true;
  controls.image.removeAttribute('src');
  controls.placeholder.hidden=false;
}

async function showClip(reply) {
  const images=[];
  try {
    for (const i of Array.from({length:reply.frameCount},(_,i)=>i)) {
      const response=await fetch('/live/frame?revision='+reply.revision+'&index='+i);
      if (!response.ok) throw new Error('预览帧下载失败');
      images.push(URL.createObjectURL(await response.blob()));
    }
    if (reply.revision!==revision) return;
    clearClip();
    clip=images.splice(0);
    controls.image.hidden=false;
    controls.placeholder.hidden=true;
    let i=0;
    const step=()=>{controls.image.src=clip[i++%clip.length];};
    step();
    clipTimer=setInterval(step,1000/12);
    notice('预览完成 · '+reply.frameCount+' 帧');
  } catch(error) {
    if (reply.revision===revision) notice(error.message);
  } finally {
    for (const url of images) URL.revokeObjectURL(url);
  }
}

async function submit(operation) {
  if (busy||!installed||!list.length) return;
  busy=true;
  const snapshot={operation,preset:settings.preset,placement:settings.placement,edits:[...settings.edits.values()]};
  syncButtons();
  try {
    const response=await fetch('/live/command',{
      method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(snapshot)
    });
    const body=await response.json();
    if (!response.ok) throw new Error(body.error);
    revision=body.revision;
    phase='';
    appliedRevision=0;
    notice(operation==='close'?'正在关闭预览…':'等待读取游戏中的预制体…');
    if (operation==='close') clearClip();
  } catch(error) { notice(error.message); }
  finally { busy=false;syncButtons(); }
}

async function poll() {
  try {
    const response=await fetch('/live/status',{cache:'no-store'});
    const info=await response.json();
    if (!response.ok) throw new Error(info.error);
    installed=info.installed;
    if (!list.length) {
      list=info.presets;
      controls.preset.replaceChildren(...list.map(p=>option(p.label,p.id)));
      controls.preset.value=settings.preset;
      notice(installed?'可读取已打开的游戏，也可以从这里启动。':'未找到本机测试插件。');
      fillFields();
    }
    const reply=info.reply;
    if (reply&&reply.revision===revision&&reply.preset===settings.preset&&
        (reply.revision!==appliedRevision||reply.phase!==phase)) {
      phase=reply.phase;
      appliedRevision=reply.revision;
      if (reply.layers) {layers=reply.layers;fillLayers();}
      if (phase==='ready') notice('已读取 '+layers.length+' 个粒子层。');
      if (phase==='rendering') notice('游戏正在渲染预览…');
      if (phase==='error') notice('游戏载入失败：'+reply.error);
      if (phase==='closed') notice('预览已断开。');
      if (phase==='done') {
        if (reply.frameCount>0) void showClip(reply);
        else notice('这次没有捕获到预览帧。');
      }
    }
  } catch(error) { notice(error.message); }
  finally {syncButtons();setTimeout(poll,350);}
}

controls.preset.onchange=()=>{
  settings.preset=controls.preset.value;
  settings.edits.clear();
  layers=[];
  revision=0;
  phase='';
  clearClip();
  fillLayers();
  notice('点「读取预制体」载入所选特效。');
  syncButtons();
};
controls.layer.onchange=fillFields;
controls.connect.onclick=()=>submit('inspect');
controls.launch.onclick=()=>{void submit('inspect');};
controls.play.onclick=()=>submit('play');
controls.disconnect.onclick=()=>submit('close');
controls.color.oninput=()=>{const row=currentEdit();if(row)row.color=controls.color.value;};
controls['reset-color'].onclick=()=>{const row=currentEdit();if(row){row.color='';fillFields();}};
controls['reset-layer'].onclick=()=>{const row=currentEdit();if(row){settings.edits.set(row.index,blank(row.index));fillFields();}};

controls.save.onclick=()=>{
  const data={format:'depersonal-prefab-preview',version:1,preset:settings.preset,
    placement:settings.placement,edits:[...settings.edits.values()]};
  const url=URL.createObjectURL(new Blob([JSON.stringify(data,null,2)],{type:'application/json'}));
  const a=document.createElement('a');
  a.href=url;a.download=settings.preset+'.prefab-vfx.json';a.click();
  setTimeout(()=>URL.revokeObjectURL(url),30000);
  notice('已保存副本。');
};
controls.open.onclick=()=>controls.file.click();
function readNumber(value,[,min,max],fallback) {
  return Number.isFinite(value)?Math.max(min,Math.min(max,value)):fallback;
}
controls.file.onchange=async()=>{
  const file=controls.file.files[0]; controls.file.value='';
  if (!file) return;
  try {
    const data=JSON.parse(await file.text());
    if (data?.format!=='depersonal-prefab-preview'||data.version!==1||!list.some(p=>p.id===data.preset)||
        !Array.isArray(data.edits)||data.edits.length>64) throw new Error('请选择由这个页面保存的副本');
    const next=new Map();
    for (const row of data.edits) {
      if (!row||!Number.isInteger(row.index)||row.index<0||row.index>=64) throw new Error('粒子层编号无效');
      const edit=blank(row.index);
      for (const [key,schema] of Object.entries(particleSchema)) edit[key]=readNumber(row[key],schema,1);
      if (typeof row.color==='string'&&/^#[a-f\d]{6}$/i.test(row.color)) edit.color=row.color;
      next.set(edit.index,edit);
    }
    settings.preset=data.preset;
    controls.preset.value=data.preset;
    settings.edits=next;
    for (const [key,schema] of Object.entries(placementSchema)) {
      settings.placement[key]=readNumber(data.placement?.[key],schema,key==='scale'?1:0);
    }
    revision=0;phase='';layers=[];
    clearClip();fillLayers();syncButtons();
    await submit('inspect');
  } catch(error) {notice('打开失败：'+error.message);}
};
poll();
