import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { gsap } from 'gsap';
import { ArrowLeft, ArrowRight, ArrowUpRight, ArrowUUpLeft, Play, SlidersHorizontal, SignOut, Sword, MouseScroll, Plus } from '@phosphor-icons/react';
import AnimatedContent from './vendor/react-bits/AnimatedContent.jsx';
import SplitText from './vendor/react-bits/SplitText.jsx';
import Space from './Space.jsx';
import DiscCarousel from './DiscCarousel.jsx';
import SaveDetails from './SaveDetails.jsx';
import CreateSaveDialog from './CreateSaveDialog.jsx';
import { wrapIndex } from './motion.js';
import { createSaveRecord, newSaveEntry, readSaves, saveToDisc, writeSaves } from './saves.js';
import { MenuAudio, saveVolumes } from './audio.js';
import sculpture from '../assets/blade-sculpture.png';

const screenReducer = (stack, action) => action.type === 'home' ? ['main'] : action.type === 'back' ? (stack.length > 1 ? stack.slice(0, -1) : stack) : [...stack, action.screen];

function useReducedMotion() {
  const [reduced, setReduced] = useState(() => matchMedia('(prefers-reduced-motion: reduce)').matches);
  useEffect(() => { const media = matchMedia('(prefers-reduced-motion: reduce)'); const update = () => setReduced(media.matches); media.addEventListener('change', update); return () => media.removeEventListener('change', update); }, []);
  return reduced;
}

function Heading({ text, className = '', reduced, tag = 'h2' }) {
  if (reduced) { const Tag = tag; return <Tag className={className}>{text}</Tag>; }
  return <SplitText text={text} tag={tag} className={className} delay={24} duration={.55}
    rootMargin="0px" textAlign="left" from={{ opacity: 0, y: 45 }} to={{ opacity: 1, y: 0 }} />;
}

function Settings({ audio, reduced, back }) {
  const [volumes, setVolumes] = useState(() => ({ ...audio.values }));
  const [status, setStatus] = useState('自动保存');
  const change = (key, value) => {
    const next = { ...volumes, [key]: value }; setVolumes(next);
    audio.set(key, value); audio.tick(); setStatus(saveVolumes(next) ? '已保存' : '仅本次会话');
  };
  const labels = [['master', 'Master Volume', '主音量'], ['music', 'Music', '音乐音量'], ['sfx', 'Sound Effects', '音效音量']];
  return <section className="screen settings-screen" aria-labelledby="settings-title">
    <button className="back-button" onClick={back}><ArrowUUpLeft size={22} /><span>BACK</span></button>
    <div className="settings-title"><Heading text="SOUND." reduced={reduced} /><p id="settings-title">音量设置</p></div>
    <div className="volume-controls">{labels.map(([key, label, chinese]) => <label key={key} className="volume-control">
      <span><b>{label}</b><small>{chinese}</small></span><output htmlFor={`${key}-volume`}>{volumes[key]}</output>
      <input id={`${key}-volume`} name={key} type="range" min="0" max="100" step="1" value={volumes[key]}
        aria-label={chinese} style={{ '--volume': `${volumes[key]}%` }} onChange={event => change(key, Number(event.target.value))} />
    </label>)}</div>
    <div className="sound-wave" aria-hidden="true">{Array.from({ length: 57 }, (_, i) => <i key={i} style={{ '--height': (Math.sin(i * .77) ** 2 * .7 + .2) * (volumes.master / 100) }} />)}</div>
    <p className="settings-caption">让每次出剑，清晰可闻。<span role="status">{status}</span></p>
  </section>;
}

