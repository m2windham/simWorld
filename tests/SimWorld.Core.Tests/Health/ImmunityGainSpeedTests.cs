using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Task #104, part 1: <see cref="StatDefOf.ImmunityGainSpeed"/> now runs through real RimWorld
    /// <see cref="Stats.StatPart_Resting"/>/<see cref="Stats.StatPart_BedStat"/> instead of a pre-baked
    /// 0.7/day approximation. These tests settle the question the task asked from the source: a downed pawn
    /// on open ground does <b>not</b> count as resting (RimWorld's own wiki, <i>Immunity gain speed</i>:
    /// "Not resting (or downed): x100%") — only actually being in a bed does, whether downed or asleep.
    /// </summary>
    public class ImmunityGainSpeedTests : ContentTestBase
    {
        public ImmunityGainSpeedTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Precision = 4;

        private static CoreMap NewMap(int size) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell)
        {
            Pawn p = NewHuman("Test");
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnBed(CoreMap map, IntVec3 cell)
        {
            Thing bed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bed"));
            GenSpawn.Spawn(bed, cell, map);
            return bed;
        }

        [Fact]
        public void Up_and_about_gets_no_bonus()
        {
            CoreMap map = NewMap(6);
            Pawn p = SpawnHuman(map, new IntVec3(2, 0, 2));

            Assert.False(p.Asleep);
            Assert.False(p.Downed);
            Assert.False(p.InBed());
            Assert.Equal(1f, p.ImmunityGainSpeed, Precision);
        }

        [Fact]
        public void Resting_asleep_on_the_ground_with_no_bed_gets_the_resting_bonus()
        {
            CoreMap map = NewMap(6);
            Pawn p = SpawnHuman(map, new IntVec3(2, 0, 2));
            p.Asleep = true;

            Assert.False(p.Downed);
            Assert.False(p.InBed());
            Assert.Equal(1.10f, p.ImmunityGainSpeed, Precision);
        }

        /// <summary>The rule task #104 asked to be settled from the source. RimWorld's own
        /// <c>StatPart_Resting.RestingMultiplier</c> (1.0 decompile) is
        /// <c>pawn.InBed() || (GetPosture() != Standing &amp;&amp; !Downed) || ...</c> — the middle clause
        /// explicitly excludes a downed pawn, so only <c>InBed()</c> can still grant the bonus to one. A raid
        /// casualty left on open ground therefore fights infection at exactly the same speed as someone
        /// standing around doing nothing — not faster for being flat on their back.</summary>
        [Fact]
        public void Downed_on_the_ground_with_no_bed_does_not_count_as_resting()
        {
            CoreMap map = NewMap(6);
            Pawn p = SpawnHuman(map, new IntVec3(2, 0, 2));
            p.health.ForceDowned = true;

            Assert.True(p.Downed);
            Assert.False(p.InBed());
            Assert.Equal(1f, p.ImmunityGainSpeed, Precision);
        }

        [Fact]
        public void In_bed_gets_resting_and_the_beds_own_bonus_together()
        {
            CoreMap map = NewMap(6);
            var bedCell = new IntVec3(1, 0, 1);
            SpawnBed(map, bedCell);
            Pawn p = SpawnHuman(map, bedCell);

            Assert.True(p.InBed());
            Assert.Equal(1.10f * 1.07f, p.ImmunityGainSpeed, Precision);
        }

        /// <summary>Being in bed grants the resting bonus regardless of whether the patient is downed or
        /// merely asleep there — RimWorld's own <c>InBed()</c> clause is downed-independent, unlike the
        /// ground case above.</summary>
        [Fact]
        public void In_bed_gets_the_bonus_whether_downed_or_asleep()
        {
            CoreMap map = NewMap(6);
            var bedCell = new IntVec3(1, 0, 1);
            SpawnBed(map, bedCell);
            Pawn downedInBed = SpawnHuman(map, bedCell);
            downedInBed.health.ForceDowned = true;

            Assert.Equal(1.10f * 1.07f, downedInBed.ImmunityGainSpeed, Precision);
        }

        [Fact]
        public void Bed_beats_ground_rest_beats_up_and_about()
        {
            CoreMap map = NewMap(8);
            var bedCell = new IntVec3(1, 0, 1);
            SpawnBed(map, bedCell);

            Pawn inBed = SpawnHuman(map, bedCell);
            Pawn restingOnGround = SpawnHuman(map, new IntVec3(4, 0, 4));
            restingOnGround.Asleep = true;
            Pawn upAndAbout = SpawnHuman(map, new IntVec3(6, 0, 6));

            Assert.True(inBed.ImmunityGainSpeed > restingOnGround.ImmunityGainSpeed);
            Assert.True(restingOnGround.ImmunityGainSpeed > upAndAbout.ImmunityGainSpeed);
        }
    }
}
