"""
Procedural synthesis of original placeholder audio for ChronoTravelers.
Everything here is generated from scratch (sine/noise oscillators + simple
filters/envelopes) - no samples, no copyrighted material. "Inspired by"
means matching a mood/structure (theremin-style sci-fi pad for the title
theme, a rising-shimmer chorus for the time-travel cue), not reproducing
any actual recording.

Requires: numpy, scipy (`pip install numpy scipy`).
Outputs 16-bit mono PCM WAV files at 44.1kHz into ./out/ next to this
script. Copy the results into src/ChronoTravelers.Console/Audio/ (see
docs/AUDIO.md) to use them in the game - this script doesn't write there
directly, so a re-run never silently overwrites the files a build depends
on.

Run: python synth_placeholder_audio.py
"""
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt, sawtooth, square
import os

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "out")
os.makedirs(OUT, exist_ok=True)

rng = np.random.default_rng(20260901)


def t_axis(seconds):
    return np.arange(int(seconds * SR)) / SR


def lowpass(x, cutoff_hz, order=4):
    sos = butter(order, cutoff_hz, btype="low", fs=SR, output="sos")
    return sosfilt(sos, x)


def highpass(x, cutoff_hz, order=4):
    sos = butter(order, cutoff_hz, btype="high", fs=SR, output="sos")
    return sosfilt(sos, x)


def bandpass(x, low_hz, high_hz, order=4):
    low_hz = max(1.0, low_hz)
    high_hz = min(SR / 2 - 1, high_hz)
    sos = butter(order, [low_hz, high_hz], btype="band", fs=SR, output="sos")
    return sosfilt(sos, x)


def fade(x, fade_in_s=0.02, fade_out_s=0.02):
    x = x.copy()
    n_in = int(fade_in_s * SR)
    n_out = int(fade_out_s * SR)
    if n_in > 0:
        x[:n_in] *= np.linspace(0, 1, n_in)
    if n_out > 0:
        x[-n_out:] *= np.linspace(1, 0, n_out)
    return x


def normalize(x, peak=0.9):
    m = np.max(np.abs(x)) + 1e-9
    return x / m * peak


def save(name, x):
    x = np.clip(x, -1.0, 1.0)
    pcm = (x * 32767).astype(np.int16)
    path = os.path.join(OUT, name)
    wavfile.write(path, SR, pcm)
    print(f"wrote {path}  ({len(x)/SR:.2f}s)")


def sine(freq, seconds, phase0=0.0):
    t = t_axis(seconds)
    return np.sin(2 * np.pi * freq * t + phase0), t


# ---------------------------------------------------------------------------
# Title theme: ~30s theremin-style melody + slow analog pad + faint noise bed
# ---------------------------------------------------------------------------

def theremin_line(note_times, note_freqs, seconds, vibrato_hz=5.3, vibrato_depth=0.006,
                   glide_s=0.35, warmth=0.18):
    """A single monophonic voice that glides (portamento) between notes -
    the defining theremin trait - with a slow pitch vibrato and a touch of
    3rd-harmonic warmth so it isn't a bare sine."""
    n = int(seconds * SR)
    freq_track = np.zeros(n)
    times = np.array(note_times + [seconds])
    freqs = np.array(note_freqs)
    for i in range(len(note_freqs)):
        seg_start = int(times[i] * SR)
        seg_end = int(times[i + 1] * SR)
        seg_len = seg_end - seg_start
        if seg_len <= 0:
            continue
        glide_n = min(int(glide_s * SR), seg_len)
        start_f = freq_track[seg_start - 1] if seg_start > 0 else freqs[i]
        ramp = np.linspace(start_f, freqs[i], glide_n)
        freq_track[seg_start:seg_start + glide_n] = ramp
        freq_track[seg_start + glide_n:seg_end] = freqs[i]

    t = t_axis(seconds)
    vibrato = 1.0 + vibrato_depth * np.sin(2 * np.pi * vibrato_hz * t)
    inst_freq = freq_track * vibrato
    phase = 2 * np.pi * np.cumsum(inst_freq) / SR
    tone = np.sin(phase) + warmth * np.sin(3 * phase)
    return tone


