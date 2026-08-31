using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Economy;
using ChronTravelers.Core.Events;
using ChronTravelers.Core.Ions;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.Time;
using ChronTravelers.Core.World;
using ChronTravelers.Engine;
using ChronTravelers.Engine.Combat;
using ChronTravelers.Engine.Content;
using ChronTravelers.Engine.Npc;
using ChronTravelers.Engine.Persistence;
using ChronTravelers.Engine.Simulation;
using Spectre.Console;

// Lore (docs/GDD.md §1): Project Meridian's temporal tunnel - a
// classified government machine, Time-Tunnel-inspired - tore a standing
// rupture on its first full-power run and "frayed" the downstream
// timeline. The crew on the gantry, the Chron Travelers, were swept
// loose and now surface at random years between 2000 and 5000 A.D.,
// unable to steer. You ride Ion surges (tunnel-charge) to move through
// the fray; the surface team never stops looking, but it can't pull you
// back. Push deepest downstream and level up - that's the board.
//
// The world is a continuous timeline (docs/GDD.md §3.2): the player starts
// in the year 2000 A.D. and `travel`s - spending Ions - to any year up to
// 5000, with monsters and loot scaling smoothly by year. Nothing gates
// travel; the only limits are the Ion cost (ceil(0.04 * |Δyear|),
// symmetric) and how hard the fights get. Every year's map is generated
// deterministically from a per-save world seed, so revisiting a year is
// stable. "Warden" years - a random 50-100 years apart, placed by the
// seed - station an automated temporal-defense construct guarding a
// year-scaled Legendary trophy from a pre-collapse tech cache, but
// block nothing.
//
// Monsters are placed spatially in the year the player is standing in
// (ChronTravelers.Core.Time.YearPopulation, seeded deterministically on first
// entry): they occupy grid rooms, drift slowly and randomly between them
// (low per-tick move chance, no fixed heading, frequent pauses - so a
// player heading for one on the `monsters` list actually finds it near
// where it was), fight each other, scavenge from the floor only when they
// need Ion fuel or a weapon upgrade (otherwise they step over a pile),
// heal from their own Ion pool, and slowly respawn toward a soft cap.
// `fight` engages a monster in the current room; its loot (rolled drops +
// anything it scavenged) falls where it dies - nothing auto-enters your
// pack, `take` it off the floor. Every year
// that's been instantiated this session keeps simulating each tick
// (ChronTravelers.Engine.Npc.MonsterController via WorldSimulation.Tick) -
// the player's year with full aggro/ambush/narration, every other one
// unattended (monsters still infight, drop loot, heal and respawn while
// you're away). Years nobody has entered stay dormant. None of this is
// written to the save (a fresh session re-seeds).
// Monsters ignore passers-by: each carries an earned aggro meter
// (ChronTravelers.Core.Monsters.AggroModel) raised by stepping onto its tile
// repeatedly / lingering on it / being shot, and decaying when you leave.
// Calm -> wanders and ignores you; Alert -> shadows you but no swing;
// Hostile -> also lands one ambush hit, but only on an idle turn (look /
// status / wait / ...), never while you're acting (move / fight / heal /
// shop / wield) and never into a store room (a haven). `monsters` shows
// each one's mood. A few years also seed one or two `IsApex` monsters
// ("Frayed <species>") - much tougher, better loot, and accruing aggro at
// a fraction of the normal rate so they essentially never provoke: the
// player chooses to take one on or walk past. The inline kill-feed after
// each command shows only events in the player's own year (plus any
// ambush on the player); everything elsewhere in the timeline is a count,
// with `news` still showing the lot.
//
// Ranged weapons (ChronTravelers.Core.Items.Item / RangedKind - wands, bows,
// later guns) reach one room away: `wield` one into its own slot, then
// `point <dir>` (wands) or `shoot <dir>` (bows/guns) down an exit to hit
// the first monster there via ChronTravelers.Engine.Combat.RangedResolver. Each
// carries a finite built-in magazine (AmmoRemaining/AmmoCapacity) that
// round-trips through the save; once spent the weapon can't fire and is
// worth only a fraction on `convert`/`sell`. A Weaken wand leaves the
// target fighting at reduced defence for its next `fight`.
//
// Content is data-driven (ChronTravelers.Content/*.json - monster-species,
// item-archetypes, eras, store-templates - loaded by
// ChronTravelers.Engine.Content.ContentLoader.LoadTimeWorld), falling back to
// ChronTravelers.Core.Time.TestTimeWorld's tiny 3-era sandbox if the files are
// missing/malformed. Ability tables (abilities.json) load too and execute
// via Engine.Combat.CombatSession: the player's own `fight` is
// interactive and round-by-round.
//
// All game rules (movement, combat, NPC AI, store transactions, travel,
// persistence, character state) live in ChronTravelers.Core / ChronTravelers.Engine;
// this file is presentation/input only, per docs/AGENTS.md's Console/UI
// contract. Input is read via plain Console.ReadLine() (Spectre's
// interactive prompts hard-fail on redirected stdin); Spectre is still
// used for all output styling.
//
// Only the player's own character is saved (Persistence.CharacterSaveData,
// which also carries the world seed) - NPCs are re-simulated fresh each
// session, scattered across the whole timeline, and only contribute their
// personal bests to the leaderboard. The save/leaderboard DB lives at
// %APPDATA%\ChronTravelers\chrontravelers.db, and carries the world seed, the
// current/furthest year, the cleared Warden years, and every store
// the player owns (year + capital + listings, re-attached on load).

AnsiConsole.Write(new FigletText("ChronTravelers").Color(Color.Green));
AnsiConsole.MarkupLine("[grey](pre-release build — the continuous 2000–5000 A.D. timeline)[/]");
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[grey]Project Meridian's tunnel opened for eight seconds and never fully closed.[/]");
AnsiConsole.MarkupLine("[grey]It frayed the future. The gantry crew fell downstream with it — you among them,[/]");
AnsiConsole.MarkupLine("[grey]surfacing somewhere between 2000 and 5000 A.D. with no way to steer and no way home.[/]");
AnsiConsole.MarkupLine("[grey]Ride the Ion surges. Go as far downstream as you can. The surface team is still looking.[/]");
AnsiConsole.WriteLine();

var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var savesDirectory = string.IsNullOrEmpty(appDataFolder)
    ? "saves"
    : Path.Combine(appDataFolder, "ChronTravelers");
Directory.CreateDirectory(savesDirectory);
var savePath = Path.Combine(savesDirectory, "chrontravelers.db");
using var repository = new GameRepository(savePath);

RenderLeaderboards(repository);
AnsiConsole.WriteLine();

var abilities = LoadAbilities();
var random = new SystemRandomSource();

var start = HandleStartScreen(repository);
if (start is null)
{
    return;
}

var (traveler, worldSeed, loadedSave) = start.Value;
var world = LoadTimeWorld(worldSeed);

// Place / re-place the character now that the world exists.
var startingRoom = world.GetYear(traveler.CurrentYear).Map;
if (startingRoom.TryGetRoom(traveler.Position) is null)
{
    traveler.PlaceAt(startingRoom.Start);
}

// Re-attach any stores this character owned in a previous session.
if (loadedSave is not null)
{
    CharacterMapper.ApplyOwnedStores(loadedSave, traveler, world);
}
else
{
    // Starter kit for a fresh character — a few field rations so the
    // first year isn't a pure attrition race before you can loot/buy any
    // HP recovery of your own (playtested).
    for (var i = 0; i < 3; i++)
    {
        traveler.AddToInventory(Item.Create("Field Ration", ItemType.Consumable, 1, Rarity.Common,
            consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 12));
    }
}

var npcs = SpawnNpcs(world, random);
var simulation = new WorldSimulation(world, npcs, random);
var shownBroadcastCount = 0;

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"Welcome, [bold]{Markup.Escape(traveler.Name)}[/] the [bold]{traveler.Class}[/]. Type [yellow]help[/] for commands.");
AnsiConsole.MarkupLine($"[grey]{npcs.Count} other Travelers are scattered across the centuries, fending for themselves.[/]");
AnsiConsole.WriteLine();

RenderRoom(traveler, world);

