using ChronoTravelers.Core.Characters;
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

            case "repair":
                Repair(game, session, arg);
                break;

            case "abilities" or "spells":
                Abilities(game, session);
                break;

            case "cast":
                Cast(game, session, arg);
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
        or "stores" or "abilities" or "spells"; // "shop"/buying/selling/store management/cast are doing-something, like the console (Program.cs's IsIdleCommand)

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
                if (!p.AddToInventory(it))
                {
                    pop.AddGroundLoot(p.Position, it);
                    session.Send($"Your pack is full ({Traveler.MaxInventorySize} items) — {it.Name} stays on the ground.");
                    break;
                }

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

        if (!p.AddToInventory(picked))
        {
            pop.AddGroundLoot(p.Position, picked);
            session.Send($"Your pack is full ({Traveler.MaxInventorySize} items) — {picked.Name} stays on the ground.");
            return;
        }

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
            // docs/GDD.md §11's "player-vs-NPC-Traveler combat" — no
            // monster here, but a living NPC sharing the tile is a valid
            // 'fight' target too (an NPC is a full Traveler). Checked only
            // as the fallback so a room with both a monster and an NPC
            // still defaults 'fight' (no argument) to the monster.
            var npcsHere = game.Npcs.Where(n => !n.Health.IsDead && n.CurrentYear == p.CurrentYear && n.Position.Equals(p.Position)).ToList();
            if (npcsHere.Count > 0)
            {
                var npcTarget = arg.Length > 0
                    ? npcsHere.FirstOrDefault(n => n.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)) ?? npcsHere[0]
                    : npcsHere[0];
                FightNpcTraveler(game, session, npcTarget);
                return;
            }

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
            session.Send($"You defeated the {target.Name}! +{result.XpAwarded} XP, +{result.CreditsAwarded} Credits.");
            game.Broadcast.Publish(GameEvent.Slain(target.Name, p.Name, year, victimIsCreature: true));

            // Loot never stays in the pack — CombatResolver.Fight auto-added
            // it, so move it (plus the monster's scavenged items) to the floor.
            var toGround = result.ItemsDropped.ToList();
            foreach (var it in toGround)
            {
                if (p.Inventory.Contains(it))
                {
                    p.RemoveFromInventory(it);
                }
            }

            toGround.AddRange(target.Inventory);
            foreach (var it in toGround)
            {
                pop.AddGroundLoot(p.Position, it);
            }

            if (isWarden)
            {
                p.RecordWardenDefeat(year);
                // target.Name rather than a hardcoded "Warden of {year}" —
                // the year-5000 capstone (docs/ENDGAME_STRATEGY.md
                // recommendation 4) is named "The Convergence," not "The
                // Warden of 5000."
                session.Send(toGround.Count > 0
                    ? $"{target.Name} falls — its haul lies at your feet. (take)"
                    : $"{target.Name} is broken.");
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

    /// <summary>
    /// docs/GDD.md §11's "player-vs-NPC-Traveler combat" — auto-resolves
    /// via <see cref="CombatResolver.FightTraveler"/>, the same auto-
    /// resolving shape every fight already takes on this server (see
    /// <see cref="Fight"/>). On a win, the NPC's whole inventory (equipped
    /// gear included) hits the floor — see FightTraveler's doc comment for
    /// why. On a loss, death is handled by <see cref="SharedGame.Tick"/>
    /// exactly like a monster kill (respawn upstream); the losing NPC
    /// itself needs no special handling either — it just sits at 0 HP
    /// until the next tick's WorldSimulation.RespawnDeadNpcs replaces it.
    /// </summary>
    private static void FightNpcTraveler(SharedGame game, Session session, Traveler npcTarget)
    {
        var p = session.Player;
        var year = p.CurrentYear;
        var pop = game.World.GetYear(year).Population;
        var levelBefore = p.Level;

        session.Send($"You square off against {npcTarget.Name} (level {npcTarget.Level})!");
        var result = CombatResolver.FightTraveler(p, npcTarget, Rng);

        foreach (var logLine in result.Log)
        {
            session.Send(logLine);
        }

        if (result.AttackerWon)
        {
            session.Send($"You defeated {npcTarget.Name}! +{result.XpAwarded} XP, +{result.CreditsAwarded} Credits.");
            game.Broadcast.Publish(GameEvent.Slain(npcTarget.Name, p.Name, year));

            foreach (var it in result.ItemsDropped)
            {
                pop.AddGroundLoot(p.Position, it);
            }

            if (result.ItemsDropped.Count > 0)
            {
                session.Send($"{npcTarget.Name} drops {string.Join(", ", result.ItemsDropped.Select(i => i.Name))} on the ground. (take)");
            }

            if (p.Level > levelBefore)
            {
                game.Broadcast.Publish(GameEvent.LevelReached(p.Name, p.Level, year));
            }
        }
        else
        {
            session.Send($"You were beaten down by {npcTarget.Name}...");
            game.Broadcast.Publish(GameEvent.Slain(p.Name, npcTarget.Name, year));
            // Death is handled by SharedGame.Tick (respawn upstream).
        }
    }

    /// <summary>Lists the player's class abilities and whether each is usable — combat-only ("fight" auto-resolves here, so nothing ever actually casts one mid-fight on this server, unlike the console's interactive CombatSession), overworld (cast any time via <see cref="Cast"/>), always-on once unlocked (Spy's Black Market Contacts), or not yet unlocked.</summary>
    private static void Abilities(SharedGame game, Session session)
    {
        var p = session.Player;
        var classAbilities = game.Abilities
            .Where(a => string.Equals(a.Class, p.Class.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Tier)
            .ToList();

        if (classAbilities.Count == 0)
        {
            session.Send("No ability data loaded.");
            return;
        }

        session.Send("Abilities:");
        foreach (var ability in classAbilities)
        {
            var unlocked = p.Level >= ability.Level;
            var isOverworld = ability.Effect is "ShortTeleport" or "ReviveAlly";
            var isAlwaysOn = string.Equals(ability.Name, "Black Market Contacts", StringComparison.OrdinalIgnoreCase);
            var hasEffect = isAlwaysOn || !string.Equals(ability.Effect, "None", StringComparison.OrdinalIgnoreCase);

            var status = !unlocked ? "locked"
                : !hasEffect ? "no effect yet"
                : isAlwaysOn ? "always on"
                : isOverworld ? "ready — cast <name> any time"
                : "combat-only (this server auto-resolves fights, so it never actually casts)";

            session.Send($"  Lv{ability.Level} {ability.Name} ({ability.TachyonCost} Tachyons) — {ability.Description} [{status}]");
        }
    }

    /// <summary>
    /// docs/GDD.md item #5's gap-analysis: Doctor "Crash Cart" and Engineer
    /// "Jump Rig" are overworld-only abilities (heal a wounded NPC in the
    /// room / short teleport) — "fight" already auto-resolves on this
    /// server (no round-by-round casting mid-fight, see <see cref="Fight"/>'s
    /// doc comment), so this is the only place any ability is ever cast
    /// here. Combat-effect abilities (Damage, Heal, etc.) are refused —
    /// see <see cref="Engine.Combat.OverworldAbilityResolver"/>'s messages.
    /// </summary>
    private static void Cast(SharedGame game, Session session, string arg)
    {
        var p = session.Player;
        if (arg.Length == 0)
        {
            session.Send("Cast what? Try 'abilities' to see your list.");
            return;
        }

        var ability = game.Abilities
            .Where(a => string.Equals(a.Class, p.Class.ToString(), StringComparison.OrdinalIgnoreCase) && a.Level <= p.Level)
            .FirstOrDefault(a => string.Equals(a.Name, arg, StringComparison.OrdinalIgnoreCase));

        if (ability is null)
        {
            session.Send($"No ability named '{arg}' available. Try 'abilities' to see your list.");
            return;
        }

        var year = p.CurrentYear;
        var result = string.Equals(ability.Effect, "ShortTeleport", StringComparison.OrdinalIgnoreCase)
            ? OverworldAbilityResolver.TryShortTeleport(p, game.World.GetYear(year).Map, ability, Rng)
            : string.Equals(ability.Effect, "ReviveAlly", StringComparison.OrdinalIgnoreCase)
                ? OverworldAbilityResolver.TryReviveAlly(p, game.Npcs.Where(n => !n.Health.IsDead && n.CurrentYear == year && n.Position.Equals(p.Position)).ToList(), ability, Rng)
                : new OverworldAbilityResolver.Result(false, $"{ability.Name} only works in a fight, and fights auto-resolve on this server — it never actually casts.");

        session.Send(result.Message);
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
        // Destructive — when the name matches more than one item (e.g. more
        // than one Time Shard, picked up in different years), pick the
        // weakest copy rather than an arbitrary one, so a duplicate never
        // costs the player their best gear (see ItemSelection.Weakest).
        var item = FindItem(session, arg, weakestFirst: true);
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
                : slot.Store!.IsGovernmentRun ? "occupied" : $"occupied ({slot.Store.CreditReserve} Credit reserve)";
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

        session.Send($"{store.Name} — Capital: {store.Capital} Credits — {store.Listings.Count}/{Store.MaxListings} items");
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

        if (!store.SellToTraveler(p, listing))
        {
            session.Send($"Your pack is full ({Traveler.MaxInventorySize} items) — sell or convert something first.");
            return;
        }

        session.Send($"Bought {listing.Item.Name} for {listing.AskingPrice} Credits.");
    }

    /// <summary>Sells an item (or converts junk) to/for the player — console parity (Program.cs's <c>HandleSellToStore</c>).</summary>
    private static void SellToStore(SharedGame game, Session session, string arg)
    {
        var p = session.Player;

        // 'sell all' / 'sell junk' — converts every Junk item for Tachyons.
        // Junk is convert-only now (no store ever buys it), so — unlike a
        // named-item sale below — this needs no store and works anywhere.
        if (arg.Trim() is "all" or "junk" or "*")
        {
            var junk = p.Inventory.Where(i => i.Type == ItemType.Junk).ToList();
            if (junk.Count == 0)
            {
                session.Send("No junk to convert. Name an item to sell that instead.");
                return;
            }

            var total = 0;
            foreach (var j in junk)
            {
                total += p.Convert(j);
            }

            session.Send($"Converted {junk.Count} junk item(s) for {total} Tachyons.");
            return;
        }

        var item = FindItem(session, arg);
        if (item is null)
        {
            session.Send(arg.Length == 0
                ? "Sell what? Type 'inventory' to see what you're carrying, or 'sell all' to convert junk."
                : $"No item matching '{arg}' in your inventory.");
            return;
        }

        if (item.Type == ItemType.Junk)
        {
            session.Send($"{item.Name} is junk — it can only be converted for Tachyons, not sold. Try 'convert {item.Name}' or 'sell all'.");
            return;
        }

        var slot = StoreSlotHere(game, session);
        if (slot?.Store is not { } store)
        {
            session.Send("You need to be at a store to sell. Try 'convert' to destroy an item for Tachyons instead, or 'stores' to find one.");
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

    /// <summary>Repairs a worn Weapon/Armor back to full Durability at the store in the player's current room — docs/GDD.md §6.3's "repair costs" Credit sink. Console parity (Program.cs's <c>HandleRepair</c>).</summary>
    private static void Repair(SharedGame game, Session session, string arg)
    {
        var slot = StoreSlotHere(game, session);
        if (slot?.Store is not { } store)
        {
            session.Send("You need to be at a store to repair gear. Try 'stores' to find one.");
            return;
        }

        var item = FindItem(session, arg, static i => i.HasDurability);
        if (item is null)
        {
            session.Send(arg.Length == 0
                ? "Repair what? Name a weapon or armor piece."
                : $"No item matching '{arg}' in your inventory.");
            return;
        }

        if (!item.HasDurability)
        {
            session.Send($"{item.Name} doesn't wear down — nothing to repair.");
            return;
        }

        if (item.Durability >= item.MaxDurability)
        {
            session.Send($"{item.Name} is already in full repair.");
            return;
        }

        var p = session.Player;
        var cost = EconomyPricing.RepairCost(item);
        if (p.Credits < cost)
        {
            session.Send($"Repairing {item.Name} costs {cost} Credits; you have {p.Credits}.");
            return;
        }

        var paid = store.Repair(p, item);
        session.Send($"Repaired {item.Name} for {paid} Credits. ({item.Durability}/{item.MaxDurability} durability)");
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
        session.Send($"You now own a store here: {slot.Store!.Name}! Use stock/withdraw/reprice to manage listings, deposit/charge/collect to move Credits in and out. It'll still be yours next session — but only if you keep charge-ing its Credit maintenance.");
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

                if (!store.Withdraw(p, listing))
                {
                    session.Send($"Your pack is full ({Traveler.MaxInventorySize} items) — {listing.Item.Name} stays listed at {store.Name}.");
                    return;
                }

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

                if (!store.Deposit(p, item, price))
                {
                    session.Send($"{store.Name} is full ({Store.MaxListings} items) — withdraw or reprice something first.");
                    return;
                }

                session.Send($"Listed {item.Name} at {store.Name} for {price} Credits.");
                break;
            }

            case "charge":
            {
                if (!int.TryParse(arg.Trim(), out var credits) || credits < 1)
                {
                    session.Send("Usage: charge <credits> - pays down your store's maintenance reserve so it isn't repossessed.");
                    return;
                }

                if (p.Credits < credits)
                {
                    session.Send($"You don't have {credits} Credits.");
                    return;
                }

                store.Charge(p, credits);
                session.Send($"Charged {credits} Credits to {store.Name}'s maintenance reserve (now {store.CreditReserve}).");
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
        session.Send("          abilities · cast <name> (Jump Rig/Crash Cart work any time; other abilities are combat-only and this server auto-resolves fights)");
        session.Send("Stores:   stores · shop · buy <item> · sell <item>|all · repair <item> · buy-store · stock <item> <price> · withdraw <item>");
        session.Send("          reprice <item> <price> · deposit <credits> · charge <credits> · collect");
        session.Send("Fights auto-resolve; loot drops on the floor — 'take' it. 'fight [name]' also targets a living NPC sharing your tile (PvP). Type 'quit' to disconnect.");
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

    private static Item? FindItem(Session session, string arg, Func<Item, bool>? prefer = null, bool weakestFirst = false)
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

        // `weakestFirst` (destructive commands like `convert`): a player can
        // carry more than one item sharing a name (e.g. a Time Shard per
        // visited year), so among the matches pick the weakest via
        // ItemSelection.Weakest rather than an arbitrary one that could be
        // the player's best copy.
        var exactMatches = inv.Where(i => string.Equals(i.Name, arg, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exactMatches.Count > 0)
        {
            return weakestFirst ? ItemSelection.Weakest(exactMatches) : exactMatches[0];
        }

        var matches = inv.Where(i => i.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
        if (prefer is not null)
        {
            var preferredMatches = matches.Where(prefer).ToList();
            if (preferredMatches.Count > 0)
            {
                return weakestFirst ? ItemSelection.Weakest(preferredMatches) : preferredMatches[0];
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        return weakestFirst ? ItemSelection.Weakest(matches) : matches[0];
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
