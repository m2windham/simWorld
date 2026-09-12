using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>A second test that watches the game rather than a module</b>, in the shape
    /// <see cref="SettlementFoodTests"/> established: found a settlement the ordinary way through
    /// <see cref="Game.NewGame"/>, tick a week of game time with nothing called by hand, and ask the
    /// questions a suite of module tests cannot.
    ///
    /// <para/><b>Why it exists.</b> With the food economy closed, a founded settlement still lost a third to
    /// a half of its founders in its first week, and the single largest term in the whole town's mood ledger
    /// was recreation at −20 points a head, on everybody, from day two onward for ever. Two defects sat
    /// behind that and each was invisible from inside its own module (<c>docs/WORK-REGISTER.md</c> §9a):
    /// <list type="number">
    /// <item><b>Nothing in <c>src/</c> could raise <c>Need_Joy</c>.</b> The need, its per-kind tolerance
    /// model and its mood thought were built, tested and green; <c>GainJoy</c> had exactly one caller and it
    /// was <c>CompDrug</c>. There was no <c>JoyGiverDef</c>, no <c>JoyGiver</c>, no <c>JobGiver_GetJoy</c> and
    /// no recreation tier in the humanlike think tree. And every one of those is map-based, so an unwatched
    /// settlement needed an abstract path too, exactly as eating did.</item>
    /// <item><b>The social fight was not a fight on a map.</b>
    /// <c>MentalState_SocialFighting</c> cast a melee verb from its own tick at range zero, so citizens of a
    /// settlement nobody had ever opened — a settlement with no map in existence — beat each other bloody;
    /// and the escalation chance was a flat 15% behind gates RimWorld does not have, against RimWorld's own
    /// 4% × a chain of continuous factors.</item>
    /// </list>
    ///
    /// <para/><b>What this asserts, and what it deliberately does not.</b> It asserts the two things this
    /// lane changed and can explain when they go red: that recreation is <i>reachable</i> and does not sit
    /// pinned at its worst stage, on a watched settlement and on an unwatched one alike; and that a
    /// settlement with nothing wrong with it does not lose a third of its people <i>to its own citizens</i>.
    /// It does not assert a mood figure, a death count of zero, or anything about food, rest, weather or
    /// raids — those are other systems, several of them known to be unhealthy (an unwatched settlement still
    /// cannot sleep), and a test that failed for their reasons could not explain itself.
    ///
    /// <para/><b>"To its own citizens" is doing real work in that sentence.</b> §9a's headline — every death
    /// in the week is <c>"X has been beaten to death"</c> by another citizen — turned out to be reading the
    /// <c>Blunt</c> DamageDef's own death message, which says that whatever dealt the blow. Measured here,
    /// the large majority of a founded settlement's first-week deaths are citizens crushed by roof collapses
    /// while mining (<c>Building.RoofCollapseUtility</c>: 100 Blunt, no instigator, under thick rock). So
    /// this counts <see cref="SimWorld.Health.Pawn_HealthTracker.KilledByAnotherPawn"/> rather than
    /// <c>DiedViolently</c>, and the mining defect is reported rather than silently absorbed.
    ///
    /// <para/><b>Two seeds, because one is a lucky draw.</b> §9a records a previous batch's 25 → 25 result
    /// holding on one seed and reading 25 → 15 on another. Every assertion below runs over both.
    /// </summary>
    public class SettlementMoraleTests : ContentTestBase
    {
        public SettlementMoraleTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>A week, as <see cref="SettlementFoodTests"/> uses. Every measurement this test is built
        /// on is flat by day three; the rest of the week is there so a slow slide has somewhere to show up.
        /// It is also the whole cost of this test — a watched settlement of twenty-five is minutes of real
        /// time per day simulated — so it is the number to cut first if the suite gets too slow.</summary>
        private const int Days = 7;

        private const int BandSize = 25;

        /// <summary>A third is the line §9a drew — "a settlement with nothing wrong with it does not lose a
        /// third of its people to its own citizens" — and is what the measured before-state crossed on every
        /// seed.</summary>
        private const float MaxLossFraction = 1f / 3f;

        [Theory]
        [InlineData("morale-a", true)]
        [InlineData("morale-a", false)]
        [InlineData("morale-b", true)]
        [InlineData("morale-b", false)]
        public void A_founded_settlement_can_reach_recreation_and_does_not_beat_itself_to_death(string seed, bool watched)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed,
                subdivisionOverride: 3, soloStart: true, bandSize: BandSize);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            if (watched) GodCommands.OpenSettlement(settlement.tile);

            // Held from tick 0: Settlement.PruneDeadCitizens drops the dead off the roster, so the roster
            // alone cannot say how many founders are left.
            List<Pawn> founders = settlement.Citizens.ToList();
            Assert.NotEmpty(founders);

            var worstJoyPerDay = new List<float>();
            for (int day = 0; day < Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                worstJoyPerDay.Add(MeanJoy(founders));
            }

            List<Pawn> alive = founders.Where(p => !p.Dead).ToList();
            Assert.NotEmpty(alive);

            // (1) Recreation is reachable at all. Before this lane the mean sat at 0.00 from day two onward
            // in every run, watched and unwatched — the need's Empty band, and -20 mood points a head.
            float bestMean = worstJoyPerDay.Max();
            Assert.True(bestMean > Need_Joy.ThreshVeryLow,
                "across " + Days + " days the settlement's mean recreation never once rose out of the worst band (peak "
                + bestMean.ToString("F2") + ") — nothing in this game can raise Need_Joy");

            // (2) And it is not a one-off windfall on day one being spent down: it is still reachable at the
            // end of the run, which is the thing per-kind tolerance is capable of taking away.
            Assert.True(worstJoyPerDay[Days - 1] > Need_Joy.ThreshVeryLow,
                "recreation was reachable early and had decayed back to the worst band by day " + Days
                + " (" + worstJoyPerDay[Days - 1].ToString("F2") + ") — tolerance is outrunning every source of it");

            // (3) Nobody is left pinned at the very bottom of the need for the whole run. Asserted on the
            // population rather than on an individual: any one citizen may be between breaks at any moment.
            int starved = alive.Count(p => (p.needs?.joy?.CurCategory ?? JoyCategory.Extreme) == JoyCategory.Empty);
            Assert.True(starved * 2 < alive.Count,
                starved + " of " + alive.Count + " citizens are at empty recreation at once");

            // (4) The settlement does not beat itself to death. Deliberately narrow, and narrower than it
            // first looked: it counts citizens killed *by another pawn*
            // (Pawn_HealthTracker.KilledByAnotherPawn), not every violent death. Those are not the same
            // thing here and reading them as the same is what §9a did — a miner crushed by the roof it just
            // mined out from under itself dies of Blunt damage with no instigator, and Blunt's own
            // deathMessage is "{0} has been beaten to death", so the whole first week read as murder. That
            // is a real and separate defect (Building.RoofCollapseUtility deals 100 Blunt under thick rock),
            // it is not this lane's, and a test that counted it would go red for a reason it cannot explain.
            int murdered = founders.Count(p => p.health.KilledByAnotherPawn);
            Assert.True(murdered < founders.Count * MaxLossFraction,
                murdered + " of " + founders.Count + " founders were killed by another citizen in " + Days
                + " days, with no hostile pawn ever reaching the map — the settlement is killing itself");

            // (5) And the thing §9a actually measured: NeedJoy was the single largest term in the whole
            // settlement's mood ledger, at the full −20 a head. Asserted as "no longer pinned at the worst
            // stage", not as a figure — the stage boundaries are content and the mood values are tuned.
            ThoughtDef needJoy = DefDatabase<ThoughtDef>.GetNamed("NeedJoy");
            float worstStage = needJoy.stages[0].baseMoodEffect;
            float meanNeedJoy = alive.Average(p => MoodFrom(p, needJoy));
            Assert.True(meanNeedJoy > worstStage / 2f,
                "mean recreation mood is " + meanNeedJoy.ToString("F1") + " points a head against a worst stage of "
                + worstStage.ToString("F1") + " — the settlement is still recreation-starved");
        }

        /// <summary>This pawn's current mood contribution from one thought, however many copies of it stack.</summary>
        private static float MoodFrom(Pawn pawn, ThoughtDef def)
        {
            ThoughtHandler? thoughts = pawn.needs?.mood?.thoughts;
            if (thoughts == null) return 0f;
            var groups = new List<Thought>();
            thoughts.GetDistinctMoodThoughtGroups(groups);
            float total = 0f;
            foreach (Thought group in groups)
            {
                if (group.def == def) total += thoughts.MoodOffsetOfGroup(group);
            }
            return total;
        }

        /// <summary>
        /// The unwatched half of the same question, stated on its own because it is the half every previous
        /// map-based fix reproduced a defect in: a settlement nobody has opened has no map, and every
        /// recreation activity in this port is a Job on a map.
        /// </summary>
        [Theory]
        [InlineData("morale-a")]
        [InlineData("morale-b")]
        public void A_settlement_nobody_is_watching_still_gets_recreation(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed,
                subdivisionOverride: 3, soloStart: true, bandSize: BandSize);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            List<Pawn> founders = settlement.Citizens.ToList();

            Assert.Null(settlement.InteriorMap);
            Assert.All(founders, p => Assert.False(p.Spawned));

            for (int i = 0; i < GenDate.TicksPerDay * Days; i++) game.TickManager.DoSingleTick();

            List<Pawn> alive = founders.Where(p => !p.Dead).ToList();
            Assert.NotEmpty(alive);
            Assert.Null(settlement.InteriorMap);

            Assert.True(MeanJoy(founders) > Need_Joy.ThreshVeryLow,
                "after " + Days + " days an unwatched settlement's mean recreation is "
                + MeanJoy(founders).ToString("F2") + " — the recreation path is map-only, exactly as the food path was");

            // And nobody was beaten up in a world that does not exist. MentalState_SocialFighting used to
            // swing a melee verb out of its own tick at range zero with no map at all, so this column carried
            // Pain thoughts and lost three to five of twenty-five founders on a settlement that had never
            // been generated. An injury is the sharpest available signal: nothing else off a map inflicts one.
            foreach (Pawn p in founders)
            {
                Assert.DoesNotContain(p.health.hediffSet.hediffs, h => h is Hediff_Injury);
            }
        }

        private static float MeanJoy(IEnumerable<Pawn> founders)
        {
            List<Pawn> alive = founders.Where(p => !p.Dead).ToList();
            return alive.Count == 0 ? 0f : alive.Average(p => p.needs?.joy?.CurLevelPercentage ?? 0f);
        }
    }
}
