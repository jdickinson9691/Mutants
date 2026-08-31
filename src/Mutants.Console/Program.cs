using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Events;
using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Time;
using Mutants.Core.World;
using Mutants.Engine;
using Mutants.Engine.Combat;
using Mutants.Engine.Content;
using Mutants.Engine.Npc;
using Mutants.Engine.Persistence;
using Mutants.Engine.Simulation;
using Spectre.Console;

// The world is a continuous timeline (docs/GDD.md §3.2): the player starts
// in the year 2000 A.D. and `travel`s - spending Ions - to any year up to
// 5000, with monsters and loot scaling smoothly by year. Nothing gates
// travel; the only limits are the Ion cost (ceil(0.2 * |Δyear|),
// symmetric) and how hard the fights get. Every year's map is generated
// deterministically from a per-save world seed, so revisiting a year is
// stable. "Gatekeeper" years - a random 50-100 years apart, placed by the
// seed - hold a tough guaranteed encounter guarding a year-scaled
// Legendary trophy, but block nothing.
//
// Content is data-driven (Mutants.Content/*.json - monster-species,
// item-archetypes, eras, store-templates - loaded by
// Mutants.Engine.Content.ContentLoader.LoadTimeWorld), falling back to
// Mutants.Core.Time.TestTimeWorld's tiny 3-era sandbox if the files are
// missing/malformed. Ability tables (abilities.json) load too and execute
// via Engine.Combat.CombatSession: the player's own `fight` is
// interactive and round-by-round.
//
// All game rules (movement, combat, NPC AI, store transactions, travel,
// persistence, character state) live in Mutants.Core / Mutants.Engine;
// this file is presentation/input only, per docs/AGENTS.md's Console/UI
// contract. Input is read via plain Console.ReadLine() (Spectre's
// interactive prompts hard-fail on redirected stdin); Spectre is still
// used for all output styling.
//
// Only the player's own character is saved (Persistence.CharacterSaveData,
// which also carries the world seed) - NPCs are re-simulated fresh each
// session, scattered across the whole timeline, and only contribute their
// personal bests to the leaderboard. The save/leaderboard DB lives at
// %APPDATA%\Chronomutants\mutants.db.
//
// KNOWN LIMITATION (parity with the pre-timeline build): player store
// ownership is session-only - it is not written to the save. Cleared
// Gatekeeper years and the world seed do persist.

AnsiConsole.Write(new FigletText("Chronomutants").Color(Color.Green));
AnsiConsole.MarkupLine("[grey](pre-release build — the continuous 2000–5000 A.D. timeline)[/]");
AnsiConsole.WriteLine();

var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var savesDirectory = string.IsNullOrEmpty(appDataFolder)
    ? "saves"
    : Path.Combine(appDataFolder, "Chronomutants");
Directory.CreateDirectory(savesDirectory);
var savePath = Path.Combine(savesDirectory, "mutants.db");
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

var (mutant, worldSeed) = start.Value;
var world = LoadTimeWorld(worldSeed);

// Place / re-place the character now that the world exists.
var startingRoom = world.GetYear(mutant.CurrentYear).Map;
if (startingRoom.TryGetRoom(mutant.Position) is null)
{
    mutant.PlaceAt(startingRoom.Start);
}

var npcs = SpawnNpcs(world, random);
var simulation = new WorldSimulation(world, npcs, random);
var shownBroadcastCount = 0;

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"Welcome, [bold]{Markup.Escape(mutant.Name)}[/] the [bold]{mutant.Class}[/]. Type [yellow]help[/] for commands.");
AnsiConsole.MarkupLine($"[grey]{npcs.Count} other Mutants are scattered across the centuries, fending for themselves.[/]");
AnsiConsole.WriteLine();

