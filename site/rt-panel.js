// The live timers of the RunnerTray panel in the hero: each running row counts up from its written
// time, once a second, as the app does. The frame gets the kit's pause button (ui.pausable); under
// reduced motion the timers stand as written.
import { pausable, reduced } from './assets/ui/ui.js';

const frame = document.querySelector('.hero .app-frame');
const timers = frame ? [...frame.querySelectorAll('[data-rt-start]')] : [];

if (timers.length && !reduced()) {
  const start = performance.now();
  const fmt = s => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  let shift = 0;
  let pausedAt = null;
  let id = null;
  const tick = () => {
    const gone = Math.floor((performance.now() - start - shift) / 1000);
    for (const t of timers) t.textContent = fmt(Number(t.dataset.rtStart) + gone);
  };
  const run = () => { if (id === null) id = setInterval(tick, 1000); };
  const ctl = {
    pause() { if (id !== null) { clearInterval(id); id = null; pausedAt = performance.now(); } },
    resume() { if (pausedAt !== null) { shift += performance.now() - pausedAt; pausedAt = null; } run(); },
  };
  run();
  pausable(frame, ctl);
}
