using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Events;
using Mutants.Core.Ions;
using Mutants.Core.Items;
using Mutants.Core.Levels;
using Mutants.Core.World;
using Mutants.Engine;
using Mutants.Engine.Combat;
using Mutants.Engine.Npc;
using Mutants.Engine.Persistence;
using Mutants.Engine.Simulation;
using Spectre.Console;

// Sandbox build covering milestones 2 (grid movement, single hardcoded
// level), 3 (combat, loot drops, convert/sell/wield), 4 (NPC simulation
// loop), 5 (stores and the Riblet economy), 6 (multi-level time travel
// with scaling), and 7 (leaderboards + start screen + save/load) per
// docs/TECH_STACK.md's milestone sequencing. Milestone 8 (Windows
// installer packaging) wraps this project up — see installer/*.iss and
// .github/workflows/. All game rules here (movement legality, combat
// resolution, NPC AI, store transactions, time travel, persistence,
// character state) live in Mutants.Core / Mutants.Engine; this file is
// presentation/input only, per docs/AGENTS.md's Console/UI Agent
// contract.
//
// Only the player's own character is saved/loaded as a full character
// (see Persistence.CharacterSaveData) - NPCs are re-simulated fresh each
// session (docs/GDD.md doesn't ask for NPC persistence, only for the
// leaderboard to have "meaning across NPC-simulated seasons," which is
// satisfied by recording their personal bests, not their full state).
// The save/leaderboard file lives at %APPDATA%\Chronomutants\mutants.db —
// not a folder relative to the exe, since an installed copy typically
// lives under Program Files, unwritable without elevation.
//
// The player can now freely time-travel across Levels.TestWorld's 3
// sandbox levels. NPCs deliberately stay on level 1 this milestone —
// giving WorldSimulation full multi-level NPC awareness (each NPC on its
// own level, wandering/fighting/trading against ITS level's content) is a
// bigger architectural change than fits alongside building time travel
// itself; flagged here as follow-up work rather than rushed in.
//
// There's still no spatial monster placement (rooms don't carry
// monsters) — "fight" spawns a random monster from the current level's
// tier-scaled roster on demand, same as NpcController does for NPCs.
// Stores, unlike monsters, ARE placed spatially per level (see
// Economy.TestStores / Levels.TestWorld) — buying/selling requires
// standing in the right room, same as movement already works.
// docs/GDD.md §9's real background tick (every ~2 seconds, independent of
// player input) isn't implemented — this console instead advances the
// world by one tick per player command, a synchronous stand-in that
// keeps the sandbox simple and scriptable.

// Input is read via plain Console.ReadLine() rather than Spectre's
// interactive prompts (TextPrompt/SelectionPrompt): those require a real
// interactive terminal and hard-fail when stdin is redirected (e.g. piped
// input, some CI/test harnesses). Spectre is still used for all output
// styling. Console.ReadLine() also degrades cleanly to null at end-of-input
// instead of throwing, which we treat as "quit."

AnsiConsole.Write(new FigletText("Chronomutants").Color(Color.Green));
AnsiConsole.MarkupLine("[grey](engine sandbox build — time travel across a 3-level test world)[/]");
AnsiConsole.WriteLine();

// %APPDATA%\Chronomutants — not a folder relative to the exe: an
// installed copy typically lives under Program Files, which a
// non-elevated player can't write to, so the save file needs a real
// per-user, always-writable location (docs/TECH_STACK.md's installer
// milestone). Falls back to a local "saves" folder if ApplicationData
// somehow isn't available (e.g. some minimal/CI environments).
var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var savesDirectory = string.IsNullOrEmpty(appDataFolder)
    ? "saves"
    : Path.Combine(appDataFolder, "Chronomutants");
Directory.CreateDirectory(savesDirectory);
var savePath = Path.Combine(savesDirectory, "mutants.db");
using var repository = new GameRepository(savePath);

RenderLeaderboards(repository);
AnsiConsole.WriteLine();

var world = TestWorld.Build();
var random = new SystemRandomSource();

