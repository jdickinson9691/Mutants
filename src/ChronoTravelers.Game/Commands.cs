using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Events;
using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine;
using ChronoTravelers.Engine.Combat;

namespace ChronoTravelers.Game;

/// <summary>
/// The shared-world command set — a focused subset of the console's verbs,
/// transport-agnostic (everything goes through <see cref="IGameOutput"/>).
/// Every call runs under the <see cref="SharedGame"/> lock. Interactive
/// round-by-round combat is out of scope here: <c>fight</c> auto-resolves
/// and drops the loot on the floor. Store commands (docs/GDD.md §6) are at
/// parity with the console: browsing/buying/selling at any store, and the
/// owner-only slot purchase / stock / withdraw / reprice / deposit /
/// charge / collect verbs for a player-owned one — see docs/SERVER.md.
/// </summary>
internal static class Commands
{
    private static readonly SystemRandomSource Rng = new();

    public static void Run(SharedGame game, Session session, string line)
    {
        var input = line.Trim();
        if (input.Length == 0)
        {
            return;
        }

        var space = input.IndexOf(' ');
        var verb = (space < 0 ? input : input[..space]).ToLowerInvariant();
        var arg = space < 0 ? "" : input[(space + 1)..].Trim();

        session.TickState.ActedIdly = IsIdle(verb);

        switch (verb)
        {
            case "help" or "?":
                Help(session);
                break;

            case "look" or "l":
                if (arg.Length == 0)
                {
                    Render.Room(game, session);
                }
                else if (DirectionExtensions.Parse(arg) is { } lookDir)
                {
                    Render.LookDirection(game, session, lookDir);
                }
                else
                {
                    session.Send("Look where? Try 'look north', or just 'look'.");
                }

                break;

            case "n" or "s" or "e" or "w" or "north" or "south" or "east" or "west":
                Move(game, session, DirectionExtensions.Parse(verb)!.Value);
                break;

            case "status" or "stat":
                Render.Status(session);
                break;

            case "inventory" or "inv" or "i" or "bag":
                Render.Inventory(session);
                break;

            case "monsters" or "mobs":
                Render.Monsters(game, session);
                break;

            case "who":
                Who(game, session);
                break;

            case "say":
                Say(game, session, arg);
                break;

            case "news" or "broadcast":
                News(game, session);
                break;

            case "heal":
                Heal(session);
                break;

            case "wait" or "z":
                session.Send("You wait a moment.");
                break;

            case "take" or "grab" or "get":
                Take(game, session, arg);
                break;

            case "fight" or "f" or "attack" or "a" or "kill":
                Fight(game, session, arg);
                break;

            case "wield" or "equip":
                Wield(session, arg);
                break;

            case "convert" or "con":
                Convert(session, arg);
                break;

            case "travel":
                Travel(game, session, arg);
                break;

            case "stores":
                Stores(game, session);
                break;

            case "shop":
                Shop(game, session);
                break;

            case "buy":
                BuyFromStore(game, session, arg);
                break;

            case "sell":
                SellToStore(game, session, arg);
                break;

            case "buy-store":
                BuyStore(game, session);
                break;

            case "stock" or "charge" or "deposit" or "withdraw" or "reprice":
                StoreManagement(game, session, verb, arg);
                break;

            case "collect":
                Collect(game, session);
                break;

            default:
                session.Send($"Unknown command: '{verb}'. Type 'help'.");
                break;
        }
    }

    private static bool IsIdle(string verb) => verb is
        "look" or "l" or "status" or "stat" or "inventory" or "inv" or "i" or "bag"
        or "monsters" or "mobs" or "who" or "news" or "broadcast" or "help" or "?" or "wait" or "z"
        or "stores"; // "shop"/buying/selling/store management are doing-something, like the console (Program.cs's IsIdleCommand)

    private static void Move(SharedGame game, Session session, Direction dir)
    {
        var p = session.Player;
        var map = game.World.GetYear(p.CurrentYear).Map;
        var move = map.TryMove(p.Position, dir);
        if (!move.Success)
        {
            session.Send("You can't go that way.");
            return;
        }

        p.MoveTo(move.Destination!.Value);
        foreach (var other in game.PlayersWith(session))
        {
            other.Send($"{p.Name} arrives from the {Opposite(dir).Name()}.");
        }

        Render.Room(game, session);
    }

