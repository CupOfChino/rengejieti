import {anchors, anchorLabels, poseAnchors, width, height, drawLayer, presets, blankProject, durationOf, clamp,
  visibleSpriteRows, modelPlacement, maxLayers} from './animation.mjs';
import {sides, sideLabel, actionLabel, modelFor, actorClip, actorCues, stageAnchors, actorFrame, sceneDuration, scenePlacements,
  sampleTimes, drawScene} from './scene.mjs';
import {zip} from './zip.mjs';
import {audioTrack, readAudioCues} from './audio.mjs';

const byId = id=>document.getElementById(id);
const scene=byId('scene'), ctx=scene.getContext('2d');
const assets=new Map(), uploads=new Map(), images=new Map();
const glow={id:'glow',label:'粉色光点',shape:'glow',kind:'effect'};
const defaultColor='#ffb8e3';
const fileInput=byId('project-file'), imageInput=byId('image-files');
let project=blankProject(), selected=-1, time=0, modelTime=0, playing=false, lastTime=0, dirty=false;
let initialModel=true, dragging=null;
let newAudioPass=true;
let projectLoads=0;
let loadRequest=0;

const status=text=>{byId('status').textContent=text;};
const sounds=audioTrack({byId,currentTime:()=>time,status,onEdit:(changed=true)=>{
  if(changed)dirty=true;
  renderTimes();
  syncTransport();
},onJump:seekTo});
project.audioCues=[];
sounds.setCues(project.audioCues);
const layerNow=()=>project.layers[selected];
const assetFor=id=>id==='glow'?glow:uploads.get(id)??assets.get(id);
const previewLength=()=>Math.max(sceneDuration(project,assetFor),sounds.duration);
const option=(value,text)=>new Option(text,value);
const modelField={left:'model',right:'rightModel'};
const modelControl={left:byId('model'),right:byId('right-model')};
const selectedCue={left:-1,right:-1};
const editCue=side=>project.actors[side].cues[selectedCue[side]]??project.actors[side];
const safeName=text=>(text.trim().replace(/[<>:"/\\|?*\x00-\x1f]/g,'_').slice(0,64)||'新特效');
const filename = file => file.name.replace(/\.[^.]+$/,'');
const sortFile = new Intl.Collator('zh-CN',{numeric:true});

const fields = {
  x:['横向（px）',-400,400,1], y:['纵向（px）',-300,300,1],
  size:['大小（px）',8,650,1], angle:['角度（°）',-360,360,1],
  scaleX:['横向比例',0.25,2.5,0.05], scaleY:['纵向比例',0.25,2.5,0.05],
  travelX:['扫出横移（px）',-320,320,1], travelY:['扫出纵移（px）',-240,240,1], turn:['扫出转角（°）',-180,180,1],
  start:['开始（秒）',0,8,0.01], duration:['持续（秒）',0.1,8,0.05], playbackRate:['播放倍率',0.25,4,0.05],
  motionTime:['扫动时长（秒）',0,3,0.01], opacity:['不透明度',0,1,0.05]
};
const projectLayerDefaults={x:0,y:0,size:150,angle:0,scaleX:1,scaleY:1,travelX:0,travelY:0,turn:0,
  motionTime:0,start:0,duration:1,playbackRate:1,opacity:1};
const numberFields=new Map();
for (const [key,[label,min,max,step]] of Object.entries(fields)) {
  const row=document.createElement('label'), input=document.createElement('input');
  row.append(label,input);
  Object.assign(input,{type:'number',id:'property-'+key,min,max,step});
  input.addEventListener('input',()=>{
    const layer=layerNow();
    if(!layer || !Number.isFinite(input.valueAsNumber)) return;
    layer[key]=clamp(input.valueAsNumber,min,max);
    dirty=true;
    syncTransport();
  });
  input.addEventListener('change',()=>{if(layerNow())input.value=layerNow()[key];});
  byId(['start','duration','playbackRate','motionTime','opacity'].includes(key)?'advanced':'fields').append(row);
  numberFields.set(key,input);
}
for(const [key,label] of Object.entries(presets)) byId('motion').append(option(key,label));

function imagePromise(url) {
  if (!images.has(url)) {
    const entry={image:null,promise:null};
    entry.promise=new Promise((resolve,reject)=>{
      const src=new Image();
      src.onload=()=>{entry.image=src;resolve(src);};
      src.onerror=()=>reject(new Error('无法读取素材图片'));
      src.src=url;
    });
    entry.promise.catch(()=>{});
    images.set(url,entry);
  }
  return images.get(url).promise;
}

function imageFor(url) {
  if(!images.has(url)) imagePromise(url);
  return images.get(url).image;
}

function updateMaterials() {
  const source=byId('source'), currentSource=source.value;
  const sources=new Set();
  const models=[];
  let defaultModel=null;
  for(const item of assets.values()){
    if(item.kind==='model'){
      models.push(item);
      if(!defaultModel||item.label==='茉莉') defaultModel=item.id;
    }else sources.add(item.source);
  }
  source.replaceChildren(option('','所有来源'),...[...sources].map(name=>option(name,name)));
  if(sources.has(currentSource)) source.value=currentSource;
  if(initialModel && defaultModel){
    project.model=defaultModel;
    initialModel=false;
  }
  for(const side of sides){
    const model=modelControl[side];
    const selectedModel=modelFor(project,side);
    model.replaceChildren(option('','隐藏人物'),...models.map(item=>
      option(item.id,(item.displayLabel??item.label)+' · '+item.source)));
    if(selectedModel&&!assets.has(selectedModel))model.append(option(selectedModel,'缺少工程中的人物素材'));
    model.value=selectedModel??'';
    refreshActor(side);
  }
  fillEffects();
}

function refreshActor(side){
  const model=assetFor(modelFor(project,side));
  const actor=project.actors[side];
  const cue=editCue(side);
  const list=byId(side+'-cues');
  list.replaceChildren(option(-1,'起始动作'),...actor.cues.map((entry,i)=>
    option(i,entry.at.toFixed(2)+' 秒 · '+(actionLabel[entry.action]??entry.action))));
  list.value=String(selectedCue[side]);
  list.disabled=!model;
  byId(side+'-add-cue').disabled=!model||actor.cues.length>=32;
  byId(side+'-remove-cue').disabled=!model||selectedCue[side]<0;
  const action=byId(side+'-action');
  const keys=Object.keys(model?.clips??{}).filter(key=>key!=='idle');
  action.replaceChildren(option('idle','待机'),option('hidden','离开画面'),...keys.map(key=>option(key,actionLabel[key]??key)));
  if(!['idle','hidden'].includes(cue.action)&&!keys.includes(cue.action))action.append(option(cue.action,'动作素材缺失'));
  action.value=cue.action;
  for(const id of ['action','at','rise','flip'])byId(side+'-'+id).disabled=!model;
  byId(side+'-at').disabled=!model||(selectedCue[side]<0&&cue.action==='idle');
  byId(side+'-at').value=cue.at;
  byId(side+'-rise').value=actor.rise;
  byId(side+'-flip').checked=actor.flip;
  renderTimes();
}

function renderTimes(){
  const actions=sides.filter(side=>modelFor(project,side)).flatMap(side=>
    actorCues(project.actors[side]).filter(cue=>cue.action!=='hidden'&&cue.action!=='idle')
      .map(cue=>({at:cue.at,label:actionLabel[cue.action]??cue.action})));
  const audio=(project.audioCues??[]).map(cue=>({
    at:cue.at,end:cue.at+sounds.durationFor(cue),label:sounds.labelFor(cue.sound)
  }));
  byId('skill-timing').hidden=!actions.length&&!audio.length;
  byId('skill-duration').textContent='动作分镜 '+sceneDuration(project,assetFor).toFixed(2)+' 秒';
  for(const [id,events,empty] of [['action-times',actions,'暂无动作'],['sound-times',audio,'暂无已编排音效']]){
    const buttons=events.sort((a,b)=>a.at-b.at).map(event=>{
      const button=document.createElement('button');
      button.type='button';
      const end=Number.isFinite(event.end)?'–'+event.end.toFixed(2):'';
      button.textContent=event.at.toFixed(2)+end+' 秒 · '+event.label;
      button.onclick=()=>seekTo(event.at);
      return button;
    });
    byId(id).replaceChildren(...(buttons.length?buttons:[empty]));
  }
}

function fillEffects(){
  const select=byId('asset'), prior=select.value;
  const query=byId('search').value.trim().toLocaleLowerCase('zh-CN'), source=byId('source').value;
  const matches=[...assets.values()].filter(a=>a.kind==='effect'&&(!source||source===a.source)&&
    (!query||(a.label+' '+a.source).toLocaleLowerCase('zh-CN').includes(query)));
  select.replaceChildren(option('',matches.length?'选择素材':'没有匹配的特效'),
    ...matches.map(a=>option(a.id,a.label+' · '+a.source)));
  if(matches.some(a=>a.id===prior))select.value=prior;
  byId('add').disabled=!select.value;
}

async function readCatalog(){
  try{
    const response=await fetch('/catalog',{cache:'no-store'});
    if(!response.ok)throw new Error('无法连接本地素材库');
    const data=await response.json();
    if(data.assets.length||!data.loading){
      assets.clear();
      for(const asset of data.assets)assets.set(asset.id,asset);
      updateMaterials();
      refresh();
    }
    byId('notes').replaceChildren(...data.notes.map(note=>{
      const li=document.createElement('li');li.textContent=note;return li;
    }));
    const nModels=data.assets.filter(a=>a.kind==='model').length;
    const nEffects=data.assets.length-nModels;
    const counts=nModels+' 个人物 · '+nEffects+' 个特效素材';
    if(data.loading){
      status(nEffects?'已读取模组素材，正在准备原版帧。'+counts:'正在读取本机游戏和模组素材…');
      setTimeout(readCatalog,1800);
    }else{status(counts);}
  }catch(error){status(error.message);}
}

function syncTransport(resetAudio=false) {
  const total=previewLength();
  time=Math.min(time,total);
  byId('seek').max=total;
  byId('seek').value=time;
  byId('clock').value=time.toFixed(2)+' / '+total.toFixed(2)+' 秒';
  byId('play').textContent=playing?'暂停':time>=total?'重播':'播放';
  sounds.update(time,playing,resetAudio);
}
function seekTo(next){
  sounds.stopAudition();
  newAudioPass=next===0;
  time=next;modelTime=time;playing=false;syncTransport();
}
function refresh(){
  byId('stage-grid').checked=project.stage.grid;
  byId('stage-background').value=project.stage.background;
  const active=layerNow();
  byId('inspector').hidden=!active;
  byId('color-row').hidden=active?.asset!=='glow';
  byId('empty').hidden=project.layers.length!==0;
  for(const id of ['duplicate','remove'])byId(id).disabled=!active;
  byId('earlier').disabled=!active||selected===0;
  byId('later').disabled=!active||selected===project.layers.length-1;
  byId('export').disabled=!project.layers.length;
  byId('layers').replaceChildren(...project.layers.map((layer,index)=>{
    const button=document.createElement('button');
    button.type='button';
    button.textContent=(index+1)+'. '+layer.name+(layer.side&&layer.side!=='center'?' · '+sideLabel[layer.side]:'')+
      (assetFor(layer.asset)?'':'（素材缺失）');
    button.setAttribute('aria-pressed',String(selected===index));
    button.onclick=()=>{selected=index;refresh();};
    return button;
  }));
  if(active){
    byId('layer-name').value=active.name;
    byId('layer-side').value=active.side??'center';
    byId('anchor').value=active.anchor;
    byId('motion').value=active.motion;
    byId('behind').checked=active.behind;
    byId('layer-flip-x').checked=active.flipX;
    byId('layer-flip-y').checked=active.flipY;
    byId('color').value=active.color;
    for(const [key,input] of numberFields)input.value=active[key];
  }
  syncTransport();
}
function add(asset){
  if(!asset||project.layers.length>=maxLayers){status('一个工程最多可放 '+maxLayers+' 个图层。');return;}
  const length=asset.times?.reduce((a,b)=>a+b,0)??(asset.frames?.length/(asset.fps||10));
  project.layers.push({...projectLayerDefaults,asset:asset.id,name:asset.label.slice(0,72),side:'center',anchor:'body',
    motion:'still',flipX:false,flipY:false,duration:length?clamp(length,0.35,8):1,
    opacity:1,behind:false,color:defaultColor});
  selected=project.layers.length-1;
  dirty=true;
  refresh();
  status('已添加 '+asset.label);
}

byId('search').addEventListener('input',fillEffects);
byId('source').addEventListener('change',fillEffects);
byId('asset').addEventListener('change',()=>{byId('add').disabled=!byId('asset').value;});
byId('add').onclick=()=>add(assetFor(byId('asset').value));
byId('make').onclick=()=>add(glow);
for(const side of sides){
  modelControl[side].onchange=()=>{
    project[modelField[side]]=modelControl[side].value||null;
    const actor=project.actors[side];
    actor.action='idle';
    actor.at=0;
    actor.cues=[];
    selectedCue[side]=-1;
    modelTime=0;initialModel=false;dirty=true;refreshActor(side);syncTransport();
  };
  byId(side+'-action').onchange=e=>{
    const cue=editCue(side);
    cue.action=e.target.value;
    seekTo(selectedCue[side]<0?0:cue.at);dirty=true;refreshActor(side);
  };
  byId(side+'-cues').onchange=e=>{selectedCue[side]=Number(e.target.value);refreshActor(side);};
  byId(side+'-add-cue').onclick=()=>{
    const cues=project.actors[side].cues;
    if(cues.length>=32)return;
    cues.push({action:editCue(side).action,at:clamp(Math.round(time*100)/100,0,8)});
    selectedCue[side]=cues.length-1;dirty=true;refreshActor(side);syncTransport();
  };
  byId(side+'-remove-cue').onclick=()=>{
    if(selectedCue[side]<0)return;
    project.actors[side].cues.splice(selectedCue[side],1);
    selectedCue[side]=-1;dirty=true;refreshActor(side);syncTransport();
  };
  byId(side+'-flip').onchange=e=>{project.actors[side].flip=e.target.checked;dirty=true;};
  for(const [key,min,max] of [['at',0,8],['rise',-70,70]])byId(side+'-'+key).onchange=e=>{
    const value=e.target.valueAsNumber;
    const entry=key==='at'?editCue(side):project.actors[side];
    if(Number.isFinite(value)){entry[key]=clamp(value,min,max);dirty=true;}
    refreshActor(side);syncTransport();
  };
}
byId('stage-grid').onchange=e=>{project.stage.grid=e.target.checked;dirty=true;};
byId('stage-background').oninput=e=>{project.stage.background=e.target.value;dirty=true;};
byId('project-name').oninput=e=>{project.name=e.target.value;dirty=true;};
byId('layer-name').onchange=e=>{if(layerNow()){layerNow().name=e.target.value.trim()||'特效';dirty=true;refresh();}};
byId('color').oninput=e=>{if(layerNow()){layerNow().color=e.target.value;dirty=true;}};
byId('layer-side').onchange=e=>{
  if(layerNow()){layerNow().side=e.target.value;dirty=true;refresh();}
};
byId('layer-flip-x').onchange=e=>{if(layerNow()){layerNow().flipX=e.target.checked;dirty=true;}};
byId('layer-flip-y').onchange=e=>{if(layerNow()){layerNow().flipY=e.target.checked;dirty=true;}};
for(const key of ['anchor','motion','behind']){
  byId(key).onchange=e=>{
    if(!layerNow())return;
    layerNow()[key]=key==='behind'?e.target.checked:e.target.value;
    if(key==='motion') seekTo(layerNow().start+layerNow().duration*0.28);
    dirty=true;
    syncTransport();
  };
}
byId('remove').onclick=()=>{
  if(!layerNow())return;
  project.layers.splice(selected,1);
  selected=Math.min(selected,project.layers.length-1);
  dirty=true;
  refresh();
};
byId('duplicate').onclick=()=>{
  if(!layerNow()||project.layers.length>=maxLayers)return;
  const layer={...layerNow(),name:(layerNow().name.slice(0,68)+' 副本')};
  project.layers.splice(++selected,0,layer);dirty=true;refresh();
};
function moveLayer(delta){
  const next=selected+delta;
  if(next<0||next>=project.layers.length)return;
  [project.layers[selected],project.layers[next]]=[project.layers[next],project.layers[selected]];
  selected=next;dirty=true;refresh();
}
byId('earlier').onclick=()=>moveLayer(-1);
byId('later').onclick=()=>moveLayer(1);
byId('play').onclick=()=>{
  if(!playing&&time>=previewLength()){time=0;modelTime=0;newAudioPass=true;}
  playing=!playing;
  syncTransport(playing&&newAudioPass);
  if(playing)newAudioPass=false;
};
byId('seek').oninput=e=>seekTo(e.target.valueAsNumber);
byId('new-project').onclick=()=>{
  if(dirty&&!confirm('当前改动还没保存，要新建工程吗？'))return;
  ++loadRequest;
  previewSkill.value=''; showSkill.disabled=true; sounds.selectSkill('');
  const {model,rightModel}=project;
  project=blankProject();project.model=model;project.rightModel=rightModel;
  newAudioPass=true;
  project.audioCues=[];sounds.setCues(project.audioCues);
  uploads.clear();selected=-1;time=0;modelTime=0;playing=false;dirty=false;
  byId('project-name').value=project.name;
  updateMaterials();refresh();
};
byId('open-project').onclick=()=>{
  if(!dirty||confirm('打开工程会替换当前画布，继续吗？'))fileInput.click();
};

function download(blob,name){
  const url=URL.createObjectURL(blob), link=document.createElement('a');
  link.href=url;link.download=name;link.click();
  setTimeout(()=>URL.revokeObjectURL(url),30000);
}
function projectData(source=project){
  const used=[...new Set(source.layers.map(layer=>layer.asset))];
  const imported=used.map(id=>uploads.get(id)).filter(Boolean);
  return {...source,imported};
}
byId('save-project').onclick=()=>{
  download(new Blob([JSON.stringify(projectData(),null,2)],{type:'application/json'}),safeName(project.name)+'.vfx.json');
  dirty=false;
  status('已保存工程，可用「打开工程」继续编辑。');
};

const isDataImage=text=>typeof text==='string'&&/^data:image\/(?:png|webp|jpeg);base64,[a-z0-9+/=]+$/i.test(text);
const isColor=text=>typeof text==='string'&&/^#[a-f0-9]{6}$/i.test(text);
const isAction=text=>typeof text==='string'&&/^[a-z0-9_-]{1,40}$/i.test(text);
function validate(data){
  if(data?.version!==1||!Array.isArray(data.layers)||data.layers.length>maxLayers)throw new Error('这不是此工具的工程文件');
  const imported=new Map();
  for(const item of data.imported??[]){
    if(!item||typeof item.id!=='string'||!item.id.startsWith('import-')||typeof item.label!=='string'||
      !Array.isArray(item.frames)||!item.frames.length||item.frames.length>96||!item.frames.every(isDataImage)){
      throw new Error('工程内的导入素材不完整');
    }
    imported.set(item.id,{...item,fps:10,kind:'effect'});
  }
  const safeLayers=data.layers.map(layer=>{
    if(!layer||typeof layer.asset!=='string')throw new Error('图层内容不完整');
    const result={asset:layer.asset,name:String(layer.name||'特效').slice(0,72),
      side:Object.hasOwn(sideLabel,layer.side)?layer.side:'center',
      anchor:Object.hasOwn(anchorLabels,layer.anchor)?layer.anchor:'body',
      motion:Object.hasOwn(presets,layer.motion)?layer.motion:'still',behind:!!layer.behind,
      flipX:layer.flipX===true,flipY:layer.flipY===true,
      color:isColor(layer.color)?layer.color:defaultColor};
    for(const [key,[,min,max]] of Object.entries(fields)){
      result[key]=clamp(Number.isFinite(layer[key])?layer[key]:projectLayerDefaults[key],min,max);
    }
    return result;
  });
  const next={...blankProject(),name:String(data.name||'新特效').slice(0,72),layers:safeLayers};
  next.audioCues=readAudioCues(data.audioCues);
  if(isColor(data.stage?.background))next.stage.background=data.stage.background;
  if(typeof data.stage?.grid==='boolean')next.stage.grid=data.stage.grid;
  for(const side of sides){
    const field=modelField[side];
    next[field]=typeof data[field]==='string'?data[field]:null;
    const motion=data.actors?.[side];
    const fallback=next.actors[side];
    if(motion&&typeof motion==='object')next.actors[side]={
      action:isAction(motion.action)?motion.action:fallback.action,
      at:Number.isFinite(motion.at)?clamp(motion.at,0,8):fallback.at,
      rise:Number.isFinite(motion.rise)?clamp(motion.rise,-70,70):fallback.rise,
      offsetX:Number.isFinite(motion.offsetX)?clamp(motion.offsetX,-400,400):0,
      flip:typeof motion.flip==='boolean'?motion.flip:fallback.flip,
      cues:Array.isArray(motion.cues)?motion.cues.slice(0,32)
        .filter(cue=>isAction(cue?.action)&&Number.isFinite(cue.at))
        .map(cue=>({action:cue.action,at:clamp(cue.at,0,8)})):[]
    };
  }
  return {next,imported};
}
function openProject(data){
  const {next,imported}=validate(data);
  project=next;uploads.clear();
  newAudioPass=true;
  playing=false;time=0;modelTime=0;
  sounds.setCues(project.audioCues);
  for(const [id,asset] of imported)uploads.set(id,asset);
  for(const side of sides)selectedCue[side]=-1;
  selected=project.layers.length?0:-1;initialModel=false;dirty=false;
  byId('project-name').value=project.name;
  updateMaterials();refresh();
}

const previewSkill=byId('preview-skill'),showSkill=byId('show-skill');
for(const entry of byId('sound-skill').options){
  if(entry.value)previewSkill.add(option(entry.value,entry.textContent));
}
previewSkill.add(option('885130','预知'));
previewSkill.onchange=()=>{++loadRequest;showSkill.disabled=!previewSkill.value;};
showSkill.onclick=async()=>{
  const id=previewSkill.value;
  if(!id||(dirty&&!confirm('载入技能分镜将替换当前未保存的改动，继续吗？')))return;
  const serial=++loadRequest;
  showSkill.disabled=true;
  const name=previewSkill.selectedOptions[0].textContent;
  status('正在读取「'+name+'」的整招分镜…');
  try{
    const [scene,track]=await Promise.all([
      fetch('/skill-preview/'+id),id==='885130'?null:fetch('/skill-audio/'+id,{cache:'no-store'})
    ]);
    if(!scene.ok)throw new Error('找不到该技能的本地分镜工程');
    if(track&&!track.ok&&track.status!==404)throw new Error('读取技能音效配置失败');
    const data=await scene.json();
    if(track?.ok)data.audioCues=(await track.json()).cues;
    if(serial!==loadRequest)return;
    openProject(data);
    sounds.selectSkill(id==='885130'?'':id);
    status('已打开「'+name+'」。点击播放，或点击时间跳到对应动作与音效。');
  }catch(error){if(serial===loadRequest)status('查看失败：'+error.message);}
  finally{if(serial===loadRequest)showSkill.disabled=false;}
};

fileInput.onchange=async ()=>{
  const file=fileInput.files[0];fileInput.value='';
  if(!file)return;
  const serial=++loadRequest;
  try{
    if(file.size>80*1024*1024)throw new Error('工程过大');
    const data=JSON.parse(await file.text());
    if(serial!==loadRequest)return;
    openProject(data);
    previewSkill.value='';showSkill.disabled=true;sounds.selectSkill('');
    status('工程已打开。');
  }catch(error){status('打开失败：'+error.message);}
  finally{projectLoads++;}
};

byId('import-images').onclick=()=>imageInput.click();
const toDataUrl=file=>new Promise((resolve,reject)=>{
  const read=new FileReader();
  read.onload=()=>resolve(read.result);read.onerror=()=>reject(new Error('无法读取 '+file.name));
  read.readAsDataURL(file);
});
imageInput.onchange=async ()=>{
  const files=[...imageInput.files].sort((a,b)=>sortFile.compare(a.name,b.name));
  imageInput.value='';
  if(!files.length)return;
  try{
    if(project.layers.length>=maxLayers)throw new Error('一个工程最多可放 '+maxLayers+' 个图层');
    if(files.length>96||files.reduce((total,f)=>total+f.size,0)>48*1024*1024)throw new Error('一次最多导入 96 张图，合计 48 MB');
    const frames=await Promise.all(files.map(toDataUrl));
    if(!frames.every(isDataImage))throw new Error('请选择 PNG、WebP 或 JPEG 图片');
    const usedUploads=[...new Set(project.layers.map(layer=>layer.asset))].map(id=>uploads.get(id)).filter(Boolean);
    const size=[...usedUploads.flatMap(asset=>asset.frames),...frames].reduce((total,url)=>total+url.length,0);
    if(size>70*1024*1024)throw new Error('此工程的自带图片已达到存储上限');
    await Promise.all(frames.map(imagePromise));
    const item={id:'import-'+crypto.randomUUID(),centered:true,kind:'effect',label:filename(files[0]),frames,fps:10};
    uploads.set(item.id,item);add(item);
  }catch(error){status('导入失败：'+error.message);}
};

const spriteMetrics = new WeakMap();
const measurementCanvas = document.createElement('canvas');
const measurement = measurementCanvas.getContext('2d',{willReadFrequently:true});

function referenceRows(image){
  if(spriteMetrics.has(image))return spriteMetrics.get(image);
  measurementCanvas.width=image.width;
  measurementCanvas.height=image.height;
  let rows=null;
  try {
    measurement.drawImage(image,0,0);
    const pixels=measurement.getImageData(0,0,image.width,image.height);
    rows=visibleSpriteRows(pixels.data,image.width,image.height);
  } catch { /* Keep the native canvas proportions when the pixels are unreadable. */ }
  spriteMetrics.set(image,rows);
  return rows;
}

function layoutActor(model,actor,feet,time,idleTime){
  if(!model)return null;
  const frame=actorFrame(model,actor,time,idleTime);
  const img=frame&&imageFor(frame);
  const idle=model.frames?.[0];
  const reference=idle&&imageFor(idle);
  if(!img||!reference)return null;
  const box=modelPlacement(img.width,img.height,reference.height,referenceRows(reference),
    model.pivot ?? .5,model.feet?.[frame],model.feet?.[idle]);
  const x=box.x-anchors.feet.x,y=box.y-anchors.feet.y;
  const flip=actor.flip?-1:1;
  const points={};
  for(const key of poseAnchors){
    const point=model.feet?.[frame]?.[key];
    if(point)points[key]={
      x:feet.x+(x+point.x*box.w/img.width)*flip,
      y:feet.y+y+point.y*box.h/img.height
    };
  }
  return {img,x,y,w:box.w,h:box.h,flip,points};
}

function drawActor(context,model,actor,feet,time,idleTime,layout=layoutActor(model,actor,feet,time,idleTime)){
  if(!layout)return;
  context.save();
  context.translate(feet.x,feet.y);
  if(layout.flip<0)context.scale(-1,1);
  context.imageSmoothingEnabled=false;
  context.drawImage(layout.img,layout.x,layout.y,layout.w,layout.h);
  context.restore();
}
function tick(now){
  const delta=lastTime?Math.min(0.08,(now-lastTime)/1000):0;
  lastTime=now;
  if(playing){
    const total=previewLength();
    const loop=byId('loop').checked;
    const step=loop?delta:Math.min(delta,Math.max(0,total-time));
    time+=step;
    modelTime+=step;
    const crossedEnd=time>=total;
    if(crossedEnd){if(loop)time%=total;else{time=total;playing=false;}}
    syncTransport(crossedEnd&&loop);
  }
  drawScene(ctx,project,time,modelTime,{assetFor,imageFor,drawActor,layoutActor,
    guides:byId('guides').checked,caption:sounds.captionAt(time)});
  requestAnimationFrame(tick);
}

scene.onpointerdown=e=>{
  const layer=layerNow();if(!layer||e.button!==0)return;
  scene.setPointerCapture(e.pointerId);sounds.stopAudition();playing=false;syncTransport();
  dragging={layer,x:e.clientX,y:e.clientY,fromX:layer.x,fromY:layer.y,id:e.pointerId};
  scene.dataset.drag='true';
};
scene.onpointermove=e=>{
  if(!dragging||dragging.id!==e.pointerId)return;
  const bounds=scene.getBoundingClientRect(), layer=dragging.layer;
  layer.x=clamp(Math.round(dragging.fromX+(e.clientX-dragging.x)*width/bounds.width),-400,400);
  layer.y=clamp(Math.round(dragging.fromY-(e.clientY-dragging.y)*height/bounds.height),-300,300);
  numberFields.get('x').value=layer.x;numberFields.get('y').value=layer.y;dirty=true;
};
scene.onlostpointercapture=()=>{dragging=null;delete scene.dataset.drag;};

const png=async canvas=>{
  const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));
  if(!blob)throw new Error('生成 PNG 失败');
  return new Uint8Array(await blob.arrayBuffer());
};
async function prepareReview(snapshot, lookup){
  const material=new Map(snapshot.layers.map(layer=>[layer.asset,lookup(layer.asset)]));
  const roles=[];
  const urls=new Set();
  for(const side of sides){
    const id=modelFor(snapshot,side);
    if(!id)continue;
    const model=lookup(id), actor=snapshot.actors[side];
    if(!model)throw new Error(sideLabel[side]+'角色素材缺失');
    material.set(id,model);
    for(const cue of actorCues(actor)){
      const clip=actorClip(model,cue.action);
      if(cue.action!=='idle'&&clip===model)throw new Error(sideLabel[side]+'动作素材缺失：'+cue.action);
      for(const url of clip.frames??[])urls.add(url);
    }
    roles.push({side,name:model.displayLabel??model.label,action:actor.action,at:actor.at,cues:actor.cues});
  }
  if(!roles.length&&!snapshot.layers.length)throw new Error('请先选择角色或添加特效');
  for(const asset of material.values()){
    if(!asset)throw new Error('工程里有缺失的图层素材');
    for(const url of asset.frames??[])urls.add(url);
  }
  await Promise.all([...urls].map(imagePromise));
  const settings={assetFor:id=>material.get(id),imageFor,drawActor,layoutActor};
  return {snapshot,settings,roles,length:sceneDuration(snapshot,settings.assetFor)};
}

function compareReview(candidate, reference, referenceImage, title, times, guides){
  const gap=12, card=times.length>5?320:480, picture=card*height/width;
  const row=Math.ceil(picture)+24;
  const sheet=document.createElement('canvas');
  sheet.width=2*card+3*gap;
  sheet.height=46+times.length*row;
  const matrix=sheet.getContext('2d');
  matrix.fillStyle='#111815';matrix.fillRect(0,0,sheet.width,sheet.height);
  matrix.fillStyle='#e4eee6';matrix.font='bold 17px "Microsoft YaHei", sans-serif';
  matrix.fillText('参考 · '+title,gap,28,card);
  matrix.fillText('当前 · '+candidate.snapshot.name,2*gap+card,28,card);
  const frame=document.createElement('canvas');
  frame.width=width;frame.height=height;
  const ctx=frame.getContext('2d');
  for(const [i,t] of times.entries()){
    const y=40+i*row;
    if(reference){
      drawScene(ctx,reference.snapshot,t,t,{...reference.settings,guides});
      matrix.drawImage(frame,gap,y,card,picture);
    }else{
      matrix.fillStyle='#17201e';matrix.fillRect(gap,y,card,picture);
      const scale=Math.min(card/referenceImage.width,picture/referenceImage.height);
      const w=referenceImage.width*scale,h=referenceImage.height*scale;
      matrix.drawImage(referenceImage,gap+(card-w)/2,y+(picture-h)/2,w,h);
    }
    drawScene(ctx,candidate.snapshot,t,t,{...candidate.settings,guides});
    matrix.drawImage(frame,2*gap+card,y,card,picture);
    matrix.fillStyle='#bdd5c4';matrix.font='14px "Microsoft YaHei", sans-serif';
    matrix.fillText(t.toFixed(3).replace(/0+$/,'').replace(/\.$/,'')+' 秒',gap+3,y+picture+17);
  }
  return sheet.toDataURL('image/png').split(',')[1];
}

async function saveReview(options={}){
  const button=byId('review');
  button.disabled=true;
  delete button.dataset.result;
  try{
    const snapshot=structuredClone(project);
    const data=projectData(snapshot), clicked=time, clickedIdle=modelTime;
    const candidate=await prepareReview(snapshot,assetFor);
    const reference=options.reference ? await prepareReview(options.reference.snapshot,options.reference.lookup) : null;
    const referenceImage=options.referenceImage;
    if(referenceImage&&!isDataImage(referenceImage.data))throw new Error('参考图需要 PNG、WebP 或 JPEG');
    const image=referenceImage ? await imagePromise(referenceImage.data) : null;
    const length=Math.max(candidate.length,reference?.length??0);
    const current=Math.min(length,options.current??clicked);
    const currentIdle=options.current===undefined?clickedIdle:current;
    const times=options.times??sampleTimes(snapshot,current,length);
    if(times.some(t=>t>length+.001))throw new Error('检查时间超出工程时长 '+length.toFixed(2)+' 秒');
    const settings={...candidate.settings,guides:!!options.guides};

    const currentCanvas=document.createElement('canvas');
    currentCanvas.width=width;currentCanvas.height=height;
    const render=currentCanvas.getContext('2d');
    drawScene(render,snapshot,current,currentIdle,settings);
    const capture=currentCanvas.toDataURL('image/png').split(',')[1];

    const sheet=document.createElement('canvas');
    const cardWidth=480, cardHeight=316, gap=12;
    sheet.width=2*cardWidth+3*gap;
    sheet.height=54+Math.ceil(times.length/2)*(cardHeight+gap);
    const matrix=sheet.getContext('2d');
    matrix.fillStyle='#111815';matrix.fillRect(0,0,sheet.width,sheet.height);
    matrix.fillStyle='#e4eee6';matrix.font='bold 20px "Microsoft YaHei", sans-serif';
    matrix.fillText(snapshot.name,gap,30,sheet.width-2*gap);
    for(const [index,t] of times.entries()){
      drawScene(render,snapshot,t,t,settings);
      const x=gap+(index%2)*(cardWidth+gap), y=48+Math.floor(index/2)*(cardHeight+gap);
      matrix.drawImage(currentCanvas,x,y,cardWidth,cardWidth*height/width);
      matrix.fillStyle='#bdd5c4';matrix.font='14px "Microsoft YaHei", sans-serif';
      matrix.fillText(t.toFixed(2)+' 秒',x+3,y+309);
    }
    const request={project:data,current,length,times,
      roles:candidate.roles,capture,timeline:sheet.toDataURL('image/png').split(',')[1]};
    if(reference||image){
      const title=reference?.snapshot.name??referenceImage.name;
      request.reference={type:reference?'project':'image',name:title,
        ...(reference ? {length:reference.length} : {})};
      request.comparison=compareReview(candidate,reference,image,title,times,settings.guides);
    }
    const response=await fetch('/review',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request)});
    const result=await response.json();
    if(!response.ok)throw new Error(result.error??'本机检查图保存失败');
    button.dataset.result=result.id;
    const link=document.createElement('a');
    link.href=result.comparisonUrl??result.url;link.target='_blank';link.rel='noopener';
    link.textContent=result.comparisonUrl?'打开对照图':'打开分镜';
    byId('status').replaceChildren(document.createTextNode('已保存检查图。'),link);
  }catch(error){status('检查图保存失败：'+error.message);}
  finally{button.disabled=false;}
}
byId('review').onclick=()=>saveReview();

