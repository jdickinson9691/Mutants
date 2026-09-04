namespace ChronoTravelers.Core.Traits;

/// <summary>
/// A monster or NPC's inborn behavioral quirk, rolled once at spawn (see
/// <see cref="CreatureTraits.RollForSpawn"/>) — never on the player, and
/// never re-rolled or changed after spawn. Shared by
/// <see cref="Monsters.Monster"/> and <see cref="Characters.Traveler"/>
/// (NPCs only) — every trait rolls for both creature types (see
/// <see cref="CreatureTraits.MonsterPool"/> / <see cref="CreatureTraits.NpcPool"/>,
/// both the same full 8), but the mechanic behind a given trait is
/// translated per creature type where the two don't share a literal
/// mechanic (e.g. Trader on a monster isn't "buys a store" — it's the
/// nearest monster-flavored equivalent, "trades up its gear more
/// eagerly"). The hooks each one pulls live where the mechanic already
/// lives (<c>MonsterController</c> for monster behavior, <c>NpcController</c>
/// and <see cref="Characters.Traveler"/> for NPC behavior) — this type is
/// just the label plus its player-facing name/description, mirroring how
/// <see cref="Characters.PassiveHook"/> separates "what lever" from "who
/// reads it."
/// </summary>
public enum CreatureTraitKind
{
    /// <summary>No trait — the default, and still the common case at a 60% spawn chance.</summary>
    None,

    /// <summary>Quicker to anger and slower to back off.</summary>
    Aggressive,

    /// <summary>Grabs everything it can carry and never willingly parts with it.</summary>
    Hoarder,

    /// <summary>Skittish — the opposite of Aggressive: reluctant to escalate, quick to disengage.</summary>
    Skittish,

    /// <summary>Squeezes extra value out of what it converts/sells — resourceful.</summary>
    Scavenger,

    /// <summary>
    /// Acquisitive and always trading up. On an NPC: leans hard into the
    /// shopkeeper side of the simulation — buys in, stocks up, holds out
    /// for a good shelf. On a monster (which owns no store): trades up its
    /// own gear far more readily, scavenging a much stronger weapon off
    /// the floor than an ordinary monster would risk carrying.
    /// </summary>
    Trader,

    /// <summary>
    /// Restless, always covering more ground. On an NPC: jumps the
    /// timeline far more often, and reaches farther when it does. On a
    /// monster (which doesn't time-travel): drifts through its year's map
    /// far more often and never settles into a rest after moving.
    /// </summary>
    Wanderer,

    /// <summary>
    /// Leads by example. On a monster: rallies every monster of its kind
    /// in the room the moment it's provoked, whether or not its species
    /// normally does. On an NPC (not a spatial pack animal, but still a
    /// natural leader in a fight): hits harder in every grind fight, a
    /// standing attack-power bonus.
    /// </summary>
    PackLeader,

    /// <summary>
    /// Strikes hardest when the opening blow matters most. On a monster:
    /// hits much harder once Hostile, and gets there faster. On an NPC
    /// (no spatial ambush to land): hits much harder specifically against
    /// a still-undamaged target — the opening strike of a grind fight.
    /// </summary>
    Ambusher,
}

/// <summary>
/// The static trait table plus the spawn roll. docs/GDD.md's monster/NPC
/// sections (§7/§7.1) get a short callout for this; the "40% of spawns get
/// a trait, uniformly picked from whichever pool fits the creature type"
/// tuning lives here as <see cref="SpawnChance"/> rather than being
/// scattered across every spawn call site.
/// </summary>
public static class CreatureTraits
{
    /// <summary>Chance a freshly spawned monster or NPC gets a trait at all — the other 60% of spawns are the original, trait-free default.</summary>
    public const double SpawnChance = 0.40;

    /// <summary>
    /// All 8 traits a spawned <see cref="Monsters.Monster"/> can roll. Kept
    /// as its own list (rather than reusing <see cref="NpcPool"/> directly)
    /// so the two creature types' pools can diverge again later without a
    /// call-site change — today they happen to be the same 8 kinds, each
    /// mechanically translated for a monster where needed (see
    /// <see cref="CreatureTraitKind"/>'s per-value doc comments).
    /// </summary>
    public static readonly IReadOnlyList<CreatureTraitKind> MonsterPool =
    [
        CreatureTraitKind.Aggressive,
        CreatureTraitKind.Hoarder,
        CreatureTraitKind.Skittish,
        CreatureTraitKind.Scavenger,
        CreatureTraitKind.Trader,
        CreatureTraitKind.Wanderer,
        CreatureTraitKind.PackLeader,
        CreatureTraitKind.Ambusher,
    ];

