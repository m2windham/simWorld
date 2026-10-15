using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.AI.Group;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A raid that cannot get to the settlement does not stand on the map for ever (system 9: AI — RimWorld's
    /// <c>LordJob_AssaultColony</c>, <c>LordToil_ExitMap</c> and <c>JobGiver_ExitMapBest</c>).
    ///
    /// <para/><b>What stood for four days.</b> Three raiders of the storyteller bench's day-14 raid, spawned
    /// inside a limestone face. Their lord did its part — at 14.3 days, half a day after they landed, it gave up
    /// and handed them <c>ExitMapBest</c> — and it changed nothing, because a pawn on an impassable cell has no
    /// region to leave from. RimWorld has no such raiders (<see cref="RaidArrivalReachabilityTests"/> is why);
    /// what is pinned here is the rest of the guarantee: a raid that lands properly and finds the colony cut off
    /// from it goes home inside the give-up window, and one that really is stuck costs the game next to nothing.
    /// </summary>
    public class StrandedRaidTests : ContentTestBase
    {
        public StrandedRaidTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

        private static void Rock(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), c, map);

        private static (Faction ours, Faction raiders) Factions()
        {
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            var theirs = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");
            Find.FactionManager.Add(ours);
            Find.FactionManager.Add(theirs);
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            return (ours, theirs);
        }

        /// <summary>Lords tick with their map; a test that runs pawns alone has to tick the lords too.</summary>
        private static void TickLordsWithTheClock(CoreMap map) =>
            Find.TickManager.PostTickers.Add(_ => map.lordManager.LordManagerTick());

        // ---- a colony the raid cannot reach ----

        [Fact]
        public void A_raid_that_lands_and_finds_the_colony_walled_off_goes_home_inside_the_give_up_window()
        {
            (Faction ours, Faction theirs) = Factions();
            var map = new CoreMap(60, 60, TerrainDefOf.Soil);
            var colonist = NewHuman("Hermit");
            colonist.faction = ours;
            GenSpawn.Spawn(colonist, new IntVec3(30, 0, 30), map);
            for (int x = 28; x <= 32; x++)
            {
                for (int z = 28; z <= 32; z++)
                {
                    if (x == 28 || x == 32 || z == 28 || z == 32) Rock(map, new IntVec3(x, 0, z));
                }
            }

            Rand.Current = new RandomStream(21);
            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestRaidStranded",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = new CivilizationTarget { Map = map }, points = 400f, faction = theirs }));
            List<Pawn> squad = worker.LastRaidPawns!.ToList();
            Lord lord = Assert.Single(map.lordManager.lords);
            Assert.All(squad, r => Assert.True(r.Spawned));

            TickLordsWithTheClock(map);
            Pawn[] everyone = squad.Append(colonist).ToArray();

            // The window RimWorld gives an assault, plus the walk to the edge: the raid is over by then
            // whichever way it ends, and the assertion is the bound, not the moment.
            int window = LordJob_AssaultColony.AssaultTimeBeforeGiveUp.max + 3000;
            int ticks = 0;
            while (ticks < window && (squad.Any(r => r.Spawned) || map.lordManager.lords.Count > 0))
            {
                RunTicks(100, everyone);
                ticks += 100;
            }

            Assert.All(squad, r => Assert.False(r.Spawned, r.Name + " was still on the map at tick " + ticks));
            Assert.Empty(map.lordManager.lords);
            Assert.False(colonist.Dead || colonist.Downed, "the colony was walled off; nobody should have reached it");
        }

        // ---- a raid that is itself stuck ----

        [Fact]
        public void Raiders_that_cannot_move_at_all_cost_a_wait_not_a_think_every_tick()
        {
            // The shape of the bug, at the raid level: a squad in an assault lord, standing in rock, with a lord
            // that gives up on schedule and a duty to leave that nobody can carry out.
            (Faction ours, Faction theirs) = Factions();
            var map = new CoreMap(40, 40, TerrainDefOf.Soil);
            var colonist = NewHuman("Colonist");
            colonist.faction = ours;
            GenSpawn.Spawn(colonist, new IntVec3(10, 0, 10), map);

            var cell = new IntVec3(30, 0, 30);
            Rock(map, cell);
            var squad = new List<Pawn>();
            for (int i = 0; i < 3; i++)
            {
                Pawn raider = NewHuman("Raider" + i);
                raider.faction = theirs;
                GenSpawn.Spawn(raider, cell, map);
                squad.Add(raider);
            }
            Lord lord = LordMaker.MakeNewLord(theirs, new LordJob_AssaultColony(theirs), map, squad);
            TickLordsWithTheClock(map);

            const int ticks = 2000;
            int runs = CountThinkRuns(squad, () => RunTicks(ticks, squad.ToArray()));

            // Three raiders, a thousand-odd ticks each: a wait's worth of asking apiece, not a tick's.
            Assert.True(runs <= squad.Count * (ticks / JobGiver_IdleError.WaitTime + 2), "asked " + runs + " times in " + ticks + " ticks");
            Assert.All(squad, r => Assert.Equal(WaitJobDefOf.Wait, r.jobs.curJob?.def));
            Assert.True(lord.ticksInToil > 0, "the lord should still be ticking");
        }

        /// <summary>How many times the humanlike tree is asked for a job on behalf of <paramref name="whose"/> — and
        /// nobody else's: the colony's own people wander and wait through the same tree all the while.</summary>
        private static int CountThinkRuns(IReadOnlyCollection<Pawn> whose, Action body)
        {
            ThinkTreeDef tree = ThinkTreeDefOf.Humanlike;
            ThinkNode original = tree.thinkRoot;
            var counter = new CountingRoot(original, whose);
            tree.thinkRoot = counter;
            try
            {
                body();
            }
            finally
            {
                tree.thinkRoot = original;
            }
            return counter.Runs;
        }

        private sealed class CountingRoot : ThinkNode
        {
            private readonly ThinkNode inner;
            private readonly IReadOnlyCollection<Pawn> whose;
            public int Runs;

            public CountingRoot(ThinkNode inner, IReadOnlyCollection<Pawn> whose)
            {
                this.inner = inner;
                this.whose = whose;
            }

            public override ThinkResult TryIssueJobPackage(Pawn pawn)
            {
                if (whose.Contains(pawn)) Runs++;
                return inner.TryIssueJobPackage(pawn);
            }
        }
    }
}
