// Menu sounds.
//
// SOUND_SET picks where they come from:
//   "ff7"      - Final Fantasy VII menu clips in public/sfx (Square Enix's audio, Nicolas's choice;
//                kept out of git, copied in by deploy/copy-sfx.sh)
//   "original" - our own synthesised sounds below. Switch to this if the clips ever have to go.
// If a clip fails to load, that one sound falls back to the original synth automatically.
export const SOUND_SET: "ff7" | "original" = "ff7";

type Sfx = "move" | "confirm" | "back" | "error" | "victory";

let ctx: AudioContext | null = null;
let enabled = true;
const buffers = new Map<Sfx, AudioBuffer | null>();

const KEY = "byteclub.sound";
try { enabled = localStorage.getItem(KEY) !== "off"; } catch { /* private mode: default on */ }

export const soundOn = () => enabled;

export function setSound(on: boolean) {
  enabled = on;
  try { localStorage.setItem(KEY, on ? "on" : "off"); } catch { /* ignore */ }
  if (on) audio();
}

// Browsers only allow audio after the visitor interacts; the first tap or key wakes it up.
function wake() { if (enabled) audio(); }
window.addEventListener("pointerdown", wake, { once: true, capture: true });
window.addEventListener("keydown", wake, { once: true, capture: true });

function audio(): AudioContext {
  if (!ctx) {
    ctx = new AudioContext();
    if (SOUND_SET === "ff7") void preload(ctx);
  }
  if (ctx.state === "suspended") void ctx.resume();
  return ctx;
}

async function preload(a: AudioContext) {
  const names: Sfx[] = ["move", "confirm", "back", "error", "victory"];
  await Promise.all(names.map(async (n) => {
    try {
      const res = await fetch(`/sfx/${n}.mp3`);
      if (!res.ok) throw new Error(String(res.status));
      buffers.set(n, await a.decodeAudioData(await res.arrayBuffer()));
    } catch {
      buffers.set(n, null); // missing clip: that sound uses the synth instead
    }
  }));
}

function playClip(name: Sfx, volume = 0.9): boolean {
  const buf = buffers.get(name);
  if (!buf || !ctx) return false;
  const src = ctx.createBufferSource();
  const gain = ctx.createGain();
  gain.gain.value = volume;
  src.buffer = buf;
  src.connect(gain).connect(ctx.destination);
  src.start();
  return true;
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

// Our own sounds: tick, two-note chime, soft drop, low buzz, and an original arpeggio jingle.
const synth: Record<Sfx, () => void> = {
  move: () => tone(2093, 0, 0.035, "square", 0.05),
  confirm: () => { tone(1568, 0, 0.06, "triangle", 0.18); tone(2349, 0.055, 0.12, "triangle", 0.16); },
  back: () => tone(880, 0, 0.09, "triangle", 0.16, 520),
  error: () => tone(98, 0, 0.22, "square", 0.08, 82),
  victory: () => {
    const notes: [number, number, number][] = [
      [392.0, 0, 0.12], [523.25, 0.12, 0.12], [659.25, 0.24, 0.12], [783.99, 0.36, 0.2],
      [698.46, 0.6, 0.12], [783.99, 0.72, 0.12], [1046.5, 0.84, 0.7],
    ];
    for (const [f, s, d] of notes) { tone(f, s, d, "square", 0.06); tone(f / 2, s, d, "triangle", 0.12); }
  },
};

function play(name: Sfx) {
  if (!enabled) return;
  audio();
  if (SOUND_SET === "ff7" && playClip(name)) return;
  synth[name]();
}

export const sfx = {
  move: () => play("move"),
  confirm: () => play("confirm"),
  back: () => play("back"),
  error: () => play("error"),
  victory: () => play("victory"),
};
