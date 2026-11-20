using System;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="StorytellerCompProperties_OnOffCycle.minSpacingDays"/> is a floor between two incidents of the
    /// comp's category, not only a term in the cycle's MTB. Seed 12345's populated run sent two raids 0.58 days
    /// apart under Cassandra's 1.5 before this was enforced.
    /// </summary>
    public class OnOffCycleSpacingTests : ContentTestBase
    {
        public OnOffCycleSpacingTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void No_incident_of_the_category_fires_within_minSpacingDays_of_the_last_one()
        {
            var testIncident = new IncidentDef
            {
                defName = "Test_" + Guid.NewGuid().ToString("N"),
                category = IncidentCategoryDefOf.Misc,
                workerClass = typeof(IncidentWorker_Placeholder),
            };
            // A cycle that is always on and wants far more incidents than the spacing allows, so the MTB term is
            // the spacing itself and the roll hits often.
            var props = new StorytellerCompProperties_OnOffCycle
            {
                onDays = 1000f, offDays = 1f, minSpacingDays = 1.5f,
                numIncidentsRange = new FloatRange(50f, 50f), incident = testIncident,
            };
            var comp = new StorytellerComp_OnOffCycle { props = props };
            IncidentDef shippedMisc = DefDatabase<IncidentDef>.AllDefsListForReading.First(d => d.category == IncidentCategoryDefOf.Misc);
            int interval = global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks;
            int spacingTicks = (int)(props.minSpacingDays * GenDate.TicksPerDay);

            int tooSoon = 0, afterwards = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                Rand.Current = new RandomStream(seed);
                var target = new CivilizationTarget();
                int firedAt = 10 * GenDate.TicksPerDay;
                Find.TickManager.DebugSetTicksGame(firedAt);
                target.StoryState.Notify_IncidentFired(new FiringIncident(shippedMisc, null, new IncidentParms { target = target }));

                for (int t = firedAt + interval; t < firedAt + spacingTicks; t += interval)
                {
                    Find.TickManager.DebugSetTicksGame(t);
                    tooSoon += comp.MakeIntervalIncidents(target).Count();
                }
                for (int t = firedAt + spacingTicks; t < firedAt + 4 * spacingTicks; t += interval)
                {
                    Find.TickManager.DebugSetTicksGame(t);
                    afterwards += comp.MakeIntervalIncidents(target).Count();
                }
            }

            Assert.Equal(0, tooSoon);
            Assert.True(afterwards > 0, "the comp must still fire once the spacing has passed");
        }
    }
}
