using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Break the sod on a real generated Agrarian village, with fields the player marks the way the third
    /// playtest's tester did. There the edict read "In force" while six stood idle and the rest cleaned: wild
    /// berries outranked sowing in the Growing list, wild growth in a field was nobody's Growing work, and the
    /// standing field was sown in an afternoon, leaving ten days with nothing to do.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BreakTheSodMapTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public BreakTheSodMapTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static readonly HashSet<string> FieldWork = new HashSet<string> { "Sow", "CutPlant", "Harvest" };

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static (Settlement settlement, CoreMap map, List<Pawn> citizens) Opened(string seed)
        {
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("AgrarianVillage").scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            return (settlement, map, settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList());
        }

        /// <summary>
        /// A <paramref name="size"/>-square block of open, fertile, unzoned ground near the band holding at least
        /// <paramref name="minPlants"/> wild plants, scanning outward from the band's middle. What a player drags a
        /// field over: the ground near home, as it is.
        /// </summary>
        private static List<IntVec3> FieldBlock(CoreMap map, List<Pawn> citizens, int size, int minPlants)
        {
            var centre = new IntVec3((int)citizens.Average(p => p.Position.x), 0, (int)citizens.Average(p => p.Position.z));
            for (int r = 4; r < 120; r += 2)
            {
                for (int dx = -r; dx <= r; dx += 2)
                {
                    for (int dz = -r; dz <= r; dz += 2)
                    {
                        var cells = new List<IntVec3>();
                        bool ok = true;
                        for (int x = 0; x < size && ok; x++)
                        {
                            for (int z = 0; z < size && ok; z++)
                            {
                                var c = new IntVec3(centre.x + dx + x, 0, centre.z + dz + z);
                                ok = GenGrid.InBounds(c, map) && map.zoneManager.ZoneAt(c) == null && map.edificeGrid[c] == null
                                    && GenGrid.Standable(c, map) && map.terrainGrid.TerrainAt(c).fertility >= 0.5f
                                    && !map.roofGrid.Roofed(c)
                                    && !map.thingGrid.CellContains(c, ThingCategory.Blueprint) && !map.thingGrid.CellContains(c, ThingCategory.Frame);
                                if (ok) cells.Add(c);
                            }
                        }
                        if (ok && cells.Count(c => map.thingGrid.CellContains(c, ThingCategory.Plant)) >= minPlants) return cells;
                    }
                }
            }
            return new List<IntVec3>();
        }

        private static int Sown(CoreMap map, List<IntVec3> field, ThingDef crop) =>
            field.Count(c => map.thingGrid.ThingsListAt(c).Any(t => t.def == crop));

        private static int WildIn(CoreMap map, List<IntVec3> field, ThingDef crop) =>
            field.Count(c => map.thingGrid.ThingsListAt(c).Any(t => t is Plant && t.def != crop));

        /// <summary>Cells of every field on the map standing in that field's own crop.</summary>
        private static int SownInAllFields(CoreMap map) =>
            map.zoneManager.AllZones.OfType<Zone_Growing>().Sum(z => z.Cells.Count(c => map.thingGrid.ThingsListAt(c).Any(t => t.def == z.plantDefToGrow)));

        private static Dictionary<string, int> Sample(List<Pawn> citizens, int ticks, int every = 125)
        {
            var jobs = new Dictionary<string, int>();
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i % every != 0) continue;
                foreach (Pawn p in citizens)
                {
                    if (p.Dead || !p.Spawned) continue;
                    string k = p.jobs.curJob?.def.defName ?? "none";
                    jobs[k] = jobs.GetValueOrDefault(k) + 1;
                }
            }
            return jobs;
        }

        private static string Show(Dictionary<string, int> jobs) =>
            string.Join(", ", jobs.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value));

        [Fact]
        public void A_grower_clears_wild_growth_from_its_own_field()
        {
            // RimWorld's WorkGiver_GrowerSow hands back a CutPlant job for a plant in the way: the field's own
            // work clears it, without waiting on PlantCutting.
            (Settlement _, CoreMap map, List<Pawn> citizens) = Opened("sod-clear-unit");
            List<IntVec3> field = FieldBlock(map, citizens, 8, 3);
            Assert.NotEmpty(field);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", field).Outcome);

            var sow = (WorkGiver_GrowerSow)DefDatabase<WorkGiverDef>.GetNamed("GrowerSow").Worker;
            Pawn grower = citizens.First(p => !p.Downed);
            IntVec3 blocked = field.First(c => map.thingGrid.CellContains(c, ThingCategory.Plant));
            Thing blocker = map.thingGrid.ThingsListAt(blocked).First(t => t is Plant);
            Assert.True(sow.HasJobOnCell(grower, blocked), "a field cell with wild growth on it is sowing work");
            Job job = sow.JobOnCell(grower, blocked)!;
            Assert.Same(PlantCuttingJobDefOf.CutPlant, job.def);
            Assert.Same(blocker, job.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Under_break_the_sod_the_band_clears_and_sows_a_field_drawn_over_wild_growth()
        {
            (Settlement _, CoreMap map, List<Pawn> citizens) = Opened("sod-wild-field");
            ThingDef potato = Def("Plant_Potato");
            List<IntVec3> field = FieldBlock(map, citizens, 10, 3);
            Assert.NotEmpty(field);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", field).Outcome);
            int wildBefore = WildIn(map, field, potato);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("BreakTheSod").Outcome);
            Dictionary<string, int> jobs = Sample(citizens, GenDate.TicksPerHour * 8);
            int sown = Sown(map, field, potato);
            int wildAfter = WildIn(map, field, potato);
            output.WriteLine($"field {field.Count}, wild {wildBefore} -> {wildAfter}, sown {sown} | {Show(jobs)}");
            foreach (IntVec3 c in field)
            {
                foreach (Thing t in map.thingGrid.ThingsListAt(c))
                    if (t is Plant pl && pl.def != potato) output.WriteLine($"  still wild: {pl.def.defName} at {c}, growth {pl.Growth:0.00}");
            }

            // Within a working day the band has cleared the field and sown nearly all of it, and field work is what
            // it was doing.
            Assert.True(wildAfter < wildBefore, $"wild growth {wildBefore} -> {wildAfter}");
            Assert.True(sown >= field.Count * 0.9, $"sown {sown} of {field.Count}");
            int fieldWork = jobs.Where(kv => FieldWork.Contains(kv.Key)).Sum(kv => kv.Value);
            Assert.True(fieldWork * 2 >= jobs.Values.Sum(), $"field work {fieldWork} of {jobs.Values.Sum()} samples: {Show(jobs)}");
        }

        [Fact]
        public void Under_break_the_sod_a_field_is_sown_before_wild_food_is_foraged()
        {
            // The probe's seed: a village whose ground carries ripe berry bushes. Before the fix every hand under
            // the edict picked berries across the map and not one field cell was sown in two hours.
            (Settlement _, CoreMap map, List<Pawn> citizens) = Opened("sod-probe-a");
            ThingDef potato = Def("Plant_Potato");
            Assert.Contains(map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>(),
                p => p.def.defName == "Plant_Berry" && p.HarvestableNow && p.FullyGrown);
            List<IntVec3> field = FieldBlock(map, citizens, 10, 0);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", field).Outcome);
            int before = SownInAllFields(map);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("BreakTheSod").Outcome);
            Dictionary<string, int> jobs = Sample(citizens, GenDate.TicksPerHour);
            int sown = SownInAllFields(map) - before;
            output.WriteLine($"sown {sown} across the fields, {Sown(map, field, potato)} of {field.Count} in the new one | {Show(jobs)}");
            // The edict's hands sow every field there is, the village's own as well as the new one.
            Assert.True(sown >= 20, $"sown {sown} cells in the first hour");
            Assert.True(jobs.GetValueOrDefault("Forage") * 4 < jobs.GetValueOrDefault("Sow"), "sowing far ahead of foraging: " + Show(jobs));
            int fieldWork = jobs.Where(kv => FieldWork.Contains(kv.Key)).Sum(kv => kv.Value);
            Assert.True(fieldWork * 2 >= jobs.Values.Sum(), $"field work {fieldWork} of {jobs.Values.Sum()} samples: {Show(jobs)}");
        }

        [Fact]
        public void Break_the_sod_breaks_new_ground_and_the_field_keeps_it()
        {
            (Settlement settlement, CoreMap map, List<Pawn> _) = Opened("sod-new-ground");
            int wanted = FarmingInitiative.FieldCellsWantedFor(settlement, map);
            for (int pass = 0; pass < 40; pass++) FarmingInitiative.Run(map);
            int before = FarmingInitiative.FieldOf(map)!.CellCount;
            Assert.True(before >= wanted, $"the village's field {before} has not reached the {wanted} it wants");
            for (int pass = 0; pass < 40; pass++) FarmingInitiative.Run(map);
            Assert.Equal(before, FarmingInitiative.FieldOf(map)!.CellCount); // with no edict it stays the size it is

            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("BreakTheSod").Outcome);
            for (int pass = 0; pass < 40; pass++) FarmingInitiative.Run(map);
            int during = FarmingInitiative.FieldOf(map)!.CellCount;
            output.WriteLine($"wanted {wanted}, field {before} -> {during} under the edict");
            Assert.True(during >= before + wanted / 2, $"the field grew only from {before} to {during}");
            Assert.True(FarmingInitiative.FieldOf(map)!.allowSow);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.RescindEdict("BreakTheSod").Outcome);
            for (int pass = 0; pass < 40; pass++) FarmingInitiative.Run(map);
            Assert.Equal(during, FarmingInitiative.FieldOf(map)!.CellCount);
        }

        [Fact]
        public void Gathering_wild_food_reads_as_foraging_and_the_hunt_as_hunting()
        {
            // Under the hunter's mandate the third playtest's roster said "harvesting." for everyone: the hands
            // with no bow fell back on gathering berries, which read as a field's harvest.
            Assert.Equal("foraging.", DefDatabase<JobDef>.GetNamed("Forage").reportString);
            Assert.Equal("hunting.", DefDatabase<JobDef>.GetNamed("Hunt").reportString);

            (Settlement settlement, CoreMap map, List<Pawn> citizens) = Opened("sod-forage-label");
            var forage = (WorkGiver_ForageWild)DefDatabase<WorkGiverDef>.GetNamed("ForageWild").Worker;
            Pawn gatherer = citizens.First(p => !p.Downed);
            Plant berry = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>()
                .Where(p => p.def.defName == "Plant_Berry" && p.FullyGrown && forage.HasJobOnThing(gatherer, p))
                .OrderBy(p => (p.Position - gatherer.Position).LengthHorizontalSquared).First();
            Job job = forage.JobOnThing(gatherer, berry)!;
            Assert.Equal("Forage", job.def.defName);
            gatherer.jobs.StartJob(job, JobCondition.InterruptForced);
            CitizenLine line = GodViewSnapshot.CitizensOf(settlement.tile)!.Citizens.Single(c => c.ThingId == gatherer.thingIDNumber);
            Assert.Equal("foraging.", line.ActivityLabel);

            // And a wild plant is not the field's harvest: the grower's own giver leaves it to the forager.
            var harvest = (WorkGiver_GrowerHarvest)DefDatabase<WorkGiverDef>.GetNamed("GrowerHarvest").Worker;
            Assert.False(harvest.HasJobOnThing(gatherer, berry));
        }
    }
}
