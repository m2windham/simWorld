using System;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

using CoreWorld = SimWorld.World.World;
using CoreSettlement = SimWorld.World.Settlement;

namespace SimWorld.Tests.Factions
{
    /// <summary>
    /// The world's own half of diplomacy (<see cref="DiplomacyAI"/>): a civilization declaring war or suing
    /// for peace with nobody at the god surface asking it to. <c>docs/design/player-first.md</c> §10: "if
    /// only the player can declare war, the world is inert and every other civilization is furniture."
    /// </summary>
    public class DiplomacyAITests : ContentTestBase
    {
        public DiplomacyAITests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            Find.World = new CoreWorld();
        }

        // ---- fixtures ----

        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");
        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        private static CoreSettlement TownFor(Faction faction, string name, int tile, int population)
        {
            var settlement = new CoreSettlement(SimWorld.World.WorldObjectDefOf.Settlement, tile, faction, name, 0);
            settlement.AddStatisticalPeople(population);
            return settlement;
        }

        /// <summary>Registers <paramref name="a"/> and <paramref name="b"/> with both <see cref="Find.FactionManager"/>
        /// and <see cref="Find.World"/>, gives them a settlement each (so <see cref="DiplomacyAI"/> has a strength
        /// reading), and sets their relation to Hostile at the goodwill floor.</summary>
        private static void SetUpHostilePair(Faction a, int popA, Faction b, int popB)
        {
            Find.FactionManager.Add(a);
            Find.FactionManager.Add(b);
            Find.World!.worldObjects.Add(TownFor(a, a.name, 10, popA));
            Find.World!.worldObjects.Add(TownFor(b, b.name, 20, popB));
            a.SetRelationDirect(b, FactionRelationKind.Hostile, -100);
        }

