using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Game;

/// <summary>Plain-text renderers for the shared game — the network equivalent of the console's Render* helpers, minus the Spectre markup.</summary>
internal static class Render
{
    public static void Room(SharedGame game, Session session)
    {
        var p = session.Player;
        var content = game.World.GetYear(p.CurrentYear);
        var room = content.Map.GetRoom(p.Position);

        session.Send($"— {content.Era.Name}, {p.CurrentYear} A.D.  {p.Position}");
        session.Send(room.Description);

        var exits = room.ExitDescriptions.Keys.OrderBy(d => d.Name()).ToList();
        session.Send(exits.Count > 0
            ? "Exits: " + string.Join(", ", exits.Select(d => d.Name()))
            : "There are no exits.");

        var others = game.PlayersWith(session).Select(s => s.Player.Name).ToList();
        if (others.Count > 0)
        {
            session.Send($"Also here: {string.Join(", ", others)}.");
        }

        var pop = content.Population;

        var warden = pop.Warden;
        if (warden is not null && !warden.Health.IsDead
            && !p.HasDefeatedWarden(p.CurrentYear) && warden.Position.Equals(p.Position))
        {
            session.Send($"{warden.Name} stands watch here. (fight when ready)");
        }

        var here = pop.MonstersAt(p.Position).ToList();
        foreach (var apex in here.Where(m => m.IsApex))
        {
            session.Send($"A {apex.Name} is here — far bigger than the rest, and it hasn't reacted to you. (fight {LastWord(apex.Name)})");
        }

        var regs = here.Where(m => !m.IsApex).Select(m => m.Name).ToList();
        if (regs.Count > 0)
        {
            session.Send($"{Join(regs)} {(regs.Count == 1 ? "is" : "are")} here. (fight)");
        }

        foreach (var dir in exits)
        {
            if (pop.HasLivingMonsterAt(p.Position.Move(dir)))
            {
                session.Send($"Something stirs to the {dir.Name()}.");
            }
        }

        var ground = pop.LootAt(p.Position);
        if (ground.Count > 0)
        {
            session.Send($"On the ground: {Join(ground.Select(i => i.Name).ToList())}. (take <item>)");
            if (ground.Any(i => i.IsTimeShard))
            {
                session.Send("A Time Shard glints among it — this year's, and yours alone.");
            }
        }

        var store = content.StoreSlots.FirstOrDefault(s => s.Location.Equals(p.Position));
        if (store?.Store is not null)
        {
            session.Send($"There's a store here: {store.Store.Name}. (shop)");
        }
    }

    public static void LookDirection(SharedGame game, Session session, Direction dir)
    {
        var p = session.Player;
        var content = game.World.GetYear(p.CurrentYear);
        var step = content.Map.TryMove(p.Position, dir);
        if (!step.Success || step.DestinationRoom is null)
        {
            session.Send($"You look {dir.Name()} — solid wall, no way through.");
            return;
        }

        session.Send($"To the {dir.Name()} ({step.Destination}): {step.DestinationRoom.Description}");
        var monsters = content.Population.MonstersAt(step.Destination!.Value).Select(m => m.Name).ToList();
        session.Send(monsters.Count > 0 ? $"You can make out {Join(monsters)} in there." : "Nothing moving that you can see.");
        var loot = content.Population.LootAt(step.Destination.Value);
        if (loot.Count > 0)
        {
            session.Send($"On the floor: {Join(loot.Select(i => i.Name).ToList())}.");
        }
    }

    public static void Status(Session session)
    {
        var p = session.Player;
        var tachyons = p.Tachyons.Uncapped ? $"{p.Tachyons.Current} Tachyons" : $"{p.Tachyons.Current}/{p.Tachyons.Max} Tachyons";
        session.Send($"{p.Name} the {p.Class} — HP {p.Health.Current}/{p.Health.Max}  {tachyons}  Credits {p.Credits}  Level {p.Level}  Year {p.CurrentYear} A.D.  Furthest {p.FurthestYearReached}  {p.Position}");
    }

    public static void Inventory(Session session)
    {
        var items = session.Player.Inventory;
        if (items.Count == 0)
        {
            session.Send("You're carrying nothing.");
            return;
        }

        session.Send("Inventory:");
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var tag = it.Type == Core.Items.ItemType.Weapon ? $" (atk +{it.AttackBonus})"
                : it.Type == Core.Items.ItemType.Armor ? $" (def +{it.DefenseBonus})"
                : "";
            session.Send($"  {i + 1}. {it.Name} [{it.Type}, {it.Rarity}, value {it.Value}]{tag}");
        }
    }

    public static void Monsters(SharedGame game, Session session)
    {
        var pop = game.World.GetYear(session.Player.CurrentYear).Population;
        var living = pop.Monsters.Where(m => !m.Health.IsDead).ToList();
        if (living.Count == 0)
        {
            session.Send("No monsters roaming this year right now.");
            return;
        }

        session.Send($"{living.Count} monster(s) roaming this year:");
        foreach (var m in living.OrderBy(m => m.Position.North).ThenBy(m => m.Position.East))
        {
            var mood = AggroModel.MoodFor(m.Aggro).ToString().ToLowerInvariant();
            var apex = m.IsApex ? " (apex)" : "";
            session.Send($"  {m.Name}{apex} — T{m.Tier}  HP {m.Health.Current}/{m.Health.Max}  {mood}  {m.Position}");
        }
    }

    public static string LastWord(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? s : parts[^1];
    }

    private static string Join(IReadOnlyList<string> names)
    {
        var list = names.Select(WithArticle).ToList();
        return list.Count switch
        {
            0 => "nothing",
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}",
        };
    }

    private static string WithArticle(string name)
    {
        var vowel = name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0;
        return (vowel ? "an " : "a ") + name;
    }
}
