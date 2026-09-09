using System.Collections.Generic;
using System.Linq;
using SimWorld.Caravans;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

namespace SimWorld.Tests.Factions
{
    public class FactionTests : ContentTestBase
    {
        public FactionTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static like TickManager/Storyteller but ContentTestBase (which we
            // may not edit) does not reset it, so each test starts with its own fresh instance.
            Find.FactionManager = new FactionManager();
        }

        // ---- helpers ----

        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");
        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");
        private static FactionDef RoughDef => DefDatabase<FactionDef>.GetNamed("RoughOutlanders");

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        private static global::SimWorld.World.World GenerateSmallWorld(string seed, int subdivision = 3)
        {
            return WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision);
        }

        /// <summary>The largest set of tiles reachable from each other using only edges WorldPathGrid treats as passable.</summary>
        private static List<int> LargestPassableComponent(WorldGrid grid)
        {
            var visited = new HashSet<int>();
            List<int> best = new List<int>();
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (visited.Contains(i) || WorldPathGrid.CalculatedMovementDifficultyAt(grid.Tiles[i]) >= WorldPathGrid.ImpassableMovementDifficulty)
                {
                    continue;
                }
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited.Add(i);
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);
                    foreach (int n in grid.NeighborsOf(cur))
                    {
                        if (visited.Contains(n) || WorldPathGrid.CalculatedMovementDifficultyAt(grid.Tiles[n]) >= WorldPathGrid.ImpassableMovementDifficulty) continue;
                        visited.Add(n);
                        queue.Enqueue(n);
                    }
                }
                if (component.Count > best.Count) best = component;
            }
            return best;
        }

        // ---- goodwill clamping and thresholds ----

        [Fact]
        public void Goodwill_clamps_to_valid_range()
        {
            Faction a = NewFaction(TribalDef, "A");
            Faction b = NewFaction(OutlanderDef, "B");

            Assert.True(a.TryAffectGoodwillWith(b, 60));
            Assert.Equal(60, a.GoodwillWith(b));
            Assert.True(a.TryAffectGoodwillWith(b, 60));
            Assert.Equal(100, a.GoodwillWith(b));

            Assert.True(a.TryAffectGoodwillWith(b, -260));
            Assert.Equal(-100, a.GoodwillWith(b));

            // Already at the floor: no further change is possible, so this is a no-op.
            Assert.False(a.TryAffectGoodwillWith(b, -10));
            Assert.Equal(-100, a.GoodwillWith(b));
        }

        [Fact]
        public void Goodwill_is_symmetric_between_both_sides()
        {
            Faction a = NewFaction(TribalDef, "A");
            Faction b = NewFaction(OutlanderDef, "B");

            a.TryAffectGoodwillWith(b, 30);
            Assert.Equal(30, a.GoodwillWith(b));
            Assert.Equal(30, b.GoodwillWith(a));

            b.TryAffectGoodwillWith(a, -10);
            Assert.Equal(20, a.GoodwillWith(b));
            Assert.Equal(20, b.GoodwillWith(a));
        }

        [Fact]
        public void Relation_kind_updates_at_thresholds()
        {
            Faction a = NewFaction(TribalDef, "A");
            Faction b = NewFaction(OutlanderDef, "B");

            Assert.Equal(FactionRelationKind.Neutral, a.RelationKindWith(b));

            a.TryAffectGoodwillWith(b, 75); // 0 -> 75
            Assert.Equal(FactionRelationKind.Ally, a.RelationKindWith(b));
            Assert.Equal(FactionRelationKind.Ally, b.RelationKindWith(a));

            a.TryAffectGoodwillWith(b, -200); // clamps to -100
            Assert.Equal(-100, a.GoodwillWith(b));
            Assert.Equal(FactionRelationKind.Hostile, a.RelationKindWith(b));
            Assert.True(a.HostileTo(b));
            Assert.True(b.HostileTo(a));

            a.TryAffectGoodwillWith(b, 20); // -100 -> -80: still <= -75, so still Hostile
            Assert.Equal(-80, a.GoodwillWith(b));
            Assert.Equal(FactionRelationKind.Hostile, a.RelationKindWith(b));
        }

        [Fact]
        public void Permanent_enemy_pairs_never_change_goodwill()
        {
            Faction a = NewFaction(RoughDef, "Raiders");
            Faction b = NewFaction(PlayerDef, "Player");
            a.SetRelationDirect(b, FactionRelationKind.Hostile, -100);

            Assert.False(a.TryAffectGoodwillWith(b, 50));
            Assert.Equal(-100, a.GoodwillWith(b));
            Assert.Equal(FactionRelationKind.Hostile, a.RelationKindWith(b));
        }

        [Fact]
        public void Goodwill_changed_event_fires_with_the_actual_applied_change()
        {
            Faction a = NewFaction(TribalDef, "A");
            Faction b = NewFaction(OutlanderDef, "B");

            (Faction from, Faction to, int change, string? reason)? seen = null;
            a.GoodwillChanged += (f, o, c, r) => seen = (f, o, c, r);

            a.TryAffectGoodwillWith(b, 40, reason: "Test");
            Assert.NotNull(seen);
            Assert.Same(a, seen!.Value.from);
            Assert.Same(b, seen.Value.to);
            Assert.Equal(40, seen.Value.change);
            Assert.Equal("Test", seen.Value.reason);

            // Clamped: only 60 of the requested 90 could actually apply.
            seen = null;
            a.TryAffectGoodwillWith(b, 90);
            Assert.Equal(60, seen!.Value.change);
        }

        [Fact]
        public void Relation_kind_changed_event_fires_only_when_a_threshold_is_crossed()
        {
            Faction a = NewFaction(TribalDef, "A");
            Faction b = NewFaction(OutlanderDef, "B");

            int kindChanges = 0;
            a.RelationKindChanged += (f, o, k, r) => kindChanges++;

            a.TryAffectGoodwillWith(b, 10); // Neutral -> Neutral: no threshold crossed
            Assert.Equal(0, kindChanges);

            a.TryAffectGoodwillWith(b, 70); // 10 -> 80: crosses into Ally
            Assert.Equal(1, kindChanges);

            a.TryAffectGoodwillWith(b, 5); // 80 -> 85: still Ally
            Assert.Equal(1, kindChanges);
        }

        // ---- initial relations ----

        [Fact]
        public void Initial_relations_permanent_enemy_locks_hostile_at_minus_100()
        {
            Faction rough = NewFaction(RoughDef, "Raiders");
            Faction player = NewFaction(PlayerDef, "Player");
            var rand = new RandomStream(1);

            rough.TryMakeInitialRelationsWith(player, rand);

            Assert.Equal(-100, rough.GoodwillWith(player));
            Assert.Equal(FactionRelationKind.Hostile, rough.RelationKindWith(player));
            Assert.Equal(-100, player.GoodwillWith(rough));
        }

        [Fact]
        public void Initial_relations_player_vs_npc_draws_from_the_npcs_startingGoodwill_range()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            var rand = new RandomStream(42);

            for (int i = 0; i < 20; i++)
            {
                Faction tribal = NewFaction(TribalDef, "Tribal" + i);
                player.TryMakeInitialRelationsWith(tribal, rand);
                Assert.True(TribalDef.startingGoodwill.Includes(player.GoodwillWith(tribal)));
                Assert.Equal(Faction.KindFromGoodwill(player.GoodwillWith(tribal)), player.RelationKindWith(tribal));
            }
        }

        [Fact]
        public void Initial_relations_npc_vs_npc_start_neutral()
        {
            Faction tribal = NewFaction(TribalDef, "Tribal");
            Faction outlander = NewFaction(OutlanderDef, "Outlander");
            var rand = new RandomStream(7);

            tribal.TryMakeInitialRelationsWith(outlander, rand);

            Assert.Equal(0, tribal.GoodwillWith(outlander));
            Assert.Equal(FactionRelationKind.Neutral, tribal.RelationKindWith(outlander));
        }

        // ---- natural goodwill drift ----

        [Fact]
        public void Natural_goodwill_drift_moves_toward_the_target_over_days()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            Faction tribal = NewFaction(TribalDef, "Tribal");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            tribal.SetRelationDirect(player, FactionRelationKind.Neutral, 0);

            Find.TickManager.DebugSetTicksGame(0);
            // TribalCivilization: naturalColonyGoodwill 0~40 (target 20), goodwillDailyGain 0.5/day.
            // 40 days at the check interval should move it roughly 40 * 0.5 = 20 points, reaching the target.
            int intervals = 40 * GenDate.TicksPerDay / Faction.GoodwillCheckInterval;
            for (int i = 1; i <= intervals; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * Faction.GoodwillCheckInterval);
                tribal.FactionTick();
            }

            Assert.True(tribal.GoodwillWith(player) > 15, "Expected goodwill to have drifted up substantially, was " + tribal.GoodwillWith(player));
            Assert.True(tribal.GoodwillWith(player) <= 20, "Drift should never overshoot the target.");
        }

        [Fact]
        public void Natural_goodwill_drift_does_not_affect_a_permanent_enemy()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            Faction rough = NewFaction(RoughDef, "Raiders");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rough);
            rough.SetRelationDirect(player, FactionRelationKind.Hostile, -100);

            Find.TickManager.DebugSetTicksGame(0);
            for (int i = 1; i <= 200; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * Faction.GoodwillCheckInterval);
                rough.FactionTick();
            }

            Assert.Equal(-100, rough.GoodwillWith(player));
        }

        // ---- FactionManager ----

        [Fact]
        public void FactionManager_OfPlayer_and_FirstFactionOfDef()
        {
            var manager = new FactionManager();
            Faction player = NewFaction(PlayerDef, "Player");
            Faction tribal = NewFaction(TribalDef, "Tribal");
            manager.Add(player);
            manager.Add(tribal);

            Assert.Same(player, manager.OfPlayer);
            Assert.Same(tribal, manager.FirstFactionOfDef(TribalDef));
            Assert.Null(manager.FirstFactionOfDef(RoughDef));
        }

        [Fact]
        public void FactionManager_GetFactions_filters_hidden_defeated_and_tech_level()
        {
            var manager = new FactionManager();
            Faction tribal = NewFaction(TribalDef, "Tribal"); // Neolithic
            Faction outlander = NewFaction(OutlanderDef, "Outlander"); // Industrial
            outlander.defeated = true;
            manager.Add(tribal);
            manager.Add(outlander);

            Assert.Equal(new[] { tribal }, manager.GetFactions().ToList());
            Assert.Equal(new[] { tribal, outlander }, manager.GetFactions(allowDefeated: true).ToList());
            Assert.Equal(new[] { outlander }, manager.GetFactions(allowDefeated: true, minTechLevel: TechLevel.Industrial).ToList());
        }

        [Fact]
        public void A_more_common_raider_is_picked_more_often()
        {
            // raidCommonality was declared but unconsumed until raids were built: RandomEnemyFaction picked
            // uniformly, so a faction content said should raid twice as often raided exactly as often as
            // everyone else. The assertion is a band, not a ratio: with a 4:1 weighting the common raider
            // should clearly dominate, without pinning the sampler's exact distribution.
            var manager = new FactionManager();
            Faction player = NewFaction(PlayerDef, "Player");
            Faction common = NewFaction(RoughDef, "Common");
            Faction rare = NewFaction(TribalDef, "Rare");
            manager.Add(player);
            manager.Add(common);
            manager.Add(rare);
            common.SetRelationDirect(player, FactionRelationKind.Hostile, -100);
            rare.SetRelationDirect(player, FactionRelationKind.Hostile, -100);

            float savedCommon = common.def.raidCommonality;
            float savedRare = rare.def.raidCommonality;
            try
            {
                common.def.raidCommonality = 4f;
                rare.def.raidCommonality = 1f;

                int commonPicks = 0;
                const int Draws = 400;
                for (int i = 0; i < Draws; i++)
                {
                    if (ReferenceEquals(manager.RandomEnemyFaction(), common)) commonPicks++;
                }

                Assert.InRange(commonPicks, Draws * 0.6, Draws * 0.95);
            }
            finally
            {
                common.def.raidCommonality = savedCommon;
                rare.def.raidCommonality = savedRare;
            }
        }

        [Fact]
        public void FactionManager_random_pickers_respect_relation_to_the_player()
        {
            var manager = new FactionManager();
            Faction player = NewFaction(PlayerDef, "Player");
            Faction enemy = NewFaction(RoughDef, "Enemy");
            Faction friend = NewFaction(TribalDef, "Friend");
            Faction stranger = NewFaction(OutlanderDef, "Stranger");
            manager.Add(player);
            manager.Add(enemy);
            manager.Add(friend);
            manager.Add(stranger);

            enemy.SetRelationDirect(player, FactionRelationKind.Hostile, -100);
            friend.SetRelationDirect(player, FactionRelationKind.Ally, 90);

            Assert.Same(enemy, manager.RandomEnemyFaction());
            Assert.Same(friend, manager.RandomAlliedFaction());
            var nonHostile = new HashSet<Faction> { friend, stranger };
            for (int i = 0; i < 10; i++)
            {
                Assert.Contains(manager.RandomNonHostileFaction()!, nonHostile);
            }
        }

        // ---- Diplomacy: war/peace and treaties (economy.diplomacy — SimWorld's own translation) ----

        private static TreatyDef NonAggressionPact => DefDatabase<TreatyDef>.GetNamed("NonAggressionPact");
        private static TreatyDef TradeAgreement => DefDatabase<TreatyDef>.GetNamed("TradeAgreement");
        private static TreatyDef Alliance => DefDatabase<TreatyDef>.GetNamed("Alliance");

        [Fact]
        public void Content_has_the_expected_treaty_defs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(NonAggressionPact.nonAggression);
            Assert.False(NonAggressionPact.tradeAccess);
            Assert.True(TradeAgreement.tradeAccess);
            Assert.False(TradeAgreement.nonAggression);
            Assert.True(Alliance.nonAggression && Alliance.tradeAccess);
        }

        [Fact]
        public void WarWith_is_false_between_freshly_related_factions()
        {
            Faction a = NewFaction(TribalDef, "WA");
            Faction b = NewFaction(OutlanderDef, "WB");

            Assert.False(a.WarWith(b));
            Assert.False(b.WarWith(a));
        }

        [Fact]
        public void DeclareWar_sets_WarState_symmetrically_and_drops_goodwill_toward_hostile()
        {
            Faction a = NewFaction(TribalDef, "DWA");
            Faction b = NewFaction(OutlanderDef, "DWB");
            a.TryAffectGoodwillWith(b, 40);

            bool result = a.DeclareWar(b);

            Assert.True(result);
            Assert.True(a.WarWith(b));
            Assert.True(b.WarWith(a));
            Assert.Equal(FactionRelationKind.Hostile, a.RelationKindWith(b));
            Assert.Equal(-100, a.GoodwillWith(b));
        }

        [Fact]
        public void DeclareWar_refuses_when_already_at_war()
        {
            Faction a = NewFaction(TribalDef, "AlreadyA");
            Faction b = NewFaction(OutlanderDef, "AlreadyB");
            Assert.True(a.DeclareWar(b));

            Assert.False(a.DeclareWar(b));
        }

        [Fact]
        public void DeclareWar_refuses_while_an_active_nonAggression_pact_holds()
        {
            Faction a = NewFaction(TribalDef, "PactA");
            Faction b = NewFaction(OutlanderDef, "PactB");
            Assert.True(a.SignTreaty(b, NonAggressionPact));

            Assert.False(a.DeclareWar(b));
            Assert.False(a.WarWith(b));
        }

        [Fact]
        public void MakePeace_clears_WarState_and_applies_the_peace_goodwill_gain()
        {
            Faction a = NewFaction(TribalDef, "PeaceA");
            Faction b = NewFaction(OutlanderDef, "PeaceB");
            Assert.True(a.DeclareWar(b));
            int atWar = a.GoodwillWith(b);

            bool result = a.MakePeace(b);

            Assert.True(result);
            Assert.False(a.WarWith(b));
            Assert.False(b.WarWith(a));
            Assert.True(a.GoodwillWith(b) > atWar, "peace should raise goodwill from where the war declaration left it");
        }

        [Fact]
        public void MakePeace_refuses_when_not_at_war()
        {
            Faction a = NewFaction(TribalDef, "NoWarA");
            Faction b = NewFaction(OutlanderDef, "NoWarB");

            Assert.False(a.MakePeace(b));
        }

        [Fact]
        public void MakePeace_refuses_for_a_permanent_enemy_pair()
        {
            Faction rough = NewFaction(RoughDef, "PermA");
            Faction player = NewFaction(PlayerDef, "PermB");
            rough.TryMakeInitialRelationsWith(player, new RandomStream(1));
            Assert.True(rough.WarWith(player)); // permanent enemies start at war (see TryMakeInitialRelationsWith)

            Assert.False(rough.MakePeace(player));
            Assert.True(rough.WarWith(player));
        }

        [Fact]
        public void Permanent_enemy_pairs_start_at_war_from_TryMakeInitialRelationsWith()
        {
            Faction rough = NewFaction(RoughDef, "InitWarA");
            Faction player = NewFaction(PlayerDef, "InitWarB");

            rough.TryMakeInitialRelationsWith(player, new RandomStream(1));

            Assert.True(rough.WarWith(player));
            Assert.True(player.WarWith(rough));
        }

        [Fact]
        public void SignTreaty_records_an_active_treaty_on_both_sides_and_applies_signing_goodwill()
        {
            Faction a = NewFaction(TribalDef, "SignA");
            Faction b = NewFaction(OutlanderDef, "SignB");

            bool result = a.SignTreaty(b, NonAggressionPact);

            Assert.True(result);
            Assert.True(a.HasNonAggressionPactWith(b));
            Assert.True(b.HasNonAggressionPactWith(a));
            Assert.Equal(NonAggressionPact.signingGoodwill, a.GoodwillWith(b));
        }

        [Fact]
        public void SignTreaty_a_nonAggression_pact_while_at_war_ends_the_war()
        {
            Faction a = NewFaction(TribalDef, "EndWarA");
            Faction b = NewFaction(OutlanderDef, "EndWarB");
            Assert.True(a.DeclareWar(b));
            Assert.True(a.WarWith(b));

            Assert.True(a.SignTreaty(b, NonAggressionPact));

            Assert.False(a.WarWith(b));
            Assert.False(b.WarWith(a));
        }

        [Fact]
        public void SignTreaty_refuses_for_a_permanent_enemy_pair()
        {
            Faction rough = NewFaction(RoughDef, "NoPactA");
            Faction player = NewFaction(PlayerDef, "NoPactB");

            Assert.False(rough.SignTreaty(player, NonAggressionPact));
            Assert.False(rough.HasNonAggressionPactWith(player));
        }

        [Fact]
        public void Treaty_is_active_immediately_and_inactive_once_its_duration_has_passed()
        {
            Find.TickManager.DebugSetTicksGame(0);
            var treaty = new Treaty(NonAggressionPact, Find.TickManager.TicksGame);

            Assert.True(treaty.IsActive(0));
            Assert.True(treaty.IsActive(NonAggressionPact.durationDays * GenDate.TicksPerDay - 1));
            Assert.False(treaty.IsActive(NonAggressionPact.durationDays * GenDate.TicksPerDay + 1));
        }

        [Fact]
        public void Treaty_with_zero_duration_never_expires()
        {
            Assert.Equal(0, Alliance.durationDays);
            var treaty = new Treaty(Alliance, 0);

            Assert.True(treaty.IsActive(0));
            Assert.True(treaty.IsActive(int.MaxValue - 1));
        }

        [Fact]
        public void DeclareWar_is_allowed_again_once_a_nonAggression_pact_has_expired()
        {
            Faction a = NewFaction(TribalDef, "ExpireA");
            Faction b = NewFaction(OutlanderDef, "ExpireB");
            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(a.SignTreaty(b, NonAggressionPact));
            Assert.False(a.DeclareWar(b));

            Find.TickManager.DebugSetTicksGame(NonAggressionPact.durationDays * GenDate.TicksPerDay + 1);

            Assert.True(a.DeclareWar(b));
        }

        [Fact]
        public void TradeAccessPriceGainWith_reads_the_best_active_tradeAccess_treaty_and_ignores_expired_ones()
        {
            Faction a = NewFaction(TribalDef, "GainA");
            Faction b = NewFaction(OutlanderDef, "GainB");

            Assert.Equal(0f, a.TradeAccessPriceGainWith(b));

            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(a.SignTreaty(b, TradeAgreement));
            Assert.Equal(TradeAgreement.tradeAccessPriceGain, a.TradeAccessPriceGainWith(b));

            Find.TickManager.DebugSetTicksGame(TradeAgreement.durationDays * GenDate.TicksPerDay + 1);
            Assert.Equal(0f, a.TradeAccessPriceGainWith(b));
        }

        [Fact]
        public void Scribe_round_trip_preserves_war_state_and_treaties()
        {
            Faction a = NewFaction(TribalDef, "RTA");
            Faction b = NewFaction(OutlanderDef, "RTB");
            Find.TickManager.DebugSetTicksGame(1000);
            Assert.True(a.SignTreaty(b, TradeAgreement));
            Assert.True(a.DeclareWar(b));

            var manager = new FactionManager();
            manager.Add(a);
            manager.Add(b);

            string xml = Scribe.SaveToString(manager, "factionManager");
            FactionManager loaded = Scribe.Load<FactionManager>(xml, "factionManager", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Faction loadedA = loaded.FirstFactionOfDef(TribalDef)!;
            Faction loadedB = loaded.FirstFactionOfDef(OutlanderDef)!;

            Assert.True(loadedA.WarWith(loadedB));
            Assert.True(loadedB.WarWith(loadedA));
            Assert.True(loadedA.HasActiveTreaty(loadedB, d => d.tradeAccess));
            Assert.Equal(TradeAgreement.tradeAccessPriceGain, loadedA.TradeAccessPriceGainWith(loadedB));
        }

        // ---- FactionDef.caravanTraderKinds (economy.traders) ----

        [Fact]
        public void CaravanTraderKinds_are_content_defined_for_the_trading_civilizations()
        {
            Assert.NotNull(TribalDef.caravanTraderKinds);
            Assert.NotEmpty(TribalDef.caravanTraderKinds!);
            Assert.NotNull(OutlanderDef.caravanTraderKinds);
            Assert.NotEmpty(OutlanderDef.caravanTraderKinds!);
            // A permanent enemy never trades — see RandomTraderKind's own null-when-none contract below.
            Assert.True(RoughDef.caravanTraderKinds == null || RoughDef.caravanTraderKinds.Count == 0);
        }

        [Fact]
        public void RandomTraderKind_is_null_when_a_faction_has_none()
        {
            Assert.Null(RoughDef.RandomTraderKind(new RandomStream(1)));
        }

        [Fact]
        public void RandomTraderKind_a_more_common_kind_is_picked_more_often()
        {
            var common = new global::SimWorld.Economy.TraderKindDef { defName = "CommonKind", commonality = 4f };
            var rare = new global::SimWorld.Economy.TraderKindDef { defName = "RareKind", commonality = 1f };
            var def = new FactionDef { defName = "WeightedTraders", caravanTraderKinds = new List<global::SimWorld.Economy.TraderKindDef> { common, rare } };
            var rand = new RandomStream(1234);

            int commonPicks = 0;
            const int Draws = 400;
            for (int i = 0; i < Draws; i++)
            {
                if (ReferenceEquals(def.RandomTraderKind(rand), common)) commonPicks++;
            }

            Assert.InRange(commonPicks, Draws * 0.6, Draws * 0.95);
        }

        // ---- FactionGenerator / world generation ----

        [Fact]
        public void FactionGenerator_counts_are_within_def_bounds_and_names_are_unique()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("faction-gen-seed", 3);

            foreach (FactionDef def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                int actual = world.factions.Count(f => f.def == def);
                Assert.True(actual >= def.requiredCountAtGameStart);
                Assert.True(actual <= def.maxCountAtGameStart);
            }

            var names = world.factions.Select(f => f.name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        [Fact]
        public void FactionGenerator_mustStartOneEnemy_guarantees_a_hostile_faction()
        {
            // AlmostNone population can legitimately roll zero RoughOutlanders (the only permanentEnemy
            // def), which is exactly the scenario PlayerCivilization.mustStartOneEnemy exists to cover.
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "must-start-one-enemy", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.AlmostNone, "Test", 3);

            Faction player = world.factions.Single(f => f.def.isPlayer);
            Assert.Contains(world.factions, f => f != player && f.HostileTo(player));
        }

        [Fact]
        public void WorldGenStep_Factions_attaches_every_settlement_to_a_generated_faction()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("attach-seed", 3);

            Assert.NotEmpty(world.worldObjects);
            foreach (WorldObject settlement in world.worldObjects)
            {
                Assert.NotNull(settlement.faction);
                Assert.Contains(settlement.faction, world.factions);
            }
        }

        // ---- WorldPathGrid / WorldPathFinder ----

        [Fact]
        public void WorldPathGrid_difficulty_scales_with_hilliness_and_biome()
        {
            var flat = new Tile { biome = BiomeDefOf.TemperateForest, elevation = 100f, hilliness = Hilliness.Flat };
            var hills = new Tile { biome = BiomeDefOf.TemperateForest, elevation = 100f, hilliness = Hilliness.LargeHills };
            var water = new Tile { biome = BiomeDefOf.Ocean, elevation = -10f, hilliness = Hilliness.Undefined };

            float flatCost = WorldPathGrid.CalculatedMovementDifficultyAt(flat);
            float hillsCost = WorldPathGrid.CalculatedMovementDifficultyAt(hills);

            Assert.True(hillsCost > flatCost);
            Assert.Equal(1f + BiomeDefOf.TemperateForest.movementDifficulty, flatCost);
            Assert.Equal(2f + BiomeDefOf.TemperateForest.movementDifficulty, hillsCost);
            Assert.True(WorldPathGrid.CalculatedMovementDifficultyAt(water) >= WorldPathGrid.ImpassableMovementDifficulty);
        }

        [Fact]
        public void WorldPathFinder_finds_a_path_across_a_generated_world_using_only_passable_tiles()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("pathfinder-seed", 3);
            WorldGrid grid = world.grid;

            List<int> component = LargestPassableComponent(grid);
            Assert.True(component.Count >= 2, "Expected at least one sizeable passable landmass for this seed.");

            int start = component[0];
            int end = component[component.Count - 1];

            bool found = WorldPathFinder.FindPath(grid, start, end, out List<int> path);
            Assert.True(found);
            Assert.Equal(start, path[0]);
            Assert.Equal(end, path[path.Count - 1]);
            foreach (int tileId in path)
            {
                Assert.True(WorldPathGrid.CalculatedMovementDifficultyAt(grid.Tiles[tileId]) < WorldPathGrid.ImpassableMovementDifficulty);
            }
        }

        [Fact]
        public void WorldPathFinder_reports_no_path_between_disconnected_tiles()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("pathfinder-disconnect-seed", 3);
            WorldGrid grid = world.grid;

            int? oceanTile = null;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (grid.Tiles[i].WaterCovered) { oceanTile = i; break; }
            }
            if (oceanTile == null) return; // this seed happened to be all-land; nothing to assert.

            List<int> component = LargestPassableComponent(grid);
            if (component.Count == 0) return;

            // Arriving AT an impassable tile must always fail: MovementCostBetween gates on the
            // destination's own difficulty, so no edge can ever end there, regardless of the start.
            bool found = WorldPathFinder.FindPath(grid, component[0], oceanTile.Value, out _);
            Assert.False(found);
        }

        // ---- Caravan ----

        [Fact]
        public void Caravan_moves_along_its_path_at_the_expected_ticks_per_tile_and_arrives()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("caravan-move-seed", 3);
            WorldGrid grid = world.grid;
            List<int> component = LargestPassableComponent(grid);
            Assert.True(component.Count >= 2);

            int start = component[0];
            int end = component[component.Count - 1];

            Pawn human = NewHuman("Traveler");
            Assert.Equal(1f, human.health.capacities.GetLevel(SimWorld.Health.PawnCapacityDefOf.Moving));

            Caravan caravan = CaravanMaker.MakeCaravan(new[] { human }, null, start, world);
            Assert.True(caravan.StartJourney(grid, end));
            Assert.NotNull(caravan.Path);

            List<int> path = caravan.Path!.ToList();
            float totalCost = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                totalCost += CaravanTicksPerMoveUtility.GetTicksPerMove(caravan.Pawns) * WorldPathGrid.MovementCostBetween(grid, path[i], path[i + 1]);
            }
            Assert.Equal(CaravanTicksPerMoveUtility.DefaultTicksPerMove, CaravanTicksPerMoveUtility.GetTicksPerMove(caravan.Pawns));

            int expectedTicks = (int)System.Math.Ceiling(totalCost);
            bool arrived = false;
            caravan.Arrived += _ => arrived = true;

            for (int i = 0; i < expectedTicks - 1; i++)
            {
                caravan.CaravanTick(grid);
            }
            // The path may cross several intermediate tiles before the final one, so only the very last
            // tick (below) is guaranteed to land exactly on the destination — not "still at start".
            Assert.False(arrived);
            Assert.True(caravan.Travelling);
            Assert.NotEqual(end, caravan.tile);

            caravan.CaravanTick(grid);
            Assert.True(arrived);
            Assert.False(caravan.Travelling);
            Assert.Equal(end, caravan.tile);
        }

        [Fact]
        public void Caravan_food_stock_drains_at_the_pawns_combined_hunger_rate()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("caravan-food-seed", 3);
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Caravan caravan = CaravanMaker.MakeCaravan(new[] { a, b }, null, 0, world);
            caravan.foodStock = 10f;

            float perTick = a.HungerRate * Need_Food.BaseFoodFallPerTick + b.HungerRate * Need_Food.BaseFoodFallPerTick;
            for (int i = 0; i < 100; i++)
            {
                caravan.CaravanTick(world.grid);
            }

            Assert.Equal(10f - perTick * 100, caravan.foodStock, 4);
        }

        [Fact]
        public void Caravan_food_stock_never_drops_below_zero()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("caravan-food-floor-seed", 3);
            Pawn a = NewHuman("A");
            Caravan caravan = CaravanMaker.MakeCaravan(new[] { a }, null, 0, world);
            caravan.foodStock = 0.00001f;

            caravan.CaravanTick(world.grid);
            caravan.CaravanTick(world.grid);

            Assert.Equal(0f, caravan.foodStock);
        }

        [Fact]
        public void World_WorldTick_advances_registered_caravans()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("world-tick-seed", 3);
            WorldGrid grid = world.grid;
            List<int> component = LargestPassableComponent(grid);
            Assert.True(component.Count >= 2);

            Pawn human = NewHuman("Traveler");
            Caravan caravan = CaravanMaker.MakeCaravan(new[] { human }, null, component[0], world);
            Assert.True(caravan.StartJourney(grid, component[component.Count - 1]));

            float before = caravan.foodStock = 5f;
            world.WorldTick();

            Assert.True(caravan.foodStock < before);
        }

        // ---- Scribe round trips ----

        [Fact]
        public void Scribe_round_trip_preserves_FactionManager_relations_by_reference()
        {
            var manager = new FactionManager();
            Faction player = NewFaction(PlayerDef, "Player");
            Faction tribal = NewFaction(TribalDef, "Tribal");
            manager.Add(player);
            manager.Add(tribal);
            tribal.SetRelationDirect(player, FactionRelationKind.Ally, 82);

            string xml = Scribe.SaveToString(manager, "factionManager");
            FactionManager loaded = Scribe.Load<FactionManager>(xml, "factionManager", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(2, loaded.AllFactionsListForReading.Count);
            Faction loadedPlayer = loaded.OfPlayer!;
            Faction loadedTribal = loaded.FirstFactionOfDef(TribalDef)!;

            Assert.Equal(82, loadedTribal.GoodwillWith(loadedPlayer));
            Assert.Equal(FactionRelationKind.Ally, loadedTribal.RelationKindWith(loadedPlayer));
            Assert.Same(loadedPlayer, loadedTribal.RelationWith(loadedPlayer)!.other);
            Assert.Same(loadedTribal, loadedPlayer.RelationWith(loadedTribal)!.other);
        }

        [Fact]
        public void Scribe_round_trip_preserves_a_caravans_travel_state()
        {
            global::SimWorld.World.World world = GenerateSmallWorld("caravan-scribe-seed", 3);
            WorldGrid grid = world.grid;
            List<int> component = LargestPassableComponent(grid);
            Assert.True(component.Count >= 2);

            Pawn human = NewHuman("Traveler");
            var original = new Caravan(WorldObjectDefOf.Caravan, component[0], null);
            original.AddPawn(human);
            original.foodStock = 12.5f;
            Assert.True(original.StartJourney(grid, component[component.Count - 1]));
            original.CaravanTick(grid); // advance partway so path/tile/cost state is non-trivial

            string xml = Scribe.SaveToString(original, "caravan");
            Caravan loaded = Scribe.Load<Caravan>(xml, "caravan", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(original.tile, loaded.tile);
            Assert.Equal(original.foodStock, loaded.foodStock);
            Assert.Equal(original.Destination, loaded.Destination);
            Assert.Equal(original.Path, loaded.Path);
            Assert.Single(loaded.Pawns);
        }
    }
}