RenderRoom(mutant, world);

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

    switch (input.ToLowerInvariant())
    {
        case "quit" or "exit":
            running = false;
            break;

        case "help" or "?":
            RenderHelp();
            break;

        case "look" or "l":
            RenderRoom(mutant, world);
            break;

        case "status" or "stat":
            RenderStatusBar(mutant, world);
            break;

        case "heal":
            HandleHeal(mutant);
            break;

        case "fight" or "f":
            if (!HandleFight(mutant, world, random, simulation.Broadcast, abilities))
            {
                running = false;
            }

            break;

        case "abilities" or "spells":
            RenderAbilities(mutant, abilities);
            break;

        case "inventory" or "i":
            RenderInventory(mutant);
            break;

        case "npcs" or "who":
            RenderNpcs(npcs);
            break;

        case "news" or "broadcast":
            RenderBroadcast(simulation.Broadcast, count: 10);
            shownBroadcastCount = simulation.Broadcast.Events.Count;
            break;

        case "stores":
            RenderStores(world.GetYear(mutant.CurrentYear).StoreSlots);
            break;

        case "shop":
            HandleShop(mutant, world);
            break;

        case "buy-store":
            HandleBuyStore(mutant, world);
            break;

        case "collect":
            HandleCollect(mutant, world);
            break;

        case "save":
            HandleSave(mutant, repository, worldSeed);
            RecordNpcLeaderboardBests(npcs, repository);
            break;

        case "leaderboard" or "board":
            RenderLeaderboards(repository, mutant.Name);
            break;

        default:
            var (command, argument) = SplitCommand(input);

            if (command is "travel")
            {
                HandleTravel(mutant, world, random, simulation.Broadcast, argument);
                break;
            }

            if (command is "sell")
            {
                HandleSellToStore(mutant, world, argument);
                break;
            }

            if (command is "buy")
            {
                HandleBuyFromStore(mutant, world, argument);
                break;
            }

            if (command is "deposit" or "withdraw" or "reprice")
            {
                HandleStoreManagement(mutant, world, command, argument);
                break;
            }

            if (TryHandleItemCommand(mutant, command, argument))
            {
                break;
            }

            var direction = DirectionExtensions.Parse(input);
            if (direction is null)
            {
                AnsiConsole.MarkupLine($"[red]Unrecognized command:[/] '{input}'. Type [yellow]help[/] for a list.");
                break;
            }

            HandleMove(mutant, world, direction.Value);
            break;
    }

    if (running && !mutant.Health.IsDead)
    {
        simulation.Tick(mutant);
        shownBroadcastCount = RenderNewBroadcastEvents(simulation.Broadcast, shownBroadcastCount);
    }
}

if (!mutant.Health.IsDead)
{
    HandleSave(mutant, repository, worldSeed);
}

RecordNpcLeaderboardBests(npcs, repository);

AnsiConsole.MarkupLine(mutant.Health.IsDead
    ? "[grey]Game over.[/]"
    : "[grey]Farewell, Mutant. Progress saved.[/]");
return;

static void HandleSave(Mutant mutant, GameRepository repository, long worldSeed)
{
    repository.SaveCharacter(CharacterMapper.ToSaveData(mutant, worldSeed));
    repository.RecordPersonalBests(mutant.Name, isPlayer: true, mutant.FurthestYearReached, mutant.Level);
    AnsiConsole.MarkupLine("[green]Game saved.[/]");
}

/// <summary>NPCs aren't saved as full characters (see file header), but their personal bests still count toward the leaderboard - docs/GDD.md §8's "across player + NPCs."</summary>
static void RecordNpcLeaderboardBests(IReadOnlyList<Mutant> npcs, GameRepository repository)
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

    AnsiConsole.MarkupLine("Choose your [green]class[/]:");
    for (var i = 0; i < classes.Length; i++)
    {
        AnsiConsole.MarkupLine($"  [green]{i + 1}[/]. {classes[i]}");
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

        AnsiConsole.MarkupLine("[red]Please enter a number from the list, or a class name.[/]");
    }
}

static string ContentDirectory() => Path.Combine(AppContext.BaseDirectory, "Content");

/// <summary>
/// Loads the real, authored timeline from Mutants.Content. Falls back to
/// Mutants.Core.Time.TestTimeWorld's small sandbox if the content files
/// are missing or malformed, so a broken deployment degrades to something
/// playable instead of crashing. <paramref name="worldSeed"/> fixes the
/// Gatekeeper schedule and every year's map/store layout.
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
static List<Mutant> SpawnNpcs(TimeWorld world, IRandomSource random)
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