var running = true;
while (running)
{
    AnsiConsole.Markup("[green]>[/] ");
    var rawInput = Console.ReadLine();
    if (rawInput is null)
    {
        break;
    }

    var input = rawInput.Trim();
    if (input.Length == 0)
    {
        continue;
    }

    // `look` and a successful move render the room *after* this turn's
    // world tick (see the end of the loop), so "a Scavenger is here" still
    // holds when your next command runs — rendering before the tick showed
    // a monster that had already wandered off by the time you typed fight.
    var renderRoomAfterTick = false;

    switch (input.ToLowerInvariant())
    {
        case "quit" or "exit":
            running = false;
            break;

        case "help" or "?":
            RenderHelp();
            break;

        case "look" or "l":
            renderRoomAfterTick = true;
            break;

        case "status" or "stat":
            RenderStatusBar(traveler, world);
            break;

        case "wait" or "z":
            AnsiConsole.MarkupLine("[grey]You wait a moment.[/] (a monster in the room may get a hit in — see [yellow]help[/])");
            break;

        case "heal":
            HandleHeal(traveler);
            break;

        case "abilities" or "spells":
            RenderAbilities(traveler, abilities);
            break;

        case "inventory" or "inv" or "i" or "bag":
            RenderInventory(traveler);
            break;

        case "npcs" or "who":
            RenderNpcs(npcs);
            break;

        case "monsters" or "mobs":
            RenderMonsters(traveler, world);
            break;

        case "news" or "broadcast":
            RenderBroadcast(simulation.Broadcast, count: 10);
            shownBroadcastCount = simulation.Broadcast.Events.Count;
            break;

        case "stores":
            RenderStores(world.GetYear(traveler.CurrentYear).StoreSlots);
            break;

        case "shop":
            HandleShop(traveler, world);
            break;

        case "buy-store":
            HandleBuyStore(traveler, world);
            break;

        case "collect":
            HandleCollect(traveler, world);
            break;

        case "save":
            HandleSave(traveler, repository, worldSeed, world);
            RecordNpcLeaderboardBests(npcs, repository);
            break;

        case "leaderboard" or "board":
            RenderLeaderboards(repository, traveler.Name);
            break;

        default:
            var (command, argument) = SplitCommand(input);

            // `look <dir>` — peek into the adjacent room without moving.
            // (Bare `look`/`l` is handled in the switch above.)
            if (command is "look" or "l")
            {
                HandleLookDirection(traveler, world, argument);
                break;
            }

            // "attack"/"atk"/"a" are the in-combat verbs; accepting them out
            // here as a `fight` alias means the extra lines a player (or a
            // piped script) mashes during a fight land harmlessly once it
            // ends — re-engaging if something's still in the room, or a plain
            // "nothing here" — instead of an "Unrecognized command" error.
            if (command is "fight" or "f" or "attack" or "atk" or "a")
            {
                if (!HandleFight(traveler, world, random, simulation.Broadcast, abilities, argument))
                {
                    running = false;
                }

                break;
            }

            if (command is "take" or "grab" or "pickup" or "get")
            {
                HandleTake(traveler, world, argument);
                break;
            }

            if (command is "shoot" or "point" or "fire")
            {
                HandleShoot(traveler, world, random, simulation.Broadcast, command, argument);
                break;
            }

            if (command is "travel")
            {
                HandleTravel(traveler, world, random, simulation.Broadcast, argument);
                break;
            }

            if (command is "sell")
            {
                HandleSellToStore(traveler, world, argument);
                break;
            }

            if (command is "buy")
            {
                HandleBuyFromStore(traveler, world, argument);
                break;
            }

            if (command is "deposit" or "withdraw" or "reprice")
            {
                HandleStoreManagement(traveler, world, command, argument);
                break;
            }

            if (TryHandleItemCommand(traveler, command, argument))
            {
                break;
            }

            var direction = DirectionExtensions.Parse(input);
            if (direction is null)
            {
                AnsiConsole.MarkupLine($"[red]Unrecognized command:[/] '{input}'. Type [yellow]help[/] for a list.");
                break;
            }

            renderRoomAfterTick = HandleMove(traveler, world, direction.Value);
            break;
    }

    if (running && !traveler.Health.IsDead)
    {
        simulation.Tick(traveler, playerActedIdly: IsIdleCommand(input));

        foreach (var line in simulation.LastTickNarration)
        {
            AnsiConsole.MarkupLine($"[grey italic]{Markup.Escape(line)}[/]");
        }

        shownBroadcastCount = RenderNewBroadcastEvents(simulation.Broadcast, shownBroadcastCount, traveler.CurrentYear);

        if (traveler.Health.IsDead)
        {
            // A monster sharing the room struck the killing blow this tick (see MonsterController's ambush).
            AnsiConsole.MarkupLine("[red]You're struck down where you stand.[/]");
            running = false;
        }
        else if (renderRoomAfterTick)
        {
            RenderRoom(traveler, world);
        }
    }
}

if (!traveler.Health.IsDead)
{
    HandleSave(traveler, repository, worldSeed, world);
}

RecordNpcLeaderboardBests(npcs, repository);

AnsiConsole.MarkupLine(traveler.Health.IsDead
    ? "[grey]Game over.[/]"
    : "[grey]Farewell, Traveler. Progress saved.[/]");
return;

static void HandleSave(Traveler traveler, GameRepository repository, long worldSeed, TimeWorld world)
{
    repository.SaveCharacter(CharacterMapper.ToSaveData(traveler, worldSeed, CollectOwnedStores(traveler, world)));
    repository.RecordPersonalBests(traveler.Name, isPlayer: true, traveler.FurthestYearReached, traveler.Level);
    AnsiConsole.MarkupLine("[green]Game saved.[/]");
}

/// <summary>The player's stores, keyed by the year each is in — gathered from every year visited this session (see TimeWorld.VisitedYears).</summary>
static Dictionary<int, Store> CollectOwnedStores(Traveler player, TimeWorld world)
{
    var owned = new Dictionary<int, Store>();
    foreach (var year in world.VisitedYears)
    {
        var slot = world.GetYear(year).StoreSlots.FirstOrDefault(s => s.Store?.Owner == player);
        if (slot is not null)
        {
            owned[year] = slot.Store!;
        }
    }

    return owned;
}

/// <summary>NPCs aren't saved as full characters (see file header), but their personal bests still count toward the leaderboard - docs/GDD.md §8's "across player + NPCs."</summary>
static void RecordNpcLeaderboardBests(IReadOnlyList<Traveler> npcs, GameRepository repository)
{
    foreach (var npc in npcs)
    {
        repository.RecordPersonalBests(npc.Name, isPlayer: false, npc.FurthestYearReached, npc.Level);
    }
}

static string? ReadNonEmptyLine(string prompt)
{
    while (true)
    {
        AnsiConsole.Markup(Markup.Escape(prompt));
        var line = Console.ReadLine();
        if (line is null)
        {
            return null;
        }

        var trimmed = line.Trim();
        if (trimmed.Length > 0)
        {
            return trimmed;
        }
    }
}

static CharacterClass? ReadClassChoice()
{
    var classes = Enum.GetValues<CharacterClass>();

    AnsiConsole.MarkupLine("Choose your [green]role[/] on the Meridian crew:");
    for (var i = 0; i < classes.Length; i++)
    {
        AnsiConsole.MarkupLine($"  [green]{i + 1}[/]. [bold]{classes[i]}[/] [grey]- {ClassBlurb(classes[i])}[/]");
    }

    while (true)
    {
        AnsiConsole.Markup("[green]>[/] ");
        var line = Console.ReadLine();
        if (line is null)
        {
            return null;
        }

        var trimmed = line.Trim();

        if (int.TryParse(trimmed, out var choice) && choice >= 1 && choice <= classes.Length)
        {
            return classes[choice - 1];
        }

        if (Enum.TryParse<CharacterClass>(trimmed, ignoreCase: true, out var byName) && Enum.IsDefined(byName))
        {
            return byName;
        }

        AnsiConsole.MarkupLine("[red]Please enter a number from the list, or a role name.[/]");
    }
}

/// <summary>One-line flavour for the role-select screen (docs/GDD.md §4). Mechanics live in ChronTravelers.Core.Classes.</summary>
static string ClassBlurb(CharacterClass characterClass) => characterClass switch
{
    CharacterClass.Soldier => "station security. Toughest, hardest-hitting, cheapest on Ions.",
    CharacterClass.Spy => "recon and infiltration. Fast, evasive, deadly on the opening strike.",
    CharacterClass.Doctor => "trauma medicine. Keeps you and any allies standing; wrecks fray-echoes.",
    CharacterClass.Scientist => "tunnel theory. Glass cannon — huge Ion damage, little armour.",
    CharacterClass.Engineer => "power and hardware. Control, sabotage, and dirty micro-jumps.",
    _ => string.Empty,
};

static string ContentDirectory() => Path.Combine(AppContext.BaseDirectory, "Content");

/// <summary>
/// Loads the real, authored timeline from ChronTravelers.Content. Falls back to
/// ChronTravelers.Core.Time.TestTimeWorld's small sandbox if the content files
/// are missing or malformed, so a broken deployment degrades to something
/// playable instead of crashing. <paramref name="worldSeed"/> fixes the
/// Warden schedule and every year's map/store layout.
/// </summary>
static TimeWorld LoadTimeWorld(long worldSeed)
{
    try
    {
        return ContentLoader.LoadTimeWorld(ContentDirectory(), worldSeed);
    }
    catch (ContentException ex)
    {
        AnsiConsole.MarkupLine($"[red]Couldn't load content ({Markup.Escape(ex.Message)}) - falling back to the built-in sandbox timeline.[/]");
        return TestTimeWorld.Build(worldSeed);
    }
}

/// <summary>Loads abilities.json's mechanical ability tables (see AbilityData). An empty list on missing/malformed content just means 'abilities' and 'cast' have nothing to show/use.</summary>
static IReadOnlyList<AbilityData> LoadAbilities()
{
    try
    {
        return ContentLoader.LoadAbilities(Path.Combine(ContentDirectory(), "abilities.json"));
    }
    catch (ContentException)
    {
        return [];
    }
}

/// <summary>Spawns the whole NPC population (npc-population.json's totalCount), scattered across the timeline. Falls back to 12 if the config is missing/malformed.</summary>
static List<Traveler> SpawnNpcs(TimeWorld world, IRandomSource random)
{
    int count;
    try
    {
        count = ContentLoader.LoadNpcCount(Path.Combine(ContentDirectory(), "npc-population.json"));
    }
    catch (ContentException)
    {
        count = 12;
    }

    return NpcPopulation.Spawn(count, world, random).ToList();
}