    private static void Heal(Session session)
    {
        var p = session.Player;
        if (p.Health.Current >= p.Health.Max)
        {
            session.Send("You're already at full health.");
            return;
        }

        if (p.Tachyons.Current <= 0)
        {
            session.Send("Not enough Tachyons to heal.");
            return;
        }

        var healed = p.Heal();
        session.Send($"You heal for {healed} HP. ({p.Health.Current}/{p.Health.Max} HP, {p.Tachyons.Current} Tachyons left)");
    }

    private static void Take(SharedGame game, Session session, string arg)
    {
        var p = session.Player;
        var pop = game.World.GetYear(p.CurrentYear).Population;
        var pile = pop.LootAt(p.Position);
        if (pile.Count == 0)
        {
            session.Send("Nothing on the ground here.");
            return;
        }

        if (arg.Length == 0 || arg.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            Item? it;
            while ((it = pop.TakeGroundLoot(p.Position, _ => true)) is not null)
            {
                p.AddToInventory(it);
                session.Send($"You pick up the {it.Name}.");
            }

            return;
        }

        var picked = pop.TakeGroundLoot(p.Position, i => i.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
        if (picked is null)
        {
            session.Send($"No '{arg}' on the ground here.");
            return;
        }

        p.AddToInventory(picked);
        session.Send($"You pick up the {picked.Name}.");
    }

    private static void Fight(SharedGame game, Session session, string arg)
    {
        var p = session.Player;
        var content = game.World.GetYear(p.CurrentYear);
        var pop = content.Population;

        var here = pop.MonstersAt(p.Position).ToList();

        Core.Monsters.Monster? target = null;
        var isWarden = false;

        if (pop.Warden is { } w && !w.Health.IsDead && !p.HasDefeatedWarden(p.CurrentYear) && w.Position.Equals(p.Position))
        {
            target = w;
            isWarden = true;
        }
        else if (here.Count > 0)
        {
            target = arg.Length > 0
                ? here.FirstOrDefault(m => m.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)) ?? here.First(m => !m.IsApex || here.All(x => x.IsApex))
                : here.FirstOrDefault(m => !m.IsApex) ?? here[0];
        }

        if (target is null)
        {
            session.Send("Nothing here to fight.");
            return;
        }

        var levelBefore = p.Level;
        session.Send($"You close on the {target.Name} (tier {target.Tier})!");
        var result = CombatResolver.Fight(p, target, Rng);

        foreach (var logLine in result.Log)
        {
            session.Send(logLine);
        }

        var year = p.CurrentYear;

        if (result.TravelerWon)
        {
            session.Send($"You defeated the {target.Name}! +{result.XpAwarded} XP.");
            game.Broadcast.Publish(GameEvent.Slain(target.Name, p.Name, year, victimIsCreature: true));

            // Loot never stays in the pack — CombatResolver.Fight auto-added
            // it, so move it (plus the monster's scavenged items) to the floor.
            var toGround = result.ItemsDropped.ToList();
            foreach (var it in toGround)
            {
                p.RemoveFromInventory(it);
            }

            toGround.AddRange(target.Inventory);
            foreach (var it in toGround)
            {
                pop.AddGroundLoot(p.Position, it);
            }

            if (isWarden)
            {
                p.RecordWardenDefeat(year);
                session.Send(toGround.Count > 0
                    ? $"The Warden of {year} falls — its haul lies at your feet. (take)"
                    : $"The Warden of {year} is broken.");
            }
            else
            {
                pop.RemoveMonster(target);
                if (toGround.Count > 0)
                {
                    session.Send($"It drops {string.Join(", ", toGround.Select(i => i.Name))} on the ground. (take)");
                }
            }

            if (p.Level > levelBefore)
            {
                game.Broadcast.Publish(GameEvent.LevelReached(p.Name, p.Level, year));
            }
        }
        else
        {
            session.Send($"You were beaten down by the {target.Name}...");
            game.Broadcast.Publish(GameEvent.Slain(p.Name, target.Name, year, killerIsCreature: true));
            // Death is handled by SharedGame.Tick (respawn upstream).
        }
    }

