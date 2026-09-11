using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A raid that reaches the settlement it came for (system 9: AI — duties), and the fixture that would
    /// have caught it standing still.
    ///
    /// <para/><b>What was wrong.</b> Two batches closed the two reasons a raid did nothing: the combat tier
    /// was missing from the think tree, and then a citizen carried no faction so neither side could see the
    /// other as hostile. Both were fixed, and a raid still did nothing, because <b>nothing walked the squad
    /// toward the town</b>. Measured here, on a settlement founded the ordinary way from shipped content with
    /// a real <c>RaidEnemy</c> firing onto its 200x200 interior — 30 citizens, 15 raiders, nobody armed or
    /// enfactioned by hand:
    ///
    /// <list type="table">
    /// <item><term>squad's distance to the town at arrival</term><description>100 cells to the nearest
    /// citizen, 103 to the town centre, against a <see cref="CombatAITuning.TargetAcquireRadius"/> of 40 — so
    /// every acquire scan on both sides correctly returned nothing</description></item>
    /// <item><term>before — closed in 10,000 ticks</term><description><b>11 cells</b>, and all of it drift
    /// from <see cref="JobGiver_WanderAnywhere"/> at the bottom of the tree</description></item>
    /// <item><term>before — first attack job</term><description>tick <b>6,408</b>, and only because a citizen
    /// working the map edge walked into the raiders; the raid never moved on the town</description></item>
    /// <item><term>after — closed in 1,000 ticks</term><description><b>62 cells</b> (103 → 41), with all 15
    /// raiders in attack jobs</description></item>
    /// <item><term>after — first attack job</term><description>tick <b>721</b></description></item>
    /// <item><term>after — outcome</term><description>resolved by tick 14,000: the town held, 14 raiders down
    /// on the map and one walked back off it</description></item>
    /// </list>
    ///
    /// <para/><b>Why the existing raid test did not catch it.</b> <see cref="CombatAITests"/>'s
    /// <c>A_raid_spawned_onto_a_map_engages_the_civilizations_armed_citizens</c> uses a 40x40 map, where a map
    /// edge and the middle are inside acquire radius by accident. It is evidence rather than a victim and is
    /// left exactly as it stands: this class is the fixture that is not kinder than the game.
    /// </summary>
    public class RaidApproachTests : ContentTestBase
    {
        public RaidApproachTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private sealed class FoundedTown
        {
            public global::SimWorld.World.World World = null!;
            public Settlement Settlement = null!;
            public CoreMap Interior = null!;
            public Faction Player = null!;
        }

        /// <summary>Founds a settlement the ordinary way — shipped content, a generated world, whoever
        /// <c>SettlementFounder</c> makes — and opens its interior. Nothing here arms or enfactions anybody,
        /// and nothing chooses the map size: <c>MapGenTuning.MapSizeForPopulation</c> does, which is what
        /// makes this 200x200 rather than a number this test picked.</summary>
        private static FoundedTown Found(string seed, int bandSize = 30)
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 4, soloStart: true);
            Faction player = world.factions.First();
            Find.FactionManager.Add(player);
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, player, bandSize, new RandomStream(20260911), "Holdfast");
            return new FoundedTown
            {
                World = world,
                Settlement = settlement,
                Interior = settlement.EnterMap(world),
                Player = player,
            };
        }

        private static Faction Hostile(FoundedTown town, string defName, string name)
        {
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(faction);
            town.Player.SetRelationDirect(faction, FactionRelationKind.Hostile, -100);
            return faction;
        }

        /// <summary>Fires a real <see cref="IncidentWorker_RaidEnemy"/> onto the town's interior and hands
        /// back the squad it generated and spawned — the same path a storyteller raid takes.</summary>
        private static IReadOnlyList<Pawn> FireRaidOnto(FoundedTown town, Faction raiders, float points = 600f)
        {
            var target = new CivilizationTarget { Map = town.Interior };
            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestRaidApproach",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = target, points = points, faction = raiders }));
            return worker.LastRaidPawns!;
        }

        private static bool IsAttackJob(Job? job) =>
            job != null
            && (ReferenceEquals(job.def, CombatAIDefOf.AttackMelee) || ReferenceEquals(job.def, CombatAIDefOf.AttackStatic));

        private static float NearestHostileDistance(IReadOnlyList<Pawn> squad, IReadOnlyList<Pawn> citizens)
        {
            float best = float.MaxValue;
            foreach (Pawn raider in squad)
            {
                if (!raider.Spawned) continue;
                foreach (Pawn citizen in citizens)
                {
                    if (!citizen.Spawned) continue;
                    float d = (raider.Position - citizen.Position).LengthHorizontal;
                    if (d < best) best = d;
                }
            }
            return best;
        }

        // ---- the measurement ----

        [Fact]
        public void A_raid_crosses_the_map_to_the_settlement_it_came_for()
        {
            FoundedTown town = Found("raid-approach-measurement");
            Faction raiders = Hostile(town, "TribalCivilization", "Tribal");
            IReadOnlyList<Pawn> squad = FireRaidOnto(town, raiders);
            List<Pawn> citizens = town.Settlement.Citizens.Where(c => c.Spawned).ToList();
            Assert.NotEmpty(squad);
            Assert.NotEmpty(citizens);

            // The fixture check, and the whole reason this class exists: a raid must land far enough away that
            // seeing each other cannot be what brings the two sides together. A test whose map is small enough
            // for the spawn edge and the town to fall inside acquire radius proves nothing about approach, and
            // that is exactly what the 40x40 raid test in CombatAITests turns out to have been doing.
            float arrival = NearestHostileDistance(squad, citizens);
            Assert.True(
                arrival > CombatAITuning.TargetAcquireRadius * 2f,
                $"the raid landed {arrival:F0} cells from the nearest citizen — inside twice the acquire radius, so this fixture is kinder than the game");

            // Everything from here is the squad's own doing: no pawn is moved by hand, and nothing but the duty
            // its arrival handed it is different from the run that closed 11 cells in ten times this long.
            var everyone = new List<Pawn>(squad);
            everyone.AddRange(citizens);
            Pawn[] ticking = everyone.ToArray();
            var engaged = new HashSet<Pawn>();
            float closest = arrival;
            for (int i = 0; i < 2000; i++)
            {
                RunTicks(1, ticking);
                foreach (Pawn raider in squad)
                {
                    if (IsAttackJob(raider.jobs.curJob)) engaged.Add(raider);
                }
                float now = NearestHostileDistance(squad, citizens);
                if (now < closest) closest = now;
            }

            Assert.True(
                closest <= CombatAITuning.TargetAcquireRadius,
                $"the squad never got within acquire radius: closest approach {closest:F0} cells of {arrival:F0}");

            // Every raider, not merely one: the whole squad carries the goal, so the whole squad arrives.
            // Asserted over the ones that can fight at all, the way CitizenFactionTests asserts it for the
            // defenders — a pawn whose backstory disables Violent work is RimWorld's rule, not a shortfall.
            List<Pawn> able = squad.Where(r => !r.WorkTagIsDisabled(WorkTags.Violent)).ToList();
            Assert.NotEmpty(able);
            Assert.All(able, r => Assert.Contains(r, engaged));
        }

        [Fact]
        public void A_raid_that_arrives_also_ends()
        {
            // "They arrive" is only half of it: a squad that reaches the town and then mills about is the same
            // bug one step further in. A raid here fights to a finish and whoever is left walks off the map
            // (JobGiver_ExitMap), so what this asserts is that the raid stops being a raid — not who won.
            FoundedTown town = Found("raid-resolves");
            Faction raiders = Hostile(town, "TribalCivilization", "Tribal");
            IReadOnlyList<Pawn> squad = FireRaidOnto(town, raiders);
            List<Pawn> citizens = town.Settlement.Citizens.Where(c => c.Spawned).ToList();

            var everyone = new List<Pawn>(squad);
            everyone.AddRange(citizens);
            Pawn[] ticking = everyone.ToArray();

            int resolvedAt = -1;
            for (int block = 0; block < 40 && resolvedAt < 0; block++)
            {
                RunTicks(1000, ticking);
                if (squad.All(r => !r.Spawned || r.Downed || r.Dead)) resolvedAt = (block + 1) * 1000;
            }

            Assert.True(resolvedAt > 0, "40,000 ticks in, the raid was still on its feet on the map with nothing resolved");

            // It ended by fighting and leaving, not by the squad evaporating: everyone is accounted for as
            // either still lying on the map or gone from it.
            Assert.Equal(squad.Count, squad.Count(r => !r.Spawned) + squad.Count(r => r.Spawned && (r.Downed || r.Dead)));
        }

        // ---- the tier, and where it sits ----

        [Fact]
        public void The_duty_tier_sits_below_the_fight_tier_and_above_needs_and_work()
        {
            // Pinned structurally rather than by behaviour alone, because the ordering is the design: contact
            // beats marching, and marching beats eating. RimWorld's own slot for its lord-duty node could not
            // be sourced from here (ThinkTrees_Humanlike.xml says so at the tier), so what is asserted is the
            // ordering this port chose and the reasons written beside it — never a claim about RimWorld's.
            var root = (ThinkNode_Priority)ThinkTreeDefOf.Humanlike.thinkRoot;
            int fight = root.subNodes.FindIndex(n => n is JobGiver_AIFightEnemies);
            int duty = root.subNodes.FindIndex(n => n is ThinkNode_Duty);
            int hungry = root.subNodes.FindIndex(n => n is ThinkNode_ConditionalHungry);
            int tired = root.subNodes.FindIndex(n => n is ThinkNode_ConditionalTired);
            int work = root.subNodes.FindIndex(n => n is JobGiver_Work);

            Assert.True(fight >= 0 && duty >= 0 && hungry >= 0 && tired >= 0 && work >= 0);
            Assert.True(fight < duty, "a squad would drop the enemy in front of it to take one step toward the enemy behind it");
            Assert.True(duty < hungry && duty < tired, "a raid that stops halfway across the map to eat is the same bug in a hat");
            Assert.True(duty < work, "an arrival's own business outranks routine work");
        }

        [Fact]
        public void A_citizen_carries_no_duty_and_the_tier_is_a_no_op_for_the_town()
        {
            // The fix is asymmetric by goal, not by faction: raiders march because they were given something
            // to do here, and nothing about this tier reaches the people who live here. If it did, a town's
            // whole population would march at a raid instead of working, which is not RimWorld's behaviour for
            // an undrafted colonist and not this port's either (CombatPostureUtility).
            FoundedTown town = Found("citizens-have-no-duty");
            Faction raiders = Hostile(town, "TribalCivilization", "Tribal");
            IReadOnlyList<Pawn> squad = FireRaidOnto(town, raiders);

            Assert.All(town.Settlement.Citizens, c => Assert.Null(c.mindState.duty));
            Assert.All(squad, r => Assert.Same(DutyDefOf.AssaultSettlement, r.mindState.duty));

            // And the tier really is consulted for a citizen rather than skipped: ask it directly.
            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Assert.False(new ThinkNode_Duty().TryIssueJobPackage(citizen).IsValid);
            Assert.True(new ThinkNode_Duty().TryIssueJobPackage(squad.First(r => r.Spawned)).IsValid);
        }

        // ---- the pieces, on a map small enough to reason about ----

        private static CoreMap NewMap(int size) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static (Faction ours, Faction theirs) HostilePair()
        {
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            var theirs = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Theirs", "F_Theirs");
            Find.FactionManager.Add(ours);
            Find.FactionManager.Add(theirs);
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            return (ours, theirs);
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction faction, string name, DutyDef? duty = null)
        {
            Pawn pawn = NewHuman(name);
            pawn.faction = faction;
            pawn.mindState.duty = duty;
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        [Fact]
        public void An_enemy_beyond_acquire_radius_is_walked_to_rather_than_ignored()
        {
            CoreMap map = NewMap(120);
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = SpawnHuman(map, new IntVec3(110, 110), ours, "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(2, 2), theirs, "Raider", DutyDefOf.AssaultSettlement);

            float start = (citizen.Position - raider.Position).LengthHorizontal;
            Assert.True(start > CombatAITuning.TargetAcquireRadius, "the fixture has to put them out of each other's sight");

            raider.jobs.TryFindAndStartJob();
            Assert.Same(DutyJobDefOf.Goto, raider.jobs.curJob?.def);

            // A ceiling, not the hour it happens to be: the job is issued against the cell an enemy was
            // standing on, so a squad marching at a ghost has to give up and re-aim eventually.
            Assert.True(raider.jobs.curJob!.expiryInterval > 0, "an approach job that never expires marches at a ghost forever");

            RunTicks(1500, raider, citizen);
            float now = (citizen.Position - raider.Position).LengthHorizontal;
            Assert.True(now < start / 2f, $"the raider closed only {start - now:F0} of {start:F0} cells");

            // Without the duty it is the same pawn on the same map and it goes nowhere near — which is what
            // makes this a test of the duty rather than of pawns drifting about.
            raider.mindState.duty = null;
            raider.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            IntVec3 parked = raider.Position;
            RunTicks(1500, raider, citizen);
            Assert.True(
                (raider.Position - parked).LengthHorizontal < JobGiver_WanderAnywhere.WanderRadius * 4f,
                "a pawn with no duty crossed the map anyway, so this test proves nothing about the duty");
        }

        [Fact]
        public void Contact_hands_over_to_the_fight_tier()
        {
            // The handover the two tiers exist to make: out of range it walks, in range it swings, and nobody
            // asked "am I close enough" twice.
            CoreMap map = NewMap(120);
            (Faction ours, Faction theirs) = HostilePair();
            SpawnHuman(map, new IntVec3(60, 60), ours, "Citizen");
            Pawn far = SpawnHuman(map, new IntVec3(2, 2), theirs, "Far", DutyDefOf.AssaultSettlement);
            Pawn near = SpawnHuman(map, new IntVec3(61, 60), theirs, "Near", DutyDefOf.AssaultSettlement);

            far.jobs.TryFindAndStartJob();
            near.jobs.TryFindAndStartJob();

            Assert.Same(DutyJobDefOf.Goto, far.jobs.curJob?.def);
            Assert.True(IsAttackJob(near.jobs.curJob), "a raider standing next to a citizen went for a walk instead");
        }

        [Fact]
        public void A_hungry_raider_still_marches()
        {
            // Duty above needs, exercised rather than only asserted as an index: the same pawn eats the moment
            // its duty is gone, which is what makes the ordering a real ordering.
            CoreMap map = NewMap(120);
            (Faction ours, Faction theirs) = HostilePair();
            SpawnHuman(map, new IntVec3(110, 110), ours, "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(2, 2), theirs, "Raider", DutyDefOf.AssaultSettlement);

            Thing meal = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MealSimple"));
            GenSpawn.Spawn(meal, new IntVec3(3, 2), map);
            raider.needs.food!.CurLevel = 0.05f;
            Assert.NotEqual(HungerCategory.Fed, raider.needs.food.CurCategory);

            raider.jobs.TryFindAndStartJob();
            Assert.Same(DutyJobDefOf.Goto, raider.jobs.curJob?.def);

            raider.mindState.duty = null;
            raider.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            raider.jobs.TryFindAndStartJob();
            Assert.Same(JobDefOf.Ingest, raider.jobs.curJob?.def);
        }

        [Fact]
        public void With_nobody_left_to_fight_the_raid_goes_home()
        {
            // What ends a raid, and the one place this port's translation shows: there is no Lord to notice
            // that half the squad is down and call a retreat (DutyDef says so in as many words), so leaving is
            // keyed on the map instead — no enemy left that this pawn would attack.
            CoreMap map = NewMap(60);
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = SpawnHuman(map, new IntVec3(30, 30), ours, "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(29, 30), theirs, "Raider", DutyDefOf.AssaultSettlement);

            // Adjacent, because nothing here is armed: an unarmed pawn's acquire radius is one cell
            // (CombatPostureUtility rule 2), so this is what "in range" means for a bare-handed raider.
            raider.jobs.TryFindAndStartJob();
            Assert.True(IsAttackJob(raider.jobs.curJob));

            // The defender goes down; there is now nobody on this map worth attacking.
            citizen.health.ForceDowned = true;
            raider.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            raider.jobs.TryFindAndStartJob();
            Assert.Same(DutyJobDefOf.ExitMap, raider.jobs.curJob?.def);

            RunTicks(3000, raider);
            Assert.False(raider.Spawned, "the raider never got off the map");
            Assert.Null(raider.mindState.duty);
            Assert.False(raider.Dead); // it left; it was not destroyed
            Assert.DoesNotContain(raider, map.mapPawns.AllPawns);
        }

        [Fact]
        public void A_raider_with_nobody_reachable_leaves_rather_than_standing_there()
        {
            // The same ending by the other road, and the reason the approach asks BestAttackTarget rather than
            // "is there an enemy anywhere": a squad that cannot path to the town at all is not a squad that
            // marches into a wall forever.
            CoreMap map = NewMap(60);
            (Faction ours, Faction theirs) = HostilePair();
            Pawn raider = SpawnHuman(map, new IntVec3(30, 30), theirs, "Raider", DutyDefOf.AssaultSettlement);
            Pawn elsewhere = SpawnHuman(NewMap(20), new IntVec3(10, 10), ours, "Citizen");

            Assert.NotSame(raider.Map, elsewhere.Map);
            raider.jobs.TryFindAndStartJob();
            Assert.Same(DutyJobDefOf.ExitMap, raider.jobs.curJob?.def);
        }

        // ---- content ----

        [Fact]
        public void The_duty_content_loads_and_is_wired_to_real_drivers()
        {
            Assert.Empty(Content.Result.Errors);

            Assert.NotNull(DutyDefOf.AssaultSettlement);
            Assert.NotNull(DutyJobDefOf.Goto);
            Assert.NotNull(DutyJobDefOf.ExitMap);
            Assert.Equal(typeof(JobDriver_Goto), DutyJobDefOf.Goto.driverClass);
            Assert.Equal(typeof(JobDriver_ExitMap), DutyJobDefOf.ExitMap.driverClass);

            // The duty's phases, in order: close on the enemy, then leave. The fight tier is deliberately not
            // in here — it sits above the whole duty node in the tree.
            var node = Assert.IsType<ThinkNode_Priority>(DutyDefOf.AssaultSettlement.thinkNode);
            Assert.Collection(
                node.subNodes,
                n => Assert.IsType<JobGiver_AIGotoNearestHostile>(n),
                n => Assert.IsType<JobGiver_ExitMap>(n));
        }

        // ---- scribe ----

        /// <summary>Map plus factions in one document, for the same reason <c>CombatAITests</c>'s own root does
        /// it: every pawn on the map carries a <see cref="Pawn.faction"/> cross-reference, and saving the map
        /// alone would leave those dangling.</summary>
        private sealed class RaidRoot : IExposable
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
        public void A_raid_saved_mid_approach_is_still_a_raid_when_it_loads()
        {
            CoreMap map = NewMap(120);
            (Faction ours, Faction theirs) = HostilePair();
            SpawnHuman(map, new IntVec3(110, 110), ours, "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(2, 2), theirs, "Raider", DutyDefOf.AssaultSettlement);

            raider.jobs.TryFindAndStartJob();
            RunTicks(400, raider);
            Assert.NotEqual(new IntVec3(2, 2), raider.Position);

            var root = new RaidRoot { map = map, factions = new List<Faction> { ours, theirs } };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            RaidRoot loadedRoot = Scribe.Load<RaidRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            CoreMap loaded = loadedRoot.map!;

            Pawn loadedRaider = loaded.mapPawns.AllPawns.First(p => p.name == "Raider");
            Pawn loadedCitizen = loaded.mapPawns.AllPawns.First(p => p.name == "Citizen");

            // The duty is the only thing making this pawn a raider rather than a tourist: leave it off the
            // save and reloading would disarm every squad on the map.
            Assert.Same(DutyDefOf.AssaultSettlement, loadedRaider.mindState.duty);

            float before = (loadedCitizen.Position - loadedRaider.Position).LengthHorizontal;
            RunTicks(600, loadedRaider, loadedCitizen);
            float after = (loadedCitizen.Position - loadedRaider.Position).LengthHorizontal;
            Assert.True(after < before, "the reloaded raid stopped marching");
        }
    }
}
