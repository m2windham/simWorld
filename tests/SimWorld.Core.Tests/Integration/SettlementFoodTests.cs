using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>A test that watches the game rather than a module.</b> It founds a settlement the ordinary way —
    /// <see cref="Game.NewGame"/> on the tribal scenario, opened through the god seam — and ticks a week of
    /// game time with nothing called by hand, then asks the one question a suite of module tests cannot:
    /// <i>did these people eat?</i>
    ///
    /// <para/><b>Why it exists.</b> A generated settlement used to starve to death in its first week: mean
    /// nutrition across twenty-five founders reached zero inside a single day and never recovered, while
    /// every needs, food, work-giver, hauling and cooking test in the suite passed. Four separate links of
    /// the food chain were broken at once and each of them was invisible from inside its own module:
    /// <list type="number">
    /// <item><b>Nothing edible existed.</b> Map generation scattered <c>WildPlant</c>, which has no
    /// harvest yield; the scenarios ship no food; and the two systems that could have produced some were both
    /// dormant. Nutrition on a freshly generated interior was exactly zero.</item>
    /// <item><b>Nothing produced food.</b> No growing zone had ever been created by anything in <c>src/</c>,
    /// so sowing could not happen; no stove could be built (no Blueprint content existed for one) and no
    /// cooking bill was ever queued; and hunting — armed, gated and wired — had no prey, because nothing
    /// spawns a wild animal on an interior map.</item>
    /// <item><b>A citizen who found food could not fill up.</b> <c>JobDriver_Ingest</c> ate one unit per
    /// job: 0.05 nutrition against a 1.0 stomach and a 1.6-a-day fall.</item>
    /// <item>The fall rate itself was fine, and is left alone.</item>
    /// </list>
    ///
    /// <para/><b>What this asserts, and what it deliberately does not.</b> It asserts the food economy
    /// closes: that the land a settlement is founded on carries food, that its citizens eat their fill, that
    /// nobody is driven to starvation, and that the larder is fuller at the end of the week than the
    /// settlement burns in a day. It does <b>not</b> assert that the population is untouched, because it is
    /// not, and the causes measured at the time of writing are not hunger: with recreation held full by hand
    /// the same settlement still loses citizens, and with food fixed the starvation signal is gone while the
    /// deaths are not. Asserting survival here would be asserting that four other systems are healthy, and
    /// would go red for reasons this test cannot explain. See <c>docs/WORK-REGISTER.md</c> §9.
    /// </summary>
    public class SettlementFoodTests : ContentTestBase
    {
        public SettlementFoodTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Days = 7;
        private const int SamplesPerDay = 2;

        private sealed class Sample
        {
            public int Alive;
            public float MeanFood;
            public int Starving;
            public float NutritionOnMap;
        }

        private static HediffDef Malnutrition => DefDatabase<HediffDef>.GetNamed("Malnutrition");

        [Fact]
        public void A_founded_settlement_feeds_itself_for_a_week()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "settlement-food",
                subdivisionOverride: 3, soloStart: true, bandSize: 25);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;

            // Link 1, before a single tick: the ground a settlement is founded on has to bear food. This was
            // zero on every generated map in the project's history.
            Assert.True(StandingWildNutrition(map) > 0f,
                "a freshly generated interior carried nothing edible and nothing that could be harvested into food");

            var samples = new List<Sample>();
            for (int half = 0; half < Days * SamplesPerDay; half++)
            {
                for (int i = 0; i < GenDate.TicksPerDay / SamplesPerDay; i++) game.TickManager.DoSingleTick();
                samples.Add(Measure(settlement));
            }

            List<Pawn> alive = settlement.Citizens.Where(p => !p.Dead).ToList();
            Assert.NotEmpty(alive);

            // Link 3, measured on the whole town rather than on one pawn at a bench: a citizen who can only
            // take a mouthful per job loses ground to its own hunger for ever, so the town's mean nutrition
            // slides to zero and stays there. It has to come back up, repeatedly.
            float bestMean = samples.Max(s => s.MeanFood);
            Assert.True(bestMean > new Need_Food(alive[0]).PercentageThreshHungry,
                "the settlement's mean nutrition never once rose back above hungry across a whole week (peak "
                + bestMean.ToString("F2") + ") — its citizens are eating, but never getting full");

            // Nobody is driven to the floor. A handful of individuals may be caught between meals on any one
            // sample; a town where a quarter of the citizens are at zero nutrition at once is starving.
            foreach (Sample s in samples)
            {
                Assert.True(s.Starving * 4 < s.Alive + 4,
                    "at one sample " + s.Starving + " of " + s.Alive + " citizens were at zero nutrition at once");
            }

            // "Does not lose citizens to hunger", stated the way the simulation states it: Malnutrition is the
            // hediff a starving pawn accrues and it is lethal at severity 1. Nobody may be far along it —
            // alive, or lying dead on the map.
            foreach (Pawn pawn in alive) AssertNotDyingOfHunger(pawn);
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is Corpse corpse && corpse.InnerPawn is Pawn dead && dead.RaceProps.Humanlike)
                {
                    AssertNotDyingOfHunger(dead);
                }
            }

            // Link 2: the larder is not a one-off windfall being eaten down. After a week of twenty-five
            // people eating, what is physically on the map still covers more than a day's burn — the wild
            // food regrows, the fields the settlement painted are standing in it, and both are being worked.
            float burnedPerDay = HuntingInitiative.NutritionWanted(settlement, map) / HuntingTuning.DaysOfFoodWanted;
            float onMap = HuntingInitiative.NutritionAvailable(null, map);
            Assert.True(onMap > burnedPerDay,
                "after a week the map held " + onMap.ToString("F1") + " nutrition against a daily burn of "
                + burnedPerDay.ToString("F1") + " — the settlement is living off a windfall, not off an economy");

            // And the settlement did the two things nothing in src/ had ever done: it painted a field, and it
            // asked for the wild food around it.
            Assert.NotNull(FarmingInitiative.FieldOf(map));
            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount > 0);
        }

        private static void AssertNotDyingOfHunger(Pawn pawn)
        {
            Hediff? malnutrition = pawn.health.hediffSet.GetFirstHediffOfDef(Malnutrition);
            if (malnutrition == null) return;

            // Well below lethalSeverity 1: a pawn briefly caught between meals picks up a trace of this and
            // sheds it again at the same pace. Anything approaching lethal is a citizen being lost to hunger.
            Assert.True(malnutrition.Severity < 0.5f,
                pawn.Label + " carries malnutrition at severity " + malnutrition.Severity.ToString("F2")
                + " (lethal at 1) after a week in a settlement that is supposed to be feeding itself");
        }

        private static Sample Measure(Settlement settlement)
        {
            List<Pawn> alive = settlement.Citizens.Where(p => !p.Dead).ToList();
            SimWorld.Map.Map map = settlement.InteriorMap!;
            return new Sample
            {
                Alive = alive.Count,
                MeanFood = alive.Count == 0 ? 0f : alive.Average(p => p.needs?.food?.CurLevelPercentage ?? 0f),
                Starving = alive.Count(p => p.needs?.food?.Starving ?? false),
                NutritionOnMap = HuntingInitiative.NutritionAvailable(null, map),
            };
        }

        /// <summary>Nutrition standing on the map as unharvested wild food — the larder a band founding a
        /// settlement here can actually draw on, as opposed to what is already lying about as items.</summary>
        private static float StandingWildNutrition(SimWorld.Map.Map map)
        {
            float total = 0f;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
            {
                if (!(thing is Plant plant) || !plant.HarvestableNow) continue;
                PlantProperties props = plant.def.plant!;
                ThingDef harvested = props.harvestedThingDef!;
                if (!harvested.IsNutritionGivingIngestible) continue;
                total += props.harvestYield * harvested.ingestible!.nutrition;
            }
            return total;
        }
    }
}
