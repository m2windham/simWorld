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
    /// A raid that breaks and runs, or gives up and goes home (system 9: AI — <c>SimWorld.AI.Group</c>, a
    /// minimal port of RimWorld's Lord). Before this the raid had no memory of itself: every raider decided
    /// alone, from the map, and the only way a raid ended was that nobody was left for it to fight — so the
    /// last raiders held the map for twenty thousand ticks while the wounded waited (docs/perf/tend).
    ///
    /// <para/>What is pinned is RimWorld's rule, read from the 1.0 and 1.6 decompiles and written out on
    /// <see cref="Lord.SetJob"/> and <see cref="LordJob_AssaultColony"/>: flee once half the squad is down,
    /// give up after <see cref="LordJob_AssaultColony.AssaultTimeBeforeGiveUp"/>, leave the downed where they
    /// lie. Both thresholds are asserted on both sides — not yet, and then yes.
    /// </summary>
    public class RaidRetreatTests : ContentTestBase
    {
        public RaidRetreatTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

        private static CoreMap NewMap(int size) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static (Faction ours, Faction theirs) HostilePair(string theirDef = "TribalCivilization")
        {
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            var theirs = new Faction(DefDatabase<FactionDef>.GetNamed(theirDef), "Theirs", "F_Theirs");
            Find.FactionManager.Add(ours);
            Find.FactionManager.Add(theirs);
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            return (ours, theirs);
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction faction, string name, string? weapon = null)
        {
            Pawn pawn = NewHuman(name);
            pawn.faction = faction;
            if (weapon != null) pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(weapon)));
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        /// <summary>A squad of <paramref name="count"/> in a row, made into one raid exactly as
        /// <c>IncidentWorker_RaidEnemy</c> makes one.</summary>
        private static (Lord lord, List<Pawn> squad) Raid(CoreMap map, Faction raiders, int count, IntVec3 first)
        {
            var squad = new List<Pawn>();
            for (int i = 0; i < count; i++) squad.Add(SpawnHuman(map, first + new IntVec3(i, 0), raiders, "Raider" + i));
            Lord lord = LordMaker.MakeNewLord(raiders, new LordJob_AssaultColony(raiders), map, squad);
            return (lord, squad);
        }

        /// <summary>Lords tick with their map; a test that runs pawns alone has to tick the lords too.</summary>
        private static void TickLordsWithTheClock(CoreMap map) =>
            Find.TickManager.PostTickers.Add(_ => map.lordManager.LordManagerTick());

        private static Trigger_TicksPassed GiveUpTrigger(Lord lord) =>
            lord.Graph!.transitions.SelectMany(t => t.triggers).OfType<Trigger_TicksPassed>().Single();

        // ---- the flee ----

        [Fact]
        public void A_raid_that_loses_half_its_number_breaks_and_runs()
        {
            CoreMap map = NewMap(60);
            (_, Faction raiders) = HostilePair();
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 4, new IntVec3(28, 30));

            Assert.IsType<LordToil_AssaultColony>(lord.CurLordToil);
            Assert.All(squad, r => Assert.Same(DutyDefOf.AssaultSettlement, r.mindState.duty));
            Assert.Equal(4, lord.numPawnsEverGained);

            // One of four down: a quarter lost, and the raid presses on.
            squad[0].health.ForceDowned = true;
            map.lordManager.LordManagerTick();
            Assert.Equal(1, lord.numPawnsLostViolently);
            Assert.IsType<LordToil_AssaultColony>(lord.CurLordToil);
            Assert.Same(DutyDefOf.AssaultSettlement, squad[1].mindState.duty);

            // Two of four: half, and the rest break.
            squad[1].health.ForceDowned = true;
            map.lordManager.LordManagerTick();
            Assert.Equal(2, lord.numPawnsLostViolently);
            Assert.IsType<LordToil_PanicFlee>(lord.CurLordToil);
            Assert.Same(LordDutyDefOf.PanicFlee, squad[2].mindState.duty);
            Assert.Same(LordDutyDefOf.PanicFlee, squad[3].mindState.duty);

            // The player is told, in RimWorld's words, and the chronicle keeps it: the first raid ever to
            // break is a moment (MomentCurator's first-of-its-kind rule reads the text before the colon).
            Assert.Contains(Find.LetterStack.LettersListForReading, l => l.text == "Tribespeople from Theirs are fleeing.");
            ChronicleEntry entry = Find.Storyteller.Chronicle.Last();
            Assert.Equal("Raiders fleeing: Tribespeople from Theirs are fleeing.", entry.incidentDefName);
            Assert.True(entry.isMoment);
        }

        [Fact]
        public void A_raider_who_walks_away_is_not_a_casualty()
        {
            // Trigger_FractionPawnsLost counts violent losses only: a raider who left the map was not beaten,
            // and two of four walking off is not half the squad down.
            CoreMap map = NewMap(60);
            (_, Faction raiders) = HostilePair();
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 4, new IntVec3(28, 30));

            squad[0].DeSpawn();
            squad[1].DeSpawn();
            map.lordManager.LordManagerTick();

            Assert.Equal(0, lord.numPawnsLostViolently);
            Assert.Equal(2, lord.ownedPawns.Count);
            Assert.IsType<LordToil_AssaultColony>(lord.CurLordToil);
        }

        [Fact]
        public void A_faction_whose_def_says_it_does_not_flee_fights_on()
        {
            // RoughOutlanders ship autoFlee=false. RimWorld adds the flee toil only for a faction that sets it
            // (Lord.SetJob), so losing half changes nothing; the timeout is still there.
            CoreMap map = NewMap(60);
            (_, Faction raiders) = HostilePair("RoughOutlanders");
            Assert.False(raiders.def.autoFlee);
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 4, new IntVec3(28, 30));

            Assert.DoesNotContain(lord.Graph!.lordToils, t => t is LordToil_PanicFlee);
            squad[0].health.ForceDowned = true;
            squad[1].health.ForceDowned = true;
            squad[2].health.ForceDowned = true;
            map.lordManager.LordManagerTick();

            Assert.IsType<LordToil_AssaultColony>(lord.CurLordToil);
            Assert.Same(DutyDefOf.AssaultSettlement, squad[3].mindState.duty);
            Assert.NotNull(GiveUpTrigger(lord));
        }

        // ---- the timeout ----

        [Fact]
        public void A_raid_that_has_been_at_it_long_enough_gives_up_and_leaves()
        {
            CoreMap map = NewMap(60);
            (_, Faction raiders) = HostilePair();
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 3, new IntVec3(28, 30));

            // RimWorld's range, rolled once per raid from the lord's own seed.
            int duration = GiveUpTrigger(lord).Duration;
            Assert.InRange(duration, LordJob_AssaultColony.AssaultTimeBeforeGiveUp.min, LordJob_AssaultColony.AssaultTimeBeforeGiveUp.max);

            // Trigger_TicksPassed fires on the tick the count goes past the duration, not the tick it reaches it.
            for (int i = 0; i < duration; i++) map.lordManager.LordManagerTick();
            Assert.IsType<LordToil_AssaultColony>(lord.CurLordToil);
            Assert.All(squad, r => Assert.Same(DutyDefOf.AssaultSettlement, r.mindState.duty));

            map.lordManager.LordManagerTick();
            Assert.IsType<LordToil_ExitMap>(lord.CurLordToil);
            Assert.All(squad, r => Assert.Same(LordDutyDefOf.ExitMapBest, r.mindState.duty));
            Assert.Contains(Find.LetterStack.LettersListForReading, l => l.text == "Tribespeople from Theirs have given up and are leaving.");
        }

        [Fact]
        public void Two_raids_on_one_map_do_not_share_a_give_up_time()
        {
            // Rolled from each lord's own seed, never the ambient stream: the second raid is not the first
            // one's twin, and making either moved nobody else's dice.
            CoreMap map = NewMap(60);
            (_, Faction raiders) = HostilePair();
            Pawn[] a = { SpawnHuman(map, new IntVec3(10, 10), raiders, "A") };
            Pawn[] b = { SpawnHuman(map, new IntVec3(40, 40), raiders, "B") };

            Rand.Current = new RandomStream(777);
            Lord first = LordMaker.MakeNewLord(raiders, new LordJob_AssaultColony(raiders), map, a);
            Lord second = LordMaker.MakeNewLord(raiders, new LordJob_AssaultColony(raiders), map, b);

            Assert.Equal(new RandomStream(777).Int, Rand.Int);
            Assert.NotEqual(GiveUpTrigger(first).Duration, GiveUpTrigger(second).Duration);
        }

        // ---- the downed and the departed ----

        [Fact]
        public void Downed_raiders_stay_where_they_fall()
        {
            CoreMap map = NewMap(60);
            (_, Faction raiders) = HostilePair();
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 2, new IntVec3(28, 30));
            TickLordsWithTheClock(map);

            Pawn down = squad[0];
            down.health.ForceDowned = true;
            IntVec3 fell = down.Position;
            RunTicks(4000, squad.ToArray());

            // Out of the lord — the half of the squad left broke and ran — but still here, where it fell,
            // to be captured or to die.
            Assert.DoesNotContain(down, lord.ownedPawns);
            Assert.True(down.Spawned);
            Assert.Equal(fell, down.Position);
            Assert.False(squad[1].Spawned, "the raider left standing did not leave");

            // It carries the way out for if it gets up: RimWorld's think tree walks a self-healed raider off
            // the map, and this port's tree has no such tier (see Lord.RemovePawn).
            Assert.Same(LordDutyDefOf.ExitMapBest, down.mindState.duty);
        }

        [Fact]
        public void Fleeing_raiders_leave_the_map_and_are_not_counted_dead()
        {
            CoreMap map = NewMap(60);
            (Faction ours, Faction raiders) = HostilePair();
            Pawn citizen = SpawnHuman(map, new IntVec3(30, 34), ours, "Citizen");
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 4, new IntVec3(28, 30));
            TickLordsWithTheClock(map);

            squad[0].health.ForceDowned = true;
            squad[1].health.ForceDowned = true;
            var everyone = new List<Pawn>(squad) { citizen };
            RunTicks(4000, everyone.ToArray());

            Pawn[] fled = { squad[2], squad[3] };
            Assert.All(fled, r =>
            {
                Assert.False(r.Spawned, r.Label + " never got off the map");
                Assert.False(r.Dead);
                Assert.DoesNotContain(r, map.mapPawns.AllPawns);
            });

            // They broke rather than won: the citizen a step away was never taken down on the way out.
            Assert.False(citizen.Downed, "the raid finished the fight instead of running from it");

            // Every member lost, so the lord is gone; and a raider who got away is nobody's death.
            Assert.Empty(map.lordManager.lords);
            Assert.Equal(0, Find.Storyteller.deaths.Total);
            Assert.Equal(0, Find.Storyteller.deaths.TotalAttributed);
        }

        [Fact]
        public void A_fleeing_raider_does_not_turn_to_fight_at_range()
        {
            // An armed raider acquires at forty cells while it assaults. Handed the flee, it looks no further
            // than arm's length — RimWorld's fleeing raider has no fight tier at all — and walks out past a
            // citizen it would otherwise have shot.
            CoreMap map = NewMap(60);
            (Faction ours, Faction raiders) = HostilePair();
            Pawn citizen = SpawnHuman(map, new IntVec3(30, 40), ours, "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(30, 30), raiders, "Archer", "Bow_Short");
            LordMaker.MakeNewLord(raiders, new LordJob_AssaultColony(raiders), map, new[] { raider });

            Assert.Equal(CombatAITuning.TargetAcquireRadius, CombatPostureUtility.TargetAcquireRadiusFor(raider));
            raider.jobs.TryFindAndStartJob();
            Assert.Same(CombatAIDefOf.AttackStatic, raider.jobs.curJob?.def);

            raider.mindState.duty = LordDutyDefOf.PanicFlee;
            raider.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            Assert.Equal(CombatAITuning.MeleeReachCells, CombatPostureUtility.TargetAcquireRadiusFor(raider));
            raider.jobs.TryFindAndStartJob();
            Assert.Same(DutyJobDefOf.ExitMap, raider.jobs.curJob?.def);
            Assert.Same(citizen, AttackTargetFinder.BestAttackTarget(raider, CombatAITuning.TargetAcquireRadius));
        }

        [Fact]
        public void A_captured_raider_is_not_walked_off_the_map()
        {
            // The exit duty a lost raider keeps must not turn into a prison break.
            CoreMap map = NewMap(60);
            (Faction ours, Faction raiders) = HostilePair();
            Pawn warden = SpawnHuman(map, new IntVec3(10, 10), ours, "Warden");
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 2, new IntVec3(28, 30));
            Pawn captive = squad[0];

            captive.health.ForceDowned = true;
            map.lordManager.LordManagerTick();
            Assert.Same(LordDutyDefOf.ExitMapBest, captive.mindState.duty);
            Assert.NotNull(CaptureUtility.Capture(warden, captive));
            captive.health.ForceDowned = false;

            captive.jobs.TryFindAndStartJob();
            Assert.NotSame(DutyJobDefOf.ExitMap, captive.jobs.curJob?.def);
            Assert.DoesNotContain(captive, lord.ownedPawns);
        }

        // ---- arrival ----

        [Fact]
        public void A_raid_arrives_as_one_lord()
        {
            CoreMap map = NewMap(80);
            (_, Faction raiders) = HostilePair();
            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestRaidRetreat",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = new CivilizationTarget { Map = map }, points = 400f, faction = raiders }));
            IReadOnlyList<Pawn> squad = worker.LastRaidPawns!;

            Lord lord = Assert.Single(map.lordManager.lords);
            Assert.Same(raiders, lord.faction);
            Assert.Equal(squad.Count, lord.ownedPawns.Count);
            Assert.All(squad, r => Assert.Same(lord, r.GetLord()));
            Assert.All(squad, r => Assert.Same(DutyDefOf.AssaultSettlement, r.mindState.duty));
        }

        // ---- save ----

        private sealed class SaveRoot : IExposable
        {
            public CoreMap? map;
            public List<Faction> factions = new List<Faction>();

            public void ExposeData()
            {
                CoreMap? m = map;
                Scribe_Deep.Look(ref m, "map");
                map = m;
                List<Faction>? f = factions;
                Scribe_Collections.Look(ref f, "factions", LookMode.Deep);
                factions = f ?? new List<Faction>();
            }
        }

        [Fact]
        public void A_raid_saved_mid_flee_loads_still_fleeing_and_still_leaves()
        {
            CoreMap map = NewMap(60);
            (Faction ours, Faction raiders) = HostilePair();
            (Lord lord, List<Pawn> squad) = Raid(map, raiders, 4, new IntVec3(28, 30));
            squad[0].health.ForceDowned = true;
            squad[1].health.ForceDowned = true;
            map.lordManager.LordManagerTick();
            Assert.IsType<LordToil_PanicFlee>(lord.CurLordToil);
            int duration = GiveUpTrigger(lord).Duration;

            string xml = Scribe.SaveToString(new SaveRoot { map = map, factions = new List<Faction> { ours, raiders } }, "root");
            Pawn.ResetThingIdCounter();
            SaveRoot loadedRoot = Scribe.Load<SaveRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            CoreMap loaded = loadedRoot.map!;

            Lord back = Assert.Single(loaded.lordManager.lords);
            Assert.IsType<LordToil_PanicFlee>(back.CurLordToil);
            Assert.Equal(4, back.numPawnsEverGained);
            Assert.Equal(2, back.numPawnsLostViolently);
            Assert.Equal(lord.graphSeed, back.graphSeed);
            Assert.Equal(duration, GiveUpTrigger(back).Duration);
            Assert.Equal("Theirs", back.faction!.name);
            Assert.Equal(new[] { "Raider2", "Raider3" }, back.ownedPawns.Select(p => p.name).OrderBy(n => n));
            Assert.All(back.ownedPawns, p => Assert.Same(LordDutyDefOf.PanicFlee, p.mindState.duty));

            // And it goes on doing what it was doing: the two still standing leave.
            TickLordsWithTheClock(loaded);
            RunTicks(4000, loaded.mapPawns.AllPawnsSpawned.ToArray());
            Assert.Empty(loaded.lordManager.lords);
            Assert.Equal(2, loaded.mapPawns.AllPawnsSpawned.Count(p => p.Downed));
        }

        [Fact]
        public void A_raid_saved_mid_assault_keeps_its_clock()
        {
            CoreMap map = NewMap(60);
            (Faction ours, Faction raiders) = HostilePair();
            (Lord lord, _) = Raid(map, raiders, 2, new IntVec3(28, 30));
            for (int i = 0; i < 1234; i++) map.lordManager.LordManagerTick();
            int left = GiveUpTrigger(lord).TicksLeft;

            string xml = Scribe.SaveToString(new SaveRoot { map = map, factions = new List<Faction> { ours, raiders } }, "root");
            Pawn.ResetThingIdCounter();
            SaveRoot loadedRoot = Scribe.Load<SaveRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Lord back = Assert.Single(loadedRoot.map!.lordManager.lords);
            Assert.IsType<LordToil_AssaultColony>(back.CurLordToil);
            Assert.Equal(left, GiveUpTrigger(back).TicksLeft);
            Assert.Equal(1234, back.ticksInToil);
        }
    }
}
