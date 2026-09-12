using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>Scribe holder for round-tripping a pawn mid-recreation.</summary>
    public class JoyHolder : IExposable
    {
        public List<Pawn>? pawns;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    /// <summary>
    /// Recreation, end to end (system 9: AI — joy). The gap this covers is the one
    /// <c>docs/WORK-REGISTER.md</c> §9a named: <c>Need_Joy</c>, its per-kind tolerance model and its mood
    /// thought were all built and green, and <c>GainJoy</c> had exactly one caller in the whole of
    /// <c>src/</c> — <c>CompDrug</c> — so the only recreation available to a citizen of this civilization was
    /// narcotics and the need sat at its worst stage, −20 mood, on everybody from day two onward for ever.
    /// <para/>
    /// Everything unsourced is asserted as a band, a trend or an ordering, never as the literal constant the
    /// content is explicit about not being able to source.
    /// </summary>
    public class JoyTests : ContentTestBase
    {
        public JoyTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 16) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static JoyKindDef Kind(string defName) => DefDatabase<JoyKindDef>.GetNamed(defName);

        // ---- content: the givers exist, and they are wired to drivers that can pay out ----

        [Fact]
        public void Content_ships_at_least_one_recreation_that_needs_nothing_built()
        {
            IReadOnlyList<JoyGiverDef> givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;
            Assert.NotEmpty(givers);
            // A band that has just founded a settlement owns a knife and some rations. If every recreation
            // in content needed furniture, day two would be exactly as it was before this module existed.
            Assert.Contains(givers, g => g.canDoWithoutMap);
        }

        [Fact]
        public void Every_joy_giver_names_a_job_that_is_actually_a_recreation_job()
        {
            foreach (JoyGiverDef giver in DefDatabase<JoyGiverDef>.AllDefsListForReading)
            {
                Assert.NotNull(giver.jobDef);
                Assert.NotNull(giver.joyKind);
                Assert.True(typeof(JobDriver_Joy).IsAssignableFrom(giver.jobDef.driverClass),
                    giver.defName + " names job " + giver.jobDef.defName + ", whose driver is not a JobDriver_Joy");

                // The job → giver lookup JoyUtility does in place of RimWorld's JobDef.joyKind is only total
                // if no two givers claim the same job.
                Assert.Same(giver, JoyUtility.GiverForJob(giver.jobDef));
            }
        }

        [Fact]
        public void More_than_one_kind_of_recreation_is_reachable_without_a_building()
        {
            // One kind is not enough: tolerance is per kind, so a population with a single reachable kind
            // goes bored of it and the need collapses again a few days later.
            var kinds = DefDatabase<JoyGiverDef>.AllDefsListForReading
                .Where(g => g.canDoWithoutMap)
                .Select(g => g.joyKind)
                .Distinct()
                .ToList();
            Assert.True(kinds.Count >= 2, "recreation reachable with nothing built covers only " + kinds.Count + " kind(s)");
        }

        // ---- the think tree tier ----

        [Fact]
        public void A_recreation_starved_citizen_on_a_map_is_given_a_recreation_job()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnHuman(map, new IntVec3(8, 0, 8));
            SpawnHuman(map, new IntVec3(9, 0, 8), "Companion");
            p.needs.joy!.CurLevel = 0f;

            p.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            p.jobs.TryFindAndStartJob();

            Assert.NotNull(p.jobs.curJob);
            Assert.NotNull(JoyUtility.GiverForJob(p.jobs.curJob!.def));
        }

        [Fact]
        public void A_citizen_with_full_recreation_is_not_given_one()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnHuman(map, new IntVec3(8, 0, 8));
            p.needs.joy!.CurLevel = 1f;
            p.needs.food!.CurLevel = p.needs.food.MaxLevel;
            p.needs.rest!.CurLevel = 1f;

            p.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            p.jobs.TryFindAndStartJob();

            Job? job = p.jobs.curJob;
            Assert.True(job == null || JoyUtility.GiverForJob(job.def) == null,
                "a citizen whose recreation is full took a recreation job anyway");
        }

        [Fact]
        public void A_citizen_left_alone_on_a_map_actually_raises_its_own_recreation()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnHuman(map, new IntVec3(8, 0, 8));
            Pawn friend = SpawnHuman(map, new IntVec3(9, 0, 8), "Companion");
            p.needs.joy!.CurLevel = 0f;
            float before = p.needs.joy.CurLevel;

            RunTicks(6000, p, friend);

            Assert.True(p.needs.joy.CurLevel > before,
                "a citizen with nothing to do and nothing wrong with it never once raised its own recreation");
        }

        // ---- the payout arithmetic ----

        [Fact]
        public void A_full_session_takes_a_starved_citizen_out_of_the_worst_band()
        {
            Pawn p = NewHuman();
            JoyGiverDef giver = DefDatabase<JoyGiverDef>.AllDefsListForReading.First(g => g.canDoWithoutMap);
            p.needs.joy!.CurLevel = 0f;

            for (int i = 0; i < giver.joyDuration; i++)
            {
                if (JoyUtility.JoyTickCheckEnd(p, giver.joyKind, giver.joyGainRate)) break;
            }

            Assert.True(p.needs.joy.CurCategory > JoyCategory.Low,
                "a whole session of recreation left the citizen still in the " + p.needs.joy.CurCategory + " band");
        }

        [Fact]
        public void Repeating_one_kind_pays_less_the_second_time()
        {
            Pawn p = NewHuman();
            JoyKindDef social = Kind("Social");

            p.needs.joy!.CurLevel = 0f;
            JoyUtility.JoyTickCheckEnd(p, social, 1f, 2000);
            float first = p.needs.joy.CurLevel;

            p.needs.joy.CurLevel = 0f;
            JoyUtility.JoyTickCheckEnd(p, social, 1f, 2000);
            float second = p.needs.joy.CurLevel;

            Assert.True(second < first, "tolerance did not damp a repeat of the same kind");
            Assert.True(p.needs.joy.tolerances[social] > 0f);
        }

        /// <summary>
        /// The constraint <see cref="JoyToleranceSet.ToleranceDecayPerInterval"/>'s own doc derives, asserted
        /// as behaviour rather than as the literal: a citizen who keeps taking recreation must not end up
        /// permanently bored of it. Below the break-even point the old 0.0003 sat at, tolerance ratchets
        /// upward for ever and no joy source of any size can hold the need up.
        /// </summary>
        [Fact]
        public void A_citizen_taking_recreation_for_a_fortnight_does_not_end_bored_of_everything()
        {
            Pawn p = NewHuman();
            Need_Joy joy = p.needs.joy!;
            joy.CurLevel = 0.5f;

            // The real path, not a hand-rolled one: NeedInterval both decays the need and, for a citizen
            // with no map, takes its break (AbstractRecreation).
            const int Days = 14;
            const int IntervalsPerDay = GenDate.TicksPerDay / Need.IntervalTicks;
            var daily = new List<float>();
            for (int interval = 0; interval < Days * IntervalsPerDay; interval++)
            {
                joy.NeedInterval();
                if (interval % IntervalsPerDay == IntervalsPerDay - 1) daily.Add(joy.CurLevel);
            }

            Assert.NotNull(AbstractRecreation.BestPlacelessGiver(joy));
            Assert.True(joy.CurCategory > JoyCategory.VeryLow,
                "after a fortnight of taking every break it could, the citizen is at " + joy.CurCategory);

            // And it is not a slow slide either: the last few days are no worse than the first few.
            Assert.True(daily.Skip(Days - 4).Average() > daily.Take(4).Average() * 0.5f,
                "recreation is decaying away over the fortnight: "
                + string.Join(", ", daily.Select(v => v.ToString("F2"))));
        }

        // ---- off the map ----

        [Fact]
        public void A_citizen_with_no_map_still_gets_recreation()
        {
            Pawn p = NewHuman();
            Assert.False(p.Spawned);
            p.needs.joy!.CurLevel = 0f;

            RunTicks(Need.IntervalTicks * 4, p);

            Assert.True(p.needs.joy.CurLevel > 0f,
                "an unspawned citizen — every citizen of every settlement nobody has opened — got no recreation at all");
        }

        [Fact]
        public void A_citizen_on_a_map_is_not_handed_abstract_recreation_on_top()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnHuman(map, new IntVec3(8, 0, 8));
            Need_Joy joy = p.needs.joy!;
            joy.CurLevel = 0f;

            AbstractRecreation.JoyInterval(p, joy);

            Assert.Equal(0f, joy.CurLevel);
        }

        [Fact]
        public void Abstract_recreation_never_draws_from_the_random_stream()
        {
            Pawn p = NewHuman();
            p.needs.joy!.CurLevel = 0f;
            Rand.Current = new RandomStream(1234);
            uint before = Rand.Current.Iterations;

            for (int i = 0; i < 50; i++) AbstractRecreation.JoyInterval(p, p.needs.joy);

            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void Abstract_recreation_rotates_away_from_a_kind_it_is_tired_of()
        {
            Pawn p = NewHuman();
            Need_Joy joy = p.needs.joy!;
            var kindsUsed = new HashSet<JoyKindDef>();

            for (int i = 0; i < 40; i++)
            {
                joy.CurLevel = 0f;
                JoyGiverDef? giver = AbstractRecreation.BestPlacelessGiver(joy);
                if (giver == null) break;
                kindsUsed.Add(giver.joyKind);
                AbstractRecreation.JoyInterval(p, joy);
            }

            Assert.True(kindsUsed.Count >= 2,
                "an abstract citizen only ever took one kind of recreation and so would go bored of it");
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trips_recreation_and_its_per_kind_tolerances()
        {
            Pawn p = NewHuman();
            JoyKindDef social = Kind("Social");
            JoyKindDef meditative = Kind("Meditative");
            p.needs.joy!.CurLevel = 0f;
            JoyUtility.JoyTickCheckEnd(p, social, 1f, 1500);
            JoyUtility.JoyTickCheckEnd(p, meditative, 1f, 500);

            float levelBefore = p.needs.joy.CurLevel;
            float socialBefore = p.needs.joy.tolerances[social];
            float meditativeBefore = p.needs.joy.tolerances[meditative];
            Assert.True(socialBefore > 0f && meditativeBefore > 0f);

            var holder = new JoyHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            JoyHolder loaded = Scribe.Load<JoyHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Need_Joy joy = loaded.pawns![0].needs.joy!;
            Assert.Equal(levelBefore, joy.CurLevel, 4);
            Assert.Equal(socialBefore, joy.tolerances[social], 4);
            Assert.Equal(meditativeBefore, joy.tolerances[meditative], 4);
        }
    }
}