    private static void Wield(Session session, string arg)
    {
        // Prefer a wieldable match so `wield shard` grabs the "Time Shard"
        // weapon, not junk "Salvage Shard" that also contains the word.
        var item = FindItem(session, arg, static i => i.IsWieldable);
        if (item is null)
        {
            session.Send($"No item matching '{arg}' in your inventory.");
            return;
        }

        if (!item.IsWieldable)
        {
            session.Send($"{item.Name} can't be wielded.");
            return;
        }

        session.Player.Wield(item);
        var off = item.IsClassCompatible(session.Player.Class) ? "" : " (off-class — reduced effect)";
        session.Send($"Wielded {item.Name}.{off}");
    }

    private static void Convert(Session session, string arg)
    {
        var item = FindItem(session, arg);
        if (item is null)
        {
            session.Send($"No item matching '{arg}' in your inventory.");
            return;
        }

        var gained = session.Player.Convert(item);
        session.Send($"Converted {item.Name} for {gained} Tachyons. ({session.Player.Tachyons.Current} Tachyons)");
    }

    private static void Travel(SharedGame game, Session session, string arg)
    {
        var p = session.Player;
        int target;

        if (arg.StartsWith('+') && int.TryParse(arg[1..], out var fwd))
        {
            target = p.CurrentYear + fwd;
        }
        else if (arg.StartsWith('-') && int.TryParse(arg[1..], out var back))
        {
            target = p.CurrentYear - back;
        }
        else if (!int.TryParse(arg, out target))
        {
            session.Send("Travel where? Try 'travel 3200', 'travel +250', 'travel -100'.");
            return;
        }

        target = Math.Clamp(target, TimeScale.MinYear, TimeScale.MaxYear);
        if (target == p.CurrentYear)
        {
            session.Send("You're already there.");
            return;
        }

        var cost = TachyonEconomy.TimeTravelCost(p.CurrentYear, target);
        if (!p.Tachyons.CanAfford(cost))
        {
            session.Send($"Not enough Tachyons ({cost} needed, you have {p.Tachyons.Current}).");
            return;
        }

        p.Tachyons.Spend(cost);
        var from = p.CurrentYear;
        p.SetCurrentYear(target);
        p.PlaceAt(game.World.GetYear(target).Map.Start);
        game.Broadcast.Publish(GameEvent.TimeTraveled(p.Name, target));
        game.AnnounceExcept(session.Id, $"{p.Name} rode a surge from {from} to {target} A.D.");
        session.Send($"You travel to {target} A.D. — {game.World.GetYear(target).Era.Name}. ({cost} Tachyons)");
        Render.Room(game, session);
    }

    /// <summary>Every store slot in the player's current year — docs/GDD.md §6, console parity (Program.cs's <c>stores</c>/<c>RenderStores</c>).</summary>
    private static void Stores(SharedGame game, Session session)
    {
        var slots = game.World.GetYear(session.Player.CurrentYear).StoreSlots;
        if (slots.Count == 0)
        {
            session.Send("No stores this year.");
            return;
        }

        session.Send($"{slots.Count} store slot(s) this year:");
        foreach (var slot in slots)
        {
            var owner = slot.Store switch
            {
                null => "vacant",
                { IsGovernmentRun: true } => "government",
                var s => s.Owner!.Name,
            };
            var items = slot.Store?.Listings.Count.ToString() ?? "-";
            var status = slot.IsAvailableForPurchase
                ? $"for sale ({slot.PurchaseCost} Credits{(slot.HasAbandonedInventory ? ", pre-stocked" : "")})"
                : slot.Store!.IsGovernmentRun ? "occupied" : $"occupied ({slot.Store.TachyonReserve} Tachyon reserve)";
            session.Send($"  {slot.Name} @ {slot.Location} — owner: {owner}, items: {items}, {status}");
        }
    }

