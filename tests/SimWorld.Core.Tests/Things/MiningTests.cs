using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// Mining yield (system: mining): a mined-out rock drops what its Def says it holds.
    ///
    /// <para/><b>What this replaced.</b> <c>JobDriver_Mine</c>'s completion toil was
    /// <c>rock.Destroy(DestroyMode.KillFinalize)</c> and nothing else. <c>ThingDef.mineableThing</c> and
    /// <c>ThingDef.mineableYield</c> were read by no line of <c>src/</c> — both were listed in
    /// <c>dormant-seams.txt</c> as <c>code-deaf</c>, which is that file's word for "shipped content sets it
    /// and nothing reads it" — so a citizen spent 300 ticks on a vein of 60 steel and the settlement finished
    /// with exactly as many items on the map as it started with. The driver's own doc claimed it read those
    /// fields "so a later module only needs to add the spawn call", and said the yield was unspawned because
    /// "no mined-resource ThingDefs exist in content"; <c>Buildings_Natural.xml</c> had declared
    /// <c>MineableSteel</c> dropping <c>Steel</c> for as long as the driver had existed, and <c>Steel</c>
    /// already had buildings and a knife recipe costing it.
    ///
    /// <para/><b>Measured, not assumed.</b> Putting that single <c>Destroy(KillFinalize)</c> call back in
    /// place of <c>DestroyMined</c> and re-running this file fails five of its tests and no others:
    /// <c>A_citizen_mines_an_ore_vein_and_the_settlement_gains_the_resource</c>,
    /// <c>The_mined_resource_lands_in_the_cell_the_vein_occupied</c>,
    /// <c>A_mined_vein_never_pays_more_than_it_held</c>,
    /// <c>A_mining_job_saved_mid_dig_still_pays_out_when_it_is_loaded_and_finished</c> and
    /// <c>Mining_teaches_the_miner_the_Mining_skill</c> — the five that go through a real job. The rest hold,
    /// because they ask <c>MineableUtility</c> and <c>Mineable</c> directly and those are what was missing.
    ///
    /// <para/>Assertions here are bands, trends and orderings. The one place a literal appears is a Def's own
    /// <c>mineableYield</c> used as the ceiling on what mining it can pay — which is a property of the
    /// mechanic (a miner never recovers more than the vein held), not a tuning number.
    /// </summary>
    public class MiningTests : ContentTestBase
    {
        public MiningTests(CoreContentFixture content) : base(content)
        {
        }

        private const string SteelVein = "MineableSteel";

        private static CoreMap NewMap(int sizeX = 6, int sizeZ = 6) =>
            new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private static Pawn SpawnMiner(CoreMap map, IntVec3 cell, int miningLevel, string name = "Miner")
        {
            Pawn p = NewHuman(name);
            SkillRecord? mining = p.skills?.GetSkill(SkillDefOf.Mining);
            if (mining != null) mining.Level = miningLevel;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Mineable SpawnVein(CoreMap map, IntVec3 cell, string defName = SteelVein)
        {
            var vein = (Mineable)ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(vein, cell, map);
            return vein;
        }

        /// <summary>Total items of <paramref name="defName"/> lying on the map, counting stacks.</summary>
        private static int AmountOnMap(CoreMap map, string defName) =>
            map.listerThings.ThingsOfDef(Def(defName)).Sum(t => t.stackCount);

        /// <summary>Points the storyteller at a named difficulty preset so its factors are in play.</summary>
        private static void UseDifficulty(string defName) =>
            Find.Storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, DefDatabase<DifficultyDef>.GetNamed(defName));

        // ---- the point of the whole module ----

        [Fact]
        public void A_citizen_mines_an_ore_vein_and_the_settlement_gains_the_resource()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 10);
            Mineable vein = SpawnVein(map, new IntVec3(2, 0, 2));

            Assert.Equal(0, AmountOnMap(map, "Steel"));

            miner.jobs.StartJob(new Job(JobDefOf.Mine, vein));
            RunTicks(2000, miner);

            Assert.True(vein.Destroyed, "The vein should have been mined out.");
            Assert.True(AmountOnMap(map, "Steel") > 0,
                "Mining a steel vein should leave steel on the map. Before this module it left nothing at all.");
        }

        [Fact]
        public void The_mined_resource_lands_in_the_cell_the_vein_occupied()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 20);
            var cell = new IntVec3(2, 0, 2);
            Mineable vein = SpawnVein(map, cell);

            miner.jobs.StartJob(new Job(JobDefOf.Mine, vein));
            RunTicks(2000, miner);

            IReadOnlyList<Thing> steel = map.listerThings.ThingsOfDef(Def("Steel"));
            Assert.Single(steel);
            Assert.Equal(cell, steel[0].Position);
        }

        [Fact]
        public void A_mined_vein_never_pays_more_than_it_held()
        {
            CoreMap map = NewMap();
            // The best possible miner on the gentlest difficulty: nothing wastes, nothing is taken away, and
            // the amount still cannot exceed what the Def says is in the rock.
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 20);
            UseDifficulty("Peaceful");
            Mineable vein = SpawnVein(map, new IntVec3(2, 0, 2));
            int inTheRock = vein.def.mineableYield;

            miner.jobs.StartJob(new Job(JobDefOf.Mine, vein));
            RunTicks(2000, miner);

            int gained = AmountOnMap(map, "Steel");
            Assert.InRange(gained, 1, inTheRock);
        }

        // ---- difficulty ----

        [Fact]
        public void Mining_yield_falls_with_the_difficulty_mineYieldFactor()
        {
            // DifficultyDef.mineYieldFactor was code-deaf too: set by Rough and Extreme in Difficulties.xml
            // and read by nothing, because there was no yield in the game for it to scale.
            ThingDef vein = Def(SteelVein);
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            DifficultyDef extreme = DefDatabase<DifficultyDef>.GetNamed("Extreme");
            Assert.Equal(1f, medium.mineYieldFactor);
            Assert.True(extreme.mineYieldFactor < 1f, "Extreme is supposed to be the stingy preset.");

            int atMedium = TotalYieldOver(500, vein, difficulty: "Medium");
            int atExtreme = TotalYieldOver(500, vein, difficulty: "Extreme");

            Assert.True(atExtreme < atMedium,
                $"Extreme ({atExtreme}) should yield less than Medium ({atMedium}) over the same 500 veins.");

            // And by roughly the factor content asks for, not merely "less": a 10% band around it, which is
            // far tighter than the difference between the two presets and far looser than the rounding noise.
            float observed = (float)atExtreme / atMedium;
            Assert.InRange(observed, extreme.mineYieldFactor - 0.1f, extreme.mineYieldFactor + 0.1f);
        }

        /// <summary>Mines <paramref name="samples"/> identical veins with no miner and totals what they paid.</summary>
        private static int TotalYieldOver(int samples, ThingDef vein, string difficulty, Pawn? miner = null, int seed = 7788)
        {
            UseDifficulty(difficulty);
            var rand = new RandomStream(seed);
            int total = 0;
            for (int i = 0; i < samples; i++) total += MineableUtility.YieldFor(vein, miner, rand);
            return total;
        }

        // ---- skill ----

        [Fact]
        public void A_better_miner_recovers_more_of_the_same_vein()
        {
            CoreMap map = NewMap();
            Pawn novice = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 0, name: "Novice");
            Pawn expert = SpawnMiner(map, new IntVec3(1, 0, 0), miningLevel: 20, name: "Expert");
            ThingDef vein = Def(SteelVein);

            int byNovice = TotalYieldOver(400, vein, "Medium", novice);
            int byExpert = TotalYieldOver(400, vein, "Medium", expert);

            Assert.True(byExpert > byNovice,
                $"A level-20 miner ({byExpert}) should recover more than a level-0 one ({byNovice}) from the same 400 veins.");
        }

        [Fact]
        public void A_rock_chunk_is_one_chunk_however_good_the_miner_is()
        {
            // Rock sets mineableYieldWasteable=false: there is nothing in a single chunk to waste, so skill
            // must not move it. (Ore sets the opposite, which the test above pins.)
            CoreMap map = NewMap();
            Pawn novice = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 0, name: "Novice");
            Pawn expert = SpawnMiner(map, new IntVec3(1, 0, 0), miningLevel: 20, name: "Expert");
            ThingDef rock = Def("Sandstone");
            Assert.False(rock.mineableYieldWasteable);

            Assert.Equal(TotalYieldOver(300, rock, "Medium", novice), TotalYieldOver(300, rock, "Medium", expert));
        }

        [Fact]
        public void Mining_teaches_the_miner_the_Mining_skill()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 0);
            SkillRecord mining = miner.skills!.GetSkill(SkillDefOf.Mining)!;
            float before = mining.xpSinceLastLevel;
            Mineable vein = SpawnVein(map, new IntVec3(2, 0, 2));

            miner.jobs.StartJob(new Job(JobDefOf.Mine, vein));
            RunTicks(2000, miner);

            Assert.True(mining.xpSinceLastLevel > before,
                "Digging a vein out should teach the miner something; before this module mining granted no xp at all.");
        }

        // ---- rock, as distinct from ore ----

        [Fact]
        public void A_mined_rock_wall_drops_a_chunk_sometimes_and_not_every_time()
        {
            // RimWorld's shape: a tunnel through a mountain leaves a scattering of chunks, not one per cell.
            // The band is the assertion — the drop chance itself is an unsourced content number.
            ThingDef sandstone = Def("Sandstone");
            UseDifficulty("Medium");
            var rand = new RandomStream(4242);

            int dropped = 0;
            const int Cells = 400;
            for (int i = 0; i < Cells; i++)
            {
                if (MineableUtility.YieldFor(sandstone, null, rand) > 0) dropped++;
            }

            Assert.InRange(dropped, 1, Cells - 1);
        }

        [Fact]
        public void Mining_a_rock_wall_puts_its_own_chunk_on_the_ground()
        {
            CoreMap map = NewMap();
            UseDifficulty("Medium");

            // Straight through Mineable.DestroyMined rather than through a job: a tenth of these pay, so the
            // end-to-end version would need a pawn to mine for most of a day and starve doing it. The job's
            // own end of this — that it calls DestroyMined at all — is pinned by the ore test above.
            int chunks = 0;
            for (int i = 0; i < 60 && chunks == 0; i++)
            {
                Mineable rock = SpawnVein(map, new IntVec3(2, 0, 2), "Granite");
                rock.DestroyMined(null);
                Assert.True(rock.Destroyed, "Each rock should be mined out whether or not it paid a chunk.");
                chunks = AmountOnMap(map, "ChunkGranite");
            }

            Assert.True(chunks > 0, "Sixty mined granite cells should have produced at least one chunk of granite.");
        }

        // ---- determinism ----

        [Fact]
        public void The_same_seed_mines_the_same_amounts_out_of_the_same_rock()
        {
            ThingDef vein = Def(SteelVein);
            ThingDef rock = Def("Sandstone");
            UseDifficulty("Rough");

            var first = new List<int>();
            var second = new List<int>();
            var a = new RandomStream(99001);
            var b = new RandomStream(99001);
            for (int i = 0; i < 60; i++)
            {
                // Alternating the two so the sequence exercises both arms of the yield — an ore vein that
                // always pays and a rock wall that pays on a roll — off one stream.
                first.Add(MineableUtility.YieldFor(vein, null, a));
                first.Add(MineableUtility.YieldFor(rock, null, a));
                second.Add(MineableUtility.YieldFor(vein, null, b));
                second.Add(MineableUtility.YieldFor(rock, null, b));
            }

            Assert.Equal(first, second);
            Assert.True(first.Distinct().Count() > 1,
                "A sequence of ore and rock should not come out constant — the rock arm is a chance.");
        }

        // ---- Scribe ----

        [Fact]
        public void A_mining_job_saved_mid_dig_still_pays_out_when_it_is_loaded_and_finished()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), miningLevel: 12);
            Mineable vein = SpawnVein(map, new IntVec3(2, 0, 2));

            miner.jobs.StartJob(new Job(JobDefOf.Mine, vein));
            RunTicks(20, miner);
            Assert.Equal(JobDefOf.Mine, miner.jobs.curJob?.def);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var loadedPawn = (Pawn)loaded.mapPawns.AllPawns[0];
            Thing loadedVein = loaded.listerThings.ThingsOfDef(Def(SteelVein))[0];
            Assert.IsType<Mineable>(loadedVein);

            RunTicks(2000, loadedPawn);

            Assert.True(loadedVein.Destroyed);
            Assert.True(AmountOnMap(loaded, "Steel") > 0,
                "A job that survived a save/load should pay out on completion exactly as an unsaved one does.");
        }

        // ---- content ----

        [Fact]
        public void Every_mineable_ThingDef_in_content_says_what_it_drops()
        {
            var silent = new List<string>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.mineable) continue;
                if (def.mineableThing == null || def.mineableYield <= 0) silent.Add(def.defName);
            }

            Assert.True(silent.Count == 0,
                "These mineable Defs can be dug out and give nothing back: " + string.Join(", ", silent));
        }

        [Fact]
        public void Every_mineable_ThingDef_in_content_is_a_Mineable()
        {
            var wrong = new List<string>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.mineable) continue;
                if (def.thingClass == null || !typeof(Mineable).IsAssignableFrom(def.thingClass)) wrong.Add(def.defName);
            }

            // Mineable.DestroyMined is the only path that turns a mined cell into items, so a mineable Def
            // whose thingClass is a plain Thing is a vein that can never pay, however much yield it declares.
            Assert.True(wrong.Count == 0,
                "These mineable Defs are not SimWorld.Things.Mineable, so mining them would drop nothing: "
                + string.Join(", ", wrong));
        }

        [Fact]
        public void Content_ships_a_mining_ladder_rather_than_a_single_ore()
        {
            List<ThingDef> mineables = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.mineable).ToList();
            List<ThingDef> veins = mineables.Where(d => d.mineableScatterCommonality > 0f).ToList();

            // Before this module exactly one Def in all of content was mineable with a yield. The number
            // below is a floor, not a target: the point is that the ore table is a table.
            Assert.True(veins.Count >= 8, "Only " + veins.Count + " ore veins in content.");

            // Every era on this project's own ladder has something in the ground.
            foreach (string mineral in new[] { "Flint", "Salt", "Copper", "Tin", "Steel", "Silver", "Gold", "Coal", "Uranium", "Plasteel" })
            {
                Assert.True(mineables.Any(d => d.mineableThing?.defName == mineral),
                    "Nothing in content is mined for " + mineral + ".");
            }
        }

        [Fact]
        public void The_minerals_are_priced_in_the_order_the_ladder_implies()
        {
            // Ordering, not literals. Steel (1.9) is the anchor this chain is hung on — content that shipped
            // long before mining did. Silver and Coal are deliberately NOT in the chain: Silver is the trade
            // currency and is 1 by definition (Items_Currency.xml says so), and Coal is priced by the Economy
            // module's own supply model rather than by where it sits on a ladder of minerals.
            string[] ascending = { "Flint", "Salt", "Tin", "Copper", "Steel", "Gold", "Jade", "Uranium", "Plasteel" };
            StatDef marketValue = DefDatabase<StatDef>.GetNamed("MarketValue");

            for (int i = 1; i < ascending.Length; i++)
            {
                float cheaper = Def(ascending[i - 1]).GetStatValueAbstract(marketValue);
                float dearer = Def(ascending[i]).GetStatValueAbstract(marketValue);
                Assert.True(cheaper < dearer,
                    ascending[i - 1] + " (" + cheaper + ") should be worth less than " + ascending[i] + " (" + dearer + ").");
            }
        }

        [Fact]
        public void Map_generation_picks_veins_from_the_content_table_and_steel_dominates_it()
        {
            var rand = new RandomStream(20260911);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < 2000; i++)
            {
                ThingDef? picked = MineableUtility.RandomVeinDef(rand);
                Assert.NotNull(picked);
                Assert.True(picked!.mineable, picked.defName + " was offered as a vein but is not mineable.");
                Assert.NotNull(picked.mineableThing);
                counts[picked.defName] = counts.TryGetValue(picked.defName, out int n) ? n + 1 : 1;
            }

            Assert.True(counts.Count >= 5, "The vein table collapsed to " + counts.Count + " Def(s).");
            string mostCommon = counts.OrderByDescending(kv => kv.Value).First().Key;
            Assert.Equal(SteelVein, mostCommon);

            // And the rare end of the ladder stays rare rather than being unreachable.
            Assert.True(counts.ContainsKey("MineablePlasteel"), "Plasteel never came up in 2000 draws.");
            Assert.True(counts["MineablePlasteel"] < counts[SteelVein] / 4,
                "Plasteel should be far rarer than steel.");
        }

        [Fact]
        public void Mining_content_loads_with_no_errors_and_the_yield_stat_is_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(MiningStatDefOf.MiningYield);

            Pawn novice = NewHuman("Novice");
            Pawn expert = NewHuman("Expert");
            novice.skills!.GetSkill(SkillDefOf.Mining)!.Level = 0;
            expert.skills!.GetSkill(SkillDefOf.Mining)!.Level = 20;

            float atZero = novice.GetStatValue(MiningStatDefOf.MiningYield);
            float atTwenty = expert.GetStatValue(MiningStatDefOf.MiningYield);

            Assert.True(atZero < atTwenty, "Mining yield should rise with the Mining skill.");
            Assert.InRange(atZero, 0f, 0.9f);
            // The stat's maxValue caps it: a master miner recovers the whole vein and cannot conjure more.
            Assert.InRange(atTwenty, 0.99f, 1f);
        }
    }
}