if(new URLSearchParams(location.search).has('agent-review')){
  let reference;
  window.vfxAgentReview={
    get loaded(){return projectLoads;},
    rememberReference(){
      const imported=new Map(uploads);
      reference={snapshot:structuredClone(project),lookup:id=>id==='glow'?glow:imported.get(id)??assets.get(id)};
    },
    save(options){void saveReview({...options,reference});},
  };
}

byId('export').onclick=async ()=>{
  const button=byId('export');
  if(!project.layers.length)return;
  button.disabled=true;
  try{
    const snapshot=structuredClone(project);
    const material=new Map(snapshot.layers.map(layer=>[layer.asset,assetFor(layer.asset)]));
    const followPose=snapshot.layers.some(layer=>poseAnchors.includes(layer.anchor));
    const review=followPose?await prepareReview(snapshot,assetFor):null;
    if(!review)for(const [id,asset] of material){
      if(!asset)throw new Error('缺少图层素材 '+id);
      await Promise.all((asset.frames??[]).map(imagePromise));
    }
    const canvas=document.createElement('canvas');
    canvas.width=width;canvas.height=height;
    const output=canvas.getContext('2d');
    const fps=24, duration=durationOf(snapshot), count=Math.ceil(duration*fps);
    const positions=Object.fromEntries([...sides,'center'].map(side=>[side,stageAnchors(snapshot,side)]));
    const info={name:snapshot.name,fps,frameCount:count,width,height,pivot:anchors.body,anchors:positions,files:[]};
    const entries=[];
    for(let i=0;i<count;i++){
      const t=i/fps;
      const placement=review?scenePlacements(snapshot,t,t,review.settings).positions:positions;
      output.clearRect(0,0,width,height);
      for(const layer of [...snapshot.layers.filter(layer=>layer.behind),...snapshot.layers.filter(layer=>!layer.behind)]){
        drawLayer(output,layer,t,material.get(layer.asset),imageFor,placement[layer.side??'center']);
      }
      const file='frames/frame-'+String(i+1).padStart(3,'0')+'.png';
      entries.push([file,await png(canvas)]);info.files.push(file);
      if(i%10===0)status('正在导出 '+(i+1)+' / '+count+' 帧…');
    }
    entries.push(['manifest.json',JSON.stringify(info,null,2)],
      ['说明.txt','PNG 为透明背景，按帧号排列。manifest.json 记录帧率、画布大小和身体挂点。可在游戏编辑器中按顺序作为帧素材导入，具体挂载点在模组内设置。\r\n']);
    download(zip(entries),safeName(snapshot.name)+'-透明帧.zip');
    status('已导出 '+count+' 张透明 PNG，24 帧/秒。');
  } catch(error){status('导出失败：'+error.message);}
  finally{button.disabled=!project.layers.length;}
};

for(const side of sides)refreshActor(side);
refresh();
readCatalog();
const linkedSkill=new URLSearchParams(location.search).get('skill');
if(linkedSkill&&[...previewSkill.options].some(entry=>entry.value===linkedSkill)){
  previewSkill.value=linkedSkill;showSkill.disabled=false;showSkill.click();
}
requestAnimationFrame(tick);
