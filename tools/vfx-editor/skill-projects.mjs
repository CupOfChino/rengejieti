import path from 'node:path';
import {readFile} from 'node:fs/promises';

const files = new Map([
  ['885121','切割攻击'], ['885124','尖端打击'], ['885125','解兔'], ['885126','解鹿'],
  ['885127','我很乐意用这颗子弹将你洞穿'], ['885128','解象'], ['885129','处置'], ['885130','预知']
]);

export async function readSkillProject(directory,id) {
  if(!files.has(id))return null;
  const read = async name => JSON.parse((await readFile(path.join(directory,id+'-'+name+'.vfx.json'),'utf8')).replace(/^\uFEFF/,''));
  if(id!=='885129')return read(files.get(id));
  const [front,back]=await Promise.all([read('处置'),read('处置-回身开火')]);
  const left=front.actors.left, right=back.actors.right;
  const thrust=left.cues.find(cue=>cue.action==='valenna_finish_thrust');
  const recover=right.cues.find(cue=>cue.action==='valenna_finish_recover');
  if(!thrust||!recover)throw new Error('处置工程缺少穿身或收枪动作');
  // 与 StrikeProfiles 及 PlayExecutionFinish 对齐：突刺预备 4 帧、定姿 12 帧、回身前风 2 帧。
  const turn=thrust.at+(4+12+2)/30;
  const returned=turn+recover.at+0.33+0.10; // ReturnTime 与 ReturnAfterglow，与其余整招的回待机点一致。
  const imports=new Map((front.imported??[]).map(asset=>[asset.id,asset]));
  const backIds=new Map();
  for(const asset of back.imported??[]){
    const id='import-finale-'+asset.id.slice('import-'.length);
    imports.set(id,{...asset,id});
    backIds.set(asset.id,id);
  }
  const frontShadow=front.layers.find(layer=>layer.asset==='import-shadow');
  const backShadow=back.layers.find(layer=>layer.asset==='import-shadow');
  if(!frontShadow||!backShadow)throw new Error('处置工程缺少靶点');
  const alignShadow=frontShadow.x-backShadow.x-312; // 两个标准站位相距 312px。
  left.cues.push({at:turn,action:'hidden'},{at:returned,action:'battle_idle'});
  front.actors.right={...right,offsetX:alignShadow,action:'hidden',at:0,cues:[
    {at:turn,action:right.action},...right.cues.map(cue=>({...cue,at:turn+cue.at})),
    {at:returned,action:'hidden'}
  ]};
  for(const layer of front.layers){
    if(layer.start===0&&['import-shadow','import-hunt-glow','import-hunt-swords'].includes(layer.asset))
      layer.duration=turn;
  }
  front.layers.push(...back.layers.map(layer=>({...layer,start:turn+layer.start,asset:backIds.get(layer.asset)??layer.asset})));
  front.rightModel=back.rightModel;
  front.name='处置';
  front.imported=[...imports.values()];
  front.audioCues=[...(front.audioCues??[]),...(back.audioCues??[]).map(cue=>({...cue,at:turn+cue.at}))];
  return front;
}