export default function App() {
  const [stack, dispatch] = useReducer(screenReducer, ['main']);
  const screen = stack.at(-1);
  const [selection, setSelection] = useState(0);
  const [saveState, setSaveState] = useState(readSaves);
  const slots = useMemo(() => [...saveState.records.map(saveToDisc), newSaveEntry], [saveState.records]);
  const slot = slots[selection] || newSaveEntry;
  const [creating, setCreating] = useState(false), [direction, setDirection] = useState(1);
  const selectionRef = useRef(0), carousel = useRef(null), createdFrame = useRef(null);
  const choose = useCallback(next => {
    if (next !== selectionRef.current) setDirection(next > selectionRef.current ? 1 : -1);
    selectionRef.current = next; setSelection(next);
  }, []);
  const [loading, setLoading] = useState(false);
  const reduced = useReducedMotion();
  const root = useRef(null), content = useRef(null), flash = useRef(null);
  const lastMenu = useRef('saves');
  const audio = useRef(null); if (!audio.current) audio.current = new MenuAudio();
  const pointer = useRef({ x: 0, y: 0, tx: 0, ty: 0 });
  const transition = useRef(null), launchTimer = useRef(null);

  useEffect(() => {
    const move = event => { pointer.current.tx = event.clientX / innerWidth * 2 - 1; pointer.current.ty = event.clientY / innerHeight * 2 - 1; };
    const reset = () => { pointer.current.tx = 0; pointer.current.ty = 0; };
    const tick = () => {
      const p = pointer.current;
      p.x += (p.tx - p.x) * .08; p.y += (p.ty - p.y) * .08;
      root.current?.style.setProperty('--mx', p.x.toFixed(4)); root.current?.style.setProperty('--my', p.y.toFixed(4));
    };
    if (!reduced) { addEventListener('pointermove', move); document.documentElement.addEventListener('pointerleave', reset); gsap.ticker.add(tick); }
    else { pointer.current = { x: 0, y: 0, tx: 0, ty: 0 }; root.current?.style.setProperty('--mx', '0'); root.current?.style.setProperty('--my', '0'); }
    return () => { removeEventListener('pointermove', move); document.documentElement.removeEventListener('pointerleave', reset); gsap.ticker.remove(tick); };
  }, [reduced]);
  useEffect(() => () => { transition.current?.kill(); clearTimeout(launchTimer.current); cancelAnimationFrame(createdFrame.current); audio.current.dispose(); }, []);

  const navigate = useCallback(action => {
    if (transition.current?.isActive() || loading) return;
    audio.current.tick(true);
    if (reduced) { dispatch(action); return; }
    transition.current = gsap.timeline()
      .fromTo(flash.current, { x: 0, xPercent: -150, skewX: -18 }, { xPercent: 40, duration: .2, ease: 'power2.in' })
      .call(() => dispatch(action))
      .to(flash.current, { xPercent: 240, duration: .28, ease: 'power3.out' });
  }, [loading, reduced]);
  const back = useCallback(() => navigate({ type: 'back' }), [navigate]);
  const home = () => navigate({ type: 'home' });
  const open = name => { lastMenu.current = name; navigate({ type: 'push', screen: name }); };
  const start = useCallback(() => {
    if (loading || transition.current?.isActive()) return;
    carousel.current?.confirmSelection();
    if (slot.empty) { audio.current.tick(true); setCreating(true); return; }
    audio.current.tick(true); setLoading(true);
    launchTimer.current = setTimeout(() => { setLoading(false); navigate({ type: 'push', screen: 'launch' }); }, reduced ? 0 : 650);
  }, [loading, navigate, reduced, slot.empty]);
  const create = name => {
    const record = createSaveRecord(name, saveState.records.length);
    const records = [...saveState.records, record];
    const persisted = writeSaves(records);
    setSaveState({ records, status: persisted ? `已创建「${record.name}」· 已保存` : `已创建「${record.name}」· 仅本次会话` });
    choose(records.length - 1); setCreating(false); audio.current.tick(true);
    createdFrame.current = requestAnimationFrame(() => carousel.current?.confirmSelection());
  };

  useEffect(() => {
    const frame = requestAnimationFrame(() => {
      const target = screen === 'main' ? content.current?.querySelector(`[data-open="${lastMenu.current}"]`) : content.current?.querySelector(screen === 'saves' ? '.carousel-window' : '.back-button, .confirm-button');
      target?.focus({ preventScroll: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [screen]);
  useEffect(() => {
    const keyboard = event => {
      if (creating) return; // Native dialog owns keyboard focus, Enter and Escape.
      if (event.key === 'Escape' && !loading && screen !== 'main') { event.preventDefault(); back(); return; }
      if (screen === 'saves' && !loading) {
        if (event.target.tagName === 'INPUT') return;
        if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') { event.preventDefault(); choose(wrapIndex(selectionRef.current + (event.key === 'ArrowRight' ? 1 : -1), slots.length)); audio.current.tick(); }
      }
      if (screen === 'main' && ['ArrowDown', 'ArrowUp'].includes(event.key)) {
        event.preventDefault(); const items = [...content.current.querySelectorAll('[data-open]')];
        const index = items.indexOf(document.activeElement);
        items[wrapIndex(index + (event.key === 'ArrowDown' ? 1 : -1), items.length)].focus(); audio.current.tick();
      }
    };
    addEventListener('keydown', keyboard); return () => removeEventListener('keydown', keyboard);
  }, [screen, back, loading, creating, choose, slots.length]);
  const actions = [['saves', 'Start Game', '选择存档，进入战场', Play], ['settings', 'Settings', '音量设置', SlidersHorizontal], ['quit', 'Quit Game', '退出菜单', SignOut]];
  const entry = reduced ? <nav className="main-actions" aria-label="主菜单">{actions.map(([id, title, subtitle, Icon], index) => <button key={id} data-open={id} onClick={() => open(id)} className={`menu-action ${index === 0 ? 'primary' : ''}`}><span className="action-number" aria-hidden="true">0{index + 1}</span><span className="action-copy">{title}<small>{subtitle}</small></span><span className="action-icon"><Icon size={24} weight={index === 0 ? 'fill' : 'regular'} /></span></button>)}</nav> : <AnimatedContent distance={18} duration={.5} delay={.18} className="action-entry"><nav className="main-actions" aria-label="主菜单">{actions.map(([id, title, subtitle, Icon], index) => <button key={id} data-open={id} onClick={() => open(id)} className={`menu-action ${index === 0 ? 'primary' : ''}`}><span className="action-number" aria-hidden="true">0{index + 1}</span><span className="action-copy">{title}<small>{subtitle}</small></span><span className="action-icon"><Icon size={24} weight={index === 0 ? 'fill' : 'regular'} /></span></button>)}</nav></AnimatedContent>;

  return <div className="app" ref={root} data-screen={screen} style={{ '--hero-image': `url(${sculpture})` }}>
    <Space pointer={pointer} reduced={reduced} /><div className="paper-grain" aria-hidden="true" />
    <div className="screen-flash" ref={flash} aria-hidden="true" />
    <div className="shell">
      <header className="top-rail"><button className="brand" aria-label="刃间 INTERBLADE，返回主菜单" onClick={home}><Sword size={30} weight="duotone" /><span>刃间 <b>INTERBLADE</b></span></button><span className="header-label">一瞬之间，锋芒尽现。</span></header>
      <div className="visual-stage" aria-hidden="true"><div className="ghost-word">STRIKE</div><div className="halftone-field" /><div className="orbit" />
        <div className="sculpture"><img src={sculpture} alt="" width="1254" height="1254" fetchPriority="high" /><div className="sculpture-ink" /></div>
        <div className="foreground-stripe" /><div className="foreground-bracket" /><div className="art-caption">刃 / 光 / 瞬间</div>
      </div>
      <main ref={content} key={screen}>
        {screen === 'main' && <section className="screen main-screen" aria-labelledby="game-title">
          <div className="main-title"><Heading text="刃间" tag="h1" reduced={reduced} /><p id="game-title">INTERBLADE<span><ArrowUpRight size={30} /></span></p></div>
          <p className="main-intro">把下一次交锋，交给自己。</p>{entry}
        </section>}
        {screen === 'saves' && <section className="screen saves-screen" aria-labelledby="saves-title">
          <div className="screen-heading"><button className="back-button" onClick={back}><ArrowUUpLeft size={22} /><span>BACK</span></button><div className="save-heading-actions"><span className="sample-label" role="status">{saveState.status}</span><button className="new-save-button" disabled={loading} onClick={() => { audio.current.tick(true); setCreating(true); }}><Plus size={16} />新建存档</button></div></div>
          <div className="archive-title"><Heading text="SELECT YOUR STORY." reduced={reduced} /><p id="saves-title">选择存档</p></div>
          <DiscCarousel ref={carousel} slots={slots} selection={selection} onSelection={choose} audio={audio.current} reduced={reduced} disabled={loading || creating} />
          <div className="archive-bottom"><SaveDetails slot={slot} direction={direction} reduced={reduced} />
            <div className="archive-controls"><div className="carousel-controls"><button aria-label="上一个存档" className="arrow-button" disabled={loading || slots.length === 1} onClick={() => { choose(wrapIndex(selectionRef.current - 1, slots.length)); audio.current.tick(); }}><ArrowLeft size={20} /></button><span aria-live="polite"><b key={selection} className="slot-counter">{String(selection + 1).padStart(2, '0')}</b> / {String(slots.length).padStart(2, '0')}</span><button aria-label="下一个存档" className="arrow-button" disabled={loading || slots.length === 1} onClick={() => { choose(wrapIndex(selectionRef.current + 1, slots.length)); audio.current.tick(); }}><ArrowRight size={20} /></button></div><button className="confirm-button" onClick={start} disabled={loading}><span key={`${slot.id}-${loading}`} className="confirm-label">{loading ? 'Loading…' : slot.empty ? '新建存档' : 'Continue'}</span><ArrowUpRight size={24} /></button><p className="scroll-hint"><MouseScroll size={20} />滚轮浏览 · 点击选择<span>← → / ENTER</span></p></div>
          </div>
        </section>}
        {screen === 'settings' && <Settings audio={audio.current} reduced={reduced} back={back} />}
        {screen === 'quit' && <section className="screen end-screen" aria-labelledby="quit-title"><Heading text="UNTIL NEXT TIME." reduced={reduced} /><p id="quit-title">下次，再见锋芒。</p><p className="end-note">现在可以关闭这个菜单页面。</p><button className="confirm-button" onClick={home}>Return to Menu<ArrowUpRight size={24} /></button></section>}
        {screen === 'launch' && <section className="screen end-screen launch-screen" aria-labelledby="launch-title"><span className="slot-kicker">SAVE {String(selection + 1).padStart(2, '0')}</span><Heading text="READY TO STRIKE." reduced={reduced} /><p id="launch-title">已选择：{slot.name}</p><p className="end-note">菜单交互演示完成。此网页尚未连接 Unity 战斗场景。</p><button className="confirm-button" onClick={home}>Return to Menu<ArrowUpRight size={24} /></button></section>}
      </main>
      <footer className="bottom-rail"><span>刃间 / 第三人称动作游戏</span><div className="input-guide"><span><kbd>↑</kbd><kbd>↓</kbd> 选择</span><span><kbd>ENTER</kbd> 确认</span><span><kbd>ESC</kbd> 返回</span></div></footer>
    </div>
    {creating && <CreateSaveDialog number={saveState.records.length + 1} onCreate={create} onCancel={() => setCreating(false)} />}
  </div>;
}
