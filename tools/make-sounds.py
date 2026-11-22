"""Synthesises the wallet's two chimes as WAV files. Original sounds: plain sine tones, no samples."""
import math
import struct
import sys
import wave

RATE = 44100


def note(freq, start, length, volume, total):
    """One bell-like tone: the fundamental plus a quiet octave, soft attack, exponential fade."""
    out = [0.0] * total
    first = int(start * RATE)
    for i in range(int(length * RATE)):
        t = i / RATE
        attack = min(1.0, t / 0.008)
        fade = math.exp(-t * 7.0)
        tail = min(1.0, (length - t) / 0.03)
        s = math.sin(2 * math.pi * freq * t) + 0.25 * math.sin(2 * math.pi * freq * 2 * t)
        if first + i < total:
            out[first + i] = s * attack * fade * tail * volume
    return out


def mix(notes, seconds):
    total = int(seconds * RATE)
    out = [0.0] * total
    for freq, start, length, volume in notes:
        for i, s in enumerate(note(freq, start, length, volume, total)):
            out[i] += s
    peak = max(abs(s) for s in out) or 1.0
    return [s / peak * 0.55 for s in out]


def save(path, samples):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s * 32767)) for s in samples))


out_dir = sys.argv[1]
# Payment received: two rising notes (A5 then E6).
save(out_dir + "/receive.wav", mix([(880.0, 0.0, 0.45, 1.0), (1318.5, 0.12, 0.5, 0.9)], 0.65))
# Send confirmed: one short, lower note (E5).
save(out_dir + "/send.wav", mix([(659.3, 0.0, 0.35, 1.0)], 0.38))
