using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The three properties <c>tools/bench --suite storyteller</c> reads the threat curve through. That suite
    /// is an instrument, and an instrument is only worth its output if the things it assumes about the core
    /// are actually true — so each of them is pinned here rather than in the bench, where nothing runs on a
    /// push.
    ///
    /// <list type="number">
    /// <item><b>The curve decomposes.</b> The suite prints every multiplier in
    /// <see cref="StorytellerUtility.DefaultThreatPointsNow"/> as its own column. That is only honest if the
    /// columns multiply back to the method's own answer, so the same reconstruction is asserted here against
    /// the method itself.</item>
    /// <item><b>A success leaves exactly one chronicle entry, as the tail, before the event.</b> The suite has
    /// to report whether a worker returned true, and <see cref="Storyteller.IncidentFired"/> does not carry
    /// the answer — so it reads the chronicle's tail inside the handler. Reorder <see cref="Storyteller.TryFire"/>
    /// and that column starts lying silently.</item>
    /// <item><b>Which shipped incidents do nothing.</b> Three shipped workers return true and have no effect.
    /// The suite detects them structurally, from the IL of the worker's own effect method; this pins both
    /// that the detection works and which incidents it currently catches, so the day one of them gets a real
    /// worker is a day somebody is told. That day has already come once: this said <i>four</i> until
    /// <c>WandererJoin</c> gained a real worker, and this test is how the change announced itself.</item>
    /// </list>
    /// </summary>
    public class ThreatCurveReadabilityTests : ContentTestBase
    {
        public ThreatCurveReadabilityTests(CoreContentFixture content) : base(content)
        {
        }

        // ---- helpers ----

        private static global::SimWorld.Director.Storyteller NewStoryteller(
            string storytellerName = "Cassandra_Classic", string difficultyName = "Medium")
        {
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed(storytellerName),
                DefDatabase<DifficultyDef>.GetNamed(difficultyName));
            Find.Storyteller = storyteller;
            return storyteller;
        }

        private static CivilizationTarget NewTarget(
            global::SimWorld.Director.Storyteller storyteller, int pawnCount, float wealth)
        {
            var target = new CivilizationTarget(storyteller) { PlayerWealthForStoryteller = wealth };
            for (int i = 0; i < pawnCount; i++) target.pawns.Add(NewHuman("Pawn" + i));
            return target;
        }

        /// <summary>
        /// The decomposition the bench prints, recomputed here in exactly the order
        /// <see cref="StorytellerUtility.DefaultThreatPointsNow"/> applies its terms — same start value, same
        /// order of additions, same order of multiplications. Anything else would be comparing two different
        /// sums of floats and would need a tolerance to pass, which would defeat the point.
        /// </summary>
        private static (float PreClamp, float BaseFromWealth, float ColonistSum) Decompose(IIncidentTarget target)
        {
            global::SimWorld.Director.Storyteller storyteller = Find.Storyteller;

            float wealth = target.PlayerWealthForStoryteller;
            float baseFromWealth = StorytellerUtility.PointsPerWealthCurve.Evaluate(wealth);
            float perColonist = StorytellerUtility.PointsPerColonistByWealthCurve.Evaluate(wealth);

            float points = baseFromWealth;
            foreach (Pawn pawn in target.PlayerPawnsForStoryteller)
            {
                float factor = pawn.Dead ? 0f : GenMath.Lerp(0.5f, 1f, pawn.health.summaryHealth.SummaryHealthPercent);
                points += perColonist * factor;
            }
            float colonistSum = points - baseFromWealth;

            if (storyteller.difficulty != null) points *= storyteller.difficulty.threatScale;
            points *= storyteller.adaptation.TotalThreatPointsFactor(storyteller.difficulty);
            points *= global::SimWorld.Research.EraTransitionUtility.CurrentEraThreatPointsFactor();
            if (storyteller.def != null)
            {
                points *= storyteller.def.pointsFactorFromDaysPassed.Evaluate(GenDate.DaysPassedAt(Find.TickManager.TicksGame));
            }

            return (points, baseFromWealth, colonistSum);
        }

        // ---- 1. the curve decomposes ----

        [Fact]
        public void The_documented_factors_multiply_back_to_the_engines_own_answer()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 25, wealth: 0f);

            (float preClamp, _, _) = Decompose(target);
            float expected = GenMath.Clamp(preClamp, StorytellerUtility.MinThreatPoints, StorytellerUtility.MaxThreatPoints);

            Assert.Equal(expected, StorytellerUtility.DefaultThreatPointsNow(target));
        }

        [Theory]
        [InlineData(20, 0f, 0)]
        [InlineData(25, 20000f, 12000)]
        [InlineData(40, 500000f, 240000)]
        public void The_decomposition_holds_across_band_wealth_and_time(int band, float wealth, int ticks)
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller, band, wealth);
            Find.TickManager.DebugSetTicksGame(ticks);

            (float preClamp, float baseFromWealth, float colonistSum) = Decompose(target);
            float expected = GenMath.Clamp(preClamp, StorytellerUtility.MinThreatPoints, StorytellerUtility.MaxThreatPoints);

            Assert.Equal(expected, StorytellerUtility.DefaultThreatPointsNow(target));

            // Both halves of the sum are separately non-negative, which is what lets the bench print them as
            // independent columns rather than as one number and a remainder.
            Assert.True(baseFromWealth >= 0f);
            Assert.True(colonistSum >= 0f);
        }

        [Fact]
        public void A_civilization_with_nobody_and_nothing_sits_on_the_floor_clamp()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 0, wealth: 0f);

            (float preClamp, _, _) = Decompose(target);

            // The reading the bench's `clamped?` column exists to make visible: below the floor, every factor
            // above it is multiplying nothing and the curve has stopped being a curve.
            Assert.True(preClamp < StorytellerUtility.MinThreatPoints);
            Assert.Equal(StorytellerUtility.MinThreatPoints, StorytellerUtility.DefaultThreatPointsNow(target));
        }

        [Fact]
        public void The_floor_clamp_hides_a_peaceful_difficulty_entirely()
        {
            // Not a tuning claim, a readability one: Peaceful's threatScale of 0 makes every factor irrelevant
            // and the answer is the floor, so a table that showed only `points` could not tell a peaceful
            // civilization from a besieged one whose numbers happened to be small. IncidentWorker's own doc
            // makes the same observation about why allowBigThreats has to exist at all.
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller(difficultyName: "Peaceful");
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 25, wealth: 0f);

            (float preClamp, _, _) = Decompose(target);

            Assert.Equal(0f, preClamp);
            Assert.Equal(StorytellerUtility.MinThreatPoints, StorytellerUtility.DefaultThreatPointsNow(target));
        }

        // ---- 2. a success is readable off the chronicle tail ----

        [Fact]
        public void A_successful_firing_leaves_its_own_entry_as_the_chronicle_tail_before_the_event()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 3, wealth: 0f);

            IncidentDef def = TestDef(typeof(IncidentWorker_AlwaysSucceeds));
            ChronicleEntry? tailAtEvent = null;
            int countAtEvent = -1;
            storyteller.IncidentFired += _ =>
            {
                countAtEvent = storyteller.Chronicle.Count;
                tailAtEvent = countAtEvent > 0 ? storyteller.Chronicle[countAtEvent - 1] : null;
            };

            Assert.True(storyteller.TryFire(new FiringIncident(def, null, new IncidentParms { target = target, points = 100f })));

            Assert.Equal(1, countAtEvent);
            Assert.NotNull(tailAtEvent);
            Assert.Equal(def.defName, tailAtEvent!.incidentDefName);
            Assert.Equal(Find.TickManager.TicksGame, tailAtEvent.tick);
        }

        [Fact]
        public void A_refused_firing_leaves_the_chronicle_exactly_as_it_was()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 3, wealth: 0f);

            IncidentDef succeeds = TestDef(typeof(IncidentWorker_AlwaysSucceeds));
            IncidentDef refuses = TestDef(typeof(IncidentWorker_AlwaysRefuses));

            Assert.True(storyteller.TryFire(new FiringIncident(succeeds, null, new IncidentParms { target = target })));
            ChronicleEntry tailAfterSuccess = storyteller.Chronicle[storyteller.Chronicle.Count - 1];

            ChronicleEntry? tailAtEvent = null;
            storyteller.IncidentFired += _ =>
                tailAtEvent = storyteller.Chronicle.Count > 0 ? storyteller.Chronicle[storyteller.Chronicle.Count - 1] : null;

            Assert.False(storyteller.TryFire(new FiringIncident(refuses, null, new IncidentParms { target = target })));

            // The tail the handler sees is the *previous* success, by reference — which is exactly how the
            // bench tells a refusal from a success without the event carrying a result.
            Assert.Same(tailAfterSuccess, tailAtEvent);
            Assert.Single(storyteller.Chronicle);
        }

        // ---- 3. which shipped incidents do nothing ----

        /// <summary>
        /// The same structural test the bench uses: the most derived <c>TryExecuteWorker</c> is a bare
        /// constant-return and nothing else.
        /// </summary>
        private static bool HasNoEffect(IncidentWorker worker)
        {
            if (worker is IncidentWorker_Placeholder) return true;

            MethodInfo? method = worker.GetType().GetMethod(
                "TryExecuteWorker", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            byte[]? il = method?.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;

            int start = il.Length > 0 && il[0] == 0x00 ? 1 : 0;
            if (il.Length - start != 2) return false;
            if (il[start + 1] != 0x2A) return false;
            return il[start] == 0x17 || il[start] == 0x16;
        }

        [Fact]
        public void The_structural_stub_test_separates_a_placeholder_from_a_real_worker()
        {
            Assert.True(HasNoEffect(new IncidentWorker_Placeholder()));
            Assert.True(HasNoEffect(new IncidentWorker_AlwaysSucceeds()));

            // A worker whose body does anything at all — even only reading parms — must not be called a stub,
            // or the bench would under-report the pressure a player is actually under.
            Assert.False(HasNoEffect(new IncidentWorker_TestThreat()));
            Assert.False(HasNoEffect(new IncidentWorker_RaidEnemy()));
            Assert.False(HasNoEffect(new IncidentWorker_Disease()));
            Assert.False(HasNoEffect(new IncidentWorker_Earthquake()));
        }

        [Fact]
        public void The_shipped_incidents_that_fire_and_do_nothing_are_the_recorded_three()
        {
            Assert.Empty(Content.Result.Errors);

            var stubs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (IncidentDef def in DefDatabase<IncidentDef>.AllDefsListForReading)
            {
                if (HasNoEffect(def.Worker)) stubs.Add(def.defName);
            }

            // An inventory, not a tuning constant: these three incidents return true and change nothing, so a
            // reading that counted them as pressure would be wrong. If this fails because an incident gained a
            // real worker, that is good news — delete it from this list and from the finding in the bench's
            // own report. If it fails because a fourth stub was added, that is the thing this test exists to
            // catch.
            //
            // It has already earned its keep once. It listed WandererJoin and failed the moment that lane's
            // real worker merged — two lanes written against the same base, neither wrong, and this is what
            // noticed. The list is deliberately explicit rather than a count for exactly that reason: a count
            // would have stayed green if one stub had been implemented and another added.
            Assert.Equal(
                new[] { "Eclipse", "ToxicFallout", "VisitorGroup" },
                stubs.ToArray());
        }

        [Fact]
        public void No_incident_that_does_nothing_is_in_a_threat_category()
        {
            foreach (IncidentDef def in DefDatabase<IncidentDef>.AllDefsListForReading)
            {
                if (!HasNoEffect(def.Worker)) continue;

                bool threat = ReferenceEquals(def.category, IncidentCategoryDefOf.ThreatBig)
                    || ReferenceEquals(def.category, IncidentCategoryDefOf.ThreatSmall);

                // A stub in a threat category would be the worst case of all: it would draw threat points,
                // spend a refire timer, occupy the storyteller's threat cadence, and do nothing with any of it.
                Assert.False(threat, def.defName + " is a threat with a worker that does nothing.");
            }
        }

        private static IncidentDef TestDef(Type workerClass) => new IncidentDef
        {
            defName = "Test_" + Guid.NewGuid().ToString("N"),
            category = IncidentCategoryDefOf.Misc,
            workerClass = workerClass,
        };
    }

    /// <summary>A stub by construction, so the structural stub test has a known positive that is not shipped
    /// content. Its own file's worth of doc lives on <see cref="ThreatCurveReadabilityTests"/>.</summary>
    public sealed class IncidentWorker_AlwaysSucceeds : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms) => true;
    }

    /// <summary>Refuses every firing, so a test can watch what <see cref="Storyteller.TryFire"/> does — and
    /// does not do — on a false return.</summary>
    public sealed class IncidentWorker_AlwaysRefuses : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms) => false;
    }
}