/// <summary>The title-screen "new game or load a save" flow. Returns null only on end-of-input (quit); otherwise the character, the world seed to build the timeline from, and (for a loaded game) the raw save data so owned stores can be re-attached once the world exists.</summary>
static (Traveler Traveler, long WorldSeed, CharacterSaveData? LoadedSave)? HandleStartScreen(GameRepository repository)
{
    var savedNames = repository.ListSavedCharacterNames();
    if (savedNames.Count > 0)
    {
        AnsiConsole.MarkupLine($"[yellow]Saved characters:[/] {string.Join(", ", savedNames.Select(Markup.Escape))}");
        AnsiConsole.MarkupLine("Type [green]new[/] to create a Traveler, or a saved name to continue them.");
    }
    else
    {
        AnsiConsole.MarkupLine("No saved characters yet. Type [green]new[/] to create a Traveler.");
    }

    string choice;
    while (true)
    {
        var input = ReadNonEmptyLine("> ");
        if (input is null)
        {
            return null;
        }

        if (string.Equals(input, "new", StringComparison.OrdinalIgnoreCase))
        {
            choice = "new";
            break;
        }

        var matchedName = savedNames.FirstOrDefault(n => string.Equals(n, input, StringComparison.OrdinalIgnoreCase));
        if (matchedName is not null)
        {
            choice = matchedName;
            break;
        }

        AnsiConsole.MarkupLine("[red]Type 'new', or the exact name of a saved character.[/]");
    }

    if (choice == "new")
    {
        var name = ReadNonEmptyLine("What is your name, Traveler? ");
        if (name is null)
        {
            return null;
        }

        var characterClass = ReadClassChoice();
        if (characterClass is null)
        {
            return null;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]You were on the gantry when the tunnel lit. The next thing you knew, the lab was gone[/]");
        AnsiConsole.MarkupLine($"[grey]and the sky was the wrong colour. Wherever — whenever — this is, you're the {characterClass} now, and[/]");
        AnsiConsole.MarkupLine($"[grey]you're on your own. Downstream is the only direction that means anything.[/]");

        return (new Traveler(name, characterClass.Value), System.Random.Shared.NextInt64(), null);
    }

    var saveData = repository.LoadCharacter(choice)!;
    var loaded = CharacterMapper.FromSaveData(saveData);
    var seed = saveData is { SchemaVersion: >= 2, WorldSeed: not 0 }
        ? saveData.WorldSeed
        : System.Random.Shared.NextInt64();

    if (saveData.SchemaVersion < CharacterSaveData.CurrentSchemaVersion)
    {
        AnsiConsole.MarkupLine("[yellow]This save predates the fray rework — your Traveler carries over, dropped into the year that matches its old depth downstream. The timeline is freshly generated.[/]");
    }

    AnsiConsole.MarkupLine($"[green]Welcome back, {Markup.Escape(loaded.Name)}![/]");
    return (loaded, seed, saveData);
}

static void RenderLeaderboards(GameRepository repository, string? highlightName = null)
{
    AnsiConsole.MarkupLine("[yellow]═══ Leaderboards ═══[/]");
    RenderLeaderboardBoard(repository, "Furthest Year Reached", repository.TopByFurthestYear(10), e => e.FurthestYearReached, highlightName);
    RenderLeaderboardBoard(repository, "Highest Character Level", repository.TopByCharacterLevel(10), e => e.HighestCharacterLevelReached, highlightName);
}

static void RenderLeaderboardBoard(
    GameRepository repository, string title, IReadOnlyList<LeaderboardEntry> top,
    Func<LeaderboardEntry, int> value, string? highlightName)
{
    if (top.Count == 0)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(title)}: no records yet.[/]");
        return;
    }

    var table = new Table().Title(Markup.Escape(title)).Expand();
    table.AddColumn("#");
    table.AddColumn("Name");
    table.AddColumn("Value");
    table.AddColumn("Who");

    foreach (var (entry, index) in top.Select((e, i) => (e, i)))
    {
        var isHighlighted = highlightName is not null && string.Equals(entry.Name, highlightName, StringComparison.OrdinalIgnoreCase);
        var nameCell = isHighlighted ? $"[bold green]{Markup.Escape(entry.Name)} (you)[/]" : Markup.Escape(entry.Name);
        table.AddRow((index + 1).ToString(), nameCell, value(entry).ToString(), entry.IsPlayer ? "player" : "NPC");
    }

    if (highlightName is not null && !top.Any(e => string.Equals(e.Name, highlightName, StringComparison.OrdinalIgnoreCase)))
    {
        var own = repository.GetLeaderboardEntry(highlightName);
        if (own is not null)
        {
            table.AddRow("-", $"[bold green]{Markup.Escape(own.Name)} (you)[/]", value(own).ToString(), "player");
        }
    }

    AnsiConsole.Write(table);
}

static (string Command, string Argument) SplitCommand(string input)
{
    var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return parts.Length switch
    {
        0 => ("", ""),
        1 => (parts[0].ToLowerInvariant(), ""),
        _ => (parts[0].ToLowerInvariant(), parts[1]),
    };
}

/// <summary>
/// True if <paramref name="input"/> is an informational no-op — checking
/// the room, your sheet, the map, the feed. Only on such a turn can a
/// monster in your room ambush you (see ChronTravelers.Engine.Npc.MonsterController):
/// anything that actually does something (move, fight, shoot, heal, shop,
/// wield, travel, take, …) is safe, as is an unrecognised command.
/// </summary>
static bool IsIdleCommand(string input) => SplitCommand(input).Command is
    "look" or "l" or "status" or "stat" or "wait" or "z" or "help" or "?"
    or "inventory" or "inv" or "i" or "bag" or "abilities" or "spells" or "npcs" or "who"
    or "monsters" or "mobs" or "news" or "broadcast" or "stores"
    or "leaderboard" or "board";

/// <summary>Handles "convert/wield/use/eat/drink &lt;item&gt;" commands. Returns false if <paramref name="command"/> isn't one of those verbs.</summary>
static bool TryHandleItemCommand(Traveler traveler, string command, string argument)
{
    if (command is not ("convert" or "wield" or "use" or "eat" or "drink"))
    {
        return false;
    }

    var item = FindInventoryItem(traveler, argument);
    if (item is null)
    {
        AnsiConsole.MarkupLine(argument.Length == 0
            ? $"[red]{command} what?[/] Try '{command} 1' or '{command} <item name>'. Type [yellow]inventory[/] to see what you're carrying."
            : $"[red]No item matching '{Markup.Escape(argument)}' in your inventory.[/]");
        return true;
    }

    switch (command)
    {
        case "convert":
            if (!traveler.Ions.Uncapped && traveler.Ions.Current >= traveler.Ions.Max)
            {
                AnsiConsole.MarkupLine($"[grey]Your Ion pool is full — converting {Markup.Escape(item.Name)} now would waste it. Sell it, or spend some Ions first.[/]");
                break;
            }

            var ions = traveler.Convert(item);
            AnsiConsole.MarkupLine($"[blue]Converted {Markup.Escape(item.Name)} for {ions} Ions.[/] ({Markup.Escape(IonText(traveler))})");
            break;

        case "wield":
            if (!item.IsWieldable)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(item.Name)} can't be wielded.[/]");
                break;
            }

            traveler.Wield(item);
            var penalty = item.IsClassCompatible(traveler.Class) ? "" : " [red](off-class - reduced effectiveness)[/]";
            AnsiConsole.MarkupLine($"[green]Wielded {Markup.Escape(item.Name)}.[/]{penalty}");
            break;

        case "use" or "eat" or "drink":
            if (!item.IsUsable)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(item.Name)} can't be used.[/]");
                break;
            }

            var effect = item.ConsumableEffect;
            var healed = traveler.Consume(item);
            AnsiConsole.MarkupLine(effect switch
            {
                ConsumableEffectType.Heal =>
                    $"[green]You use {Markup.Escape(item.Name)} and heal for {healed} HP.[/] ({traveler.Health.Current}/{traveler.Health.Max} HP)",
                ConsumableEffectType.BuffAttack =>
                    $"[green]You use {Markup.Escape(item.Name)}. Your attack is bolstered for {item.EffectDurationTicks} ticks.[/]",
                ConsumableEffectType.BuffDefense =>
                    $"[green]You use {Markup.Escape(item.Name)}. Your defenses are bolstered for {item.EffectDurationTicks} ticks.[/]",
                _ => $"[green]You use {Markup.Escape(item.Name)}.[/]",
            });
            break;
    }

    return true;
}

static Item? FindInventoryItem(Traveler traveler, string argument)
{
    if (argument.Length == 0)
    {
        return null;
    }

    if (int.TryParse(argument, out var index) && index >= 1 && index <= traveler.Inventory.Count)
    {
        return traveler.Inventory[index - 1];
    }

    // Exact name wins; otherwise any item whose name contains the text
    // (so `wield marshal` finds "Marshal's Repeater" — names wrap in the
    // inventory table and carry apostrophes, so exact-only is a trap).
    return traveler.Inventory.FirstOrDefault(i => string.Equals(i.Name, argument, StringComparison.OrdinalIgnoreCase))
        ?? traveler.Inventory.FirstOrDefault(i => i.Name.Contains(argument, StringComparison.OrdinalIgnoreCase));
}

static StoreListing? FindListing(Store store, string argument)
{
    if (argument.Length == 0)
    {
        return null;
    }

    if (int.TryParse(argument, out var index) && index >= 1 && index <= store.Listings.Count)
    {
        return store.Listings[index - 1];
    }

    return store.Listings.FirstOrDefault(l => string.Equals(l.Item.Name, argument, StringComparison.OrdinalIgnoreCase))
        ?? store.Listings.FirstOrDefault(l => l.Item.Name.Contains(argument, StringComparison.OrdinalIgnoreCase));
}

static StoreSlot? FindStoreSlotAt(IReadOnlyList<StoreSlot> storeSlots, Coordinate position) =>
    storeSlots.FirstOrDefault(s => s.Location == position);

/// <summary>Splits "&lt;item&gt; &lt;price&gt;" - the last whitespace token is the price, everything before it is the item name/index.</summary>
static (string ItemArg, int Price)? SplitItemAndPrice(string argument)
{
    var tokens = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length < 2 || !int.TryParse(tokens[^1], out var price) || price < 1)
    {
        return null;
    }

    return (string.Join(' ', tokens[..^1]), price);
}

static void HandleShop(Traveler traveler, TimeWorld world)
{
    var storeSlots = world.GetYear(traveler.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, traveler.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]There's no store here.[/]");
        return;
    }

    RenderShop(store);
}

