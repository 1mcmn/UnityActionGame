// Engine-neutral tuning. See UNITY_MOTION.md for the equivalent client hierarchy.
export const MENU_MOTION = Object.freeze({
  carousel: { duration: .78, ease: 'power3.out', spacing: .63, wheelThreshold: 24, wheelCooldown: 170 },
  hover: { duration: .28, ease: 'power2.out', maxX: 5, maxY: 6, lift: 8 },
  // Reference: fine, imperfect elliptical traces drawn along the disc rim.
  confirm: { draw: .56, stagger: .13, hold: .24, fade: .18, ease: 'none' },
  details: { exit: .12, enter: .38, stagger: .045, distance: 16, ease: 'power3.out' },
});
export const wrapIndex = (value, count) => count > 0 ? ((value % count) + count) % count : 0;
