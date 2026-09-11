using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>Guest or prisoner (RimWorld: <c>RimWorld.GuestStatus</c>; the Ideology-only <c>Slave</c> value is out of scope).</summary>
    public enum GuestStatus
    {
        Guest,
        Prisoner,
    }

    /// <summary>How a warden may act on a prisoner during a visit (RimWorld: <c>RimWorld.PrisonerInteractionModeDef</c>; trimmed to the two modes this pass's <see cref="WardenUtility"/> actually implements — no chat/enslave/execute/ideology conversion, none of which this pass models).</summary>
    public class PrisonerInteractionModeDef : Def
    {
    }

    [DefOf]
    public static class PrisonerInteractionModeDefOf
    {
        public static PrisonerInteractionModeDef NoInteraction = null!;
        public static PrisonerInteractionModeDef AttemptRecruit = null!;
    }

    /// <summary>
    /// One captured pawn's status with its host faction (RimWorld: <c>Verse.Pawn_GuestTracker</c>).
    /// <b>Deviation — where this state lives:</b> real RimWorld hangs this off <c>Pawn.guest</c>, a field on
    /// <c>Pawn</c> itself. This pass's brief explicitly keeps <c>Pawns/**</c> off limits, and <c>Pawn</c>
    /// carries no such field — so instead of a per-pawn field, the <em>host</em> <see cref="Faction"/> keeps
    /// one of these per prisoner it holds (<see cref="Faction.prisoners"/>); <see cref="CaptureUtility.FindHostFaction"/>
    /// is the reverse lookup ("who, if anyone, holds this pawn"). Mechanically this is the same fact recorded
    /// the other way around: RimWorld can ask a pawn "who holds you?"; this port asks a faction "who do you
    /// hold?" and scans for the pawn's entry. Everything this pass actually needed — status, interaction
    /// mode, resistance/will — is on here unchanged from RimWorld's own field names.
    /// <b>Trim:</b> real RimWorld's tracker also carries slavery, ideology conversion, prison-break/escape
    /// timers, and a "does this pawn even know a trap/warden schedule exists" memory model — none of which
    /// exist elsewhere in this pass either, so none of it is reproduced here.
    /// </summary>
    public sealed class Pawn_GuestTracker : IExposable
    {
        public Pawn pawn = null!;

        public GuestStatus guestStatus = GuestStatus.Prisoner;

        public PrisonerInteractionModeDef interactionMode = null!;

        /// <summary>Falls toward 0 as a warden interacts (RimWorld: <c>Pawn_GuestTracker.resistance</c>); at 0, the next interaction recruits instead.</summary>
        public float resistance;

        /// <summary>Carried over from RimWorld's shape but not yet read by anything in this pass — recruit chance here is a flat "resistance hit zero", not further gated by will (see <see cref="WardenUtility"/>'s own remarks on what real RimWorld's recruit roll also weighs that this port does not: negotiator skill, mood, opinion).</summary>
        public float will;

        /// <summary>For Scribe's deep-load construction.</summary>
        public Pawn_GuestTracker()
        {
        }

        public Pawn_GuestTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public bool IsPrisoner => guestStatus == GuestStatus.Prisoner;

        public void ExposeData()
        {
            Pawn? p = pawn;
            Scribe_References.Look(ref p, "pawn");
            pawn = p!;
            Scribe_Values.Look(ref guestStatus, "guestStatus", GuestStatus.Prisoner);
            PrisonerInteractionModeDef? mode = interactionMode;
            Scribe_Defs.Look(ref mode, "interactionMode");
            interactionMode = mode!;
            Scribe_Values.Look(ref resistance, "resistance");
            Scribe_Values.Look(ref will, "will");
        }

        public override string ToString() => (pawn?.Label ?? "?") + " (" + guestStatus + ", resistance " + resistance.ToString("0.#") + ")";
    }

    /// <summary>
    /// The downed → captured → prisoner transition (RimWorld: the effect of <c>JobDriver_Capturee</c> calling
    /// <c>Pawn_GuestTracker.CapturedBy</c> — this pass has no job/hauling system to carry a downed pawn to a
    /// bed, so <see cref="Capture"/> is the bare state transition a future carry-to-prison job would call).
    /// </summary>
    public static class CaptureUtility
    {
        /// <summary>
        /// Starting resistance range. <b>Unsourced:</b> real RimWorld draws this per <c>PawnKindDef.initialResistanceRange</c>,
        /// a field this port's <c>PawnKindDef</c> does not carry (Pawns/Generation is another lane's module —
        /// see this module's report); this is this port's own flat stand-in, pinned by <c>CombatTests</c>'
        /// own capture tests on the trend (starts positive, interacting brings it down, hitting zero
        /// recruits), never on the literal.
        /// </summary>
        public static readonly FloatRange DefaultInitialResistanceRange = new FloatRange(4f, 14f);

        /// <summary>Starting will range; same "unsourced own stand-in" caveat as <see cref="DefaultInitialResistanceRange"/>. Not yet read by <see cref="WardenUtility"/> (see <see cref="Pawn_GuestTracker.will"/>'s own remarks).</summary>
        public static readonly FloatRange DefaultInitialWillRange = new FloatRange(0f, 4f);

        /// <summary>
        /// Whether <paramref name="captor"/> may capture <paramref name="victim"/> right now: downed, alive,
        /// each with a faction, the victim's faction hostile to the captor's, and not already someone's
        /// prisoner. <b>Trim:</b> real RimWorld also allows capturing a factionless human/animal (a wild man,
        /// a tamed-but-hostile creature); this port requires a faction on both sides, so that case is simply
        /// never capturable here — see this module's report.
        /// </summary>
        public static bool CanCapture(Pawn captor, Pawn victim)
        {
            if (captor == null || victim == null) return false;
            if (ReferenceEquals(captor, victim)) return false;
            if (victim.Dead || !victim.Downed) return false;
            if (captor.faction == null || victim.faction == null) return false;
            if (ReferenceEquals(captor.faction, victim.faction)) return false;
            if (!captor.faction.HostileTo(victim.faction)) return false;
            return FindHostFaction(victim) == null;
        }

        /// <summary>Captures <paramref name="victim"/> for <paramref name="captor"/>'s faction, or returns null when <see cref="CanCapture"/> refuses.</summary>
        public static Pawn_GuestTracker? Capture(Pawn captor, Pawn victim)
        {
            if (!CanCapture(captor, victim)) return null;

            var tracker = new Pawn_GuestTracker(victim)
            {
                guestStatus = GuestStatus.Prisoner,
                interactionMode = PrisonerInteractionModeDefOf.NoInteraction,
                resistance = Rand.Range(DefaultInitialResistanceRange),
                will = Rand.Range(DefaultInitialWillRange),
            };
            captor.faction!.prisoners.Add(tracker);

            // Becoming a prisoner changes which needs this pawn should have (NeedDef.neverOnPrisoner —
            // shipped on recreation, which a prisoner has no way to satisfy), and unlike recruitment and
            // release this transition does not touch Pawn.faction, whose setter would otherwise have done
            // this. RimWorld sweeps the same way from Pawn_GuestTracker.SetGuestStatus.
            victim.needs?.AddOrRemoveNeedsAsAppropriate();
            return tracker;
        }

        /// <summary>The faction currently holding <paramref name="pawn"/> prisoner, or null.</summary>
        public static Faction? FindHostFaction(Pawn pawn) => FindHostFaction(pawn, out _);

        /// <summary>As <see cref="FindHostFaction(Pawn)"/>, also handing back the tracker itself.</summary>
        public static Faction? FindHostFaction(Pawn pawn, out Pawn_GuestTracker? tracker)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            IReadOnlyList<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < factions.Count; i++)
            {
                List<Pawn_GuestTracker> list = factions[i].prisoners;
                for (int j = 0; j < list.Count; j++)
                {
                    if (ReferenceEquals(list[j].pawn, pawn))
                    {
                        tracker = list[j];
                        return factions[i];
                    }
                }
            }
            tracker = null;
            return null;
        }
    }

    /// <summary>
    /// What the Warden work type has to do (RimWorld: the effect of <c>InteractionWorker_RecruitAttempt.Interacted</c>
    /// on a prisoner — this pass has no social-interaction scheduler or job system, so <see cref="TryInteract"/>
    /// is the bare per-visit resolution step a future <c>WorkGiver_Warden</c>/<c>JobDriver_Warden</c> (Work/AI,
    /// not this lane) would call once per interaction it carries out).
    /// </summary>
    public static class WardenUtility
    {
        /// <summary>RimWorld: <c>RimWorld.InteractionWorker_RecruitAttempt.BaseResistanceReductionPerInteraction</c>. <b>Trim:</b> real RimWorld then scales this by the negotiator's <c>NegotiationAbility</c> stat and curves over the prisoner's mood and opinion of the warden — none of Stats/Needs/Relations' specifics are reached from here, so every interaction reduces by exactly this much.</summary>
        public const float BaseResistanceReductionPerInteraction = 1f;

        public enum InteractionOutcome
        {
            /// <summary>Not eligible: not a prisoner of the warden's own faction, or not set to be recruited.</summary>
            NotEligible,
            ResistanceReduced,
            Recruited,
        }

        /// <summary>
        /// One warden visit: reduces resistance, or — once resistance is already at zero from a prior visit —
        /// recruits outright (RimWorld's own two-step shape: the visit that brings resistance to zero only
        /// reduces it; the next one recruits).
        /// </summary>
        public static InteractionOutcome TryInteract(Pawn warden, Pawn prisoner)
        {
            if (warden == null) throw new ArgumentNullException(nameof(warden));
            if (prisoner == null) throw new ArgumentNullException(nameof(prisoner));

            Faction? host = CaptureUtility.FindHostFaction(prisoner, out Pawn_GuestTracker? tracker);
            if (host == null || tracker == null || !tracker.IsPrisoner) return InteractionOutcome.NotEligible;
            if (warden.faction == null || !ReferenceEquals(host, warden.faction)) return InteractionOutcome.NotEligible;
            if (tracker.interactionMode != PrisonerInteractionModeDefOf.AttemptRecruit) return InteractionOutcome.NotEligible;

            if (tracker.resistance > 0f)
            {
                tracker.resistance = Math.Max(0f, tracker.resistance - BaseResistanceReductionPerInteraction);
                return InteractionOutcome.ResistanceReduced;
            }

            host.prisoners.Remove(tracker);
            prisoner.faction = host;
            return InteractionOutcome.Recruited;
        }

        /// <summary>Frees a prisoner outright: removed from its host's roster, faction cleared. False when <paramref name="prisoner"/> was not held by anyone.</summary>
        public static bool Release(Pawn prisoner)
        {
            if (prisoner == null) throw new ArgumentNullException(nameof(prisoner));
            Faction? host = CaptureUtility.FindHostFaction(prisoner, out Pawn_GuestTracker? tracker);
            if (host == null || tracker == null) return false;
            host.prisoners.Remove(tracker);
            prisoner.faction = null;
            return true;
        }
    }
}
