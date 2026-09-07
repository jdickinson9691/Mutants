using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Tests.Combat;

/// <summary>docs/GDD.md item #5 — Engineer "Jump Rig" / Doctor "Crash Cart", the two overworld abilities cast outside a fight. See <see cref="OverworldAbilityResolver"/>.</summary>
public class OverworldAbilityResolverTests
{
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    // A straight 5-room east corridor, same fixture shape as LevelMapTests.
    private static LevelMap Corridor()
    {
        var rooms = new Dictionary<Coordinate, Room>();
        for (var e = 0; e <= 4; e++)
        {
            var coordinate = new Coordinate(e, 0);
            var exits = new List<(Direction, string)>();
            if (e > 0)
            {
                exits.Add((Direction.West, "back"));
            }

            if (e < 4)
            {
                exits.Add((Direction.East, "onward"));
            }

            rooms[coordinate] = Room.Create($"Room {e}", exits.ToArray());
        }

        return new LevelMap("Corridor", Coordinate.Origin, rooms);
    }

    private static AbilityData JumpRig() => new()
    {
        Class = "Engineer", Tier = 2, Level = 5, Name = "Jump Rig",
        TachyonCost = 12, Effect = "ShortTeleport", Magnitude = 3,
    };

    private static AbilityData CrashCart() => new()
    {
        Class = "Doctor", Tier = 5, Level = 25, Name = "Crash Cart",
        TachyonCost = 25, Effect = "ReviveAlly", Magnitude = 0.5,
    };

    // LevelUp() bypasses the soft level cap (SoftLevelCapForYear(2000) is
    // only 10 for a fresh Traveler) — same pattern every other test fixture
    // in this suite uses to build a high-level Traveler without also
    // faking a deep FurthestYearReached.
    private static Traveler LeveledEngineer(int level = 10)
    {
        var t = new Traveler("Wren", CharacterClass.Engineer);
        for (var i = 1; i < level; i++)
        {
            t.LevelUp();
        }

        return t;
    }

    private static Traveler LeveledDoctor(int level = 30)
    {
        var t = new Traveler("Ashen", CharacterClass.Doctor);
        for (var i = 1; i < level; i++)
        {
            t.LevelUp();
        }

        return t;
    }

    [Fact]
    public void TryShortTeleport_MovesCasterToARoomWithinRange_AndSpendsTachyons()
    {
        var engineer = LeveledEngineer();
        var tachyonsBefore = engineer.Tachyons.Current;
        var map = Corridor();

        var result = OverworldAbilityResolver.TryShortTeleport(engineer, map, JumpRig(), NeutralRandom());

        Assert.True(result.Success);
        Assert.NotEqual(Coordinate.Origin, engineer.Position);
        Assert.Contains(engineer.Position, map.RoomsWithinHops(Coordinate.Origin, maxHops: 3));
        Assert.True(engineer.Tachyons.Current < tachyonsBefore);
    }

    [Fact]
    public void TryShortTeleport_WrongClass_Fails()
    {
        var doctor = LeveledDoctor();
        var result = OverworldAbilityResolver.TryShortTeleport(doctor, Corridor(), JumpRig(), NeutralRandom());

        Assert.False(result.Success);
    }

    [Fact]
    public void TryShortTeleport_BelowUnlockLevel_Fails()
    {
        var engineer = new Traveler("Wren", CharacterClass.Engineer); // level 1, Jump Rig unlocks at 5
        var result = OverworldAbilityResolver.TryShortTeleport(engineer, Corridor(), JumpRig(), NeutralRandom());

        Assert.False(result.Success);
    }

    [Fact]
    public void TryShortTeleport_CombatEffectAbility_IsRefusedAsOverworld()
    {
        var engineer = LeveledEngineer();
        var combatAbility = new AbilityData { Class = "Engineer", Level = 1, Name = "Dampener", Effect = "Damage", Magnitude = 1.5 };

        var result = OverworldAbilityResolver.TryShortTeleport(engineer, Corridor(), combatAbility, NeutralRandom());

        Assert.False(result.Success);
    }

    [Fact]
    public void TryReviveAlly_HealsTheMostWoundedNpcInRoom()
    {
        var doctor = LeveledDoctor();
        var tachyonsBefore = doctor.Tachyons.Current;

        var badlyWounded = new Traveler("Fang", CharacterClass.Soldier);
        badlyWounded.Health.Damage(badlyWounded.Health.Max - 1); // 1 HP left, well under 25%

        var fine = new Traveler("Static", CharacterClass.Spy); // full HP, not a valid target

        var result = OverworldAbilityResolver.TryReviveAlly(doctor, [fine, badlyWounded], CrashCart(), NeutralRandom());

        Assert.True(result.Success);
        Assert.Same(badlyWounded, result.RevivedAlly);
        Assert.True(badlyWounded.Health.Current > 1);
        Assert.Equal((int)Math.Round(badlyWounded.Health.Max * 0.5), badlyWounded.Health.Current);
        Assert.True(doctor.Tachyons.Current < tachyonsBefore);
    }

    [Fact]
    public void TryReviveAlly_NoWoundedNpcInRoom_FailsWithoutSpendingTachyons()
    {
        var doctor = LeveledDoctor();
        var tachyonsBefore = doctor.Tachyons.Current;
        var fine = new Traveler("Static", CharacterClass.Spy);

        var result = OverworldAbilityResolver.TryReviveAlly(doctor, [fine], CrashCart(), NeutralRandom());

        Assert.False(result.Success);
        Assert.Null(result.RevivedAlly);
        Assert.Equal(tachyonsBefore, doctor.Tachyons.Current);
    }

    [Fact]
    public void TryReviveAlly_DeadNpcInRoom_IsNotATarget()
    {
        // Confirms the documented scope call: a literally-dead (0 HP) NPC
        // is not a valid Crash Cart target — see the class doc comment for
        // why ("downed" reads as badly wounded, not dead).
        var doctor = LeveledDoctor();
        var dead = new Traveler("Fallen", CharacterClass.Soldier);
        dead.Health.Damage(dead.Health.Max);
        Assert.True(dead.Health.IsDead);

        var result = OverworldAbilityResolver.TryReviveAlly(doctor, [dead], CrashCart(), NeutralRandom());

        Assert.False(result.Success);
    }
}
