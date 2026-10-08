using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// What a fire on the ground does to a pawn standing in it (task #111; RimWorld's <c>Fire.DoComplexCalcs</c>,
    /// 1.0 decompile <c>RimWorld/Fire.cs</c>). Before this, every pass of a fire burned every flammable thing
    /// in its cell, pawns included, whatever its size — so the first spark in a field set the farmer standing
    /// in it alight. RimWorld neither burns nor ignites a pawn until the fire is past
    /// <see cref="Fire.MinSizeForIgniteMovables"/>, and then sets them alight at a fifth of its size.
    /// <para/>
    /// The pawn in each test is held still — taken off the tick lists — because this is about what the fire
    /// does to whoever is in it, not what they do about it (that is <c>BurningResponseTests</c>).
    /// </summary>
    public class FirePawnIgnitionTests : ContentTestBase
    {
        public FirePawnIgnitionTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(12, 12, TerrainDefOf.Soil);

        private static Pawn StillPawnAt(CoreMap map, IntVec3 cell)
        {
            Pawn pawn = NewHuman("Bystander");
            GenSpawn.Spawn(pawn, cell, map);
            Find.TickManager.DeRegisterAllTickabilityFor(pawn);
            return pawn;
        }

        /// <summary>Put straight onto the cell: <see cref="FireUtility.TryStartFireIn"/> would (rightly)
        /// refuse, since a pawn alone is not fuel for a spark.</summary>
        private static Fire IgniteUnder(CoreMap map, IntVec3 cell, float size)
        {
            var fire = (Fire)ThingMaker.MakeThing(FireThingDefOf.Fire);
            fire.fireSize = size;
            GenSpawn.Spawn(fire, cell, map);
            return fire;
        }

        private static List<Hediff> Burns(Pawn pawn)
        {
            HediffDef burn = DefDatabase<HediffDef>.GetNamed("Burn");
            return pawn.health.hediffSet.hediffs.Where(h => h.def == burn).ToList();
        }

        private static void RunMapTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick();
        }

        [Fact]
        public void A_small_fire_neither_burns_nor_ignites_a_pawn_standing_in_it_but_keeps_burning_on_them()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            Pawn pawn = StillPawnAt(map, cell);
            Fire fire = IgniteUnder(map, cell, Fire.MinFireSize);

            // A spark on the same cell could not have started this: a pawn is not fuel for one.
            Assert.Equal(0f, FireUtility.ChanceToStartFireIn(cell, map));

            // Three passes from the smallest size: growth per pass is fixed by the fuel, so every one of these
            // passes starts below the threshold — asserted below rather than assumed.
            float sizeAtLastPass = fire.fireSize;
            for (int pass = 0; pass < 3; pass++)
            {
                sizeAtLastPass = fire.fireSize;
                RunMapTicks(Fire.ComplexCalcsInterval);
            }
            Assert.True(sizeAtLastPass < Fire.MinSizeForIgniteMovables, "test setup: the last pass must still have been a small fire");

            Assert.Empty(Burns(pawn));
            Assert.False(pawn.IsBurning(), "A fire under the ignition size set the pawn standing in it alight.");

            // RimWorld counts the pawn as the fire's fuel all the same, so it neither dies for want of it nor
            // stops growing toward the size at which it does catch them.
            Assert.False(fire.Destroyed, "A small fire with a pawn standing in it went out for want of fuel.");
            Assert.True(fire.fireSize > Fire.MinFireSize);
        }

        [Fact]
        public void A_big_fire_burns_a_pawn_standing_in_it_and_sets_them_alight_at_a_fifth_of_its_size()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            Pawn pawn = StillPawnAt(map, cell);
            const float size = 1f;
            Fire fire = IgniteUnder(map, cell, size);

            int ticks = 0;
            while (!pawn.IsBurning() && ticks < Fire.ComplexCalcsInterval)
            {
                RunMapTicks(1);
                ticks++;
            }

            Assert.True(pawn.IsBurning(), "A fire past the ignition size did not set the pawn standing in it alight.");
            Fire riding = pawn.GetAttachedFire()!;
            Assert.NotSame(fire, riding);
            Assert.Equal(size * Fire.IgniteMovablesSizeFactor, riding.fireSize, 3);

            // And the same pass burned them: the pawn was the only thing there, and past the threshold a pawn
            // is fair game for the one thing a pass burns.
            Assert.NotEmpty(Burns(pawn));
        }
    }
}