static void HandleBuyFromStore(Traveler traveler, TimeWorld world, string argument)
{
    var storeSlots = world.GetYear(traveler.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, traveler.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]There's no store here to buy from.[/]");
        return;
    }

    var listing = FindListing(store, argument);
    if (listing is null)
    {
        AnsiConsole.MarkupLine(argument.Length == 0
            ? "[red]Buy what?[/] Type [yellow]shop[/] to see what's for sale."
            : $"[red]'{Markup.Escape(argument)}' isn't for sale here.[/]");
        return;
    }

    if (traveler.Credits < listing.AskingPrice)
    {
        AnsiConsole.MarkupLine($"[red]You can't afford {Markup.Escape(listing.Item.Name)} ({listing.AskingPrice} Credits; you have {traveler.Credits}).[/]");
        return;
    }

    store.SellToTraveler(traveler, listing);
    AnsiConsole.MarkupLine($"[green]Bought {Markup.Escape(listing.Item.Name)} for {listing.AskingPrice} Credits.[/]");
}

static void HandleSellToStore(Traveler traveler, TimeWorld world, string argument)
{
    var storeSlots = world.GetYear(traveler.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, traveler.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]You need to be at a store to sell.[/] Try [yellow]convert[/] to destroy an item for Ions instead, or [yellow]stores[/] to find one.");
        return;
    }

    // 'sell all' / 'sell junk' — clears the vendor trash (Junk items only;
    // gear and consumables you keep unless you name them).
    if (argument.Trim() is "all" or "junk" or "*")
    {
        var junk = traveler.Inventory.Where(i => i.Type == ItemType.Junk).ToList();
        if (junk.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No junk to sell.[/] Name an item to sell that instead.");
            return;
        }

        var total = 0;
        var count = 0;
        foreach (var j in junk)
        {
            var got = store.BuyFromTraveler(traveler, j);
            if (got is null)
            {
                break;
            }

            total += got.Value;
            count++;
        }

        AnsiConsole.MarkupLine($"[yellow]Sold {count} junk item(s) to {Markup.Escape(store.Name)} for {total} Credits.[/]");
        return;
    }

    var item = FindInventoryItem(traveler, argument);
    if (item is null)
    {
        AnsiConsole.MarkupLine(argument.Length == 0
            ? "[red]Sell what?[/] Type [yellow]inventory[/] to see what you're carrying, or [yellow]sell all[/] to dump junk."
            : $"[red]No item matching '{Markup.Escape(argument)}' in your inventory.[/]");
        return;
    }

    var price = store.BuyFromTraveler(traveler, item);
    if (price is null)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(store.Name)} can't afford to buy that right now.[/]");
        return;
    }

    AnsiConsole.MarkupLine($"[yellow]Sold {Markup.Escape(item.Name)} to {Markup.Escape(store.Name)} for {price} Credits.[/]");
}

static void HandleBuyStore(Traveler traveler, TimeWorld world)
{
    var storeSlots = world.GetYear(traveler.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, traveler.Position);
    if (slot is null)
    {
        AnsiConsole.MarkupLine("[red]There's no store slot here.[/]");
        return;
    }

    if (!slot.IsAvailableForPurchase)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(slot.Name)} is already occupied.[/]");
        return;
    }

    if (traveler.Credits < slot.PurchaseCost)
    {
        AnsiConsole.MarkupLine($"[red]You need {slot.PurchaseCost} Credits to buy this slot; you have {traveler.Credits}.[/]");
        return;
    }

    slot.Purchase(traveler);
    AnsiConsole.MarkupLine($"[green]You now own a store here: {Markup.Escape(slot.Store!.Name)}![/] Use [yellow]deposit[/]/[yellow]withdraw[/]/[yellow]reprice[/]/[yellow]collect[/] to run it. It'll still be yours next session.");
}

/// <summary>Collects from every store the Traveler owns across every year visited this session — an owner needn't be standing there (docs/GDD.md §6.2's "idle-income loop").</summary>
static void HandleCollect(Traveler traveler, TimeWorld world)
{
    var owned = world.VisitedYears
        .SelectMany(y => world.GetYear(y).StoreSlots)
        .Where(s => s.Store?.Owner == traveler)
        .ToList();

    if (owned.Count == 0)
    {
        AnsiConsole.MarkupLine("[red]You don't own a store.[/] Find an empty slot and use [yellow]buy-store[/].");
        return;
    }

    var totalCollected = 0;
    foreach (var slot in owned)
    {
        var capital = slot.Store!.Capital;
        if (capital > 0)
        {
            totalCollected += slot.Store.CollectCapital(traveler, capital);
        }
    }

    AnsiConsole.MarkupLine(totalCollected > 0
        ? $"[yellow]Collected {totalCollected} Credits from your store(s).[/]"
        : "[grey]Nothing to collect yet.[/]");
}

/// <summary>docs/GDD.md §2 [SOURCE]: "spend Ions to heal wounds directly," usable at any time.</summary>
static void HandleHeal(Traveler traveler)
{
    if (traveler.Health.Current >= traveler.Health.Max)
    {
        AnsiConsole.MarkupLine("[grey]You're already at full health.[/]");
        return;
    }

    if (traveler.Ions.Current <= 0)
    {
        AnsiConsole.MarkupLine("[red]Not enough Ions to heal.[/]");
        return;
    }

    var healed = traveler.Heal();
    AnsiConsole.MarkupLine($"[green]You heal for {healed} HP.[/] ({traveler.Health.Current}/{traveler.Health.Max} HP, {Markup.Escape(IonText(traveler))} left)");
}

/// <summary>Player Ion readout — just the number when the pool is uncapped (no "/max" ceiling to show), "current/max" otherwise.</summary>
static string IonText(Traveler traveler) => traveler.Ions.Uncapped
    ? $"{traveler.Ions.Current} Ions"
    : $"{traveler.Ions.Current}/{traveler.Ions.Max} Ions";

static void HandleStoreManagement(Traveler traveler, TimeWorld world, string command, string argument)
{
    var storeSlots = world.GetYear(traveler.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, traveler.Position);
    if (slot?.Store is not { } store || store.Owner != traveler)
    {
        AnsiConsole.MarkupLine("[red]You need to be at a store you own to do that.[/]");
        return;
    }

    switch (command)
    {
        case "withdraw":
            var listing = FindListing(store, argument);
            if (listing is null)
            {
                AnsiConsole.MarkupLine($"[red]No listing matching '{Markup.Escape(argument)}' at {Markup.Escape(store.Name)}.[/]");
                return;
            }

            store.Withdraw(traveler, listing);
            AnsiConsole.MarkupLine($"[green]Withdrew {Markup.Escape(listing.Item.Name)} back into your inventory.[/]");
            break;

        case "deposit":
        {
            var split = SplitItemAndPrice(argument);
            if (split is null)
            {
                AnsiConsole.MarkupLine("[red]Usage: deposit <item> <price>[/]");
                return;
            }

            var (itemArg, price) = split.Value;
            var item = FindInventoryItem(traveler, itemArg);
            if (item is null)
            {
                AnsiConsole.MarkupLine($"[red]No item matching '{Markup.Escape(itemArg)}' in your inventory.[/]");
                return;
            }

            store.Deposit(traveler, item, price);
            AnsiConsole.MarkupLine($"[green]Listed {Markup.Escape(item.Name)} at {Markup.Escape(store.Name)} for {price} Credits.[/]");
            break;
        }

        case "reprice":
        {
            var split = SplitItemAndPrice(argument);
            if (split is null)
            {
                AnsiConsole.MarkupLine("[red]Usage: reprice <item> <new price>[/]");
                return;
            }

            var (itemArg, price) = split.Value;
            var listingToReprice = FindListing(store, itemArg);
            if (listingToReprice is null)
            {
                AnsiConsole.MarkupLine($"[red]No listing matching '{Markup.Escape(itemArg)}' at {Markup.Escape(store.Name)}.[/]");
                return;
            }

            store.AdjustPrice(traveler, listingToReprice, price);
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(listingToReprice.Item.Name)} is now {price} Credits.[/]");
            break;
        }
    }
}

