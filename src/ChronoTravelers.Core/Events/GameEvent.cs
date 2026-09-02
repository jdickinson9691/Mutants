using System.Text.RegularExpressions;

namespace ChronoTravelers.Core.Events;

/// <summary>What a <see cref="GameEvent"/> reports — lets the UI treat the noisy background chatter (NPC time-hops) differently from events that matter to the player right now (kills, level-ups, ambushes).</summary>
public enum GameEventKind
{
    Slain,
    LevelReached,
    TimeTraveled,
    Ambushed,
    StoreRepossessed,
}

/// <summary>
/// One entry on the shared "telepathic broadcast" / kill-feed channel —
/// docs/GDD.md §7: NPCs and the player post to the same channel ("An
/// Ashfall Echo was slain by a Dune Stalker," "Fang reached level 12,"
/// "Static time traveled to 3200 A.D.") so the world feels alive without a
/// live human population. <see cref="Year"/> is the timeline year the
/// event happened in (null if it isn't tied to one), so the console can
/// show only what's happening in the player's own year inline and leave
/// the rest for <c>news</c>.
///
/// Names are cleaned for the feed: a monster (a common noun — "Ashfall
/// Echo") gets an "a"/"an" article, capitalised at the start of the line;
/// a named agent (an NPC Traveler, the player) is a proper noun and stays
/// bare; the auto-generated NPC instance suffix (" 2", " 3") is dropped.
/// </summary>
public sealed record GameEvent(string Message, GameEventKind Kind = GameEventKind.Slain, int? Year = null)
{
    private static readonly Regex NpcSuffix = new(@"\s+\d+$", RegexOptions.Compiled);

    /// <param name="victimIsCreature">True if the victim's name is a common noun (a monster) that should read "a/an &lt;name&gt;"; false for a proper noun (an NPC / the player).</param>
    /// <param name="killerIsCreature">As <paramref name="victimIsCreature"/>, for the killer.</param>
    public static GameEvent Slain(
        string victimName, string killerName, int? year = null,
        bool victimIsCreature = false, bool killerIsCreature = false) =>
        new($"{Ref(victimName, victimIsCreature, sentenceStart: true)} was slain by {Ref(killerName, killerIsCreature, sentenceStart: false)}.",
            GameEventKind.Slain, year);

    public static GameEvent LevelReached(string name, int level, int? year = null) =>
        new($"{Plain(name)} reached level {level}!", GameEventKind.LevelReached, year);

    public static GameEvent TimeTraveled(string name, int year) =>
        new($"{Plain(name)} time traveled to {year} A.D.", GameEventKind.TimeTraveled, year);

    /// <summary>Only a monster ever ambushes, so the attacker always gets an article.</summary>
    public static GameEvent Ambushed(string monsterName, string victimName, int damage, int? year = null) =>
        new($"{Ref(monsterName, isCreature: true, sentenceStart: true)} ambushes {Plain(victimName)} for {damage}.",
            GameEventKind.Ambushed, year);

    /// <summary>docs/GDD.md §6.2: unpaid Tachyon maintenance eventually "causes stores, and their inventories, to become for sale" — published when that threshold is crossed and a slot is reclaimed.</summary>
    public static GameEvent StoreRepossessed(string storeName, string ownerName, int year) =>
        new($"{storeName} fell behind on maintenance and was repossessed from {Plain(ownerName)} — it's for sale again.",
            GameEventKind.StoreRepossessed, year);

    /// <summary>Formats a participant for the feed — article for a creature, bare for a proper noun, instance suffix always stripped.</summary>
    private static string Ref(string name, bool isCreature, bool sentenceStart)
    {
        var clean = Plain(name);
        if (!isCreature || StartsWithDeterminer(clean))
        {
            return clean;
        }

        var vowel = clean.Length > 0 && "AEIOUaeiou".IndexOf(clean[0]) >= 0;
        var article = sentenceStart
            ? (vowel ? "An " : "A ")
            : (vowel ? "an " : "a ");
        return article + clean;
    }

    /// <summary>
    /// Drops the auto-generated NPC instance suffix (" 2", " 3", …) — the
    /// feed doesn't need to enumerate. Only strips it off a single-word
    /// base name (an NPC like "Ashen 2"), never off a phrase that just ends
    /// in a number ("The Warden of 3200").
    /// </summary>
    private static string Plain(string name)
    {
        var match = NpcSuffix.Match(name);
        if (!match.Success)
        {
            return name;
        }

        var withoutSuffix = name[..match.Index];
        return withoutSuffix.Contains(' ') ? name : withoutSuffix;
    }

    private static bool StartsWithDeterminer(string name) =>
        name.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("a ", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("an ", StringComparison.OrdinalIgnoreCase);
}
