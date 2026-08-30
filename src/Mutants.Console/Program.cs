using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.World;
using Spectre.Console;

// Milestone 2 sandbox: grid movement on a single hardcoded level, playable
// via console, per docs/TECH_STACK.md's milestone sequencing. No combat,
// NPCs, or economy yet — those are later milestones. All game rules here
// (movement legality, character state) live in Mutants.Core; this file is
// presentation/input only, per docs/AGENTS.md's Console/UI Agent contract.

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

        default:
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

AnsiConsole.MarkupLine("[grey]Farewell, Mutant.[/]");
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
    AnsiConsole.MarkupLine("  [green]look[/] (or l)   - redescribe the current room");
    AnsiConsole.MarkupLine("  [green]status[/]        - show the status bar");
    AnsiConsole.MarkupLine("  [green]help[/] (or ?)   - show this list");
    AnsiConsole.MarkupLine("  [green]quit[/] (or exit) - leave the game");
}