    /// <summary>Browses the store in the player's current room — console parity (Program.cs's <c>HandleShop</c>/<c>RenderShop</c>).</summary>
    private static void Shop(SharedGame game, Session session)
    {
        var slot = StoreSlotHere(game, session);
        if (slot?.Store is not { } store)
        {
            session.Send("There's no store here.");
            return;
        }

        session.Send($"{store.Name} — Capital: {store.Capital} Credits");
        if (store.Listings.Count == 0)
        {
            session.Send("Nothing for sale right now.");
            return;
        }

        for (var i = 0; i < store.Listings.Count; i++)
        {
            var l = store.Listings[i];
            session.Send($"  {i + 1}. {l.Item.Name} [{l.Item.Type}, {l.Item.Rarity}, tier {l.Item.Tier}] — {l.AskingPrice} Credits");
        }
    }

    /// <summary>Buys a listed item from the store in the player's current room — console parity (Program.cs's <c>HandleBuyFromStore</c>).</summary>
    private static void BuyFromStore(SharedGame game, Session session, string arg)
    {
        var slot = StoreSlotHere(game, session);
        if (slot?.Store is not { } store)
        {
            session.Send("There's no store here to buy from.");
            return;
        }

        var listing = FindListing(store, arg);
        if (listing is null)
        {
            session.Send(arg.Length == 0
                ? "Buy what? Type 'shop' to see what's for sale."
                : $"'{arg}' isn't for sale here.");
            return;
        }

        var p = session.Player;
        if (p.Credits < listing.AskingPrice)
        {
            session.Send($"You can't afford {listing.Item.Name} ({listing.AskingPrice} Credits; you have {p.Credits}).");
            return;
        }

        store.SellToTraveler(p, listing);
        session.Send($"Bought {listing.Item.Name} for {listing.AskingPrice} Credits.");
    }

    /// <summary>Sells an item (or dumps junk) to the store in the player's current room — console parity (Program.cs's <c>HandleSellToStore</c>).</summary>
    private static void SellToStore(SharedGame game, Session session, string arg)
    {
        var slot = StoreSlotHere(game, session);
        if (slot?.Store is not { } store)
        {
            session.Send("You need to be at a store to sell. Try 'convert' to destroy an item for Tachyons instead, or 'stores' to find one.");
            return;
        }

        var p = session.Player;

        // 'sell all' / 'sell junk' — clears the vendor trash (Junk items
        // only; gear and consumables you keep unless you name them).
        if (arg.Trim() is "all" or "junk" or "*")
        {
            var junk = p.Inventory.Where(i => i.Type == ItemType.Junk).ToList();
            if (junk.Count == 0)
            {
                session.Send("No junk to sell. Name an item to sell that instead.");
                return;
            }

            var total = 0;
            var count = 0;
            foreach (var j in junk)
            {
                var got = store.BuyFromTraveler(p, j);
                if (got is null)
                {
                    break;
                }

                total += got.Value;
                count++;
            }

            session.Send($"Sold {count} junk item(s) to {store.Name} for {total} Credits.");
            return;
        }

        var item = FindItem(session, arg);
        if (item is null)
        {
            session.Send(arg.Length == 0
                ? "Sell what? Type 'inventory' to see what you're carrying, or 'sell all' to dump junk."
                : $"No item matching '{arg}' in your inventory.");
            return;
        }

        var price = store.BuyFromTraveler(p, item);
        if (price is null)
        {
            session.Send($"{store.Name} can't afford to buy that right now.");
            return;
        }

        session.Send($"Sold {item.Name} to {store.Name} for {price} Credits.");
    }

    /// <summary>Purchases the empty store slot the player is standing in — console parity (Program.cs's <c>HandleBuyStore</c>).</summary>
    private static void BuyStore(SharedGame game, Session session)
    {
        var slot = StoreSlotHere(game, session);
        if (slot is null)
        {
            session.Send("There's no store slot here.");
            return;
        }

        if (!slot.IsAvailableForPurchase)
        {
            session.Send($"{slot.Name} is already occupied.");
            return;
        }

        var p = session.Player;
        if (p.Credits < slot.PurchaseCost)
        {
            session.Send($"You need {slot.PurchaseCost} Credits to buy this slot; you have {p.Credits}.");
            return;
        }

        var hadAbandonedInventory = slot.HasAbandonedInventory;
        slot.Purchase(p);
        session.Send($"You now own a store here: {slot.Store!.Name}! Use stock/withdraw/reprice to manage listings, deposit/charge/collect to move Credits/Tachyons in and out. It'll still be yours next session — but only if you keep charge-ing its Tachyon maintenance.");
        if (hadAbandonedInventory)
        {
            session.Send($"The previous owner's old stock came with it — {slot.Store.Listings.Count} item(s) already for sale.");
        }
    }

