// Original menu sounds, synthesised with the Web Audio API. No audio files, nothing borrowed:
// a short square-wave tick for the cursor, a two-note chime to confirm, a soft drop to go back,
// a low buzz for "can't do that", and an original victory jingle for a confirmed booking.

let ctx: AudioContext | null = null;
let enabled = false;

const KEY = "byteclub.sound";
try { enabled = localStorage.getItem(KEY) === "on"; } catch { /* private mode: stay muted */ }

export const soundOn = () => enabled;

export function setSound(on: boolean) {
  enabled = on;
  try { localStorage.setItem(KEY, on ? "on" : "off"); } catch { /* ignore */ }
  if (on) audio();
}

function audio(): AudioContext {
  ctx ??= new AudioContext();
  if (ctx.state === "suspended") void ctx.resume();
  return ctx;
}

function tone(freq: number, start: number, dur: number, type: OscillatorType, vol: number, slideTo?: number) {
  const a = audio();
  const osc = a.createOscillator();
  const gain = a.createGain();
  const t = a.currentTime + start;
  osc.type = type;
  osc.frequency.setValueAtTime(freq, t);
  if (slideTo) osc.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
  gain.gain.setValueAtTime(0, t);
  gain.gain.linearRampToValueAtTime(vol, t + 0.004);
  gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  osc.connect(gain).connect(a.destination);
  osc.start(t);
  osc.stop(t + dur + 0.02);
}

export const sfx = {
  move() { if (enabled) tone(2093, 0, 0.035, "square", 0.05); },
  confirm() {
    if (!enabled) return;
    tone(1568, 0, 0.06, "triangle", 0.18);
    tone(2349, 0.055, 0.12, "triangle", 0.16);
  },
  back() { if (enabled) tone(880, 0, 0.09, "triangle", 0.16, 520); },
  error() { if (enabled) tone(98, 0, 0.22, "square", 0.08, 82); },
  victory() {
    if (!enabled) return;
    // original: a rising C major arpeggio, a step up, and a held top note
    const notes: [number, number, number][] = [
      [392.0, 0, 0.12], [523.25, 0.12, 0.12], [659.25, 0.24, 0.12], [783.99, 0.36, 0.2],
      [698.46, 0.6, 0.12], [783.99, 0.72, 0.12], [1046.5, 0.84, 0.7],
    ];
    for (const [f, s, d] of notes) {
      tone(f, s, d, "square", 0.06);
      tone(f / 2, s, d, "triangle", 0.12);
    }
  },
};
