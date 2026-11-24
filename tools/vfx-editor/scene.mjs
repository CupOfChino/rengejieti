import {anchors, anchorLabels, width, height, drawLayer, frameAt, previewDuration} from './animation.mjs';

export const sides = ['left','right'];
export const sideLabel = {left:'左侧',right:'右侧',center:'画面中央'};
export const actionLabel = {hidden:'离开画面',idle:'待机',battle_idle:'战斗待机',sp_idle:'特殊待机',guard:'架势',
  attack:'攻击',shoot:'射击',cast:'施法',hurt:'受击',gethit:'受击（命中）',dodge:'闪避',death:'倒地',
  walk:'行走',move:'移动',run:'奔跑',prayer:'祈祷',windup:'起手',strike:'出招',aim:'瞄准',
  valenna_draw:'握柄起手',valenna_dash:'近身',valenna_rise:'挑斩',valenna_crosscut:'横斩',
  valenna_cut_crosscut:'切割攻击·首斩',
  valenna_cut_approach:'切割攻击·握柄后近身',
  valenna_rabbit_sweep:'解兔·低身扫斩',valenna_deer_sweep:'解鹿·低位收尾',
  valenna_elephant_sweep:'解象·横扫收势',valenna_finish_thrust:'处置·立身突刺',
  valenna_orbit:'旋斩',valenna_thrust:'突刺',valenna_guard:'预知拦击',valenna_muzzle:'枪刃',
  valenna_point_recoil:'尖端打击·后仰突刺',valenna_cut_recover:'切割攻击·挑斩收势',
  valenna_thrust_recover:'突刺·撤刃回握',valenna_deer_recover:'解鹿·扫斩收势',
  valenna_finish_recover:'处置·收枪',
  valenna_volley:'枪刃连闪',valenna_recover:'收招',valenna_finish_draw:'处置·背身起手',
  valenna_finish_turn:'处置·旋身起斩',valenna_finish_sweep:'处置·连斩',valenna_finish_low:'处置·低位扫斩',
  valenna_finish_volley:'处置·开火',
  valenna_cut_rise:'挑斩',valenna_thrust_brace:'蓄势',valenna_point_pass:'穿身末刺',
  valenna_rabbit_open:'起刀',valenna_rabbit_brace:'回身蓄势',valenna_rabbit_pass:'穿身收刀',
  valenna_deer_open:'起刀',valenna_deer_first_sweep:'首轮横斩',valenna_deer_raise:'提刀',
  valenna_deer_spin:'转刀',valenna_bullet_thrust:'起手突刺',valenna_bullet_double:'连续突刺',
  valenna_bullet_recoil:'后仰回撤',valenna_bullet_shot:'枪刃直闪',valenna_bullet_pass:'穿身长刺',
  valenna_dive_recover:'撤刃回位',valenna_elephant_prepare:'近身架势',valenna_elephant_thrust:'突刺',
  valenna_elephant_volley:'枪刃连闪',valenna_elephant_first_sweep:'回身横斩',valenna_elephant_spin:'转刀',
  valenna_elephant_hold:'末斩收势',valenna_finish_turn_slash:'回身斩',valenna_finish_raise:'提刀',
  valenna_finish_spin:'回旋斩',valenna_finish_brace:'回身举铳',valenna_finish_insert:'平剑刺入',
  valenna_finish_pin:'抵住蓄光',valenna_prescience_dodge:'预知闪避'};
for(const id of [885121,885124,885125,885126,885127,885128])actionLabel['valenna_draw_'+id]='眼、鞘起光';

export const modelFor = (project,side) => side==='right' ? project.rightModel : project.model;
export const actorClip = (model, action) => Object.hasOwn(model?.clips??{},action) ? model.clips[action] : model;
export const actorCues = actor => [actor,...(actor.cues??[])];

export function clipDuration(clip) {
  if(!clip?.frames?.length)return 0;
  return clip.times?.length===clip.frames.length ? clip.times.reduce((sum,hold)=>sum+hold,0) :
    clip.frames.length/(clip.fps||10);
}

export function stageAnchors(project, side) {
  if(side!=='left'&&side!=='right')return anchors;
  const dx=(side==='right' ? 156 : (project.rightModel ? -156 : 0))+(project.actors[side].offsetX??0);
  const rise=project.actors[side].rise;
  return Object.fromEntries(Object.entries(anchors).map(([name,point])=>
    [name,{x:point.x+dx,y:point.y-rise}]));
}

export function sceneDuration(project, assetFor) {
  let length=previewDuration(project,assetFor(project.model));
  for(const side of sides){
    const model=assetFor(modelFor(project,side));
    if(!model)continue;
    const actor=project.actors[side];
    const cues=actorCues(actor).slice().sort((a,b)=>a.at-b.at);
    for(const [index,cue] of cues.entries()){
      if(cue===actor&&cue.action==='idle')continue;
      const resting=cue.action==='idle'||cue.action==='hidden'||(cue!==actor&&cue.action==='battle_idle');
      const duration=resting?0:clipDuration(actorClip(model,cue.action));
      length=Math.max(length,Math.min(cue.at+duration,cues[index+1]?.at??Infinity));
    }
    if(!project.layers.length&&actor.action==='idle')length=Math.max(length,clipDuration(model));
  }
  return length;
}

