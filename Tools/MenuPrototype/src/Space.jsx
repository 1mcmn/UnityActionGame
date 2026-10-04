'use client';
import { memo, useEffect, useRef } from 'react';

export default memo(function Space({ pointer, reduced }) {
  const ref = useRef(null);
  useEffect(() => {
    const canvas = ref.current;
    const ctx = canvas.getContext('2d');
    let width = 0, height = 0, frame, previous = 0;
    const resize = () => {
      width = innerWidth; height = innerHeight;
      const ratio = Math.min(devicePixelRatio || 1, 2);
      canvas.width = width * ratio; canvas.height = height * ratio;
      ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
      draw();
    };
    function draw() {
      ctx.clearRect(0, 0, width, height);
      const px = reduced ? 0 : pointer.current.x, py = reduced ? 0 : pointer.current.y;
      const vanishing = width * .65 + px * 13;
      const horizon = height * .61 + py * 7;
      const floor = height - horizon;
      const col = width / 13;
      for (let row = 1; row <= 17; row++) {
        const depth = Math.pow(row / 17, 2.4);
        const y = horizon + depth * floor;
        ctx.strokeStyle = `rgba(123,139,122,${.025 + depth * .19})`;
        ctx.lineWidth = .75;
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(width, y); ctx.stroke();
        for (let column = -24; column <= 24; column++) {
          const x = vanishing + column * col * depth * 1.4;
          if (x < 0 || x > width) continue;
          ctx.fillStyle = `rgba(106,124,103,${.04 + depth * .21})`;
          ctx.beginPath(); ctx.arc(x, y, .7 + depth * .7, 0, Math.PI * 2); ctx.fill();
        }
      }
      for (let column = -24; column <= 24; column++) {
        const endX = vanishing + column * col * 1.4;
        ctx.strokeStyle = 'rgba(124,140,122,.15)'; ctx.lineWidth = .75;
        ctx.beginPath(); ctx.moveTo(vanishing + column * col * .003, horizon); ctx.lineTo(endX, height); ctx.stroke();
      }
      // Fixed seed: connected, sparse branching geometry, rather than per-frame random noise.
      const origins = [[.48,.65], [.82,.67], [.91,.68]];
      for (let branch = 0; branch < origins.length; branch++) {
        let x = width * origins[branch][0] - px * 6, y = height * origins[branch][1];
        ctx.strokeStyle = 'rgba(111,137,105,.28)'; ctx.fillStyle = '#9aa992';
        for (let n = 0; n < 5; n++) {
          const nx = x + Math.sin(n * 4.7 + branch * 7) * 45;
          const ny = y - (n === 0 ? 48 : 25 + n * 7);
          ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(nx, ny); ctx.stroke();
          ctx.beginPath(); ctx.arc(x, y, 1.7, 0, Math.PI * 2); ctx.fill();
          x = nx; y = ny;
        }
        ctx.beginPath(); ctx.arc(x, y, 1.8, 0, Math.PI * 2); ctx.fill();
      }
    }
    const tick = time => {
      if (time - previous > 32 && !document.hidden) { draw(); previous = time; }
      frame = requestAnimationFrame(tick);
    };
    resize(); addEventListener('resize', resize);
    if (!reduced) frame = requestAnimationFrame(tick);
    return () => { cancelAnimationFrame(frame); removeEventListener('resize', resize); };
  }, [pointer, reduced]);
  return <canvas className="space" ref={ref} aria-hidden="true" />;
});
