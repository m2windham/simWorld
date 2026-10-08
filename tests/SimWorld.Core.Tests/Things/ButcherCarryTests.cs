using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// The body a butcher fetches to the bench is carried, not parked in a driver field
    /// (<see cref="JobDriver_ButcherCorpse"/> through <see cref="Pawn_CarryTracker"/>). An interruption was
    /// already survived by the field (it respawned the body at the pawn's feet); a save taken mid-carry was
    /// not, because nothing in a <see cref="JobDriver"/> is Scribed — the body was off the map and in no saved
    /// object. These pin both: interrupted on the way or at the bench, the same body is back on the ground; and
    /// saved mid-carry, the same body is in the loaded butcher's hands and still gets butchered.
    /// </summary>
    public class ButcherCarryTests : ContentTestBase
    {
        public ButcherCarryTests(CoreContentFixture content) : base(content)
        {
            // Generation is lazy by design (see CorpseDefGenerator's own doc); see CorpseTests' constructor.
            CorpseDefGenerator.EnsureGenerated();
        }

        public enum Interruption
        {
            Downed,
            OrderedAway,
            JobEnded,
        }

        private static readonly IntVec3 BenchCell = new IntVec3(2, 0, 2);
        private static readonly IntVec3 CarcassCell = new IntVec3(9, 0, 9);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static (CoreMap map, Pawn butcher, Pawn husky, CorpseThing corpse, Thing bench) ButcherSite()
        {
            CoreMap map = new CoreMap(12, 12, TerrainDefOf.Soil);
            Thing bench = GenSpawn.Spawn(ThingMaker.MakeThing(Def("TableButcher")), BenchCell, map);
            Pawn butcher = NewHuman("Butcher");
            GenSpawn.Spawn(butcher, new IntVec3(1, 0, 1), map);
            var husky = new Pawn(Husky, "Rex");
            GenSpawn.Spawn(husky, CarcassCell, map);
            husky.health.Kill(null, null);
            Assert.NotNull(husky.corpse);
            return (map, butcher, husky, husky.corpse!, bench);
        }

        private static int MeatOnMap(CoreMap map) =>
            map.listerThings.ThingsOfDef(Def("Meat_Generic")).Sum(t => t.stackCount);

        /// <summary>Ticks until the butcher holds the body: it has left the map and the butchery job is
        /// running. With <paramref name="atBench"/>, until the butcher is also standing at the bench, so the
        /// body is in hand during the work itself.</summary>
        private static void RunUntilBodyIsInHand(Pawn butcher, CorpseThing corpse, IntVec3 benchCell, bool atBench, int maxTicks = 3000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                bool inHand = !corpse.Spawned && !corpse.Destroyed && butcher.jobs.curJob?.def == CorpseWorkDefOf.ButcherCorpse;
                bool there = (butcher.Position - benchCell).LengthHorizontalSquared <= 2;
                if (inHand && (!atBench || there)) return;
                RunTicks(1, butcher);
            }
            Assert.Fail("The butcher never reached the stage this test needs within " + maxTicks + " ticks.");
        }

        private static void Interrupt(Pawn butcher, Interruption how)
        {
            switch (how)
            {
                case Interruption.Downed:
                    butcher.health.ForceDowned = true;
                    break;
                case Interruption.OrderedAway:
                    butcher.jobs.StartJob(new Job(DutyJobDefOf.Goto, new IntVec3(0, 0, 11)) { playerForced = true }, JobCondition.InterruptForced);
                    break;
                case Interruption.JobEnded:
                    butcher.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    break;
            }
        }

        // ---- interruption ----

        /// <summary>Interrupted on the way to the bench, or at the bench with the work under way: the very same
        /// body is lying on the ground at the butcher's feet, with the very same animal in it, and nothing has
        /// been butchered.</summary>
        [Theory]
        [InlineData(Interruption.Downed, false)]
        [InlineData(Interruption.Downed, true)]
        [InlineData(Interruption.OrderedAway, false)]
        [InlineData(Interruption.OrderedAway, true)]
        [InlineData(Interruption.JobEnded, false)]
        [InlineData(Interruption.JobEnded, true)]
        public void An_interrupted_butchery_puts_the_same_body_down(Interruption how, bool atBench)
        {
            (CoreMap map, Pawn butcher, Pawn husky, CorpseThing corpse, Thing bench) = ButcherSite();
            RunUntilBodyIsInHand(butcher, corpse, bench.Position, atBench);
            IntVec3 where = butcher.Position;

            Interrupt(butcher, how);

            Assert.Null(butcher.carryTracker.CarriedThing);
            Assert.True(corpse.Spawned);
            Assert.False(corpse.Destroyed);
            Assert.Equal(where, corpse.Position);
            Assert.Same(husky, corpse.InnerPawn);
            Assert.Equal(0, MeatOnMap(map));
        }

        // ---- the ordinary case ----

        /// <summary>An uninterrupted butchery still turns the body into meat at the bench and leaves the
        /// butcher empty-handed.</summary>
        [Fact]
        public void An_uninterrupted_butchery_still_yields_meat_at_the_bench()
        {
            (CoreMap map, Pawn butcher, Pawn husky, CorpseThing corpse, Thing bench) = ButcherSite();

            RunTicks(6000, butcher);

            Assert.True(corpse.Destroyed);
            Assert.True(husky.Destroyed);
            Assert.Null(butcher.carryTracker.CarriedThing);
            Assert.True(MeatOnMap(map) > 0);
            Assert.Equal(bench.Position, map.listerThings.ThingsOfDef(Def("Meat_Generic"))[0].Position);
        }

        // ---- Scribe ----

        /// <summary>A save taken with the body in hand — on the way, or at the bench — keeps it: the loaded
        /// butcher is holding the same animal's body, the loaded job's target is that very Thing, and the
        /// butchery finishes.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_butcher_saved_mid_carry_still_holds_the_body_after_loading_and_butchers_it(bool atBench)
        {
            (CoreMap map, Pawn butcher, Pawn husky, CorpseThing corpse, Thing bench) = ButcherSite();
            RunUntilBodyIsInHand(butcher, corpse, bench.Position, atBench);
            Assert.Same(corpse, butcher.carryTracker.CarriedThing);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedButcher = loaded.mapPawns.AllPawns.Single(p => p.thingIDNumber == butcher.thingIDNumber);
            var held = Assert.IsType<CorpseThing>(loadedButcher.carryTracker.CarriedThing);
            Assert.False(held.Spawned);
            Assert.Equal(corpse.thingIDNumber, held.thingIDNumber);
            Assert.NotNull(held.InnerPawn);
            Assert.Equal(husky.thingIDNumber, held.InnerPawn!.thingIDNumber);
            Assert.Equal(CorpseWorkDefOf.ButcherCorpse, loadedButcher.jobs.curJob?.def);
            Assert.Same(held, loadedButcher.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.Equal(0, MeatOnMap(loaded));

            RunTicks(6000, loadedButcher);

            Assert.True(held.Destroyed, "The carcass is consumed by the butchery.");
            Assert.Null(loadedButcher.carryTracker.CarriedThing);
            Assert.True(MeatOnMap(loaded) > 0);
        }
    }
}
