using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>Tests that watch the game rather than a module</b>, for the defect that started this lane.
    ///
    /// <para/><b>The measurement.</b> A seed-777 populated TribalStart world, watched: limestone 12,985 cells
    /// at day 0 and 242 at day 6, about two hundred rocks vanishing every game hour from the thirty-second,
    /// dipping at night. By day 6 no mountain stood anywhere on the map. The cause was <c>WorkGiver_Miner</c>
    /// offering every mineable edifice on the map, with <c>JobDriver_Mine</c> removing the rock after a flat
    /// 300 ticks. The same run measured after this lane (4 days, same seed): 12,989 mineable cells at day 0,
    /// 12,984 at day 4, and the five that went were a vein and the dig to it.
    ///
    /// <para/><b>Why two tests.</b> <see cref="A_settlement_with_nothing_to_dig_for_leaves_its_mountain_standing"/>
    /// is the one that carries the proof: a hand-built mountain, a dozen idle citizens whose only work is
    /// mining, thirty-odd thousand ticks — enough that the old behaviour takes a third of the mountain —
    /// and it runs in a second, so it can be broken on purpose and put back (the mutation proof is in this
    /// lane's report). <see cref="A_populated_settlements_mountain_is_still_standing_after_three_days"/> is
    /// the measurement itself, taken on the real world it was found in, and it costs the better part of two
    /// minutes.
    /// </summary>
    public class SettlementMiningTests : ContentTestBase
    {
        public SettlementMiningTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private static HashSet<IntVec3> MineableCells(CoreMap map)
        {
            var cells = new HashSet<IntVec3>();
            foreach (IntVec3 c in map.AllCells)
            {
                Thing? edifice = map.edificeGrid[c];
                if (edifice != null && edifice.def.mineable) cells.Add(c);
            }
            return cells;
        }

        private static int CountMineable(CoreMap map) => MineableCells(map).Count;

        private static int CountOf(CoreMap map, string defName) => map.listerThings.ThingsOfDef(Def(defName)).Count;

        // ---- the one that carries the proof ----

        private const int Citizens = 12;
        private const int Ticks = 36000;
        private static readonly CellRect Mountain = new CellRect(10, 10, 40, 40);

        private (CoreMap map, List<Pawn> citizens) MiningTown()
        {
            var map = new CoreMap(60, 60, TerrainDefOf.Soil);
            foreach (IntVec3 c in Mountain.Cells) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sandstone")), c, map);
            var citizens = new List<Pawn>();
            for (int i = 0; i < Citizens; i++)
            {
                Pawn p = NewHuman("Citizen" + i);
                p.skills!.GetSkill(SkillDefOf.Mining)!.Level = 10;
                GenSpawn.Spawn(p, new IntVec3(2 + i, 0, 2), map);
                citizens.Add(p);
            }
            return (map, citizens);
        }

        [Fact]
        public void A_settlement_with_nothing_to_dig_for_leaves_its_mountain_standing()
        {
            (CoreMap map, List<Pawn> citizens) = MiningTown();
            int before = CountOf(map, "Sandstone");
            Assert.Equal(Mountain.Area, before);

            for (int t = 0; t < Ticks; t++)
            {
                RunTicks(1, citizens.ToArray());
                MiningInitiative.TickMap(map); // self-gated on the rare tick, exactly as Map.MapTick drives it
            }

            int after = CountOf(map, "Sandstone");
            Assert.True(after * 10 >= before * 9,
                "Only " + after + " of " + before + " rock cells were left after " + Ticks + " ticks with nothing marked and nothing needing stone.");
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void The_same_town_digs_exactly_what_it_is_marked_to_dig()
        {
            // The positive control that gives the test above its meaning: these citizens do mine, quickly, when
            // there is something marked.
            (CoreMap map, List<Pawn> citizens) = MiningTown();
            var marked = new List<IntVec3>();
            for (int x = Mountain.minX; x < Mountain.minX + 6; x++)
            {
                for (int z = Mountain.minZ; z < Mountain.minZ + 2; z++) marked.Add(new IntVec3(x, 0, z));
            }
            foreach (IntVec3 c in marked) Assert.True(map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.Mine)));

            RunTicks(Ticks, citizens.ToArray());

            Assert.All(marked, c => Assert.Null(map.edificeGrid[c]));
            Assert.Empty(map.designationManager.AllDesignations);
            Assert.Equal(Mountain.Area - marked.Count, CountOf(map, "Sandstone"));
        }

        // ---- the measurement itself ----

        [Fact]
        public void A_populated_settlements_mountain_is_still_standing_after_three_days()
        {
            // Seed 777, populated (soloStart: false), TribalStart, the world the defect was measured in.
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "777",
                subdivisionOverride: 3, soloStart: false, bandSize: 25);
            Settlement settlement = game.CivilizationTarget.Seat!;
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;

            HashSet<IntVec3> before = MineableCells(map);
            int limestoneBefore = CountOf(map, "Limestone");
            Assert.True(limestoneBefore > 1000, "Setup: this seed's tile should carry a real mountain (" + limestoneBefore + " limestone cells).");
            var everMarked = new HashSet<IntVec3>();
            int mostMarkedAtOnce = 0;

            for (int tick = 0; tick < 3 * GenDate.TicksPerDay; tick++)
            {
                game.TickManager.DoSingleTick();
                // Often enough that no mark can be made and dug between two looks: a dig is hundreds of ticks.
                if (tick % 50 != 0) continue;
                int marks = 0;
                foreach (Designation mark in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine))
                {
                    everMarked.Add(mark.target.Cell);
                    marks++;
                }
                mostMarkedAtOnce = System.Math.Max(mostMarkedAtOnce, marks);
            }

            HashSet<IntVec3> after = MineableCells(map);
            int limestoneAfter = CountOf(map, "Limestone");

            // The measurement: 12,985 limestone cells to 242 in six days. A band, as the brief for this lane
            // asked - nearly all of the rock is still there...
            Assert.True(after.Count * 10 >= before.Count * 9,
                "The mountains went from " + before.Count + " cells to " + after.Count + " in three days: a settlement with nothing to dig for must leave them standing.");
            Assert.True(limestoneAfter * 10 >= limestoneBefore * 9, "Limestone: " + limestoneBefore + " -> " + limestoneAfter + ".");

            // ...and the invariant that the band is a proxy for: nothing was dug that nobody had marked. Three
            // days is long enough to see this and, with the defect put back, not long enough to lose a tenth of
            // the rock (the pick-hit loop is several times slower than the flat wait the defect was measured
            // against), so the band alone cannot be what proves this test.
            List<IntVec3> unmarked = before.Where(c => !after.Contains(c) && !everMarked.Contains(c)).ToList();
            Assert.True(unmarked.Count == 0, unmarked.Count + " rock cells were dug out without ever being marked, e.g. " + unmarked.FirstOrDefault() + ".");

            // And the initiative is what is working here: it ran through the real map tick, marked something,
            // and never held more than its cap.
            Assert.NotEmpty(everMarked);
            Assert.True(mostMarkedAtOnce <= MiningTuning.MaxOutstandingDesignations,
                "At one point " + mostMarkedAtOnce + " cells were marked at once; the cap is " + MiningTuning.MaxOutstandingDesignations + ".");
        }

        [Fact]
        public void On_a_generated_map_the_initiative_marks_a_vein_and_the_dig_to_it_inside_the_cap()
        {
            // The rule on real data rather than a picture: this seed's tile has four steel veins, none on open
            // ground, each 3 to 13 cells of rock from the nearest ground a citizen can stand on.
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "777",
                subdivisionOverride: 3, soloStart: false, bandSize: 25);
            Settlement settlement = game.CivilizationTarget.Seat!;
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;
            Assert.Empty(map.designationManager.AllDesignations);

            int marked = MiningInitiative.Run(settlement, map);

            List<IntVec3> marks = map.designationManager.AllDesignations.Select(d => d.target.Cell).ToList();
            Assert.True(marked > 0, "Four steel veins and an empty reserve: something should have been marked.");
            Assert.Equal(marked, marks.Count);
            Assert.True(marks.Count <= MiningTuning.MaxOutstandingDesignations);
            Assert.All(marks, c => Assert.NotNull(MineableUtility.GetFirstMineable(c, map)));

            // Every vein marked comes with its whole dig: its marks chain, four-connected, out to a cell with
            // open ground beside it.
            List<IntVec3> veins = marks.Where(c => MineableUtility.GetFirstMineable(c, map)!.def.mineableScatterCommonality > 0f).ToList();
            Assert.NotEmpty(veins);
            var markSet = new HashSet<IntVec3>(marks);
            foreach (IntVec3 vein in veins)
            {
                var seen = new HashSet<IntVec3> { vein };
                var queue = new Queue<IntVec3>();
                queue.Enqueue(vein);
                bool reachesOpenGround = false;
                while (queue.Count > 0 && !reachesOpenGround)
                {
                    IntVec3 c = queue.Dequeue();
                    reachesOpenGround = GenAdj.AdjacentCells.Any(a => GenGrid.InBounds(c + a, map) && GenGrid.Walkable(c + a, map));
                    foreach (IntVec3 d in GenAdj.CardinalDirections)
                    {
                        IntVec3 n = c + d;
                        if (markSet.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                    }
                }
                Assert.True(reachesOpenGround, "The dig marked for the vein at " + vein + " never reaches open ground.");
            }

            // No mountain: a sliver of what is there.
            Assert.True(marks.Count * 100 < CountMineable(map));
        }
    }
}
