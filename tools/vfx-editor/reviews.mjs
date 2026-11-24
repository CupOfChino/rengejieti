import fs from 'node:fs/promises';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {maxLayers} from './animation.mjs';

const reviewId = /^\d{8}T\d{9}Z-[a-f0-9]{8}$/;
const bodyLimit = 96*1024*1024;
const header = Buffer.from([137,80,78,71,13,10,26,10]);

function json(res,status,value){
  res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
  res.end(JSON.stringify(value));
}

function png(value){
  if(typeof value!=='string'||value.length>12*1024*1024||
    !/^[a-z0-9+/]+={0,2}$/i.test(value))throw new Error('检查图的图片格式无效');
  const bytes=Buffer.from(value,'base64');
  if(bytes.length<32||!bytes.subarray(0,8).equals(header)||bytes.toString('ascii',12,16)!=='IHDR'||
    bytes.readUInt32BE(16)<1||bytes.readUInt32BE(16)>1800||
    bytes.readUInt32BE(20)<1||bytes.readUInt32BE(20)>1800)throw new Error('检查图的 PNG 尺寸无效');
  return bytes;
}

function validReport(report){
  const {project,current,length,times,roles,comparison,reference}=report??{};
  if(project?.version!==1||typeof project.name!=='string'||project.name.length>72||
    !Array.isArray(project.layers)||project.layers.length>maxLayers||
    !Number.isFinite(current)||current<0||current>90||
    !Number.isFinite(length)||length<=0||length>90||
    !Array.isArray(times)||times.length<1||times.length>8||
    !times.every(t=>Number.isFinite(t)&&t>=0&&t<=length+.01)||
    !Array.isArray(roles)||roles.length>2)throw new Error('检查图的工程或时间信息无效');
  if(comparison!==undefined||reference!==undefined){
    if(typeof comparison!=='string'||!reference||!['project','image'].includes(reference.type)||
      typeof reference.name!=='string'||reference.name.length>120||
      (reference.type==='project'&&(!Number.isFinite(reference.length)||reference.length<=0||reference.length>length)))
      throw new Error('参考画面的信息无效');
  }
}

export function reviewRoutes(root){
  return async (req,res,route,host)=>{
    if(route==='/review'&&req.method==='POST'){
      if(req.headers.origin!=='http://'+host||
        req.headers['content-type']?.split(';')[0]!=='application/json'){
        json(res,403,{error:'请在本机编辑器内保存检查图'});return;
      }
      try{
        const length=Number(req.headers['content-length']);
        if(!Number.isInteger(length)||length<=0||length>bodyLimit){
          json(res,413,{error:'检查图工程过大或文件长度无效'});return;
        }
        const chunks=[];
        let size=0;
        for await(const chunk of req){
          size+=chunk.length;
          if(size>bodyLimit)throw new Error('检查图工程过大');
          chunks.push(chunk);
        }
        const report=JSON.parse(Buffer.concat(chunks).toString('utf8'));
        validReport(report);
        const current=png(report.capture), timeline=png(report.timeline);
        const comparison=report.comparison===undefined ? null : png(report.comparison);
        const id=new Date().toISOString().replace(/[-:.]/g,'')+'-'+randomUUID().slice(0,8);
        const directory=path.join(root,id);
        const info={id,name:report.project.name,current:report.current,length:report.length,
          times:report.times,roles:report.roles,url:'/review/'+id+'/timeline.png'};
        if(comparison){
          const {type,name,length}=report.reference;
          info.reference=type==='project'?{type,name,length}:{type,name};
          info.comparisonUrl='/review/'+id+'/comparison.png';
        }
        await fs.mkdir(root,{recursive:true});
        await fs.mkdir(directory);
        const files=[
          fs.writeFile(path.join(directory,'current.png'),current,{flag:'wx'}),
          fs.writeFile(path.join(directory,'timeline.png'),timeline,{flag:'wx'}),
          fs.writeFile(path.join(directory,'project.vfx.json'),JSON.stringify(report.project,null,2),{flag:'wx'}),
          fs.writeFile(path.join(directory,'manifest.json'),JSON.stringify(info,null,2),{flag:'wx'})
        ];
        if(comparison)files.push(fs.writeFile(path.join(directory,'comparison.png'),comparison,{flag:'wx'}));
        await Promise.all(files);
        const stage=path.join(root,id+'.tmp');
        try{
          await fs.writeFile(stage,JSON.stringify(info,null,2),{flag:'wx'});
          await fs.rename(stage,path.join(root,'latest.json'));
        }finally{await fs.rm(stage,{force:true});}
        json(res,200,info);
      }catch(error){json(res,400,{error:error.message});}
      return;
    }
    const [,id,name]=route.split('/').slice(1);
    if(req.method!=='GET'||!reviewId.test(id??'')||!['current.png','timeline.png','comparison.png'].includes(name)){
      res.writeHead(404).end();return;
    }
    try{
      const data=await fs.readFile(path.join(root,id,name));
      res.writeHead(200,{'Content-Type':'image/png','Cache-Control':'no-cache'});
      res.end(data);
    }catch{res.writeHead(404).end();}
  };
}
