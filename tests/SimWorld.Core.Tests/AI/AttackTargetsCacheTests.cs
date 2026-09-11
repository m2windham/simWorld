using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
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
    /// The per-map index that turned "who on this map is hostile to me?" from a walk of the whole population
    /// into a lookup (system 9: AI — <see cref="AttackTargetsCache"/>).
    /// <para/>
    /// <b>The correctness tests come first and there are more of them than speed tests, on purpose.</b> A
    /// cache that is fast and wrong makes a pawn shoot a corpse or ignore a raider, and neither failure
    /// announces itself. The strongest statement available here is the differential one:
    /// <see cref="AttackTargetFinder.BestAttackTargetUncached"/> is the same scoring loop over the whole map,
    /// so asserting the two agree across randomly generated map states tests the <i>index</i> and nothing
    /// else. Everything below it names one specific way a pawn enters or leaves the set.
    /// </summary>
    public class AttackTargetsCacheTests : ContentTestBase
    {
        public AttackTargetsCacheTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size = 24) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

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

        private static Pawn Spawn(CoreMap map, IntVec3 cell, Faction? faction = null, string name = "Pawn")
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnAnimal(CoreMap map, IntVec3 cell, string name = "Beast")
        {
            var p = new Pawn(Husky, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>A grudge, written the way the two callers that make one write it
        /// (<c>TameUtility.TryTame</c>, <c>HuntUtility.TryProvokeRevenge</c>): the target, then the clock.</summary>
        private static void Provoke(Pawn holder, Pawn victim, int ticks = 5000)
        {
            holder.mindState.angryAt = victim;
            holder.mindState.angryUntilTick = Find.TickManager.TicksGame + ticks;
        }

        private static float Radius => CombatAITuning.TargetAcquireRadius;

        /// <summary>Asserts the cached scan and the whole-map scan return the identical pawn for every pawn on
        /// the map — the property the whole cache rests on.</summary>
        private static void AssertAgreesWithFullScan(CoreMap map)
        {
            foreach (Pawn searcher in map.mapPawns.AllPawnsSpawned.ToList())
            {
                foreach (float radius in new[] { 1.5f, Radius })
                {
                    Pawn? cached = AttackTargetFinder.BestAttackTarget(searcher, radius);
                    Pawn? full = AttackTargetFinder.BestAttackTargetUncached(searcher, radius);
                    Assert.Same(full, cached);
                }
            }
        }

        // ---- the differential test ----

        [Fact]
        public void The_index_and_a_walk_of_the_whole_map_pick_the_same_target_in_every_random_state()
        {
            // Every lever the index has to survive, pulled at random and in random order: four factions with
            // randomly assigned relations, pawns of each of them plus factionless animals, deaths, downings,
            // despawns, grudges in both directions and faction changes mid-life. Seeded, so a failure is
            // reproducible from the seed printed in the assertion.
            var rand = new RandomStream(20260911);

            for (int trial = 0; trial < 40; trial++)
            {
                Find.FactionManager = new FactionManager();
                Find.TickManager = new TickManager();
                CoreMap map = NewMap(28);

                var factions = new List<Faction>();
                for (int i = 0; i < 4; i++) factions.Add(NewFaction("F" + trial + "_" + i));
                for (int i = 0; i < factions.Count; i++)
                {
                    for (int j = i + 1; j < factions.Count; j++)
                    {
                        bool hostile = rand.Chance(0.4f);
                        factions[i].SetRelationDirect(
                            factions[j],
                            hostile ? FactionRelationKind.Hostile : FactionRelationKind.Neutral,
                            hostile ? -100 : 0);
                    }
                }

                var pawns = new List<Pawn>();
                int count = rand.Range(6, 20);
                for (int i = 0; i < count; i++)
                {
                    var cell = new IntVec3(rand.Range(1, 27), 0, rand.Range(1, 27));
                    bool wild = rand.Chance(0.25f);
                    pawns.Add(wild
                        ? SpawnAnimal(map, cell, "Wild" + i)
                        : Spawn(map, cell, rand.Element(factions), "P" + i));
                }

                // Now churn it: everything that can move a pawn in or out of the index, applied at random.
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (rand.Chance(0.12f) && p.Spawned) p.health.Kill(null, null);
                    else if (rand.Chance(0.12f)) p.health.ForceDowned = true;
                    else if (rand.Chance(0.10f) && p.Spawned) p.DeSpawn();
                    else if (rand.Chance(0.15f)) p.faction = rand.Chance(0.3f) ? null : rand.Element(factions);
                    if (rand.Chance(0.15f) && pawns.Count > 1) Provoke(p, pawns[rand.Range(0, pawns.Count)]);
                }

                // A late arrival, after all of the above — the "spawned mid-fight" case, inside the sweep.
                if (rand.Chance(0.5f))
                {
                    Spawn(map, new IntVec3(rand.Range(1, 27), 0, rand.Range(1, 27)), rand.Element(factions), "Late");
                }

                AssertAgreesWithFullScan(map);
            }
        }

        // ---- one invalidation path per test ----

        [Fact]
        public void A_target_that_dies_leaves_the_set_on_the_tick_it_dies()
        {
            CoreMap map = NewMap();
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = Spawn(map, new IntVec3(2, 0, 2), ours, "Citizen");
            Pawn near = Spawn(map, new IntVec3(4, 0, 2), theirs, "Near");
            Pawn far = Spawn(map, new IntVec3(12, 0, 2), theirs, "Far");

            Assert.Same(near, AttackTargetFinder.BestAttackTarget(citizen, Radius));
            Assert.Equal(2, map.mapPawns.AttackTargets.PawnsInFaction(theirs).Count);

            near.health.Kill(null, null);

            // Not "by the next tick" — the very next scan, with no tick in between. Death runs through
            // Pawn.Notify_Died -> CorpseMaker, which de-spawns; the despawn is what tells the index.
            Assert.Same(far, AttackTargetFinder.BestAttackTarget(citizen, Radius));
            Assert.DoesNotContain(near, map.mapPawns.AttackTargets.PawnsInFaction(theirs));
            AssertAgreesWithFullScan(map);

            // And the body it left behind is a Corpse, not a Pawn: it cannot re-enter the index by any route.
            Assert.NotNull(near.corpse);
            Assert.True(near.corpse!.Spawned);
            Assert.DoesNotContain(map.mapPawns.AllPawnsSpawned, p => ReferenceEquals(p, near));
        }

        [Fact]
        public void A_pawn_that_spawns_mid_fight_is_found_by_the_next_scan()
        {
            CoreMap map = NewMap();
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = Spawn(map, new IntVec3(2, 0, 2), ours, "Citizen");
            Pawn far = Spawn(map, new IntVec3(14, 0, 2), theirs, "Far");

            Assert.Same(far, AttackTargetFinder.BestAttackTarget(citizen, Radius));

            Pawn reinforcement = Spawn(map, new IntVec3(4, 0, 2), theirs, "Reinforcement");
            Assert.Same(reinforcement, AttackTargetFinder.BestAttackTarget(citizen, Radius));
            AssertAgreesWithFullScan(map);
        }

        [Fact]
        public void Changing_faction_while_spawned_refiles_the_pawn_both_ways()
        {
            CoreMap map = NewMap();
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = Spawn(map, new IntVec3(2, 0, 2), ours, "Citizen");
            Pawn raider = Spawn(map, new IntVec3(5, 0, 2), theirs, "Raider");
            Pawn neighbour = Spawn(map, new IntVec3(3, 0, 2), ours, "Neighbour");

            Assert.Same(raider, AttackTargetFinder.BestAttackTarget(citizen, Radius));

            // Recruited: the same pawn, standing on the same cell, is one of us now (the shape
            // Pawn_GuestTracker's recruit path writes, and the shape TameUtility writes for an animal).
            raider.faction = ours;
            Assert.Null(AttackTargetFinder.BestAttackTarget(citizen, Radius));
            Assert.Empty(map.mapPawns.AttackTargets.PawnsInFaction(theirs));
            Assert.Equal(3, map.mapPawns.AttackTargets.PawnsInFaction(ours).Count);

            // And the other direction: a neighbour who turns coat is a target immediately.
            neighbour.faction = theirs;
            Assert.Same(neighbour, AttackTargetFinder.BestAttackTarget(citizen, Radius));
            AssertAgreesWithFullScan(map);

            // Released to nobody: a pawn with no faction is nobody's enemy by relation, and is filed nowhere.
            neighbour.faction = null;
            Assert.Null(AttackTargetFinder.BestAttackTarget(citizen, Radius));
            Assert.Equal(2, map.mapPawns.AttackTargets.FiledPawnCount);
            AssertAgreesWithFullScan(map);
        }

        [Fact]
        public void A_relation_turning_hostile_needs_no_invalidation_at_all()
        {
            // The index files pawns by faction *membership* and asks about hostility only at scan time, so
            // diplomacy changing under it is not an event it has to hear about. This is the one invalidation
            // path deliberately designed out rather than handled.
            CoreMap map = NewMap();
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Faction theirs = NewFaction("Theirs");
            ours.SetRelationDirect(theirs, FactionRelationKind.Neutral, 0);

            Pawn citizen = Spawn(map, new IntVec3(2, 0, 2), ours, "Citizen");
            Pawn stranger = Spawn(map, new IntVec3(5, 0, 2), theirs, "Stranger");

            Assert.Null(AttackTargetFinder.BestAttackTarget(citizen, Radius));

            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            Assert.Same(stranger, AttackTargetFinder.BestAttackTarget(citizen, Radius));

            ours.SetRelationDirect(theirs, FactionRelationKind.Neutral, 0);
            Assert.Null(AttackTargetFinder.BestAttackTarget(citizen, Radius));
            AssertAgreesWithFullScan(map);
        }

        [Fact]
        public void Downing_is_not_an_invalidation_path_and_healing_needs_none_either()
        {
            CoreMap map = NewMap();
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = Spawn(map, new IntVec3(2, 0, 2), ours, "Citizen");
            Pawn raider = Spawn(map, new IntVec3(5, 0, 2), theirs, "Raider");

            raider.health.ForceDowned = true;
            Assert.Null(AttackTargetFinder.BestAttackTarget(citizen, Radius));

            // Still filed: the index holds it and ThreatDisabled refuses it, which is why getting back up
            // costs nothing to notice.
            Assert.Contains(raider, map.mapPawns.AttackTargets.PawnsInFaction(theirs));

            raider.health.ForceDowned = false;
            Assert.Same(raider, AttackTargetFinder.BestAttackTarget(citizen, Radius));
            AssertAgreesWithFullScan(map);
        }

        [Fact]
        public void A_grudge_makes_its_holder_visible_to_the_pawn_it_is_angry_at()
        {
            // The half no faction bucket could ever supply: a wild animal has no faction, so it is filed
            // nowhere, and the hunter it turned on has to be able to fight back.
            CoreMap map = NewMap();
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Pawn hunter = Spawn(map, new IntVec3(2, 0, 2), ours, "Hunter");
            Pawn muffalo = SpawnAnimal(map, new IntVec3(4, 0, 2), "Muffalo");
            Pawn bystander = SpawnAnimal(map, new IntVec3(3, 0, 2), "Bystander");

            Assert.Null(AttackTargetFinder.BestAttackTarget(hunter, Radius));
            Assert.Empty(map.mapPawns.AttackTargets.GrudgeHolders);

            Provoke(muffalo, hunter);

            Assert.Contains(muffalo, map.mapPawns.AttackTargets.GrudgeHolders);
            Assert.Same(muffalo, AttackTargetFinder.BestAttackTarget(hunter, Radius));  // fights back
            Assert.Same(hunter, AttackTargetFinder.BestAttackTarget(muffalo, Radius));  // and charges
            Assert.Null(AttackTargetFinder.BestAttackTarget(bystander, Radius));        // nobody else cares
            AssertAgreesWithFullScan(map);

            // Dropping the grudge takes the holder straight back out.
            muffalo.mindState.angryAt = null;
            Assert.Empty(map.mapPawns.AttackTargets.GrudgeHolders);
            Assert.Null(AttackTargetFinder.BestAttackTarget(hunter, Radius));
            AssertAgreesWithFullScan(map);
        }

        [Fact]
        public void A_grudge_against_a_pawn_on_another_map_finds_nothing()
        {
            // A grudge is a plain reference and does not care about maps; both scans have to agree that a
            // target standing somewhere else is not a target here.
            CoreMap here = NewMap();
            CoreMap elsewhere = NewMap();
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Pawn animal = SpawnAnimal(here, new IntVec3(2, 0, 2), "Animal");
            Pawn hunter = Spawn(elsewhere, new IntVec3(2, 0, 2), ours, "Hunter");

            Provoke(animal, hunter);

            Assert.Null(AttackTargetFinder.BestAttackTarget(animal, Radius));
            Assert.Null(AttackTargetFinder.BestAttackTargetUncached(animal, Radius));
            AssertAgreesWithFullScan(here);
            AssertAgreesWithFullScan(elsewhere);
        }

        [Fact]
        public void Everything_that_leaves_the_map_leaves_the_index()
        {
            CoreMap map = NewMap();
            (Faction ours, Faction theirs) = HostilePair();
            var all = new List<Pawn>();
            for (int i = 0; i < 6; i++) all.Add(Spawn(map, new IntVec3(2 + i, 0, 2), i % 2 == 0 ? ours : theirs, "P" + i));
            Pawn animal = SpawnAnimal(map, new IntVec3(9, 0, 2), "Animal");
            Provoke(animal, all[0]);

            Assert.Equal(6, map.mapPawns.AttackTargets.FiledPawnCount);
            Assert.Single(map.mapPawns.AttackTargets.GrudgeHolders);

            all[0].health.Kill(null, null);      // death: de-spawns and leaves a corpse
            all[1].DeSpawn();                    // a tier demotion's despawn, and a pawn simply walking off
            animal.DeSpawn();
            foreach (Pawn p in all.Skip(2)) p.DeSpawn();

            Assert.Equal(0, map.mapPawns.AttackTargets.FiledPawnCount);
            Assert.Empty(map.mapPawns.AttackTargets.GrudgeHolders);
            Assert.Empty(map.mapPawns.AttackTargets.FactionsPresent);
        }

        // ---- save / load ----

        /// <summary>Map plus factions in one document — every pawn carries a <see cref="Pawn.faction"/>
        /// cross-reference, so saving the map alone would leave them dangling (same reason
        /// <c>CombatAITests</c> and <c>CaptureTests</c> do it).</summary>
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
        public void A_load_rebuilds_the_index_rather_than_restoring_it()
        {
            CoreMap map = NewMap();
            (Faction ours, Faction theirs) = HostilePair();
            Pawn citizen = Spawn(map, new IntVec3(2, 0, 2), ours, "Citizen");
            Spawn(map, new IntVec3(5, 0, 2), theirs, "Near");
            Spawn(map, new IntVec3(12, 0, 2), theirs, "Far");
            Pawn animal = SpawnAnimal(map, new IntVec3(7, 0, 2), "Animal");
            Provoke(animal, citizen);

            var root = new SaveRoot { map = map, factions = new List<Faction> { ours, theirs } };
            string xml = Scribe.SaveToString(root, "root");

            // Nothing of the index is in the document. It cannot load stale because there is nothing to load.
            Assert.DoesNotContain("attackTargets", xml, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("grudgeHolders", xml, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("factionsPresent", xml, System.StringComparison.OrdinalIgnoreCase);

            Pawn.ResetThingIdCounter();
            SaveRoot loadedRoot = Scribe.Load<SaveRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            CoreMap loaded = loadedRoot.map!;

            // Re-spawning every Thing is what rebuilt it, and it came back complete.
            AttackTargetsCache cache = loaded.mapPawns.AttackTargets;
            Assert.Equal(3, cache.FiledPawnCount);
            Assert.Equal(2, cache.FactionsPresent.Count);
            Assert.Single(cache.GrudgeHolders);

            Pawn loadedCitizen = loaded.mapPawns.AllPawnsSpawned.First(p => p.name == "Citizen");
            Pawn loadedNear = loaded.mapPawns.AllPawnsSpawned.First(p => p.name == "Near");
            Assert.Same(loadedNear, AttackTargetFinder.BestAttackTarget(loadedCitizen, Radius));
            AssertAgreesWithFullScan(loaded);
        }

        // ---- what it was built for ----

        [Fact]
        public void The_scan_visits_the_raid_and_not_the_settlement_however_large_the_settlement_grows()
        {
            // The cost claim, stated exactly rather than on a stopwatch: the number of candidates a scan
            // weighs is fully determined by the index, so it can be counted instead of timed. Before the
            // cache this number was the whole population, which is what made one evaluation cost a function
            // of settlement size (docs/perf/attack-targets-cache.md).
            const int Raiders = 5;
            int[] populations = { 25, 100, 400 };
            var visited = new List<int>();

            foreach (int population in populations)
            {
                Find.FactionManager = new FactionManager();
                CoreMap map = NewMap(120);
                (Faction ours, Faction theirs) = HostilePair();

                Pawn citizen = Spawn(map, new IntVec3(1, 0, 1), ours, "Citizen");
                for (int i = 0; i < population; i++) Spawn(map, new IntVec3(2 + i % 100, 0, 2 + i / 100), ours, "C" + i);
                for (int i = 0; i < Raiders; i++) Spawn(map, new IntVec3(60 + i, 0, 60), theirs, "R" + i);

                visited.Add(CandidatesWeighedBy(citizen, map));
                Assert.Equal(population + 1 + Raiders, map.mapPawns.AllPawnsSpawned.Count);
            }

            // Flat, not merely smaller: sixteen times the settlement, the same scan.
            Assert.Equal(Raiders, visited[0]);
            Assert.All(visited, v => Assert.Equal(Raiders, v));
        }

        /// <summary>How many candidates <see cref="AttackTargetFinder.BestAttackTarget"/> will weigh for this
        /// searcher, read off the index the same way the scan reads it.</summary>
        private static int CandidatesWeighedBy(Pawn searcher, CoreMap map)
        {
            AttackTargetsCache cache = map.mapPawns.AttackTargets;
            int total = 0;
            Faction? ours = searcher.faction;
            if (ours != null)
            {
                foreach (Faction other in cache.FactionsPresent)
                {
                    if (ours.HostileTo(other)) total += cache.PawnsInFaction(other).Count;
                }
            }
            foreach (Pawn holder in cache.GrudgeHolders)
            {
                if (!(ours != null && holder.faction != null && ours.HostileTo(holder.faction))) total++;
            }
            return total;
        }
    }
}