/// <summary>
/// Resolves one fight against a monster standing in the player's current
/// room (or that year's Warden, stationed at the map's start room in
/// a Warden year the player hasn't cleared). <paramref name="targetName"/>
/// picks one when several share the room; empty takes the first.
/// Interactive and round-by-round via CombatSession — "attack" or "cast
/// <ability>" each round. On a win the monster is removed from the year's
/// live population and its loot (table roll + anything it had scavenged)
/// goes to the player. Returns false if the Traveler was defeated (caller
/// ends the session). End-of-input mid-fight auto-attacks each remaining
/// round.
/// </summary>
static bool HandleFight(Traveler traveler, TimeWorld world, IRandomSource random, BroadcastChannel broadcast, IReadOnlyList<AbilityData> abilities, string targetName)
{
    var year = traveler.CurrentYear;
    var yearContent = world.GetYear(year);
    var population = yearContent.Population;

    Monster monster;
    var isWardenFight = false;

    var warden = population.Warden;
    if (warden is not null && !warden.Health.IsDead
        && !traveler.HasDefeatedWarden(year)
        && traveler.Position.Equals(warden.Position))
    {
        monster = warden;
        isWardenFight = true;
    }
    else
    {
        var here = population.MonstersAt(traveler.Position).ToList();
        if (here.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing here to fight.[/] Monsters roam the rooms — go find one.");
            return true;
        }

        monster = targetName.Length > 0
            ? here.FirstOrDefault(m => m.Name.Contains(targetName, StringComparison.OrdinalIgnoreCase)) ?? here[0]
            // A bare 'fight' never picks the apex by accident — name it to take it on.
            : here.FirstOrDefault(m => !m.IsApex) ?? here[0];
    }

    var levelBefore = traveler.Level;

    AnsiConsole.MarkupLine(isWardenFight
        ? $"[bold]{Markup.Escape(monster.Name)} rises to meet you![/] (tier {monster.Tier})"
        : $"You close on the [bold]{Markup.Escape(monster.Name)}[/] (tier {monster.Tier})!");

    var usableAbilities = abilities
        .Where(a => string.Equals(a.Class, traveler.Class.ToString(), StringComparison.OrdinalIgnoreCase) && a.Level <= traveler.Level)
        .ToList();

    var session = new CombatSession(traveler, monster, random);
    var loggedSoFar = 0;

    while (!session.IsOver)
    {
        AnsiConsole.Markup("[green]  (attack)[/] or [green]cast <ability>[/]? > ");
        var rawInput = Console.ReadLine();

        if (rawInput is not null)
        {
            var trimmed = rawInput.Trim();

            if (trimmed.Length > 0 && !string.Equals(trimmed, "attack", StringComparison.OrdinalIgnoreCase) && !string.Equals(trimmed, "a", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("cast", StringComparison.OrdinalIgnoreCase))
                {
                    var abilityName = trimmed.Length > 4 ? trimmed[4..].Trim() : "";
                    var ability = usableAbilities.FirstOrDefault(a => string.Equals(a.Name, abilityName, StringComparison.OrdinalIgnoreCase));
                    if (ability is null)
                    {
                        AnsiConsole.MarkupLine($"[red]No ability named '{Markup.Escape(abilityName)}' available.[/] Type 'abilities' outside combat to see your list.");
                        continue;
                    }

                    var castResult = session.Cast(ability);
                    AnsiConsole.MarkupLine(castResult.Success ? $"[blue]{Markup.Escape(castResult.Message)}[/]" : $"[red]{Markup.Escape(castResult.Message)}[/]");
                    if (!castResult.Success)
                    {
                        continue;
                    }

                    PrintNewLogLines(session, ref loggedSoFar);
                    continue;
                }

                AnsiConsole.MarkupLine("[red]Type 'attack' or 'cast <ability name>'.[/]");
                continue;
            }
        }

        session.Attack();
        PrintNewLogLines(session, ref loggedSoFar);
    }

    var foe = monster.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
        ? monster.Name
        : $"the {monster.Name}";

    if (session.TravelerWon)
    {
        AnsiConsole.MarkupLine($"[green]You defeated {Markup.Escape(foe)}! +{session.XpAwarded} XP.[/]");
        broadcast.Publish(GameEvent.Slain(monster.Name, traveler.Name, year, victimIsCreature: true));

        // Loot never auto-enters the pack — it falls where the fight was
        // (the rolled drops plus anything the monster had scavenged). Walk
        // it off the floor with `take`.
        var dropped = session.ItemsDropped.Concat(monster.Inventory).ToList();
        foreach (var item in dropped)
        {
            population.AddGroundLoot(traveler.Position, item);
        }

        if (isWardenFight)
        {
            traveler.RecordWardenDefeat(year);
            var trophy = session.ItemsDropped.FirstOrDefault();
            AnsiConsole.MarkupLine(trophy is not null
                ? $"[bold yellow]The Warden of {year} falls. Its {Markup.Escape(trophy.Name)} lies at your feet — [yellow]take[/] it.[/]"
                : $"[bold]The Warden of {year} is broken. This year is yours.[/]");
        }
        else
        {
            population.RemoveMonster(monster);
            if (dropped.Count > 0)
            {
                AnsiConsole.MarkupLine($"[green]It drops {Markup.Escape(NameList(dropped.Select(i => i.Name).ToList()))} on the ground.[/] [grey](take <item>)[/]");
            }
        }

        if (traveler.Level > levelBefore)
        {
            broadcast.Publish(GameEvent.LevelReached(traveler.Name, traveler.Level, year));
        }

        RenderStatusBar(traveler, world);
        return true;
    }

    // docs/GDD.md §3.3 (death & recall) is not implemented yet; a defeat here just ends the session.
    AnsiConsole.MarkupLine($"[red]You were defeated by {Markup.Escape(foe)}...[/]");
    broadcast.Publish(GameEvent.Slain(traveler.Name, monster.Name, year, killerIsCreature: true));
    return false;
}

static void PrintNewLogLines(CombatSession session, ref int loggedSoFar)
{
    for (var i = loggedSoFar; i < session.Log.Count; i++)
    {
        AnsiConsole.MarkupLine(Markup.Escape(session.Log[i]));
    }

    loggedSoFar = session.Log.Count;
}

/// <summary>
/// Fires the readied ranged weapon (Traveler.EquippedRanged) one room away
/// in an exit direction — 'point &lt;dir&gt;' for a Wand, 'shoot &lt;dir&gt;'
/// for a Bow/Gun — hitting the first living monster there (or that room's
/// stationed Warden). A hit spends one round of the weapon's built-in
/// ammo via ChronTravelers.Engine.Combat.RangedResolver; no target or no exit that
/// way spends nothing. On a kill, XP and loot are awarded here — the loot
/// lands on the target room's floor, since the player never walked in.
/// Softening a monster with a Weaken wand carries into the next 'fight'
/// (CombatSession consumes Monster.PendingDefensePenalty once).
/// </summary>
static void HandleShoot(Traveler traveler, TimeWorld world, IRandomSource random, BroadcastChannel broadcast, string verb, string argument)
{
    var weapon = traveler.EquippedRanged;
    if (weapon is null)
    {
        AnsiConsole.MarkupLine("[red]You have no ranged weapon readied.[/] [yellow]wield[/] a wand, bow, or gun first.");
        return;
    }

    if (weapon.IsDepleted)
    {
        AnsiConsole.MarkupLine($"[red]Your {Markup.Escape(weapon.Name)} is spent — [yellow]convert[/] or [yellow]sell[/] it.[/]");
        return;
    }

    var direction = DirectionExtensions.Parse(argument.Trim());
    if (direction is null)
    {
        AnsiConsole.MarkupLine($"[red]{verb} which way?[/] Try '{verb} north'.");
        return;
    }

    var yearContent = world.GetYear(traveler.CurrentYear);
    if (!yearContent.Map.GetRoom(traveler.Position).ExitDescriptions.ContainsKey(direction.Value))
    {
        AnsiConsole.MarkupLine("[red]You can't shoot through a wall.[/] There's no exit that way.");
        return;
    }

    var targetRoom = traveler.Position.Move(direction.Value);
    var population = yearContent.Population;

    var target = population.MonstersAt(targetRoom).FirstOrDefault(m => !m.Health.IsDead);
    var warden = population.Warden;
    var targetIsWarden = false;
    if (target is null
        && warden is not null && !warden.Health.IsDead
        && !traveler.HasDefeatedWarden(traveler.CurrentYear)
        && warden.Position.Equals(targetRoom))
    {
        target = warden;
        targetIsWarden = true;
    }

    if (target is null)
    {
        AnsiConsole.MarkupLine($"[grey]Nothing to {verb} that way.[/] (no shot spent)");
        return;
    }

    var levelBefore = traveler.Level;
    var result = RangedResolver.Fire(traveler, target, weapon, random);
    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(result.Message)}[/]");
    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(weapon.Name)}: {weapon.AmmoRemaining}/{weapon.AmmoCapacity} shots left.[/]");

    if (!result.Killed)
    {
        return;
    }

    broadcast.Publish(GameEvent.Slain(target.Name, traveler.Name, traveler.CurrentYear, victimIsCreature: true));
    traveler.GainXp(target.XpReward);

    var drops = LootDropRoller.RollForKill(target, random).Concat(target.Inventory).ToList();

    if (targetIsWarden)
    {
        traveler.RecordWardenDefeat(traveler.CurrentYear);
        foreach (var drop in drops)
        {
            population.AddGroundLoot(targetRoom, drop);
        }

        AnsiConsole.MarkupLine($"[bold yellow]You drop the Warden of {traveler.CurrentYear} from a room away — its trophy lies to the {direction.Value.Name()} ({Markup.Escape(targetRoom.ToString())}).[/] +{target.XpReward} XP.");
    }
    else
    {
        population.RemoveMonster(target);
        foreach (var drop in drops)
        {
            population.AddGroundLoot(targetRoom, drop);
        }

        AnsiConsole.MarkupLine(drops.Count > 0
            ? $"[green]The {Markup.Escape(target.Name)} drops. +{target.XpReward} XP. Its loot is on the floor to the {direction.Value.Name()} — walk in and [yellow]take[/] it.[/]"
            : $"[green]The {Markup.Escape(target.Name)} drops. +{target.XpReward} XP.[/]");
    }

    if (traveler.Level > levelBefore)
    {
        broadcast.Publish(GameEvent.LevelReached(traveler.Name, traveler.Level, traveler.CurrentYear));
    }
}

