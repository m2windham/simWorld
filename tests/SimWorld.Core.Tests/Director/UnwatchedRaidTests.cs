using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// Raids on settlements nobody is watching (tracker item <c>director.raids</c>).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> Before
    /// <see cref="SettlementRaidResolver"/>, a raid that picked a settlement with no reachable map generated a
    /// full squad, spawned nobody, and returned true. Measured on exactly the fixture
    /// <see cref="A_raid_on_an_unwatched_settlement_used_to_do_literally_nothing"/> builds — twelve citizens,
    /// four hundred in the Statistical cohort, a hundred Steel, a five-hundred-point raid — the old behaviour
    /// produced: twelve raiders generated, none spawned, population 412 before and 412 after, stores 100
    /// before and 100 after, zero deaths on either side, and nothing in the chronicle beyond the storyteller's
    /// own "a RaidEnemy fired" line. The log said the civilization had been raided and the world had not moved.
    ///
    /// <para/>Selection is deliberately *not* part of the fix: see
    /// <see cref="Raids_still_reach_settlements_nobody_is_watching"/>.
    /// </summary>
    public class UnwatchedRaidTests : ContentTestBase
    {
        public UnwatchedRaidTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static Faction Raiders(string name = "Rough") =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), name, "F_" + name);

        private static ThingDef Steel => DefDatabase<ThingDef>.GetNamed("Steel");

        private static IncidentWorker_RaidEnemy NewWorker()
        {
            var def = new IncidentDef
            {
                defName = "TestRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            };
            return (IncidentWorker_RaidEnemy)def.Worker;
        }

        private static Settlement Town(string name, int tile, int citizens, int cohort, int steel)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman(name + i));
            if (cohort > 0) settlement.AddStatisticalPeople(cohort);
            if (steel > 0) settlement.AddStore(Steel, steel);
            return settlement;
        }

        private static CivilizationTarget TargetOf(params Settlement[] settlements)
        {
            var target = new CivilizationTarget();
            target.SetSettlements(settlements);
            return target;
        }

        private static bool Fire(IncidentWorker_RaidEnemy worker, CivilizationTarget target, Faction faction, float points = 500f) =>
            worker.TryExecute(new IncidentParms { target = target, points = points, faction = faction });

        private static IEnumerable<string> ChronicleHeadlines() =>
            Find.Storyteller.Chronicle.Select(e => e.incidentDefName);

        // ---- the headline ----

        [Fact]
        public void A_raid_on_an_unwatched_settlement_used_to_do_literally_nothing()
        {
            // The exact fixture the class doc quotes the old numbers from. Nobody is watching: the god has no
            // focus at all, which is the civilization-scope state every settlement but one is always in.
            Assert.False(Find.God.Attention.HasFocus);

            Settlement town = Town("Unwatched", 11, citizens: 12, cohort: 400, steel: 100);
            CivilizationTarget target = TargetOf(town);

            int populationBefore = town.TotalPopulation;
            int steelBefore = town.StoreCountOf(Steel);

            // Per raid: it resolved against this settlement, it left a mark, and it said so. A raid that is
            // cleanly repelled costs the settlement nothing and takes nothing — that is a real outcome and it
            // is allowed; what is never allowed is a raid whose only trace is the log line claiming it.
            const int Raids = 10;
            for (int i = 0; i < Raids; i++)
            {
                int linesBefore = Find.Storyteller.Chronicle.Count;
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, target, Raiders()));

                Assert.Same(town, worker.LastRaidSettlement);
                Assert.NotNull(worker.LastRaidOutcome);
                SettlementRaidOutcome outcome = worker.LastRaidOutcome!.Value;
                Assert.True(outcome.Resolved);
                Assert.True(outcome.ChangedTheWorld, "raid " + i + " resolved without changing anything");
                Assert.True(Find.Storyteller.Chronicle.Count > linesBefore, "raid " + i + " left nothing in the chronicle");
            }

            // Across the sweep: the settlement is measurably worse off. This is the assertion the old
            // behaviour could not have passed at any number of raids.
            Assert.True(town.TotalPopulation < populationBefore,
                "a settlement came through " + Raids + " raids with every one of its " + populationBefore + " people");
            Assert.True(town.StoreCountOf(Steel) < steelBefore,
                "a settlement came through " + Raids + " raids with all " + steelBefore + " of its stores");

            // ...and the chronicle names the place, through the same free-form hook births and migrations use.
            Assert.Contains(ChronicleHeadlines(), h => h.StartsWith("Raid:", System.StringComparison.Ordinal));
            Assert.Contains(ChronicleHeadlines(), h => h.Contains("Unwatched", System.StringComparison.Ordinal));
        }

        [Fact]
        public void An_unwatched_raid_is_a_moment_the_first_time_it_ever_happens()
        {
            // The chronicle line categorises as "Raid" through MomentCurator.CategoryForFreeform with no call
            // site needing to know — so a civilization's first raid ever enters its curated history.
            CivilizationTarget target = TargetOf(Town("First", 3, citizens: 8, cohort: 0, steel: 40));
            Assert.True(Fire(NewWorker(), target, Raiders()));

            Assert.Contains(Find.Storyteller.Moments, m => m.incidentDefName.StartsWith("Raid:", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Whoever_lost_the_engagement_lost_at_least_one_person()
        {
            // A raid is never free to the side that lost it. Run many so both outcomes are covered by the same
            // assertion rather than by whichever one this seed happens to roll.
            for (int i = 0; i < 60; i++)
            {
                Settlement town = Town("Ledger" + i, 100 + i, citizens: 6, cohort: 60, steel: 50);
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, TargetOf(town), Raiders()));

                SettlementRaidOutcome outcome = worker.LastRaidOutcome!.Value;
                if (outcome.Repelled) Assert.True(outcome.RaidersKilled >= 1, "a repelled raid cost the attackers nothing");
                else Assert.True(outcome.TotalLivesLost >= 1, "a settlement was overrun and lost nobody");
                Assert.True(outcome.ChangedTheWorld);
            }
        }

        // ---- the watched settlement still fights for real ----

        [Fact]
        public void A_raid_on_the_settlement_the_player_has_open_lands_on_its_map_with_real_pawns()
        {
            Game game = NewSoloGame("watched-raid");
            Settlement settlement = game.CivilizationTarget.Settlements.Single();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);

            global::SimWorld.Map.Map map = settlement.InteriorMap!;
            int defendersOnMap = settlement.Citizens.Count(p => p.Spawned && ReferenceEquals(p.Map, map));
            Assert.True(defendersOnMap > 0, "the opened settlement had nobody standing in it to defend");

            IncidentWorker_RaidEnemy worker = NewWorker();
            Assert.True(Fire(worker, game.CivilizationTarget, Raiders("Attackers")));

            // Physical, not abstract: no outcome struct, and every raider is on the settlement's own interior.
            Assert.Null(worker.LastRaidOutcome);
            Assert.NotEmpty(worker.LastRaidPawns!);
            Assert.All(worker.LastRaidPawns!, p =>
            {
                Assert.True(p.Spawned);
                Assert.Same(map, p.Map);
            });

            // Both sides are on the one map, which is what "fights for real" means here — the actual fighting
            // is AI/Combat's, already wired, and deliberately not re-asserted by this module's tests.
            Assert.Contains(map.mapPawns.AllPawns, p => settlement.Citizens.Contains(p));
            Assert.Contains(map.mapPawns.AllPawns, p => worker.LastRaidPawns!.Contains(p));
        }

        [Fact]
        public void A_town_with_a_cached_interior_that_nobody_is_watching_is_still_raided_for_real()
        {
            // The second face of the same bug, and the one that is easy to miss: EnterMap caches an interior
            // forever, and GodCommands.GenerateSettlementInterior will build one for a settlement nobody is
            // attending. Only a Full-tier citizen is ever placed on an interior and only attention holds an
            // ordinary citizen at Full — so raiders spawned into a cached-but-unwatched map would walk around
            // an empty town. "A map exists" is not the condition; "the god is watching it" is.
            Game game = NewSoloGame("cached-interior");
            Settlement settlement = game.CivilizationTarget.Settlements.Single();
            GodCommands.OpenSettlement(settlement.tile);
            GodCommands.ClearSettlementFocus();
            settlement.SyncCitizenSpawns();

            Assert.NotNull(settlement.InteriorMap);
            Assert.False(Find.God.Attention.HasFocus);
            Assert.DoesNotContain(settlement.Citizens, p => p.Spawned);

            IncidentWorker_RaidEnemy worker = NewWorker();
            Assert.True(Fire(worker, game.CivilizationTarget, Raiders("Ghosts")));

            Assert.NotNull(worker.LastRaidOutcome);
            Assert.True(worker.LastRaidOutcome!.Value.ChangedTheWorld);
            Assert.All(worker.LastRaidPawns!, p => Assert.False(p.Spawned, "raiders were spawned into a town with no defenders in it"));
        }

        // ---- arrival: one entry point, many cells ----

        [Fact]
        public void Raiders_no_longer_all_spawn_on_one_cell()
        {
            // The port previously computed RandomEdgeCell once and spawned the whole squad on it — a raid
            // arrived as a single stack of pawns standing inside each other.
            Game game = NewSoloGame("arrival-scatter");
            Settlement settlement = game.CivilizationTarget.Settlements.Single();
            GodCommands.OpenSettlement(settlement.tile);

            IncidentWorker_RaidEnemy worker = NewWorker();
            Assert.True(Fire(worker, game.CivilizationTarget, Raiders("Scatter"), points: 900f));

            IReadOnlyList<Pawn> squad = worker.LastRaidPawns!;
            Assert.True(squad.Count > 1, "needed more than one raider to say anything about where they landed");

            int distinctCells = squad.Select(p => p.Position).Distinct().Count();
            Assert.True(distinctCells > 1, "the whole squad spawned on one cell (" + squad[0].Position + ")");

            // ...but they still arrive together, from one direction: every raider is within the closewalk
            // radius of the group's entry cell, which is on an edge.
            global::SimWorld.Map.Map map = settlement.InteriorMap!;
            Assert.All(squad, p => Assert.True(
                DistanceToNearestEdge(p.Position, map) <= IncidentWorker_RaidEnemy.ClosewalkRadius,
                p.Position + " is further from every map edge than one group entry could scatter"));
        }

        private static int DistanceToNearestEdge(global::SimWorld.Map.IntVec3 cell, global::SimWorld.Map.Map map) =>
            new[] { cell.x, cell.z, map.Size.x - 1 - cell.x, map.Size.z - 1 - cell.z }.Min();

        // ---- the model behaves ----

        [Fact]
        public void A_stronger_settlement_repels_the_same_raid_more_often()
        {
            int weakHeld = RepelledOutOf(200, citizens: 3, cohort: 0, seed: 808);
            int strongHeld = RepelledOutOf(200, citizens: 3, cohort: 4000, seed: 808);

            Assert.True(strongHeld > weakHeld,
                "a settlement that can field a militia did no better than one that cannot (weak=" + weakHeld + ", strong=" + strongHeld + ")");
        }

        private static int RepelledOutOf(int trials, int citizens, int cohort, int seed)
        {
            Rand.Current = new RandomStream(seed);
            Faction raiders = Raiders("Repeat");
            int held = 0;
            for (int i = 0; i < trials; i++)
            {
                Settlement town = Town("T" + i, 500 + i, citizens, cohort, steel: 0);
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, TargetOf(town), raiders));
                if (worker.LastRaidOutcome!.Value.Repelled) held++;
            }
            return held;
        }

        [Fact]
        public void A_war_band_cannot_depopulate_a_city()
        {
            // Casualties are bounded by what the *enemy* can inflict, not by how big you are — the property
            // that stops a forty-thousand-person city losing forty times as many people to the same raid as a
            // thousand-person town. A settlement this size is never going to be overrun anyway; what is being
            // pinned is that even so, being large does not make you lose more.
            Settlement city = Town("Metropolis", 21, citizens: 10, cohort: 40000, steel: 0);
            int before = city.TotalPopulation;

            IncidentWorker_RaidEnemy worker = NewWorker();
            Assert.True(Fire(worker, TargetOf(city), Raiders("WarBand")));

            SettlementRaidOutcome outcome = worker.LastRaidOutcome!.Value;
            int losable = (int)(outcome.RaidStrength * RaidResolutionTuning.DeathsPerCombatPower) + 1;
            Assert.True(outcome.TotalLivesLost <= losable,
                "a city lost " + outcome.TotalLivesLost + " people to a raid that could account for at most " + losable);
            Assert.True(city.TotalPopulation >= before - losable);
        }

        [Fact]
        public void A_settlements_statistical_cohort_defends_it_without_ever_being_enumerated()
        {
            Settlement bare = Town("Bare", 31, citizens: 4, cohort: 0, steel: 0);
            Settlement peopled = Town("Peopled", 32, citizens: 4, cohort: 2000, steel: 0);

            Assert.True(SettlementRaidResolver.DefenceStrengthOf(peopled) > SettlementRaidResolver.DefenceStrengthOf(bare),
                "a settlement of two thousand defended itself no better than one of four");
            Assert.True(SettlementRaidResolver.MusterOf(peopled) > SettlementRaidResolver.MusterOf(bare));

            // The cohort has no Pawn per head by the tier's own design, and answering this question must never
            // materialise one: the live roster is still exactly the four citizens that were added.
            Assert.Equal(4, peopled.Citizens.Count);

            // The militia is a share of the cohort, not all of it — a settlement of forty thousand fields an
            // army, not forty thousand soldiers.
            Assert.True(SettlementRaidResolver.MusterOf(peopled) < peopled.TotalPopulation);
        }

        [Fact]
        public void A_citizen_killed_by_an_off_map_raid_dies_the_same_way_anyone_else_does()
        {
            // The trap this avoids: reaching for Pawn_HealthTracker.Kill directly would leave the family tree
            // and the chronicle disagreeing about who is alive. Every citizen death here goes through
            // FamilyManager.HandleDeath, exactly as a death from age does.
            Settlement town = Town("Bereaved", 41, citizens: 30, cohort: 0, steel: 0);
            var founders = town.Citizens.ToList();
            Find.FamilyManager.FoundHousehold(founders[0], founders[1], Find.TickManager.TicksGame);
            int familyId = founders[0].relations.familyId;
            int livingBefore = Find.FamilyManager.GetFamily(familyId)!.livingCount;

            int killed = 0;
            for (int i = 0; i < 40 && killed == 0; i++)
            {
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, TargetOf(town), Raiders("Killers"), points: 1200f));
                killed = worker.LastRaidOutcome!.Value.CitizensKilled;
            }
            Assert.True(killed > 0, "no raid in forty tries killed a citizen, so this test proved nothing");

            // The chronicle knows who died and why...
            Assert.Contains(Find.Storyteller.Chronicle,
                e => e.incidentDefName == "Death" && e.deathCause == DeathCause.Injury);

            // ...and the roster stopped counting them at once rather than at the next rare sync.
            Assert.DoesNotContain(town.Citizens, p => p.Dead);
            Assert.Equal(town.Citizens.Count, town.TotalPopulation);

            // If the household lost anybody, its own living count moved with them — the bookkeeping that only
            // happens on the real death path.
            Family family = Find.FamilyManager.GetFamily(familyId)!;
            int householdDead = founders.Take(2).Count(p => p.Dead);
            Assert.Equal(livingBefore - householdDead, family.livingCount);
        }

        [Fact]
        public void Losing_citizens_to_an_unwatched_raid_moves_the_storytellers_own_adaptation()
        {
            // A raid changes the director as well as the settlement: RimWorld's adaptation drops when the
            // player loses people to a threat, which is what makes the next raid smaller after a bad one.
            // Before this module nothing in the core called Notify_ColonistDied at all.
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            StoryWatcher_Adaptation adaptation = Find.Storyteller.adaptation;
            for (int i = 0; i < 4000; i++) adaptation.AdaptationTick(medium);
            float quietDays = adaptation.AdaptDays;
            Assert.True(quietDays > StoryWatcher_Adaptation.DeathAdaptDaysPenalty,
                "needed a peaceful stretch on the clock before a death could be seen to cut it short");

            int killed = 0;
            for (int i = 0; i < 40 && killed == 0; i++)
            {
                Settlement town = Town("Adapt" + i, 300 + i, citizens: 8, cohort: 0, steel: 0);
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, TargetOf(town), Raiders("Costly"), points: 900f));
                killed = worker.LastRaidOutcome!.Value.CitizensKilled;
            }
            Assert.True(killed > 0, "no raid in forty tries killed a citizen, so this test proved nothing");

            Assert.True(adaptation.AdaptDays < quietDays,
                "the civilization lost " + killed + " people and the storyteller's quiet-time clock did not move");
        }

        // ---- selection was deliberately left alone ----

        [Fact]
        public void Raids_still_reach_settlements_nobody_is_watching()
        {
            // The obvious fix — aim raids at the settlement the player has open — is the one this lane
            // rejected. A civilization is meant to run without the player watching every town; raids that only
            // ever land where the camera points make the rest of the world a stage set, which is the exact
            // thing §11's tiering exists to prevent. Once an unwatched raid is real, there is nothing left for
            // a bias to buy. So selection is untouched, and this is the test that keeps it that way.
            Settlement watched = Town("Watched", 61, citizens: 10, cohort: 400, steel: 0);
            Settlement elsewhere = Town("Elsewhere", 62, citizens: 10, cohort: 400, steel: 0);
            CivilizationTarget target = TargetOf(watched, elsewhere);
            Find.God.Attention.Focus(watched);

            var rand = new RandomStream(4242);
            int elsewhereHits = 0;
            const int Rolls = 1000;
            for (int i = 0; i < Rolls; i++)
            {
                if (ReferenceEquals(target.ChooseTargetSettlement(rand), elsewhere)) elsewhereHits++;
            }

            // Two equally populated towns, one of them watched: still a coin flip, not a pull toward the camera.
            Assert.InRange(elsewhereHits / (double)Rolls, 0.4, 0.6);
        }

        // ---- determinism and persistence ----

        [Fact]
        public void The_same_seed_resolves_the_same_raid()
        {
            SettlementRaidOutcome first = ResolveOnceFromSeed(9001, out string firstLine);
            SettlementRaidOutcome second = ResolveOnceFromSeed(9001, out string secondLine);

            Assert.Equal(first.Repelled, second.Repelled);
            Assert.Equal(first.CitizensKilled, second.CitizensKilled);
            Assert.Equal(first.CohortLosses, second.CohortLosses);
            Assert.Equal(first.RaidersKilled, second.RaidersKilled);
            Assert.Equal(first.GoodsLooted, second.GoodsLooted);
            Assert.Equal(first.DefenceStrength, second.DefenceStrength, 3);
            Assert.Equal(first.RaidStrength, second.RaidStrength, 3);
            Assert.Equal(firstLine, secondLine);
        }

        private static SettlementRaidOutcome ResolveOnceFromSeed(int seed, out string chronicleLine)
        {
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Find.Storyteller = new Storyteller();

            Settlement town = Town("Determinism", 71, citizens: 9, cohort: 300, steel: 80);
            IncidentWorker_RaidEnemy worker = NewWorker();
            Assert.True(Fire(worker, TargetOf(town), Raiders("Same")));

            chronicleLine = Find.Storyteller.Chronicle
                .First(e => e.incidentDefName.StartsWith("Raid:", System.StringComparison.Ordinal)).incidentDefName;
            return worker.LastRaidOutcome!.Value;
        }

        [Fact]
        public void What_an_abstract_raid_did_survives_a_save_and_a_load()
        {
            // The resolver keeps no state of its own — it spends the raid on the world and on the chronicle,
            // so those two are what has to round-trip. A raid the save forgets is a raid that did nothing,
            // one reload later.
            Game game = NewSoloGame("raid-scribe");
            Settlement settlement = game.CivilizationTarget.Settlements.Single();
            settlement.AddStatisticalPeople(2000);
            settlement.AddStore(Steel, 200);

            IncidentWorker_RaidEnemy worker = NewWorker();
            Assert.True(Fire(worker, game.CivilizationTarget, Raiders("Scribed"), points: 800f));
            SettlementRaidOutcome outcome = worker.LastRaidOutcome!.Value;
            Assert.True(outcome.ChangedTheWorld);

            int populationAfter = settlement.TotalPopulation;
            int steelAfter = settlement.StoreCountOf(Steel);
            string raidLine = Find.Storyteller.Chronicle
                .First(e => e.incidentDefName.StartsWith("Raid:", System.StringComparison.Ordinal)).incidentDefName;

            string worldXml = Scribe.SaveToString(game.World!, "world");
            global::SimWorld.World.World loadedWorld =
                Scribe.Load<global::SimWorld.World.World>(worldXml, "world", out IReadOnlyList<string> worldErrors, Content.Database);
            Assert.Empty(worldErrors);

            string storytellerXml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            Storyteller loadedStoryteller =
                Scribe.Load<Storyteller>(storytellerXml, "storyteller", out IReadOnlyList<string> storytellerErrors, Content.Database);
            Assert.Empty(storytellerErrors);

            Settlement loaded = loadedWorld.worldObjects.OfType<Settlement>().First(s => s.name == settlement.name);
            Assert.Equal(populationAfter, loaded.TotalPopulation);
            Assert.Equal(steelAfter, loaded.StoreCountOf(Steel));
            Assert.Contains(loadedStoryteller.Chronicle, e => e.incidentDefName == raidLine);
        }

        // ---- helpers ----

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
    }
}
