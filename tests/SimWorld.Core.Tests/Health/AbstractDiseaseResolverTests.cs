using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// <see cref="AbstractDiseaseResolver"/>: the fix for the citizen who caught something, dropped to
    /// <see cref="PawnTier.Interval"/>, and then simply stopped being sick because nobody was simulating them
    /// any more. <see cref="DeathAttributionHolder"/> (declared in <c>DeathAttributionTests.cs</c>, this same
    /// namespace) is reused for the Scribe round-trip test rather than declaring a second copy of it.
    /// </summary>
    public class AbstractDiseaseResolverTests : ContentTestBase
    {
        public AbstractDiseaseResolverTests(CoreContentFixture content) : base(content)
        {
        }

        private static HediffDef HediffNamed(string name) => DefDatabase<HediffDef>.GetNamed(name);

        // ---- the headline: an unwatched citizen is no longer frozen ----

        /// <summary>
        /// Before <see cref="AbstractDiseaseResolver"/> existed, this failed: three days is comfortably past
        /// how long untended plague takes to kill a watched pawn
        /// (<c>HealthTests.Plague_kills_untended_but_a_tended_pawn_survives</c> tends across two), and an
        /// Interval-tier citizen carried the hediff at its exact starting severity through the whole span
        /// because <c>Pawn_TierTracker.ApplyElapsed</c>'s Interval branch never touched a hediff at all.
        /// </summary>
        [Fact]
        public void An_unwatched_citizen_with_plague_is_no_longer_frozen()
        {
            Pawn p = NewHuman();
            HediffDef plague = HediffNamed("Plague");
            Hediff hediff = p.health.AddHediff(plague);
            float startingSeverity = hediff.Severity;

            p.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Interval, p.tier.Tier);

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 3 * GenDate.TicksPerDay);
            p.tier.CoarseTick();

            bool stillSickAtExactlyTheSameSeverity =
                !p.Dead
                && p.health.hediffSet.HasHediff(plague)
                && p.health.hediffSet.GetFirstHediffOfDef(plague)!.Severity == startingSeverity;

            Assert.False(
                stillSickAtExactlyTheSameSeverity,
                "an unwatched citizen with plague must not sit frozen at their starting severity forever");

            // The specific outcome untended plague already has to produce at Full tier: it kills, per
            // HealthTests.Plague_kills_untended_but_a_tended_pawn_survives. A citizen with nobody to tend
            // them and nobody watching should reach the same place, not merely "something changed".
            Assert.True(p.Dead);
            Assert.Same(plague, p.health.DeathCauseHediff);
        }

        /// <summary>The mirror case: recovery also has to actually happen, not just death.</summary>
        [Fact]
        public void An_unwatched_citizen_beats_the_flu_given_enough_time()
        {
            Pawn p = NewHuman();
            HediffDef flu = HediffNamed("Flu");
            p.health.AddHediff(flu);

            p.tier.Notify_AttentionChanged(false);

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 4 * GenDate.TicksPerDay);
            p.tier.CoarseTick();

            Assert.False(p.Dead);
            Assert.False(p.health.hediffSet.HasHediff(flu), "flu should have run its course, not sat frozen");
        }

        /// <summary>A span shorter than the illness takes to resolve still moves severity/immunity forward —
        /// the race is a closed-form calculation over whatever span it is handed, not an all-or-nothing flag.</summary>
        [Fact]
        public void A_short_unwatched_span_still_advances_the_race_without_resolving_it()
        {
            Pawn p = NewHuman();
            HediffDef plague = HediffNamed("Plague");
            Hediff hediff = p.health.AddHediff(plague);
            float startingSeverity = hediff.Severity;

            p.tier.Notify_AttentionChanged(false);
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenDate.TicksPerHour * 6);
            p.tier.CoarseTick();

            Assert.False(p.Dead);
            Hediff? stillThere = p.health.hediffSet.GetFirstHediffOfDef(plague);
            Assert.NotNull(stillThere);
            Assert.NotEqual(startingSeverity, stillThere!.Severity);
        }

        // ---- the attention lever: medical capacity, not the camera ----

        private static CoreWorld NewWorld(string seed)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "DiseaseCapacity", 2, soloStart: true);
            Find.World = world;
            return world;
        }

        private static int[] TwoLandTiles(CoreWorld world) =>
            Enumerable.Range(0, world.grid.TilesCount).Where(i => !world.grid.Tiles[i].WaterCovered).Take(2).ToArray();

        private static Settlement NewSettlement(CoreWorld world, string name, int tile)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, Find.TickManager.TicksGame);
            world.worldObjects.Add(settlement);
            return settlement;
        }

        /// <summary>
        /// The design this whole module exists to protect: infrastructure buys the right to not look.
        /// Neither settlement is watched — both citizens sit at Interval tier throughout — so the only
        /// difference between them is what <see cref="AbstractDiseaseResolver"/> reads off each settlement's
        /// own roster. A lone citizen with nobody else around races untended plague exactly as the Full-tier
        /// untended case does and loses; a settlement with a skilled doctor on its roster gets that doctor's
        /// tend quality applied throughout and wins, with nobody ever looking at either place.
        /// </summary>
        [Fact]
        public void A_settlement_with_medical_capacity_does_better_unwatched_than_one_without()
        {
            CoreWorld world = NewWorld("disease-capacity");
            int[] tiles = TwoLandTiles(world);
            Assert.True(tiles.Length == 2, "the generated world needs two land tiles for this fixture");

            Settlement lonely = NewSettlement(world, "Lonely", tiles[0]);
            Pawn aloneSick = NewHuman("Alone");
            lonely.AddCitizen(aloneSick);

            Settlement cared = NewSettlement(world, "Cared", tiles[1]);
            Pawn caredSick = NewHuman("Patient");
            Pawn doctor = NewHuman("Doctor");
            doctor.skills!.GetSkill(SkillDefOf.Medicine)!.Level = SkillRecord.MaxLevel;
            cared.AddCitizen(caredSick);
            cared.AddCitizen(doctor);

            HediffDef plague = HediffNamed("Plague");
            aloneSick.health.AddHediff(plague);
            caredSick.health.AddHediff(plague);

            aloneSick.tier.Notify_AttentionChanged(false);
            caredSick.tier.Notify_AttentionChanged(false);

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 3 * GenDate.TicksPerDay);
            aloneSick.tier.CoarseTick();
            caredSick.tier.CoarseTick();

            Assert.True(aloneSick.Dead, "nobody to tend them: plague should run its untended, lethal course");
            Assert.False(caredSick.Dead, "a skilled doctor on the roster should be enough to pull the patient through");
        }

        /// <summary>A caregiver who is also the patient does not count — self-tending is not a real doctor,
        /// matching <c>WorkGiver_Tend</c>'s own rule. A settlement of one sick citizen has no capacity.</summary>
        [Fact]
        public void A_lone_citizen_gets_no_credit_for_tending_themselves()
        {
            CoreWorld world = NewWorld("disease-capacity-solo");
            int tile = TwoLandTiles(world).First();
            Settlement settlement = NewSettlement(world, "Solo", tile);
            Pawn sick = NewHuman("OnlyOne");
            sick.skills!.GetSkill(SkillDefOf.Medicine)!.Level = SkillRecord.MaxLevel;
            settlement.AddCitizen(sick);

            HediffDef plague = HediffNamed("Plague");
            sick.health.AddHediff(plague);
            sick.tier.Notify_AttentionChanged(false);

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 3 * GenDate.TicksPerDay);
            sick.tier.CoarseTick();

            Assert.True(sick.Dead, "their own Medicine skill should not tend themselves");
        }

        // ---- Scribe ----

        [Fact]
        public void Hediff_provenance_survives_a_scribe_round_trip()
        {
            Pawn p = NewHuman("Patient");
            Hediff hediff = p.health.AddHediff(HediffNamed("Plague"));
            hediff.sourceIncident = "Disease_Plague";

            var holder = new DeathAttributionHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            DeathAttributionHolder loaded = Scribe.Load<DeathAttributionHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Hediff loadedHediff = loaded.pawns![0].health.hediffSet.GetFirstHediffOfDef(HediffNamed("Plague"))!;
            Assert.Equal("Disease_Plague", loadedHediff.sourceIncident);
        }

        /// <summary>A hediff with nothing behind it round-trips a null, not an empty string or a crash.</summary>
        [Fact]
        public void An_ordinary_hediff_with_no_incident_behind_it_round_trips_null_provenance()
        {
            Pawn p = NewHuman("Patient");
            p.health.AddHediff(HediffNamed("Flu"));

            var holder = new DeathAttributionHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            DeathAttributionHolder loaded = Scribe.Load<DeathAttributionHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Hediff loadedHediff = loaded.pawns![0].health.hediffSet.GetFirstHediffOfDef(HediffNamed("Flu"))!;
            Assert.Null(loadedHediff.sourceIncident);
        }
    }
}
