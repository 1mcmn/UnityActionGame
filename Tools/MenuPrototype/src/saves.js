const KEY = 'interblade-menu-saves-v1';
const THEMES = ['rain', 'fracture', 'light'];
const COVERS = [['FIRST', 'STRIKE'], ['BLADE', 'DIARY'], ['NEW', 'DAWN']];
export const newSaveEntry = Object.freeze({ id: 'new-save', name: '新的故事', cover: ['YOUR', 'STORY'], art: 'empty', empty: true });
export function readSaves() {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return { records: [], status: '浏览器本地存档' };
    const data = JSON.parse(raw);
    if (data.version !== 1 || !Array.isArray(data.records)) throw new Error('Invalid saves');
    const ids = new Set();
    const records = data.records.filter(record => {
      if (!record || typeof record.id !== 'string' || ids.has(record.id) ||
          typeof record.name !== 'string' || !record.name.trim() || record.name.length > 24 ||
          !THEMES.includes(record.art) || typeof record.createdAt !== 'string' ||
          !Number.isFinite(Date.parse(record.createdAt))) return false;
      ids.add(record.id); return true;
    });
    return { records, status: records.length === data.records.length ? '浏览器本地存档' : '部分记录无法读取' };
  } catch { return { records: [], status: '存储不可用 · 仅本次会话' }; }
}
export function createSaveRecord(name, index) {
  return { id: crypto.randomUUID(), name: name.trim().slice(0, 24), art: THEMES[index % THEMES.length], createdAt: new Date().toISOString() };
}
export function writeSaves(records) {
  try { localStorage.setItem(KEY, JSON.stringify({ version: 1, records })); return true; }
  catch { return false; }
}
export function saveToDisc(record) {
  return { ...record, cover: COVERS[THEMES.indexOf(record.art)], chapter: '初始战场', chapterNumber: '01',
    time: '00:00:00', progress: '0%', empty: false };
}
