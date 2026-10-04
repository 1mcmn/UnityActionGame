import { useLayoutEffect, useRef, useState } from 'react';
import { gsap } from 'gsap';
import { MENU_MOTION } from './motion.js';
export default function SaveDetails({ slot, direction, reduced }) {
  const [displayed, setDisplayed] = useState(slot);
  const root = useRef(null), animation = useRef(null), requested = useRef(slot);
  const travel = useRef(direction); travel.current = direction; requested.current = slot;
  useLayoutEffect(() => {
    if (displayed.id === slot.id) {
      // A quick reverse can return to the displayed item during its exit.
      // Restore it rather than leaving cancelled text half-transparent.
      if (root.current.dataset.motionState === 'exit') {
        animation.current?.kill();
        const nodes = root.current.querySelectorAll('.detail-line');
        if (reduced) { gsap.set(nodes, { autoAlpha: 1, y: 0 }); root.current.dataset.motionState = 'idle'; }
        else {
          root.current.dataset.motionState = 'enter';
          animation.current = gsap.to(nodes, { autoAlpha: 1, y: 0, duration: MENU_MOTION.details.enter,
            ease: MENU_MOTION.details.ease, onComplete: () => { root.current.dataset.motionState = 'idle'; } });
        }
        return () => animation.current?.kill();
      }
      return;
    }
    animation.current?.kill();
    if (reduced) { setDisplayed(slot); return; }
    root.current.dataset.motionState = 'exit';
    animation.current = gsap.to(root.current.querySelectorAll('.detail-line'), {
      autoAlpha: 0, y: -travel.current * 10, duration: MENU_MOTION.details.exit, stagger: .018, ease: 'power2.in',
      onComplete: () => setDisplayed(requested.current),
    });
    return () => animation.current?.kill();
  }, [slot, reduced, displayed.id]);
  useLayoutEffect(() => {
    animation.current?.kill();
    const nodes = root.current.querySelectorAll('.detail-line');
    if (reduced) { gsap.set(nodes, { autoAlpha: 1, y: 0 }); root.current.dataset.motionState = 'idle'; return; }
    const m = MENU_MOTION.details;
    root.current.dataset.motionState = 'enter';
    animation.current = gsap.fromTo(nodes, { autoAlpha: 0, y: travel.current * m.distance },
      { autoAlpha: 1, y: 0, duration: m.enter, stagger: m.stagger, ease: m.ease,
        onComplete: () => { root.current.dataset.motionState = 'idle'; } });
    return () => animation.current?.kill();
  }, [displayed, reduced]);
  return <div className="slot-detail" ref={root} aria-live="polite" aria-atomic="true" data-slot-id={displayed.id}>
    <span className="slot-kicker detail-line">{displayed.empty ? 'NEW SAVE' : `CHAPTER ${displayed.chapterNumber}`}</span>
    <h3 className="detail-line">{displayed.name}</h3>
    <p className="detail-line">{displayed.empty ? '从空白开始，写下你的第一场战斗。' : displayed.chapter}</p>
    <div className="slot-stats detail-line"><span>{displayed.empty ? '00:00:00' : displayed.time}<small>PLAY TIME</small></span>
      <span>{displayed.empty ? '0%' : displayed.progress}<small>PROGRESS</small></span></div>
  </div>;
}