var mutant = HandleStartScreen(repository, world);
if (mutant is null)
{
    return;
}

const int NpcPopulationSize = 5; // arbitrary sandbox default - GDD §7 calls this "a configurable population"
var npcLevel = world.GetLevel(1); // NPCs stay on level 1 for now - see file header note
var npcs = NpcPopulation.Spawn(NpcPopulationSize, npcLevel.Map, random);
var simulation = new WorldSimulation(npcLevel.Map, npcs, random, npcLevel.StoreSlots);
var shownBroadcastCount = 0;

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"Welcome, [bold]{Markup.Escape(mutant.Name)}[/] the [bold]{mutant.Class}[/]. Type [yellow]help[/] for commands.");
AnsiConsole.MarkupLine($"[grey]{NpcPopulationSize} other Mutants are already out there, fending for themselves.[/]");
AnsiConsole.WriteLine();

RenderRoom(mutant, world);

var running = true;
while (running)
{
    AnsiConsole.Markup("[green]>[/] ");
    var rawInput = Console.ReadLine();
    if (rawInput is null)
    {
        break; // end of input (e.g. piped stdin exhausted) - quit gracefully
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

        case "fight" or "f":
            if (!HandleFight(mutant, world, random, simulation.Broadcast))
            {
                running = false; // defeated - see HandleFight
            }

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
            RenderStores(world.GetLevel(mutant.CurrentTimeLevel).StoreSlots);
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
            HandleSave(mutant, repository);
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
    HandleSave(mutant, repository);
}

RecordNpcLeaderboardBests(npcs, repository);

AnsiConsole.MarkupLine(mutant.Health.IsDead
    ? "[grey]Game over.[/]"
    : "[grey]Farewell, Mutant. Progress saved.[/]");
return;

static void HandleSave(Mutant mutant, GameRepository repository)
{
    repository.SaveCharacter(CharacterMapper.ToSaveData(mutant));
    repository.RecordPersonalBests(mutant.Name, isPlayer: true, mutant.UnlockedTimeLevel, mutant.Level);
    AnsiConsole.MarkupLine("[green]Game saved.[/]");
}

/// <summary>NPCs aren't saved as full characters (see file header), but their personal bests still count toward the leaderboard - docs/GDD.md §8's "across player + NPCs."</summary>
static void RecordNpcLeaderboardBests(IReadOnlyList<Mutant> npcs, GameRepository repository)
{
    foreach (var npc in npcs)
    {
        repository.RecordPersonalBests(npc.Name, isPlayer: false, npc.UnlockedTimeLevel, npc.Level);
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
            return null; // end of input
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
            return null; // end of input
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

/// <summary>The title-screen "new game or load a save" flow. Returns null only on end-of-input (quit).</summary>
static Mutant? HandleStartScreen(GameRepository repository, GameWorld world)
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
            return null; // end of input
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

        var mutant = new Mutant(name, characterClass.Value);
        mutant.PlaceAt(world.GetLevel(mutant.CurrentTimeLevel).Map.Start);
        return mutant;
    }

    var saveData = repository.LoadCharacter(choice)!;
    var loaded = CharacterMapper.FromSaveData(saveData);

    // Defensive: if the world's content ever changes, a saved position
    // might no longer exist - fall back to that level's start room rather
    // than crash on the next GetRoom() call.
    var levelDefinition = world.TryGetLevel(loaded.CurrentTimeLevel);
    if (levelDefinition is null || levelDefinition.Map.TryGetRoom(loaded.Position) is null)
    {
        levelDefinition ??= world.GetLevel(1);
        loaded.SetCurrentTimeLevel(levelDefinition.LevelNumber);
        loaded.PlaceAt(levelDefinition.Map.Start);
    }

    AnsiConsole.MarkupLine($"[green]Welcome back, {Markup.Escape(loaded.Name)}![/]");
    return loaded;
}

static void RenderLeaderboards(GameRepository repository, string? highlightName = null)
{
    AnsiConsole.MarkupLine("[yellow]═══ Leaderboards ═══[/]");
    RenderLeaderboardBoard(repository, "Deepest Time Level Reached", repository.TopByTimeLevel(10), e => e.DeepestTimeLevelReached, highlightName);
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

    // docs/GDD.md §8: the player's own best is shown even if outside the top 10.
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

/// <summary>Handles "convert/wield &lt;item&gt;" commands. Returns false if <paramref name="command"/> isn't one of those verbs. ("sell" now requires a store — see HandleSellToStore.)</summary>
static bool TryHandleItemCommand(Mutant mutant, string command, string argument)
{
    if (command is not ("convert" or "wield"))
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

/// <summary>Splits "&lt;item&gt; &lt;price&gt;" - the last whitespace token is the price, everything before it is the item name/index (so multi-word item names still work).</summary>
static (string ItemArg, int Price)? SplitItemAndPrice(string argument)
{
    var tokens = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length < 2 || !int.TryParse(tokens[^1], out var price) || price < 1)
    {
        return null;
    }

    return (string.Join(' ', tokens[..^1]), price);
}

static void HandleShop(Mutant mutant, GameWorld world)
{
    var storeSlots = world.GetLevel(mutant.CurrentTimeLevel).StoreSlots;
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]There's no store here.[/]");
        return;
    }

    RenderShop(store);
}

