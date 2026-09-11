using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Thoughts;
using SimWorld.Work;

namespace SimWorld.Offices
{
    /// <summary>Who a seat belongs to — one settlement, or the whole civilization.</summary>
    public enum OfficeScope
    {
        /// <summary>One seat per <see cref="World.Settlement"/>. A civilization of forty towns has forty
        /// stewards, one each, and they have nothing to do with one another.</summary>
        Settlement,

        /// <summary>One seat per <see cref="Factions.Faction"/> — the civilization, not the town. Its holder
        /// is drawn from every settlement that civilization owns, and a settlement with no faction has no
        /// claim on it at all.</summary>
        Civilization,
    }

    /// <summary>
    /// A <b>station</b>: a scarce, named seat one citizen holds, filled by a deterministic rule and refilled
    /// when it falls empty. This is what <see cref="God.AttentionBudget"/> means by "significant by station"
    /// and what <c>Pawn_TierTracker.Notify_RoleChanged</c>'s own doc calls "a role — leader, founder, great
    /// worker": until this module landed, that notification had exactly one caller
    /// (<c>MigrationManager</c> marking an arriving household founder) and the top rank of the significance
    /// ordering the Full-tier budget spends against could not be earned by anything a settlement does.
    ///
    /// <para/><b>Recorded translation, not a port — and deliberately not a second "role".</b> RimWorld's two
    /// nearest relatives both miss. <c>Pawn.royalty</c>'s <c>RoyalTitleDef</c> is granted by an <i>outside</i>
    /// faction in exchange for quest favour, is per-pawn rather than per-place, and has no succession at all
    /// (a dead noble's title simply ends). <c>Precept_Role</c> is closer in shape — it is capped, singular and
    /// re-assignable — but it exists only because an ideoligion precept grants it, and this port already has
    /// that as <see cref="Social.Ideology.IdeoRoleDef"/>, which is about belief rather than about running a
    /// town. What was missing is the seat itself.
    ///
    /// <para/>So this def does <b>not</b> introduce a third notion of "role". It introduces the thing the two
    /// existing ones lacked — scarcity, a place, a selection rule and succession — and expresses what its
    /// holder actually <i>does</i> by reusing <see cref="Work.RoleDef"/> through <see cref="role"/>, exactly
    /// the way <see cref="Crafting.GuildDef.role"/> already says "a citizen is a member of this guild because
    /// they carry this role". A station is a role plus a seat; the seat is this def, and the role is the one
    /// that was already there.
    ///
    /// <para/><b>The holder is the citizen carrying <see cref="role"/> — there is no separate roster.</b>
    /// That is the whole of this module's state, and it is state that already existed and already saved:
    /// <see cref="Pawn_WorkSettings.Role"/> Scribes with the pawn, and so does
    /// <c>Pawn_TierTracker.hasRole</c>. Nothing here is registered with <see cref="Sim.Find"/>, nothing is
    /// deep-saved by <see cref="Sim.Game"/>, and a save round-trip cannot desynchronise a seat from its
    /// holder because the holder <i>is</i> the seat. The cost of that choice is stated plainly on
    /// <see cref="OfficeManager"/>: an office role is reserved for its office, and anything else that writes
    /// one onto a citizen is corrected on the next sweep.
    /// </summary>
    public class OfficeDef : Def
    {
        /// <summary>Whether this seat belongs to a settlement or to the civilization. See <see cref="OfficeScope"/>.</summary>
        public OfficeScope scope = OfficeScope.Settlement;

        /// <summary>
        /// The standing <see cref="Work.RoleDef"/> this seat's holder carries, and the only record of who
        /// holds it (see the class doc). It is also what the office <i>does</i> in the part of the simulation
        /// that has nothing to do with tiering: <see cref="Pawn_WorkSettings.SetRole"/> lifts the role's
        /// <see cref="Work.RoleDef.emphasizedWorkTypes"/> to <see cref="Pawn_WorkSettings.EmphasizedPriority"/>
        /// and turns <see cref="Pawn_WorkSettings.useWorkPriorities"/> on, so the holder's work-giver order
        /// (<see cref="Pawn_WorkSettings.WorkGiversInOrderNormal"/> — what the think tree actually scans)
        /// genuinely changes, and a settlement with an empty seat has nobody doing that work first.
        /// <para/>
        /// Must be unique across every <see cref="OfficeDef"/>, since it is the identity of the seat; a
        /// content test asserts it rather than <see cref="ConfigErrors"/>, which runs before the database this
        /// would have to consult is readable.
        /// </summary>
        public RoleDef role = null!;

