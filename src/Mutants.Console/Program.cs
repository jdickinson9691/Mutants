using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.World;
using Mutants.Engine;
using Mutants.Engine.Combat;
using Spectre.Console;

// Sandbox build covering milestones 2 (grid movement, single hardcoded
// level) and 3 (combat, loot drops, convert/sell/wield) per
// docs/TECH_STACK.md's milestone sequencing. NPCs, stores/economy, and
// time travel are later milestones. All game rules here (movement
// legality, combat resolution, character state) live in Mutants.Core /
// Mutants.Engine; this file is presentation/input only, per
// docs/AGENTS.md's Console/UI Agent contract. There's no spatial monster
// placement yet (rooms don't carry monsters) — "fight" spawns a random
// test monster on demand so the full loot loop is playable ahead of that.

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

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"Welcome, [bold]{Markup.Escape(mutant.Name)}[/] the [bold]{mutant.Class}[/]. Type [yellow]help[/] for commands.");
AnsiConsole.WriteLine();

RenderRoom(mutant, level);

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
            RenderRoom(mutant, level);
            break;

        case "status" or "stat":
            RenderStatusBar(mutant, level);
            break;

        case "fight" or "f":
            if (!HandleFight(mutant, level, random))
            {
                running = false; // defeated - see HandleFight
            }

            break;

        case "inventory" or "i":
            RenderInventory(mutant);
            break;

        default:
            var (command, argument) = SplitCommand(input);
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

            HandleMove(mutant, level, direction.Value);
            break;
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

/// <summary>Handles "convert/sell/wield &lt;item&gt;" commands. Returns false if <paramref name="command"/> isn't one of those verbs.</summary>
static bool TryHandleItemCommand(Mutant mutant, string command, string argument)
{
    if (command is not ("convert" or "sell" or "wield"))
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

        case "sell":
            var riblets = mutant.Sell(item);
            AnsiConsole.MarkupLine($"[yellow]Sold {Markup.Escape(item.Name)} for {riblets} Riblets.[/]");
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

/// <summary>Spawns a random test monster and resolves combat. Returns false if the Mutant was defeated (caller should end the session).</summary>
static bool HandleFight(Mutant mutant, LevelMap level, IRandomSource random)
{
    var monster = TestMonsters.All[System.Random.Shared.Next(TestMonsters.All.Count)]();

    AnsiConsole.MarkupLine($"A [bold]{Markup.Escape(monster.Name)}[/] (tier {monster.Tier}) attacks!");

    var result = CombatResolver.Fight(mutant, monster, random);
    foreach (var line in result.Log)
    {
        AnsiConsole.MarkupLine(Markup.Escape(line));
    }

    if (result.MutantWon)
    {
        AnsiConsole.MarkupLine($"[green]You defeated the {Markup.Escape(monster.Name)}! +{result.XpAwarded} XP.[/]");
        RenderStatusBar(mutant, level);
        return true;
    }

    // docs/GDD.md §3.3 (death & recall - dropping inventory, returning to a
    // home base with an Ion penalty) is not implemented yet; a defeat here
    // just ends the session.
    AnsiConsole.MarkupLine($"[red]You were defeated by the {Markup.Escape(monster.Name)}...[/]");
    return false;
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

static void HandleMove(Mutant mutant, LevelMap level, Direction direction)
{
    var result = level.TryMove(mutant.Position, direction);
    if (!result.Success)
    {
        AnsiConsole.MarkupLine("[red]You can't go that way.[/]");
        return;
    }

    mutant.MoveTo(result.Destination!.Value);
    RenderRoom(mutant, level);
}

static void RenderRoom(Mutant mutant, LevelMap level)
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
    AnsiConsole.MarkupLine("  [green]convert <item>[/]     - destroy an item for Ions");
    AnsiConsole.MarkupLine("  [green]sell <item>[/]        - sell an item for Riblets");
    AnsiConsole.MarkupLine("  [green]wield <item>[/]       - equip a weapon or armor item");
    AnsiConsole.MarkupLine("    ('<item>' is either its inventory number or its name)");
    AnsiConsole.MarkupLine("  [green]status[/]              - show the status bar");
    AnsiConsole.MarkupLine("  [green]help[/] (or ?)         - show this list");
    AnsiConsole.MarkupLine("  [green]quit[/] (or exit)      - leave the game");
}