/// <summary>Lists the player's class's abilities - locked ones greyed, the handful with no combat effect flagged.</summary>
static void RenderAbilities(Traveler traveler, IReadOnlyList<AbilityData> abilities)
{
    var classAbilities = abilities
        .Where(a => string.Equals(a.Class, traveler.Class.ToString(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(a => a.Tier)
        .ToList();

    if (classAbilities.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]No ability data loaded.[/]");
        return;
    }

    var table = new Table().Expand();
    table.AddColumn("Level");
    table.AddColumn("Name");
    table.AddColumn("Ion Cost");
    table.AddColumn("Description");
    table.AddColumn("Status");

    foreach (var ability in classAbilities)
    {
        var unlocked = traveler.Level >= ability.Level;
        var hasCombatEffect = !string.Equals(ability.Effect, "None", StringComparison.OrdinalIgnoreCase);
        var status = !unlocked
            ? "[grey]locked[/]"
            : hasCombatEffect ? "[green]ready[/]" : "[yellow]no combat effect[/]";

        table.AddRow(
            ability.Level.ToString(),
            Markup.Escape(ability.Name),
            ability.IonCost.ToString(),
            Markup.Escape(ability.Description),
            status);
    }

    AnsiConsole.Write(table);
}

/// <summary>Handles "travel &lt;year&gt;", "travel +N"/"-N" (relative years), and "travel next"/"prev" (the next/previous Warden year) — docs/GDD.md §3.2.</summary>
static void HandleTravel(Traveler traveler, TimeWorld world, IRandomSource random, BroadcastChannel broadcast, string argument)
{
    var arg = argument.Trim();
    int targetYear;

    switch (arg.ToLowerInvariant())
    {
        case "":
            AnsiConsole.MarkupLine("[red]Travel when?[/] Try 'travel 3200', 'travel +150', 'travel -100', or 'travel next'/'prev' (Warden years).");
            return;

        case "next":
        {
            var next = world.Wardens.NextAfter(traveler.CurrentYear);
            if (next is null)
            {
                AnsiConsole.MarkupLine("[grey]No Warden years remain ahead of you.[/]");
                return;
            }

            targetYear = next.Value;
            break;
        }

        case "prev" or "previous":
        {
            var prev = world.Wardens.PreviousBefore(traveler.CurrentYear);
            if (prev is null)
            {
                AnsiConsole.MarkupLine("[grey]No Warden years behind you.[/]");
                return;
            }

            targetYear = prev.Value;
            break;
        }

        default:
            if ((arg.StartsWith('+') || arg.StartsWith('-')) && int.TryParse(arg, out var delta))
            {
                targetYear = traveler.CurrentYear + delta;
            }
            else if (int.TryParse(arg, out var absolute))
            {
                targetYear = absolute;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]'travel' needs a year (e.g. 3200), a relative offset (+150 / -100), or 'next'/'prev'.[/]");
                return;
            }

            break;
    }

    if (targetYear == traveler.CurrentYear)
    {
        AnsiConsole.MarkupLine("[grey]You're already there.[/]");
        return;
    }

    if (!TimeScale.IsValidYear(targetYear))
    {
        AnsiConsole.MarkupLine($"[red]The timeline only runs from {TimeScale.MinYear} to {TimeScale.MaxYear} A.D.[/]");
        return;
    }

    var cost = IonEconomy.TimeTravelCost(traveler.CurrentYear, targetYear);
    var yearGap = Math.Abs(targetYear - traveler.CurrentYear);
    var targetTier = TimelineContentFactory.DisplayTier(targetYear);
    // Well above your level band — allowed (that's how you go loot-hunting
    // in the deep future), but you should know what you're walking into.
    var overreaching = traveler.Level < 10 * (targetTier - 2);

    if (yearGap > 500 || overreaching)
    {
        if (overreaching)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Heads up:[/] {targetYear} A.D. is around tier {targetTier} — its monsters and loot " +
                $"scale to roughly level {10 * targetTier}, and you're level {traveler.Level}. " +
                "Better gear if you can grab it and run; a quick death if you can't.");
        }

        AnsiConsole.Markup($"[yellow]That's a {yearGap}-year jump — {cost} Ions (you have {traveler.Ions.Current}). Proceed? (y/n)[/] ");
        var confirm = Console.ReadLine();
        if (confirm is null || !confirm.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[grey]Stayed put.[/]");
            return;
        }
    }

    var levelBefore = traveler.Level;
    var result = TimeTravelResolver.Travel(traveler, world, targetYear, random);

    if (!result.Success)
    {
        AnsiConsole.MarkupLine(result.FailureReason switch
        {
            TimeTravelFailureReason.YearOutOfRange => $"[red]{targetYear} is off the timeline (2000–5000).[/]",
            TimeTravelFailureReason.InsufficientIons =>
                $"[red]Not enough Ions ({cost} needed; you have {traveler.Ions.Current}).[/]",
            _ => "[red]Travel failed.[/]",
        });
        return;
    }

    if (traveler.Level > levelBefore)
    {
        broadcast.Publish(GameEvent.LevelReached(traveler.Name, traveler.Level, targetYear));
    }

    var arrival = world.GetYear(targetYear);
    AnsiConsole.MarkupLine($"[bold]You travel to {targetYear} A.D. — {Markup.Escape(arrival.Era.Name)}.[/] [grey]({result.IonsSpent} Ions)[/]");
    broadcast.Publish(GameEvent.TimeTraveled(traveler.Name, targetYear));
    RenderRoom(traveler, world);
}

static void RenderNpcs(IReadOnlyList<Traveler> npcs)
{
    var table = new Table().Expand();
    table.AddColumn("Name");
    table.AddColumn("Class");
    table.AddColumn("Level");
    table.AddColumn("Year");
    table.AddColumn("HP");
    table.AddColumn("Ions");
    table.AddColumn("Location");
    table.AddColumn("Status");

    foreach (var npc in npcs.OrderBy(n => n.CurrentYear))
    {
        table.AddRow(
            Markup.Escape(npc.Name),
            npc.Class.ToString(),
            npc.Level.ToString(),
            npc.CurrentYear.ToString(),
            $"{npc.Health.Current}/{npc.Health.Max}",
            $"{npc.Ions.Current}/{npc.Ions.Max}",
            Markup.Escape(npc.Position.ToString()),
            npc.Health.IsDead ? "[red]defeated[/]" : "[green]active[/]");
    }

    AnsiConsole.Write(table);
}

/// <summary>Lists the monsters roaming the player's current year (see YearPopulation), the one the player is standing with marked.</summary>
static void RenderMonsters(Traveler traveler, TimeWorld world)
{
    var population = world.GetYear(traveler.CurrentYear).Population;
    var living = population.Monsters.Where(m => !m.Health.IsDead).ToList();

    if (population.Warden is { Health.IsDead: false } gk && !traveler.HasDefeatedWarden(traveler.CurrentYear))
    {
        living.Insert(0, gk);
    }

    if (living.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]No monsters roaming this year right now — they'll trickle back.[/]");
        return;
    }

    var here = living.Count(m => m.Position.Equals(traveler.Position));
    AnsiConsole.MarkupLine(here > 0
        ? $"[grey]{living.Count} monster(s) roaming — [red]{here} in your room[/].[/]"
        : $"[grey]{living.Count} monster(s) roaming this year.[/]");

    var table = new Table().Expand();
    table.AddColumn("Name");
    table.AddColumn("Tier");
    table.AddColumn("HP");
    table.AddColumn("Ions");
    table.AddColumn("Mood");
    table.AddColumn("Location");

    foreach (var m in living.OrderBy(m => m.Position.Equals(traveler.Position) ? 0 : 1).ThenBy(m => m.Position.North).ThenBy(m => m.Position.East))
    {
        var loc = Markup.Escape(m.Position.ToString())
            + (m.Position.Equals(traveler.Position) ? " [green](here)[/]"
               : m.Heading is { } hd ? $" [grey]drifting {hd.Name()}[/]"
               : "");
        var mood = AggroModel.MoodFor(m.Aggro) switch
        {
            AggroMood.Hostile => "[red]hostile[/]",
            AggroMood.Alert => "[yellow]alert[/]",
            _ => "[grey]calm[/]",
        };
        var name = m.IsApex ? $"[bold red]{Markup.Escape(m.Name)}[/] [red](apex)[/]" : Markup.Escape(m.Name);
        table.AddRow(name, m.Tier.ToString(), $"{m.Health.Current}/{m.Health.Max}", $"{m.Ions.Current}/{m.Ions.Max}", mood, loc);
    }

    AnsiConsole.Write(table);
}

/// <summary>Picks up ground loot at the player's coordinate — 'take &lt;item&gt;' (name or number) or 'take all'.</summary>
static void HandleTake(Traveler traveler, TimeWorld world, string argument)
{
    var population = world.GetYear(traveler.CurrentYear).Population;
    var pile = population.LootAt(traveler.Position);
    if (pile.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]Nothing on the ground here.[/]");
        return;
    }

    var arg = argument.Trim();
    if (arg.Length == 0 || string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
    {
        Item? item;
        while ((item = population.TakeGroundLoot(traveler.Position, _ => true)) is not null)
        {
            traveler.AddToInventory(item);
            AnsiConsole.MarkupLine($"[green]You pick up the {Markup.Escape(item.Name)}.[/]");
        }

        return;
    }

    Item? match = null;
    if (int.TryParse(arg, out var index) && index >= 1 && index <= pile.Count)
    {
        match = pile[index - 1];
    }
    else
    {
        match = pile.FirstOrDefault(i => i.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }

    if (match is null)
    {
        AnsiConsole.MarkupLine($"[red]No '{Markup.Escape(arg)}' on the ground here.[/] It holds: {Markup.Escape(NameList(pile.Select(i => i.Name).ToList()))}.");
        return;
    }

    var picked = population.TakeGroundLoot(traveler.Position, i => ReferenceEquals(i, match));
    if (picked is not null)
    {
        traveler.AddToInventory(picked);
        AnsiConsole.MarkupLine($"[green]You pick up the {Markup.Escape(picked.Name)}.[/]");
    }
}

/// <summary>"A, B and C" — a readable comma list.</summary>
static string NameList(IReadOnlyList<string> names) => names.Count switch
{
    0 => "nothing",
    1 => names[0],
    2 => $"{names[0]} and {names[1]}",
    _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
};

static void RenderBroadcast(BroadcastChannel broadcast, int count)
{
    var recent = broadcast.Recent(count);
    if (recent.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]Nothing has happened yet.[/]");
        return;
    }

    AnsiConsole.MarkupLine("[cyan]Recent broadcasts:[/]");
    foreach (var evt in recent)
    {
        AnsiConsole.MarkupLine($"  [cyan]*[/] {Markup.Escape(evt.Message)}");
    }
}

