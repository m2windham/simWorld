using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// Disease keying off the biomes a civilization actually lives in
    /// (<see cref="BiomeDef.diseaseMtbDays"/>). Content has always authored a mean time between diseases per
    /// biome — 30 days in a tropical swamp, 200 in a desert — and
    /// <see cref="StorytellerComp_Disease"/> read none of it: every civilization anywhere caught disease at
    /// one flat storyteller-wide rate, whose own field doc still said "SimWorld has no biomes yet".
    ///
    /// <para/>Everything here is asserted as an ordering or a ratio between two civilizations, never as a
    /// number of days: the content values are this port's own approximations and the test has no business
    /// pinning them.
    /// </summary>
    public class DiseaseBiomeTests : ContentTestBase
    {
        public DiseaseBiomeTests(CoreContentFixture content) : base(content)
        {
        }

        private static BiomeDef Biome(string defName) => DefDatabase<BiomeDef>.GetNamed(defName);

        private static global::SimWorld.Director.Storyteller Teller(string defName = "Cassandra_Classic", string difficulty = "Medium")
        {
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed(defName), DefDatabase<DifficultyDef>.GetNamed(difficulty));
            Find.Storyteller = storyteller;
            return storyteller;
        }

        private static StorytellerComp_Disease Comp(float baseMtbDays = StorytellerCompProperties_Disease.NeutralBaseMtbDays) =>
            new StorytellerComp_Disease
            {
                props = new StorytellerCompProperties_Disease
                {
                    category = IncidentCategoryDefOf.DiseaseHuman,
                    baseMtbDays = baseMtbDays,
                },
            };

        /// <summary>
        /// A civilization of one settlement per named biome, on a real (if tiny) world grid, with
        /// <see cref="Find.World"/> pointed at it — the disease comp resolves a settlement's biome through the
        /// world exactly as a running game does, so a test that faked it would prove nothing.
        /// </summary>
        private static CivilizationTarget CivilizationIn(params string[] biomeNames)
        {
            WorldGrid grid = WorldGrid.Generate(2);
            var world = new global::SimWorld.World.World(
                new WorldInfo { name = "Test", seedString = "disease-biomes", seed = 7, subdivisionLevel = 2 }, grid);
            Find.World = world;

            var settlements = new List<Settlement>();
            for (int i = 0; i < biomeNames.Length; i++)
            {
                int tileId = 3 + i * 11;
                grid.Tiles[tileId].biome = Biome(biomeNames[i]);
                var settlement = new Settlement(WorldObjectDefOf.Settlement, tileId, null, "Town" + i, 0);
                settlement.AddCitizen(NewHuman("Citizen" + i));
                world.worldObjects.Add(settlement);
                settlements.Add(settlement);
            }

            var target = new CivilizationTarget();
            target.SetSettlements(settlements);
            return target;
        }

        // ----- the rate itself -----

        [Fact]
        public void A_civilization_in_a_wetter_hotter_biome_sickens_sooner_than_one_in_a_cold_or_dry_one()
        {
            Teller();
            StorytellerComp_Disease comp = Comp();

            float swamp = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("TropicalSwamp"));
            float forest = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("TemperateForest"));
            float tundra = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("Tundra"));
            float desert = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("Desert"));

            Assert.True(swamp < forest, $"A tropical swamp ({swamp}d) should sicken sooner than a temperate forest ({forest}d).");
            Assert.True(forest < tundra, $"A temperate forest ({forest}d) should sicken sooner than tundra ({tundra}d).");
            Assert.True(tundra < desert, $"Tundra ({tundra}d) should sicken sooner than desert ({desert}d).");

            // The ordering is the content's, not this test's: assert it is the same ordering the defs declare.
            Assert.True(Biome("TropicalSwamp").diseaseMtbDays < Biome("TemperateForest").diseaseMtbDays);
            Assert.True(Biome("TemperateForest").diseaseMtbDays < Biome("Tundra").diseaseMtbDays);
            Assert.True(Biome("Tundra").diseaseMtbDays < Biome("Desert").diseaseMtbDays);
        }

        /// <summary>
        /// The civilization-scale part of the translation: RimWorld rolls per map, and a civilization is
        /// several places at once, so their risks add. Three towns in one biome catch something three times
        /// as often as one town does — asserted as the ratio, which is the model, rather than as days.
        /// </summary>
        [Fact]
        public void Risk_adds_up_across_a_civilizations_settlements()
        {
            Teller();
            StorytellerComp_Disease comp = Comp();

            float one = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("BorealForest"));
            float three = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("BorealForest", "BorealForest", "BorealForest"));

            Assert.True(three < one);
            Assert.Equal(one / 3f, three, 2);
        }

        /// <summary>A settlement in a healthy biome cannot make a sickly civilization healthier: adding it
        /// adds risk (or, at the limit, none), never subtracts any.</summary>
        [Fact]
        public void Adding_a_settlement_never_lowers_a_civilizations_disease_rate()
        {
            Teller();
            StorytellerComp_Disease comp = Comp();

            float swampAlone = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("TropicalSwamp"));
            float swampAndIceSheet = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("TropicalSwamp", "IceSheet"));

            Assert.True(swampAndIceSheet < swampAlone);
        }

        [Fact]
        public void A_target_with_no_settlements_still_gets_the_storytellers_own_flat_rate()
        {
            global::SimWorld.Director.Storyteller storyteller = Teller();
            StorytellerComp_Disease comp = Comp(baseMtbDays: 12f);

            var posed = new CivilizationTarget();
            posed.pawns.Add(NewHuman());

            float expected = 12f * (storyteller.difficulty?.diseaseIntervalFactor ?? 1f);
            Assert.Equal(expected, comp.MeanTimeBetweenDiseaseDays(posed), 3);
        }

        /// <summary>
        /// The storyteller keeps its own say: <see cref="StorytellerCompProperties_Disease.baseMtbDays"/> is
        /// now read as an appetite relative to its own declared default, so a gentler narrator stretches every
        /// biome's mean time by the same factor instead of flattening them all to one number.
        /// </summary>
        [Fact]
        public void A_gentler_storyteller_stretches_every_biomes_rate_by_the_same_factor()
        {
            Teller();
            const float gentle = 2f * StorytellerCompProperties_Disease.NeutralBaseMtbDays;

            foreach (string biome in new[] { "TropicalRainforest", "Tundra" })
            {
                float neutral = Comp().MeanTimeBetweenDiseaseDays(CivilizationIn(biome));
                float gentler = Comp(gentle).MeanTimeBetweenDiseaseDays(CivilizationIn(biome));
                Assert.Equal(neutral * 2f, gentler, 2);
            }
        }

        /// <summary>The difficulty's own factor still applies on top of the biome, unchanged — Rough's
        /// <see cref="DifficultyDef.diseaseIntervalFactor"/> shortens the biome's mean time by exactly its own
        /// ratio to Medium's, rather than the biome quietly replacing it.</summary>
        [Fact]
        public void Difficulty_still_scales_the_biomes_rate()
        {
            StorytellerComp_Disease comp = Comp();
            float mediumFactor = DefDatabase<DifficultyDef>.GetNamed("Medium").diseaseIntervalFactor;
            float roughFactor = DefDatabase<DifficultyDef>.GetNamed("Rough").diseaseIntervalFactor;
            Assert.True(roughFactor < mediumFactor, "Rough is expected to be the harsher difficulty in content.");

            Teller(difficulty: "Medium");
            float medium = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("TemperateForest"));
            Teller(difficulty: "Rough");
            float rough = comp.MeanTimeBetweenDiseaseDays(CivilizationIn("TemperateForest"));

            Assert.True(rough < medium);
            Assert.Equal(medium * (roughFactor / mediumFactor), rough, 2);
        }

        // ----- the storyteller actually firing it -----

        /// <summary>
        /// The end-to-end half: the comp really does select more disease incidents for a swamp civilization
        /// than for a tundra one over the same number of intervals, from the same seeded stream. Counted, not
        /// timed — the point is the gap between the two, which was exactly zero before the biome was read.
        /// </summary>
        [Fact]
        public void The_storyteller_fires_disease_more_often_in_the_swamp_than_on_the_tundra()
        {
            Teller();
            StorytellerComp_Disease comp = Comp();
            const int intervals = 30000;

            int swampFirings = CountFirings(comp, CivilizationIn("TropicalSwamp", "TropicalSwamp", "TropicalSwamp"), intervals, seed: 4242);
            int tundraFirings = CountFirings(comp, CivilizationIn("Tundra", "Tundra", "Tundra"), intervals, seed: 4242);

            Assert.True(swampFirings > 0, "A swamp civilization should catch something over this many intervals.");
            Assert.True(
                swampFirings > tundraFirings * 2,
                $"Swamp firings ({swampFirings}) should clearly outrun tundra firings ({tundraFirings}); the biomes' rates differ by about 5x.");
        }

        private static int CountFirings(StorytellerComp_Disease comp, CivilizationTarget target, int intervals, int seed)
        {
            Rand.Current = new RandomStream(seed);
            int fired = 0;
            for (int i = 0; i < intervals; i++)
            {
                if (comp.MakeIntervalIncidents(target).Any()) fired++;
            }
            return fired;
        }
    }
}
