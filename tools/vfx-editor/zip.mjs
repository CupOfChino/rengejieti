// PNG 本身已压缩。ZIP 使用存储模式，便于直接用 Windows 解压。
const encoder = new TextEncoder();
const crcTable = Array.from({length:256},(_,i)=>{
  let c=i;
  for(let bit=0;bit<8;bit++) c=(c&1) ? (0xedb88320^(c>>>1)) : c>>>1;
  return c>>>0;
});
function crc32(bytes) {
  let crc=0xffffffff;
  for (const byte of bytes) crc=crcTable[(crc^byte)&255]^(crc>>>8);
  return (crc^0xffffffff)>>>0;
}
function header(length,words) {
  const bytes=new Uint8Array(length), view=new DataView(bytes.buffer);
  for(const [offset,size,value] of words){
    if(size===2)view.setUint16(offset,value,true); else view.setUint32(offset,value,true);
  }
  return bytes;
}
export function zip(entries) {
  const local=[], central=[];
  let offset=0, directoryLength=0;
  for(const [name,data] of entries) {
    const file=encoder.encode(name), value=typeof data==='string'?encoder.encode(data):data;
    const checksum=crc32(value), size=value.length;
    const lf=header(30,[[0,4,0x04034b50],[4,2,20],[6,2,0x800],[14,4,checksum],[18,4,size],[22,4,size],[26,2,file.length]]);
    const cf=header(46,[[0,4,0x02014b50],[4,2,20],[6,2,20],[8,2,0x800],[16,4,checksum],[20,4,size],[24,4,size],[28,2,file.length],[42,4,offset]]);
    local.push(lf,file,value);central.push(cf,file);
    offset+=lf.length+file.length+size;directoryLength+=cf.length+file.length;
  }
  const end=header(22,[[0,4,0x06054b50],[8,2,entries.length],[10,2,entries.length],[12,4,directoryLength],[16,4,offset]]);
  return new Blob([...local,...central,end],{type:'application/zip'});
}