/// <summary>
/// Prints broadcast events published since <paramref name="alreadyShownCount"/>,
/// as an inline feed after a command. Only events in the player's own year
/// (<paramref name="playerYear"/>) — plus any not tied to a year, and any
/// ambush on the player — show inline; everything happening elsewhere in
/// the timeline is the bulk of the channel and is noise here, summarised
/// as a count and left for the full <c>news</c> view. Returns the new
/// total (all events count as "seen" so they don't resurface later).
/// </summary>
static int RenderNewBroadcastEvents(BroadcastChannel broadcast, int alreadyShownCount, int playerYear)
{
    const int InlineFeedCap = 6;

    var events = broadcast.Events;
    var fresh = new List<GameEvent>();
    var elsewhere = 0;
    for (var i = alreadyShownCount; i < events.Count; i++)
    {
        var evt = events[i];
        if (evt.Kind == GameEventKind.TimeTraveled)
        {
            continue; // NPC time-hops: always news-only
        }

        var local = evt.Year is null || evt.Year == playerYear || evt.Kind == GameEventKind.Ambushed;
        if (local)
        {
            fresh.Add(evt);
        }
        else
        {
            elsewhere++;
        }
    }

    // Keep the tail and summarise the rest so a busy tick doesn't bury the
    // room — but an ambush on the player is never dropped.
    var shown = fresh.Count <= InlineFeedCap
        ? fresh
        : fresh.Where(e => e.Kind == GameEventKind.Ambushed)
            .Concat(fresh.Where(e => e.Kind != GameEventKind.Ambushed).TakeLast(InlineFeedCap))
            .ToList();

    foreach (var evt in shown)
    {
        var colour = evt.Kind == GameEventKind.Ambushed ? "red" : "cyan";
        AnsiConsole.MarkupLine($"[{colour}]* {Markup.Escape(evt.Message)}[/]");
    }

    var hidden = (fresh.Count - shown.Count) + elsewhere;
    if (hidden > 0)
    {
        AnsiConsole.MarkupLine($"[grey]  …and {hidden} more across the timeline (see [yellow]news[/]).[/]");
    }

    return events.Count;
}

static void RenderStores(IReadOnlyList<StoreSlot> storeSlots)
{
    if (storeSlots.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]No stores this year.[/]");
        return;
    }

    var table = new Table().Expand();
    table.AddColumn("Name");
    table.AddColumn("Location");
    table.AddColumn("Owner");
    table.AddColumn("Items");
    table.AddColumn("Status");

    foreach (var slot in storeSlots)
    {
        var owner = slot.Store switch
        {
            null => "-",
            { IsGovernmentRun: true } => "[grey]Government[/]",
            var s => Markup.Escape(s.Owner!.Name),
        };
        var items = slot.Store?.Listings.Count.ToString() ?? "-";
        var status = slot.IsAvailableForPurchase ? $"[yellow]for sale ({slot.PurchaseCost} Credits)[/]" : "occupied";

        table.AddRow(Markup.Escape(slot.Name), Markup.Escape(slot.Location.ToString()), owner, items, status);
    }

    AnsiConsole.Write(table);
}

static void RenderShop(Store store)
{
    AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(store.Name)}[/] — Capital: {store.Capital} Credits");

    if (store.Listings.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]Nothing for sale right now.[/]");
        return;
    }

    var table = new Table().Expand();
    table.AddColumn("#");
    table.AddColumn("Name");
    table.AddColumn("Type");
    table.AddColumn("Tier");
    table.AddColumn("Rarity");
    table.AddColumn("Price");

    for (var i = 0; i < store.Listings.Count; i++)
    {
        var listing = store.Listings[i];
        table.AddRow(
            (i + 1).ToString(),
            Markup.Escape(listing.Item.Name),
            listing.Item.Type.ToString(),
            listing.Item.Tier.ToString(),
            listing.Item.Rarity.ToString(),
            listing.AskingPrice.ToString());
    }

    AnsiConsole.Write(table);
}

static void RenderInventory(Traveler traveler)
{
    if (traveler.Inventory.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]Your inventory is empty.[/]");
        return;
    }

    var table = new Table().Expand();
    table.AddColumn("#");
    table.AddColumn("Name");
    table.AddColumn("Type");
    table.AddColumn("Tier");
    table.AddColumn("Rarity");
    table.AddColumn("Value");
    table.AddColumn("Effect");
    table.AddColumn("Equipped");

    for (var i = 0; i < traveler.Inventory.Count; i++)
    {
        var item = traveler.Inventory[i];
        var equipped = item == traveler.EquippedWeapon || item == traveler.EquippedArmor || ReferenceEquals(item, traveler.EquippedRanged) ? "yes" : "";
        var effect = item.ConsumableEffect switch
        {
            ConsumableEffectType.Heal => $"heals {item.EffectMagnitude:0} HP",
            ConsumableEffectType.BuffAttack => $"+{item.EffectMagnitude:0} attack ({item.EffectDurationTicks} ticks)",
            ConsumableEffectType.BuffDefense => $"+{item.EffectMagnitude:0} defense ({item.EffectDurationTicks} ticks)",
            _ => item.IsRanged
                ? (item.IsDepleted
                    ? $"{item.RangedKind} — spent (convert/sell only)"
                    : $"{item.RangedKind} — {item.AmmoRemaining}/{item.AmmoCapacity} shots" + (item.RangedEffect != RangedEffectType.None ? $", {item.RangedEffect}" : ""))
                : "",
        };
        table.AddRow(
            (i + 1).ToString(),
            Markup.Escape(item.Name),
            item.Type.ToString(),
            item.Tier.ToString(),
            item.Rarity.ToString(),
            item.Value.ToString(),
            effect,
            equipped);
    }

    AnsiConsole.Write(table);
}

/// <summary>Moves the player one room. Returns true on a successful move — the caller renders the room after the world tick, so what you see (monsters here / nearby) matches what the next command will act on.</summary>
static bool HandleMove(Traveler traveler, TimeWorld world, Direction direction)
{
    var map = world.GetYear(traveler.CurrentYear).Map;
    var result = map.TryMove(traveler.Position, direction);
    if (!result.Success)
    {
        AnsiConsole.MarkupLine("[red]You can't go that way.[/]");
        return false;
    }

    traveler.MoveTo(result.Destination!.Value);
    return true;
}

/// <summary>`look &lt;dir&gt;` — peek into the adjacent room (its description, who's standing in it, what's on the floor) without stepping in.</summary>
static void HandleLookDirection(Traveler traveler, TimeWorld world, string argument)
{
    var direction = DirectionExtensions.Parse(argument.Trim());
    if (direction is null)
    {
        AnsiConsole.MarkupLine("[red]Look where?[/] Try [yellow]look north[/] (or just [yellow]look[/] for the room you're in).");
        return;
    }

    var yearContent = world.GetYear(traveler.CurrentYear);
    var step = yearContent.Map.TryMove(traveler.Position, direction.Value);
    if (!step.Success || step.DestinationRoom is null)
    {
        AnsiConsole.MarkupLine($"[grey]You look {direction.Value.Name()} — solid wall, no way through.[/]");
        return;
    }

    var there = step.Destination!.Value;
    AnsiConsole.MarkupLine($"[grey]To the {direction.Value.Name()} ({Markup.Escape(there.ToString())}):[/] {Markup.Escape(step.DestinationRoom.Description)}");

    var population = yearContent.Population;

    var warden = population.Warden;
    if (warden is not null && !warden.Health.IsDead
        && !traveler.HasDefeatedWarden(traveler.CurrentYear)
        && warden.Position.Equals(there))
    {
        AnsiConsole.MarkupLine($"  [bold red]{Markup.Escape(warden.Name)} stands watch there.[/]");
    }

    var whoLine = population.MonstersAt(there)
        .OrderBy(m => m.IsApex ? 0 : 1)
        .Select(m => m.IsApex
            ? $"[bold red]{Markup.Escape(m.Name)}[/] [red](apex)[/]"
            : Markup.Escape(m.Name))
        .ToList();
    AnsiConsole.MarkupLine(whoLine.Count > 0
        ? $"  [red]You can make out {NameListMarkup(whoLine)} in there.[/]"
        : "  [grey]Nothing moving that you can see.[/]");

    var ground = population.LootAt(there);
    if (ground.Count > 0)
    {
        AnsiConsole.MarkupLine($"  [yellow]On the floor:[/] {Markup.Escape(NameList(ground.Select(i => i.Name).ToList()))}.");
    }
}

/// <summary>Like <see cref="NameList"/> but the parts already carry markup, so it doesn't escape them.</summary>
static string NameListMarkup(IReadOnlyList<string> names) => names.Count switch
{
    0 => "nothing",
    1 => names[0],
    2 => $"{names[0]} and {names[1]}",
    _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
};

