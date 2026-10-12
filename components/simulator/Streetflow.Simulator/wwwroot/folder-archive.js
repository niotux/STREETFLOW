// Store selected browser files in a ZIP so folder and archive imports use the same API.
const maximumFolderBytes = 256 * 1024 * 1024;
const maximumFolderFiles = 4096;
const crcTable = new Uint32Array(256);
for (let index = 0; index < crcTable.length; index++) {
  let value = index;
  for (let bit = 0; bit < 8; bit++) {
    value = (value >>> 1) ^ ((value & 1) ? 0xedb88320 : 0);
  }
  crcTable[index] = value;
}
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc = crcTable[(crc ^ byte) & 255] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function header(length) {
  const bytes = new Uint8Array(length);
  return {bytes, view: new DataView(bytes.buffer)};
}
export async function archiveFolder(files) {
  if (files.length > maximumFolderFiles) {
    throw new Error('This folder contains too many files. Choose the city project folder itself.');
  }
  let total = 0;
  for (const file of files) {
    total += file.size;
  }
  if (total > maximumFolderBytes) {
    throw new Error('This folder exceeds the 256 MB import limit.');
  }
  const parts = [], directory = [];
  const encoder = new TextEncoder();
  let offset = 0, directorySize = 0;
  for (const file of files) {
    const name = encoder.encode(file.webkitRelativePath || file.name);
    const checksum = crc32(new Uint8Array(await file.arrayBuffer()));
    const local = header(30);
    local.view.setUint32(0, 0x04034b50, true);
    local.view.setUint16(4, 20, true);
    local.view.setUint16(6, 0x0800, true);
    local.view.setUint32(14, checksum, true);
    local.view.setUint32(18, file.size, true);
    local.view.setUint32(22, file.size, true);
    local.view.setUint16(26, name.length, true);
    parts.push(local.bytes, name, file);
    const entry = header(46);
    entry.view.setUint32(0, 0x02014b50, true);
    entry.view.setUint16(4, 20, true);
    entry.view.setUint16(6, 20, true);
    entry.view.setUint16(8, 0x0800, true);
    entry.view.setUint32(16, checksum, true);
    entry.view.setUint32(20, file.size, true);
    entry.view.setUint32(24, file.size, true);
    entry.view.setUint16(28, name.length, true);
    entry.view.setUint32(42, offset, true);
    directory.push(entry.bytes, name);
    offset += 30 + name.length + file.size;
    directorySize += 46 + name.length;
  }
  const end = header(22);
  end.view.setUint32(0, 0x06054b50, true);
  end.view.setUint16(8, files.length, true);
  end.view.setUint16(10, files.length, true);
  end.view.setUint32(12, directorySize, true);
  end.view.setUint32(16, offset, true);
  return new Blob([...parts, ...directory, end.bytes], {type: 'application/zip'});
}
