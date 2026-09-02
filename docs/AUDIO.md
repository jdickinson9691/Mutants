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
| Title screen (`RenderTitle()`) | `Audio/title_theme.wav` | Plays once — and only once — per run. Both entry points (`--connect` and the local single-player game) call `RenderTitle()` exactly once each, so `AudioManager.PlayTitleThemeOnce()` there is sufficient; a static flag makes every later call a no-op even if that ever changes. The fade in/out is baked into the file itself (~30s total, a few seconds of fade on each end) rather than done in code, so playing the file start-to-finish is all the code has to do. |
| A successful grid move (`HandleMove`) | one of `Audio/wind_1.wav`, `wind_2.wav`, `scrape_1.wav`, `footsteps_1.wav`, `footsteps_2.wav`, picked at random | Roughly **1 move in 4** (`AudioManager`'s `MovementSfxOneInN`), never every step — the brief was "random, not every time." Failed moves ("You can't go that way") don't trigger it. |
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
**All seven are original, procedurally synthesized placeholder audio** (sine
oscillators, filtered noise, simple envelopes — see the synthesis script
kept alongside this doc's PR/commit if you want to regenerate or tweak
them); nothing here is sampled from *Lost in Space*, *Star Trek*, or any
other copyrighted source. They're "inspired by" in mood only — a
theremin-style wandering melody over a slow analog pad for the title
theme, a rising/shimmering chorus with a sparkly dispersing tail for the
time-travel cue, filtered noise beds for wind, a swept bandpass for
scraping, and short percussive bursts for footsteps.

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
`release.yml` CI job) ship all seven WAVs with no manual step.