export function actorFrame(model, actor, time, idleTime) {
  if(!model)return null;
  let action=actor.action, at=action==='idle'?0:actor.at;
  for(const next of actor.cues??[]){
    if(next.at<=time&&(at>time||next.at>=at)){action=next.action;at=next.at;}
  }
  if(action==='hidden')return null;
  const clip=actorClip(model,action);
  const elapsed=time-at;
  if(action==='idle'||clip===model||elapsed<0)return frameAt(model,idleTime);
  if(clip.loop!==false||elapsed<clipDuration(clip)||action==='death')return frameAt(clip,elapsed);
  return frameAt(model,idleTime);
}

export function sampleTimes(project, time, length) {
  const points=[0,time,length];
  for(const side of sides){
    if(!modelFor(project,side))continue;
    for(const cue of actorCues(project.actors[side]))
      if(cue.action!=='idle')points.push(cue.at,cue.at+.14);
  }
  for(const layer of project.layers.slice(0,3))points.push(layer.start+layer.duration*.25);
  for(const t of [.25,.5,.75])if(points.length<8)points.push(length*t);
  const unique=[...new Set(points.map(t=>Math.round(Math.min(length,Math.max(0,t))*100)/100))].sort((a,b)=>a-b);
  if(unique.length<=8)return unique;
  return unique.filter((_,index)=>Array.from({length:8},(_,i)=>Math.round(i*(unique.length-1)/7)).includes(index));
}

export function scenePlacements(project,time,idleTime,{assetFor,layoutActor}) {
  const positions=Object.fromEntries([...sides,'center'].map(side=>[side,stageAnchors(project,side)]));
  const layouts={};
  if(layoutActor)for(const side of sides){
    const model=modelFor(project,side);
    if(!model)continue;
    const layout=layoutActor(assetFor(model),project.actors[side],positions[side].feet,time,idleTime);
    layouts[side]=layout;
    if(layout)Object.assign(positions[side],layout.points);
  }
  return {positions,layouts};
}

let previousCaption='',captionLines=[],captionHalfWidth=0;
function drawCaption(context,text,head){
  const lineHeight=18,limit=248;
  context.save();
  context.font='bold 15px "Microsoft YaHei", sans-serif';
  if(previousCaption!==text){
    previousCaption=text;
    captionLines=[];
    for(const paragraph of text.split(/\r?\n/)){
      let line='';
      for(const char of paragraph){
        if(line&&context.measureText(line+char).width>limit){captionLines.push(line);line='';}
        line+=char;
      }
      captionLines.push(line);
    }
    captionHalfWidth=Math.max(...captionLines.map(line=>context.measureText(line).width))/2;
  }
  const x=Math.max(captionHalfWidth+5,Math.min(width-captionHalfWidth-5,head.x));
  const top=Math.max(5,head.y-12-captionLines.length*lineHeight);
  context.textAlign='center';context.textBaseline='top';context.lineJoin='round';
  context.lineWidth=3;context.strokeStyle='#342719';context.fillStyle='#ffe2a0';
  captionLines.forEach((line,i)=>{
    context.strokeText(line,x,top+i*lineHeight);context.fillText(line,x,top+i*lineHeight);
  });
  context.restore();
}

export function drawScene(context,project,time,idleTime,{assetFor,imageFor,drawActor,layoutActor,guides=false,caption=''}) {
  context.fillStyle=project.stage?.background??'#17201e';
  context.fillRect(0,0,width,height);
  const characters=sides.filter(side=>modelFor(project,side));
  const {positions,layouts}=scenePlacements(project,time,idleTime,{assetFor,layoutActor});
  const guidesFor=characters.length ? characters : ['center'];
  if(project.stage?.grid!==false){
    context.strokeStyle='#22312c';
    context.beginPath();
    for(let x=0;x<width;x+=40){context.moveTo(x+.5,0);context.lineTo(x+.5,height);}
    for(let y=0;y<height;y+=40){context.moveTo(0,y+.5);context.lineTo(width,y+.5);}
    context.stroke();
    context.strokeStyle='#526b60';
    context.beginPath();
    if(!project.rightModel){context.moveTo(0,positions[guidesFor[0]].feet.y+.5);context.lineTo(width,positions[guidesFor[0]].feet.y+.5);}
    else for(const side of characters){
      const {x,y}=positions[side].feet;
      context.moveTo(x-110,y+.5);context.lineTo(x+110,y+.5);
    }
    context.stroke();
  }
  const paintLayer=layer=>drawLayer(context,layer,time,assetFor(layer.asset),imageFor,positions[layer.side??'center']);
  for(const layer of project.layers)if(layer.behind)paintLayer(layer);
  characters.sort((a,b)=>positions[a].feet.y-positions[b].feet.y);
  for(const side of characters)
    drawActor(context,assetFor(modelFor(project,side)),project.actors[side],positions[side].feet,time,idleTime,layouts[side]);
  for(const layer of project.layers)if(!layer.behind)paintLayer(layer);
  if(caption){
    const speaker=sides.find(side=>layouts[side]);
    if(speaker){
      const eye=layouts[speaker].points.eye;
      drawCaption(context,caption,eye?{x:eye.x,y:eye.y-12}:positions[speaker].head);
    }
  }
  if(!guides)return;
  context.fillStyle='#accab9';
  context.font='12px "Microsoft YaHei", sans-serif';
  for(const side of guidesFor)for(const [key,{x,y}] of Object.entries(positions[side])){
    context.fillRect(x-3,y,7,1);context.fillRect(x,y-3,1,7);
    context.fillText(anchorLabels[key],x+9,y-7);
  }
}
