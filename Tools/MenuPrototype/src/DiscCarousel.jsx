'use client';
import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef } from 'react';
import { gsap } from 'gsap';
import { MENU_MOTION, wrapIndex } from './motion.js';
import blade from '../assets/blade-sculpture.png';

const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const RAD = Math.PI / 180;
function projectPoint(px, py, pose, width, height, perspective) {
  const { x, y, z, rotationX, rotationY, rotationZ, scale } = pose;
  const [cx, cy, cz] = [rotationX, rotationY, rotationZ].map(angle => Math.cos(angle * RAD));
  const [sx, sy, sz] = [rotationX, rotationY, rotationZ].map(angle => Math.sin(angle * RAD));
  px *= scale; py *= scale;
  const rx = cy * px + sy * sx * py, ry = cx * py, rz = -sy * px + cy * sx * py;
  const factor = perspective / (perspective - z - rz);
  return { x: width / 2 + (x + cz * rx - sz * ry) * factor,
    y: height / 2 + (y + sz * rx + cz * ry) * factor };
}
// Project the conservative rectangle with the CSS/GSAP Rz * Ry * Rx order.
function projectedBounds(diameter, pose, width, height, perspective) {
  const corners = [-1, 1].flatMap(a => [-1, 1].map(b => projectPoint(a * diameter / 2, b * diameter / 2, pose, width, height, perspective)));
  return { left: Math.min(...corners.map(p => p.x)), right: Math.max(...corners.map(p => p.x)),
    top: Math.min(...corners.map(p => p.y)), bottom: Math.max(...corners.map(p => p.y)) };
}

function referenceTrace(diameter, pose, size, layer) {
  const start = (-146 + layer * 13) * RAD;
  const sweep = (layer === 0 ? 334 : layer === 1 ? 306 : 246) * RAD;
  const radius = diameter / 2 + Math.min(9, diameter * .022) + layer * 2.2;
  const points = Array.from({ length: 65 }, (_, index) => {
    const angle = start + index / 64 * sweep;
    const irregularity = Math.sin(angle * 3 + layer * 1.7) * .8 + Math.cos(angle * 5 - layer) * .45;
    return projectPoint(Math.cos(angle) * (radius + irregularity), Math.sin(angle) * (radius + irregularity),
      pose, size.width, size.height, size.perspective);
  });
  // Catmull-Rom → cubic curves: continuous ink, without angular brackets or a perfect CAD ring.
  let path = `M${points[0].x.toFixed(2)} ${points[0].y.toFixed(2)}`;
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[Math.max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Math.min(points.length - 1, i + 2)];
    path += ` C${(p1.x + (p2.x - p0.x) / 6).toFixed(2)} ${(p1.y + (p2.y - p0.y) / 6).toFixed(2)}` +
      ` ${(p2.x - (p3.x - p1.x) / 6).toFixed(2)} ${(p2.y - (p3.y - p1.y) / 6).toFixed(2)} ${p2.x.toFixed(2)} ${p2.y.toFixed(2)}`;
  }
  return path;
}
function DiscArt({ type }) {
  if (type === 'fracture') return <div className="disc-blade-art"><img src={blade} alt="" /></div>;
  if (type === 'empty') return <div className="disc-empty-grid" />;
  return <div className={`disc-cover-art ${type}`}>
    <div className="disc-cover-dots" /><div className="cover-moon" />
    <div className="cover-city">{Array.from({ length: 16 }, (_, i) => <i key={i} style={{ '--height': `${22 + (i * 47 % 96)}px`, '--width': `${9 + i * 7 % 15}px` }} />)}</div>
    <div className="cover-horizon" /><div className="cover-print-stripe" />
  </div>;
}