        /// <summary>
        /// Decision order, lowest first, and the reason a citizen can hold at most one office. Seats are
        /// reconciled in this order and a citizen already seated is not a candidate for anything later, so a
        /// civilization's eldest is chosen before a town's steward and never ends up being asked to be both —
        /// which matters because <see cref="Pawn_WorkSettings.Role"/> is a single field and two offices
        /// writing to it would leave the loser's seat looking permanently vacant.
        /// </summary>
        public int precedence;

        /// <summary>
        /// How many citizens stand for the seat when it falls empty: the strongest
        /// <see cref="OfficeSelectionWorker.CandidacyStrength"/> claims, in order. Candidacy and selection are
        /// deliberately two different questions — see <see cref="OfficeSelectionWorker"/> — and this bounds
        /// the second, which is the expensive one.
        /// </summary>
        public int candidatePoolSize = 8;

        /// <summary>Runtime selection rule; the same <c>Class=</c> polymorphism <see cref="God.EdictDef.workerClass"/>
        /// and <see cref="Thoughts.ThoughtDef.workerClass"/> already use, so a new kind of election is content
        /// plus one class rather than a change to this module.</summary>
        public Type selectionWorkerClass = typeof(OfficeSelectionWorker);

        /// <summary>
        /// A situational <see cref="ThoughtDef"/> active on this seat's holder for exactly as long as they
        /// hold it (its <see cref="ThoughtDef.workerClass"/> must be <see cref="ThoughtWorker_HoldsOffice"/>).
        /// Situational rather than a granted memory for the same reason <see cref="God.EdictDef.moodThought"/>
        /// is: it must vanish the instant the seat changes hands, with nothing left behind on a citizen who
        /// no longer holds it.
        /// </summary>
        public ThoughtDef? holderThought;

        private OfficeSelectionWorker? workerInt;

        /// <summary>Lazily constructed, cached worker instance (same pattern as <see cref="God.EdictDef.Worker"/>).</summary>
        public OfficeSelectionWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (OfficeSelectionWorker)Activator.CreateInstance(selectionWorkerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;

            if (role == null)
            {
                yield return "role is null — the role a holder carries is the only record that this seat is held at all.";
            }
            if (candidatePoolSize < 1)
            {
                yield return "candidatePoolSize must be >= 1, or nobody could ever stand for the seat.";
            }
            if (precedence < 0)
            {
                yield return "precedence must be >= 0.";
            }
            if (!typeof(OfficeSelectionWorker).IsAssignableFrom(selectionWorkerClass))
            {
                yield return "selectionWorkerClass must derive from OfficeSelectionWorker.";
            }
            if (holderThought != null && !holderThought.IsSituational)
            {
                yield return "holderThought must be a situational thought (a workerClass, not a duration) so it "
                    + "ends with the holder's tenure instead of decaying on somebody who no longer holds the seat.";
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            workerInt = null;
        }
    }

    /// <summary>
    /// The stations this port ships. Its own class rather than fields appended to another module's DefOf:
    /// <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type, so a module's bindings never need an
    /// edit to a file another lane may also be editing (CLAUDE.md — "add a file rather than edit a shared
    /// one").
    /// </summary>
    [DefOf]
    public static class OfficeDefOf
    {
        /// <summary>A settlement's leader (<see cref="OfficeScope.Settlement"/>).</summary>
        public static OfficeDef Steward = null!;

        /// <summary>The civilization's eldest — old enough to remember its founding
        /// (<see cref="OfficeScope.Civilization"/>). Retires itself: see <see cref="OfficeSelectionWorker_Eldest"/>.</summary>
        public static OfficeDef Elder = null!;
    }
}