    /// <summary>
    /// Collects from every store the player owns across every year the
    /// shared world has visited — not just their current room (docs/GDD.md
    /// §6.2's "idle-income loop"). Console parity (Program.cs's
    /// <c>HandleCollect</c>), using the same shared <see cref="TimeWorld.VisitedYears"/>.
    /// </summary>
    private static void Collect(SharedGame game, Session session)
    {
        var p = session.Player;
        var owned = game.World.VisitedYears
            .SelectMany(y => game.World.GetYear(y).StoreSlots)
            .Where(s => s.Store?.Owner == p)
            .ToList();

        if (owned.Count == 0)
        {
            session.Send("You don't own a store. Find an empty slot and use 'buy-store'.");
            return;
        }

        var totalCollected = 0;
        foreach (var slot in owned)
        {
            var capital = slot.Store!.Capital;
            if (capital > 0)
            {
                totalCollected += slot.Store.CollectCapital(p, capital);
            }
        }

        session.Send(totalCollected > 0
            ? $"Collected {totalCollected} Credits from your store(s)."
            : "Nothing to collect yet.");
    }

    /// <summary>Owner-only store upkeep verbs (stock/charge/deposit/withdraw/reprice) at the store in the player's current room — console parity (Program.cs's <c>HandleStoreManagement</c>).</summary>
    private static void StoreManagement(SharedGame game, Session session, string command, string arg)
    {
        var slot = StoreSlotHere(game, session);
        var p = session.Player;
        if (slot?.Store is not { } store || store.Owner != p)
        {
            session.Send("You need to be at a store you own to do that.");
            return;
        }

        switch (command)
        {
            case "withdraw":
            {
                var listing = FindListing(store, arg);
                if (listing is null)
                {
                    session.Send($"No listing matching '{arg}' at {store.Name}.");
                    return;
                }

                store.Withdraw(p, listing);
                session.Send($"Withdrew {listing.Item.Name} back into your inventory.");
                break;
            }

            case "deposit":
            {
                if (!int.TryParse(arg.Trim(), out var amount) || amount < 1)
                {
                    session.Send("Usage: deposit <credits> - funds your store's Capital, which pays for buying from other travelers.");
                    return;
                }

                if (p.Credits < amount)
                {
                    session.Send($"You only have {p.Credits} Credits.");
                    return;
                }

                store.Deposit(p, amount);
                session.Send($"Deposited {amount} Credits into {store.Name}'s Capital (now {store.Capital}).");
                break;
            }

            case "stock":
            {
                var split = SplitItemAndPrice(arg);
                if (split is null)
                {
                    session.Send("Usage: stock <item> <price>");
                    return;
                }

                var (itemArg, price) = split.Value;
                var item = FindItem(session, itemArg);
                if (item is null)
                {
                    session.Send($"No item matching '{itemArg}' in your inventory.");
                    return;
                }

                store.Deposit(p, item, price);
                session.Send($"Listed {item.Name} at {store.Name} for {price} Credits.");
                break;
            }

            case "charge":
            {
                if (!int.TryParse(arg.Trim(), out var tachyons) || tachyons < 1)
                {
                    session.Send("Usage: charge <tachyons> - pays down your store's maintenance reserve so it isn't repossessed.");
                    return;
                }

                if (!p.Tachyons.CanAfford(tachyons))
                {
                    session.Send($"You don't have {tachyons} Tachyons.");
                    return;
                }

                store.Charge(p, tachyons);
                session.Send($"Charged {tachyons} Tachyons to {store.Name}'s maintenance reserve (now {store.TachyonReserve}).");
                break;
            }

            case "reprice":
            {
                var split = SplitItemAndPrice(arg);
                if (split is null)
                {
                    session.Send("Usage: reprice <item> <new price>");
                    return;
                }

                var (itemArg, price) = split.Value;
                var listing = FindListing(store, itemArg);
                if (listing is null)
                {
                    session.Send($"No listing matching '{itemArg}' at {store.Name}.");
                    return;
                }

                store.AdjustPrice(p, listing, price);
                session.Send($"{listing.Item.Name} is now {price} Credits.");
                break;
            }
        }
    }

