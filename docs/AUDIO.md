# Sound

`AudioManager.cs` (in `ChronoTravelers.Console`) is the whole system — one
static class, several fire-and-forget entry points. It plays through
`NAudio`'s Windows `waveOut` output, which is why sound is a
`ChronoTravelers.Console`-only feature: `ChronoTravelers.Server` is
headless and never touches it, and a raw telnet client obviously can't play
anything on the player's machine either way — only the actual Windows
console app has speakers to use.

## What plays, and when

| Trigger | File | Behaviour |
|---|---|---|
| Opening cinematic (`Program.cs`'s `PlayIntroCinematic()`) | `Audio/intro_cinematic.wav` | Plays once, right before the very first `RenderTitle()` — see "The opening cinematic" below. |
| Title screen (`RenderTitle()`) | `Audio/title_theme.wav`, `Audio/title_theme_alt.wav`, **or** `Audio/title_theme_sad.wav`, picked evenly (1 in 3) | Plays once — and only once — per run. Both entry points (`--connect` and the local single-player game) call `RenderTitle()` exactly once each, so `AudioManager.PlayTitleThemeOnce()` there is sufficient; a static flag makes every later call a no-op even if that ever changes. Which theme plays is a fresh random pick each run (`AudioManager.TitleThemeFiles`, `Rng.Next`), not a fixed per-install choice. The fade in/out is baked into each file itself (~30s total, a few seconds of fade on each end) rather than done in code, so playing the file start-to-finish is all the code has to do. `RenderTitle()` also independently picks its ASCII banner at random each run (see "Title screen banners" below) — the banner and theme picks are unrelated to each other, so any banner can pair with any theme. |
| A successful grid move (`HandleMove`) | one of `Audio/wind_1.wav`, `wind_2.wav`, `scrape_1.wav`, `footsteps_1.wav`, `footsteps_2.wav`, `moan_1.wav`, `wraith_scream_1.wav`, picked at random | Roughly **1 move in 4** (`AudioManager`'s `MovementSfxOneInN`), never every step — the brief was "random, not every time." Failed moves ("You can't go that way") don't trigger it. `moan_1.wav` and `wraith_scream_1.wav` play at **half the current master volume** — see "Per-clip volume" below — so they land as a quieter, unsettling undertone rather than as prominent as the wind/footsteps clips. |
| A successful time-travel jump | `Audio/transporter.wav` | Every time, right after the jump succeeds (not on a failed/cancelled jump). |

## The opening cinematic

A ~30 second, silent-film-style ASCII animation plays once per run, right
before the first `RenderTitle()` — `Program.cs`'s `PlayIntroCinematic()`,
called once right after `AudioManager.LoadSettings()` so it precedes both
entry points (`--connect` and local single-player) without needing its own
call in each. It dramatizes docs/GDD.md §1's backstory: a Traveler
approaching Project Meridian's tunnel, riding it deeper as the first
full-power run destabilizes, and the accident that frays the timeline and
flings the gantry crew downstream.

The frames live in `Program.cs`'s `IntroCinematicScenes()` — a static list
of `(string[] Lines, string Colour, int DurationMs)`, each clearing the
screen and holding for its slice of the ~30000ms total, arc-matched to the
score below: a hushed, sad opening (0-6s) as the Traveler approaches the
dormant tunnel; a rising, anxious middle (6-20s) as concentric
`TimeTunnelBanner()`-style rings rush past faster and the colour climbs
from calm blue through amber to red; the accident itself as a run of fast
cuts (20-23s, including a one-frame white flash); and a quiet, mournful
aftermath fade (23-30s) that leads straight into the title screen.
`WaitOrSkip` polls `Console.KeyAvailable` every ~100ms between frames, so
any keypress jumps straight to the title screen — useful on a second or
third run of the game. The whole thing is skipped outright (no clears, no
delays) when stdin is redirected (`Console.IsInputRedirected`), same guard
as `Program.ReadMenuLine`'s live-key handling, since there's no
interactive console to skip from and nothing worth animating to a pipe or
an automated test run.

`AudioManager.PlayIntroCinematicOnce()` fires `Audio/intro_cinematic.wav`
at the very start of the cinematic — see "The audio files" below for what
the track itself sounds like.

## Title screen banners

`RenderTitle()` (`Program.cs`) also picks at random, once per render,
among three full ASCII-art banners (`TitleBanners()`, combining
`WordmarkBanner()`, `TimeTunnelBanner()`, and `RuinedCityBanner()`):

- **Wordmark** — the original full-block "CHRONO TRAVELERS" logo.
- **Time Tunnel** — a nested-rectangle "looking down the tunnel" motif
  with diagonal corner connectors and a glowing "· T A C H Y O N ·" core
  label, followed by the centered title line.
- **Ruined City** — a jagged skyline of block-character buildings (several
  broken/notched to show damage), street-level rubble, a running ASCII
  figure with motion lines, and a cracked street baseline, followed by the
  centered title line.

Same mechanism as the theme pick: a fresh `Random.Shared` choice on every
render, coloured afterwards with the existing random title-colour pick
(`titleColour`/`titleColours`). All banner lines — and every
`IntroCinematicScenes()` frame above — are plain ASCII with no `[`/`]`
characters, since Spectre.Console's `MarkupLine` would otherwise misparse
them as style tags.

## Master volume

One 0–100% level applied to every clip. `+` / `=` turns it up a notch
(10%), `-` turns it down; `volume` (or `vol`) just prints the current
level. These work **from the start menu and from any in-game prompt** —
`AudioManager.TryApplyVolumeCommand` is checked before the command switch
in both loops, so adjusting volume never costs a game turn and never
reaches the "unrecognised command" path. A change takes effect
immediately, including on a clip that's already playing (the title theme
fading under the menu): `AudioManager` keeps each active clip's
`AudioFileReader` in `ActivePlayers` and rewrites its `Volume` in place.

### Per-clip volume

A handful of clips (currently `moan_1.wav` and `wraith_scream_1.wav`) are
meant to sit quieter than the rest of the pool regardless of what the
master level is set to. `PlayFireAndForget` takes an optional
`volumeMultiplier` (default `1.0`, i.e. full master volume); a clip started
at `0.5f` plays at half the current master volume the instant it starts,
and stays at half if the player then adjusts the master volume while it's
still playing — `ActivePlayers` stores the multiplier alongside each active
`AudioFileReader`, and `SetVolume` retunes every active clip as
`master × thatClip'sMultiplier`, not just to the new master level.
`MovementSfxFiles` carries the multiplier per entry (`(string File, float
VolumeMultiplier)[]`); every other trigger (intro cinematic, title theme,
transporter) implicitly uses the default `1.0`.

On the **start menu specifically**, `+`/`=`/`-`/`_` apply the instant
they're pressed — no Enter needed — via `Program.ReadMenuLine`, which reads
key by key instead of a full line and special-cases those four keys before
they ever reach the buffer. Everything else typed there (`1`–`5`, `play`,
...) still needs Enter same as always. That live-key handling needs an
actual interactive console, so it falls back to the old Enter-terminated
`ReadNonEmptyLine` when stdin is redirected (piped input / automation) —
the word forms (`volume`/`vol`) still work either way. The in-game prompt
is unchanged: it already reads full commands a line at a time, so `+`/`-`
there are typed and submitted like any other command, just without
costing a turn.

The level is persisted to `%APPDATA%\ChronoTravelers\settings.json`
(`{ "volume": 0.0–1.0 }`) — loaded once at startup by
`AudioManager.LoadSettings()` **before** the intro cinematic or the first
`RenderTitle()`, saved on every adjustment. A missing, empty, or corrupt
file just means the **75% default** (`AudioManager.DefaultVolume` — the
raw clips run a little hot at full, so a fresh install starts a quarter
down and the player brings it up with `+`); a read-only profile means the
change holds for the session but isn't remembered. This file is
`ChronoTravelers.Console`'s only setting store and is deliberately separate
from the LiteDB save DB so it needs no schema/migration handling.

## The audio files

`src/ChronoTravelers.Console/Audio/*.wav` — 16-bit PCM mono, 44.1kHz.
**All twelve are original, procedurally synthesized placeholder audio**
(sine oscillators, filtered noise, ring modulation, bandpass/lowpass
filters, simple envelopes — see the synthesis scripts kept alongside this
doc's PR/commit if you want to regenerate or tweak them); nothing here is
sampled from *Lost in Space*, *Star Trek*, *The Lord of the Rings*, the
*Buck Rogers* TV theme, *Quantum Leap*, or any other copyrighted source.
They're "inspired by" in mood only:

- **`intro_cinematic.wav`** (~30s, no baked fade beyond a 2s fade-in/4s
  fade-out) — the opening cinematic's score, "urgent but sad" per the
  brief. A hushed original piano-and-pad passage in A minor to open
  (mirroring `title_theme_sad.wav`'s harmonic language without repeating
  its melody); a sine-pluck arpeggio that accelerates and climbs an octave
  as a wind bed and a rising sine-sweep (siren-like, evocative of an alarm
  without copying one) swell underneath; a sharp accident hit around the
  20s mark — a low sub-bass thud, a dissonant minor-second cluster stab, a
  filtered noise burst, a ring-modulated glitch texture, and a fast
  downward pitch-sweep "system failure" whoosh, all layered in the same
  half-second; then a return to a slower, quieter Em/F pad with a single
  lingering high tone drifting into silence for the aftermath. Timed to
  `Program.cs`'s `IntroCinematicScenes()` frame list — see "The opening
  cinematic" above.
- a theremin-style wandering melody over a slow analog pad for the
  original title theme,
- a four-on-the-floor synth-bass/brass groove evoking late-1970s
  space-disco for the alternative title theme (`title_theme_alt.wav`),
- a wistful original piano-and-pad piece in A minor for the sad title
  theme (`title_theme_sad.wav`) — a sparse original piano melody over
  sustained pad chords (Am–F–C–G–Am–Em–F–E) with a few soft high "bell"
  shimmer accents and a quiet low sub-pad, evocative of the melancholy
  mood of *Quantum Leap*'s opening theme without reproducing or deriving
  from that copyrighted composition,
- a rising/shimmering chorus with a sparkly dispersing tail for the
  time-travel cue,
- filtered noise beds for wind, a swept bandpass for scraping, short
  percussive bursts for footsteps,
- a slow wavering vocal-like swell built from sine harmonics through
  vowel-formant bandpasses for the moan (`moan_1.wav`),
- and a rising sine-sweep through ring modulation and filtered noise for
  the wraith shriek (`wraith_scream_1.wav`, evocative of a classic fantasy
  wraith-screech mood, not derived from any specific film's sound design).

The moan and wraith-scream clips are also baked quieter than the rest of
the pool (a lower normalize peak) on top of their 0.5 playback multiplier
— see "Per-clip volume" above. Like the three title themes, `intro_
cinematic.wav` runs its fade in code-free — baked directly into the
waveform (2s in, 4s out) rather than applied by `AudioManager`.

Swap in better/licensed audio any time by replacing these files under the
same names and durations (or updating the filenames in `AudioManager.cs`
if you rename them) — the code only cares about the file path.

## Failure handling

`AudioManager.PlayFireAndForget` never throws outward: a missing `Audio/`
folder (e.g. a `dotnet run` from a dev tree that hasn't copied content
yet), a missing individual file, no sound device, or any NAudio exception
all just mean "no sound this time," never a crashed game or an interrupted
command. Setting **volume to 0%** (hold `-`) is the mute — there's no
separate mute toggle.

## Packaging

Same story as the JSON content catalogs: WAV files can't be embedded into
the single-file publish, so `dotnet publish` copies `Audio/*.wav` next to
the exe (`ChronoTravelers.Console.csproj`), and
`installer/ChronoTravelers.iss` bundles that `Audio\` folder into the
installed app the same way it already does for `Content\`. Both use plain
`Audio\*` wildcards, so `intro_cinematic.wav` needed no project-file
changes to ship — it's picked up automatically alongside the other eleven.

The `Content Include="Audio\*.wav"` items handle the normal copy, but the
SDK's single-file publish pipeline reproducibly drops `title_theme.wav`
(the largest file) from its computed publish list, so the csproj also has
a `ForceCopyAudioToPublishDir` target (`AfterTargets="Publish"`) that
re-copies the whole `Audio\` folder into `$(PublishDir)Audio` afterwards,
unconditionally. That's what makes a plain `dotnet publish` (local or the
`release.yml` CI job) ship all eleven WAVs with no manual step.