        /// <summary>Advances the clock one assessment interval and ticks <see cref="DiplomacyAI"/> directly —
        /// the same "advance the clock, then invoke the interval-gated method" idiom
        /// <c>EmergenceTests.RunYears</c> already uses, since single-stepping ticks would make these tests far
        /// too slow.</summary>
        private static void OneCheck()
        {
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + DiplomacyAI.AssessmentIntervalTicks);
            DiplomacyAI.Tick(Find.FactionManager);
        }

        /// <summary>
        /// Runs checks until <paramref name="condition"/> holds or <paramref name="maxChecks"/> is used up,
        /// stopping the instant it does. Deliberately does NOT keep running to <paramref name="maxChecks"/>
        /// and check only at the end: this lane's own peace-suing pass means a war that fires can later end in
        /// peace again on a long enough run, which is a correct interaction (see
        /// <see cref="The_weaker_side_of_a_long_war_eventually_sues_for_peace"/>) and not something "did this
        /// ever happen" should be blind to by only looking at the final tick.
        /// </summary>
        private static bool RunUntil(Func<bool> condition, int maxChecks)
        {
            if (condition()) return true;
            for (int i = 0; i < maxChecks; i++)
            {
                OneCheck();
                if (condition()) return true;
            }
            return false;
        }

        /// <summary>Runs <paramref name="checks"/> checks, asserting <paramref name="forbidden"/> is false
        /// after every single one — not only at the end, which (per <see cref="RunUntil"/>'s own doc) a
        /// war-then-peace cycle could otherwise mask.</summary>
        private static void RunAssertingNever(Func<bool> forbidden, int checks, string because)
        {
            Assert.False(forbidden(), because);
            for (int i = 0; i < checks; i++)
            {
                OneCheck();
                Assert.False(forbidden(), because);
            }
        }

        // ---- war: a hostile, dominant civilization turns on a weaker one ----

        [Fact]
        public void A_dominant_hostile_civilization_eventually_declares_war_on_its_own()
        {
            Faction strong = NewFaction(TribalDef, "Strong");
            Faction weak = NewFaction(OutlanderDef, "Weak");
            SetUpHostilePair(strong, popA: 300, weak, popB: 50);

            bool declared = RunUntil(() => strong.WarWith(weak), maxChecks: 20000);

            Assert.True(declared, "expected the stronger, hostile civilization to have declared war on its own within 20000 checks");
        }

        [Fact]
        public void The_player_never_initiates_a_war_on_its_own_behalf()
        {
            // The mirror case: the player's own civilization is dominant and hostile toward an NPC, but only
            // GodCommands.DeclareWar may act for the player — DiplomacyAI must never do it on their behalf.
            Faction player = NewFaction(PlayerDef, "PlayerActor");
            Faction rival = NewFaction(OutlanderDef, "RivalTarget");
            SetUpHostilePair(player, popA: 500, rival, popB: 10);

            RunAssertingNever(() => player.WarWith(rival), 20000,
                "the player's own civilization must never declare war through DiplomacyAI");
        }

        [Fact]
        public void A_civilization_never_attacks_one_it_is_not_hostile_toward()
        {
            Faction strong = NewFaction(TribalDef, "NeutStrong");
            Faction weak = NewFaction(OutlanderDef, "NeutWeak");
            Find.FactionManager.Add(strong);
            Find.FactionManager.Add(weak);
            Find.World!.worldObjects.Add(TownFor(strong, strong.name, 30, 300));
            Find.World!.worldObjects.Add(TownFor(weak, weak.name, 40, 50));
            strong.SetRelationDirect(weak, FactionRelationKind.Neutral, 0); // dominant in strength, but not Hostile.

            RunAssertingNever(() => strong.WarWith(weak), 20000,
                "goodwill never crossed into Hostile, so nothing should have triggered a declaration");
        }

        [Fact]
        public void A_hostile_civilization_never_attacks_one_strong_enough_to_match_it()
        {
            Faction a = NewFaction(TribalDef, "EvenA");
            Faction b = NewFaction(OutlanderDef, "EvenB");
            SetUpHostilePair(a, popA: 100, b, popB: 100); // evenly matched: neither clears MinAttackStrengthRatio.

            RunAssertingNever(() => a.WarWith(b) || b.WarWith(a), 20000,
                "neither side was ever strong enough relative to the other to meet MinAttackStrengthRatio");
        }

        [Fact]
        public void A_nonAggression_pact_prevents_the_AI_from_declaring_war_even_when_eligible()
        {
            Faction strong = NewFaction(TribalDef, "PactStrong");
            Faction weak = NewFaction(OutlanderDef, "PactWeak");
            SetUpHostilePair(strong, popA: 500, weak, popB: 10);
            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(strong.SignTreaty(weak, DefDatabase<TreatyDef>.GetNamed("NonAggressionPact")));

            // Bounded well within NonAggressionPact's own 60-day duration (60 * GenDate.TicksPerDay /
            // DiplomacyAI.AssessmentIntervalTicks = 1440 checks) — once it lapses, DeclareWar is allowed
            // again (see FactionTests.DeclareWar_is_allowed_again_once_a_nonAggression_pact_has_expired), so
            // running past that would not be testing this rule any more.
            RunAssertingNever(() => strong.WarWith(weak), 1000,
                "a non-aggression pact holds; DiplomacyAI must never declare war while it does");
        }

        // ---- determinism: same tick history, same decision ----

        [Fact]
        public void AI_war_declarations_are_reproducible_from_the_same_tick_history()
        {
            (bool declared, int declaredAtTick) RunOnce()
            {
                Find.Reset();
                Find.TickManager = new TickManager();
                Find.FactionManager = new FactionManager();
                Find.World = new CoreWorld();

                Faction strong = NewFaction(TribalDef, "DetStrong");
                Faction weak = NewFaction(OutlanderDef, "DetWeak");
                SetUpHostilePair(strong, popA: 300, weak, popB: 50);

                bool declared = RunUntil(() => strong.WarWith(weak), maxChecks: 20000);
                return (declared, Find.TickManager.TicksGame);
            }

            (bool declaredA, int tickA) = RunOnce();
            (bool declaredB, int tickB) = RunOnce();

            Assert.True(declaredA, "expected this scenario to produce a declaration within 20000 checks");
            Assert.Equal(declaredA, declaredB);
            Assert.Equal(tickA, tickB);
        }

        // ---- peace: the weaker side of a long war sues for it ----

        [Fact]
        public void The_weaker_side_of_a_long_war_eventually_sues_for_peace()
        {
            Faction strong = NewFaction(TribalDef, "WarStrong");
            Faction weak = NewFaction(OutlanderDef, "WarWeak");
            Find.FactionManager.Add(strong);
            Find.FactionManager.Add(weak);
            Find.World!.worldObjects.Add(TownFor(strong, strong.name, 1, 400));
            Find.World!.worldObjects.Add(TownFor(weak, weak.name, 2, 20));

            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(DiplomacyActions.DeclareWar(strong, weak, "test setup"));
            Assert.NotNull(DiplomacyActions.WarStartTick(strong, weak));

            bool peaceMade = RunUntil(() => !strong.WarWith(weak), maxChecks: 20000);

            Assert.True(peaceMade, "expected the weaker side to have sued for peace after a long war within 20000 checks");
        }

        [Fact]
        public void A_war_below_the_minimum_duration_never_ends_in_an_AI_sued_peace()
        {
            Faction strong = NewFaction(TribalDef, "ShortStrong");
            Faction weak = NewFaction(OutlanderDef, "ShortWeak");
            Find.FactionManager.Add(strong);
            Find.FactionManager.Add(weak);
            Find.World!.worldObjects.Add(TownFor(strong, strong.name, 1, 400));
            Find.World!.worldObjects.Add(TownFor(weak, weak.name, 2, 20));

            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(DiplomacyActions.DeclareWar(strong, weak, "test setup"));

            // Fewer checks than DiplomacyAI.MinWarDurationForPeaceTicks can possibly cover.
            int checksUnderThreshold = DiplomacyAI.MinWarDurationForPeaceTicks / DiplomacyAI.AssessmentIntervalTicks / 2;
            RunAssertingNever(() => !strong.WarWith(weak), checksUnderThreshold,
                "the war ended before it could plausibly count as \"long\"");
        }

        [Fact]
        public void A_permanent_enemy_pairs_war_is_never_assessed_for_peace()
        {
            Faction rough = NewFaction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "PermRough");
            Faction player = NewFaction(PlayerDef, "PermPlayer");
            Find.FactionManager.Add(rough);
            Find.FactionManager.Add(player);
            Find.World!.worldObjects.Add(TownFor(rough, rough.name, 1, 20));
            Find.World!.worldObjects.Add(TownFor(player, player.name, 2, 400));

            // Forced straight to War outside DiplomacyActions, exactly as world generation does — see
            // FactionRelation.warStartTick's own doc on why this leaves it null.
            rough.TryMakeInitialRelationsWith(player, new RandomStream(1));
            Assert.True(rough.WarWith(player));
            Assert.Null(DiplomacyActions.WarStartTick(rough, player));

            RunAssertingNever(() => !rough.WarWith(player), 20000,
                "a permanent-enemy war must never end, in peace or otherwise");
        }
    }
}