/// <summary>The title-screen "new game or load a save" flow. Returns null only on end-of-input (quit); otherwise the character plus the world seed to build the timeline from.</summary>
static (Mutant Mutant, long WorldSeed)? HandleStartScreen(GameRepository repository)
{
    var savedNames = repository.ListSavedCharacterNames();
    if (savedNames.Count > 0)
    {
        AnsiConsole.MarkupLine($"[yellow]Saved characters:[/] {string.Join(", ", savedNames.Select(Markup.Escape))}");
        AnsiConsole.MarkupLine("Type [green]new[/] to create a Mutant, or a saved name to continue them.");
    }
    else
    {
        AnsiConsole.MarkupLine("No saved characters yet. Type [green]new[/] to create a Mutant.");
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
        var name = ReadNonEmptyLine("What is your name, Mutant? ");
        if (name is null)
        {
            return null;
        }

        var characterClass = ReadClassChoice();
        if (characterClass is null)
        {
            return null;
        }

        return (new Mutant(name, characterClass.Value), System.Random.Shared.NextInt64());
    }

    var saveData = repository.LoadCharacter(choice)!;
    var loaded = CharacterMapper.FromSaveData(saveData);
    var seed = saveData is { SchemaVersion: >= 2, WorldSeed: not 0 }
        ? saveData.WorldSeed
        : System.Random.Shared.NextInt64();

    if (saveData.SchemaVersion < CharacterSaveData.CurrentSchemaVersion)
    {
        AnsiConsole.MarkupLine("[yellow]This save predates the timeline rework — your character carries over, dropped into the year that matches its old depth. The world is freshly generated.[/]");
    }

    AnsiConsole.MarkupLine($"[green]Welcome back, {Markup.Escape(loaded.Name)}![/]");
    return (loaded, seed);
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

/// <summary>Handles "convert/wield/use/eat/drink &lt;item&gt;" commands. Returns false if <paramref name="command"/> isn't one of those verbs.</summary>
static bool TryHandleItemCommand(Mutant mutant, string command, string argument)
{
    if (command is not ("convert" or "wield" or "use" or "eat" or "drink"))
    {
        return false;
    }

    var item = FindInventoryItem(mutant, argument);
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
            var ions = mutant.Convert(item);
            AnsiConsole.MarkupLine($"[blue]Converted {Markup.Escape(item.Name)} for {ions} Ions.[/]");
            break;

        case "wield":
            if (!item.IsWieldable)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(item.Name)} can't be wielded.[/]");
                break;
            }

            mutant.Wield(item);
            var penalty = item.IsClassCompatible(mutant.Class) ? "" : " [red](off-class - reduced effectiveness)[/]";
            AnsiConsole.MarkupLine($"[green]Wielded {Markup.Escape(item.Name)}.[/]{penalty}");
            break;

        case "use" or "eat" or "drink":
            if (!item.IsUsable)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(item.Name)} can't be used.[/]");
                break;
            }

            var effect = item.ConsumableEffect;
            var healed = mutant.Consume(item);
            AnsiConsole.MarkupLine(effect switch
            {
                ConsumableEffectType.Heal =>
                    $"[green]You use {Markup.Escape(item.Name)} and heal for {healed} HP.[/] ({mutant.Health.Current}/{mutant.Health.Max} HP)",
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

static Item? FindInventoryItem(Mutant mutant, string argument)
{
    if (argument.Length == 0)
    {
        return null;
    }

    if (int.TryParse(argument, out var index) && index >= 1 && index <= mutant.Inventory.Count)
    {
        return mutant.Inventory[index - 1];
    }

    return mutant.Inventory.FirstOrDefault(i => string.Equals(i.Name, argument, StringComparison.OrdinalIgnoreCase));
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

    return store.Listings.FirstOrDefault(l => string.Equals(l.Item.Name, argument, StringComparison.OrdinalIgnoreCase));
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

static void HandleShop(Mutant mutant, TimeWorld world)
{
    var storeSlots = world.GetYear(mutant.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]There's no store here.[/]");
        return;
    }

    RenderShop(store);
}

static void HandleBuyFromStore(Mutant mutant, TimeWorld world, string argument)
{
    var storeSlots = world.GetYear(mutant.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
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

    if (mutant.Riblets < listing.AskingPrice)
    {
        AnsiConsole.MarkupLine($"[red]You can't afford {Markup.Escape(listing.Item.Name)} ({listing.AskingPrice} Riblets; you have {mutant.Riblets}).[/]");
        return;
    }

    store.SellToMutant(mutant, listing);
    AnsiConsole.MarkupLine($"[green]Bought {Markup.Escape(listing.Item.Name)} for {listing.AskingPrice} Riblets.[/]");
}

static void HandleSellToStore(Mutant mutant, TimeWorld world, string argument)
{
    var storeSlots = world.GetYear(mutant.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]You need to be at a store to sell.[/] Try [yellow]convert[/] to destroy an item for Ions instead, or [yellow]stores[/] to find one.");
        return;
    }

    var item = FindInventoryItem(mutant, argument);
    if (item is null)
    {
        AnsiConsole.MarkupLine(argument.Length == 0
            ? "[red]Sell what?[/] Type [yellow]inventory[/] to see what you're carrying."
            : $"[red]No item matching '{Markup.Escape(argument)}' in your inventory.[/]");
        return;
    }

    var price = store.BuyFromMutant(mutant, item);
    if (price is null)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(store.Name)} can't afford to buy that right now.[/]");
        return;
    }

    AnsiConsole.MarkupLine($"[yellow]Sold {Markup.Escape(item.Name)} to {Markup.Escape(store.Name)} for {price} Riblets.[/]");
}

