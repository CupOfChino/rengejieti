import fs from 'node:fs/promises';
import path from 'node:path';
import {randomBytes} from 'node:crypto';

const presets = new Map([
  ['blade-hit','利刃命中'],
  ['charm','魅惑'],
  ['adrenaline','肾上腺素']
]);

const number = (value, fallback, low, high) =>
  typeof value === 'number' && Number.isFinite(value) ? Math.max(low,Math.min(high,value)) : fallback;
const invalid = message => Object.assign(new Error(message),{httpStatus:400});

function buildCommand(body) {
  if (!body || !['inspect','play','close'].includes(body.operation)) throw invalid('操作无效');
  if (!presets.has(body.preset)) throw invalid('请选择列表中的预制体');
  if (body.edits !== undefined && (!Array.isArray(body.edits) || body.edits.length > 64)) {
    throw invalid('粒子层参数不完整');
  }

  const placement = {
    x:number(body.placement?.x,0,-1.5,1.5), y:number(body.placement?.y,0,-1.2,1.2),
    scale:number(body.placement?.scale,1,.2,4), angle:number(body.placement?.angle,0,-180,180)
  };
  const edits = new Map();
  for (const row of body.edits ?? []) {
    if (!row || !Number.isInteger(row.index) || row.index < 0 || row.index >= 64) throw invalid('粒子层编号无效');
    const color = typeof row.color === 'string' && /^#[a-f\d]{6}$/i.test(row.color) ? row.color : '';
    edits.set(row.index,{
      index:row.index, color,
      size:number(row.size,1,.1,4), lifetime:number(row.lifetime,1,.1,4),
      speed:number(row.speed,1,.1,4), density:number(row.density,1,0,4)
    });
  }
  return {operation:body.operation,preset:body.preset,placement,edits:[...edits.values()]};
}

async function receive(request) {
  const chunks = [];
  let length = 0;
  for await (const bytes of request) {
    length += bytes.length;
    if (length > 24_576) throw invalid('参数文件过大');
    chunks.push(bytes);
  }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw invalid('参数格式无效'); }
}

export async function previewBridge(paths) {
  const session = randomBytes(16).toString('hex');
  let mailbox;
  let command = null;
  let revision = 0;
  let serial = Promise.resolve();
  let touchedAt = 0;

  for (const root of paths.workshop) {
    const mod = path.join(root,'3801731500');
    const plugin = await fs.stat(path.join(mod,'plugins','AttackTargetVisualizer.dll')).catch(()=>null);
    if (plugin?.isFile()) {
      mailbox = path.join(mod,'temp','VfxEditorBridge');
      break;
    }
  }
  const json = (res,data,code=200) => {
    res.writeHead(code,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});
    res.end(JSON.stringify(data));
  };
  const enqueue = action => {
    const result = serial.then(action);
    serial = result.catch(()=>{});
    return result;
  };

  async function putCommand(body) {
    await fs.mkdir(mailbox,{recursive:true});
    const post = {...body,session,revision:++revision};
    const temporary = path.join(mailbox,'request-'+session+'.new');
    await fs.writeFile(temporary,JSON.stringify(post),'utf8');
    await fs.rename(temporary,path.join(mailbox,'request.json'));
    command = post;
    touchedAt = Date.now();
    return {revision:post.revision};
  }

  async function readReply() {
    if (!command) return null;
    const source = await fs.readFile(path.join(mailbox,'response.json'),'utf8').catch(()=>null);
    if (!source) return null;
    let reply;
    try { reply = JSON.parse(source); }
    catch { return null; }
    return reply?.session === session && reply?.revision === command.revision ? reply : null;
  }

  return async (request,response,route) => {
    try {
      if (route.pathname === '/live/status' && request.method === 'GET') {
        if (command && command.operation !== 'close' && Date.now() - touchedAt > 2000) {
          await enqueue(async()=>{
            const inbox = path.join(mailbox,'request.json');
            const now = new Date();
            const source = await fs.readFile(inbox,'utf8').catch(()=>null);
            if (source?.includes(session)) await fs.utimes(inbox,now,now).catch(()=>{});
            touchedAt = now.getTime();
          });
        }
        json(response,{installed:!!mailbox,revision:command?.revision??0, reply:await readReply(),
          presets:[...presets].map(([id,label])=>({id,label}))});
      } else if (route.pathname === '/live/command' && request.method === 'POST') {
        if (!mailbox) { json(response,{error:'找不到本机的「可视化攻击目标」测试插件'},409); return; }
        const body = buildCommand(await receive(request));
        json(response,await enqueue(()=>putCommand(body)));
      } else if (route.pathname === '/live/frame' && request.method === 'GET') {
        const reply = await readReply();
        const index = Number(route.searchParams.get('index'));
        if (!reply || reply.phase !== 'done' || Number(route.searchParams.get('revision')) !== reply.revision ||
            !route.searchParams.has('index') || !Number.isInteger(index) || index < 0 || index >= reply.frameCount || index >= 15) {
          response.writeHead(404).end(); return;
        }
        const bytes = await fs.readFile(path.join(mailbox,'frame-'+index+'.jpg'));
        response.writeHead(200,{'Content-Type':'image/jpeg','Cache-Control':'no-store'});
        response.end(bytes);
      } else {
        response.writeHead(404).end();
      }
    } catch (error) {
      json(response,{error:error.message},error.httpStatus ?? 500);
    }
  };
}