    private static void Who(SharedGame game, Session session)
    {
        var sessions = game.AllSessions();
        session.Send($"{sessions.Count} Traveler(s) online:");
        foreach (var s in sessions.OrderBy(s => s.Player.Name, StringComparer.OrdinalIgnoreCase))
        {
            var you = s.Id == session.Id ? " (you)" : "";
            session.Send($"  {s.Player.Name} the {s.Player.Class} — level {s.Player.Level}, {s.Player.CurrentYear} A.D.{you}");
        }
    }

    private static void Say(SharedGame game, Session session, string message)
    {
        if (message.Length == 0)
        {
            session.Send("Say what?");
            return;
        }

        foreach (var s in game.AllSessions())
        {
            s.Send(s.Id == session.Id ? $"You say: {message}" : $"{session.Player.Name} says: {message}");
        }
    }

    private static void News(SharedGame game, Session session)
    {
        var recent = game.Broadcast.Events;
        var tail = recent.Skip(Math.Max(0, recent.Count - 12)).ToList();
        if (tail.Count == 0)
        {
            session.Send("Nothing has happened yet.");
            return;
        }

        session.Send("Recent broadcasts:");
        foreach (var e in tail)
        {
            session.Send($"  * {e.Message}");
        }
    }

    private static void Help(Session session)
    {
        session.Send("Commands: look [dir] · n/s/e/w · monsters · status · inventory · heal · take [all] · fight [name]");
        session.Send("          wield <item> · convert|con <item> · travel <year|+N|-N> · news · who · say <msg> · wait · quit");
        session.Send("Stores:   stores · shop · buy <item> · sell <item>|all · buy-store · stock <item> <price> · withdraw <item>");
        session.Send("          reprice <item> <price> · deposit <credits> · charge <tachyons> · collect");
        session.Send("Fights auto-resolve; loot drops on the floor — 'take' it. Type 'quit' to disconnect.");
    }

    /// <summary>The store slot (if any) at the player's current position, in their current year.</summary>
    private static StoreSlot? StoreSlotHere(SharedGame game, Session session) =>
        game.World.GetYear(session.Player.CurrentYear).StoreSlots.FirstOrDefault(s => s.Location.Equals(session.Player.Position));

    private static StoreListing? FindListing(Store store, string arg)
    {
        if (arg.Length == 0)
        {
            return null;
        }

        if (int.TryParse(arg, out var index) && index >= 1 && index <= store.Listings.Count)
        {
            return store.Listings[index - 1];
        }

        return store.Listings.FirstOrDefault(l => string.Equals(l.Item.Name, arg, StringComparison.OrdinalIgnoreCase))
            ?? store.Listings.FirstOrDefault(l => l.Item.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Splits "&lt;item&gt; &lt;price&gt;" — the last whitespace token is the price, everything before it is the item name/index.</summary>
    private static (string ItemArg, int Price)? SplitItemAndPrice(string arg)
    {
        var tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !int.TryParse(tokens[^1], out var price) || price < 1)
        {
            return null;
        }

        return (string.Join(' ', tokens[..^1]), price);
    }

    private static Item? FindItem(Session session, string arg, Func<Item, bool>? prefer = null)
    {
        if (arg.Length == 0)
        {
            return null;
        }

        var inv = session.Player.Inventory;
        if (int.TryParse(arg, out var n) && n >= 1 && n <= inv.Count)
        {
            return inv[n - 1];
        }

        var exact = inv.FirstOrDefault(i => string.Equals(i.Name, arg, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var matches = inv.Where(i => i.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
        return (prefer is not null ? matches.FirstOrDefault(prefer) : null) ?? matches.FirstOrDefault();
    }

    private static Direction Opposite(Direction d) => d switch
    {
        Direction.North => Direction.South,
        Direction.South => Direction.North,
        Direction.East => Direction.West,
        Direction.West => Direction.East,
        _ => d,
    };
}
