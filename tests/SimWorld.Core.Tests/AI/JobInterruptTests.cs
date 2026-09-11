using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
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
    /// Nothing interrupts a job in flight — until now (system 9: AI — interrupts). The think tree was only
    /// ever consulted when <see cref="Pawn_JobTracker.curJob"/> was null, so a pawn already doing something
    /// did not react to anything until that something ended: a sleeping citizen slept through being shot, and
    /// a hauler carried its rock across a battlefield. Two mechanisms close it, and they are deliberately
    /// separate — the constant think tree (a pawn <i>sees</i> a threat) and
    /// <see cref="Pawn_JobTracker.Notify_DamageTaken"/> (a pawn <i>is hit</i>) — because those are different
    /// strengths of claim on a pawn's attention, and <c>JobDefs_Core.xml</c>'s <c>LayDown</c> is the case that
    /// proves it: asleep is not a state you notice a raider from, but it is very much a state you are woken
    /// out of.
    /// <para/>
    /// Every tuned number here (<see cref="ConstantThinkTreeTuning"/>) is asserted as a bound or an ordering,
    /// never as its literal, per CLAUDE.md.
    /// </summary>
    public class JobInterruptTests : ContentTestBase
    {
        public JobInterruptTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Faction NewFaction(string name, string defName = "TribalCivilization")
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static (Faction ours, Faction theirs) HostilePair()
        {
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Faction theirs = NewFaction("Theirs");
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            return (ours, theirs);
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction? faction = null, string? weaponDefName = null, string name = "Test")
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            if (weaponDefName != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weaponDefName)));
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnItem(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            thing.stackCount = count;
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static Zone_Stockpile NewStockpile(CoreMap map, string allowedDefName, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            zone.filter.SetAllow(Def(allowedDefName), true);
            return zone;
        }

        private static bool IsAttackJob(Job? job) =>
            job != null && (job.def == CombatAIDefOf.AttackMelee || job.def == CombatAIDefOf.AttackStatic);

        /// <summary>One hit, small enough that nobody goes down from it — this module is about what a pawn
        /// <i>decides</i> when hurt, not about lethality, which <c>CombatAITests</c> already covers.</summary>
        private static void Hit(Pawn victim, Pawn instigator, float amount = 3f) =>
            DamageDefOf.Cut.Worker.Apply(new DamageInfo(DamageDefOf.Cut, amount, 0f, instigator), victim);

        /// <summary>Puts a pawn to sleep the way the game does — tired enough that the needs tier issues
        /// <c>LayDown</c> on its own — rather than by setting the flag behind the job system's back.</summary>
        private static Pawn SleepingCitizen(CoreMap map, IntVec3 cell, Faction faction, string? weaponDefName, string name)
        {
            Pawn p = SpawnHuman(map, cell, faction, weaponDefName, name);
            p.needs.rest!.CurLevel = 0.1f;
            RunTicks(5, p);
            Assert.Equal(JobDefOf.LayDown, p.jobs.curJob?.def);
            Assert.True(p.Asleep, "The citizen was supposed to be asleep before the test starts.");
            return p;
        }

        /// <summary>Every job giver reachable in a think tree, walked through the two node kinds content can
        /// build a tree out of.</summary>
        private static IEnumerable<ThinkNode> AllNodes(ThinkNode root)
        {
            yield return root;
            List<ThinkNode>? children = root switch
            {
                ThinkNode_Priority priority => priority.subNodes,
                ThinkNode_Conditional conditional => conditional.subNodes,
                _ => null,
            };
            if (children == null) yield break;
            foreach (ThinkNode child in children)
            {
                foreach (ThinkNode node in AllNodes(child)) yield return node;
            }
        }

        // ---- content ----

        [Fact]
        public void The_constant_trees_load_and_nothing_but_the_danger_tier_sits_in_them()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ConstantThinkTreeDefOf.HumanlikeConstant);
            Assert.NotNull(ConstantThinkTreeDefOf.AnimalConstant);

            foreach (ThinkTreeDef tree in new[] { ConstantThinkTreeDefOf.HumanlikeConstant, ConstantThinkTreeDefOf.AnimalConstant })
            {
                List<ThinkNode> nodes = AllNodes(tree.thinkRoot).ToList();

                // The gate is not optional: without it every job in the game becomes droppable, and the
                // "asleep is not a state you notice a raider from" half of this module disappears.
                Assert.Contains(nodes, n => n is ThinkNode_ConditionalCanDoConstantThinkTreeJobNow);

                // And the answer to "a pawn should not abandon surgery because someone walked past" is that
                // nothing which could offer that pawn a different job is in here at all.
                Assert.All(
                    nodes.OfType<ThinkNode_JobGiver>(),
                    giver => Assert.IsAssignableFrom<JobGiver_AIFightEnemies>(giver));
            }
        }

        [Fact]
        public void Only_sleep_refuses_a_casual_interrupt_and_every_job_reconsiders_itself_when_hurt()
        {
            List<JobDef> jobs = DefDatabase<JobDef>.AllDefsListForReading.ToList();
            Assert.True(jobs.Count > 10);

            // Content, not code, decides which jobs survive seeing a threat — and exactly one does.
            Assert.False(JobDefOf.LayDown.casualInterruptible);
            Assert.Equal(new[] { JobDefOf.LayDown }, jobs.Where(j => !j.casualInterruptible).ToArray());

            // The damage path is the one every job is on, including the sleep that refuses the other path.
            Assert.All(jobs, j => Assert.Equal(CheckJobOverrideOnDamageMode.OnlyIfInstigatorNotJobTarget, j.checkOverrideOnDamage));
        }

        // ---- the headline: a sleeping pawn under fire ----

        [Fact]
        public void A_sleeping_pawn_shot_at_wakes_and_fights_back()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SleepingCitizen(map, new IntVec3(5, 0, 5), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(9, 0, 5), theirs, "Gun_Revolver", "Raider");

            Hit(citizen, raider);

            // Waking is unconditional and immediate — it does not wait for the think tree to approve of it.
            Assert.False(citizen.Asleep, "A pawn shot in its sleep slept on. This is the case the module exists for.");
            Assert.False(citizen.Dead);
            Assert.False(citizen.Downed);

            // And responding is the think tree's own answer, reached through the same danger tier that was
            // already there and already unreachable from inside a running job.
            Assert.True(IsAttackJob(citizen.jobs.curJob), "The woken citizen did not react to the pawn shooting it.");
            Assert.Same(raider, citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);

            // The fight it woke into is a real one, driven by the ordinary drivers.
            for (int i = 0; i < 12000 && !raider.Downed && !raider.Dead && !citizen.Downed && !citizen.Dead; i++)
            {
                RunTicks(1, citizen, raider);
            }
            Assert.True(raider.Downed || raider.Dead || citizen.Downed || citizen.Dead,
                "Two armed enemies traded fire for hours after the wake-up and nothing came of it.");
        }

        [Fact]
        public void A_sleeping_pawn_is_not_pulled_out_of_bed_by_an_enemy_it_has_merely_seen()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SleepingCitizen(map, new IntVec3(5, 0, 5), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(9, 0, 5), theirs, name: "Raider"); // unarmed: it will not shoot

            // Comfortably more than one constant-tree interval, so this is "it did not fire", not "it has not
            // fired yet" — and not the literal interval either.
            RunTicks(ConstantThinkTreeTuning.IntervalTicks * 6, citizen);

            Assert.True(citizen.Asleep, "A sleeping colonist got out of bed because a raider walked into the room.");
            Assert.Equal(JobDefOf.LayDown, citizen.jobs.curJob?.def);
            Assert.True(raider.Spawned);
        }

        // ---- the constant tree: seeing a threat while busy ----

        [Fact]
        public void A_pawn_mid_haul_drops_it_and_fights_when_a_hostile_appears()
        {
            CoreMap map = NewMap(16, 16);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0), ours, "Gun_Revolver", "Hauler");
            SpawnItem(map, new IntVec3(12, 0, 12), "WoodLog", 10);
            NewStockpile(map, "WoodLog", new IntVec3(1, 0, 1));

            RunTicks(3, citizen);
            Assert.Equal(JobDefOf.HaulToCell, citizen.jobs.curJob?.def);

            Pawn raider = SpawnHuman(map, new IntVec3(4, 0, 4), theirs, "Gun_Revolver", "Raider");
            RunTicks(ConstantThinkTreeTuning.IntervalTicks * 2, citizen);

            Assert.True(IsAttackJob(citizen.jobs.curJob), "The hauler carried its rock across a firefight.");
            Assert.Same(raider, citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);

            // Dropping a job mid-carry must not eat the goods — JobDriver_HaulToCell.Notify_Ending puts them
            // down, and an interrupt is the first thing in this codebase that routinely triggers it.
            Assert.Equal(10, map.listerThings.ThingsOfDef(Def("WoodLog")).Sum(t => t.stackCount));
        }

        [Fact]
        public void A_pawn_does_not_abandon_its_job_for_anyone_who_is_not_a_threat()
        {
            CoreMap map = NewMap(16, 16);
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Faction neutrals = NewFaction("Neutrals");

            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0), ours, "Gun_Revolver", "Hauler");
            SpawnItem(map, new IntVec3(12, 0, 12), "WoodLog", 10);
            NewStockpile(map, "WoodLog", new IntVec3(1, 0, 1));

            RunTicks(3, citizen);
            Assert.Equal(JobDefOf.HaulToCell, citizen.jobs.curJob?.def);

            // Everybody who is not an enemy, standing right on top of the worker: an ally, a neutral trader,
            // and a wild animal. None of them is a job the constant tree can offer, so none of them is an
            // interrupt — no rule needed, just an empty answer.
            SpawnHuman(map, new IntVec3(1, 0, 0), ours, name: "Ally");
            SpawnHuman(map, new IntVec3(0, 0, 1), neutrals, name: "Trader");
            var muffalo = new Pawn(Def("Muffalo"), "Beast");
            GenSpawn.Spawn(muffalo, new IntVec3(1, 0, 1), map);

            for (int i = 0; i < ConstantThinkTreeTuning.IntervalTicks * 6; i++)
            {
                RunTicks(1, citizen);
                Assert.False(IsAttackJob(citizen.jobs.curJob), "The worker attacked someone who was merely standing there.");
            }
        }

        [Fact]
        public void A_job_the_player_ordered_is_not_dropped_for_a_threat_the_pawn_has_only_seen()
        {
            CoreMap map = NewMap(16, 16);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0), ours, "Gun_Revolver", "Hauler");
            SpawnItem(map, new IntVec3(12, 0, 12), "WoodLog", 10);
            NewStockpile(map, "WoodLog", new IntVec3(1, 0, 1));

            RunTicks(3, citizen);
            Job haul = citizen.jobs.curJob!;
            Assert.Equal(JobDefOf.HaulToCell, haul.def);
            haul.playerForced = true;

            // Unarmed and out of reach: this raider can be *seen* and cannot land a hit, which is exactly the
            // line this test is about. Being hurt is a stronger claim than being seen and goes down the other
            // path (Notify_DamageTaken), which deliberately does not consult playerForced at all.
            SpawnHuman(map, new IntVec3(4, 0, 4), theirs, name: "Raider");
            RunTicks(ConstantThinkTreeTuning.IntervalTicks * 4, citizen);

            Assert.Same(haul, citizen.jobs.curJob);
        }

        [Fact]
        public void A_pawn_already_fighting_its_enemy_is_not_restarted_by_the_constant_tree()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(12, 0, 4), theirs, name: "Raider");

            citizen.jobs.TryFindAndStartJob();
            Job fight = citizen.jobs.curJob!;
            Assert.Equal(CombatAIDefOf.AttackStatic, fight.def);
            int startedAt = fight.startTick;

            RunTicks(ConstantThinkTreeTuning.IntervalTicks * 5, citizen);

            // Same Job object, same startTick: the constant tree proposes this same attack every interval and
            // must not be allowed to rewind the driver, retake the reservations or push the expiry out.
            Assert.Same(fight, citizen.jobs.curJob);
            Assert.Equal(startedAt, citizen.jobs.curJob!.startTick);
            Assert.True(raider.Spawned);
        }

        // ---- the damage path: who re-decides, and how often ----

        [Fact]
        public void Being_hit_by_the_enemy_you_are_already_fighting_does_not_re_decide_but_a_third_party_does()
        {
            CoreMap map = NewMap(40, 40);
            (Faction ours, Faction theirs) = HostilePair();

            // Two identical situations twenty cells apart, so neither citizen's enemies are in the running
            // for the other's think tree: a pawn shooting a distant enemy while a nearer one stands by.
            Pawn stubborn = SpawnHuman(map, new IntVec3(10, 0, 10), ours, "Gun_Revolver", "Stubborn");
            Pawn engagedA = SpawnHuman(map, new IntVec3(25, 0, 10), theirs, name: "EngagedA");
            Pawn nearerA = SpawnHuman(map, new IntVec3(12, 0, 10), theirs, name: "NearerA");

            Pawn switcher = SpawnHuman(map, new IntVec3(10, 0, 30), ours, "Gun_Revolver", "Switcher");
            Pawn engagedB = SpawnHuman(map, new IntVec3(25, 0, 30), theirs, name: "EngagedB");
            Pawn nearerB = SpawnHuman(map, new IntVec3(12, 0, 30), theirs, name: "NearerB");
            Pawn thirdParty = SpawnHuman(map, new IntVec3(10, 0, 36), theirs, name: "ThirdParty");

            stubborn.jobs.StartJob(new Job(CombatAIDefOf.AttackStatic, engagedA));
            switcher.jobs.StartJob(new Job(CombatAIDefOf.AttackStatic, engagedB));

            // Hit by the pawn it is already shooting at: nothing to reconsider, even though a nearer enemy
            // exists and the think tree would certainly pick it.
            Hit(stubborn, engagedA);
            Assert.Same(engagedA, stubborn.jobs.curJob!.GetTarget(TargetIndex.A).Thing);

            // Hit by somebody else: re-decide. Note what it re-decides *to* — the tree's own nearest-enemy
            // answer, not the pawn that pulled the trigger, which is further away than nearerB is.
            Hit(switcher, thirdParty);
            Assert.Same(nearerB, switcher.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void A_burst_of_hits_buys_one_reconsideration_not_one_per_bullet()
        {
            CoreMap map = NewMap(40, 40);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(10, 0, 10), ours, "Gun_Revolver", "Citizen");
            Pawn engaged = SpawnHuman(map, new IntVec3(25, 0, 10), theirs, name: "Engaged");
            Pawn nearer = SpawnHuman(map, new IntVec3(14, 0, 10), theirs, name: "Nearer");
            Pawn shooter = SpawnHuman(map, new IntVec3(10, 0, 25), theirs, name: "Shooter");

            citizen.jobs.StartJob(new Job(CombatAIDefOf.AttackStatic, engaged));

            Hit(citizen, shooter);
            Assert.Same(nearer, citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);

            // A closer enemy arrives, and the same burst keeps landing on the same tick. The second hit is
            // inside the throttle window, so it costs no think-tree pass and the citizen keeps its target.
            Pawn nearest = SpawnHuman(map, new IntVec3(11, 0, 10), theirs, name: "Nearest");
            Hit(citizen, shooter);
            Assert.Same(nearer, citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.True(nearest.Spawned);
        }

        // ---- tiering: none of this exists below Full ----

        [Fact]
        public void Neither_interrupt_costs_an_Interval_citizen_anything()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SleepingCitizen(map, new IntVec3(5, 0, 5), ours, "Gun_Revolver", "Citizen");

            // Demote the way a real attention change would (see SettlementTests): attended, then not.
            citizen.tier.Notify_AttentionChanged(true);
            citizen.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Interval, citizen.tier.Tier);

            // Demotion ends whatever the citizen was doing and takes them off the Normal tick list entirely
            // (Pawn_TierTracker.Demote) — an Interval citizen has no jobs at all, which is the property this
            // module must not quietly undo.
            Assert.Null(citizen.jobs.curJob);
            citizen.Asleep = true; // the coarse-tier state a sleeping citizen carries with no job behind it

            Pawn raider = SpawnHuman(map, new IntVec3(6, 0, 5), theirs, "Gun_Revolver", "Raider");
            RunTicks(ConstantThinkTreeTuning.IntervalTicks * 6, citizen);

            // The constant tree never ran: JobTrackerTick — and with it every line this module added — is
            // never reached for a pawn off the Normal list, so a hostile standing over an Interval citizen
            // costs exactly nothing.
            Assert.Null(citizen.jobs.curJob);
            Assert.True(citizen.Asleep);

            // And damage, which arrives from outside the tick loop and so reaches a citizen at any tier,
            // stops at the tier check — before even the wake, which is otherwise unconditional — rather than
            // starting a job nothing would ever tick.
            Hit(citizen, raider);
            Assert.True(citizen.Asleep, "An Interval citizen was woken into a job its tier never ticks.");
            Assert.Null(citizen.jobs.curJob);
        }

        // ---- scribe ----

        /// <summary>Map plus factions in one document — a job's target is a pawn and every pawn carries a
        /// faction cross-reference, so the roster has to travel with the map (same reason
        /// <c>CombatAITests.FightRoot</c> exists).</summary>
        private sealed class InterruptRoot : IExposable
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
        public void A_pawn_interrupted_mid_job_round_trips_through_Scribe_throttle_and_all()
        {
            CoreMap map = NewMap(40, 40);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(10, 0, 10), ours, "Gun_Revolver", "Citizen");
            Pawn engaged = SpawnHuman(map, new IntVec3(25, 0, 10), theirs, name: "Engaged");
            SpawnHuman(map, new IntVec3(14, 0, 10), theirs, name: "Nearer");
            Pawn shooter = SpawnHuman(map, new IntVec3(10, 0, 25), theirs, name: "Shooter");

            citizen.jobs.StartJob(new Job(CombatAIDefOf.AttackStatic, engaged));
            Hit(citizen, shooter);
            Assert.Equal("Nearer", ((Pawn)citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing!).name);

            var root = new InterruptRoot { map = map, factions = new List<Faction> { ours, theirs } };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            InterruptRoot loadedRoot = Scribe.Load<InterruptRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            CoreMap loaded = loadedRoot.map!;

            Pawn loadedCitizen = loaded.mapPawns.AllPawns.First(p => p.name == "Citizen");
            Pawn loadedShooter = loaded.mapPawns.AllPawns.First(p => p.name == "Shooter");

            // The interrupted-into job survived, target and all.
            Assert.Equal(CombatAIDefOf.AttackStatic, loadedCitizen.jobs.curJob?.def);
            Assert.Equal("Nearer", ((Pawn)loadedCitizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing!).name);

            // And so did the throttle: a hit on the same tick as the pre-save one still buys no second
            // think-tree pass, so the newly-nearest enemy below does not steal the target.
            Pawn newest = SpawnHuman(loaded, new IntVec3(11, 0, 10), loadedRoot.factions.First(f => f.name == "Theirs"), name: "Newest");
            Hit(loadedCitizen, loadedShooter);
            Assert.Equal("Nearer", ((Pawn)loadedCitizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing!).name);
            Assert.True(newest.Spawned);

            // The reloaded fight is a live one, not a frozen job: it resolves on its own.
            Pawn loadedTarget = (Pawn)loadedCitizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing!;
            for (int i = 0; i < 12000 && !loadedTarget.Downed && !loadedTarget.Dead && !loadedCitizen.Downed && !loadedCitizen.Dead; i++)
            {
                RunTicks(1, loadedCitizen, loadedTarget);
            }
            Assert.True(loadedTarget.Downed || loadedTarget.Dead || loadedCitizen.Downed || loadedCitizen.Dead,
                "The reloaded fight never resolved.");
        }
    }
}