static void HandleBuyStore(Mutant mutant, TimeWorld world)
{
    var storeSlots = world.GetYear(mutant.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
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

    if (mutant.Riblets < slot.PurchaseCost)
    {
        AnsiConsole.MarkupLine($"[red]You need {slot.PurchaseCost} Riblets to buy this slot; you have {mutant.Riblets}.[/]");
        return;
    }

    slot.Purchase(mutant);
    AnsiConsole.MarkupLine($"[green]You now own a store here: {Markup.Escape(slot.Store!.Name)}![/] Use [yellow]deposit[/]/[yellow]withdraw[/]/[yellow]reprice[/]/[yellow]collect[/] to run it. [grey](Store ownership is session-only for now.)[/]");
}

/// <summary>Collects from every store the Mutant owns across every year visited this session — an owner needn't be standing there (docs/GDD.md §6.2's "idle-income loop").</summary>
static void HandleCollect(Mutant mutant, TimeWorld world)
{
    var owned = world.VisitedYears
        .SelectMany(y => world.GetYear(y).StoreSlots)
        .Where(s => s.Store?.Owner == mutant)
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
            totalCollected += slot.Store.CollectCapital(mutant, capital);
        }
    }

    AnsiConsole.MarkupLine(totalCollected > 0
        ? $"[yellow]Collected {totalCollected} Riblets from your store(s).[/]"
        : "[grey]Nothing to collect yet.[/]");
}

/// <summary>docs/GDD.md §2 [SOURCE]: "spend Ions to heal wounds directly," usable at any time.</summary>
static void HandleHeal(Mutant mutant)
{
    if (mutant.Health.Current >= mutant.Health.Max)
    {
        AnsiConsole.MarkupLine("[grey]You're already at full health.[/]");
        return;
    }

    if (mutant.Ions.Current <= 0)
    {
        AnsiConsole.MarkupLine("[red]Not enough Ions to heal.[/]");
        return;
    }

    var healed = mutant.Heal();
    AnsiConsole.MarkupLine($"[green]You heal for {healed} HP.[/] ({mutant.Health.Current}/{mutant.Health.Max} HP, {mutant.Ions.Current}/{mutant.Ions.Max} Ions left)");
}

