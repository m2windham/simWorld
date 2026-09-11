using System.Collections.Generic;
using System.Linq;

using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Attention as the thing that actually drives citizen tiering (<c>docs/spec/simworld-spec.md</c>
    /// §11.2/§11.3). <see cref="AttentionManager"/> defines "attention" as the god's focus — zero or one
    /// settlement the player has open — and is the only caller of
    /// <c>Pawn_TierTracker.Notify_AttentionChanged</c> and <c>DemoteToStatistical</c> in the codebase.
    /// <para/>
    /// The load-bearing test here is
    /// <see cref="A_multi_settlement_game_holds_far_fewer_Full_tier_citizens_than_it_did_before"/>: before
    /// this lane every founded citizen was <see cref="PawnTier.Full"/> forever, so the tier system that exists
    /// to bound the Full population bounded nothing.
    /// </summary>
    [Collection("GlobalDefs")]
    public class AttentionTests : ContentTestBase
    {
        public AttentionTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        /// <summary>A real game, already focused on the settlement it founded (<c>Game.NewGame</c>).</summary>
        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        /// <summary>Founds <paramref name="count"/> further settlements, each with a live band of real
        /// <c>Pawn</c>s — the shape <c>SettlementFounder.Found</c> produces for an emergent rival
        /// civilization, and the only shape that can contribute to a Full-tier population at all (a
        /// <c>FoundColony</c> cohort has no <c>Pawn</c> per person by design).</summary>
        private static List<Settlement> FoundLiveBands(Game game, int count, int bandSize, int seed)
        {
            CoreWorld world = game.World!;
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            var founded = new List<Settlement>(count);

            for (int i = 0; i < world.grid.TilesCount && founded.Count < count; i++)
            {
                if (taken.Contains(i) || world.grid.Tiles[i].WaterCovered) continue;
                taken.Add(i);
                founded.Add(SettlementFounder.Found(
                    world, i, world.factions.First(), bandSize, new RandomStream(seed + founded.Count)));
            }

            Assert.Equal(count, founded.Count);
            return founded;
        }

        private static int FullCitizensInWorld(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().Sum(s => s.PopulationOf(PawnTier.Full));

        private static int LiveCitizensInWorld(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().Sum(s => s.Citizens.Count);

        // ---- the test that matters ----

        /// <summary>
        /// The whole point of the lane. A civilization of four settlements founds every citizen
        /// <see cref="PawnTier.Full"/> — that is what <c>Pawn_TierTracker</c> starts at, and before attention
        /// was wired nothing ever moved them off it. One pass of the real game loop now leaves exactly the
        /// focused settlement at Full and everyone else at Interval, and moving the focus moves the Full
        /// population with it rather than growing it.
        /// </summary>
        [Fact]
        public void A_multi_settlement_game_holds_far_fewer_Full_tier_citizens_than_it_did_before()
        {
            Game game = NewSoloGame("attention-bounds-full");
            Settlement home = PlayerSettlement(game);
            List<Settlement> others = FoundLiveBands(game, 2, bandSize: 20, seed: 700);

            // "Before": founding alone leaves every citizen in the world at Full, in four settlements.
            int everyone = LiveCitizensInWorld(game);
            Assert.Equal(everyone, FullCitizensInWorld(game));
            Assert.True(everyone > home.Citizens.Count, "expected the rival bands to add Full-tier citizens");

            // "After": one tick of the real loop — Game.WireTickHooks -> GodManager.GodTick ->
            // AttentionManager.Reconcile. No test-only call anywhere in this path.
            RunTicks(1);

            Assert.Equal(home.Citizens.Count, FullCitizensInWorld(game));
            Assert.True(FullCitizensInWorld(game) < everyone);
            foreach (Settlement other in others)
            {
                Assert.All(other.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));
            }

            // And the bound travels with the focus rather than accumulating: attending a second settlement
            // does not leave the first one attended too.
            Assert.True(GodCommands.FocusSettlement(others[0].tile).Changed);
            Assert.Equal(others[0].Citizens.Count, FullCitizensInWorld(game));
            Assert.All(home.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));
        }

        // ---- focus moves ----

        [Fact]
        public void Moving_the_focus_changes_the_tier_of_both_settlements_citizens()
        {
            Game game = NewSoloGame("attention-focus-moves");
            Settlement home = PlayerSettlement(game);
            Settlement away = FoundLiveBands(game, 1, bandSize: 20, seed: 710)[0];
            RunTicks(1);

            Assert.All(home.Citizens, c => Assert.True(c.tier.Attending));
            Assert.All(away.Citizens, c => Assert.False(c.tier.Attending));

            GodCommandResult moved = GodCommands.FocusSettlement(away.tile);

            Assert.Equal(GodCommandOutcome.Done, moved.Outcome);
            Assert.Contains(away.name, moved.Reason);
            Assert.All(away.Citizens, c => Assert.True(c.tier.Attending));
            Assert.All(away.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));
            Assert.All(home.Citizens, c => Assert.False(c.tier.Attending));
            Assert.All(home.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));
        }

        [Fact]
        public void With_no_settlement_in_focus_nobody_anywhere_is_attending()
        {
            Game game = NewSoloGame("attention-no-focus");
            Settlement home = PlayerSettlement(game);
            Settlement away = FoundLiveBands(game, 1, bandSize: 20, seed: 720)[0];
            RunTicks(1);

            GodCommandResult cleared = GodCommands.ClearSettlementFocus();

            Assert.Equal(GodCommandOutcome.Done, cleared.Outcome);
            Assert.False(Find.God.Attention.HasFocus);
            Assert.Equal(AttentionManager.NoFocus, Find.God.Attention.FocusedTile);
            Assert.Equal(0, FullCitizensInWorld(game));
            foreach (Settlement settlement in new[] { home, away })
            {
                Assert.All(settlement.Citizens, c => Assert.False(c.tier.Attending));
                Assert.All(settlement.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));
            }

            // Idempotent in both directions: re-clearing changes nothing and says so.
            Assert.Equal(GodCommandOutcome.NoChange, GodCommands.ClearSettlementFocus().Outcome);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.FocusSettlement(home.tile).Outcome);
            Assert.Equal(GodCommandOutcome.NoChange, GodCommands.FocusSettlement(home.tile).Outcome);
            Assert.Equal(home.Citizens.Count, FullCitizensInWorld(game));
        }

        /// <summary>Attention is one of four promotion reasons, not the only one (§11.3). A citizen the
        /// civilization has a reason to care about stays Full when the player looks away — which is also what
        /// stops the focus from being a blunt "everyone but one settlement goes quiet".</summary>
        [Fact]
        public void A_citizen_significant_for_another_reason_does_not_demote_when_the_focus_leaves()
        {
            Game game = NewSoloGame("attention-other-significance");
            Settlement home = PlayerSettlement(game);
            RunTicks(1);

            Pawn leader = home.Citizens[0];
            Pawn remembered = home.Citizens[1];
            Pawn kin = home.Citizens[2];
            Pawn nobody = home.Citizens[3];

            leader.tier.Notify_RoleChanged(true);
            remembered.tier.Notify_ChronicleNamed();
            kin.tier.Notify_RelatedToPromoted(true);

            Assert.True(GodCommands.ClearSettlementFocus().Changed);
            Find.God.Attention.Reconcile(); // the periodic sweep must not undo any of the three either

            Assert.Equal(PawnTier.Full, leader.tier.Tier);
            Assert.Equal(PawnTier.Full, remembered.tier.Tier);
            Assert.Equal(PawnTier.Full, kin.tier.Tier);
            Assert.Equal(PawnTier.Interval, nobody.tier.Tier);

            // None of the three is attending — they are Full for their own reason, which is the distinction
            // that keeps "who is being watched" and "who matters" from collapsing into one flag.
            foreach (Pawn p in new[] { leader, remembered, kin })
            {
                Assert.False(p.tier.Attending);
                Assert.True(p.tier.Significant);
            }
        }

        // ---- the settle-to-Statistical policy ----

        [Fact]
        public void A_citizen_settles_to_Statistical_only_once_the_threshold_has_passed()
        {
            Game game = NewSoloGame("attention-settle-threshold");
            Settlement home = PlayerSettlement(game);
            Settlement away = FoundLiveBands(game, 1, bandSize: 20, seed: 730)[0];
            RunTicks(1);

            Assert.All(away.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));
            int becameInsignificant = away.Citizens[0].tier.InsignificantSinceTick;
            Assert.NotEqual(Pawn_TierTracker.NeverInsignificant, becameInsignificant);

            // One tick short of the threshold: still Interval. The assertion is the boundary either side of
            // TieringTuning.IntervalSettleTicks, never the literal value of it.
            Find.TickManager.DebugSetTicksGame(becameInsignificant + TieringTuning.IntervalSettleTicks - 1);
            Find.God.Attention.Reconcile();
            Assert.All(away.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));

            Find.TickManager.DebugSetTicksGame(becameInsignificant + TieringTuning.IntervalSettleTicks);
            Find.God.Attention.Reconcile();
            Assert.All(away.Citizens, c => Assert.Equal(PawnTier.Statistical, c.tier.Tier));

            // The attended settlement is untouched however long the others have been waiting.
            Assert.All(home.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));

            // Settling is not a one-way door: significance still promotes straight back to Full, skipping
            // Interval entirely, exactly as §11.3's flowchart has it.
            Assert.True(GodCommands.FocusSettlement(away.tile).Changed);
            Assert.All(away.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));
        }

        [Fact]
        public void A_citizen_significant_for_another_reason_never_settles_however_long_attention_is_elsewhere()
        {
            Game game = NewSoloGame("attention-settle-spares-the-significant");
            Settlement away = FoundLiveBands(game, 1, bandSize: 20, seed: 740)[0];
            RunTicks(1);

            Pawn leader = away.Citizens[0];
            Pawn nobody = away.Citizens[1];
            leader.tier.Notify_RoleChanged(true);

            // Ten times the settle threshold, with the sweep running throughout.
            int start = Find.TickManager.TicksGame;
            for (int i = 1; i <= 10; i++)
            {
                Find.TickManager.DebugSetTicksGame(start + i * TieringTuning.IntervalSettleTicks);
                Find.God.Attention.Reconcile();
            }

            Assert.Equal(PawnTier.Full, leader.tier.Tier);
            Assert.Equal(Pawn_TierTracker.NeverInsignificant, leader.tier.InsignificantSinceTick);
            Assert.Equal(PawnTier.Statistical, nobody.tier.Tier);

            // Losing the role starts the clock from that moment, not from when attention left: the citizen
            // has to serve the whole span again before settling.
            int lostRoleAt = Find.TickManager.TicksGame;
            leader.tier.Notify_RoleChanged(false);
            Assert.Equal(PawnTier.Interval, leader.tier.Tier);
            Assert.Equal(lostRoleAt, leader.tier.InsignificantSinceTick);

            Find.TickManager.DebugSetTicksGame(lostRoleAt + TieringTuning.IntervalSettleTicks - 1);
            Find.God.Attention.Reconcile();
            Assert.Equal(PawnTier.Interval, leader.tier.Tier);

            Find.TickManager.DebugSetTicksGame(lostRoleAt + TieringTuning.IntervalSettleTicks);
            Find.God.Attention.Reconcile();
            Assert.Equal(PawnTier.Statistical, leader.tier.Tier);
        }

        // ---- the sweep catches what no focus-change event ever fired for ----

        [Fact]
        public void A_settlement_founded_after_the_focus_was_set_is_reconciled_by_the_sweep()
        {
            Game game = NewSoloGame("attention-sweep-catches-new-settlements");
            RunTicks(1);

            // Nothing fires a focus change when a civilization emerges; its band arrives Full and
            // insignificant, exactly as SettlementFounder.Found builds it.
            Settlement latecomer = FoundLiveBands(game, 1, bandSize: 20, seed: 750)[0];
            Assert.All(latecomer.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));

            Find.God.Attention.Reconcile();

            Assert.All(latecomer.Citizens, c => Assert.Equal(PawnTier.Interval, c.tier.Tier));
        }

        [Fact]
        public void A_focused_settlement_that_ceases_to_exist_falls_back_to_civilization_scope()
        {
            Game game = NewSoloGame("attention-destroyed-focus");
            Settlement home = PlayerSettlement(game);
            RunTicks(1);
            Assert.True(Find.God.Attention.HasFocus);

            game.World!.worldObjects.Remove(home);
            Find.God.Attention.Reconcile();

            Assert.False(Find.God.Attention.HasFocus);
            Assert.Null(Find.God.Attention.FocusedSettlement);
            Assert.Null(GodViewSnapshot.Capture().FocusedSettlementTile);

            // And re-focusing a tile nothing sits on is a stale handle, not a refusal.
            GodCommandResult stale = GodCommands.FocusSettlement(home.tile);
            Assert.Equal(GodCommandOutcome.UnknownSettlement, stale.Outcome);
            Assert.False(stale.Changed);
            Assert.False(string.IsNullOrWhiteSpace(stale.Reason));
        }

        // ---- the read model can read back what the command surface set ----

        [Fact]
        public void The_snapshot_reports_whatever_focus_the_commands_last_set()
        {
            Game game = NewSoloGame("attention-snapshot-readback");
            Settlement home = PlayerSettlement(game);
            Settlement away = FoundLiveBands(game, 1, bandSize: 20, seed: 760)[0];
            RunTicks(1);

            GodViewSnapshot atStart = GodViewSnapshot.Capture();
            Assert.Equal(home.tile, atStart.FocusedSettlementTile);
            Assert.Contains(atStart.Settlements, s => s.Tile == atStart.FocusedSettlementTile);

            GodCommands.FocusSettlement(away.tile);
            Assert.Equal(away.tile, GodViewSnapshot.Capture().FocusedSettlementTile);

            GodCommands.ClearSettlementFocus();
            Assert.Null(GodViewSnapshot.Capture().FocusedSettlementTile);

            // The tier counts the view draws move with the focus — this is the number §11.3 says a player is
            // entitled to see, because it is the cost of their own attention.
            GodCommands.FocusSettlement(away.tile);
            GodViewSnapshot focused = GodViewSnapshot.Capture();
            Assert.Equal(away.Citizens.Count, focused.Civilization.FullCount);
            Assert.Equal(home.Citizens.Count, focused.Civilization.IntervalCount);
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trips_the_focus_and_the_tiers_it_holds()
        {
            Game game = NewSoloGame("attention-scribe");
            Settlement home = PlayerSettlement(game);
            Settlement away = FoundLiveBands(game, 1, bandSize: 20, seed: 770)[0];
            RunTicks(1);

            GodCommands.FocusSettlement(away.tile);
            home.Citizens[0].tier.Notify_RoleChanged(true); // a citizen Full for a reason other than focus
            int awayTile = away.tile;
            int homeTile = home.tile;

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.Equal(awayTile, loaded.God.Attention.FocusedTile);
            Assert.True(loaded.God.Attention.HasFocus);

            Settlement loadedAway = loaded.World!.worldObjects.OfType<Settlement>().Single(s => s.tile == awayTile);
            Settlement loadedHome = loaded.World!.worldObjects.OfType<Settlement>().Single(s => s.tile == homeTile);
            Assert.Same(loadedAway, loaded.God.Attention.FocusedSettlement);

            // Tiers come back as they were, without a re-application pass: each citizen's own attention flag
            // and tier are Scribed by Pawn_TierTracker, so the focus and the population it holds at Full are
            // already in agreement the moment the load finishes.
            Assert.All(loadedAway.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));
            Assert.All(loadedAway.Citizens, c => Assert.True(c.tier.Attending));
            Assert.Equal(1, loadedHome.Citizens.Count(c => c.tier.Tier == PawnTier.Full));
            Assert.All(loadedHome.Citizens, c => Assert.False(c.tier.Attending));

            // The insignificance clock survives too, so a load does not hand every unattended citizen a fresh
            // full term before they can settle.
            Pawn insignificant = loadedHome.Citizens.First(c => !c.tier.Significant);
            Assert.NotEqual(Pawn_TierTracker.NeverInsignificant, insignificant.tier.InsignificantSinceTick);
            Find.TickManager.DebugSetTicksGame(insignificant.tier.InsignificantSinceTick + TieringTuning.IntervalSettleTicks);
            loaded.God.Attention.Reconcile();
            Assert.Equal(PawnTier.Statistical, insignificant.tier.Tier);

            // And the loaded game keeps driving attention on its own tick loop, not only when poked.
            Assert.True(GodCommands.FocusSettlement(homeTile).Changed);
            Assert.All(loadedHome.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));
        }

        // ---- safety ----

        [Fact]
        public void Attention_is_harmless_with_no_world_and_no_focus_at_all()
        {
            var attention = new AttentionManager();

            Assert.False(attention.HasFocus);
            Assert.Null(attention.FocusedSettlement);
            Assert.False(attention.ClearFocus());
            attention.Reconcile(); // Find.World is null in a bare test: a no-op, never a throw.
            Assert.False(attention.HasFocus);

            Assert.Equal(GodCommandOutcome.UnknownSettlement, GodCommands.FocusSettlement(0).Outcome);
            Assert.Equal(GodCommandOutcome.NoChange, GodCommands.ClearSettlementFocus().Outcome);
        }

        /// <summary>A pawn nobody has ever attended or dismissed has no insignificance clock running at all,
        /// so the settle policy can never reach them. That is what keeps <c>Pawn_TierTracker</c> inert without
        /// a director: the tracker itself still decides nothing about elapsed time.</summary>
        [Fact]
        public void A_pawn_no_director_has_considered_has_no_clock_and_never_settles()
        {
            Pawn p = NewHuman();

            Assert.Equal(PawnTier.Full, p.tier.Tier);
            Assert.Equal(Pawn_TierTracker.NeverInsignificant, p.tier.InsignificantSinceTick);
            Assert.False(p.tier.HasBeenInsignificantFor(0));

            Find.TickManager.DebugSetTicksGame(TieringTuning.IntervalSettleTicks * 5);
            Assert.False(p.tier.HasBeenInsignificantFor(TieringTuning.IntervalSettleTicks));

            // One word from a director starts it, from that moment.
            int told = Find.TickManager.TicksGame;
            p.tier.Notify_AttentionChanged(false);
            Assert.Equal(told, p.tier.InsignificantSinceTick);
            Assert.True(p.tier.HasBeenInsignificantFor(0));
            Assert.False(p.tier.HasBeenInsignificantFor(1));
        }
    }
}
