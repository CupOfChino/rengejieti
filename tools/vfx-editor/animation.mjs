export const presets = {
  still:'原样播放', fade:'渐显', reveal:'逐渐显现', fall:'掉落', spin:'旋转',
  rise:'上浮淡出', pulse:'呼吸', orbit:'绕身'
};

export const anchors = {head:{x:400,y:185}, body:{x:400,y:276}, feet:{x:400,y:369}};
const poseAnchorLabels = {eye:'左眼',sheath:'剑鞘口',gun:'铳剑口'};
export const poseAnchors = Object.keys(poseAnchorLabels);
export const anchorLabels = {head:'头顶',body:'身体',feet:'脚边',...poseAnchorLabels};
export const width = 800;
export const height = 480;
export const maxLayers = 96;
export const unitHeight = anchors.feet.y - anchors.head.y;
export const blankProject = () => ({version:1, name:'新特效', model:null, rightModel:null, layers:[],
  stage:{background:'#17201e',grid:true},
  actors:{left:{action:'idle',at:0,flip:false,rise:0,cues:[]},right:{action:'idle',at:0,flip:true,rise:16,cues:[]}}});
const tau = Math.PI * 2;
const degrees = Math.PI / 180;
export const clamp = (x,min=0,max=1) => Math.min(max,Math.max(min,x));
const lerp = (a,b,t)=>a+(b-a)*t;
const ease = n => n*n*(3-2*n);

export function pose(layer, time, placement = anchors) {
  if (time < layer.start || time > layer.start + layer.duration + 0.000001) return null;
  const progress = clamp((time-layer.start)/layer.duration);
  const origin = placement[layer.anchor];
  if (!origin) return null;
  const p = {x:origin.x+layer.x, y:origin.y-layer.y, size:layer.size, angle:layer.angle, opacity:layer.opacity, reveal:1};
  if(layer.motionTime>0){
    const swept=ease(clamp((time-layer.start)/layer.motionTime));
    p.x+=(layer.travelX??0)*swept;
    p.y-=(layer.travelY??0)*swept;
    p.angle+=(layer.turn??0)*swept;
  }
  switch (layer.motion) {
    case 'fade': p.opacity *= ease(clamp(progress * 2.5)); break;
    case 'reveal': p.reveal = ease(clamp(progress * 1.8)); break;
    case 'fall': p.y += (progress*progress - 0.55)*unitHeight; p.angle += 110*progress; p.opacity *= clamp((1-progress)*4); break;
    case 'spin': p.angle += 360*progress; break;
    case 'rise': p.y -= 0.5*unitHeight*ease(progress); p.opacity *= 1-ease(progress); break;
    case 'pulse': p.size *= lerp(0.84,1.10,0.5-0.5*Math.cos(progress*tau)); p.opacity *= lerp(0.7,1,0.5-0.5*Math.cos(progress*tau)); break;
    case 'orbit': p.x += 0.40*unitHeight*Math.sin(progress*tau); p.y += 0.18*unitHeight*Math.cos(progress*tau); break;
  }
  return p;
}

export function frameAt(asset, time) {
  if (!asset.frames?.length) return null;
  if (asset.frames.length === 1) return asset.frames[0];
  if (asset.times?.length === asset.frames.length) {
    const duration = asset.times.reduce((a,b)=>a+b,0);
    let remaining = asset.loop !== false ? time%duration : Math.min(time,duration);
    for (let index=0;index<asset.times.length;index++) {
      if (remaining < asset.times[index]) return asset.frames[index];
      remaining -= asset.times[index];
    }
    return asset.frames.at(-1);
  }
  const index = Math.floor(time * (asset.fps || 10));
  return asset.frames[asset.loop === false ? Math.min(index,asset.frames.length-1) : index%asset.frames.length];
}

export function visibleSpriteRows(rgba, pixelWidth, pixelHeight) {
  let top = pixelHeight, bottom = 0;
  for (let row = 0; row < pixelHeight; row++) {
    const end = (row + 1) * pixelWidth * 4;
    for (let alpha = row * pixelWidth * 4 + 3; alpha < end; alpha += 4) {
      if (rgba[alpha] < 96) continue;
      top = Math.min(top, row);
      bottom = row + 1;
      break;
    }
  }
  return bottom > top ? {top, bottom} : null;
}

export function modelPlacement(frameWidth, frameHeight, referenceHeight, rows, pivot = .5, frameFeet, idleFeet) {
  const top = rows?.top ?? 0;
  const anchored = frameFeet && idleFeet && idleFeet.y > top;
  const bottom = anchored ? idleFeet.y : (rows?.bottom ?? referenceHeight);
  const scale = unitHeight / (bottom - top);
  const feet = anchors.feet;
  const w = frameWidth * scale, h = frameHeight * scale;
  if (anchored) return {x:feet.x-frameFeet.x*scale, y:feet.y-frameFeet.y*scale, w, h};
  const bottomPadding = (referenceHeight - bottom) * scale;
  return {x:feet.x - w * pivot, y:feet.y - h + bottomPadding, w, h};
}

export function durationOf(project) {
  return Math.max(0.1, ...project.layers.map(layer=>layer.start+layer.duration), project.layers.length ? 0 : 1);
}

export function previewDuration(project, model) {
  const effects = durationOf(project);
  if (project.layers.length || !model?.frames?.length) return effects;
  const idle = model.times?.length === model.frames.length
    ? model.times.reduce((total,hold)=>total+hold,0)
    : model.frames.length/(model.fps || 10);
  return Math.max(effects,idle);
}

export function drawLayer(ctx, layer, time, asset, imageFor, placement = anchors) {
  const p = pose(layer,time,placement);
  if (!p || !asset || p.opacity <= 0 || p.reveal <= 0) return;
  const frame = frameAt(asset, (time-layer.start)*(layer.playbackRate??1));
  const img = frame ? imageFor(frame) : null;
  if (frame && !img) return;
  ctx.save();
  ctx.translate(p.x,p.y);
  ctx.rotate(-p.angle*degrees);
  ctx.scale((layer.flipX?-1:1)*(layer.scaleX??1),(layer.flipY?-1:1)*(layer.scaleY??1));
  ctx.globalAlpha=p.opacity;
  if (img) {
    const imageWidth=p.size*img.width/Math.max(img.width,img.height);
    const imageHeight=p.size*img.height/Math.max(img.width,img.height);
    if(p.reveal < 1){
      ctx.beginPath();ctx.rect(-imageWidth/2,-imageHeight/2,imageWidth*p.reveal,imageHeight);ctx.clip();
    }
    ctx.imageSmoothingEnabled = img.width>128 || img.height>128;
    ctx.drawImage(img,-imageWidth/2,-imageHeight/2,imageWidth,imageHeight);
  } else if (asset.shape === 'glow') {
    const radius = p.size/2;
    if(p.reveal < 1){
      ctx.beginPath();ctx.rect(-radius,-radius,p.size*p.reveal,p.size);ctx.clip();
    }
    const glow = ctx.createRadialGradient(0,0,0,0,0,radius);
    glow.addColorStop(0,'#fff8ff');
    glow.addColorStop(0.18,layer.color);
    glow.addColorStop(0.57,layer.color+'75');
    glow.addColorStop(1,layer.color+'00');
    ctx.fillStyle=glow;ctx.fillRect(-radius,-radius,p.size,p.size);
  }
  ctx.restore();
}
