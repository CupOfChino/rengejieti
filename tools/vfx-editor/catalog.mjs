import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { poseAnchors } from './animation.mjs';

const imagePattern = /\.(png|webp|jpe?g|gif)$/i;
const actionPattern = /^(.*?)[_-](idle|stand|battle_idle|walk|move|attack|hurt|death)[_-](\d.*)$/i;
const cmp = new Intl.Collator('zh-CN', {numeric:true});
const base = file => path.basename(file, path.extname(file));
const digest = text => createHash('sha256').update(text).digest('hex').slice(0, 20);
const escapeRegex = value => value.replace(/[.*+?^$(){}|[\]\\]/g, '\\$&');
const list = async dir => fs.readdir(dir, {withFileTypes:true}).catch(() => []);
const isDir = async p => (await fs.stat(p).catch(() => null))?.isDirectory() ?? false;
const readJson = async file => JSON.parse((await fs.readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const fullStem = input => base(input.replaceAll('\\','/'));

async function imagesIn(dir, depth = 0) {
  const files = [];
  for (const entry of await list(dir)) {
    const file = path.join(dir, entry.name);
    if (entry.isFile() && imagePattern.test(entry.name)) files.push(file);
    if (entry.isDirectory() && depth > 0) files.push(...await imagesIn(file, depth - 1));
  }
  return files.sort(cmp.compare);
}

async function definitions(dir) {
  const configs = [];
  for (const item of await list(dir)) {
    if (!item.isFile() || !item.name.endsWith('.txt')) continue;
    try {
      const value = (await readJson(path.join(dir,item.name))).Data?.value;
      if (Array.isArray(value?.AnimationList)) configs.push(value);
    } catch {
      // 目录可能同时存放运行时无法直接解析的资源。
    }
  }
  return configs;
}

export async function locate(repo) {
  const steamLocations = [...new Set([
    process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'],'Steam'),
    process.env.ProgramFiles && path.join(process.env.ProgramFiles,'Steam'),
    'E:/SteamLibrary', 'D:/SteamLibrary', 'C:/SteamLibrary'
  ].filter(Boolean))];
  for (const folder of [...steamLocations]) {
    const vdf = await fs.readFile(path.join(folder,'steamapps','libraryfolders.vdf'),'utf8').catch(()=>'');
    for (const match of vdf.matchAll(/"path"\s+"([^"]+)"/gi)) steamLocations.push(match[1].replaceAll('\\\\','\\'));
  }
  const gameCandidates = [process.env.DEPERSONAL_GAME, ...steamLocations.map(s=>path.join(s,'steamapps','common','Depersonalization'))];
  let game = null;
  for (const candidate of gameCandidates.filter(Boolean)) {
    if (await isDir(path.join(candidate,'Depersonalization-Release_Data'))) {
      game = path.resolve(candidate);
      break;
    }
  }
  const workshop = [];
  const candidates = [process.env.DEPERSONAL_WORKSHOP, ...steamLocations.map(s=>path.join(s,'steamapps','workshop','content','1477070'))];
  for (const candidate of candidates.filter(Boolean)) {
    const dir = path.resolve(candidate);
    if (!workshop.includes(dir) && await isDir(dir)) workshop.push(dir);
  }
  const local = process.env.DEPERSONAL_LOCAL || (process.env.LOCALAPPDATA &&
    path.resolve(process.env.LOCALAPPDATA, '..','LocalLow','MeowNature','Depersonalization-Release'));
  return {game, workshop, local, repo};
}

async function extractVanilla(paths, configs, temp) {
  if (!paths.game) return {files:[], note:'未找到游戏安装目录'};
  const packed = path.join(paths.game,'Depersonalization-Release_Data','resources.assets');
  const exe = path.join(paths.repo,'tools-external','AssetStudioCLI','AssetStudioModCLI_net472_win32_64','AssetStudioModCLI.exe');
  const stats = await Promise.all([fs.stat(packed),fs.stat(exe)]).catch(()=>null);
  if (!stats) return {files:[], note:'找不到原版资源或仓库内的 AssetStudioCLI'};
  const names = new Set();
  for (const config of configs) {
    for (const anim of config.AnimationList) {
      for (const frame of anim.ExtraAnims ?? []) {
        const name = frame.SpriteName || (frame.ResLoadPath && fullStem(frame.ResLoadPath));
        if (name) names.add(name);
      }
    }
  }
  // 同名精灵会保留 PathID 后缀，目录索引会跳过有歧义的帧。
  const terms = [...names].map(escapeRegex);
  const charPattern = '[A-Za-z].*?[_-](?:idle|stand|battle_idle)[_-][0-9].*';
  const chunks = [ [charPattern,'puncture_attack_fx_.*'] ];
  let current = chunks[0];
  let length = current.join('|').length;
  for (const term of terms) {
    if (length + term.length > 23_000) { current = []; chunks.push(current); length = 0; }
    current.push(term);
    length += term.length + 1;
  }
  const key = digest(JSON.stringify([packed,stats[0].size,stats[0].mtimeMs,stats[1].mtimeMs,chunks,2]));
  const output = path.join(temp,'原版帧', key);
  const ready = path.join(output, 'complete');
  if (await fs.stat(ready).catch(()=>null)) return {files:await imagesIn(output, 3)};
  await fs.mkdir(output,{recursive:true});
  for (const chunk of chunks) {
    const result = await new Promise(resolve => {
      const child = spawn(exe,[packed,'-m','export','-t','sprite','-g','none','-o',output,
        '--filter-with-regex','--filter-by-name','(?i)^(?:'+chunk.join('|')+')$',
        '--max-export-tasks','2','--log-level','warning','--log-output','console'],
      {stdio:'ignore', windowsHide:true});
      child.once('error',()=>resolve(1));
      child.once('exit',code=>resolve(code));
    });
    if (result !== 0) return {files:[],note:'原版帧提取失败，模组素材仍可用'};
  }
  const files = await imagesIn(output,3);
  if (!files.length) return {files:[],note:'未提取到原版帧'};
  await fs.writeFile(ready,'ok');
  return {files};
}

function lookupImages(files, stripDuplicates = false) {
  const byName = new Map();
  for (const file of files) {
    const name = (stripDuplicates ? base(file).replace(/_#-?\d+$/,'') : base(file)).toLowerCase();
    if (!byName.has(name)) byName.set(name, []);
    byName.get(name).push(file);
  }
  return name => {
    const matches = byName.get(String(name).toLowerCase());
    return matches?.length === 1 ? matches[0] : null;
  };
}

function frameSets(configs, resolver, source, add) {
  let skipped = 0;
  for (const config of configs) {
    for (const anim of config.AnimationList) {
      const timeline = anim.ExtraAnims ?? [];
      const frames = timeline.map(entry=>resolver(entry));
      if (!frames.length || frames.some(file=>!file)) { skipped++; continue; }
      const name = anim.AnimName && anim.AnimName !== 'idle'
        ? config.EffectName + ' / ' + anim.AnimName : config.EffectName;
      const times = timeline.map(frame=> Math.max(0.015, (Number(config.AnimSpeed) || .12) * (Number(frame.ShowCount) || 1)));
      add('effect', name, source, frames, {times, loop:!!anim.IsLoop});
    }
  }
  return skipped;
}

function roleTimelines(configs, resolveFrame) {
  const byFrames = new Map();
  const byFirstFrame = new Map();
  for (const config of configs) {
    const clips = Object.create(null);
    for (const clip of config.AnimationList) {
      if (!/^[a-z0-9_-]{1,40}$/i.test(clip.Key??'') || clip.IsUseOldMode ||
        !clip.SpriteDatas?.length || !(Number(clip.IntervalTime)>0)) continue;
      const frames = clip.SpriteDatas.map(frame=>resolveFrame(frame.AnimSprite?.Key));
      const times = clip.SpriteDatas.map(frame=>Number(frame.ShowCount)*Number(clip.IntervalTime));
      if (frames.every(Boolean) && times.every(t=>Number.isFinite(t)&&t>0))
        clips[clip.Key] = {frames,times,loop:!!clip.IsLoop};
    }
    if (clips.idle) {
      const frames=clips.idle.frames;
      const timeline={times:clips.idle.times,clips,displayLabel:config.Speaker||config.ModelName};
      byFrames.set(frames.join('\0'),timeline);
      byFirstFrame.set(frames[0],byFirstFrame.has(frames[0]) ? null : timeline);
    }
  }
  return {byFrames,byFirstFrame};
}

async function modRoots(paths) {
  const roots = new Map();
  async function add(source, dir) {
    if (await isDir(dir)) roots.set(path.resolve(dir), source);
  }
  for (const root of paths.workshop) for (const entry of await list(root)) {
    if (!entry.isDirectory()) continue;
    const folder = path.join(root,entry.name);
    const data = await readJson(path.join(folder,'Uploader')).catch(()=>null);
    const label = data?.Title || data?.Name || data?.WorkshopTitle;
    const named = entry.name === '3253405563' ? '茉莉 · 生命树奥萝菈' : label;
    await add(named || '工坊 '+entry.name, folder);
  }
  for (const baseDir of [path.join(paths.repo,'mods'), paths.game && path.join(paths.game,'本地Mod测试'), paths.local && path.join(paths.local,'UGC')].filter(Boolean)) {
    for (const entry of await list(baseDir)) if (entry.isDirectory()) await add('本地 · '+entry.name,path.join(baseDir,entry.name));
  }
  if (paths.local) await add('本地游戏编辑工程', paths.local);
  return roots;
}

export async function getCatalog(paths, temp, onUpdate) {
  const media = new Map(), entries = new Map();
  const notes = [];
  const modAnimations = [];
  const mediaUrl = file => {
    const key = digest(file);
    media.set(key,file);
    return '/media/'+key;
  };
  const add = (kind, label, source, files, options = {}, identityOptions = options, identityFiles = files) => {
    if (!files.length || typeof label !== 'string') return;
    const id = digest(JSON.stringify([kind,label,source,identityOptions,identityFiles]));
    if (entries.has(id)) return;
    const frames = files.map(mediaUrl);
    const entry = {id,kind,label,source,frames,...options};
    if (options.clips) entry.clips = Object.fromEntries(Object.entries(options.clips).map(([key,clip])=>
      [key,{...clip,frames:clip.frames.map(mediaUrl)}]));
    entries.set(id,entry);
  };
  const publish = (loading = false) => {
    const assets = [...entries.values()].sort((a,b)=>cmp.compare(a.source,b.source)||cmp.compare(a.label,b.label));
    onUpdate({assets,notes:[...notes],loading},media);
  };
  for (const [root,source] of await modRoots(paths)) {
    const resources = await isDir(path.join(root,'Project_Depersonal','Assets','Resources'))
      ? path.join(root,'Project_Depersonal','Assets','Resources') : path.join(root,'Assets','Resources');
    const fxImages = await imagesIn(path.join(resources,'Texture','ExtraAnim'),3);
    const resolveFx = lookupImages([...fxImages,...await imagesIn(path.join(resources,'Texture','Effect'),3)]);
    const fxConfigs = await definitions(path.join(resources,'Config','Game','ExtraAnim'));
    modAnimations.push({configs:fxConfigs,resolveFx,source});
    frameSets(fxConfigs,frame=>resolveFx(frame.AnimSprite?.Key),source,add);
    for (const file of fxImages) add('effect',base(file),source,[file]);
    for (const file of await imagesIn(path.join(root,'assets','Particles'),1)) add('effect',base(file),source,[file]);

    const input = path.join(root,'ResourcesFramework','Input');
    for (const folder of await list(input)) {
      if (!folder.isDirectory()) continue;
      const files = await imagesIn(path.join(input,folder.name));
      const idle = files.filter(f=>/^1-\d+$|(?:^|[_-])(?:idle|stand)(?:$|[_-])/i.test(base(f)));
      if (idle.length) add('model',source.includes('奥萝菈')?'茉莉':folder.name,source,idle.slice(0,32),{fps:10,pivot:.45});
    }
    const modelFolders = await Promise.all(['RoleModel','Model','Charator'].map(name=>imagesIn(path.join(resources,'Texture',name),3)));
    const modelTimings = roleTimelines(await definitions(path.join(resources,'Config','Game','RoleModel')),lookupImages(modelFolders.flat()));
    for (const files of modelFolders) {
      const groups = new Map();
      for (const file of files) {
        const key = path.dirname(file), frames = groups.get(key) ?? [];
        frames.push(file);
        groups.set(key,frames);
      }
      for (const [dir,frames] of groups) {
        const idle = frames.filter(f=>/idle|stand|^1-\d+$/i.test(base(f))).slice(0,32);
        if (!idle.length) continue;
        const layout = await readJson(path.join(dir,'pose-pivots.json')).catch(()=>null);
        const authored = modelTimings.byFrames.get(idle.join('\0')) ?? (layout
          ? idle.map(frame=>modelTimings.byFirstFrame.get(frame)).find(Boolean)
          : modelTimings.byFirstFrame.get(idle[0]));
        const feet = layout ? Object.fromEntries(frames.flatMap(file=>{
          const point=layout[path.basename(file)];
          const valid=p=>p && Number.isFinite(p.x) && p.x>=0 && Number.isFinite(p.y) && p.y>=0;
          if (!valid(point)) return [];
          const anchors={x:point.x,y:point.y};
          for (const name of poseAnchors)
            if (valid(point[name])) anchors[name]={x:point[name].x,y:point[name].y};
          return [[mediaUrl(file),anchors]];
        })) : undefined;
        // 配有逐帧落点时按动作表识别待机，避免举枪等其它站姿改变已有工程的素材 ID。
        const identity = {fps:9};
        const referenceIdle = authored?.clips.idle.frames.find(file=>path.basename(file)===layout?.referenceIdle);
        const identityFrames = referenceIdle ? [referenceIdle]
          : layout && authored ? [...new Set(authored.clips.idle.frames)] : idle;
        add('model',path.basename(dir),source,authored?.clips.idle.frames ?? idle,
          authored ? {...identity,...authored,feet} : identity,identity,identityFrames);
      }
    }
  }
  publish(true);

  const streaming = paths.game && path.join(paths.game,'Depersonalization-Release_Data','StreamingAssets');
  const configs = streaming ? await definitions(path.join(streaming,'Game','ExtraAnim')) : [];
  const extracted = await extractVanilla(paths,[...configs,...modAnimations.flatMap(entry=>entry.configs)],temp);
  if (extracted.note) notes.push(extracted.note);
  const lookup = lookupImages(extracted.files,true);
  const resolveVanilla = frame=>lookup(frame.SpriteName || (frame.ResLoadPath && fullStem(frame.ResLoadPath)));
  const missing = frameSets(configs,resolveVanilla,'原版',add);
  let modFramesMissing = 0;
  for (const group of modAnimations) {
    modFramesMissing += frameSets(group.configs,
      frame=>group.resolveFx(frame.AnimSprite?.Key) || (frame.IsInternal && resolveVanilla(frame)),group.source,add);
  }
  if (missing && extracted.files.length) notes.push('原版仅列出帧可唯一匹配的动画；同名图集帧留待进一步解析。');
  if (modFramesMissing) notes.push('有些模组动画引用其他打包资源，素材库只列出可还原的动画。');
  const roles = new Map();
  const puncture = [];
  for (const file of extracted.files) {
    const name = base(file);
    if (/^puncture_attack_fx_\d+$/i.test(name)) puncture.push(file);
    const match = actionPattern.exec(name);
    if (!match || !/^(idle|stand|battle_idle)$/i.test(match[2])) continue;
    const key = match[1]+' / '+match[2];
    if (!roles.has(key)) roles.set(key, []);
    roles.get(key).push(file);
  }
  if (puncture.length) add('effect','突刺','原版',puncture,{fps:18,loop:false});
  const roleConfigs = streaming ? await definitions(path.join(streaming,'Game','RoleModel')) : [];
  const originalTimings = roleTimelines(roleConfigs,lookup);
  for (const [name,frames] of roles) {
    const idle=frames.slice(0,30);
    const authored=originalTimings.byFrames.get(idle.join('\0'));
    add('model',name,'原版',idle,authored ? {fps:9,...authored} : {fps:9},{fps:9});
  }
  notes.push('Unity 粒子预制体由游戏运行时播放。编辑器素材库支持能够提取的二维帧和贴图。');
  publish(false);
}
