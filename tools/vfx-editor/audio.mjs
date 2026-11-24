const maxCues=64;
const maxStart=60;
const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
const soundId=/^[a-f0-9]{20}$/;
const maxGroup=32;
const maxSubtitle=240;
const speedFor=cue=>Number.isFinite(cue.speed)?clamp(cue.speed,0.5,2):1;
const groupFor=cue=>Number.isInteger(cue.group)?clamp(cue.group,0,maxGroup):0;

export function readAudioCues(value) {
  if(!Array.isArray(value))return [];
  return value.filter(cue=>typeof cue?.sound==='string'&&soundId.test(cue.sound)&&
    Number.isFinite(cue.at)&&Number.isFinite(cue.volume)).slice(0,maxCues)
    .map(cue=>({sound:cue.sound,at:clamp(cue.at,0,maxStart),volume:clamp(cue.volume,0,100),
      speed:speedFor(cue),group:groupFor(cue),
      ...(typeof cue.subtitle==='string'?{subtitle:cue.subtitle.slice(0,maxSubtitle)}:{})}));
}

export function audioTrack({byId,currentTime,onEdit,onJump,status}) {
  const picker=byId('sound-asset'), search=byId('sound-search'), preview=byId('sound-preview');
  const add=byId('sound-add'), list=byId('sound-cues');
  const groupButton=byId('sound-pair');
  const skill=byId('sound-skill'), loadSkill=byId('sound-load-skill'), applySkill=byId('sound-apply-skill');
  const sounds=new Map(), voices=new Map();
  const groupSelection=new Set(), chosen=new Map();
  let cues=[], audition=null, auditionSource=null, loaded=false, failed=false, playhead=0, running=false;
  const createAudio=id=>new Audio('/sound/'+id);
  const durationFor=cue=>voices.get(cue)?.audio.duration/speedFor(cue);
  const selected=cue=>groupFor(cue)===0||chosen.get(groupFor(cue))===cue;
  const canPlay=cue=>cue.volume>0&&sounds.has(cue.sound)&&!voices.get(cue)?.failed;
  const defaultSubtitle=id=>{
    const subtitle=sounds.get(id)?.subtitle;
    return typeof subtitle==='string'?subtitle.slice(0,maxSubtitle):'';
  };

  function fillSubtitles() {
    for(const cue of cues){
      if(cue.subtitle===undefined&&sounds.has(cue.sound))cue.subtitle=defaultSubtitle(cue.sound);
    }
  }

  function chooseGroups(reset=false) {
    const groups=new Map();
    for(const cue of cues){
      const group=groupFor(cue);
      if(!group||!canPlay(cue))continue;
      if(!groups.has(group))groups.set(group,[]);
      groups.get(group).push(cue);
    }
    for(const group of chosen.keys()){
      if(!groups.has(group))chosen.delete(group);
    }
    for(const [group,variants] of groups){
      if(reset||!variants.includes(chosen.get(group)))
        chosen.set(group,variants[Math.floor(Math.random()*variants.length)]);
    }
  }

  function stopAudition() {
    if(!audition)return;
    audition.pause();audition=null;auditionSource=null;preview.textContent='试听';
    list.querySelectorAll('.cue-audition').forEach(button=>button.textContent='试听本条');
  }

  function updatePicker() {
    const before=picker.value, query=search.value.trim().toLowerCase();
    const matches=[...sounds.values()].filter(sound=>sound.label.toLowerCase().includes(query));
    picker.replaceChildren(...matches.map(sound=>new Option(sound.label,sound.id)));
    if(matches.some(sound=>sound.id===before))picker.value=before;
    if(!matches.length)picker.append(new Option(failed?'音效库读取失败':loaded?'没有匹配音效':'正在读取音效…',''));
    if(typeof auditionSource==='string'&&auditionSource!==picker.value)stopAudition();
    preview.disabled=!matches.length;
    add.disabled=!matches.length||cues.length>=maxCues;
  }

  function refreshVoices() {
    for(const [cue,voice] of voices){
      if(cues.includes(cue)&&sounds.has(cue.sound))continue;
      voice.audio.pause();voices.delete(cue);
    }
    for(const cue of cues){
      if(voices.has(cue)||!sounds.has(cue.sound))continue;
      const audio=createAudio(cue.sound);
      audio.preload='metadata';
      audio.preservesPitch=true;
      voices.set(cue,{audio,active:false});
      audio.addEventListener('loadedmetadata',()=>{
        if(voices.has(cue))onEdit(false);
      });
      audio.addEventListener('error',()=>{
        const voice=voices.get(cue);
        if(voice?.audio!==audio)return;
        voice.failed=true;
        chooseGroups();
        update(playhead,running);
        onEdit(false);
        status('无法读取音效「'+sounds.get(cue.sound).label+'」。');
      });
    }
  }

  function update(time,playing,reset=false) {
    playhead=time;running=playing;
    if(reset)chooseGroups(true);
    if(playing)stopAudition();
    for(const [cue,voice] of voices){
      const {audio}=voice, speed=speedFor(cue), offset=(time-cue.at)*speed;
      audio.volume=cue.volume/100;
      audio.playbackRate=speed;
      if(!playing||!canPlay(cue)||!selected(cue)||offset<0||!Number.isFinite(audio.duration)||offset>=audio.duration){
        if(voice.active)audio.pause();
        voice.active=false;
        continue;
      }
      if(voice.active&&!reset&&Math.abs(audio.currentTime-offset)<0.18)continue;
      const seeking=voice.active&&!reset;
      audio.currentTime=offset;
      if(seeking)continue;
      voice.active=true;
      audio.play().catch(error=>{
        if(error.name!=='AbortError'&&voice.active&&voices.has(cue))
          status('无法播放音效「'+sounds.get(cue.sound).label+'」。');
      });
    }
  }

  function updateGroupButton() {
    groupButton.disabled=groupSelection.size<2;
  }

  function reviseGroups() {
    groupSelection.clear();chooseGroups();stopAudition();render();update(playhead,running);onEdit();
  }

  groupButton.onclick=()=>{
    if(groupSelection.size<2)return;
    const used=new Set(cues.filter(cue=>!groupSelection.has(cue)).map(groupFor));
    const group=Array.from({length:maxGroup},(_,index)=>index+1).find(value=>!used.has(value));
    if(group===undefined){status('已使用全部 32 个随机组。');return;}
    for(const cue of groupSelection)cue.group=group;
    reviseGroups();
  };

  function auditionFrom(source,button) {
    if(auditionSource===source){stopAudition();return;}
    const cue=typeof source==='string'?null:source;
    const id=cue?.sound??source;
    if(!sounds.has(id))return;
    onJump(currentTime());
    const audio=createAudio(id);
    audio.playbackRate=cue?speedFor(cue):1;
    audio.preservesPitch=true;
    audio.volume=cue?cue.volume/100:1;
    audition=audio;auditionSource=source;button.textContent='停止试听';
    audio.onended=()=>{if(audition===audio)stopAudition();};
    audio.play().catch(()=>{
      if(audition!==audio)return;
      stopAudition();status('无法试听「'+sounds.get(id).label+'」。');
    });
  }

  function render() {
    updateGroupButton();
    list.replaceChildren(...cues.map(cue=>{
      const row=document.createElement('div'), controls=document.createElement('div');
      row.className='audio-cue';controls.className='audio-controls';
      const title=document.createElement('span');
      title.textContent=sounds.get(cue.sound)?.label??(loaded?'素材缺失 · '+cue.sound:'正在读取音效…');
      const choose=document.createElement('input'), choice=document.createElement('label');
      choose.type='checkbox';choose.checked=groupSelection.has(cue);
      choose.onchange=()=>{
        if(choose.checked)groupSelection.add(cue);
        else groupSelection.delete(cue);
        updateGroupButton();
      };
      choice.className='audio-pair-choice';choice.append(choose,' 选入随机组');
      for(const [key,label,min,max,step,fallback] of [
        ['at','开始秒',0,maxStart,0.01,0],['volume','音量 %',0,100,1,100],
        ['speed','语速 ×',0.5,2,0.05,1],['group','随机组',0,maxGroup,1,0]
      ]){
        const input=document.createElement('input'), field=document.createElement('label');
        input.type='number';input.min=min;input.max=max;input.step=step;input.value=cue[key]??fallback;
        input.onchange=()=>{
          const parsed=input.valueAsNumber;
          if(Number.isFinite(parsed))cue[key]=clamp(key==='group'?Math.round(parsed):parsed,min,max);
          input.value=cue[key]??fallback;
          if(key==='group'){reviseGroups();return;}
          if(auditionSource===cue&&key==='speed')audition.playbackRate=cue.speed;
          if(key==='volume'){
            if(auditionSource===cue)audition.volume=cue.volume/100;
            chooseGroups();
          }
          update(playhead,running);onEdit();
        };
        field.append(label+' ',input);controls.append(field);
      }
      const jump=document.createElement('button'), remove=document.createElement('button');
      const auditionCue=document.createElement('button'), unpair=document.createElement('button');
      jump.type=remove.type=auditionCue.type=unpair.type='button';
      jump.textContent='跳转';remove.textContent='删除';unpair.textContent='解除随机组';
      auditionCue.className='cue-audition';auditionCue.disabled=!sounds.has(cue.sound);
      auditionCue.textContent=auditionSource===cue?'停止试听':'试听本条';
      auditionCue.onclick=()=>auditionFrom(cue,auditionCue);
      unpair.disabled=groupFor(cue)===0;
      unpair.onclick=()=>{
        const group=groupFor(cue);
        for(const variant of cues)if(groupFor(variant)===group)variant.group=0;
        reviseGroups();
      };
      const subtitle=document.createElement('textarea'), subtitleField=document.createElement('label');
      subtitle.rows=2;subtitle.maxLength=maxSubtitle;subtitle.value=cue.subtitle??'';
      subtitle.oninput=()=>{cue.subtitle=subtitle.value.slice(0,maxSubtitle);onEdit();};
      subtitleField.className='audio-subtitle';subtitleField.append('字幕',subtitle);
      jump.onclick=()=>onJump(cue.at);
      remove.onclick=()=>{
        cues.splice(cues.indexOf(cue),1);
        groupSelection.delete(cue);chooseGroups();
        if(auditionSource===cue)stopAudition();
        refreshVoices();render();updatePicker();onEdit();
      };
      controls.append(jump,auditionCue,unpair,remove);row.append(title,choice,controls,subtitleField);
      return row;
    }));
  }

  search.oninput=updatePicker;
  picker.onchange=stopAudition;
  preview.onclick=()=>auditionFrom(picker.value,preview);
  add.onclick=()=>{
    if(!sounds.has(picker.value)||cues.length>=maxCues)return;
    cues.push({sound:picker.value,at:clamp(Math.round(currentTime()*100)/100,0,maxStart),volume:100,
      speed:1,subtitle:defaultSubtitle(picker.value),group:0});
    refreshVoices();render();updatePicker();onEdit();
  };
  let busy=false;
  function updateSkillButtons(){
    loadSkill.disabled=applySkill.disabled=!skill.value||busy;
  }
  async function configureSkill(apply){
    if(busy||!skill.value)return;
    const name=skill.selectedOptions[0].textContent;
    if(apply&&!confirm('将当前 '+cues.length+' 条演示音效应用到「'+name+'」？'))return;
    if(!apply&&cues.length&&!confirm('用「'+name+'」已保存的音效替换当前音效轨？'))return;
    const target=cues;
    busy=true;updateSkillButtons();
    try{
      const request=apply?{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({cues})}:undefined;
      const response=await fetch('/skill-audio/'+skill.value,request);
      const data=await response.json();
      if(!response.ok)throw new Error(data.error);
      if(apply){
        status(data.warning??'已应用到「'+name+'」'+(data.synced?'，并同步本机测试宿主。':'。'));
      }else if(cues===target){
        onJump(currentTime());
        cues.splice(0,cues.length,...readAudioCues(data.cues));
        groupSelection.clear();fillSubtitles();chooseGroups(true);
        refreshVoices();render();updatePicker();onEdit();
        status('已载入「'+name+'」的音效。');
      }
    }catch(error){
      status(error.message);
    }finally{
      busy=false;updateSkillButtons();
    }
  }
  skill.onchange=updateSkillButtons;
  loadSkill.onclick=()=>void configureSkill(false);
  applySkill.onclick=()=>void configureSkill(true);
  void fetch('/sounds').then(response=>{
    if(!response.ok)throw new Error('读取失败');
    return response.json();
  }).then(items=>{
    for(const item of items)sounds.set(item.id,item);
  }).catch(()=>{failed=true;}).finally(()=>{
    loaded=true;fillSubtitles();refreshVoices();chooseGroups();render();updatePicker();onEdit(false);
  });

  return {
    labelFor:id=>sounds.get(id)?.label.split(' / ').at(-1)??'素材缺失',
    durationFor,
    captionAt(time){
      if(auditionSource!==null&&audition.volume>0)
        return typeof auditionSource==='string'?defaultSubtitle(auditionSource):auditionSource.subtitle??'';
      let latest=null;
      for(const cue of cues){
        if(canPlay(cue)&&selected(cue)&&cue.subtitle?.trim()&&Number.isFinite(durationFor(cue))&&
          cue.at<=time&&(!latest||cue.at>=latest.at))latest=cue;
      }
      return latest&&time<latest.at+durationFor(latest)?latest.subtitle:'';
    },
    selectSkill(id){skill.value=id;updateSkillButtons();},
    update,
    stopAudition,
    setCues(next){stopAudition();cues=next;groupSelection.clear();fillSubtitles();chooseGroups(true);
      refreshVoices();render();updatePicker();},
    get duration(){
      return cues.reduce((end,cue)=>{
        const duration=durationFor(cue);
        return Math.max(end,cue.at+(Number.isFinite(duration)?duration:0.1));
      },0);
    }
  };
}
