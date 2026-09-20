using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Factions
{
    /// <summary>
    /// The player's own half of diplomacy: <c>GodCommands.Diplomacy.cs</c> (declare war, make peace, sign a
    /// treaty) and <c>GodViewSnapshot.Diplomacy.cs</c> (who exists, how they feel about us, what state each
    /// relation is in). <c>docs/design/player-first.md</c> §10: before this, <see cref="Faction.DeclareWar"/>
    /// and <see cref="Faction.SignTreaty"/> had zero callers anywhere in <c>src/</c>.
    /// </summary>
    public class DiplomacyCommandsTests : ContentTestBase
    {
        public DiplomacyCommandsTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");
        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");
        private static FactionDef RoughDef => DefDatabase<FactionDef>.GetNamed("RoughOutlanders");

        private static TreatyDef NonAggressionPact => DefDatabase<TreatyDef>.GetNamed("NonAggressionPact");

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        /// <summary>Registers the player and one rival, related Neutral, and returns both.</summary>
        private (Faction player, Faction rival) NewRelatedPair(FactionDef rivalDef, string suffix)
        {
            Faction player = NewFaction(PlayerDef, "Player" + suffix);
            Faction rival = NewFaction(rivalDef, "Rival" + suffix);
            player.SetRelationDirect(rival, FactionRelationKind.Neutral, 0);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rival);
            return (player, rival);
        }

        // ---- DeclareWar: the happy path and its refusals ----

        [Fact]
        public void DeclareWar_through_the_command_surface_actually_declares_it()
        {
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "DW");

            GodCommandResult result = GodCommands.DeclareWar(rival.loadID);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.True(player.WarWith(rival));
        }

        [Fact]
        public void DeclareWar_refuses_an_unknown_civilization()
        {
            NewRelatedPair(TribalDef, "DWUnknown");

            GodCommandResult result = GodCommands.DeclareWar("NoSuchCivilization");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
        }

        [Fact]
        public void DeclareWar_on_a_civilization_already_at_war_is_a_no_op()
        {
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "DWAgain");
            Assert.Equal(GodCommandOutcome.Done, GodCommands.DeclareWar(rival.loadID).Outcome);

            GodCommandResult result = GodCommands.DeclareWar(rival.loadID);

            Assert.Equal(GodCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
            Assert.True(player.WarWith(rival)); // still at war — the no-op did not undo anything.
        }

        [Fact]
        public void DeclareWar_refuses_while_an_active_nonAggression_pact_holds()
        {
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "DWPact");
            Find.TickManager.DebugSetTicksGame(0);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SignTreaty(rival.loadID, NonAggressionPact.defName).Outcome);

            GodCommandResult result = GodCommands.DeclareWar(rival.loadID);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(player.WarWith(rival));
        }

        // ---- MakePeace: the happy path and its refusals ----

        [Fact]
        public void MakePeace_through_the_command_surface_actually_ends_the_war()
        {
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "MP");
            Assert.Equal(GodCommandOutcome.Done, GodCommands.DeclareWar(rival.loadID).Outcome);

            GodCommandResult result = GodCommands.MakePeace(rival.loadID);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.False(player.WarWith(rival));
        }

        [Fact]
        public void MakePeace_refuses_an_unknown_civilization()
        {
            NewRelatedPair(TribalDef, "MPUnknown");

            Assert.Equal(GodCommandOutcome.Refused, GodCommands.MakePeace("NoSuchCivilization").Outcome);
        }

        [Fact]
        public void MakePeace_when_not_at_war_is_a_no_op()
        {
            (_, Faction rival) = NewRelatedPair(TribalDef, "MPNoWar");

            GodCommandResult result = GodCommands.MakePeace(rival.loadID);

            Assert.Equal(GodCommandOutcome.NoChange, result.Outcome);
        }

        [Fact]
        public void MakePeace_refuses_a_permanent_enemy()
        {
            Faction player = NewFaction(PlayerDef, "MPPermPlayer");
            Faction rough = NewFaction(RoughDef, "MPPermRough");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rough);
            rough.TryMakeInitialRelationsWith(player, new RandomStream(1));
            Assert.True(player.WarWith(rough)); // permanent enemies start at war.

            GodCommandResult result = GodCommands.MakePeace(rough.loadID);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.True(player.WarWith(rough));
        }

        // ---- SignTreaty: the happy path and its refusals ----

        [Fact]
        public void SignTreaty_through_the_command_surface_actually_signs_it()
        {
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "ST");
            Find.TickManager.DebugSetTicksGame(0);

            GodCommandResult result = GodCommands.SignTreaty(rival.loadID, NonAggressionPact.defName);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(player.HasNonAggressionPactWith(rival));
        }

        [Fact]
        public void SignTreaty_refuses_an_unknown_civilization()
        {
            NewRelatedPair(TribalDef, "STUnknown");

            Assert.Equal(GodCommandOutcome.Refused, GodCommands.SignTreaty("NoSuchCivilization", NonAggressionPact.defName).Outcome);
        }

        [Fact]
        public void SignTreaty_refuses_an_unknown_treaty()
        {
            (_, Faction rival) = NewRelatedPair(TribalDef, "STUnknownTreaty");

            Assert.Equal(GodCommandOutcome.Refused, GodCommands.SignTreaty(rival.loadID, "NoSuchTreaty").Outcome);
        }

        [Fact]
        public void SignTreaty_refuses_a_permanent_enemy()
        {
            Faction player = NewFaction(PlayerDef, "STPermPlayer");
            Faction rough = NewFaction(RoughDef, "STPermRough");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rough);

            GodCommandResult result = GodCommands.SignTreaty(rough.loadID, NonAggressionPact.defName);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(player.HasNonAggressionPactWith(rough));
        }

        // ---- a bad decision is allowed to be bad: never refuse a suicidal war ----

        [Fact]
        public void Declaring_war_on_a_civilization_ten_times_stronger_is_never_refused()
        {
            // docs/design/player-first.md's own phrase: "declaring on someone ten times your strength is the
            // player's call and exactly the sort of decision this game exists to let them make and regret."
            // GodCommands.DeclareWar reads no strength at all (only DiplomacyAI's own assessment of an NPC's
            // war does), so a world/settlement fixture is not even needed to prove it — the command has
            // nothing to refuse it with regardless of how outmatched the player's civilization actually is.
            Faction player = NewFaction(PlayerDef, "Suicide1Player");
            Faction giant = NewFaction(TribalDef, "Suicide1Giant");
            player.SetRelationDirect(giant, FactionRelationKind.Neutral, 0);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(giant);

            GodCommandResult result = GodCommands.DeclareWar(giant.loadID);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(player.WarWith(giant));
        }

        [Fact]
        public void Declaring_a_second_war_while_already_fighting_one_is_never_refused()
        {
            // A second, unwise front: still purely the player's call.
            Faction player = NewFaction(PlayerDef, "Suicide2Player");
            Faction firstEnemy = NewFaction(TribalDef, "Suicide2First");
            Faction secondEnemy = NewFaction(OutlanderDef, "Suicide2Second");
            player.SetRelationDirect(firstEnemy, FactionRelationKind.Neutral, 0);
            player.SetRelationDirect(secondEnemy, FactionRelationKind.Neutral, 0);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(firstEnemy);
            Find.FactionManager.Add(secondEnemy);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.DeclareWar(firstEnemy.loadID).Outcome);

            GodCommandResult result = GodCommands.DeclareWar(secondEnemy.loadID);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(player.WarWith(firstEnemy));
            Assert.True(player.WarWith(secondEnemy));
        }

        // ---- the read side: who exists, how they feel about us, what state the relation is in ----

        [Fact]
        public void The_snapshot_reports_every_other_civilizations_goodwill_standing_war_and_treaties()
        {
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "View");
            player.TryAffectGoodwillWith(rival, -90); // 0 -> -90, crosses into Hostile.
            Find.TickManager.DebugSetTicksGame(0);
            // TradeAgreement's own signingGoodwill (+10, see its content) still leaves this at -80 — comfortably
            // past the Hostile threshold, so the treaty itself does not accidentally undo the setup above.
            Assert.True(player.SignTreaty(rival, DefDatabase<TreatyDef>.GetNamed("TradeAgreement")));

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            FactionRelationView view = snapshot.Civilizations.Single(c => c.CivilizationId == rival.loadID);

            Assert.Equal(rival.name, view.Name);
            Assert.Equal(rival.def.defName, view.CivilizationDefName);
            Assert.Equal(player.GoodwillWith(rival), view.Goodwill);
            Assert.Equal(DiplomaticStanding.Hostile, view.Standing);
            Assert.False(view.AtWar); // Hostile goodwill without a declared war is not the same as War — see WarState's own doc.
            Assert.Contains(view.Treaties, t => t.DefName == "TradeAgreement" && t.Active);
        }

        [Fact]
        public void The_snapshot_never_lists_the_players_own_civilization_among_its_relations()
        {
            (Faction player, _) = NewRelatedPair(TribalDef, "SelfExclude");

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.DoesNotContain(snapshot.Civilizations, c => c.CivilizationId == player.loadID);
        }

        [Fact]
        public void The_snapshot_is_empty_before_a_player_civilization_exists()
        {
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Empty(snapshot.Civilizations);
        }

        // ---- the real tick loop: declaring war interacts with machinery this lane did not write ----

        [Fact]
        public void Declaring_war_drops_goodwill_to_the_floor_and_the_real_tick_loop_then_carries_it_off_that_floor()
        {
            // TribalCivilization (a real content def, and NOT RoughOutlanders — a permanent enemy's goodwill
            // never moves at all, see Faction.TryAffectGoodwillWith) has a nonzero goodwillDailyGain and a
            // naturalColonyGoodwill target above -100 — Faction.FactionTick's own natural-goodwill-drift
            // machinery, wired into FactionManager.FactionManagerTick exactly the way Game.cs's own
            // PostTickers call it, and never touched by this lane.
            (Faction player, Faction rival) = NewRelatedPair(TribalDef, "TickLoop");

            Assert.Equal(GodCommandOutcome.Done, GodCommands.DeclareWar(rival.loadID).Outcome);
            Assert.Equal(-100, player.GoodwillWith(rival)); // the command's own, immediate effect — not what this test is about.

            // Drive the REAL tick loop: FactionManager.FactionManagerTick, not a direct call to whatever moves
            // goodwill. 200 checks is comfortably enough for TribalCivilization's own drift rate to move the
            // relation at least one point off the declaration floor.
            for (int i = 0; i < 200; i++)
            {
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + Faction.GoodwillCheckInterval);
                Find.FactionManager.FactionManagerTick();
            }

            Assert.True(player.GoodwillWith(rival) > -100,
                "expected the real tick loop's own natural-goodwill-drift machinery to have moved the relation off the war-declaration floor");
            Assert.True(player.WarWith(rival), "the war itself must still hold — drift must not end it on its own");
        }

        // ---- Scribe round trip: warStartTick, the one field this lane added ----

        [Fact]
        public void Scribe_round_trip_preserves_warStartTick_and_the_war_it_belongs_to()
        {
            (Faction a, Faction b) = NewRelatedPair(TribalDef, "RT");
            Find.TickManager.DebugSetTicksGame(12345);
            Assert.True(DiplomacyActions.DeclareWar(a, b, "round trip"));
            int? startTickBefore = DiplomacyActions.WarStartTick(a, b);
            Assert.Equal(12345, startTickBefore);

            var manager = new FactionManager();
            manager.Add(a);
            manager.Add(b);

            string xml = Scribe.SaveToString(manager, "factionManager");
            FactionManager loaded = Scribe.Load<FactionManager>(xml, "factionManager", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Faction loadedA = loaded.FirstFactionOfDef(PlayerDef)!;
            Faction loadedB = loaded.FirstFactionOfDef(TribalDef)!;

            Assert.True(loadedA.WarWith(loadedB));
            Assert.Equal(startTickBefore, DiplomacyActions.WarStartTick(loadedA, loadedB));

            // And the round trip clears it the same way the live objects do: making peace on the loaded pair
            // clears warStartTick exactly as DiplomacyActions.MakePeace does on a freshly-created one.
            Assert.True(DiplomacyActions.MakePeace(loadedA, loadedB, "post-load peace"));
            Assert.Null(DiplomacyActions.WarStartTick(loadedA, loadedB));
        }
    }
}
