using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Mining reads marks and takes as long as the rock says (system: mining; RimWorld:
    /// <c>WorkGiver_Miner</c>, <c>JobDriver_Mine</c>, <c>Mineable</c>).
    ///
    /// <para/><b>What this replaced.</b> The giver scanned every mineable edifice on the map and the driver
    /// waited a flat 300 ticks. Measured on a seed-777 settlement that took 12,985 limestone cells to 242 in
    /// six days. The first half of this class is the fix, stated as behaviour: no mark, no job; a mark, a job;
    /// the mark gone when the rock is. The second half is the other change, that a dig is the rock's hit
    /// points: pick hits every <c>round(100 ÷ MiningSpeed)</c> ticks, 80 damage to natural rock and 40 to
    /// anything else, and the time follows the miner's skill and the rock's toughness.
    ///
    /// <para/>Times are asserted against the formula, built from the driver's own public constants
    /// (<see cref="JobDriver_Mine.BaseTicksBetweenPickHits"/>, the two damages), which are RimWorld's numbers
    /// and not this port's. Everything this port chose (rock hit points, the <c>MiningSpeed</c> curve) is
    /// asserted as a trend or read off the Def.
    /// </summary>
    public class MiningDesignationTests : ContentTestBase
    {
        public MiningDesignationTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX = 12, int sizeZ = 5) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private Pawn SpawnMiner(CoreMap map, IntVec3 cell, int level, string name = "Miner")
        {
            Pawn p = NewHuman(name);
            p.skills!.GetSkill(SkillDefOf.Mining)!.Level = level;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Mineable SpawnRock(CoreMap map, IntVec3 cell, string defName = "Sandstone")
        {
            var rock = (Mineable)ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(rock, cell, map);
            return rock;
        }

        private static Mineable SpawnMarkedRock(CoreMap map, IntVec3 cell, string defName = "Sandstone")
        {
            Mineable rock = SpawnRock(map, cell, defName);
            Assert.True(map.designationManager.AddDesignation(new Designation(cell, DesignationDefOf.Mine)));
            return rock;
        }

        private static bool IsMarked(CoreMap map, IntVec3 cell) =>
            map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) != null;

        /// <summary>Ticks a miner standing beside the rock takes to mine it out, or -1 if it is not out in
        /// <paramref name="limit"/>. The job is handed over directly so the number is the dig, not the think tree.</summary>
        private int TicksToMine(Pawn miner, Mineable rock, int limit = 6000)
        {
            miner.jobs.StartJob(new Job(JobDefOf.Mine, rock));
            for (int t = 1; t <= limit; t++)
            {
                RunTicks(1, miner);
                if (rock.Destroyed) return t;
            }
            return -1;
        }

        /// <summary>
        /// Several miners dug side by side on one clock: the tick each rock came out, or -1. Every pawn spawned
        /// in a test ticks whenever the shared clock does, so timing two miners one after the other would let
        /// the first run drive the second.
        /// </summary>
        private int[] TicksToMineTogether(int limit, params (Pawn miner, Mineable rock)[] digs)
        {
            var finished = Enumerable.Repeat(-1, digs.Length).ToArray();
            foreach ((Pawn miner, Mineable rock) in digs) miner.jobs.StartJob(new Job(JobDefOf.Mine, rock));
            Pawn[] miners = digs.Select(d => d.miner).ToArray();
            for (int t = 1; t <= limit; t++)
            {
                RunTicks(1, miners);
                for (int i = 0; i < digs.Length; i++)
                {
                    if (finished[i] < 0 && digs[i].rock.Destroyed) finished[i] = t;
                }
                if (finished.All(f => f >= 0)) break;
            }
            return finished;
        }

        /// <summary>How many pick hits it takes to get through <paramref name="hitPoints"/> at
        /// <paramref name="damage"/> a hit: every hit takes <c>damage</c> off until what is left is no more
        /// than one hit's worth, and that last hit is the one that breaks it.</summary>
        private static int HitsToMine(int hitPoints, int damage) => (hitPoints + damage - 1) / damage;

        // ---- the fix: no mark, no mining ----

        [Fact]
        public void With_no_mark_on_the_map_the_giver_offers_nothing_and_nobody_mines()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 2), level: 10);
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 2));
            var giver = (WorkGiver_Miner)DefDatabase<WorkGiverDef>.GetNamed("Mine").Worker;

            Assert.Empty(giver.PotentialWorkThingsGlobal(miner));
            Assert.False(giver.HasJobOnThing(miner, rock));

            bool everMined = false;
            for (int i = 0; i < 4000; i++)
            {
                RunTicks(1, miner);
                everMined |= miner.jobs.curJob?.def == JobDefOf.Mine;
            }

            Assert.False(everMined, "A citizen picked up a pick with nothing marked.");
            Assert.False(rock.Destroyed);
            Assert.Equal(rock.MaxHitPoints, rock.HitPoints);
        }

        [Fact]
        public void A_marked_rock_is_mined_out_and_its_mark_goes_with_it()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 2), level: 10);
            Mineable rock = SpawnMarkedRock(map, new IntVec3(3, 0, 2));

            RunTicks(3000, miner);

            Assert.True(rock.Destroyed, "The marked rock should have been mined out by a citizen with nothing else to do.");
            Assert.False(IsMarked(map, new IntVec3(3, 0, 2)));
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void Only_the_marked_rock_is_mined_and_its_neighbours_are_left_standing()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 2), level: 10);
            Mineable marked = SpawnMarkedRock(map, new IntVec3(3, 0, 2));
            Mineable neighbourA = SpawnRock(map, new IntVec3(3, 0, 3));
            Mineable neighbourB = SpawnRock(map, new IntVec3(4, 0, 2));
            Mineable far = SpawnRock(map, new IntVec3(8, 0, 1));

            RunTicks(6000, miner);

            Assert.True(marked.Destroyed);
            foreach (Mineable standing in new[] { neighbourA, neighbourB, far })
            {
                Assert.False(standing.Destroyed, standing.Position + " was mined without a mark.");
                Assert.Equal(standing.MaxHitPoints, standing.HitPoints);
            }
        }

        [Fact]
        public void A_marked_cell_with_rock_all_round_it_is_offered_only_once_a_neighbour_is_open()
        {
            CoreMap map = NewMap(9, 9);
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), level: 10);
            for (int x = 3; x <= 5; x++)
            {
                for (int z = 3; z <= 5; z++) SpawnRock(map, new IntVec3(x, 0, z));
            }
            var centre = new IntVec3(4, 0, 4);
            map.designationManager.AddDesignation(new Designation(centre, DesignationDefOf.Mine));
            var giver = (WorkGiver_Miner)DefDatabase<WorkGiverDef>.GetNamed("Mine").Worker;

            Assert.Empty(giver.PotentialWorkThingsGlobal(miner));

            MineableUtility.GetFirstMineable(new IntVec3(4, 0, 3), map)!.Destroy(DestroyMode.Vanish);

            Assert.Equal(centre, giver.PotentialWorkThingsGlobal(miner).Single().Position);
        }

        [Fact]
        public void A_players_direct_order_digs_an_unmarked_rock_without_a_mark()
        {
            // The order names the rock, so it carries the choice a mark would (see JobDriver_Mine's doc).
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 10);
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 2));

            miner.jobs.StartJob(new Job(JobDefOf.Mine, rock) { playerForced = true });
            RunTicks(3000, miner);

            Assert.True(rock.Destroyed);
        }

        [Fact]
        public void The_same_job_without_an_order_and_without_a_mark_ends_at_once()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 10);
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 2));

            miner.jobs.StartJob(new Job(JobDefOf.Mine, rock));
            RunTicks(1, miner);

            Assert.NotEqual(JobDefOf.Mine, miner.jobs.curJob?.def);
            Assert.False(rock.Destroyed);
        }

        [Fact]
        public void Cancelling_the_mark_mid_dig_stops_the_miner_and_leaves_the_rock_as_damaged_as_it_is()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 10);
            Mineable rock = SpawnMarkedRock(map, new IntVec3(3, 0, 2), "Granite");
            miner.jobs.StartJob(new Job(JobDefOf.Mine, rock));

            RunTicks(2 * JobDriver_Mine.TicksBetweenPickHits(miner) + 5, miner);
            int damaged = rock.HitPoints;
            Assert.True(damaged < rock.MaxHitPoints, "Setup: the miner should have landed a hit by now.");
            Assert.Equal(JobDefOf.Mine, miner.jobs.curJob?.def);

            map.designationManager.DesignationAt(rock.Position, DesignationDefOf.Mine)!.Delete();
            RunTicks(500, miner);

            Assert.False(rock.Destroyed);
            Assert.Equal(damaged, rock.HitPoints);
            Assert.NotEqual(JobDefOf.Mine, miner.jobs.curJob?.def);
        }

        // ---- the roof guard survives the designation layer ----

        [Fact]
        public void A_marked_rock_that_is_holding_a_roof_up_is_still_not_dug()
        {
            // The one translation left in this giver: a mark does not make a support safe to take out.
            CoreMap map = NewMap(20, 3);
            global::SimWorld.Building.Building wall = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, new IntVec3(0, 0, 1), map);
            Mineable support = SpawnMarkedRock(map, new IntVec3(2, 0, 1));
            for (int x = 1; x <= 9; x++) map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            Assert.True(RoofCollapseUtility.WouldCollapseRoofIfRemoved(support), "Setup: this rock must be a support.");
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 0), level: 10);
            var giver = (WorkGiver_Miner)DefDatabase<WorkGiverDef>.GetNamed("Mine").Worker;

            Assert.Single(giver.PotentialWorkThingsGlobal(miner));
            Assert.False(giver.HasJobOnThing(miner, support));

            RunTicks(4000, miner);

            Assert.False(support.Destroyed);
            Assert.True(map.roofGrid.Roofed(new IntVec3(9, 0, 1)));
            Assert.True(IsMarked(map, support.Position), "The mark stays: it is the player's or the settlement's, not the guard's.");
        }

        [Fact]
        public void A_dig_that_turns_into_a_support_part_way_through_ends_instead_of_bringing_the_roof_down()
        {
            CoreMap map = NewMap(20, 3);
            global::SimWorld.Building.Building wall = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, new IntVec3(0, 0, 1), map);
            Mineable rockA = SpawnMarkedRock(map, new IntVec3(2, 0, 1));
            Mineable rockB = SpawnRock(map, new IntVec3(3, 0, 1)); // a second support beside it
            for (int x = 1; x <= 8; x++) map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            Assert.False(RoofCollapseUtility.WouldCollapseRoofIfRemoved(rockA), "Setup: with B standing, A is spare.");
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 0), level: 10);
            miner.jobs.StartJob(new Job(JobDefOf.Mine, rockA));
            RunTicks(JobDriver_Mine.TicksBetweenPickHits(miner) + 2, miner);

            rockB.Destroy(DestroyMode.Vanish); // somebody else's dig, or anything else, takes the other support
            Assert.True(map.roofGrid.Roofed(new IntVec3(8, 0, 1)), "Setup: A alone still holds the far end up.");
            Assert.True(RoofCollapseUtility.WouldCollapseRoofIfRemoved(rockA), "Setup: A has become a support.");
            RunTicks(3000, miner);

            Assert.False(rockA.Destroyed, "The last hit must not remove a rock that has become a support.");
            Assert.True(map.roofGrid.Roofed(new IntVec3(8, 0, 1)));
            Assert.NotEqual(JobDefOf.Mine, miner.jobs.curJob?.def);
        }

        // ---- time: the rock's hit points and the miner's speed ----

        [Fact]
        public void A_rock_takes_the_pick_hits_its_hit_points_say_at_RimWorlds_interval()
        {
            // A level-10 miner is at the base MiningSpeed of 1.0 plus whatever the curve gives; ask the
            // interval, don't assume it. Standing beside the rock, the dig is hits x interval, give or take
            // the first tick and the walk of none.
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 10);
            Mineable rock = SpawnMarkedRock(map, new IntVec3(3, 0, 2), "Granite");
            int interval = JobDriver_Mine.TicksBetweenPickHits(miner);
            int hits = HitsToMine(rock.HitPoints, JobDriver_Mine.BaseDamagePerPickHit_NaturalRock);

            int ticks = TicksToMine(miner, rock);

            Assert.True(ticks > 0, "The rock was never mined out.");
            Assert.InRange(ticks, hits * interval - 3, hits * interval + 8);
        }

        [Fact]
        public void At_a_mining_speed_of_one_a_rock_takes_a_hundred_ticks_a_hit()
        {
            // RimWorld's own number, with the port's skill curve taken out of it: whoever mines, a hit is
            // round(100 / speed) ticks, so at speed 1 it is exactly BaseTicksBetweenPickHits.
            Assert.Equal(100, JobDriver_Mine.BaseTicksBetweenPickHits);
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 10);
            float speed = miner.GetStatValue(StatDefOf.MiningSpeed);
            Assert.Equal((int)Math.Round(JobDriver_Mine.BaseTicksBetweenPickHits / speed), JobDriver_Mine.TicksBetweenPickHits(miner));
        }

        [Fact]
        public void A_faster_miner_finishes_the_same_rock_sooner_in_proportion_to_their_speed()
        {
            CoreMap mapA = NewMap();
            CoreMap mapB = NewMap();
            Pawn novice = SpawnMiner(mapA, new IntVec3(2, 0, 2), level: 0, name: "Novice");
            Pawn expert = SpawnMiner(mapB, new IntVec3(2, 0, 2), level: 20, name: "Expert");
            Mineable rockA = SpawnMarkedRock(mapA, new IntVec3(3, 0, 2), "Granite");
            Mineable rockB = SpawnMarkedRock(mapB, new IntVec3(3, 0, 2), "Granite");
            Assert.True(expert.GetStatValue(StatDefOf.MiningSpeed) > novice.GetStatValue(StatDefOf.MiningSpeed), "Setup: skill must drive MiningSpeed.");

            int[] done = TicksToMineTogether(6000, (novice, rockA), (expert, rockB));
            int slow = done[0];
            int fast = done[1];

            Assert.True(fast > 0 && slow > 0);
            Assert.True(fast < slow, "A level-20 miner (" + fast + " ticks) should finish before a level-0 one (" + slow + ").");
            float expectedRatio = expert.GetStatValue(StatDefOf.MiningSpeed) / novice.GetStatValue(StatDefOf.MiningSpeed);
            float measuredRatio = (float)slow / fast;
            Assert.InRange(measuredRatio, expectedRatio * 0.9f, expectedRatio * 1.1f);
        }

        [Fact]
        public void The_time_between_pick_hits_falls_as_the_mining_skill_rises()
        {
            CoreMap map = NewMap();
            int previous = int.MaxValue;
            for (int level = 0; level <= 20; level += 5)
            {
                Pawn p = SpawnMiner(map, new IntVec3(level / 5, 0, 0), level, "Level" + level);
                int interval = JobDriver_Mine.TicksBetweenPickHits(p);
                Assert.True(interval < previous, "Level " + level + " (" + interval + ") should hit faster than the level before it (" + previous + ").");
                previous = interval;
            }
        }

        [Fact]
        public void Natural_rock_gives_way_to_a_pick_twice_as_fast_per_hit_point_as_a_vein()
        {
            Assert.Equal(2 * JobDriver_Mine.BaseDamagePerPickHit_NotNaturalRock, JobDriver_Mine.BaseDamagePerPickHit_NaturalRock);
            Assert.True(Def("Sandstone").isNaturalRock);
            Assert.True(Def("Granite").isNaturalRock);
            Assert.True(Def("Limestone").isNaturalRock);
            Assert.False(Def("MineableSteel").isNaturalRock);

            // And through the real driver, with the vein made exactly as tough as the rock: it takes twice the hits.
            CoreMap mapA = NewMap();
            CoreMap mapB = NewMap();
            Pawn a = SpawnMiner(mapA, new IntVec3(2, 0, 2), level: 10);
            Pawn b = SpawnMiner(mapB, new IntVec3(2, 0, 2), level: 10);
            Mineable rock = SpawnMarkedRock(mapA, new IntVec3(3, 0, 2), "Sandstone");
            Mineable vein = SpawnMarkedRock(mapB, new IntVec3(3, 0, 2), "MineableSteel");
            vein.HitPoints = rock.HitPoints;

            int[] done = TicksToMineTogether(6000, (a, rock), (b, vein));
            int rockTicks = done[0];
            int veinTicks = done[1];

            Assert.True(rockTicks > 0 && veinTicks > 0);
            Assert.InRange((float)veinTicks / rockTicks, 1.7f, 2.3f);
        }

        // ---- what a dig earns ----

        [Fact]
        public void Mining_teaches_continuously_as_the_pick_swings_not_in_a_lump_when_the_rock_breaks()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 0);
            SkillRecord mining = miner.skills!.GetSkill(SkillDefOf.Mining)!;
            Mineable rock = SpawnMarkedRock(map, new IntVec3(3, 0, 2), "Granite");
            float start = mining.xpSinceLastLevel;
            miner.jobs.StartJob(new Job(JobDefOf.Mine, rock));

            RunTicks(100, miner);
            float after100 = mining.xpSinceLastLevel - start;
            RunTicks(100, miner);
            float after200 = mining.xpSinceLastLevel - start;

            Assert.False(rock.Destroyed, "Setup: the rock must still be standing for this to say anything.");
            Assert.True(after100 > 0f, "A miner should be learning while the pick swings, before anything breaks.");
            Assert.InRange(after200 / after100, 1.8f, 2.2f); // the same rate each tick, not a step
            Assert.True(after200 <= 200 * JobDriver_Mine.LearnXpPerTick * 2f, "No faster than RimWorld's rate and the best passion multiplier.");
        }

        [Fact]
        public void An_interrupted_dig_still_taught_the_miner_for_the_time_spent()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 0);
            SkillRecord mining = miner.skills!.GetSkill(SkillDefOf.Mining)!;
            SpawnMarkedRock(map, new IntVec3(3, 0, 2), "Granite");
            float start = mining.xpSinceLastLevel;
            miner.jobs.StartJob(new Job(JobDefOf.Mine, MineableUtility.GetFirstMineable(new IntVec3(3, 0, 2), map)));
            RunTicks(150, miner);

            map.designationManager.DesignationAt(new IntVec3(3, 0, 2), DesignationDefOf.Mine)!.Delete();
            RunTicks(5, miner);

            Assert.True(mining.xpSinceLastLevel > start);
        }

        [Fact]
        public void An_ore_vein_pays_the_part_of_itself_each_miner_dug_and_a_better_miner_more_of_it()
        {
            CoreMap map = NewMap();
            Pawn novice = SpawnMiner(map, new IntVec3(2, 0, 1), level: 0, name: "Novice");
            Pawn expert = SpawnMiner(map, new IntVec3(2, 0, 3), level: 20, name: "Expert");
            Mineable forNovice = SpawnMarkedRock(map, new IntVec3(3, 0, 1), "MineableSteel");
            Mineable forExpert = SpawnMarkedRock(map, new IntVec3(3, 0, 3), "MineableSteel");
            novice.jobs.StartJob(new Job(JobDefOf.Mine, forNovice));
            expert.jobs.StartJob(new Job(JobDefOf.Mine, forExpert));

            RunTicks(4000, novice, expert);

            Assert.True(forNovice.Destroyed && forExpert.Destroyed);
            int atNovice = map.thingGrid.ThingsListAt(new IntVec3(3, 0, 1)).Where(t => t.def.defName == "Steel").Sum(t => t.stackCount);
            int atExpert = map.thingGrid.ThingsListAt(new IntVec3(3, 0, 3)).Where(t => t.def.defName == "Steel").Sum(t => t.stackCount);
            Assert.InRange(atNovice, 1, forNovice.def.mineableYield);
            Assert.InRange(atExpert, 1, forExpert.def.mineableYield);
            Assert.True(atExpert > atNovice, "The expert recovered " + atExpert + " steel and the novice " + atNovice + " from the same vein.");
        }

        [Fact]
        public void Two_miners_share_the_credit_for_one_vein_by_how_much_each_dug()
        {
            CoreMap map = NewMap();
            Pawn novice = SpawnMiner(map, new IntVec3(0, 0, 0), level: 0, name: "Novice");
            Pawn expert = SpawnMiner(map, new IntVec3(0, 0, 1), level: 20, name: "Expert");
            Mineable vein = SpawnRock(map, new IntVec3(3, 0, 2), "MineableSteel");
            int half = vein.MaxHitPoints / 2;

            vein.Notify_TookMiningDamage(half, novice);
            float afterNovice = vein.YieldPct;
            vein.Notify_TookMiningDamage(half, expert);

            float expected = 0.5f * novice.GetStatValue(MiningStatDefOf.MiningYield) + 0.5f * expert.GetStatValue(MiningStatDefOf.MiningYield);
            Assert.InRange(afterNovice, 0.01f, 0.5f);
            Assert.Equal(expected, vein.YieldPct, 3);
            Assert.True(vein.YieldPct > afterNovice);
        }

        [Fact]
        public void Only_a_pick_hit_from_a_pawn_is_credited_to_a_veins_yield()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), level: 10);
            Mineable vein = SpawnRock(map, new IntVec3(3, 0, 2), "MineableSteel");

            vein.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 40f, instigator: miner)); // not a pick
            vein.TakeDamage(new DamageInfo(MiningDamageDefOf.Mining, 40f)); // no one swinging it
            Assert.Equal(0f, vein.YieldPct);

            vein.TakeDamage(new DamageInfo(MiningDamageDefOf.Mining, 40f, instigator: miner));
            Assert.True(vein.YieldPct > 0f);
        }

        [Fact]
        public void Plain_rock_credits_nothing_to_a_miners_skill_because_its_chunk_is_one_chunk()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 0), level: 20);
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 2), "Sandstone");
            Assert.False(rock.def.mineableYieldWasteable);

            rock.TakeDamage(new DamageInfo(MiningDamageDefOf.Mining, 80f, instigator: miner));

            Assert.Equal(0f, rock.YieldPct);
            Assert.Equal(rock.MaxHitPoints - 80, rock.HitPoints);
        }

        [Fact]
        public void Rock_drops_a_chunk_on_some_of_the_cells_dug_and_not_all_of_them()
        {
            // Through the driver this time: many small rocks dug by one fast miner. The drop chance itself is
            // an unsourced content number, so what is asserted is the band the chance implies, not the literal.
            CoreMap map = NewMap(40, 3);
            Pawn miner = SpawnMiner(map, new IntVec3(0, 0, 1), level: 20);
            var rocks = new List<Mineable>();
            for (int x = 2; x < 38; x++)
            {
                // A one-hit rock keeps the run short; the Def is otherwise the shipped sandstone.
                Mineable rock = SpawnMarkedRock(map, new IntVec3(x, 0, 1));
                rock.HitPoints = 1;
                rocks.Add(rock);
            }

            RunTicks(12000, miner);

            Assert.True(rocks.All(r => r.Destroyed), "Setup: every rock should have been dug in the time given.");
            int chunks = map.listerThings.ThingsOfDef(Def("ChunkSandstone")).Count;
            Assert.InRange(chunks, 1, rocks.Count - 1);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        // ---- Scribe ----

        [Fact]
        public void A_half_dug_rock_keeps_its_damage_its_credit_and_its_mark_through_a_save_and_load()
        {
            CoreMap map = NewMap();
            Pawn miner = SpawnMiner(map, new IntVec3(2, 0, 2), level: 20);
            Mineable vein = SpawnMarkedRock(map, new IntVec3(3, 0, 2), "MineableSteel");
            miner.jobs.StartJob(new Job(JobDefOf.Mine, vein));
            RunTicks(2 * JobDriver_Mine.TicksBetweenPickHits(miner) + 5, miner);
            Assert.True(vein.HitPoints < vein.MaxHitPoints && !vein.Destroyed);
            int hitPoints = vein.HitPoints;
            float credit = vein.YieldPct;
            Assert.True(credit > 0f);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            var loadedVein = (Mineable)loaded.listerThings.ThingsOfDef(Def("MineableSteel"))[0];
            Assert.Equal(hitPoints, loadedVein.HitPoints);
            Assert.Equal(credit, loadedVein.YieldPct, 5);
            Assert.True(loaded.designationManager.DesignationAt(loadedVein.Position, DesignationDefOf.Mine) != null);

            // And the loaded miner finishes it: the job, the mark and the half-credit all survived.
            var loadedMiner = (Pawn)loaded.mapPawns.AllPawns[0];
            RunTicks(4000, loadedMiner);
            Assert.True(loadedVein.Destroyed);
            Assert.True(loaded.listerThings.ThingsOfDef(Def("Steel")).Sum(t => t.stackCount) > 0);
            Assert.Empty(loaded.designationManager.AllDesignations);
        }
    }
}
