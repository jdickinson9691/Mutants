using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;

/// <summary>
/// `ChronoTravelers.exe --connect &lt;url&gt;` — a thin SignalR client onto a
/// ChronoTravelers.Server shared world. Everything the server pushes to
/// <c>Receive</c> is printed as-is; the local loop reads a line and
/// forwards it via <c>Send</c>. The login / character-select handshake is
/// a few string-typed hub invokes (see GameHub).
/// </summary>
internal static class ServerClient
{
    public static async Task RunAsync(string url)
    {
        var hubUrl = url.TrimEnd('/') + "/game";
        var conn = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        conn.On<string>("Receive", AnsiConsole.WriteLine);
        conn.Reconnecting += _ => { AnsiConsole.MarkupLine("[grey](connection dropped — reconnecting…)[/]"); return Task.CompletedTask; };
        conn.Reconnected += _ => { AnsiConsole.MarkupLine("[grey](reconnected)[/]"); return Task.CompletedTask; };
        conn.Closed += _ => { AnsiConsole.MarkupLine("[grey](disconnected)[/]"); return Task.CompletedTask; };

        AnsiConsole.MarkupLine($"[grey]Connecting to {Markup.Escape(hubUrl)} …[/]");
        try
        {
            await conn.StartAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't reach the server:[/] {Markup.Escape(ex.Message)}");
            return;
        }

        AnsiConsole.MarkupLine("[green]Connected.[/]");

        if (!await LoginAsync(conn) || !await ChooseCharacterAsync(conn))
        {
            await conn.StopAsync();
            return;
        }

        // In the world — the server has already pushed the room. Pump input.
        while (true)
        {
            AnsiConsole.Markup("[green]>[/] ");
            var line = System.Console.ReadLine();
            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (trimmed.Length == 0)
            {
                continue;
            }

            try
            {
                await conn.InvokeAsync("Send", trimmed);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]send failed:[/] {Markup.Escape(ex.Message)}");
                break;
            }
        }

        await conn.StopAsync();
        AnsiConsole.MarkupLine("[grey]Farewell, Traveler.[/]");
    }

    private static async Task<bool> LoginAsync(HubConnection conn)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var account = Prompt("Account name: ");
            if (account is null)
            {
                return false;
            }

            var password = Prompt("Password: ");
            if (password is null)
            {
                return false;
            }

            var result = await conn.InvokeAsync<string>("Login", account, password);
            switch (result)
            {
                case "ok":
                    return true;
                case "created":
                    AnsiConsole.MarkupLine("[green]New account created.[/]");
                    return true;
                case "badpassword":
                    AnsiConsole.MarkupLine("[red]Wrong password.[/]");
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(result)}[/]");
                    break;
            }
        }

        AnsiConsole.MarkupLine("[red]Too many attempts.[/]");
        return false;
    }

    private static async Task<bool> ChooseCharacterAsync(HubConnection conn)
    {
        var listing = await conn.InvokeAsync<string>("Characters");
        var lines = string.IsNullOrEmpty(listing) ? [] : listing.Split('\n');

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(lines.Length > 0 ? "[yellow]Your Travelers:[/]" : "[grey]You have no Travelers yet.[/]");
        foreach (var l in lines)
        {
            AnsiConsole.MarkupLine("  " + Markup.Escape(l));
        }

        var newOption = lines.Length + 1;
        AnsiConsole.MarkupLine($"  {newOption}. New Traveler [grey](a role you haven't played)[/]");

        while (true)
        {
            var pick = Prompt("> ");
            if (pick is null)
            {
                return false;
            }

            if (int.TryParse(pick, out var n) && n >= 1 && n <= lines.Length)
            {
                var r = await conn.InvokeAsync<string>("Continue", n);
                if (r == "joined")
                {
                    return true;
                }

                AnsiConsole.MarkupLine($"[red]{Markup.Escape(r)}[/]");
                continue;
            }

            if (pick.Equals("new", StringComparison.OrdinalIgnoreCase) || (int.TryParse(pick, out var m) && m == newOption))
            {
                if (await CreateAsync(conn))
                {
                    return true;
                }

                continue;
            }

            AnsiConsole.MarkupLine($"[red]Pick 1–{newOption}.[/]");
        }
    }

    private static async Task<bool> CreateAsync(HubConnection conn)
    {
        var name = Prompt("Name your Traveler: ");
        if (name is null)
        {
            return false;
        }

        var offered = (await conn.InvokeAsync<string>("OfferedClasses")).Split(',', StringSplitOptions.RemoveEmptyEntries);
        AnsiConsole.MarkupLine("Choose your role:");
        for (var i = 0; i < offered.Length; i++)
        {
            AnsiConsole.MarkupLine($"  {i + 1}. {offered[i]}");
        }

        while (true)
        {
            var pick = Prompt("> ");
            if (pick is null)
            {
                return false;
            }

            if (int.TryParse(pick, out var n) && n >= 1 && n <= offered.Length)
            {
                var r = await conn.InvokeAsync<string>("CreateCharacter", name, offered[n - 1]);
                if (r == "joined")
                {
                    return true;
                }

                AnsiConsole.MarkupLine($"[red]{Markup.Escape(r)}[/]");
                return false;
            }

            AnsiConsole.MarkupLine($"[red]Pick 1–{offered.Length}.[/]");
        }
    }

    private static string? Prompt(string text)
    {
        AnsiConsole.Markup(Markup.Escape(text));
        var line = System.Console.ReadLine();
        return line?.Trim();
    }
}
