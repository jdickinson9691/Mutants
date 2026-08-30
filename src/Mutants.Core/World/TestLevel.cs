namespace Mutants.Core.World;

/// <summary>
/// A small hardcoded 3x3 level, per docs/TECH_STACK.md milestone 2
/// ("Grid/movement + a single hardcoded level, playable via console").
/// This is engine-sandbox content only — NOT launch content. Real level
/// themes/room text/monster-loot-store population are future Content Agent
/// work per docs/CONTENT_PLAN.md, loaded from data files once
/// Mutants.Content exists. Serves as time-travel level 1 in
/// Levels.TestWorld.
/// </summary>
public static class TestLevel
{
    public static LevelMap Build()
    {
        // Flavor lines loosely match the style of docs/GDD.md §3.1's
        // examples ("You're in a maintenance shop.", "You see rubble
        // everywhere.", "You feel a cold breeze.").
        var descriptions = new Dictionary<Coordinate, string>
        {
            [new Coordinate(0, 0)] = "You are standing at the crossroads of a ruined city block.",
            [new Coordinate(1, 0)] = "You're in a maintenance shop, shelves stripped bare.",
            [new Coordinate(-1, 0)] = "You see rubble everywhere; the street has collapsed into it.",
            [new Coordinate(0, 1)] = "You feel a cold breeze cutting between the buildings.",
            [new Coordinate(0, -1)] = "A flickering streetlamp buzzes over cracked asphalt.",
            [new Coordinate(1, 1)] = "Broken glass crunches underfoot outside a gutted storefront.",
            [new Coordinate(1, -1)] = "You smell smoke drifting from a fire burning somewhere nearby.",
            [new Coordinate(-1, 1)] = "Twisted rebar juts from a collapsed parking structure.",
            [new Coordinate(-1, -1)] = "An old subway entrance yawns open into darkness.",
        };

        return GridLevelBuilder.Build("Level 1 — Ruined City", Coordinate.Origin, descriptions);
    }
}
