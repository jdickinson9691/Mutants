using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Events;
using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.World;
using Mutants.Engine;
using Mutants.Engine.Combat;
using Mutants.Engine.Npc;
using Mutants.Engine.Simulation;
using Spectre.Console;

// Sandbox build covering milestones 2 (grid movement, single hardcoded
// level), 3 (combat, loot drops, convert/sell/wield), 4 (NPC simulation
// loop), and 5 (stores and the Riblet economy) per docs/TECH_STACK.md's
// milestone sequencing. Time travel is the next milestone. All game rules
// here (movement legality, combat resolution, NPC AI, store transactions,
// character state) live in Mutants.Core / Mutants.Engine; this file is
// presentation/input only, per docs/AGENTS.md's Console/UI Agent
// contract. There's no spatial monster placement yet (rooms don't carry
// monsters) — "fight" spawns a random test monster on demand so the full
// loot loop is playable ahead of that; NPCs use the same on-demand spawn
// via NpcController. Stores, unlike monsters, ARE placed spatially (see
// Economy.TestStores) — buying/selling requires standing in the right
// room, same as movement already works. docs/GDD.md §9's real background
// tick (every ~2 seconds, independent of player input) isn't implemented
// — this console instead advances the world by one tick per player
// command, a synchronous stand-in that keeps the sandbox simple and
// scriptable.

// Input is read via plain Console.ReadLine() rather than Spectre's
// interactive prompts (TextPrompt/SelectionPrompt): those require a real
// interactive terminal and hard-fail when stdin is redirected (e.g. piped
// input, some CI/test harnesses). Spectre is still used for all output
// styling. Console.ReadLine() also degrades cleanly to null at end-of-input
// instead of throwing, which we treat as "quit."

AnsiConsole.Write(new FigletText("Chronomutants").Color(Color.Green));
AnsiConsole.MarkupLine("[grey](engine sandbox build — grid movement, single test level)[/]");
AnsiConsole.WriteLine();

var name = ReadNonEmptyLine("What is your name, Mutant? ");
if (name is null)
{
    return;
}

var characterClass = ReadClassChoice();
if (characterClass is null)
{
    return;
}

var mutant = new Mutant(name, characterClass.Value);
var level = TestLevel.Build();
mutant.PlaceAt(level.Start);
var random = new SystemRandomSource();
var storeSlots = TestStores.Build();

const int NpcPopulationSize = 5; // arbitrary sandbox default - GDD §7 calls this "a configurable population"
var npcs = NpcPopulation.Spawn(NpcPopulationSize, level, random);
var simulation = new WorldSimulation(level, npcs, random, storeSlots);
var shownBroadcastCount = 0;

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"Welcome, [bold]{Markup.Escape(mutant.Name)}[/] the [bold]{mutant.Class}[/]. Type [yellow]help[/] for commands.");
AnsiConsole.MarkupLine($"[grey]{NpcPopulationSize} other Mutants are already out there, fending for themselves.[/]");
AnsiConsole.WriteLine();

RenderRoom(mutant, level, storeSlots);

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
            RenderRoom(mutant, level, storeSlots);
            break;

        case "status" or "stat":
            RenderStatusBar(mutant, level);
            break;

        case "fight" or "f":
            if (!HandleFight(mutant, level, random, simulation.Broadcast))
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
            RenderStores(storeSlots);
            break;

        case "shop":
            HandleShop(mutant, storeSlots);
            break;

        case "buy-store":
            HandleBuyStore(mutant, storeSlots);
            break;

        case "collect":
            HandleCollect(mutant, storeSlots);
            break;

        default:
            var (command, argument) = SplitCommand(input);

            if (command is "sell")
            {
                HandleSellToStore(mutant, storeSlots, argument);
                break;
            }

            if (command is "buy")
            {
                HandleBuyFromStore(mutant, storeSlots, argument);
                break;
            }

            if (command is "deposit" or "withdraw" or "reprice")
            {
                HandleStoreManagement(mutant, storeSlots, command, argument);
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

            HandleMove(mutant, level, direction.Value, storeSlots);
            break;
    }

    if (running && !mutant.Health.IsDead)
    {
        simulation.Tick(mutant);
        shownBroadcastCount = RenderNewBroadcastEvents(simulation.Broadcast, shownBroadcastCount);
    }
}

AnsiConsole.MarkupLine(mutant.Health.IsDead
    ? "[grey]Game over.[/]"
    : "[grey]Farewell, Mutant.[/]");
return;

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

static void HandleShop(Mutant mutant, IReadOnlyList<StoreSlot> storeSlots)
{
    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
    if (slot?.Store is not { } store)
    {
        AnsiConsole.MarkupLine("[red]There's no store here.[/]");
        return;
    }

    RenderShop(store);
}

