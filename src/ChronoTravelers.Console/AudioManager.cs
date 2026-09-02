using System.Collections.Concurrent;
using System.Text.Json;
using NAudio.Wave;

/// <summary>
/// Local sound playback for the actual Windows console app — docs/AUDIO.md.
/// Every playback entry point here is fire-and-forget and never throws
/// outward: no sound card, a dev tree with no <c>Audio\</c> folder next to
/// the exe, or any NAudio failure must never crash the game or block a
/// command. Not used by <c>ChronoTravelers.Server</c> (headless) and has no
/// effect for a raw telnet client — only this process's own speakers play
/// anything.
///
/// Master volume is a single 0–100% level, adjusted with <c>+</c> / <c>-</c>
/// (see <see cref="TryApplyVolumeCommand"/>) from the start menu or any
/// in-game prompt, and persisted to <c>%APPDATA%\ChronoTravelers\settings.json</c>
/// so it carries across sessions.
/// </summary>
internal static class AudioManager
{
    private static readonly string AudioDirectory = Path.Combine(AppContext.BaseDirectory, "Audio");
    private static readonly Random Rng = new();
    private static bool _titleThemePlayed;

    // A purely local WaveOutEvent has no GC root once PlayFireAndForget
    // returns except its own playback thread - fine in practice, but this
    // dictionary gives it an explicit one for the life of the clip so a clip
    // can never get cut off by a GC pass, and drops it again on
    // PlaybackStopped so nothing here leaks. The value is the clip's reader,
    // kept so a live volume change can reach every currently-playing clip.
    private static readonly ConcurrentDictionary<WaveOutEvent, AudioFileReader> ActivePlayers = new();

    // "Random, not every time" per the brief - roughly one move in four gets
    // an ambience clip (wind / scraping / footsteps), picked from the pool
    // below so repeat moves don't always sound identical.
    private const int MovementSfxOneInN = 4;
    private static readonly string[] MovementSfxFiles =
    [
        "wind_1.wav", "wind_2.wav", "scrape_1.wav", "footsteps_1.wav", "footsteps_2.wav",
    ];

    // --- Master volume ---------------------------------------------------

    private const float VolumeStep = 0.1f;

    /// <summary>Master volume for a fresh install (no <c>settings.json</c> yet). 0.75 — the raw clips sit a touch hot at full, so start a quarter down and let the player bring it up with <c>+</c>.</summary>
    private const float DefaultVolume = 0.75f;

    private static float _volume = DefaultVolume;

    private static readonly string SettingsPath = ResolveSettingsPath();

    /// <summary>The current master volume as a whole percentage, 0–100.</summary>
    public static int VolumePercent => (int)Math.Round(_volume * 100);

    /// <summary>
    /// Loads the persisted master volume, if any. Call once at startup
    /// before the title theme plays. A missing, empty, or corrupt settings
    /// file just leaves the volume at its <see cref="DefaultVolume"/> (75%)
    /// — never fatal.
    /// </summary>
    public static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var stream = File.OpenRead(SettingsPath);
                var dto = JsonSerializer.Deserialize<ConsoleSettings>(stream);
                if (dto is not null)
                {
                    _volume = Math.Clamp(dto.Volume, 0f, 1f);
                }
            }
        }
        catch
        {
            // Unreadable / malformed settings must not block startup.
        }
    }

    /// <summary>
    /// If <paramref name="input"/> is a volume command — <c>+</c> / <c>=</c>
    /// (up), <c>-</c> (down), or <c>volume</c> / <c>vol</c> (just report) —
    /// applies it, persists the new level, retunes anything already playing,
    /// and returns the resulting percentage. Returns <c>null</c> for any
    /// other input so the caller can carry on handling it. Never costs a
    /// game turn: callers handle this before the world tick.
    /// </summary>
    public static int? TryApplyVolumeCommand(string input)
    {
        switch (input.Trim().ToLowerInvariant())
        {
            case "+" or "=" or "volume up" or "vol+" or "louder":
                SetVolume(_volume + VolumeStep);
                return VolumePercent;

            case "-" or "_" or "volume down" or "vol-" or "quieter":
                SetVolume(_volume - VolumeStep);
                return VolumePercent;

            case "volume" or "vol":
                return VolumePercent;

            default:
                return null;
        }
    }

    private static void SetVolume(float value)
    {
        _volume = Math.Clamp((float)Math.Round(value, 2), 0f, 1f);

        foreach (var reader in ActivePlayers.Values)
        {
            try
            {
                reader.Volume = _volume;
            }
            catch
            {
                // Reader may be mid-dispose on its playback thread — harmless.
            }
        }

        SaveSettings();
    }

    private static void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = File.Create(SettingsPath);
            JsonSerializer.Serialize(stream, new ConsoleSettings { Volume = _volume });
        }
        catch
        {
            // Read-only profile / locked file — the change still holds for
            // this session, it just won't be remembered next time.
        }
    }

    private static string ResolveSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData)
            ? Path.Combine("saves", "settings.json")
            : Path.Combine(appData, "ChronoTravelers", "settings.json");
    }

    private sealed class ConsoleSettings
    {
        public float Volume { get; set; } = DefaultVolume;
    }

    // --- Playback -------------------------------------------------------

    /// <summary>
    /// Plays the ~30s title theme (fade in/out is baked into the file
    /// itself) the first time — and only the first time — the title screen
    /// is shown this run. Safe to call from every code path that renders
    /// the title (single-player and <c>--connect</c> both call
    /// <c>RenderTitle()</c> once each), since the flag makes every call
    /// after the first a no-op.
    /// </summary>
    public static void PlayTitleThemeOnce()
    {
        if (_titleThemePlayed)
        {
            return;
        }

        _titleThemePlayed = true;
        PlayFireAndForget("title_theme.wav");
    }

    /// <summary>Call after a successful grid move (one room to the next). Plays a random ambience clip about 1 time in <see cref="MovementSfxOneInN"/> — never on every step.</summary>
    public static void MaybePlayMovementSfx()
    {
        if (Rng.Next(MovementSfxOneInN) != 0)
        {
            return;
        }

        PlayFireAndForget(MovementSfxFiles[Rng.Next(MovementSfxFiles.Length)]);
    }

    /// <summary>Call once a time-travel jump actually succeeds.</summary>
    public static void PlayTimeTravelSfx() => PlayFireAndForget("transporter.wav");

    private static void PlayFireAndForget(string fileName)
    {
        try
        {
            var path = Path.Combine(AudioDirectory, fileName);
            if (!File.Exists(path))
            {
                return; // A dev build run outside publish/, or a stripped tree — never fatal.
            }

            var reader = new AudioFileReader(path) { Volume = _volume };
            var output = new WaveOutEvent();
            output.PlaybackStopped += (_, _) =>
            {
                ActivePlayers.TryRemove(output, out _);
                output.Dispose();
                reader.Dispose();
            };
            ActivePlayers.TryAdd(output, reader);
            output.Init(reader);
            output.Play();
        }
        catch
        {
            // Sound is decoration, never a reason to crash or interrupt play
            // (e.g. no audio device on the machine / in a CI runner).
        }
    }
}
