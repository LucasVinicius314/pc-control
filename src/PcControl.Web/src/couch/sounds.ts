// The small sounds of the controller UI: a tick when the focus moves, a chime when something is chosen.
// They are made on the fly with the Web Audio API (no sound files), short and quiet.

export type Sound = "move" | "accept" | "back" | "tab" | "bump";

interface Note {
  /** Frequency at the start, and at the end (a short slide), in hertz. */
  from: number;
  to?: number;
  /** When it starts after the sound is triggered, and how long it lasts, in seconds. */
  at: number;
  length: number;
  type: OscillatorType;
  /** Loudness, 0 to 1, before the master volume. */
  gain: number;
}

const NOTES: Record<Sound, Note[]> = {
  move: [{ from: 1250, to: 1000, at: 0, length: 0.03, type: "triangle", gain: 0.5 }],
  accept: [
    { from: 660, at: 0, length: 0.07, type: "sine", gain: 0.7 },
    { from: 990, at: 0.06, length: 0.11, type: "sine", gain: 0.7 },
  ],
  back: [{ from: 520, to: 340, at: 0, length: 0.1, type: "sine", gain: 0.6 }],
  tab: [
    { from: 700, at: 0, length: 0.04, type: "triangle", gain: 0.5 },
    { from: 1050, at: 0.04, length: 0.05, type: "triangle", gain: 0.5 },
  ],
  bump: [{ from: 200, to: 150, at: 0, length: 0.05, type: "sine", gain: 0.5 }],
};

const MASTER = 0.07;

export class Sounds {
  private context: AudioContext | null = null;

  constructor(private readonly enabled: boolean) {}

  play(sound: Sound): void {
    if (!this.enabled) return;
    try {
      const context = (this.context ??= new AudioContext());
      if (context.state === "suspended") void context.resume();

      const start = context.currentTime;
      for (const note of NOTES[sound]) {
        const oscillator = context.createOscillator();
        const volume = context.createGain();
        const begin = start + note.at;
        const end = begin + note.length;

        oscillator.type = note.type;
        oscillator.frequency.setValueAtTime(note.from, begin);
        if (note.to !== undefined) oscillator.frequency.exponentialRampToValueAtTime(note.to, end);

        // A quick rise and an exponential fade, so there is no click at either end.
        volume.gain.setValueAtTime(0.0001, begin);
        volume.gain.exponentialRampToValueAtTime(MASTER * note.gain, begin + 0.006);
        volume.gain.exponentialRampToValueAtTime(0.0001, end);

        oscillator.connect(volume).connect(context.destination);
        oscillator.start(begin);
        oscillator.stop(end + 0.02);
      }
    } catch {
      // No audio here (or it is blocked); the UI works the same without it.
    }
  }
}