static void HandleBuyFromStore(Mutant mutant, GameWorld world, string argument)
{
    var storeSlots = world.GetLevel(mutant.CurrentTimeLevel).StoreSlots;
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

static void HandleSellToStore(Mutant mutant, GameWorld world, string argument)
{
    var storeSlots = world.GetLevel(mutant.CurrentTimeLevel).StoreSlots;
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

static void HandleBuyStore(Mutant mutant, GameWorld world)
{
    var storeSlots = world.GetLevel(mutant.CurrentTimeLevel).StoreSlots;
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
    AnsiConsole.MarkupLine($"[green]You now own a store here: {Markup.Escape(slot.Store!.Name)}![/] Use [yellow]deposit[/]/[yellow]withdraw[/]/[yellow]reprice[/]/[yellow]collect[/] to run it.");
}

/// <summary>Collects from every store the Mutant owns, across every level — an owner needn't be standing there to collect (docs/GDD.md §6.2's "idle-income loop").</summary>
static void HandleCollect(Mutant mutant, GameWorld world)
{
    var allSlots = Enumerable.Range(1, world.MaxLevel).SelectMany(n => world.GetLevel(n).StoreSlots);
    var owned = allSlots.Where(s => s.Store?.Owner == mutant).ToList();
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

static void HandleStoreManagement(Mutant mutant, GameWorld world, string command, string argument)
{
    var storeSlots = world.GetLevel(mutant.CurrentTimeLevel).StoreSlots;
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

/// <summary>Spawns a random monster from the current level's tier-scaled roster and resolves combat. Returns false if the Mutant was defeated (caller should end the session).</summary>
static bool HandleFight(Mutant mutant, GameWorld world, IRandomSource random, BroadcastChannel broadcast)
{
    var levelDefinition = world.GetLevel(mutant.CurrentTimeLevel);
    var roster = levelDefinition.MonsterRoster;
    var monster = roster[System.Random.Shared.Next(roster.Count)]();
    var levelBefore = mutant.Level;

    AnsiConsole.MarkupLine($"A [bold]{Markup.Escape(monster.Name)}[/] (tier {monster.Tier}) attacks!");

    var result = CombatResolver.Fight(mutant, monster, random);
    foreach (var line in result.Log)
    {
        AnsiConsole.MarkupLine(Markup.Escape(line));
    }

    if (result.MutantWon)
    {
        AnsiConsole.MarkupLine($"[green]You defeated the {Markup.Escape(monster.Name)}! +{result.XpAwarded} XP.[/]");
        broadcast.Publish(GameEvent.Slain(monster.Name, mutant.Name));
        if (mutant.Level > levelBefore)
        {
            broadcast.Publish(GameEvent.LevelReached(mutant.Name, mutant.Level));
        }

        RenderStatusBar(mutant, world);
        return true;
    }

    // docs/GDD.md §3.3 (death & recall - dropping inventory, returning to a
    // home base with an Ion penalty) is not implemented yet; a defeat here
    // just ends the session.
    AnsiConsole.MarkupLine($"[red]You were defeated by the {Markup.Escape(monster.Name)}...[/]");
    broadcast.Publish(GameEvent.Slain(mutant.Name, monster.Name));
    return false;
}

/// <summary>Handles "travel next/prev/&lt;level&gt;" — docs/GDD.md §3.2.</summary>
static void HandleTravel(Mutant mutant, GameWorld world, IRandomSource random, BroadcastChannel broadcast, string argument)
{
    int targetLevel;
    switch (argument.Trim().ToLowerInvariant())
    {
        case "":
            AnsiConsole.MarkupLine("[red]Travel where?[/] Try 'travel next', 'travel prev', or 'travel <level number>'.");
            return;

        case "next":
            targetLevel = mutant.CurrentTimeLevel + 1;
            break;

        case "prev" or "previous":
            targetLevel = mutant.CurrentTimeLevel - 1;
            if (targetLevel < 1)
            {
                AnsiConsole.MarkupLine("[red]You're already at the shallowest level.[/]");
                return;
            }

            break;

        default:
            if (!int.TryParse(argument.Trim(), out targetLevel))
            {
                AnsiConsole.MarkupLine("[red]'travel' needs 'next', 'prev', or a level number.[/]");
                return;
            }

            break;
    }

    if (targetLevel == mutant.CurrentTimeLevel)
    {
        AnsiConsole.MarkupLine("[grey]You're already there.[/]");
        return;
    }

    var levelBefore = mutant.Level;
    var result = TimeTravelResolver.Travel(mutant, world, targetLevel, random);

    var gatekeeperFight = result.GatekeeperFight;
    if (gatekeeperFight is not null)
    {
        AnsiConsole.MarkupLine($"[bold]The Gatekeeper of Level {targetLevel} blocks your way![/]");
        foreach (var line in gatekeeperFight.Log)
        {
            AnsiConsole.MarkupLine(Markup.Escape(line));
        }
    }

    if (!result.Success)
    {
        AnsiConsole.MarkupLine(result.FailureReason switch
        {
            TimeTravelFailureReason.UnknownLevel => $"[red]There is no level {targetLevel}.[/]",
            TimeTravelFailureReason.BelowMinimumCharacterLevel =>
                $"[red]You need to be at least character level {world.GetLevel(targetLevel).MinCharacterLevelToUnlock} to attempt level {targetLevel}.[/]",
            TimeTravelFailureReason.LostToGatekeeper => $"[red]The gatekeeper defeated you. Level {targetLevel} remains locked.[/]",
            TimeTravelFailureReason.InsufficientIons =>
                $"[red]Not enough Ions ({IonEconomy.TimeTravelCost(targetLevel)} needed; you have {mutant.Ions.Current}).[/]",
            _ => "[red]Travel failed.[/]",
        });
        return;
    }

    if (gatekeeperFight is { MutantWon: true })
    {
        AnsiConsole.MarkupLine($"[green]You defeated the Gatekeeper of Level {targetLevel}! Level {targetLevel} is now unlocked.[/]");
        broadcast.Publish(GameEvent.Slain($"The Gatekeeper of Level {targetLevel}", mutant.Name));
    }

    if (mutant.Level > levelBefore)
    {
        broadcast.Publish(GameEvent.LevelReached(mutant.Name, mutant.Level));
    }

    AnsiConsole.MarkupLine($"[bold]You travel to level {targetLevel}: {Markup.Escape(world.GetLevel(targetLevel).Map.Name)}.[/]");
    broadcast.Publish(new GameEvent($"{mutant.Name} time traveled to level {targetLevel}."));
    RenderRoom(mutant, world);
}

static void RenderNpcs(IReadOnlyList<Mutant> npcs)
{
    var table = new Table().Expand();
    table.AddColumn("Name");
    table.AddColumn("Class");
    table.AddColumn("Level");
    table.AddColumn("HP");
    table.AddColumn("Ions");
    table.AddColumn("Location");
    table.AddColumn("Status");

    foreach (var npc in npcs)
    {
        table.AddRow(
            Markup.Escape(npc.Name),
            npc.Class.ToString(),
            npc.Level.ToString(),
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
        AnsiConsole.MarkupLine("[grey]No stores on this level yet.[/]");
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
    table.AddColumn("Equipped");

    for (var i = 0; i < mutant.Inventory.Count; i++)
    {
        var item = mutant.Inventory[i];
        var equipped = item == mutant.EquippedWeapon || item == mutant.EquippedArmor ? "yes" : "";
        table.AddRow(
            (i + 1).ToString(),
            Markup.Escape(item.Name),
            item.Type.ToString(),
            item.Tier.ToString(),
            item.Rarity.ToString(),
            item.Value.ToString(),
            equipped);
    }

    AnsiConsole.Write(table);
}

static void HandleMove(Mutant mutant, GameWorld world, Direction direction)
{
    var map = world.GetLevel(mutant.CurrentTimeLevel).Map;
    var result = map.TryMove(mutant.Position, direction);
    if (!result.Success)
    {
        AnsiConsole.MarkupLine("[red]You can't go that way.[/]");
        return;
    }

    mutant.MoveTo(result.Destination!.Value);
    RenderRoom(mutant, world);
}

static void RenderRoom(Mutant mutant, GameWorld world)
{
    var levelDefinition = world.GetLevel(mutant.CurrentTimeLevel);
    var room = levelDefinition.Map.GetRoom(mutant.Position);

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

    var slot = FindStoreSlotAt(levelDefinition.StoreSlots, mutant.Position);
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

static void RenderStatusBar(Mutant mutant, GameWorld world)
{
    var levelDefinition = world.GetLevel(mutant.CurrentTimeLevel);
    var status = $"[red]HP {mutant.Health.Current}/{mutant.Health.Max}[/]  " +
                 $"[blue]Ions {mutant.Ions.Current}/{mutant.Ions.Max}[/]  " +
                 $"[yellow]Riblets {mutant.Riblets}[/]  " +
                 $"Char Level {mutant.Level}  " +
                 $"Time Level {mutant.CurrentTimeLevel}/{mutant.UnlockedTimeLevel} unlocked  " +
                 $"Location {Markup.Escape(mutant.Position.ToString())}";

    AnsiConsole.Write(new Panel(status)
        .Header($"[bold]{Markup.Escape(mutant.Name)}[/] — {Markup.Escape(levelDefinition.Map.Name)}")
        .Expand());
}

static void RenderHelp()
{
    AnsiConsole.MarkupLine("[yellow]Commands:[/]");
    AnsiConsole.MarkupLine("  [green]n[/]/[green]s[/]/[green]e[/]/[green]w[/] (or north/south/east/west) - move");
    AnsiConsole.MarkupLine("  [green]look[/] (or l)         - redescribe the current room");
    AnsiConsole.MarkupLine("  [green]fight[/] (or f)        - fight a random monster from this level's roster");
    AnsiConsole.MarkupLine("  [green]travel next[/]/[green]prev[/]/[green]<N>[/] - jump between time-travel levels");
    AnsiConsole.MarkupLine("  [green]inventory[/] (or i)    - list what you're carrying");
    AnsiConsole.MarkupLine("  [green]npcs[/] (or who)       - list the other Mutants out in the world");
    AnsiConsole.MarkupLine("  [green]news[/] (or broadcast) - show recent kill-feed events");
    AnsiConsole.MarkupLine("  [green]convert <item>[/]     - destroy an item for Ions");
    AnsiConsole.MarkupLine("  [green]wield <item>[/]       - equip a weapon or armor item");
    AnsiConsole.MarkupLine("    ('<item>' is either its inventory number or its name)");
    AnsiConsole.MarkupLine("  [green]stores[/]              - list every store on this level");
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
