export const defaultVolumes = { master: 60, music: 35, sfx: 55 };
const STORAGE = 'interblade-menu-audio-v1';

export function readVolumes() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE));
    return Object.fromEntries(Object.entries(defaultVolumes).map(([key, fallback]) =>
      [key, Number.isFinite(saved?.[key]) ? Math.min(100, Math.max(0, saved[key])) : fallback]));
  } catch { return { ...defaultVolumes }; }
}

export function saveVolumes(values) {
  try { localStorage.setItem(STORAGE, JSON.stringify(values)); return true; }
  catch { return false; }
}

// Local synthesized preview audio. No external audio download or autoplay.
export class MenuAudio {
  constructor() { this.values = readVolumes(); this.context = null; this.timer = null; this.lastTick = 0; }
  async activate() {
    try {
      if (!this.context) {
        const Audio = window.AudioContext || window.webkitAudioContext;
        if (!Audio) return false;
        this.context = new Audio();
        this.master = this.context.createGain();
        this.music = this.context.createGain();
        this.sfx = this.context.createGain();
        this.master.connect(this.context.destination);
        this.music.connect(this.master); this.sfx.connect(this.master);
        this.apply();
        let note = 0;
        const notes = [146.83, 220, 174.61, 196, 146.83, 261.63, 220, 164.81];
        this.timer = setInterval(() => {
          if (!document.hidden && this.context.state === 'running')
            this.tone(notes[note++ % notes.length], 1.7, this.music, 'sine', .028);
        }, 1100);
      }
      if (this.context.state === 'suspended') await this.context.resume();
      return true;
    } catch { return false; }
  }
  apply() {
    if (!this.context) return;
    const now = this.context.currentTime;
    for (const key of ['master', 'music', 'sfx']) this[key].gain.setTargetAtTime(this.values[key] / 100, now, .025);
  }
  set(key, value) { this.values[key] = value; this.apply(); }
  tone(frequency, duration, bus, type = 'sine', amplitude = .07) {
    if (!this.context || this.context.state !== 'running') return;
    const oscillator = this.context.createOscillator();
    const gain = this.context.createGain();
    oscillator.type = type; oscillator.frequency.value = frequency;
    const now = this.context.currentTime;
    gain.gain.setValueAtTime(0, now);
    gain.gain.linearRampToValueAtTime(amplitude, now + .012);
    gain.gain.exponentialRampToValueAtTime(.0001, now + duration);
    oscillator.connect(gain); gain.connect(bus);
    oscillator.start(); oscillator.stop(now + duration + .02);
    oscillator.onended = () => { oscillator.disconnect(); gain.disconnect(); };
  }
  async tick(confirm = false) {
    const now = performance.now();
    if (!confirm && now - this.lastTick < 75) return;
    this.lastTick = now;
    if (!await this.activate()) return;
    this.tone(confirm ? 660 : 440, confirm ? .18 : .07, this.sfx, 'triangle', .045);
    if (confirm) this.tone(990, .13, this.sfx, 'sine', .02);
  }
  dispose() { clearInterval(this.timer); this.timer = null; this.context?.close(); }
}
