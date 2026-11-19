using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// One test per cause the played-game round trip (<see cref="SaveLoadRoundTripTests"/>) found, each small
    /// enough to name its cause when it fails.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SaveLoadRoundTripCauseTests : ContentTestBase
    {
        public SaveLoadRoundTripCauseTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private sealed class PawnHolder : IExposable
        {
            public Pawn? pawn;
            public void ExposeData() => Scribe_Deep.Look(ref pawn, "pawn");
        }

        [Fact]
        public void A_muffalo_keeps_its_milk_and_its_wool_fullness_apart_through_a_save()
        {
            CoreMap map = NewMap(5, 5);
            var muffalo = new Pawn(Def("Muffalo"), "Muffalo") { gender = Gender.Female };
            GenSpawn.Spawn(muffalo, new IntVec3(2, 0, 2), map);
            muffalo.GetComp<CompMilkable>()!.fullness = 0.25f;
            muffalo.GetComp<CompShearable>()!.fullness = 0.75f;

            string xml = Scribe.SaveToString(new PawnHolder { pawn = muffalo }, "game");
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(0.25f, loaded.pawn!.GetComp<CompMilkable>()!.fullness);
            Assert.Equal(0.75f, loaded.pawn.GetComp<CompShearable>()!.fullness);
        }

        [Fact]
        public void A_room_temperature_survives_a_save_taken_straight_after_a_load()
        {
            CoreMap map = NewMap(10, 10);
            map.outdoorTemperature = 0f;
            var rect = new CellRect(2, 2, 4, 4);
            foreach (IntVec3 c in rect.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            map.roomTracker.RoomAt(new IntVec3(3, 0, 3))!.Group.temperature = 12f;

            // Load, and save again before any tick rebuilds the rooms — what a host's save-after-load does.
            string first = Scribe.SaveToString(map, "map");
            CoreMap once = Scribe.Load<CoreMap>(first, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            string second = Scribe.SaveToString(once, "map");
            CoreMap twice = Scribe.Load<CoreMap>(second, "map", out errors);
            Assert.Empty(errors);

            twice.MapTick();
            Assert.InRange(twice.roomTracker.RoomAt(new IntVec3(3, 0, 3))!.Temperature, 11f, 12f);
        }

        [Fact]
        public void Lifespan_adjustment_reasons_survive_a_save_and_a_load()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
            pawn.ageTracker.AdjustLifespan(-100f, "test: chronic illness");
            string described = pawn.ageTracker.DescribeLifespanForChronicle();
            Assert.Contains("chronic illness", described);

            string xml = Scribe.SaveToString(new PawnHolder { pawn = pawn }, "game");
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(described, pawn.ageTracker.DescribeLifespanForChronicle()); // saving must not clear it
            Assert.Equal(described, loaded.pawn!.ageTracker.DescribeLifespanForChronicle());
        }

        [Fact]
        public void A_whole_stack_in_a_haulers_hands_is_saved_and_its_job_and_reservation_relink()
        {
            CoreMap map = NewMap(12, 12);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, new IntVec3(0, 0, 0), map);
            var stockpile = new Zone_Stockpile();
            map.zoneManager.RegisterZone(stockpile);
            map.zoneManager.AddCell(stockpile, new IntVec3(10, 0, 10));
            stockpile.filter.SetAllow(Def("RawBerries"), true);
            Thing berries = ThingMaker.MakeThing(Def("RawBerries"));
            berries.stackCount = 8;
            GenSpawn.Spawn(berries, new IntVec3(2, 0, 2), map);

            hauler.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);
            for (int i = 0; i < 600 && berries.Spawned; i++) RunTicks(1, hauler);
            Assert.False(berries.Spawned, "the hauler never picked the berries up");
            Assert.Equal(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedHauler = loaded.mapPawns.AllPawns.Single(p => p.RaceProps.Humanlike);
            Thing? target = loadedHauler.jobs.curJob?.GetTarget(TargetIndex.A).Thing;
            Assert.NotNull(target);
            Assert.Equal(Def("RawBerries"), target!.def);
            Assert.Equal(8, target.stackCount);
            Assert.True(loaded.reservationManager.IsReservedBy(loadedHauler, target));

            // The rebuilt driver gets the Thing back, so the berries are not lost once play resumes: they reach
            // the stockpile or, if the resumed job gives up, the hauler's feet.
            RunTicks(2000, loadedHauler);
            List<Thing> onMap = loaded.listerThings.ThingsOfDef(Def("RawBerries")).Where(t => t.Spawned).ToList();
            Assert.Equal(8, onMap.Sum(t => t.stackCount));
        }
    }
}
