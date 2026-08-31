namespace ChronTravelers.Core.Classes;

/// <summary>
/// The five playable/NPC classes — the crew roles of a Chron project
/// research station. Their mechanical shapes descend from the five classes
/// of the door game this is inspired by (Warrior / Thief / Priest / Wizard
/// / Mage — see research/ORIGINAL_MUTANTS_RESEARCH.md); the names, lore,
/// and ability trees are original design (docs/GDD.md §4).
/// </summary>
public enum CharacterClass
{
    /// <summary>Frontline security — was the Warrior. Strength primary, best HP, cheapest Ion drain.</summary>
    Soldier,

    /// <summary>Infiltration and recon — was the Thief. Agility primary, fast and evasive.</summary>
    Spy,

    /// <summary>Trauma medicine and triage — was the Priest. Resolve primary, healing and support.</summary>
    Doctor,

    /// <summary>Theory and the tunnel itself — was the Mage. Intellect primary, glass-cannon utility.</summary>
    Scientist,

    /// <summary>Systems, power, and hardware — was the Wizard. Intellect primary, control and disruption.</summary>
    Engineer,
}
