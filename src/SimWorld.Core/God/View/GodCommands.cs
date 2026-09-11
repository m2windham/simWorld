using System;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>What happened when the host asked for something.</summary>
    public enum GodCommandOutcome
    {
        /// <summary>The simulation did what was asked.</summary>
        Done,

        /// <summary>No edict in content carries that defName. A host built against a snapshot cannot hit this
        /// except by holding a name across a content change, which is exactly when it wants to be told.</summary>
        UnknownEdict,

        /// <summary>No settlement sits on that world tile. Separate from <see cref="UnknownEdict"/> rather
        /// than a shared "unknown target" because the two are reached by different commands and a host
        /// switching on the outcome should not have to know which one it just called; and separate from
        /// <see cref="Refused"/> because nothing refused anything — the handle is simply stale, which is what
        /// a settlement destroyed between two snapshots looks like from the host's side.</summary>
        UnknownSettlement,

        /// <summary>The rules refused it. <see cref="GodCommandResult.Reason"/> says which rule.</summary>
        Refused,

        /// <summary>Nothing to do — the request was already true (rescinding an edict nobody issued).</summary>
        NoChange,
    }

    /// <summary>The outcome of one command, with a sentence explaining it.</summary>
    public sealed class GodCommandResult
    {
        internal GodCommandResult(GodCommandOutcome outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        public GodCommandOutcome Outcome { get; }

        /// <summary>Always populated, success included, so a host can surface the same field either way.</summary>
        public string Reason { get; }

        /// <summary>True only for <see cref="GodCommandOutcome.Done"/> — a refusal and a no-op are both "the
        /// world did not change", but only one of them is worth telling the player about, so they stay
        /// distinct in <see cref="Outcome"/> rather than collapsing into a bool.</summary>
        public bool Changed => Outcome == GodCommandOutcome.Done;

        internal static GodCommandResult Done(string reason) => new GodCommandResult(GodCommandOutcome.Done, reason);
        internal static GodCommandResult Unknown(string defName) =>
            new GodCommandResult(GodCommandOutcome.UnknownEdict, "No edict named '" + defName + "'.");
        internal static GodCommandResult UnknownSettlement(int tile) =>
            new GodCommandResult(GodCommandOutcome.UnknownSettlement,
                "No settlement on tile " + tile.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        internal static GodCommandResult Refused(string reason) => new GodCommandResult(GodCommandOutcome.Refused, reason);
        internal static GodCommandResult NoChange(string reason) => new GodCommandResult(GodCommandOutcome.NoChange, reason);
    }

    /// <summary>
    /// Everything a god can actually do, and the only way a host can do it.
    ///
    /// <para/>The pairing with <see cref="GodViewSnapshot"/> is the whole design: the host reads a snapshot of
    /// values and writes back nothing but a name and an intent. It never holds a <see cref="EdictDef"/>, never
    /// holds a <see cref="GodManager"/>, and so cannot reach a worker or mutate simulation state by any route
    /// this class does not offer. Widening what a god may do means adding a method here, deliberately, rather
    /// than a host discovering it can already do it.
    ///
    /// <para/><b>Refusals are explained, not just returned.</b> Every path returns a reason in the host's own
    /// terms, because the alternative — a bare false — pushes the host into reimplementing the rules to guess
    /// why, and a reimplemented rule is a rule that drifts.
    ///
    /// <para/><b>Why this is not a queue.</b> A command applies immediately, on the caller's thread, exactly as
    /// if the simulation had done it. Deferring commands to a tick boundary would be the right answer if the
    /// host ran on its own thread against a ticking sim — and if this port ever grows that, this class is where
    /// the queue goes, with no host change beyond the wait. It does not have it yet, and a queue that only ever
    /// drains immediately would be a fiction that made the seam look safer than it is.
    /// </summary>
    public static class GodCommands
    {
        /// <summary>Issues the named edict. Refuses for exactly the reasons
        /// <see cref="EdictOption.Availability"/> reports, in the same words.</summary>
        public static GodCommandResult IssueEdict(string defName)
        {
            EdictDef? def = Resolve(defName);
            if (def == null) return GodCommandResult.Unknown(defName);

            GodManager god = Find.God;
            if (god.IsActive(def)) return GodCommandResult.NoChange(def.LabelCap + " is already in force.");

            if (!god.CanActivate(def))
            {
                // Reuse the read model's own wording rather than composing a second explanation here: two
                // descriptions of one rule set drift apart, and the view has already shown the player this one.
                EdictOption option = EdictOption.For(def, isActive: false, Find.ResearchManager.CurrentEra,
                    slotFree: god.ActiveEdicts.Count < GodTuning.MaxActiveEdicts);
                return GodCommandResult.Refused(option.Reason);
            }

            return god.Activate(def)
                ? GodCommandResult.Done(def.LabelCap + " issued.")
                // Unreachable while Activate's only refusal is CanActivate, which was just checked. Kept
                // because "the simulation said no after saying yes" is worth surfacing rather than asserting
                // away, and a silent false here would be indistinguishable from success to the host.
                : GodCommandResult.Refused(def.LabelCap + " was refused on issue.");
        }

        /// <summary>Rescinds the named edict. Rescinding one that is not in force is a no-op, not a failure —
        /// the world already matches what was asked for.</summary>
        public static GodCommandResult RescindEdict(string defName)
        {
            EdictDef? def = Resolve(defName);
            if (def == null) return GodCommandResult.Unknown(defName);

            return Find.God.Deactivate(def)
                ? GodCommandResult.Done(def.LabelCap + " rescinded.")
                : GodCommandResult.NoChange(def.LabelCap + " was not in force.");
        }

        /// <summary>
        /// Opens the settlement on <paramref name="tile"/> — the god's attention moves there, and with it the
        /// only thing that holds a citizen at <see cref="Pawns.PawnTier.Full"/> (spec §11.2/§11.3: "dropping
        /// into ticked time <i>is</i> promoting the attended settlement to Full"). Every citizen of that
        /// settlement is promoted; every citizen of the settlement that had focus is let go.
        ///
        /// <para/><b>A settlement is named by its world tile</b>, the same way an edict is named by its
        /// defName and for the same reason: a value the host can hold across a reload and a repaint, which
        /// cannot be used to reach the settlement object itself. <see cref="SettlementSummary.Tile"/> on the
        /// snapshot is where a host gets one. See <see cref="AttentionManager"/>'s class doc for why the tile
        /// and not <see cref="World.Settlement.name"/>, which is not unique.
        /// </summary>
        public static GodCommandResult FocusSettlement(int tile)
        {
            World.Settlement? settlement = AttentionManager.SettlementAt(tile);
            if (settlement == null) return GodCommandResult.UnknownSettlement(tile);

            AttentionManager attention = Find.God.Attention;
            return attention.Focus(settlement)
                ? GodCommandResult.Done("Attention moved to " + settlement.name + ".")
                : GodCommandResult.NoChange(settlement.name + " already has the god's attention.");
        }

        /// <summary>Steps back to civilization scope: no settlement is open, so nobody is attended and every
        /// citizen held at Full by attention alone falls to Interval. Clearing a focus that was never set is a
        /// no-op, not a failure — the same treatment <see cref="RescindEdict"/> gives an edict nobody
        /// issued.</summary>
        public static GodCommandResult ClearSettlementFocus()
        {
            return Find.God.Attention.ClearFocus()
                ? GodCommandResult.Done("Attention withdrawn to the civilization.")
                : GodCommandResult.NoChange("No settlement had the god's attention.");
        }

        private static EdictDef? Resolve(string defName) =>
            string.IsNullOrEmpty(defName) ? null : DefDatabase<EdictDef>.GetNamedSilentFail(defName);
    }
}
