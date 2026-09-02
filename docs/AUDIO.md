# Sound

`AudioManager.cs` (in `ChronoTravelers.Console`) is the whole system — one
static class, three entry points, all fire-and-forget. It plays through
`NAudio`'s Windows `waveOut` output, which is why sound is a
`ChronoTravelers.Console`-only feature: `ChronoTravelers.Server` is
headless and never touches it, and a raw telnet client obviously can't play
anything on the player's machine either way — only the actual Windows
console app has speakers to use.

## What plays, and when

| Trigger | File | Behaviour |
|---|---|---|
| Title screen (`RenderTitle()`) | `Audio/title_theme.wav` **or** `Audio/title_theme_alt.wav`, picked 50/50 | Plays once — and only once — per run. Both entry points (`--connect` and the local single-player game) call `RenderTitle()` exactly once each, so `AudioManager.PlayTitleThemeOnce()` there is sufficient; a static flag makes every later call a no-op even if that ever changes. Which theme plays is a coin flip each run (`AudioManager.TitleThemeFiles`, `Rng.Next`), not a fixed per-install choice. The fade in/out is baked into each file itself (~30s total, a few seconds of fade on each end) rather than done in code, so playing the file start-to-finish is all the code has to do. |
| A successful grid move (`HandleMove`) | one of `Audio/wind_1.wav`, `wind_2.wav`, `scrape_1.wav`, `footsteps_1.wav`, `footsteps_2.wav`, `moan_1.wav`, `wraith_scream_1.wav`, picked at random | Roughly **1 move in 4** (`AudioManager`'s `MovementSfxOneInN`), never every step — the brief was "random, not every time." Failed moves ("You can't go that way") don't trigger it. `moan_1.wav` and `wraith_scream_1.wav` play at **half the current master volume** — see "Per-clip volume" below — so they land as a quieter, unsettling undertone rather than as prominent as the wind/footsteps clips. |
| A successful time-travel jump | `Audio/transporter.wav` | Every time, right after the jump succeeds (not on a failed/cancelled jump). |

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
VolumeMultiplier)[]`); every other trigger (title theme, transporter)
implicitly uses the default `1.0`.

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
`AudioManager.LoadSettings()` **before** the first `RenderTitle()`, saved
on every adjustment. A missing, empty, or corrupt file just means the
**75% default** (`AudioManager.DefaultVolume` — the raw clips run a little
hot at full, so a fresh install starts a quarter down and the player
brings it up with `+`); a read-only profile means the change holds for the
session but isn't remembered. This file is `ChronoTravelers.Console`'s only
setting store and is deliberately separate from the LiteDB save DB so it
needs no schema/migration handling.

## The audio files

`src/ChronoTravelers.Console/Audio/*.wav` — 16-bit PCM mono, 44.1kHz.
**All ten are original, procedurally synthesized placeholder audio** (sine
oscillators, filtered noise, ring modulation, simple envelopes — see the
synthesis script kept alongside this doc's PR/commit if you want to
regenerate or tweak them); nothing here is sampled from *Lost in Space*,
*Star Trek*, *The Lord of the Rings*, the *Buck Rogers* TV theme, or any
other copyrighted source. They're "inspired by" in mood only — a
theremin-style wandering melody over a slow analog pad for the original
title theme, a four-on-the-floor synth-bass/brass groove evoking
late-1970s space-disco for the alternative title theme
(`title_theme_alt.wav`), a rising/shimmering chorus with a sparkly
dispersing tail for the time-travel cue, filtered noise beds for wind, a
swept bandpass for scraping, short percussive bursts for footsteps, a
slow wavering vocal-like swell built from sine harmonics through
vowel-formant bandpasses for the moan (`moan_1.wav`), and a rising
sine-sweep through ring modulation and filtered noise for the wraith
shriek (`wraith_scream_1.wav`, evocative of a classic fantasy
wraith-screech mood, not derived from any specific film's sound design).
The moan and wraith-scream clips are also baked quieter than the rest of
the pool (a lower normalize peak) on top of their 0.5 playback multiplier
— see "Per-clip volume" above.

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
installed app the same way it already does for `Content\`.

The `Content Include="Audio\*.wav"` items handle the normal copy, but the
SDK's single-file publish pipeline reproducibly drops `title_theme.wav`
(the largest file) from its computed publish list, so the csproj also has
a `ForceCopyAudioToPublishDir` target (`AfterTargets="Publish"`) that
re-copies the whole `Audio\` folder into `$(PublishDir)Audio` afterwards,
unconditionally. That's what makes a plain `dotnet publish` (local or the
`release.yml` CI job) ship all ten WAVs with no manual step.
