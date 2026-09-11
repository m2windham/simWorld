using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Offices
{
    /// <summary>
    /// Stations: who holds one, what holding one does, and what happens when the holder dies
    /// (<see cref="OfficeDef"/>, <see cref="OfficeManager"/>).
    /// <para/>
    /// The load-bearing ones are
    /// <see cref="A_station_holder_is_never_displaced_by_the_Full_tier_cap_however_large_the_settlement_grows"/>
    /// — the reason this module exists, since <see cref="AttentionBudget"/> ranks <c>hasRole</c> first and
    /// nothing could earn it — and
    /// <see cref="A_steward_who_dies_is_succeeded_and_the_chronicle_records_it"/>, which is the question the
    /// design had to answer rather than assume.
    /// <para/>
    /// Nothing here asserts a tuning literal: the numbers this module owns are a sweep interval, an electorate
    /// cap, a candidate pool size and two mood offsets, and each is pinned by a band, a direction or an
    /// invariant instead.
    /// </summary>
    [Collection("GlobalDefs")]
    public class OfficeTests : ContentTestBase
    {
        public OfficeTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static CoreWorld NewWorld(string seed) =>
            WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 3, soloStart: true);

        /// <summary>Founds a real band on the first free land tile — the only shape that puts live, adult
        /// <c>Pawn</c>s on a roster. The same fixture <c>AttentionBudgetTests.Found</c> uses, for the same
        /// reason it exists there.</summary>
        private static Settlement Found(CoreWorld world, int seed, string name, int bandSize = 20)
        {
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            for (int tile = 0; tile < world.grid.TilesCount; tile++)
            {
                if (taken.Contains(tile) || world.grid.Tiles[tile].WaterCovered) continue;
                return SettlementFounder.Found(world, tile, world.factions[0], bandSize, new RandomStream(seed), name);
            }

            Assert.Fail("no free land tile for a settlement");
            return null!;
        }

        private static Settlement SettledWorld(string seed, int foundSeed, string name, out CoreWorld world)
        {
            world = NewWorld(seed);
            Find.World = world;
            Settlement town = Found(world, foundSeed, name);
            OfficeManager.ReconcileWorld(world);
            return town;
        }

        private static Pawn Steward(Settlement town) =>
            OfficeManager.HolderIn(OfficeDefOf.Steward, town)
            ?? throw new Xunit.Sdk.XunitException("no steward seated in " + town.name);

        private static Pawn NewAdult(float ageYears = 34f) =>
            PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Tribesperson, fixedBiologicalAge: ageYears));

        private static IEnumerable<string> ChronicleLines() =>
            Find.Storyteller.Chronicle.Select(e => e.incidentDefName);

        // ---- the seat exists at all ----

        [Fact]
        public void A_founded_settlement_seats_a_steward_and_the_civilization_seats_an_elder()
        {
            Settlement town = SettledWorld("offices-seated", 11, "Seatington", out CoreWorld _);

            Pawn steward = Steward(town);
            Assert.Same(OfficeDefOf.Steward, OfficeManager.OfficeOf(steward));
            Assert.Same(OfficeDefOf.Steward.role, steward.workSettings.Role);

            Pawn? elder = OfficeManager.HolderIn(OfficeDefOf.Elder, town);
            Assert.NotNull(elder);
            Assert.Same(OfficeDefOf.Elder, OfficeManager.OfficeOf(elder!));

            // One office per citizen: the eldership is decided first (precedence 0), so whoever holds it is
            // not also the steward.
            Assert.NotSame(elder, steward);

            // Both seatings are in the record, and the first of them is a moment the curator kept.
            Assert.Contains(ChronicleLines(), l => l.StartsWith("Accession:") && l.Contains("stewardship of Seatington"));
            Assert.Contains(ChronicleLines(), l => l.StartsWith("Accession:") && l.Contains("eldership"));
            Assert.Contains(Find.Storyteller.Moments, m => m.incidentDefName.StartsWith("Accession:"));
        }

        [Fact]
        public void Reconciling_twice_over_an_unchanged_roster_changes_nothing_and_writes_nothing()
        {
            Settlement town = SettledWorld("offices-idempotent", 12, "Sameville", out CoreWorld world);

            Pawn steward = Steward(town);
            int linesAfterFirst = Find.Storyteller.Chronicle.Count;

            OfficeManager.ReconcileWorld(world);
            OfficeManager.ReconcileWorld(world);

            Assert.Same(steward, Steward(town));
            Assert.Equal(linesAfterFirst, Find.Storyteller.Chronicle.Count);
        }

        // ---- significance: the reason this module exists ----

        [Fact]
        public void Taking_a_station_promotes_a_citizen_to_Full_and_holds_them_there_when_attention_leaves()
        {
            Settlement town = SettledWorld("offices-significance", 13, "Holdfast", out CoreWorld _);
            Pawn steward = Steward(town);

            Assert.True(steward.tier.HasRole);
            Assert.True(steward.tier.Significant);
            Assert.Equal(PawnTier.Full, steward.tier.Tier);

            // Attention comes and goes; the station does not care. A citizen with no station falls to Interval
            // the moment the god looks away, which is what makes the steward staying Full meaningful.
            Pawn ordinary = town.Citizens.First(p => OfficeManager.OfficeOf(p) == null);
            Find.God.Attention.Focus(town);
            Assert.Equal(PawnTier.Full, ordinary.tier.Tier);

            Find.God.Attention.ClearFocus();
            Assert.Equal(PawnTier.Interval, ordinary.tier.Tier);
            Assert.Equal(PawnTier.Full, steward.tier.Tier);
            Assert.Equal(0, AttentionBudget.RankOf(steward.tier));
        }

        [Fact]
        public void A_station_holder_is_never_displaced_by_the_Full_tier_cap_however_large_the_settlement_grows()
        {
            CoreWorld world = NewWorld("offices-budget");
            Find.World = world;
            Settlement town = Found(world, 14, "Throneholme");

            // Three times the budget. The fillers are newborns, so none of them can stand for a seat and the
            // stewardship goes to the founding band — who are also the most senior on the tie-break, which
            // would make the assertion below vacuous. So the invariant is tested twice over: on the seated
            // steward, and on the single most junior citizen in the settlement, flagged by station directly.
            while (town.Citizens.Count < TieringTuning.FullTierBudget * 3)
            {
                town.AddCitizen(NewHuman("Latecomer"));
            }

            OfficeManager.ReconcileWorld(world);
            Pawn steward = Steward(town);

            Pawn mostJunior = town.Citizens.OrderBy(p => p.thingIDNumber).Last();
            Assert.NotSame(steward, mostJunior);
            mostJunior.tier.Notify_RoleChanged(true);

            Find.God.Attention.Focus(town);
            Assert.Equal(TieringTuning.FullTierBudget, town.PopulationOf(PawnTier.Full));
            Assert.Equal(PawnTier.Full, steward.tier.Tier);
            Assert.False(steward.tier.AttentionWithheld);
            Assert.Equal(PawnTier.Full, mostJunior.tier.Tier);
            Assert.False(mostJunior.tier.AttentionWithheld);

            // Grow again by a whole budget's worth and reconcile: still seated, still Full, still within cap.
            for (int i = 0; i < TieringTuning.FullTierBudget; i++) town.AddCitizen(NewHuman("Newcomer"));
            Find.God.Attention.Reconcile();
            OfficeManager.ReconcileWorld(world);

            Assert.Same(steward, Steward(town));
            Assert.Equal(PawnTier.Full, steward.tier.Tier);
            Assert.Equal(PawnTier.Full, mostJunior.tier.Tier);
            Assert.Equal(TieringTuning.FullTierBudget, town.PopulationOf(PawnTier.Full));
        }

        // ---- succession ----

        [Fact]
        public void A_steward_who_dies_is_succeeded_and_the_chronicle_records_it()
        {
            Settlement town = SettledWorld("offices-succession", 15, "Mourncross", out CoreWorld world);
            Pawn first = Steward(town);

            first.health.Kill(null, null);
            OfficeManager.ReconcileWorld(world);

            Pawn second = Steward(town);
            Assert.NotSame(first, second);
            Assert.False(second.Dead);
            Assert.Same(OfficeDefOf.Steward.role, second.workSettings.Role);

            // The late steward keeps nothing: the role is stripped, and so is the station that held them Full.
            Assert.Null(first.workSettings.Role);
            Assert.False(first.tier.HasRole);

            // The succession is legible from both ends — who died holding it, and who took it.
            Assert.Contains(ChronicleLines(), l => l.StartsWith("Succession:") && l.Contains(first.Label));
            Assert.Contains(ChronicleLines(), l => l.StartsWith("Accession:") && l.Contains(second.Label));
        }

        [Fact]
        public void A_settlement_with_nobody_eligible_goes_without_and_seats_one_again_when_it_can()
        {
            CoreWorld world = NewWorld("offices-vacancy");
            Find.World = world;
            Settlement town = Found(world, 16, "Emptyhall");

            // Kill the whole band: nobody is left to stand, so the seat stays empty rather than falling to
            // somebody ineligible.
            foreach (Pawn p in town.Citizens.ToList()) p.health.Kill(null, null);
            OfficeManager.ReconcileWorld(world);
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Steward, town));

            // A century on, somebody eligible arrives. The next sweep seats them: for a settlement, an empty
            // seat is a vacancy rather than a retirement. (They are far too young for the eldership, which is
            // the contrasting case below.)
            Find.TickManager.DebugSetTicksGame(100 * GenDate.TicksPerYear);
            Pawn arrival = NewAdult();
            town.AddCitizen(arrival);
            OfficeManager.ReconcileWorld(world);

            Assert.Same(arrival, OfficeManager.HolderIn(OfficeDefOf.Steward, town));
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Elder, town));
        }

        [Fact]
        public void The_eldership_retires_itself_once_nobody_alive_remembers_the_founding()
        {
            CoreWorld world = NewWorld("offices-eldership");
            Find.World = world;
            Settlement town = Found(world, 17, "Longmemory");
            OfficeManager.ReconcileWorld(world);
            Assert.NotNull(OfficeManager.HolderIn(OfficeDefOf.Elder, town));

            // A century passes and the founding band is gone; everyone left was born since. The stewardship
            // is refilled — a settlement always needs someone to speak for it — and the eldership cannot be,
            // because nobody alive is old enough to have seen the founding. That is the whole of its
            // retirement: no flag, no saved state, just an eligibility rule nobody can satisfy any more.
            Find.TickManager.DebugSetTicksGame(100 * GenDate.TicksPerYear);
            foreach (Pawn p in town.Citizens.ToList()) p.health.Kill(null, null);
            for (int i = 0; i < 6; i++) town.AddCitizen(NewAdult(30f));

            OfficeManager.ReconcileWorld(world);

            Assert.NotNull(OfficeManager.HolderIn(OfficeDefOf.Steward, town));
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Elder, town));
        }

        [Fact]
        public void An_incumbent_is_never_re_contested_while_they_live()
        {
            Settlement town = SettledWorld("offices-incumbency", 18, "Steadyrock", out CoreWorld world);
            Pawn steward = Steward(town);

            // Everyone in town comes to loathe the steward. Under a rule that re-elected every sweep this
            // could unseat them; tenure is for life, so it does not — which is what keeps the system from
            // trading the stewardship back and forth and paying a promotion/demotion pair each time.
            foreach (Pawn elector in town.Citizens.Where(p => p != steward))
            {
                for (int i = 0; i < 3; i++) elector.needs.mood?.thoughts.memories.TryGainMemory(Insulted, steward);
            }
            Assert.True(town.Citizens.Where(p => p != steward).Sum(p => p.relations.OpinionOf(steward)) < 0);

            OfficeManager.ReconcileWorld(world);
            Assert.Same(steward, Steward(town));
        }

        // ---- the election itself ----

        [Fact]
        public void The_seat_goes_to_the_best_esteemed_of_the_candidates_and_the_candidates_are_the_eldest()
        {
            Settlement town = SettledWorld("offices-esteem", 19, "Wellthoughtof", out CoreWorld _);
            Pawn steward = Steward(town);
            var worker = (OfficeSelectionWorker_Esteem)OfficeDefOf.Steward.Worker;
            var context = new OfficeContext(town.name, town.foundingTick);

            // Rebuild the field the election actually ran over: everyone eligible who was not already seated
            // in the higher-precedence eldership, strongest claim (eldest) first, ties to the more senior id.
            List<Pawn> standing = town.Citizens
                .Where(p => !p.Dead && OfficeManager.OfficeOf(p) != OfficeDefOf.Elder && worker.IsEligible(p, context))
                .OrderByDescending(p => p.ageTracker.AgeBiologicalYearsFloat)
                .ThenBy(p => p.thingIDNumber)
                .ToList();

            List<Pawn> candidates = standing.Take(OfficeDefOf.Steward.candidatePoolSize).ToList();
            Assert.Contains(steward, candidates);

            // Candidacy really is by years lived: nobody outside the pool is older than everybody in it.
            if (standing.Count > candidates.Count)
            {
                Assert.True(
                    candidates.Min(p => p.ageTracker.AgeBiologicalYearsFloat)
                    >= standing.Skip(candidates.Count).Max(p => p.ageTracker.AgeBiologicalYearsFloat),
                    "a citizen outside the candidate pool was older than one inside it");
            }

            // And among those candidates the seat went to the one the settlement thinks best of, ties to the
            // more senior — exactly the rule, recomputed here rather than restated as a literal.
            Pawn expected = candidates[0];
            float best = worker.Score(expected, town.Citizens, context);
            foreach (Pawn candidate in candidates.Skip(1))
            {
                float score = worker.Score(candidate, town.Citizens, context);
                if (score > best)
                {
                    best = score;
                    expected = candidate;
                }
            }
            Assert.Same(expected, steward);
        }

        [Fact]
        public void Esteem_falls_when_the_settlement_turns_on_a_candidate()
        {
            Settlement town = SettledWorld("offices-esteem-trend", 28, "Sourtown", out CoreWorld _);
            var worker = (OfficeSelectionWorker_Esteem)OfficeDefOf.Steward.Worker;
            var context = new OfficeContext(town.name, town.foundingTick);

            Pawn subject = town.Citizens.First(p => OfficeManager.OfficeOf(p) == null && !p.Dead);
            float before = worker.Score(subject, town.Citizens, context);

            foreach (Pawn elector in town.Citizens.Where(p => p != subject))
            {
                elector.needs.mood?.thoughts.memories.TryGainMemory(Insulted, subject);
            }

            float after = worker.Score(subject, town.Citizens, context);
            Assert.True(after < before, "a settlement's esteem for someone it has come to resent did not fall");
        }

        [Fact]
        public void Only_an_adult_stands_for_a_seat()
        {
            CoreWorld world = NewWorld("offices-adults");
            Find.World = world;
            Settlement town = Found(world, 20, "Growingup");
            foreach (Pawn p in town.Citizens.ToList()) p.health.Kill(null, null);

            Pawn child = NewAdult(9f);
            town.AddCitizen(child);

            OfficeManager.ReconcileWorld(world);
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Steward, town));
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Elder, town));
            Assert.False(child.tier.HasRole);
        }

        // ---- what a station actually does, beyond the tier ----

        [Fact]
        public void A_steward_really_does_take_up_the_stewardship_work_first()
        {
            Settlement town = SettledWorld("offices-work", 21, "Busyfields", out CoreWorld world);
            Pawn steward = Steward(town);
            Pawn ordinary = town.Citizens.First(p => OfficeManager.OfficeOf(p) == null && !p.Dead);

            RoleDef role = OfficeDefOf.Steward.role;
            WorkTypeDef emphasized = role.emphasizedWorkTypes[0];
            WorkTypeDef unemphasized = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .First(w => !role.emphasizedWorkTypes.Contains(w) && !steward.WorkTypeIsDisabled(w));

            // Better (lower-numbered) than an ordinary citizen's, and better than the same citizen's own
            // priority for work the station says nothing about — comparisons, not literals.
            Assert.True(steward.workSettings.GetPriority(emphasized) < ordinary.workSettings.GetPriority(emphasized));
            Assert.True(steward.workSettings.GetPriority(emphasized) < steward.workSettings.GetPriority(unemphasized));

            // And the scan order the think tree actually walks reflects it.
            IReadOnlyList<WorkGiverDef> order = steward.workSettings.WorkGiversInOrderNormal;
            int firstEmphasized = IndexOfGiverFor(order, emphasized);
            int firstUnemphasized = IndexOfGiverFor(order, unemphasized);
            Assert.True(firstEmphasized >= 0, "no work giver exists for the work the stewardship emphasizes");
            Assert.True(firstUnemphasized < 0 || firstEmphasized < firstUnemphasized);

            // Losing the seat leaves no trace of the tenure: every priority the role wrote is back at default.
            steward.health.Kill(null, null);
            OfficeManager.ReconcileWorld(world);
            Assert.Equal(Pawn_WorkSettings.DefaultPriority, steward.workSettings.GetPriority(emphasized));
        }

        [Fact]
        public void Holding_a_station_reaches_mood_and_ends_with_the_tenure()
        {
            Settlement town = SettledWorld("offices-mood", 22, "Gladhall", out CoreWorld _);
            Pawn steward = Steward(town);

            ThoughtDef stewardship = DefDatabase<ThoughtDef>.GetNamed("HoldsStewardship");
            Assert.True(stewardship.IsSituational);

            steward.needs.mood!.thoughts.situational.Notify_SituationalThoughtsDirty();
            float withOffice = steward.needs.mood.thoughts.TotalMoodOffset();

            // Unseated by the god's own population-wide work policy, which clears every standing role. A
            // situational thought is never granted or revoked, so this is the whole mechanism.
            WorkPolicyUtility.ApplyRoleToPopulation(town.Citizens, null);
            steward.needs.mood.thoughts.situational.Notify_SituationalThoughtsDirty();
            float withoutOffice = steward.needs.mood.thoughts.TotalMoodOffset();

            Assert.True(withOffice > withoutOffice, "holding the stewardship left mood no better than losing it");
        }

        // ---- interaction with the work-policy lever that shares the RoleDef ----

        [Fact]
        public void A_civilization_wide_work_policy_cannot_permanently_unseat_a_station()
        {
            Settlement town = SettledWorld("offices-policy", 23, "Policyville", out CoreWorld world);
            Pawn steward = Steward(town);

            // The god sweeps the whole settlement onto a work role. Pawn_WorkSettings.Role is one field, so
            // that writes over the steward's own role and the stewardship is momentarily held by nobody.
            RoleDef farmer = DefDatabase<RoleDef>.GetNamed("Farmer");
            WorkPolicyUtility.ApplyRoleToPopulation(town.Citizens, farmer);
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Steward, town));

            // The next sweep corrects it, and re-seats the incumbent rather than electing afresh: an office's
            // role is reserved for its office, self-healing rather than guarded.
            OfficeManager.ReconcileWorld(world);
            Assert.Same(steward, Steward(town));
        }

        [Fact]
        public void A_second_carrier_of_a_stations_role_is_stripped_and_the_seat_stays_singular()
        {
            Settlement town = SettledWorld("offices-singular", 24, "Onechair", out CoreWorld world);
            Pawn steward = Steward(town);
            Pawn pretender = town.Citizens.First(p => OfficeManager.OfficeOf(p) == null && !p.Dead);

            pretender.workSettings.SetRole(OfficeDefOf.Steward.role);
            OfficeManager.ReconcileWorld(world);

            Assert.Same(steward, Steward(town));
            Assert.Null(pretender.workSettings.Role);
            Assert.Single(town.Citizens, p => OfficeManager.Holds(p, OfficeDefOf.Steward));
        }

        // ---- determinism ----

        [Fact]
        public void Two_identical_worlds_seat_the_same_citizens()
        {
            Settlement a = SettledWorld("offices-determinism", 25, "Twinsburg", out CoreWorld _);
            int stewardId = Steward(a).thingIDNumber;
            int elderId = OfficeManager.HolderIn(OfficeDefOf.Elder, a)!.thingIDNumber;

            // Same seeds from a clean slate — including the thing-id allocator, which is what makes the ids
            // above comparable across two runs at all.
            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(12345);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();

            Settlement b = SettledWorld("offices-determinism", 25, "Twinsburg", out CoreWorld _);
            Assert.Equal(stewardId, Steward(b).thingIDNumber);
            Assert.Equal(elderId, OfficeManager.HolderIn(OfficeDefOf.Elder, b)!.thingIDNumber);
        }

        [Fact]
        public void Stations_survive_a_save_and_reload_and_the_reloaded_world_agrees_about_who_holds_them()
        {
            Settlement town = SettledWorld("offices-scribe", 26, "Scribehold", out CoreWorld world);
            Pawn steward = Steward(town);
            Pawn elder = OfficeManager.HolderIn(OfficeDefOf.Elder, town)!;

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Scribehold");
            Pawn loadedSteward = Steward(reloaded);
            Pawn loadedElder = OfficeManager.HolderIn(OfficeDefOf.Elder, reloaded)!;

            // The seat is the role the citizen carries, so a round trip cannot desynchronise the two — there
            // is no second roster to disagree with the first.
            Assert.Equal(steward.thingIDNumber, loadedSteward.thingIDNumber);
            Assert.Equal(elder.thingIDNumber, loadedElder.thingIDNumber);
            Assert.Same(OfficeDefOf.Steward.role, loadedSteward.workSettings.Role);
            Assert.True(loadedSteward.tier.HasRole);
            Assert.Equal(PawnTier.Full, loadedSteward.tier.Tier);
            Assert.Equal(0, AttentionBudget.RankOf(loadedSteward.tier));

            // And the work the station emphasizes survives with it, rather than needing a re-application pass.
            WorkTypeDef emphasized = OfficeDefOf.Steward.role.emphasizedWorkTypes[0];
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, loadedSteward.workSettings.GetPriority(emphasized));

            // Reconciling the loaded world is a no-op rather than a re-election.
            Find.World = loaded;
            int before = Find.Storyteller.Chronicle.Count;
            OfficeManager.ReconcileWorld(loaded);
            Assert.Same(loadedSteward, Steward(reloaded));
            Assert.Equal(before, Find.Storyteller.Chronicle.Count);
        }

        // ---- content and wiring ----

        [Fact]
        public void Shipped_offices_are_coherent_content()
        {
            IReadOnlyList<OfficeDef> offices = DefDatabase<OfficeDef>.AllDefsListForReading;
            Assert.NotEmpty(offices);
            Assert.All(offices, o => Assert.Empty(o.ConfigErrors()));

            // The role IS the seat's identity, so two offices sharing one would make both unidentifiable.
            // Asserted here rather than in ConfigErrors, which runs before this database is readable.
            Assert.Equal(offices.Count, offices.Select(o => o.role).Distinct().Count());

            Assert.All(offices, o =>
            {
                Assert.NotNull(o.holderThought);
                Assert.True(o.holderThought!.IsSituational);
                Assert.Equal(typeof(ThoughtWorker_HoldsOffice), o.holderThought.workerClass);
            });

            // The two shipped stations emphasize different work: two stations doing the same work would be
            // one station with two names.
            Assert.Empty(OfficeDefOf.Steward.role.emphasizedWorkTypes.Intersect(OfficeDefOf.Elder.role.emphasizedWorkTypes));
            Assert.True(OfficeDefOf.Elder.precedence < OfficeDefOf.Steward.precedence);
            Assert.Equal(OfficeScope.Civilization, OfficeDefOf.Elder.scope);
            Assert.Equal(OfficeScope.Settlement, OfficeDefOf.Steward.scope);
        }

        [Fact]
        public void A_real_game_seats_its_settlements_steward_by_simply_running()
        {
            // End to end through Game's own tick order rather than by calling the sweep: this is the only
            // thing that proves the one line in Sim/Game.cs's WireTickHooks is actually there and firing.
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario, "offices-end-to-end", subdivisionOverride: 3, soloStart: true);

            Settlement town = game.World!.worldObjects.OfType<Settlement>().First(s => s.Citizens.Count > 0);
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Steward, town));

            for (int i = 0; i < OfficeTuning.ReconcileIntervalTicks; i++) game.TickManager.DoSingleTick();

            Pawn steward = Steward(town);
            Assert.True(steward.tier.HasRole);
            Assert.Equal(0, AttentionBudget.RankOf(steward.tier));
            Assert.Contains(
                game.Storyteller.Chronicle.Select(e => e.incidentDefName),
                l => l.StartsWith("Accession:") && l.Contains(town.name));
        }

        [Fact]
        public void The_sweep_is_self_gated_so_a_per_tick_caller_costs_a_modulo()
        {
            CoreWorld world = NewWorld("offices-gate");
            Find.World = world;
            Settlement town = Found(world, 27, "Gatetown");

            Find.TickManager.DebugSetTicksGame(OfficeTuning.ReconcileIntervalTicks + 1);
            OfficeManager.Tick();
            Assert.Null(OfficeManager.HolderIn(OfficeDefOf.Steward, town));

            Find.TickManager.DebugSetTicksGame(OfficeTuning.ReconcileIntervalTicks * 2);
            OfficeManager.Tick();
            Assert.NotNull(OfficeManager.HolderIn(OfficeDefOf.Steward, town));
        }

        // ---- helpers ----

        private static ThoughtDef Insulted => DefDatabase<ThoughtDef>.GetNamed("Insulted");

        private static int IndexOfGiverFor(IReadOnlyList<WorkGiverDef> order, WorkTypeDef workType)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].workType == workType) return i;
            }
            return -1;
        }
    }
}
