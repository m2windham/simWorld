using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="IncidentDef.pointsScaleable"/>: the opt-in that says an incident's severity is bought with
    /// <see cref="IncidentParms.points"/>.
    ///
    /// <para/><b>What was wrong.</b> <see cref="StorytellerComp_RandomMain"/> is the only place in this port
    /// that multiplies an incident's points by anything random, and it applied
    /// <see cref="StorytellerCompProperties_RandomMain.randomPointsFactorRange"/> to whatever it had just
    /// picked — the <c>if</c> RimWorld gates that multiply with had been dropped. Nothing read the flag.
    ///
    /// <para/><b>What that cost, honestly.</b> Nothing numeric yet:
    /// <see cref="StorytellerUtility.DefaultParmsNow"/> only gives points to the two threat categories, and
    /// both shipped incidents that carry points declare the flag — so every multiply that ever reached a
    /// non-zero number was one the gate allows. What it did cost was a draw from the seeded stream taken for
    /// incidents with nothing to scale, which moved every roll after it.
    /// </summary>
    public class PointsScaleableTests : ContentTestBase
    {
        public PointsScaleableTests(CoreContentFixture content) : base(content)
        {
        }

        private static IncidentDef Incident(string defName) => DefDatabase<IncidentDef>.GetNamed(defName);

        private static global::SimWorld.Director.Storyteller NewStoryteller()
        {
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Randy_Random"),
                DefDatabase<DifficultyDef>.GetNamed("Medium"));
            Find.Storyteller = storyteller;
            return storyteller;
        }

        private static CivilizationTarget NewTarget(global::SimWorld.Director.Storyteller storyteller)
        {
            var target = new CivilizationTarget(storyteller);
            for (int i = 0; i < 3; i++) target.pawns.Add(NewHuman("Pawn" + i));
            return target;
        }

        /// <summary>A RandomMain comp that fires every interval and jitters points hard enough that an
        /// ungated multiply could not be mistaken for no multiply.</summary>
        private static StorytellerComp_RandomMain AlwaysFiringThreatComp() =>
            new StorytellerComp_RandomMain
            {
                props = new StorytellerCompProperties_RandomMain
                {
                    mtbDays = 0.0001f,
                    randomPointsFactorRange = new FloatRange(3f, 4f),
                    categoryWeights = new List<IncidentCategoryEntry>
                    {
                        new IncidentCategoryEntry { category = IncidentCategoryDefOf.ThreatBig, weight = 1f },
                    },
                },
            };

        // ---- content ----

        [Fact]
        public void Only_the_incidents_that_spend_points_declare_the_flag()
        {
            // The content half, and the answer to "which incidents could this ever matter for": exactly the
            // two that are handed threat points. Weather, trade, quests and disease are not point-priced and
            // must not be jittered as if they were.
            var scaleable = DefDatabase<IncidentDef>.AllDefsListForReading.Where(d => d.pointsScaleable).ToList();

            Assert.NotEmpty(scaleable);
            Assert.All(scaleable, d => Assert.True(
                ReferenceEquals(d.category, IncidentCategoryDefOf.ThreatBig)
                || ReferenceEquals(d.category, IncidentCategoryDefOf.ThreatSmall),
                d.defName + " declares pointsScaleable but its category is never given points"));

            // Of the two, only RaidEnemy has severity to scale today: it spends the points through
            // PawnGroupMakerUtility. ManhunterPack's worker validates them and does nothing with them, so its
            // flag is content waiting on a worker rather than a second live reader.
            Assert.Contains(scaleable, d => d.workerClass == typeof(IncidentWorker_RaidEnemy));
            Assert.Equal(typeof(IncidentWorker_ThreatEvent), Incident("ManhunterPack").workerClass);
        }

        // ---- the gate ----

        [Fact]
        public void A_scaleable_incident_is_jittered_and_the_same_incident_unflagged_is_not()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller);

            // No hostile faction is registered, so RaidEnemy cannot fire and ManhunterPack is the only usable
            // ThreatBig — one incident, so what comes back is always it and the comparison is like for like.
            Find.FactionManager = new global::SimWorld.Factions.FactionManager();
            IncidentDef manhunter = Incident("ManhunterPack");
            Assert.True(manhunter.pointsScaleable);

            List<float> jittered = PointsOverIntervals(target, 12);
            float storytellerPoints = StorytellerUtility.DefaultThreatPointsNow(target);

            // The flag is flipped and put back: this is the only way to compare one incident against itself,
            // and tests that read DefDatabase.Global share a collection and never run beside each other.
            List<float> plain;
            manhunter.pointsScaleable = false;
            try
            {
                plain = PointsOverIntervals(target, 12);
            }
            finally
            {
                manhunter.pointsScaleable = true;
            }

            Assert.NotEmpty(jittered);
            Assert.NotEmpty(plain);

            // Unflagged: the storyteller's own number reaches the incident untouched.
            Assert.All(plain, p => Assert.Equal(storytellerPoints, p, 3));

            // Flagged: the range is entirely above 1, so every firing is strictly larger than it.
            Assert.All(jittered, p => Assert.True(p > storytellerPoints,
                "a pointsScaleable incident was handed " + p + ", not scaled up from " + storytellerPoints));
        }

        [Fact]
        public void An_incident_with_nothing_to_scale_costs_no_roll()
        {
            // The determinism half. A draw taken for an incident that cannot use it is not free: it moves
            // every roll after it. Asserted as "fewer draws", not as a draw count, since everything else in
            // the pick is free to change.
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller);
            Find.FactionManager = new global::SimWorld.Factions.FactionManager();
            IncidentDef manhunter = Incident("ManhunterPack");

            uint scaleableDraws = DrawsForOneFiring(target);

            uint plainDraws;
            manhunter.pointsScaleable = false;
            try
            {
                plainDraws = DrawsForOneFiring(target);
            }
            finally
            {
                manhunter.pointsScaleable = true;
            }

            Assert.True(plainDraws < scaleableDraws,
                "gating the jitter should spend one fewer draw (" + plainDraws + " vs " + scaleableDraws + ")");
        }

        private static List<float> PointsOverIntervals(CivilizationTarget target, int intervals)
        {
            StorytellerComp_RandomMain comp = AlwaysFiringThreatComp();
            Rand.Current = new RandomStream(31337);
            var points = new List<float>();
            for (int i = 1; i <= intervals; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
                foreach (FiringIncident firing in comp.MakeIntervalIncidents(target))
                {
                    points.Add(firing.parms.points);
                }
            }
            return points;
        }

        private static uint DrawsForOneFiring(CivilizationTarget target)
        {
            StorytellerComp_RandomMain comp = AlwaysFiringThreatComp();
            Rand.Current = new RandomStream(31337);
            Find.TickManager.DebugSetTicksGame(global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
            uint before = Rand.Current.Iterations;
            Assert.NotEmpty(comp.MakeIntervalIncidents(target).ToList());
            return Rand.Current.Iterations - before;
        }
    }
}