def analog_pad(chord_freqs_over_time, seconds, breathe_hz=0.12):
    """chord_freqs_over_time: list of (start_s, [freqs]) chord changes."""
    n = int(seconds * SR)
    out = np.zeros(n)
    t_full = t_axis(seconds)
    starts = [c[0] for c in chord_freqs_over_time] + [seconds]
    for i, (start_s, freqs) in enumerate(chord_freqs_over_time):
        end_s = starts[i + 1]
        seg_start = int(start_s * SR)
        seg_end = int(end_s * SR)
        seg_len = seg_end - seg_start
        if seg_len <= 0:
            continue
        t = np.arange(seg_len) / SR
        seg = np.zeros(seg_len)
        for f in freqs:
            detune = 1.0 + rng.uniform(-0.003, 0.003)
            seg += np.sin(2 * np.pi * f * detune * t)
        seg /= len(freqs)
        # crossfade this chord in/out so changes aren't clicky
        x_n = min(int(0.6 * SR), seg_len // 2)
        if x_n > 0:
            seg[:x_n] *= np.linspace(0, 1, x_n)
            seg[-x_n:] *= np.linspace(1, 0, x_n)
        out[seg_start:seg_end] += seg
    breathe = 0.75 + 0.25 * np.sin(2 * np.pi * breathe_hz * t_full)
    out = lowpass(out, 900) * breathe
    return out


def noise_bed(seconds, cutoff=500, lfo_hz=0.08):
    t = t_axis(seconds)
    n = len(t)
    noise = rng.normal(0, 1, n)
    bed = lowpass(noise, cutoff)
    swell = 0.5 + 0.5 * np.sin(2 * np.pi * lfo_hz * t + 1.7)
    return bed * swell


def make_title_theme():
    seconds = 30.0

    # A slow, wandering minor-key motif - E space-opera-ish, not a real tune,
    # built from a natural minor scale so it always sounds "on key".
    E3, FS3, G3, A3, B3, C4, D4, E4, FS4, G4 = (
        164.81, 185.00, 196.00, 220.00, 246.94, 261.63, 293.66, 329.63, 369.99, 392.00,
    )
    note_times = [0.5, 3.0, 5.6, 8.2, 11.0, 14.0, 17.2, 20.2, 23.0, 25.6]
    note_freqs = [E4, G4, FS4, E4, B3, D4, C4, B3, A3, E4]
    melody = np.zeros(int(seconds * SR))
    voice = theremin_line(note_times, note_freqs, seconds - 0.5)
    melody[int(0.5 * SR):int(0.5 * SR) + len(voice)] += voice

    chords = [
        (0.0, [E3, G3, B3]),
        (5.6, [C4 / 2, E3, A3]),
        (11.0, [FS3, A3, C4]),
        (17.2, [E3, G3, B3]),
        (23.0, [A3 / 2, C4, E4]),
    ]
    pad = analog_pad(chords, seconds)

    bed = noise_bed(seconds, cutoff=350, lfo_hz=0.07)

    mix = 0.62 * melody + 0.30 * pad + 0.05 * bed
    mix = lowpass(mix, 6000)
    mix = normalize(mix, 0.85)
    mix = fade(mix, fade_in_s=3.0, fade_out_s=4.0)
    save("title_theme.wav", mix)


# ---------------------------------------------------------------------------
# Movement ambience: wind, scraping, footsteps - short, random-pick clips
# ---------------------------------------------------------------------------

def make_wind(name, seconds=8.0, cutoff=450, gust_hz=0.18, seed_offset=0):
    local_rng = np.random.default_rng(20260901 + seed_offset)
    t = t_axis(seconds)
    n = len(t)
    noise = local_rng.normal(0, 1, n)
    body = lowpass(noise, cutoff)
    moan = bandpass(local_rng.normal(0, 1, n), 220, 340) * 0.4
    gust = 0.55 + 0.45 * np.sin(2 * np.pi * gust_hz * t + local_rng.uniform(0, 6))
    gust *= 0.85 + 0.15 * np.sin(2 * np.pi * gust_hz * 3.3 * t)
    x = (body * 0.8 + moan) * gust
    x = normalize(x, 0.55)
    x = fade(x, 0.8, 1.2)
    save(name, x)


def make_scrape(name, seconds=4.5, seed_offset=0):
    local_rng = np.random.default_rng(20260901 + seed_offset)
    t = t_axis(seconds)
    n = len(t)
    noise = local_rng.normal(0, 1, n)
    # center frequency wanders - a dragged/scraped-debris texture
    center = 500 + 350 * np.sin(2 * np.pi * 0.6 * t) + 150 * np.sin(2 * np.pi * 1.7 * t + 1.0)
    # approximate a swept bandpass by filtering in short overlapping chunks
    chunk = 2048
    out = np.zeros(n)
    win = np.hanning(chunk)
    step = chunk // 2
    for start in range(0, n - chunk, step):
        c = center[start + chunk // 2]
        seg = bandpass(noise[start:start + chunk], max(80, c - 200), c + 200)
        out[start:start + chunk] += seg * win
    envelope = 0.5 + 0.5 * np.sin(2 * np.pi * 0.45 * t - 1.0)
    envelope *= 0.7 + 0.3 * (local_rng.uniform(0, 1, n) > 0.9995).astype(float)
    x = out * (0.4 + 0.6 * envelope)
    x = normalize(x, 0.5)
    x = fade(x, 0.05, 0.6)
    save(name, x)


def make_footsteps(name, seconds=4.0, step_interval=0.55, seed_offset=0):
    local_rng = np.random.default_rng(20260901 + seed_offset)
    n = int(seconds * SR)
    x = np.zeros(n)
    step_len = int(0.09 * SR)
    time_cursor = 0.15
    while time_cursor + 0.09 < seconds:
        idx = int(time_cursor * SR)
        burst = local_rng.normal(0, 1, step_len)
        burst = bandpass(burst, 90, 1800)
        thump_t = np.arange(step_len) / SR
        thump = np.sin(2 * np.pi * 70 * thump_t) * np.exp(-thump_t * 40)
        step_sound = (burst * 0.5 + thump * 0.8) * np.exp(-thump_t * 18)
        amp = local_rng.uniform(0.55, 1.0)
        end = min(idx + step_len, n)
        x[idx:end] += step_sound[: end - idx] * amp
        time_cursor += step_interval * local_rng.uniform(0.85, 1.2)
    x = normalize(x, 0.6)
    x = fade(x, 0.02, 0.4)
    save(name, x)


def make_moan(name, seconds=5.0, seed_offset=0):
    """A slow, wavering vocal-like moan - a low fundamental that drifts
    and vibratos rather than holding a fixed pitch, shaped through two
    vowel-ish ("oh") formant bands so it reads as far-off and mournful
    rather than a bare tone. One long swell in and back out. Entirely
    synthesized (sine harmonics + bandpass "formants" + a breath-noise
    layer) - no sampled voice of any kind."""
    local_rng = np.random.default_rng(20260901 + seed_offset)
    t = t_axis(seconds)
    n = len(t)

    vibrato = 1.0 + 0.03 * np.sin(2 * np.pi * 0.35 * t + local_rng.uniform(0, 6))
    drift = 1.0 + 0.05 * np.sin(2 * np.pi * 0.07 * t + local_rng.uniform(0, 6))
    f0 = 105.0 * vibrato * drift
    phase = 2 * np.pi * np.cumsum(f0) / SR
    tone = np.sin(phase) + 0.5 * np.sin(2 * phase) + 0.25 * np.sin(3 * phase)

    formant1 = bandpass(tone, 350, 550)
    formant2 = bandpass(tone, 850, 1150)
    voiced = formant1 * 0.7 + formant2 * 0.35 + tone * 0.2

    breath = lowpass(local_rng.normal(0, 1, n), 700) * 0.06

    # One long bell-shaped swell - the moan rises, holds, fades.
    envelope = np.sin(np.pi * np.clip(t / seconds, 0, 1)) ** 1.5

    x = (voiced + breath) * envelope
    x = normalize(x, 0.275)
    x = fade(x, 0.3, 1.2)
    save(name, x)


def make_wraith_scream(name, seconds=2.6, seed_offset=0):
    """A short, harsh shrieking wail - a fast rise from a low growl into a
    high shriek, then a quavering, slowly descending tail - in the mood of
    a classic fantasy wraith screech (evocative, not derivative: an
    original sine-sweep + ring-modulation + filtered-noise texture, not
    sampled from any film or recording)."""
    local_rng = np.random.default_rng(20260901 + seed_offset)
    t = t_axis(seconds)
    n = len(t)

    rise_n = int(0.25 * n)
    hold_n = n - rise_n
    f_rise = np.linspace(220, 2400, rise_n)
    t_hold = np.arange(hold_n) / SR
    tremor = 1.0 + 0.10 * np.sin(2 * np.pi * 14 * t_hold)
    f_hold = np.linspace(2400, 1100, hold_n) * tremor
    f_track = np.concatenate([f_rise, f_hold])
    phase = 2 * np.pi * np.cumsum(f_track) / SR
    tone = np.sin(phase)

    # Ring modulation against a low square wave for a harsh, inhuman edge.
    ring = np.sign(np.sin(2 * np.pi * 55 * t))
    harsh = tone * (0.5 + 0.5 * np.abs(ring))

    rasp = bandpass(local_rng.normal(0, 1, n), 1200, 4500) * 0.35

    attack_n = max(1, int(0.04 * n))
    envelope = np.concatenate([
        np.linspace(0, 1, attack_n),
        np.linspace(1, 0.05, n - attack_n) ** 1.2,
    ])[:n]

    x = (harsh * 0.8 + rasp) * envelope
    x = highpass(x, 150)
    x = normalize(x, 0.375)
    x = fade(x, 0.005, 0.9)
    save(name, x)


# ---------------------------------------------------------------------------
# Alternative title theme: an original up-tempo analog-disco synth piece,
# in the general MOOD of a late-1970s/early-1980s space-opera TV fanfare
# (arpeggiated synth bass, a four-on-the-floor pulse, a brassy synth-lead
# stab motif) - a wholly new composition. No melody, chord progression, or
# arrangement is transcribed or borrowed from any actual copyrighted
# theme; the four-chord vamp and fanfare motif below are original.
# ---------------------------------------------------------------------------

def _synth_kick(seconds=0.32):
    t = np.arange(int(seconds * SR)) / SR
    freq = 130 * np.exp(-t * 22) + 42  # classic drum-machine pitch drop
    phase = 2 * np.pi * np.cumsum(freq) / SR
    body = np.sin(phase) * np.exp(-t * 13)
    click = np.exp(-t * 500) * 0.25
    return body + click


def _synth_hihat(seconds=0.07, seed_offset=0):
    local_rng = np.random.default_rng(20260901 + seed_offset)
    n = int(seconds * SR)
    x = highpass(local_rng.normal(0, 1, n), 7000)
    env = np.exp(-np.arange(n) / SR * 70)
    return x * env


def _synth_bass_note(freq, seconds, decay=9.0):
    t = np.arange(int(seconds * SR)) / SR
    raw = sawtooth(2 * np.pi * freq * t)
    env = np.exp(-t * decay)
    return lowpass(raw, 1400) * env


def _synth_brass_stab(freq, seconds=0.5):
    """Detuned squares stacked with an octave-up voice - an analog-synth
    approximation of a horn-section "stab", the disco-fanfare signature."""
    t = np.arange(int(seconds * SR)) / SR
    voice = sum(square(2 * np.pi * freq * detune * t) for detune in (0.997, 1.0, 1.003, 2.006)) / 4
    env = np.exp(-t * 4.5) * np.clip(t * 300, 0, 1)
    return lowpass(voice, 3200) * env


def make_title_theme_disco_alt(name, seconds=30.0):
    bpm = 124.0
    beat = 60.0 / bpm
    eighth = beat / 2
    intro_offset = 1.4
    outro_tail = 3.0

    # An original 4-bar minor-key vamp (Dm - Bb - F - C), each entry the
    # chord's (root, third, fifth, octave) in Hz.
    bars = [
        (146.83, 174.61, 220.00, 293.66),  # Dm
        (116.54, 146.83, 174.61, 233.08),  # Bb
        (174.61, 220.00, 261.63, 349.23),  # F
        (130.81, 164.81, 196.00, 261.63),  # C
    ]
    loops = 3
    sequence = bars * loops

    n_total = int(seconds * SR)
    mix = np.zeros(n_total)

    kick_snd = _synth_kick()
    hh_snds = [_synth_hihat(seed_offset=i) for i in range(4)]

    def place(dest, start_seconds, clip, gain=1.0):
        idx = int(start_seconds * SR)
        end = min(idx + len(clip), n_total)
        if end > idx:
            dest[idx:end] += clip[: end - idx] * gain

    # Rhythm + arpeggiated bass, bar by bar.
    cursor = intro_offset
    for bar_i, chord in enumerate(sequence):
        for beat_i in range(4):
            beat_time = cursor + beat_i * beat
            place(mix, beat_time, kick_snd, 0.9)
            hh = hh_snds[(bar_i * 4 + beat_i) % len(hh_snds)]
            place(mix, beat_time + eighth, hh, 0.35)
            for e in range(2):  # two eighth-note bass notes per beat, walking the chord tones
                note_freq = chord[(beat_i * 2 + e) % len(chord)] / 2  # down an octave
                snd = _synth_bass_note(note_freq, eighth * 1.05)
                place(mix, beat_time + e * eighth, snd, 0.5)
        cursor += 4 * beat
    groove_end = cursor

    # Brassy fanfare stabs: a short four-note motif at the top of every
    # other bar (original melody: A3-C4-D4-F4 over the vamp).
    lead_freqs = [220.00, 261.63, 293.66, 349.23]
    cursor = intro_offset
    for bar_i in range(0, len(sequence), 2):
        for k, f in enumerate(lead_freqs):
            snd = _synth_brass_stab(f)
            place(mix, cursor + k * (beat * 0.9), snd, 0.55)
        cursor += 8 * beat

    # Sustained analog pad under the groove for harmonic glue, following
    # the same bar progression an octave up from the bass.
    pad_chords = []
    cursor = intro_offset
    for chord in sequence:
        pad_chords.append((cursor, [f * 2 for f in chord[:3]]))
        cursor += 4 * beat
    pad = analog_pad(pad_chords, groove_end - intro_offset + 0.01, breathe_hz=0.2)
    place(mix, intro_offset, pad, 0.22)

    # Intro riser: a short upward filtered-noise sweep + rising sine,
    # like an arpeggiator spinning up before the groove drops.
    riser_rng = np.random.default_rng(20260901 + 99)
    riser_t = np.arange(int(intro_offset * SR)) / SR
    riser_noise = riser_rng.normal(0, 1, len(riser_t))
    sweep_center = np.linspace(300, 3500, len(riser_t))
    riser = np.zeros(len(riser_t))
    chunk = 2048
    win = np.hanning(chunk)
    for start in range(0, max(0, len(riser_t) - chunk), chunk // 2):
        c = sweep_center[start + chunk // 2]
        seg = bandpass(riser_noise[start:start + chunk], max(80, c - 250), c + 250)
        riser[start:start + chunk] += seg * win
    riser *= np.linspace(0.05, 0.6, len(riser_t))
    place(mix, 0.0, riser, 1.0)

    # A final held stab + pad tag, then fade to silence.
    tag_chord = sequence[-1]
    place(mix, groove_end, _synth_brass_stab(tag_chord[3], seconds=1.4), 0.5)
    tail_pad = analog_pad([(0.0, [f * 2 for f in tag_chord[:3]])], min(outro_tail + 0.5, seconds - groove_end), breathe_hz=0.15)
    place(mix, groove_end, tail_pad, 0.2)

    mix = lowpass(mix, 8500)
    mix = normalize(mix, 0.85)
    mix = fade(mix, fade_in_s=0.15, fade_out_s=2.5)
    save(name, mix)


# ---------------------------------------------------------------------------
# Time-travel cue: rising shimmer chorus + sparkle + dispersing tail
# ("similar to" a transporter effect in structure/mood - an original sound)
# ---------------------------------------------------------------------------

def make_transporter(name, seconds=3.6):
    t = t_axis(seconds)
    n = len(t)

    # Rising chorus: several detuned sine sweeps converging upward
    rise = np.zeros(n)
    base_start, base_end = 220.0, 1400.0
    rise_frac = 0.45
    rise_n = int(rise_frac * n)
    for k, detune in enumerate([0.98, 1.0, 1.005, 1.5, 2.01]):
        f_track = np.concatenate([
            np.linspace(base_start, base_end, rise_n),
            np.full(n - rise_n, base_end),
        ]) * detune
        phase = 2 * np.pi * np.cumsum(f_track) / SR
        rise += np.sin(phase) * (0.35 if detune >= 1.5 else 0.6)
    rise /= 3.0

    # Metallic shimmer via fast ring modulation
    ring = np.sin(2 * np.pi * 37 * t) * 0.5 + 0.5
    shimmer = rise * ring

    # Sparkle: short random bandpassed noise "twinkles" scattered through
    sparkle = np.zeros(n)
    n_sparkles = 28
    for _ in range(n_sparkles):
        start = rng.uniform(0.05, seconds - 0.12)
        dur = rng.uniform(0.03, 0.09)
        idx = int(start * SR)
        length = int(dur * SR)
        if idx + length >= n:
            continue
        burst = rng.normal(0, 1, length)
        center = rng.uniform(1800, 5200)
        burst = bandpass(burst, center - 300, center + 300)
        env = np.hanning(length)
        sparkle[idx:idx + length] += burst * env * rng.uniform(0.3, 0.7)

    # Overall arc: build then disperse (materialize -> dematerialize)
    arc = np.concatenate([
        np.linspace(0, 1, int(0.35 * n)),
        np.linspace(1, 0.15, n - int(0.35 * n)),
    ])[:n]

    mix = (shimmer * 0.8 + sparkle * 0.9) * arc
    mix = highpass(mix, 120)
    mix = normalize(mix, 0.85)
    mix = fade(mix, 0.01, 0.5)
    save(name, mix)


if __name__ == "__main__":
    make_title_theme()
    make_wind("wind_1.wav", seed_offset=1)
    make_wind("wind_2.wav", seed_offset=2, cutoff=380, gust_hz=0.24)
    make_scrape("scrape_1.wav", seed_offset=3)
    make_footsteps("footsteps_1.wav", seed_offset=4)
    make_footsteps("footsteps_2.wav", seed_offset=5, step_interval=0.62)
    make_moan("moan_1.wav", seed_offset=6)
    make_wraith_scream("wraith_scream_1.wav", seed_offset=7)
    make_title_theme_disco_alt("title_theme_alt.wav")
    make_transporter("transporter.wav")
    print("done")
