using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Hypothermia, heatstroke and frostbite from the air (RimWorld: <c>HediffGiver_Hypothermia</c>,
    /// <c>HediffGiver_Heat</c>), and the remedy (<c>JobGiver_SeekSafeTemperature</c>). The rates are
    /// RimWorld's, read from its decompiled source and the wiki, so they are asserted as literals; the
    /// unsourced numbers (apparel insulation, frostbite vulnerabilities) are asserted only as behaviour.
    /// </summary>
    public class TemperatureInjuryTests : ContentTestBase
    {
        private const float Eps = 1e-6f;

        public TemperatureInjuryTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(float outdoor)
        {
            var map = new CoreMap(14, 14, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };
            map.MapTick();
            return map;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Citizen")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static float Severity(Pawn p, HediffDef def) => p.health.hediffSet.GetFirstHediffOfDef(def)?.Severity ?? 0f;

        private static void SetSeverity(Pawn p, HediffDef def, float severity)
        {
            HealthUtility.AdjustSeverity(p, def, severity - Severity(p, def));
        }

        [Fact]
        public void Content_binds_and_a_naked_human_is_comfortable_from_16_to_26()
        {
            Assert.Empty(Content.Result.Errors);
            CoreMap map = NewMap(20f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(p);
            Assert.Equal(16f, comfy.min, 3);
            Assert.Equal(26f, comfy.max, 3);
            FloatRange safe = TemperatureInjuryUtility.SafeTemperatureRange(p);
            Assert.Equal(6f, safe.min, 3);
            Assert.Equal(36f, safe.max, 3);
        }

        [Theory]
        [InlineData(-30f, 36f * 6.45e-5f)] // 36 degrees under the 6 C safe minimum
        [InlineData(5f, 0.00075f)]         // one degree under: the floor
        public void Hypothermia_rises_per_interval_by_RimWorld_rate_below_the_safe_minimum(float air, float expected)
        {
            CoreMap map = NewMap(air);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            TemperatureInjuryUtility.TemperatureInterval(p);
            Assert.Equal(expected, Severity(p, TemperatureDefOf.Hypothermia), 6);
        }

        [Fact]
        public void Between_the_safe_and_comfortable_minimum_hypothermia_neither_starts_nor_recovers()
        {
            CoreMap map = NewMap(10f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            TemperatureInjuryUtility.TemperatureInterval(p);
            Assert.Null(p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia));
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.3f);
            TemperatureInjuryUtility.TemperatureInterval(p);
            Assert.Equal(0.3f, Severity(p, TemperatureDefOf.Hypothermia), 5);
        }

        [Theory]
        [InlineData(0.5f, 0.0135f)] // 2.7% of severity
        [InlineData(0.9f, 0.015f)]  // capped
        [InlineData(0.02f, 0.0015f)] // floored
        public void Back_in_comfortable_air_hypothermia_recovers_by_a_clamped_share_of_itself(float start, float drop)
        {
            CoreMap map = NewMap(20f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            SetSeverity(p, TemperatureDefOf.Hypothermia, start);
            TemperatureInjuryUtility.TemperatureInterval(p);
            Assert.Equal(start - drop, Severity(p, TemperatureDefOf.Hypothermia), 5);
        }

        [Theory]
        [InlineData(60f, 24f * 6.45e-5f)]  // 24 over: on the curve's straight part
        [InlineData(86f, 40f * 6.45e-5f)]  // 50 over: the curve bends it to 40
        [InlineData(37f, 0.000375f)]       // one over: the floor
        public void Heatstroke_rises_by_the_curved_excess_above_the_safe_maximum(float air, float expected)
        {
            CoreMap map = NewMap(air);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            TemperatureInjuryUtility.TemperatureInterval(p);
            Assert.Equal(expected, Severity(p, TemperatureDefOf.Heatstroke), 6);
        }

        [Fact]
        public void Heatstroke_recovers_below_the_comfortable_maximum()
        {
            CoreMap map = NewMap(20f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            SetSeverity(p, TemperatureDefOf.Heatstroke, 0.5f);
            TemperatureInjuryUtility.TemperatureInterval(p);
            Assert.Equal(0.5f - 0.0135f, Severity(p, TemperatureDefOf.Heatstroke), 5);
        }

        [Fact]
        public void Stages_progress_shivering_to_serious_to_extreme_and_cut_consciousness()
        {
            CoreMap map = NewMap(-30f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            float fullConsciousness = p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.05f);
            Assert.True(TemperatureInjuryUtility.HasTemperatureInjury(p, TemperatureInjuryStage.Initial));
            Assert.False(TemperatureInjuryUtility.HasTemperatureInjury(p, TemperatureInjuryStage.Serious));
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.36f);
            Assert.True(TemperatureInjuryUtility.HasTemperatureInjury(p, TemperatureInjuryStage.Serious));
            Assert.True(p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) < fullConsciousness);
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.7f);
            Assert.True(TemperatureInjuryUtility.HasTemperatureInjury(p, TemperatureInjuryStage.Extreme));
            Assert.True(p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) <= 0.1f + Eps);
            Assert.True(p.health.Downed);
        }

        [Fact]
        public void Left_in_deep_cold_a_human_dies_of_hypothermia_through_the_health_tick()
        {
            CoreMap map = NewMap(-40f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            // 46 degrees under: 0.002967 per 60 ticks, so a little over 20,000 ticks to reach 1.
            RunTicks(30000, p);
            Assert.True(p.Dead);
            Assert.Equal(DeathCause.Exposure, p.health.CauseOfDeath);
            Assert.True(Severity(p, TemperatureDefOf.Hypothermia) >= 1f - Eps);
        }

        [Fact]
        public void Left_in_great_heat_a_human_dies_of_heatstroke_through_the_health_tick()
        {
            CoreMap map = NewMap(70f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            RunTicks(40000, p);
            Assert.True(p.Dead);
            Assert.Equal(DeathCause.Exposure, p.health.CauseOfDeath);
            Assert.True(Severity(p, TemperatureDefOf.Heatstroke) >= 1f - Eps);
        }

        [Fact]
        public void A_parka_lowers_the_comfortable_minimum_so_the_same_cold_does_no_harm()
        {
            CoreMap map = NewMap(-10f);
            Pawn bare = SpawnHuman(map, new IntVec3(4, 0, 4), "Bare");
            Pawn wrapped = SpawnHuman(map, new IntVec3(8, 0, 8), "Wrapped");
            var parka = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Parka"), DefDatabase<ThingDef>.GetNamed("Cloth"));
            wrapped.apparel.Wear(parka);
            Assert.True(TemperatureInjuryUtility.ComfortableTemperatureRange(wrapped).min < TemperatureInjuryUtility.ComfortableTemperatureRange(bare).min);
            TemperatureInjuryUtility.TemperatureInterval(bare);
            TemperatureInjuryUtility.TemperatureInterval(wrapped);
            Assert.True(Severity(bare, TemperatureDefOf.Hypothermia) > 0f);
            Assert.Equal(0f, Severity(wrapped, TemperatureDefOf.Hypothermia));
        }

        [Fact]
        public void Serious_hypothermia_in_freezing_air_frostbites_an_extremity()
        {
            CoreMap map = NewMap(-20f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.5f);
            Hediff_Injury? bite = null;
            for (int i = 0; i < 2000 && bite == null; i++)
            {
                SetSeverity(p, TemperatureDefOf.Hypothermia, 0.5f);
                TemperatureInjuryUtility.TemperatureInterval(p);
                bite = p.health.hediffSet.hediffs.OfType<Hediff_Injury>().FirstOrDefault(h => h.def.defName == "Frostbite");
            }
            Assert.NotNull(bite);
            Assert.True(bite!.Part!.def.frostbiteVulnerability > 0f);
        }

        [Fact]
        public void No_frostbite_above_freezing_or_below_the_severity_threshold()
        {
            CoreMap map = NewMap(3f);
            Pawn warmish = SpawnHuman(map, new IntVec3(4, 0, 4), "Warmish");
            for (int i = 0; i < 500; i++)
            {
                SetSeverity(warmish, TemperatureDefOf.Hypothermia, 0.6f);
                TemperatureInjuryUtility.TemperatureInterval(warmish);
            }
            Assert.DoesNotContain(warmish.health.hediffSet.hediffs, h => h.def.defName == "Frostbite");

            map.outdoorTemperature = -20f;
            Pawn mild = SpawnHuman(map, new IntVec3(8, 0, 8), "Mild");
            for (int i = 0; i < 500; i++)
            {
                SetSeverity(mild, TemperatureDefOf.Hypothermia, 0.3f);
                TemperatureInjuryUtility.TemperatureInterval(mild);
            }
            Assert.DoesNotContain(mild.health.hediffSet.hediffs, h => h.def.defName == "Frostbite");
        }

        // ---- the remedy ----

        [Fact]
        public void Below_the_serious_stage_nobody_seeks_safe_temperature()
        {
            // Both places a giver could send it are on offer: comfortable air where it stands (a wait), and a
            // warm house from a freezing doorstep (a walk). At the minor stage it takes neither.
            CoreMap map = NewMap(20f);
            Pawn inWarmth = SpawnHuman(map, new IntVec3(12, 0, 12), "InWarmth");
            SetSeverity(inWarmth, TemperatureDefOf.Hypothermia, 0.3f);
            Assert.False(new JobGiver_SeekSafeTemperature().TryIssueJobPackage(inWarmth).IsValid);

            CoreMap cold = NewMap(-20f);
            IntVec3 doorstep = BuildHouse(cold, 20f);
            Pawn onDoorstep = SpawnHuman(cold, doorstep, "OnDoorstep");
            SetSeverity(onDoorstep, TemperatureDefOf.Hypothermia, 0.3f);
            Assert.False(new JobGiver_SeekSafeTemperature().TryIssueJobPackage(onDoorstep).IsValid);
        }

        [Fact]
        public void A_seriously_hypothermic_pawn_already_in_comfortable_air_waits_there()
        {
            CoreMap map = NewMap(20f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.4f);
            ThinkResult result = new JobGiver_SeekSafeTemperature().TryIssueJobPackage(p);
            Assert.True(result.IsValid);
            Assert.Equal(SafeTemperatureJobDefOf.Wait_SafeTemperature, result.Job!.def);
            Assert.Equal(JobGiver_SeekSafeTemperature.WaitTicks, result.Job.expiryInterval);
        }

        /// <summary>
        /// A walled, roofed house (5x5 inside) with one door to the outside, its air set to
        /// <paramref name="inside"/>. A door keeps its room thermally separate, so the house holds its own
        /// air against the outdoors. Returns the cell just outside the door.
        /// </summary>
        private static IntVec3 BuildHouse(CoreMap map, float inside)
        {
            var shell = new CellRect(1, 2, 7, 7);
            var door = new IntVec3(7, 0, 5);
            foreach (IntVec3 c in shell.EdgeCells)
            {
                SpawnEdifice(map, c, c == door ? "Door" : "Wall");
            }
            foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            Room room = map.roomTracker.RoomAt(new IntVec3(4, 0, 5))!;
            Assert.False(room.Group.UsesOutdoorTemperature);
            room.Group.temperature = inside;
            return new IntVec3(8, 0, 5);
        }

        [Fact]
        public void A_seriously_hypothermic_pawn_walks_to_comfortable_air_and_recovers_there()
        {
            CoreMap map = NewMap(-20f);
            IntVec3 doorstep = BuildHouse(map, 20f);
            Pawn p = SpawnHuman(map, doorstep);
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.4f);

            ThinkResult result = new JobGiver_SeekSafeTemperature().TryIssueJobPackage(p);
            Assert.True(result.IsValid);
            Assert.Equal(SafeTemperatureJobDefOf.GotoSafeTemperature, result.Job!.def);
            IntVec3 target = result.Job.targetA.Cell;
            Assert.Equal(20f, map.roomTracker.RoomAt(target)!.Temperature);

            p.jobs.StartJob(result.Job);
            for (int i = 0; i < 30000 && TemperatureInjuryUtility.HasTemperatureInjury(p, TemperatureInjuryStage.Serious); i++)
            {
                map.roomTracker.RoomAt(target)!.Group.temperature = 20f; // hold the room warm against the cold outside
                RunTicks(1, p);
            }
            Assert.False(p.Dead);
            Assert.NotEqual(doorstep, p.Position);
            Assert.False(TemperatureInjuryUtility.HasTemperatureInjury(p, TemperatureInjuryStage.Serious));
        }

        [Fact]
        public void The_humanlike_tree_sends_a_seriously_cold_citizen_to_warm_air()
        {
            CoreMap map = NewMap(-20f);
            IntVec3 doorstep = BuildHouse(map, 20f);
            Pawn p = SpawnHuman(map, doorstep);
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.4f);
            p.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            p.jobs.TryFindAndStartJob();
            Assert.Equal(SafeTemperatureJobDefOf.GotoSafeTemperature, p.jobs.curJob?.def);
        }

        [Fact]
        public void With_no_comfortable_air_in_reach_the_giver_gives_nothing()
        {
            CoreMap map = NewMap(-20f);
            BuildHouse(map, -20f);
            Pawn p = SpawnHuman(map, new IntVec3(12, 0, 12));
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.4f);
            Assert.False(new JobGiver_SeekSafeTemperature().TryIssueJobPackage(p).IsValid);
        }

        [Fact]
        public void A_seriously_heatstruck_pawn_seeks_cooler_air_too()
        {
            CoreMap map = NewMap(45f);
            IntVec3 doorstep = BuildHouse(map, 22f);
            Pawn p = SpawnHuman(map, doorstep);
            SetSeverity(p, TemperatureDefOf.Heatstroke, 0.4f);
            ThinkResult result = new JobGiver_SeekSafeTemperature().TryIssueJobPackage(p);
            Assert.Equal(SafeTemperatureJobDefOf.GotoSafeTemperature, result.Job!.def);
        }

        [Fact]
        public void Temperature_injuries_survive_a_save_round_trip()
        {
            CoreMap map = NewMap(20f);
            Pawn p = SpawnHuman(map, new IntVec3(5, 0, 5));
            SetSeverity(p, TemperatureDefOf.Hypothermia, 0.42f);
            SetSeverity(p, TemperatureDefOf.Heatstroke, 0.13f);
            var holder = new PawnHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder back = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Pawn loaded = back.pawns![0];
            Assert.Equal(0.42f, Severity(loaded, TemperatureDefOf.Hypothermia), 4);
            Assert.Equal(0.13f, Severity(loaded, TemperatureDefOf.Heatstroke), 4);
        }

        private static void SpawnEdifice(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)), cell, map);
    }
}
