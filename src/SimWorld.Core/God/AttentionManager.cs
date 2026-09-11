using System;
using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.God
{
    /// <summary>
    /// What the god is looking at, and the one thing that drives <see cref="Pawn_TierTracker"/>'s attention
    /// flag (<c>docs/spec/simworld-spec.md</c> §11.2/§11.3). RimWorld has no equivalent — its player is always
    /// looking at the one colony that exists — so this is SimWorld's own, recorded under the <c>god</c> system.
    ///
    /// <para/><b>"Attention" is defined here as the god's focus: zero or one settlement the player currently
    /// has open.</b> §11.3 leaves the word undefined beyond "the settlement under the player's attention",
    /// and this is the reading that phrase actually supports: a scope, not a camera frustum, not a selection,
    /// not a distance. The alternatives were considered and rejected — a per-citizen selection would make
    /// attention an arbitrarily large set the player could grow without limit, and anything derived from
    /// rendering (what is on screen, how far the camera is zoomed) would put simulation policy in the host,
    /// which is exactly what §12a's seam exists to prevent. Keeping the definition here means the host can
    /// only ever <i>name</i> a settlement (<see cref="View.GodCommands.FocusSettlement"/>); it cannot invent
    /// a second, drifting answer to "who is being watched".
    ///
    /// <para/><b>Settlements are named by world tile, never by name or reference.</b>
    /// <see cref="World.Settlement.name"/> is not unique — <see cref="World.SettlementFounder.Found"/> and
    /// <c>FoundColony</c> both take an explicit <c>name</c> and never check it (<see cref="Game.NewGame"/>'s
    /// own <c>settlementName</c> parameter goes straight through), and
    /// <see cref="World.RegionNameMaker.MakeRegionName"/> only avoids names that exist at the instant it
    /// draws one, so a later explicit founding can duplicate an earlier generated name. <c>tile</c> is an
    /// <c>int</c> already on <see cref="World.WorldObject"/>, already Scribed, already carried on
    /// <see cref="View.SettlementSummary.Tile"/>, and unique in practice because both founding paths reject a
    /// site within <c>WorldGenStep_Factions.MinSettlementDistance</c> tiles of an existing settlement. It is
    /// also the handle this codebase already uses to re-find a settlement across a save boundary
    /// (<c>Crafting.Guild.ResolveSettlement</c>), because <see cref="World.WorldObject"/> is not
    /// <c>ILoadReferenceable</c> and so cannot be saved as a reference at all.
    ///
    /// <para/><b>Two paths, on purpose.</b> <see cref="Focus"/>/<see cref="ClearFocus"/> apply the change
    /// immediately and touch only the two settlements involved — the player should not wait a tick bucket to
    /// see the settlement they just opened come alive. <see cref="Reconcile"/> is the periodic safety net run
    /// from <see cref="GodManager.GodTick"/>: a citizen born since the last sweep, a settlement founded since
    /// (every <see cref="World.SettlementFounder.Found"/> band arrives <see cref="PawnTier.Full"/> and
    /// insignificant), or a focused settlement that has ceased to exist are all states no focus-change event
    /// ever fired for. Both are idempotent — re-notifying a citizen who already holds the flag recomputes to
    /// the tier they are already at and changes nothing.
    /// </summary>
    public sealed class AttentionManager : IExposable
    {
        /// <summary>Sentinel <see cref="FocusedTile"/> meaning nothing is focused — the god is at
        /// civilization scope, and no citizen anywhere is attended.</summary>
        public const int NoFocus = -1;

        private int focusedTile = NoFocus;

        /// <summary>The world tile of the settlement in focus, or <see cref="NoFocus"/>. See the class doc for
        /// why a tile rather than a name.</summary>
        public int FocusedTile => focusedTile;

        public bool HasFocus => focusedTile != NoFocus;

        /// <summary>The focused settlement, resolved live from <see cref="Find.World"/> — null when nothing is
        /// focused, when no world exists yet, or when the focused settlement has ceased to exist (a stale
        /// focus is cleared by the next <see cref="Reconcile"/>, not by reading this).</summary>
        public World.Settlement? FocusedSettlement => SettlementAt(focusedTile);

        /// <summary>
        /// Moves the god's attention to <paramref name="settlement"/>: every citizen of the settlement losing
        /// focus is told it left, then every citizen of the new one is told it arrived. Returns false — and
        /// notifies nobody — when that settlement is already the focus, so a host that re-sends its current
        /// state cannot churn the tier system.
        /// </summary>
        public bool Focus(World.Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (settlement.tile == focusedTile) return false;

            World.Settlement? previous = SettlementAt(focusedTile);
            focusedTile = settlement.tile;

            if (previous != null) SetAttending(previous, false);
            SetAttending(settlement, true);
            return true;
        }

        /// <summary>Steps back to civilization scope: nobody is attended any more. Returns false when nothing
        /// was focused, which is a no-op rather than a failure — the world already matches what was asked
        /// for. Safe when the focused settlement has been destroyed in the meantime: the focus is dropped and
        /// there is simply nobody left to notify.</summary>
        public bool ClearFocus()
        {
            if (focusedTile == NoFocus) return false;

            World.Settlement? previous = SettlementAt(focusedTile);
            focusedTile = NoFocus;

            if (previous != null) SetAttending(previous, false);
            return true;
        }

        /// <summary>
        /// Brings every citizen in the world into agreement with the current focus, and settles the ones who
        /// have been insignificant long enough (see <see cref="SettlePolicy"/>). Called from
        /// <see cref="GodManager.GodTick"/>, which is already gated to
        /// <see cref="GodTuning.GodTickIntervalTicks"/> — deliberately not self-gated a second time, so the
        /// god layer's cadence is stated in exactly one place.
        ///
        /// <para/>Cost is O(settlements) + O(every Full/Interval citizen), never O(Statistical population):
        /// a settlement's bare <see cref="World.Settlement.StatisticalPopulation"/> has no citizen to walk,
        /// by that tier's own design, and is skipped rather than materialised — the same guarantee
        /// <see cref="GodRollup"/> makes for the read model.
        /// </summary>
        public void Reconcile()
        {
            World.World? world = Find.World;
            if (world == null) return;

            bool focusStillExists = false;
            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is World.Settlement settlement)) continue;

                bool attended = focusedTile != NoFocus && settlement.tile == focusedTile;
                if (attended) focusStillExists = true;

                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int c = 0; c < citizens.Count; c++)
                {
                    Pawn citizen = citizens[c];
                    if (citizen.Dead) continue;

                    citizen.tier.Notify_AttentionChanged(attended);
                    if (!attended) SettlePolicy(citizen);
                }
            }

            // The focused settlement is gone — destroyed, or a save that outlived it. Attention cannot rest on
            // something that does not exist, and leaving a stale tile here would silently attend whatever
            // settlement was founded on that tile next.
            if (focusedTile != NoFocus && !focusStillExists) focusedTile = NoFocus;
        }

        /// <summary>
        /// The director policy <see cref="Pawn_TierTracker.DemoteToStatistical"/> deliberately refuses to
        /// invent for itself: a citizen who has been <see cref="PawnTier.Interval"/> and insignificant for
        /// <see cref="TieringTuning.IntervalSettleTicks"/> settles into the cohort. Elapsed time appears on
        /// this side of the boundary only, and only in the <i>downward</i> direction — promotion stays purely
        /// by significance (the four <c>Notify_</c> hooks), exactly as §11.3 requires, and a citizen who
        /// becomes significant again at any point has their clock cleared and starts over if they lose it.
        /// </summary>
        private static void SettlePolicy(Pawn citizen)
        {
            if (!citizen.tier.HasBeenInsignificantFor(TieringTuning.IntervalSettleTicks)) return;
            citizen.tier.DemoteToStatistical();
        }

        private static void SetAttending(World.Settlement settlement, bool attending)
        {
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn citizen = citizens[i];
                if (citizen.Dead) continue;
                citizen.tier.Notify_AttentionChanged(attending);
            }
        }

        /// <summary>The settlement sitting on <paramref name="tile"/>, or null. The one place a tile is turned
        /// back into a settlement, so <see cref="View.GodCommands"/> and this class can never disagree about
        /// what a tile names.</summary>
        public static World.Settlement? SettlementAt(int tile)
        {
            if (tile == NoFocus) return null;
            World.World? world = Find.World;
            if (world == null) return null;

            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is World.Settlement settlement && settlement.tile == tile) return settlement;
            }
            return null;
        }

        // ---- Scribe ----

        /// <summary>
        /// Only the tile is saved, and that is the whole of it. Each citizen's own attention flag and tier are
        /// already round-tripped by <see cref="Pawn_TierTracker.ExposeData"/>, so a load needs no re-application
        /// pass — which also means this never has to care that <see cref="Game.ExposeData"/> deep-saves the
        /// god before the world, and that <see cref="Find.World"/> is therefore not yet readable while this
        /// runs.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref focusedTile, "focusedTile", NoFocus);
        }
    }
}
