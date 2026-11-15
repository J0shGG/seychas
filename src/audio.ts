import type { AmbientKind } from './types'

let ctx: AudioContext | null = null
let master: GainNode | null = null
let source: AudioBufferSourceNode | null = null
let crackleTimer: number | null = null

function audioContext(): AudioContext {
  if (!ctx) ctx = new AudioContext()
  return ctx
}

function noiseBuffer(c: AudioContext, seconds = 2, brown = false): AudioBuffer {
  const length = Math.floor(c.sampleRate * seconds)
  const buffer = c.createBuffer(1, length, c.sampleRate)
  const data = buffer.getChannelData(0)
  let last = 0
  for (let i = 0; i < length; i++) {
    const white = Math.random() * 2 - 1
    if (brown) {
      last = (last + 0.02 * white) / 1.02
      data[i] = last * 3.5
    } else {
      data[i] = white
    }
  }
  return buffer
}

function stopAmbient() {
  if (crackleTimer !== null) {
    window.clearInterval(crackleTimer)
    crackleTimer = null
  }
  if (source) {
    try {
      source.stop()
    } catch {
      /* already stopped */
    }
    source.disconnect()
    source = null
  }
  if (master) {
    master.disconnect()
    master = null
  }
}

export async function setAmbient(kind: AmbientKind): Promise<void> {
  stopAmbient()
  if (kind === 'off') return

  const c = audioContext()
  if (c.state === 'suspended') await c.resume()

  master = c.createGain()
  master.gain.value = 0.0001
  master.connect(c.destination)

  const filter = c.createBiquadFilter()
  const src = c.createBufferSource()
  src.loop = true

  if (kind === 'rain') {
    src.buffer = noiseBuffer(c, 3, false)
    filter.type = 'lowpass'
    filter.frequency.value = 920
    filter.Q.value = 0.7
    master.gain.exponentialRampToValueAtTime(0.045, c.currentTime + 1.2)
  } else if (kind === 'fire') {
    src.buffer = noiseBuffer(c, 2, true)
    filter.type = 'lowpass'
    filter.frequency.value = 380
    filter.Q.value = 0.4
    master.gain.exponentialRampToValueAtTime(0.055, c.currentTime + 1.2)
    crackleTimer = window.setInterval(() => playCrackle(c), 1400 + Math.random() * 1800)
  } else {
    src.buffer = noiseBuffer(c, 3, true)
    filter.type = 'bandpass'
    filter.frequency.value = 420
    filter.Q.value = 0.5
    master.gain.exponentialRampToValueAtTime(0.035, c.currentTime + 1.2)
  }

  src.connect(filter)
  filter.connect(master)
  src.start()
  source = src
}

function playCrackle(c: AudioContext) {
  if (!master) return
  const o = c.createOscillator()
  const g = c.createGain()
  const f = c.createBiquadFilter()
  o.type = 'sawtooth'
  o.frequency.value = 90 + Math.random() * 140
  f.type = 'highpass'
  f.frequency.value = 700
  g.gain.value = 0.0001
  g.gain.exponentialRampToValueAtTime(0.04, c.currentTime + 0.01)
  g.gain.exponentialRampToValueAtTime(0.0001, c.currentTime + 0.08 + Math.random() * 0.08)
  o.connect(f)
  f.connect(g)
  g.connect(master)
  o.start()
  o.stop(c.currentTime + 0.18)
}

export async function playChime(): Promise<void> {
  const c = audioContext()
  if (c.state === 'suspended') await c.resume()
  const now = c.currentTime
  const notes = [523.25, 659.25, 783.99]
  notes.forEach((freq, i) => {
    const o = c.createOscillator()
    const g = c.createGain()
    o.type = 'sine'
    o.frequency.value = freq
    g.gain.value = 0.0001
    const t = now + i * 0.12
    g.gain.exponentialRampToValueAtTime(0.08, t + 0.02)
    g.gain.exponentialRampToValueAtTime(0.0001, t + 1.4)
    o.connect(g)
    g.connect(c.destination)
    o.start(t)
    o.stop(t + 1.5)
  })
}

export function unlockAudio(): void {
  const c = audioContext()
  if (c.state === 'suspended') void c.resume()
}