    /// <summary>All 8 traits a spawned NPC <see cref="Characters.Traveler"/> can roll — see <see cref="MonsterPool"/>'s doc comment for why this is a separate list holding the same 8 kinds.</summary>
    public static readonly IReadOnlyList<CreatureTraitKind> NpcPool =
    [
        CreatureTraitKind.Aggressive,
        CreatureTraitKind.Hoarder,
        CreatureTraitKind.Skittish,
        CreatureTraitKind.Scavenger,
        CreatureTraitKind.Trader,
        CreatureTraitKind.Wanderer,
        CreatureTraitKind.PackLeader,
        CreatureTraitKind.Ambusher,
    ];

    /// <summary>Player-facing name.</summary>
    public static string Name(this CreatureTraitKind kind) => kind switch
    {
        CreatureTraitKind.Aggressive => "Aggressive",
        CreatureTraitKind.Hoarder => "Hoarder",
        CreatureTraitKind.Skittish => "Skittish",
        CreatureTraitKind.Scavenger => "Scavenger",
        CreatureTraitKind.Trader => "Trader",
        CreatureTraitKind.Wanderer => "Wanderer",
        CreatureTraitKind.PackLeader => "Pack Leader",
        CreatureTraitKind.Ambusher => "Ambusher",
        _ => "",
    };

    /// <summary>Player-facing one-line description — written generically enough to read true for both a monster and an NPC; see <see cref="CreatureTraitKind"/>'s per-value doc comments for the exact mechanic each type gets.</summary>
    public static string Description(this CreatureTraitKind kind) => kind switch
    {
        CreatureTraitKind.Aggressive => "Quicker to anger, slower to back off.",
        CreatureTraitKind.Hoarder => "Grabs everything it can carry and never sells, converts, or trades a thing away.",
        CreatureTraitKind.Skittish => "Reluctant to pick a fight, quick to disengage.",
        CreatureTraitKind.Scavenger => "Squeezes extra value out of everything it converts or sells.",
        CreatureTraitKind.Trader => "Acquisitive and always trading up — runs a busier shop, or carries better gear.",
        CreatureTraitKind.Wanderer => "Restless — covers far more ground than most, whether that's rooms or years.",
        CreatureTraitKind.PackLeader => "Leads by example — rallies its own kind to a fight, or simply hits harder itself.",
        CreatureTraitKind.Ambusher => "Strikes hardest on the opening blow, and is quicker to throw it.",
        _ => "",
    };

    /// <summary>
    /// Rolls a spawn trait from <paramref name="pool"/> — <see cref="SpawnChance"/>
    /// (40%) of the time, a uniformly random pick from the pool; the other
    /// 60%, <see cref="CreatureTraitKind.None"/>. <paramref name="nextDouble"/>
    /// is deliberately just <c>Func&lt;double&gt;</c> rather than a concrete
    /// random type, so both Engine's <c>IRandomSource</c> (NPC/engine call
    /// sites) and a plain <see cref="System.Random"/> (Core's deterministic
    /// per-year world-gen streams, e.g. <see cref="Time.YearPopulation.Seed"/>)
    /// can call this without Core taking a dependency on Engine's random
    /// abstraction. An empty pool always returns
    /// <see cref="CreatureTraitKind.None"/> without consuming a roll.
    /// </summary>
    public static CreatureTraitKind RollForSpawn(IReadOnlyList<CreatureTraitKind> pool, Func<double> nextDouble)
    {
        // The gate check always consumes exactly one roll, empty pool or
        // not — short-circuiting on pool.Count first would skip it, which
        // shifts every subsequent draw on a deterministic random stream
        // whenever the pool happens to be empty.
        if (nextDouble() >= SpawnChance || pool.Count == 0)
        {
            return CreatureTraitKind.None;
        }

        return pool[(int)(nextDouble() * pool.Count)];
    }
}
