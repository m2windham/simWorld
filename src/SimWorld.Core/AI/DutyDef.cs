using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// A standing goal a pawn carries while it is on somebody else's map (RimWorld: <c>Verse.AI.DutyDef</c>,
    /// whose <c>thinkNode</c> field this is the whole of). A pawn with a duty runs the duty's own think node
    /// as one tier of its main tree (<see cref="ThinkNode_Duty"/>), above needs and work; a pawn without one
    /// — every citizen of the settlement being played — never evaluates a line of this and behaves exactly as
    /// it did before duties existed.
    ///
    /// <para/><b>This is the port's answer to RimWorld's Lord, and the difference is deliberate.</b> In
    /// RimWorld a raid is a <c>Lord</c> owning the squad, running a <c>LordJob_AssaultColony</c> whose
    /// <c>LordToil</c>s hand every member a duty and whose <c>Trigger</c>s swap one toil for another when
    /// something changes for the group as a whole ("half of us are down" → everybody flees). This port has no
    /// Lord, no LordToil and no Trigger, and adding all three is far more than a squad that walks to town
    /// needs. So the graph is split at its one load-bearing joint:
    /// <list type="bullet">
    /// <item><b>The shared goal is kept</b> — every member of an arriving group is handed the same
    /// <see cref="DutyDef"/> at the moment it arrives (<see cref="DutyUtility.AssignToAll"/>), which is what
    /// a <c>LordToil</c> does to its lord's pawns and is the entire reason a squad moves as one.</item>
    /// <item><b>The toil transitions become think-tree priority order.</b> A duty's think node is a
    /// <see cref="ThinkNode_Priority"/> whose tiers are the phases of the job in order — close on the enemy,
    /// then, when there is no enemy left, leave. The tree is re-evaluated every time the pawn needs a job, so
    /// each tier's own "do I apply?" answers continuously what a <c>Trigger</c> would have answered once.</item>
    /// <item><b>What is genuinely lost is group feedback</b>, and it is worth naming precisely: no phase
    /// change can depend on a fact about the <i>squad</i> rather than about the map — "we have lost half our
    /// number, break off" is the canonical one, since nothing here remembers how many arrived. Every pawn
    /// decides alone, from what it can see, and reaches the same answer at slightly different moments rather
    /// than all at once. A morale/retreat model would need the Lord back (or a roster object that is one in
    /// all but name); it is recorded as absent here rather than improvised, the same way
    /// <see cref="CombatPostureUtility"/> records that nobody flees.</item>
    /// </list>
    ///
    /// <para/><b>Why a Def carrying a think node rather than a hard-coded tier per arrival kind.</b> The bug
    /// this landed for is a raid that stands on the map edge, but approach is not a raid mechanic: a hunting
    /// party, a caravan and a trade group all want "you arrived over there, your business is over here", and
    /// each of those is a second <see cref="DutyDef"/> in content — a different priority list over the same
    /// <c>JobGiver_*</c>s — with no new code in the AI layer at all. A tier per arrival kind would have been
    /// four conditions in the shared think tree, each a special case.
    ///
    /// <para/><b>What this does not carry.</b> RimWorld's <c>PawnDuty</c> (the per-pawn instance) also holds a
    /// focus target and a radius; nothing in this port reads either yet, so the pawn's duty is the def alone
    /// (<c>MindState.Pawn_MindState.duty</c>) rather than a wrapper object with unread fields on it.
    /// </summary>
    public class DutyDef : Def
    {
        /// <summary>What a pawn under this duty does, in priority order — evaluated by
        /// <see cref="ThinkNode_Duty"/> as one tier of the pawn's main think tree.</summary>
        public ThinkNode thinkNode = null!;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (thinkNode == null) yield return "thinkNode is required.";
        }
    }

    /// <summary>
    /// The tier that runs a pawn's duty, if it has one (RimWorld: <c>Verse.AI.ThinkNode_Duty</c>). Contributes
    /// nothing at all for a pawn with no duty, which is every pawn who lives here — so this node is a strict
    /// no-op for a settlement's own citizens and changes nothing about how they behave.
    /// </summary>
    public sealed class ThinkNode_Duty : ThinkNode
    {
        public override ThinkResult TryIssueJobPackage(Pawn pawn)
        {
            DutyDef? duty = pawn.mindState?.duty;
            if (duty?.thinkNode == null) return ThinkResult.NoJob;
            return duty.thinkNode.TryIssueJobPackage(pawn);
        }
    }

    /// <summary>Assigns and clears duties. Its own file rather than a method on the arrival site, so the
    /// reasoning above has one home and the raid worker's edit is a single call (CLAUDE.md — "add a file
    /// rather than edit a shared one").</summary>
    public static class DutyUtility
    {
        /// <summary>Hands every pawn in <paramref name="group"/> the same standing goal — a
        /// <c>LordToil</c>'s one job, without a Lord to own it. Pass null to clear.</summary>
        public static void AssignToAll(IReadOnlyList<Pawn> group, DutyDef? duty)
        {
            if (group == null) return;
            for (int i = 0; i < group.Count; i++)
            {
                Pawn pawn = group[i];
                if (pawn.mindState != null) pawn.mindState.duty = duty;
            }
        }
    }

    /// <summary>
    /// Duties bound by name. Its own class rather than fields appended to an existing <c>[DefOf]</c>:
    /// <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type, so a module's bindings never need an
    /// edit to a file another lane is also in (CLAUDE.md), exactly as <see cref="CombatAIDefOf"/> says.
    /// </summary>
    [DefOf]
    public static class DutyDefOf
    {
        /// <summary>Cross the map, find the settlement's people, and when there is nobody left to fight, go
        /// home (RimWorld: <c>DutyDefOf.AssaultColony</c>, the duty <c>LordToil_AssaultColony</c> hands out).
        /// Given to a raid squad the moment it lands — see <c>Director.IncidentWorker_RaidEnemy.Arrive</c>.</summary>
        public static DutyDef AssaultSettlement = null!;
    }

    /// <summary>The jobs a duty's tiers issue. Separate from <see cref="CombatAIDefOf"/> for the same reason
    /// that class is separate from <see cref="JobDefOf"/>.</summary>
    [DefOf]
    public static class DutyJobDefOf
    {
        /// <summary>Walk to a cell, and nothing else (RimWorld: <c>JobDefOf.Goto</c>).</summary>
        public static JobDef Goto = null!;

        /// <summary>Walk off the edge of the map and be gone (RimWorld: <c>JobDefOf.Goto</c> with
        /// <c>Job.exitMapOnArrival</c>; a job of its own here — see <see cref="JobDriver_ExitMap"/>).</summary>
        public static JobDef ExitMap = null!;
    }
}