static void HandleStoreManagement(Mutant mutant, TimeWorld world, string command, string argument)
{
    var storeSlots = world.GetYear(mutant.CurrentYear).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
    if (slot?.Store is not { } store || store.Owner != mutant)
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

            store.Withdraw(mutant, listing);
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
            var item = FindInventoryItem(mutant, itemArg);
            if (item is null)
            {
                AnsiConsole.MarkupLine($"[red]No item matching '{Markup.Escape(itemArg)}' in your inventory.[/]");
                return;
            }

            store.Deposit(mutant, item, price);
            AnsiConsole.MarkupLine($"[green]Listed {Markup.Escape(item.Name)} at {Markup.Escape(store.Name)} for {price} Riblets.[/]");
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

            store.AdjustPrice(mutant, listingToReprice, price);
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(listingToReprice.Item.Name)} is now {price} Riblets.[/]");
            break;
        }
    }
}

/// <summary>
/// Resolves one fight. In a Gatekeeper year the player hasn't cleared, the
/// opponent is that year's Gatekeeper (a bullet sponge guarding a
/// Legendary trophy, which drops through the normal loot path on a win);
/// otherwise it's a random monster from the year's roster. The fight
/// itself is interactive and round-by-round via CombatSession - "attack"
/// or "cast <ability>" each round. Returns false if the Mutant was
/// defeated (caller ends the session). End-of-input mid-fight auto-attacks
/// each remaining round.
/// </summary>
static bool HandleFight(Mutant mutant, TimeWorld world, IRandomSource random, BroadcastChannel broadcast, IReadOnlyList<AbilityData> abilities)
{
    var year = mutant.CurrentYear;
    var yearContent = world.GetYear(year);

    var isGatekeeperFight = yearContent.Gatekeeper is not null && !mutant.HasDefeatedGatekeeper(year);
    var monster = isGatekeeperFight
        ? yearContent.Gatekeeper!()
        : yearContent.MonsterRoster[System.Random.Shared.Next(yearContent.MonsterRoster.Count)]();
    var levelBefore = mutant.Level;

    AnsiConsole.MarkupLine(isGatekeeperFight
        ? $"[bold]{Markup.Escape(monster.Name)} rises to meet you![/] (tier {monster.Tier})"
        : $"A [bold]{Markup.Escape(monster.Name)}[/] (tier {monster.Tier}) attacks!");

    var usableAbilities = abilities
        .Where(a => string.Equals(a.Class, mutant.Class.ToString(), StringComparison.OrdinalIgnoreCase) && a.Level <= mutant.Level)
        .ToList();

    var session = new CombatSession(mutant, monster, random);
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

    if (session.MutantWon)
    {
        AnsiConsole.MarkupLine($"[green]You defeated {Markup.Escape(foe)}! +{session.XpAwarded} XP.[/]");
        broadcast.Publish(GameEvent.Slain(monster.Name, mutant.Name));

        if (isGatekeeperFight)
        {
            mutant.RecordGatekeeperDefeat(year);
            var trophy = session.ItemsDropped.FirstOrDefault();
            AnsiConsole.MarkupLine(trophy is not null
                ? $"[bold yellow]The Gatekeeper of {year} yields its {Markup.Escape(trophy.Name)}![/]"
                : $"[bold]The Gatekeeper of {year} is broken. This year is yours.[/]");
        }

        if (mutant.Level > levelBefore)
        {
            broadcast.Publish(GameEvent.LevelReached(mutant.Name, mutant.Level));
        }

        RenderStatusBar(mutant, world);
        return true;
    }

    // docs/GDD.md §3.3 (death & recall) is not implemented yet; a defeat here just ends the session.
    AnsiConsole.MarkupLine($"[red]You were defeated by {Markup.Escape(foe)}...[/]");
    broadcast.Publish(GameEvent.Slain(mutant.Name, monster.Name));
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

/// <summary>Lists the player's class's abilities - locked ones greyed, the handful with no combat effect flagged.</summary>
static void RenderAbilities(Mutant mutant, IReadOnlyList<AbilityData> abilities)
{
    var classAbilities = abilities
        .Where(a => string.Equals(a.Class, mutant.Class.ToString(), StringComparison.OrdinalIgnoreCase))
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
        var unlocked = mutant.Level >= ability.Level;
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

/// <summary>Handles "travel &lt;year&gt;", "travel +N"/"-N" (relative years), and "travel next"/"prev" (the next/previous Gatekeeper year) — docs/GDD.md §3.2.</summary>
static void HandleTravel(Mutant mutant, TimeWorld world, IRandomSource random, BroadcastChannel broadcast, string argument)
{
    var arg = argument.Trim();
    int targetYear;

    switch (arg.ToLowerInvariant())
    {
        case "":
            AnsiConsole.MarkupLine("[red]Travel when?[/] Try 'travel 3200', 'travel +150', 'travel -100', or 'travel next'/'prev' (Gatekeeper years).");
            return;

        case "next":
        {
            var next = world.Gatekeepers.NextAfter(mutant.CurrentYear);
            if (next is null)
            {
                AnsiConsole.MarkupLine("[grey]No Gatekeeper years remain ahead of you.[/]");
                return;
            }

            targetYear = next.Value;
            break;
        }

        case "prev" or "previous":
        {
            var prev = world.Gatekeepers.PreviousBefore(mutant.CurrentYear);
            if (prev is null)
            {
                AnsiConsole.MarkupLine("[grey]No Gatekeeper years behind you.[/]");
                return;
            }

            targetYear = prev.Value;
            break;
        }

        default:
            if ((arg.StartsWith('+') || arg.StartsWith('-')) && int.TryParse(arg, out var delta))
            {
                targetYear = mutant.CurrentYear + delta;
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

    if (targetYear == mutant.CurrentYear)
    {
        AnsiConsole.MarkupLine("[grey]You're already there.[/]");
        return;
    }

    if (!TimeScale.IsValidYear(targetYear))
    {
        AnsiConsole.MarkupLine($"[red]The timeline only runs from {TimeScale.MinYear} to {TimeScale.MaxYear} A.D.[/]");
        return;
    }

    var cost = IonEconomy.TimeTravelCost(mutant.CurrentYear, targetYear);
    if (Math.Abs(targetYear - mutant.CurrentYear) > 500)
    {
        AnsiConsole.Markup($"[yellow]That's a {Math.Abs(targetYear - mutant.CurrentYear)}-year jump — {cost} Ions (you have {mutant.Ions.Current}). Proceed? (y/n)[/] ");
        var confirm = Console.ReadLine();
        if (confirm is null || !confirm.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[grey]Stayed put.[/]");
            return;
        }
    }

    var levelBefore = mutant.Level;
    var result = TimeTravelResolver.Travel(mutant, world, targetYear, random);

    if (!result.Success)
    {
        AnsiConsole.MarkupLine(result.FailureReason switch
        {
            TimeTravelFailureReason.YearOutOfRange => $"[red]{targetYear} is off the timeline (2000–5000).[/]",
            TimeTravelFailureReason.InsufficientIons =>
                $"[red]Not enough Ions ({cost} needed; you have {mutant.Ions.Current}).[/]",
            _ => "[red]Travel failed.[/]",
        });
        return;
    }

    if (mutant.Level > levelBefore)
    {
        broadcast.Publish(GameEvent.LevelReached(mutant.Name, mutant.Level));
    }

    var arrival = world.GetYear(targetYear);
    AnsiConsole.MarkupLine($"[bold]You travel to {targetYear} A.D. — {Markup.Escape(arrival.Era.Name)}.[/] [grey]({result.IonsSpent} Ions)[/]");
    broadcast.Publish(GameEvent.TimeTraveled(mutant.Name, targetYear));
    RenderRoom(mutant, world);
}

static void RenderNpcs(IReadOnlyList<Mutant> npcs)
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

/// <summary>Prints any broadcast events published since <paramref name="alreadyShownCount"/>. Returns the new total shown.</summary>
static int RenderNewBroadcastEvents(BroadcastChannel broadcast, int alreadyShownCount)
{
    var events = broadcast.Events;
    for (var i = alreadyShownCount; i < events.Count; i++)
    {
        AnsiConsole.MarkupLine($"[cyan]* {Markup.Escape(events[i].Message)}[/]");
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
        var status = slot.IsAvailableForPurchase ? $"[yellow]for sale ({slot.PurchaseCost} Riblets)[/]" : "occupied";

        table.AddRow(Markup.Escape(slot.Name), Markup.Escape(slot.Location.ToString()), owner, items, status);
    }

    AnsiConsole.Write(table);
}

static void RenderShop(Store store)
{
    AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(store.Name)}[/] — Capital: {store.Capital} Riblets");

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

static void RenderInventory(Mutant mutant)
{
    if (mutant.Inventory.Count == 0)
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

    for (var i = 0; i < mutant.Inventory.Count; i++)
    {
        var item = mutant.Inventory[i];
        var equipped = item == mutant.EquippedWeapon || item == mutant.EquippedArmor ? "yes" : "";
        var effect = item.ConsumableEffect switch
        {
            ConsumableEffectType.Heal => $"heals {item.EffectMagnitude:0} HP",
            ConsumableEffectType.BuffAttack => $"+{item.EffectMagnitude:0} attack ({item.EffectDurationTicks} ticks)",
            ConsumableEffectType.BuffDefense => $"+{item.EffectMagnitude:0} defense ({item.EffectDurationTicks} ticks)",
            _ => "",
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

static void HandleMove(Mutant mutant, TimeWorld world, Direction direction)
{
    var map = world.GetYear(mutant.CurrentYear).Map;
    var result = map.TryMove(mutant.Position, direction);
    if (!result.Success)
    {
        AnsiConsole.MarkupLine("[red]You can't go that way.[/]");
        return;
    }

    mutant.MoveTo(result.Destination!.Value);
    RenderRoom(mutant, world);
}

static void RenderRoom(Mutant mutant, TimeWorld world)
{
    var yearContent = world.GetYear(mutant.CurrentYear);
    var room = yearContent.Map.GetRoom(mutant.Position);

    RenderStatusBar(mutant, world);
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

    if (yearContent.Gatekeeper is not null && !mutant.HasDefeatedGatekeeper(mutant.CurrentYear))
    {
        AnsiConsole.MarkupLine("[bold red]A Gatekeeper holds this year. It will not let you leave unnoticed — [yellow]fight[/] when you're ready.[/]");
    }

    var slot = FindStoreSlotAt(yearContent.StoreSlots, mutant.Position);
    if (slot is not null)
    {
        AnsiConsole.MarkupLine(slot.Store switch
        {
            null => $"[cyan]There's an empty storefront here — '{Markup.Escape("buy-store")}' to claim it for {slot.PurchaseCost} Riblets.[/]",
            { IsGovernmentRun: true } => $"[cyan]There's a store here: {Markup.Escape(slot.Store.Name)}. Type 'shop' to browse.[/]",
            _ => $"[cyan]There's a player-owned store here: {Markup.Escape(slot.Store.Name)} (owned by {Markup.Escape(slot.Store.Owner!.Name)}). Type 'shop' to browse.[/]",
        });
    }

    AnsiConsole.WriteLine();
}

static void RenderStatusBar(Mutant mutant, TimeWorld world)
{
    var yearContent = world.GetYear(mutant.CurrentYear);
    var status = $"[red]HP {mutant.Health.Current}/{mutant.Health.Max}[/]  " +
                 $"[blue]Ions {mutant.Ions.Current}/{mutant.Ions.Max}[/]  " +
                 $"[yellow]Riblets {mutant.Riblets}[/]  " +
                 $"Char Level {mutant.Level}  " +
                 $"Year {mutant.CurrentYear} A.D.  " +
                 $"Furthest {mutant.FurthestYearReached}  " +
                 $"Location {Markup.Escape(mutant.Position.ToString())}";

    if (mutant.ActiveEffects.Count > 0)
    {
        var effects = mutant.ActiveEffects.Select(e => e.Type switch
        {
            ConsumableEffectType.BuffAttack => $"+{e.Magnitude:0} attack ({e.TicksRemaining} ticks left)",
            ConsumableEffectType.BuffDefense => $"+{e.Magnitude:0} defense ({e.TicksRemaining} ticks left)",
            _ => e.Type.ToString(),
        });
        status += $"\n[green]Active: {string.Join(", ", effects)}[/]";
    }

    AnsiConsole.Write(new Panel(status)
        .Header($"[bold]{Markup.Escape(mutant.Name)}[/] — {Markup.Escape(yearContent.Era.Name)}, {mutant.CurrentYear} A.D.")
        .Expand());
}

static void RenderHelp()
{
    AnsiConsole.MarkupLine("[yellow]Commands:[/]");
    AnsiConsole.MarkupLine("  [green]n[/]/[green]s[/]/[green]e[/]/[green]w[/] (or north/south/east/west) - move");
    AnsiConsole.MarkupLine("  [green]look[/] (or l)         - redescribe the current room");
    AnsiConsole.MarkupLine("  [green]fight[/] (or f)        - fight a monster from this year's roster (or its Gatekeeper)");
    AnsiConsole.MarkupLine("    (each round, type [green]attack[/] or [green]cast <ability>[/])");
    AnsiConsole.MarkupLine("  [green]heal[/]                - spend Ions to recover HP (usable any time)");
    AnsiConsole.MarkupLine("  [green]abilities[/] (or spells) - list your class's abilities unlocked so far");
    AnsiConsole.MarkupLine("  [green]travel <year>[/]      - jump to a year (2000–5000); costs ceil(0.2·|Δyear|) Ions");
    AnsiConsole.MarkupLine("  [green]travel +N[/]/[green]-N[/]      - jump N years forward/back");
    AnsiConsole.MarkupLine("  [green]travel next[/]/[green]prev[/]   - jump to the next/previous Gatekeeper year");
    AnsiConsole.MarkupLine("  [green]inventory[/] (or i)    - list what you're carrying");
    AnsiConsole.MarkupLine("  [green]npcs[/] (or who)       - list the other Mutants out in the timeline");
    AnsiConsole.MarkupLine("  [green]news[/] (or broadcast) - show recent kill-feed events");
    AnsiConsole.MarkupLine("  [green]convert <item>[/]     - destroy an item for Ions");
    AnsiConsole.MarkupLine("  [green]wield <item>[/]       - equip a weapon or armor item");
    AnsiConsole.MarkupLine("  [green]use[/]/[green]eat[/]/[green]drink <item>[/] - consume a potion or food item");
    AnsiConsole.MarkupLine("    ('<item>' is either its inventory number or its name)");
    AnsiConsole.MarkupLine("  [green]stores[/]              - list every store this year");
    AnsiConsole.MarkupLine("  [green]shop[/]                - browse the store in your current room");
    AnsiConsole.MarkupLine("  [green]buy <item>[/]         - buy a listed item (must be at a store)");
    AnsiConsole.MarkupLine("  [green]sell <item>[/]        - sell an item to the store here (must be at a store)");
    AnsiConsole.MarkupLine("  [green]buy-store[/]           - purchase an empty store slot you're standing in");
    AnsiConsole.MarkupLine("  [green]deposit <item> <price>[/] - list your own item for sale at your store");
    AnsiConsole.MarkupLine("  [green]withdraw <item>[/]    - pull a listing back into your inventory");
    AnsiConsole.MarkupLine("  [green]reprice <item> <price>[/] - change a listing's asking price");
    AnsiConsole.MarkupLine("  [green]collect[/]             - withdraw your store's earnings into your Riblets");
    AnsiConsole.MarkupLine("  [green]save[/]                - save your character now");
    AnsiConsole.MarkupLine("  [green]leaderboard[/] (or board) - show the leaderboards, your best highlighted");
    AnsiConsole.MarkupLine("  [green]status[/]              - show the status bar");
    AnsiConsole.MarkupLine("  [green]help[/] (or ?)         - show this list");
    AnsiConsole.MarkupLine("  [green]quit[/] (or exit)      - leave the game (auto-saves unless you died)");
}
