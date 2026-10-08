// Inspect the ZIP directory without extracting files or inflating untrusted data.
// Reports are stored as attachments; this is format validation, not a malware scanner.
export function isXlsx(content: Buffer): boolean {
  if (content.length < 22 || content.readUInt32LE(0) !== 0x04034b50) return false;
  let end = content.length - 22;
  const minimum = Math.max(0, end - 65535);
  while (end >= minimum && content.readUInt32LE(end) !== 0x06054b50) end--;
  if (end < minimum || end + 22 + content.readUInt16LE(end + 20) !== content.length) return false;
  if (content.readUInt16LE(end + 4) || content.readUInt16LE(end + 6)) return false;
  const count = content.readUInt16LE(end + 10);
  if (!count || count > 5000 || count !== content.readUInt16LE(end + 8)) return false;
  const directorySize = content.readUInt32LE(end + 12);
  let cursor = content.readUInt32LE(end + 16);
  const directoryStart = cursor;
  if (cursor + directorySize !== end) return false;
  const names = new Set<string>();
  let expanded = 0;
  for (let index = 0; index < count; index++) {
    if (cursor + 46 > end || content.readUInt32LE(cursor) !== 0x02014b50) return false;
    const flags = content.readUInt16LE(cursor + 8);
    const method = content.readUInt16LE(cursor + 10);
    const compressed = content.readUInt32LE(cursor + 20);
    expanded += content.readUInt32LE(cursor + 24);
    const nameSize = content.readUInt16LE(cursor + 28);
    const next = cursor + 46 + nameSize + content.readUInt16LE(cursor + 30) + content.readUInt16LE(cursor + 32);
    if (next > end || flags & 1 || ![0, 8].includes(method) || expanded > 100_000_000) return false;
    const name = content.toString('utf8', cursor + 46, cursor + 46 + nameSize);
    if (names.has(name) || name.startsWith('/') || name.includes('\\') || name.includes('\0') || name.split('/').includes('..') || /vbaproject\.bin$/i.test(name)) return false;
    const local = content.readUInt32LE(cursor + 42);
    if (local + 30 > directoryStart || content.readUInt32LE(local) !== 0x04034b50) return false;
    const localNameSize = content.readUInt16LE(local + 26);
    const dataStart = local + 30 + localNameSize + content.readUInt16LE(local + 28);
    if (dataStart + compressed > directoryStart || content.toString('utf8', local + 30, local + 30 + localNameSize) !== name) return false;
    names.add(name);
    cursor = next;
  }
  return cursor === end && ['[Content_Types].xml', '_rels/.rels', 'xl/workbook.xml'].every((name) => names.has(name));
}