static void RenderRoom(Traveler traveler, TimeWorld world)
{
    var yearContent = world.GetYear(traveler.CurrentYear);
    var room = yearContent.Map.GetRoom(traveler.Position);

    RenderStatusBar(traveler, world);
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine(Markup.Escape(room.Description));

    var exitDirections = room.ExitDescriptions.Keys.OrderBy(d => d.Name()).ToList();
    if (exitDirections.Count > 0)
    {
        var hint = string.Join(", ", exitDirections.Select(d => d.Name()));
        AnsiConsole.MarkupLine($"[green]You see exits to the {hint}.[/]");
        foreach (var direction in exitDirections)
        {
            AnsiConsole.MarkupLine($"  [green]{direction.Name()}[/] - {Markup.Escape(room.ExitDescriptions[direction])}");
        }
    }
    else
    {
        AnsiConsole.MarkupLine("[green]There are no exits. You are stuck.[/]");
    }

    var population = yearContent.Population;

    var monstersHere = population.MonstersAt(traveler.Position).ToList();
    var apexHere = monstersHere.Where(m => m.IsApex).ToList();
    var here = monstersHere.Where(m => !m.IsApex).Select(m => m.Name).ToList();
    var warden = population.Warden;
    var wardenHere = warden is not null && !warden.Health.IsDead
        && !traveler.HasDefeatedWarden(traveler.CurrentYear)
        && warden.Position.Equals(traveler.Position);

    if (wardenHere)
    {
        AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(warden!.Name)} stands watch here. [yellow]fight[/] when you're ready.[/]");
    }

    foreach (var apex in apexHere)
    {
        AnsiConsole.MarkupLine($"[bold red]A {Markup.Escape(apex.Name)} is here — far bigger and harder than the rest, and it hasn't reacted to you.[/] [grey](fight {Markup.Escape(apex.Name.Split(' ')[^1])} — or leave it be)[/]");
    }

    if (here.Count > 0)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(NameList(here))} {(here.Count == 1 ? "is" : "are")} here.[/] [grey](fight{(here.Count > 1 ? " <name>" : "")})[/]");
    }

    foreach (var direction in exitDirections)
    {
        var adjacent = traveler.Position.Move(direction);
        if (!population.HasLivingMonsterAt(adjacent))
        {
            continue;
        }

        // Vary the phrasing, but keep it stable for a given room+direction.
        var flavour = (Math.Abs(adjacent.East * 31 + adjacent.North * 17 + (int)direction) % 4) switch
        {
            0 => $"You hear something to the {direction.Name()}.",
            1 => $"Something stirs to the {direction.Name()}.",
            2 => $"There's movement in the room to the {direction.Name()}.",
            _ => $"Something shuffles about to the {direction.Name()}.",
        };
        AnsiConsole.MarkupLine($"[grey]{flavour}[/]");
    }

    var ground = population.LootAt(traveler.Position);
    if (ground.Count > 0)
    {
        AnsiConsole.MarkupLine($"[yellow]On the ground:[/] {Markup.Escape(NameList(ground.Select(i => i.Name).ToList()))}. [grey](take <item>)[/]");
    }

    var slot = FindStoreSlotAt(yearContent.StoreSlots, traveler.Position);
    if (slot is not null)
    {
        AnsiConsole.MarkupLine(slot.Store switch
        {
            null => $"[cyan]There's an empty storefront here — '{Markup.Escape("buy-store")}' to claim it for {slot.PurchaseCost} Credits.[/]",
            { IsGovernmentRun: true } => $"[cyan]There's a store here: {Markup.Escape(slot.Store.Name)}. Type 'shop' to browse.[/]",
            _ => $"[cyan]There's a player-owned store here: {Markup.Escape(slot.Store.Name)} (owned by {Markup.Escape(slot.Store.Owner!.Name)}). Type 'shop' to browse.[/]",
        });
    }

    AnsiConsole.WriteLine();
}

static void RenderStatusBar(Traveler traveler, TimeWorld world)
{
    var yearContent = world.GetYear(traveler.CurrentYear);
    var status = $"[red]HP {traveler.Health.Current}/{traveler.Health.Max}[/]  " +
                 $"[blue]{Markup.Escape(IonText(traveler))}[/]  " +
                 $"[yellow]Credits {traveler.Credits}[/]  " +
                 $"Char Level {traveler.Level}  " +
                 $"Year {traveler.CurrentYear} A.D.  " +
                 $"Furthest {traveler.FurthestYearReached}  " +
                 $"Location {Markup.Escape(traveler.Position.ToString())}";

    if (traveler.EquippedRanged is { } ranged)
    {
        status += ranged.IsDepleted
            ? $"\n[grey]Ranged: {Markup.Escape(ranged.Name)} (spent)[/]"
            : $"\n[blue]Ranged: {Markup.Escape(ranged.Name)} — {ranged.AmmoRemaining}/{ranged.AmmoCapacity} shots[/]";
    }

    if (traveler.ActiveEffects.Count > 0)
    {
        var effects = traveler.ActiveEffects.Select(e => e.Type switch
        {
            ConsumableEffectType.BuffAttack => $"+{e.Magnitude:0} attack ({e.TicksRemaining} ticks left)",
            ConsumableEffectType.BuffDefense => $"+{e.Magnitude:0} defense ({e.TicksRemaining} ticks left)",
            _ => e.Type.ToString(),
        });
        status += $"\n[green]Active: {string.Join(", ", effects)}[/]";
    }

    AnsiConsole.Write(new Panel(status)
        .Header($"[bold]{Markup.Escape(traveler.Name)}[/] — {Markup.Escape(yearContent.Era.Name)}, {traveler.CurrentYear} A.D.")
        .Expand());
}

static void RenderHelp()
{
    AnsiConsole.MarkupLine("[yellow]Commands:[/]");
    AnsiConsole.MarkupLine("  [green]n[/]/[green]s[/]/[green]e[/]/[green]w[/] (or north/south/east/west) - move");
    AnsiConsole.MarkupLine("  [green]look[/] (or l)         - redescribe the current room (monsters here / nearby, ground loot)");
    AnsiConsole.MarkupLine("  [green]look <dir>[/]          - peek into the adjacent room (what's there, on the floor) without moving");
    AnsiConsole.MarkupLine("  [green]fight[/] (or f, attack, a) [green]<name>[/] - fight a monster in this room (or the Warden at the year's start)");
    AnsiConsole.MarkupLine("    (each round, type [green]attack[/] or [green]cast <ability>[/])");
    AnsiConsole.MarkupLine("  [green]shoot[/]/[green]point <dir>[/] - fire your readied ranged weapon one room away (finite built-in ammo)");
    AnsiConsole.MarkupLine("  [green]take[/] (or grab) [green]<item>[/] - pick up loot off the ground here ('take all' works)");
    AnsiConsole.MarkupLine("  [green]monsters[/] (or mobs)  - list the monsters roaming this year");
    AnsiConsole.MarkupLine("  [green]heal[/]                - spend Ions to recover HP (usable any time)");
    AnsiConsole.MarkupLine("  [green]abilities[/] (or spells) - list your class's abilities unlocked so far");
    AnsiConsole.MarkupLine("  [green]travel <year>[/]      - jump to a year (2000–5000); costs ceil(0.04·|Δyear|) Ions");
    AnsiConsole.MarkupLine("  [green]travel +N[/]/[green]-N[/]      - jump N years forward/back");
    AnsiConsole.MarkupLine("  [green]travel next[/]/[green]prev[/]   - jump to the next/previous Warden year");
    AnsiConsole.MarkupLine("  [green]inventory[/] (or inv, i, bag) - list what you're carrying");
    AnsiConsole.MarkupLine("  [green]npcs[/] (or who)       - list the other Travelers out in the timeline");
    AnsiConsole.MarkupLine("  [green]news[/] (or broadcast) - show recent kill-feed events");
    AnsiConsole.MarkupLine("  [green]convert <item>[/]     - destroy an item for Ions (a spent ranged weapon is worth a fraction)");
    AnsiConsole.MarkupLine("  [green]wield <item>[/]       - equip a weapon, armor, or ranged (wand/bow/gun) item");
    AnsiConsole.MarkupLine("  [green]use[/]/[green]eat[/]/[green]drink <item>[/] - consume a potion or food item");
    AnsiConsole.MarkupLine("    ('<item>' is either its inventory number or its name)");
    AnsiConsole.MarkupLine("  [green]stores[/]              - list every store this year");
    AnsiConsole.MarkupLine("  [green]shop[/]                - browse the store in your current room");
    AnsiConsole.MarkupLine("  [green]buy <item>[/]         - buy a listed item (must be at a store)");
    AnsiConsole.MarkupLine("  [green]sell <item>[/] / [green]sell all[/] - sell one item, or dump all junk, to the store here");
    AnsiConsole.MarkupLine("  [green]buy-store[/]           - purchase an empty store slot you're standing in");
    AnsiConsole.MarkupLine("  [green]deposit <item> <price>[/] - list your own item for sale at your store");
    AnsiConsole.MarkupLine("  [green]withdraw <item>[/]    - pull a listing back into your inventory");
    AnsiConsole.MarkupLine("  [green]reprice <item> <price>[/] - change a listing's asking price");
    AnsiConsole.MarkupLine("  [green]collect[/]             - withdraw your store's earnings into your Credits");
    AnsiConsole.MarkupLine("  [green]save[/]                - save your character now");
    AnsiConsole.MarkupLine("  [green]leaderboard[/] (or board) - show the leaderboards, your best highlighted");
    AnsiConsole.MarkupLine("  [green]status[/]              - show the status bar");
    AnsiConsole.MarkupLine("  [green]wait[/] (or z)         - pass a turn (a monster in the room may get a hit in)");
    AnsiConsole.MarkupLine("  [green]help[/] (or ?)         - show this list");
    AnsiConsole.MarkupLine("  [green]quit[/] (or exit)      - leave the game (auto-saves unless you died)");
    AnsiConsole.MarkupLine("[grey]  Monsters ignore you until provoked — mostly by stepping onto their tile over and[/]");
    AnsiConsole.MarkupLine("[grey]  over, or shooting them ([yellow]monsters[/] shows each one's mood: calm/alert/hostile).[/]");
    AnsiConsole.MarkupLine("[grey]  Only a hostile monster in your room hits you, and only on an idle turn[/]");
    AnsiConsole.MarkupLine("[grey]  (look/status/wait/…). Acting is safe, a store room is a haven, and moving a[/]");
    AnsiConsole.MarkupLine("[grey]  couple of rooms away calms a monster back down.[/]");
    AnsiConsole.MarkupLine("[grey]  Movement near you is called out — something coming into earshot, entering, or[/]");
    AnsiConsole.MarkupLine("[grey]  leaving your room, with its direction; [yellow]monsters[/] shows each one's position.[/]");
    AnsiConsole.MarkupLine("[grey]  A few years hold an [red](apex)[/] — much tougher, barely provokable, better loot.[/]");
    AnsiConsole.MarkupLine("[grey]  It leaves you alone; [yellow]fight <name>[/] it only if you want what it's carrying.[/]");
}
