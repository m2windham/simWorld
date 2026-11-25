using System.Collections.Generic;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Guards its subnodes behind a condition (RimWorld: <c>Verse.AI.ThinkNode_Conditional</c>). When
    /// <see cref="Satisfied"/> is false, this node contributes nothing and the enclosing
    /// <see cref="ThinkNode_Priority"/> falls through to its next child — used for the needs tier so a
    /// pawn who is not hungry never even asks <see cref="JobGiver_GetFood"/> to look.
    /// </summary>
    public abstract class ThinkNode_Conditional : ThinkNode
    {
        public List<ThinkNode> subNodes = new List<ThinkNode>();

        protected abstract bool Satisfied(Pawn pawn);

        public override ThinkResult TryIssueJobPackage(Pawn pawn)
        {
            if (!Satisfied(pawn)) return ThinkResult.NoJob;
            for (int i = 0; i < subNodes.Count; i++)
            {
                ThinkResult result = subNodes[i].TryIssueJobPackage(pawn);
                if (result.IsValid) return result;
            }
            return ThinkResult.NoJob;
        }
    }

    /// <summary>
    /// True while a mental state is active. Placed first in the humanlike tree so it pre-empts needs, orders
    /// and work entirely (RimWorld: mental states run their own substituted think tree, replacing the whole
    /// evaluation rather than sitting inside it as a guarded branch — see this module's report for the
    /// deliberate simplification here).
    /// </summary>
    public sealed class ThinkNode_ConditionalInMentalState : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.InMentalState;
    }

    /// <summary>True while the pawn's food need has fallen below "fed".</summary>
    public sealed class ThinkNode_ConditionalHungry : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn.needs.food == null) return false;
            HungerCategory hunger = pawn.needs.food.CurCategory;
            // Lean rations (EdictDef.rationFood): wait for urgent hunger, when the need falls at a quarter of the
            // fed rate instead of a half. Only a citizen an edict in force applies to; animals never.
            if (pawn.RaceProps.Humanlike && God.EdictWorkUtility.Rationed(pawn)) return hunger >= HungerCategory.UrgentlyHungry;
            return hunger != HungerCategory.Fed;
        }
    }

    /// <summary>
    /// True while the pawn, not already resting, should go to bed: its rest need has fallen below "rested", or
    /// it is night and the need is below <see cref="FallAsleepMaxLevel"/>.
    ///
    /// <para/><b>The night half is RimWorld's default timetable.</b> A colonist's schedule is Sleep from 22h to
    /// 5h and Anything between (<c>Pawn_TimetableTracker</c>'s constructor), and <c>JobGiver_GetRest</c> puts a
    /// colonist to bed under Sleep while rest is below <c>RestUtility.FallAsleepMaxLevel</c> (0.75), under
    /// Anything only once it is below 0.3 (recalled from the decompiled source, not quoted). This port had only
    /// the second rule, so nobody's sleep was tied to the clock: a citizen slept when tired, woke rested and
    /// worked through the night, and the band drifted until the fourth playtest found healthy people "lying
    /// down" at noon (a 20-strong band at 12h had up to five in bed and at midnight a dozen hauling). There is no
    /// timetable to edit here, so every citizen keeps the default one; the hours are read at the longitude
    /// daylight is (<see cref="Building.PlantUtility.AssumedLongitudeDegrees"/>), so night is when it is dark.
    /// People only: RimWorld gives animals a night of their own (before 7h and after 21h), which is not ported
    /// here because nothing about animals was wrong.
    /// </para>
    /// </summary>
    public sealed class ThinkNode_ConditionalTired : ThinkNode_Conditional
    {
        /// <summary>RimWorld's <c>RestUtility.FallAsleepMaxLevel</c>: under the Sleep block a pawn goes to bed
        /// below this.</summary>
        public const float FallAsleepMaxLevel = 0.75f;

        /// <summary>First hour of the default timetable's Sleep block.</summary>
        public const int SleepFromHour = 22;

        /// <summary>Last hour of the default timetable's Sleep block.</summary>
        public const int SleepUntilHour = 5;

        protected override bool Satisfied(Pawn pawn)
        {
            Need_Rest? rest = pawn.needs.rest;
            if (rest == null || pawn.Asleep) return false;
            if (rest.CurCategory != RestCategory.Rested) return true;
            return pawn.RaceProps.Humanlike && IsSleepHour() && rest.CurLevel < FallAsleepMaxLevel;
        }

        /// <summary>Whether the clock is in the default timetable's Sleep block now.</summary>
        public static bool IsSleepHour()
        {
            int hour = GenDate.HourOfDay(Find.TickManager.TicksAbs, Building.PlantUtility.AssumedLongitudeDegrees);
            return hour >= SleepFromHour || hour <= SleepUntilHour;
        }
    }

    /// <summary>
    /// True while this animal is marked angry at a would-be tamer after a failed taming roll (system:
    /// ai.animals). Placed first in the animal think tree, mirroring
    /// <see cref="ThinkNode_ConditionalInMentalState"/>'s position in the humanlike one — see
    /// <see cref="MindState.Pawn_MindState.angryAt"/>'s own doc for why this is the trimmed stand-in for
    /// RimWorld's manhunter mental state.
    /// </summary>
    public sealed class ThinkNode_ConditionalAngryAtHandler : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) =>
            pawn.mindState.angryAt != null && Find.TickManager.TicksGame < pawn.mindState.angryUntilTick;
    }
}