const DiscCarousel = forwardRef(function DiscCarousel({ slots, selection, onSelection, audio, reduced, disabled = false }, ref) {
  const viewport = useRef(null), discs = useRef([]), mark = useRef(null);
  const progress = useRef({ position: selection }), target = useRef(selection);
  const chooseRef = useRef(onSelection); chooseRef.current = onSelection;
  const disabledRef = useRef(disabled); disabledRef.current = disabled;
  const tween = useRef(null), markTween = useRef(null), markTimer = useRef(null), drag = useRef(null);
  const size = useRef({ width: 0, height: 0, diameter: 0, perspective: 1100 });
  const poses = useRef([]);
  const count = slots.length;
  const clearConfirmation = useCallback(() => {
    markTween.current?.kill(); clearTimeout(markTimer.current);
    if (mark.current) { gsap.set(mark.current, { autoAlpha: 0 }); mark.current.dataset.active = 'false'; }
  }, []);
  const resetHover = useCallback(element => {
    const face = element?.querySelector('.disc-face');
    if (face) gsap.to(face, { rotationX: 0, rotationY: 0, z: 0, scale: 1,
      duration: reduced ? 0 : MENU_MOTION.hover.duration, ease: MENU_MOTION.hover.ease, overwrite: true });
  }, [reduced]);
  const draw = useCallback(() => {
    const { width, height, diameter, perspective } = size.current;
    if (!width || !diameter) return;
    const compact = width < 720;
    const radius = compact ? width * .34 : Math.max(0, width / 2 - diameter * .61 - 24);
    const padding = compact ? 18 : 24;
    discs.current.forEach((element, i) => {
      if (!element || i >= count) return;
      const offset = i - progress.current.position;
      const theta = clamp(offset, -2.5, 2.5) * (compact ? .77 : MENU_MOTION.carousel.spacing);
      const sideScale = compact ? 1 - .36 * Math.min(Math.abs(offset), 1) : 1 + Math.sin(theta) * .055;
      const pose = { x: Math.sin(theta) * radius, y: -Math.sin(theta) * Math.min(height * .22, 85),
        z: (Math.cos(theta) - 1) * (compact ? 150 : 230) + Math.sin(theta) * (compact ? 75 : 150),
        rotationX: 10, rotationY: -20 + Math.sin(theta) * 12, rotationZ: -13 + Math.sin(theta) * 4, scale: sideScale };
      let bounds = projectedBounds(diameter, pose, width, height, perspective);
      const fit = Math.min(1, (width - padding * 2) / (bounds.right - bounds.left), (height - padding * 2) / (bounds.bottom - bounds.top));
      if (fit < 1) pose.scale *= fit * .98;
      for (let iteration = 0; iteration < 3; iteration++) {
        bounds = projectedBounds(diameter, pose, width, height, perspective);
        const factor = perspective / (perspective - pose.z);
        pose.x += (Math.max(0, padding - bounds.left) - Math.max(0, bounds.right - width + padding)) / factor;
        pose.y += (Math.max(0, padding - bounds.top) - Math.max(0, bounds.bottom - height + padding)) / factor;
      }
      const visibleRange = compact ? 1.65 : 2.55;
      const opacity = clamp((visibleRange - Math.abs(offset)) / .45, 0, 1);
      gsap.set(element, { xPercent: -50, yPercent: -50, ...pose, opacity });
      element.style.zIndex = `${Math.round(pose.z + 300)}`;
      element.style.pointerEvents = opacity > .2 ? 'auto' : 'none';
      element.dataset.visible = opacity > .02 ? 'true' : 'false';
      poses.current[i] = pose;
    });
  }, [count]);
  const confirm = useCallback(() => {
    clearConfirmation();
    const el = mark.current;
    const pose = poses.current[target.current];
    if (!el || !pose) return;
    el.dataset.active = 'true';
    el.setAttribute('viewBox', `0 0 ${size.current.width} ${size.current.height}`);
    const paths = [...el.querySelectorAll('path')];
    paths.forEach((path, layer) => {
      path.setAttribute('d', referenceTrace(size.current.diameter, pose, size.current, layer));
      const length = path.getTotalLength(); path.style.strokeDasharray = `${length} ${length}`;
      path.style.strokeDashoffset = reduced ? '0' : `${length}`;
    });
    if (reduced) {
      gsap.set(el, { autoAlpha: 1 });
      markTimer.current = setTimeout(clearConfirmation, 700); return;
    }
    const m = MENU_MOTION.confirm;
    markTween.current = gsap.timeline({ onComplete: clearConfirmation })
      .set(el, { autoAlpha: 1 })
      .to(paths, { strokeDashoffset: 0, duration: m.draw, stagger: m.stagger, ease: m.ease }, 0)
      .to(el, { autoAlpha: 0, duration: m.fade, ease: 'power2.in' }, `+=${m.hold}`);
  }, [clearConfirmation, reduced]);
  const move = useCallback((next, feedback = true, confirmAfter = false) => {
    if (disabledRef.current) return;
    next = wrapIndex(next, count);
    clearConfirmation(); discs.current.forEach(resetHover);
    const unchanged = next === target.current && Math.abs(progress.current.position - next) < .01;
    target.current = next; chooseRef.current(next); tween.current?.kill();
    if (feedback) audio.tick(confirmAfter);
    if (reduced || unchanged) { progress.current.position = next; draw(); if (confirmAfter) confirm(); }
    else tween.current = gsap.to(progress.current, { position: next, duration: MENU_MOTION.carousel.duration,
      ease: MENU_MOTION.carousel.ease, onUpdate: draw, onComplete: confirmAfter ? confirm : undefined });
  }, [audio, clearConfirmation, confirm, count, draw, reduced, resetHover]);
  useImperativeHandle(ref, () => ({ confirmSelection: () => move(selection, false, true) }), [move, selection]);
  useEffect(() => { if (target.current !== selection) move(selection, false); }, [selection, move]);
  useEffect(() => {
    const el = viewport.current;
    const measure = () => {
      if (size.current.width !== el.clientWidth || size.current.height !== el.clientHeight) clearConfirmation();
      size.current = { width: el.clientWidth, height: el.clientHeight, diameter: discs.current[0]?.offsetWidth || 0,
        perspective: parseFloat(getComputedStyle(el).perspective) || 1100 };
      draw();
    };
    measure();
    let accumulated = 0, lastWheel = 0, settle;
    const wheel = event => {
      event.preventDefault();
      if (disabledRef.current || count === 1) return;
      const delta = Math.abs(event.deltaY) > Math.abs(event.deltaX) ? event.deltaY : event.deltaX;
      if (performance.now() - lastWheel < MENU_MOTION.carousel.wheelCooldown) return;
      accumulated += delta * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? el.clientHeight : 1);
      clearTimeout(settle); settle = setTimeout(() => { accumulated = 0; }, 160);
      if (Math.abs(accumulated) >= MENU_MOTION.carousel.wheelThreshold) {
        move(target.current + Math.sign(accumulated)); accumulated = 0; lastWheel = performance.now();
      }
    };
    el.addEventListener('wheel', wheel, { passive: false });
    const observer = new ResizeObserver(measure); observer.observe(el);
    return () => { el.removeEventListener('wheel', wheel); observer.disconnect(); clearTimeout(settle); tween.current?.kill(); };
  }, [count, draw, move, clearConfirmation]);
  useEffect(() => () => {
    markTween.current?.kill(); clearTimeout(markTimer.current);
    discs.current.forEach(el => { if (el) gsap.killTweensOf(el.querySelector('.disc-face')); });
  }, []);
  const hover = (event, element) => {
    if (reduced || disabledRef.current || drag.current?.used || event.pointerType !== 'mouse') return;
    const rect = element.getBoundingClientRect();
    const nx = clamp((event.clientX - rect.left) / rect.width * 2 - 1, -1, 1);
    const ny = clamp((event.clientY - rect.top) / rect.height * 2 - 1, -1, 1);
    const m = MENU_MOTION.hover;
    gsap.to(element.querySelector('.disc-face'), { rotationX: -ny * m.maxX, rotationY: nx * m.maxY, z: m.lift,
      scale: 1.012, duration: m.duration, ease: m.ease, overwrite: true });
  };
  const down = event => {
    if (disabledRef.current || (event.pointerType === 'mouse' && event.button !== 0)) return;
    drag.current = { x: event.clientX, y: event.clientY, used: false, id: event.pointerId };
  };
  const dragMove = event => {
    if (!drag.current) return;
    const distance = event.clientX - drag.current.x;
    if (Math.abs(distance) > 65 && Math.abs(distance) > Math.abs(event.clientY - drag.current.y)) {
      move(target.current - Math.sign(distance));
      drag.current.x = event.clientX; drag.current.y = event.clientY; drag.current.used = true;
      if (!viewport.current.hasPointerCapture(event.pointerId)) viewport.current.setPointerCapture(event.pointerId);
    }
  };
  const end = () => {
    const pointer = drag.current;
    if (pointer && viewport.current.hasPointerCapture(pointer.id)) viewport.current.releasePointerCapture(pointer.id);
    if (pointer?.used) setTimeout(() => { drag.current = null; }, 0);
    else drag.current = null;
  };
  return <div className="carousel-window" ref={viewport} tabIndex={0}
    aria-label="光盘存档轮盘：滚轮、拖动或方向键切换，点击确认选择"
    onPointerDown={down} onPointerMove={dragMove} onPointerUp={end} onPointerCancel={end}
    onPointerLeave={() => { if (drag.current && !viewport.current.hasPointerCapture(drag.current.id)) drag.current = null; }}
    onKeyDown={event => {
      if (['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].includes(event.key)) {
        event.preventDefault(); event.stopPropagation(); move(target.current + (['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1));
      } else if (event.key === 'Enter' && event.target === event.currentTarget) {
        event.preventDefault(); event.stopPropagation(); move(target.current, true, true);
      }
    }}>
    <div className="disc-track" role="group" aria-label="本地存档与新建入口">
      {slots.map((slot, i) => <button key={slot.id} ref={element => { discs.current[i] = element; }}
        className={`disc disc-${slot.art}`} data-selected={selection === i} aria-pressed={selection === i}
        tabIndex={selection === i ? 0 : -1} disabled={disabled}
        aria-label={slot.empty ? '新建存档光盘' : `存档 ${i + 1}：${slot.name}`}
        onPointerEnter={event => hover(event, event.currentTarget)} onPointerMove={event => hover(event, event.currentTarget)}
        onPointerLeave={event => resetHover(event.currentTarget)}
        onClick={() => { if (!drag.current?.used) move(i, true, true); }}>
        <div className="disc-face"><div className="disc-label"><DiscArt type={slot.art} /><div className="disc-print">
          <div className="disc-topline"><span>INTERBLADE</span><span>{slot.empty ? 'NEW SAVE' : 'SAVE DATA'}</span></div>
          <h4>{slot.cover[0]}<br />{slot.cover[1]}</h4>
          <span className="disc-subtitle">{slot.empty ? '＋ 新建存档' : slot.name}</span>
          <div className="disc-number"><span>{slot.empty ? 'CREATE' : `SIDE ${String(i + 1).padStart(2, '0')}`}</span><span>{slot.empty ? 'EMPTY' : `CHAPTER ${slot.chapterNumber}`}</span></div>
        </div><div className="disc-specular" /></div><div className="disc-center" /></div>
      </button>)}
    </div>
    {/* A fixed screen-space snapshot of the rim, matching the reference's thin hand-drawn arcs. */}
    <svg className="selection-mark" ref={mark} data-active="false" preserveAspectRatio="none" aria-hidden="true">
      <g fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round">
        <path strokeWidth="1.15" opacity=".88" /><path strokeWidth=".9" opacity=".58" /><path strokeWidth=".8" opacity=".38" />
      </g>
    </svg>
  </div>;
});
export default DiscCarousel;