static void HandleBuyFromStore(Mutant mutant, IReadOnlyList<StoreSlot> storeSlots, string argument)
{
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

static void HandleSellToStore(Mutant mutant, IReadOnlyList<StoreSlot> storeSlots, string argument)
{
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

static void HandleBuyStore(Mutant mutant, IReadOnlyList<StoreSlot> storeSlots)
{
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

static void HandleCollect(Mutant mutant, IReadOnlyList<StoreSlot> storeSlots)
{
    var owned = storeSlots.Where(s => s.Store?.Owner == mutant).ToList();
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

static void HandleStoreManagement(Mutant mutant, IReadOnlyList<StoreSlot> storeSlots, string command, string argument)
{
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

/// <summary>Spawns a random test monster and resolves combat. Returns false if the Mutant was defeated (caller should end the session).</summary>
static bool HandleFight(Mutant mutant, LevelMap level, IRandomSource random, BroadcastChannel broadcast)
{
    var monster = TestMonsters.All[System.Random.Shared.Next(TestMonsters.All.Count)]();
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

        RenderStatusBar(mutant, level);
        return true;
    }

    // docs/GDD.md §3.3 (death & recall - dropping inventory, returning to a
    // home base with an Ion penalty) is not implemented yet; a defeat here
    // just ends the session.
    AnsiConsole.MarkupLine($"[red]You were defeated by the {Markup.Escape(monster.Name)}...[/]");
    broadcast.Publish(GameEvent.Slain(mutant.Name, monster.Name));
    return false;
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

static void HandleMove(Mutant mutant, LevelMap level, Direction direction, IReadOnlyList<StoreSlot> storeSlots)
{
    var result = level.TryMove(mutant.Position, direction);
    if (!result.Success)
    {
        AnsiConsole.MarkupLine("[red]You can't go that way.[/]");
        return;
    }

    mutant.MoveTo(result.Destination!.Value);
    RenderRoom(mutant, level, storeSlots);
}

static void RenderRoom(Mutant mutant, LevelMap level, IReadOnlyList<StoreSlot> storeSlots)
{
    var room = level.GetRoom(mutant.Position);

    RenderStatusBar(mutant, level);
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

    var slot = FindStoreSlotAt(storeSlots, mutant.Position);
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

static void RenderStatusBar(Mutant mutant, LevelMap level)
{
    var status = $"[red]HP {mutant.Health.Current}/{mutant.Health.Max}[/]  " +
                 $"[blue]Ions {mutant.Ions.Current}/{mutant.Ions.Max}[/]  " +
                 $"[yellow]Riblets {mutant.Riblets}[/]  " +
                 $"Level {mutant.Level}  " +
                 $"Location {Markup.Escape(mutant.Position.ToString())}";

    AnsiConsole.Write(new Panel(status)
        .Header($"[bold]{Markup.Escape(mutant.Name)}[/] — {Markup.Escape(level.Name)}")
        .Expand());
}

static void RenderHelp()
{
    AnsiConsole.MarkupLine("[yellow]Commands:[/]");
    AnsiConsole.MarkupLine("  [green]n[/]/[green]s[/]/[green]e[/]/[green]w[/] (or north/south/east/west) - move");
    AnsiConsole.MarkupLine("  [green]look[/] (or l)         - redescribe the current room");
    AnsiConsole.MarkupLine("  [green]fight[/] (or f)        - fight a random monster");
    AnsiConsole.MarkupLine("  [green]inventory[/] (or i)    - list what you're carrying");
    AnsiConsole.MarkupLine("  [green]npcs[/] (or who)       - list the other Mutants out in the world");
    AnsiConsole.MarkupLine("  [green]news[/] (or broadcast) - show recent kill-feed events");
    AnsiConsole.MarkupLine("  [green]convert <item>[/]     - destroy an item for Ions");
    AnsiConsole.MarkupLine("  [green]wield <item>[/]       - equip a weapon or armor item");
    AnsiConsole.MarkupLine("    ('<item>' is either its inventory number or its name)");
    AnsiConsole.MarkupLine("  [green]stores[/]              - list every store in the world");
    AnsiConsole.MarkupLine("  [green]shop[/]                - browse the store in your current room");
    AnsiConsole.MarkupLine("  [green]buy <item>[/]         - buy a listed item (must be at a store)");
    AnsiConsole.MarkupLine("  [green]sell <item>[/]        - sell an item to the store here (must be at a store)");
    AnsiConsole.MarkupLine("  [green]buy-store[/]           - purchase an empty store slot you're standing in");
    AnsiConsole.MarkupLine("  [green]deposit <item> <price>[/] - list your own item for sale at your store");
    AnsiConsole.MarkupLine("  [green]withdraw <item>[/]    - pull a listing back into your inventory");
    AnsiConsole.MarkupLine("  [green]reprice <item> <price>[/] - change a listing's asking price");
    AnsiConsole.MarkupLine("  [green]collect[/]             - withdraw your store's earnings into your Riblets");
    AnsiConsole.MarkupLine("  [green]status[/]              - show the status bar");
    AnsiConsole.MarkupLine("  [green]help[/] (or ?)         - show this list");
    AnsiConsole.MarkupLine("  [green]quit[/] (or exit)      - leave the game");
}
