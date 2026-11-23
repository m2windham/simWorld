using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <b>A settlement that is hurt gets a window to recover in</b> (RimWorld:
    /// <c>StoryWatcher_Adaptation</c>). The 20/99 populated arc on 87c389a lost 32 citizens to raids and went
    /// extinct on day ~410 with 2,600 food in store: the adaptation factor sat at its 1.2 ceiling from day 60
    /// on, because adapt-days grew without bound and a death cost 1.5 of them. The raid after a sixteen-death
    /// massacre was sized as if nothing had happened. These pin the mechanic's shape, not its literals: the
    /// threat answers losses, the answer is bigger for a small band, recovery is quicker from a hole than
    /// growth is at peace, and quiet time cannot bank an unbounded threat.
    /// </summary>
    public class AdaptationRecoveryTests : ContentTestBase
    {
        public AdaptationRecoveryTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"), Medium);
            Find.FactionManager = new FactionManager();
            Find.TickManager.DebugSetTicksGame(400 * GenDate.TicksPerDay); // past the storyteller's starting factor
        }

        private static StoryWatcher_Adaptation Adaptation => Find.Storyteller.adaptation;

        private static DifficultyDef Medium => DefDatabase<DifficultyDef>.GetNamed("Medium");

        /// <summary>One storyteller interval's worth of quiet, <paramref name="days"/> times over.</summary>
        private static void Quiet(float days)
        {
            int intervals = (int)(days * GenDate.TicksPerDay / Storyteller.IncidentCycleLengthTicks);
            for (int i = 0; i < intervals; i++) Adaptation.AdaptationTick(Medium);
        }

        private CivilizationTarget Town(int citizens, List<Pawn> people)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = NewHuman("Citizen" + i);
                people.Add(p);
                target.pawns.Add(p);
            }
            return target;
        }

        [Fact]
        public void Losing_a_quarter_of_the_town_more_than_halves_the_next_threat()
        {
            // A long peace first, so the factor is wherever quiet time leaves it.
            var people = new List<Pawn>();
            CivilizationTarget town = Town(20, people);
            Quiet(400f);
            float before = StorytellerUtility.DefaultThreatPointsNow(town);

            for (int i = 0; i < 5; i++) people[i].health.Kill(null, null);
            float after = StorytellerUtility.DefaultThreatPointsNow(town);

            // Five of twenty gone takes a quarter off by headcount alone (each living citizen adds the same
            // points here). The storyteller has to answer the losses on top of that, or the next raid lands on
            // the survivors almost as hard as the last one did.
            Assert.True(after < 0.5f * before,
                "after losing 5 of 20 the next threat was " + after + " points against " + before + " before");
            Assert.True(after >= StorytellerUtility.MinThreatPoints, "the threat is eased, not switched off");
        }

        [Fact]
        public void A_loss_costs_a_small_band_more_than_a_large_town()
        {
            Assert.True(StoryWatcher_Adaptation.DeathAdaptDaysLoss(5) > StoryWatcher_Adaptation.DeathAdaptDaysLoss(25));
            Assert.True(StoryWatcher_Adaptation.DownedAdaptDaysLoss(2) > StoryWatcher_Adaptation.DownedAdaptDaysLoss(25));
            Assert.True(StoryWatcher_Adaptation.DeathAdaptDaysLoss(25) > StoryWatcher_Adaptation.DownedAdaptDaysLoss(2),
                "losing someone for good must still cost more than any downing");

            // And through the real funnel: the same death, charged against the people it leaves.
            var few = new List<Pawn>();
            Town(3, few);
            Quiet(80f);
            float quiet = Adaptation.AdaptDays;
            few[0].health.Kill(null, null);
            float costInABand = quiet - Adaptation.AdaptDays;

            Find.Storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"), Medium);
            var many = new List<Pawn>();
            Town(25, many);
            Quiet(80f);
            quiet = Adaptation.AdaptDays;
            many[0].health.Kill(null, null);
            float costInATown = quiet - Adaptation.AdaptDays;

            Assert.True(costInABand > costInATown, "band " + costInABand + " vs town " + costInATown);
        }

        [Fact]
        public void A_massacre_digs_a_hole_and_the_settlement_climbs_out_of_it()
        {
            var people = new List<Pawn>();
            Town(20, people);
            Quiet(400f);
            float peace = Adaptation.TotalThreatPointsFactor(Medium);

            for (int i = 0; i < 8; i++) people[i].health.Kill(null, null);
            float hole = Adaptation.TotalThreatPointsFactor(Medium);
            Assert.True(Adaptation.AdaptDays < 0f, "eight dead should put the clock below a fresh start");
            Assert.True(hole < 0.8f * peace);

            // Recovery from the hole is quicker than growth at peace (RimWorld's growth curve), so a hurt
            // settlement is not left at the floor for good, and the threat does come back.
            float start = Adaptation.AdaptDays;
            Quiet(10f);
            float climbed = Adaptation.AdaptDays - start;

            Quiet(60f);
            float atPeaceStart = Adaptation.AdaptDays;
            Quiet(10f);
            float grewAtPeace = Adaptation.AdaptDays - atPeaceStart;

            Assert.True(climbed > grewAtPeace, "climbed " + climbed + " vs grew " + grewAtPeace);
            Assert.True(Adaptation.TotalThreatPointsFactor(Medium) > hole);
        }

        [Fact]
        public void Quiet_time_cannot_bank_an_unbounded_threat()
        {
            Quiet(400f);
            float afterAYear = Adaptation.TotalThreatPointsFactor(Medium);
            Quiet(2000f);

            Assert.True(Adaptation.AdaptDays <= StoryWatcher_Adaptation.AdaptDaysMax);
            Assert.Equal(afterAYear, Adaptation.TotalThreatPointsFactor(Medium), 3);
            Assert.True(afterAYear < 1.5f, "a long peace should not be punished without limit");
        }

        [Fact]
        public void A_softer_adaptation_impact_pulls_the_factor_toward_one_from_both_sides()
        {
            // RimWorld's "Adaptation impact": the wiki's 40% example narrows the range to about 0.75..1.19, so it
            // lerps toward 1 rather than scaling, and a weak impact must never turn a hole into a harsher raid.
            var soft = new DifficultyDef { defName = "SoftAdaptation", adaptationEffectFactor = 0.4f };

            Quiet(400f);
            float high = Adaptation.TotalThreatPointsFactor(Medium);
            float highSoft = Adaptation.TotalThreatPointsFactor(soft);
            Assert.True(highSoft > 1f && highSoft < high);

            var people = new List<Pawn>();
            Town(10, people);
            for (int i = 0; i < 6; i++) people[i].health.Kill(null, null);
            float low = Adaptation.TotalThreatPointsFactor(Medium);
            float lowSoft = Adaptation.TotalThreatPointsFactor(soft);
            Assert.True(lowSoft < 1f && lowSoft > low);
        }

        [Fact]
        public void A_negative_clock_survives_a_save()
        {
            var people = new List<Pawn>();
            Town(6, people);
            Quiet(20f);
            for (int i = 0; i < 4; i++) people[i].health.Kill(null, null);
            Assert.True(Adaptation.AdaptDays < 0f);

            string xml = Scribe.SaveToString(Adaptation, "adaptation");
            StoryWatcher_Adaptation reloaded = Scribe.Load<StoryWatcher_Adaptation>(xml, "adaptation");
            Assert.Equal(Adaptation.AdaptDays, reloaded.AdaptDays, 4);
            Assert.Equal(Adaptation.TotalThreatPointsFactor(Medium), reloaded.TotalThreatPointsFactor(Medium), 4);
        }
    }
}
