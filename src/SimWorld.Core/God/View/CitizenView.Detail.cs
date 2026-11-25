using System;
using System.Collections.Generic;

using SimWorld.Health;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.God.View
{
    /// <summary>
    /// One reason for somebody's mood — RimWorld's needs-tab mood list, one row per thought group: "Ate without
    /// table −3", "Slept outside −4 x2". The answer to the break-risk tooltip's "find out what is weighing on
    /// them".
    /// </summary>
    public sealed class ThoughtLine
    {
        internal ThoughtLine(string defName, string label, string description, float moodOffset, int count,
            bool isMemory, int? ticksLeft, string? reason)
        {
            DefName = defName;
            Label = label;
            Description = description;
            MoodOffset = moodOffset;
            Count = count;
            IsMemory = isMemory;
            TicksLeft = ticksLeft;
            Reason = reason;
        }

        public string DefName { get; }

        /// <summary>The thought's current stage label, in words ("ate without table").</summary>
        public string Label { get; }

        /// <summary>The stage's (or the def's) description; empty when content gave none.</summary>
        public string Description { get; }

        /// <summary>What the whole group adds to mood, in mood points out of 100 — after RimWorld's stacking
        /// rule, so the rows sum to the thoughts' share of the mood target. Negative is a burden.</summary>
        public float MoodOffset { get; }

        /// <summary>How many thoughts are stacked in this row (the "x2"). 1 for most.</summary>
        public int Count { get; }

        /// <summary>True for something that happened and fades; false for something true of their situation
        /// right now, which lasts as long as the situation does.</summary>
        public bool IsMemory { get; }

        /// <summary>For a memory, ticks until the newest one in the row fades; null for a situational
        /// thought.</summary>
        public int? TicksLeft { get; }

        /// <summary>The situation's own explanation where its worker gave one; usually null.</summary>
        public string? Reason { get; }
    }

    public sealed partial class HediffLine
    {
        /// <summary>Whether it can be and wants tending right now (<c>Hediff.TendableNow</c>): an untended
        /// wound, an illness due its next treatment.</summary>
        public bool NeedsTendingNow { get; private set; }

        /// <summary>How far immunity has got against it, 0-1, for an illness that races immunity (flu, plague,
        /// a wound infection); null for anything else. Severity reaching <see cref="LethalSeverity"/> before
        /// this reaches 1 is a death.</summary>
        public float? Immunity { get; private set; }

        /// <summary>The severity at which it kills, or null when it never does on its own.</summary>
        public float? LethalSeverity { get; private set; }

        /// <summary>False for something good or neutral to have (an implant, a high).</summary>
        public bool IsBad { get; private set; }

        internal void FillDetail(Hediff h)
        {
            NeedsTendingNow = h.TendableNow();
            HediffComp_Immunizable? immunizable = h.TryGetComp<HediffComp_Immunizable>();
            Immunity = immunizable?.Immunity;
            LethalSeverity = h.def.lethalSeverity > 0f ? h.def.lethalSeverity : (float?)null;
            IsBad = h.def.isBad;
        }
    }

    public sealed partial class CitizenLine
    {
        /// <summary>The current job's label in words ("cut plant"), beside <see cref="CurrentJobDefName"/>;
        /// null when they have no job or are not on a map. Show this, never the defName.</summary>
        public string? CurrentJobLabel { get; internal set; }

        /// <summary>A job def's label for the player, falling back to its defName only when content gave it
        /// none (the content tests require a label on every job the seam orders).</summary>
        internal static string? JobLabelOf(AI.JobDef? job) => job == null ? null : job.label ?? job.defName;
    }

    /// <summary>
    /// The half of a citizen the playtest found missing: why their mood is where it is, what they are doing,
    /// and how close they are to breaking. Filled by <see cref="CitizenView.Of"/>; a remembered view (no live
    /// record) leaves all of it empty or null with <see cref="ThoughtsFidelity"/> NotTracked.
    /// </summary>
    public sealed partial class CitizenView
    {
        /// <summary>
        /// Every reason for their mood, one row per thought group, worst first — <b>or an empty list meaning
        /// nobody is tracking their thoughts at this tier</b>; <see cref="ThoughtsFidelity"/> tells the two
        /// apart. At Statistical the mood is drawn from the cohort and no thought explains it, so none is
        /// reported, the same rule <see cref="Hediffs"/> follows.
        ///
        /// <para/>Read as last computed. A situational thought is recomputed by the simulation every ten ticks;
        /// looking does not force that, because a view that refreshed the cache would move the simulation's own
        /// clock (<c>ThoughtHandler.MoodThoughtGroupsAsComputed</c>).
        /// </summary>
        public IReadOnlyList<ThoughtLine> Thoughts { get; private set; } = Array.Empty<ThoughtLine>();

        public CitizenFidelity ThoughtsFidelity { get; private set; } = CitizenFidelity.NotTracked;

        /// <summary>The rows of <see cref="Thoughts"/> summed, in mood points — the thoughts' whole pull on the
        /// mood target, which is 50 plus this plus the difficulty's flat offset.</summary>
        public float ThoughtsMoodOffset { get; private set; }

        /// <summary>The mood level (0-1, the same scale as <see cref="MoodLevel"/>) below which a minor break
        /// becomes possible; null with no mood. <see cref="MoodBand"/> is which side of these they are on.</summary>
        public float? BreakThresholdMinor { get; private set; }

        public float? BreakThresholdMajor { get; private set; }

        public float? BreakThresholdExtreme { get; private set; }

        /// <summary>What they are doing, in words — the same reading <see cref="CitizenLine.ActivityLabel"/>
        /// gives.</summary>
        public string? ActivityLabel { get; private set; }

        public string? CurrentJobDefName { get; private set; }

        /// <summary>See <see cref="CitizenLine.CurrentJobLabel"/>.</summary>
        public string? CurrentJobLabel { get; private set; }

        public string? MentalStateDefName { get; private set; }

        /// <summary>The mental state they are in, in words ("berserk"), or null.</summary>
        public string? MentalStateLabel { get; private set; }

        /// <summary>Their role's label (<see cref="RoleDefName"/> in words), or null with no role.</summary>
        public string? RoleLabel { get; private set; }

        /// <summary>Blood lost per day across every wound (<c>HediffSet.BleedRateTotal</c>); 0 when not
        /// bleeding or when <see cref="HediffFidelity"/> is NotTracked.</summary>
        public float BleedRateTotal { get; private set; }

        /// <summary>Whether any of <see cref="Hediffs"/> wants tending now.</summary>
        public bool NeedsTending { get; private set; }

        /// <summary>What they carry and wear (<see cref="Map.View.GearView"/>): the weapon first, then the apparel
        /// in the order worn, each with its label ("bronze spear"), stuff, era and wear. Empty for someone with
        /// nothing, and for a person who is not on a map, whose kit nobody is tracking.</summary>
        public IReadOnlyList<Map.View.GearView> Gear { get; private set; } = Array.Empty<Map.View.GearView>();

        internal void FillDetail(Pawn pawn, PawnTier tier, bool hediffsReportable)
        {
            ActivityLabel = CitizenLine.ActivityOf(pawn);
            AI.JobDef? job = pawn.Spawned ? pawn.jobs?.curJob?.def : null;
            CurrentJobDefName = job?.defName;
            CurrentJobLabel = CitizenLine.JobLabelOf(job);
            MentalStateDef? mental = pawn.MentalStateDef;
            MentalStateDefName = mental?.defName;
            MentalStateLabel = mental?.label ?? mental?.defName;
            RoleLabel = pawn.workSettings?.Role?.LabelCap;
            Gear = Map.View.GearView.Of(pawn);

            Need_Mood? mood = pawn.needs?.mood;
            MentalBreaker? breaker = pawn.mindState?.mentalBreaker;
            if (mood != null && breaker != null)
            {
                BreakThresholdMinor = breaker.BreakThresholdMinor;
                BreakThresholdMajor = breaker.BreakThresholdMajor;
                BreakThresholdExtreme = breaker.BreakThresholdExtreme;
            }

            if (mood != null && tier != PawnTier.Statistical)
            {
                Thoughts = ThoughtsOf(mood.thoughts, out float total);
                ThoughtsMoodOffset = total;
                ThoughtsFidelity = CitizenLine.FidelityOfLiveState(tier);
            }

            if (hediffsReportable && pawn.health?.hediffSet != null)
            {
                BleedRateTotal = pawn.health.hediffSet.BleedRateTotal;
                for (int i = 0; i < Hediffs.Count; i++) NeedsTending |= Hediffs[i].NeedsTendingNow;
            }
        }

        private static List<ThoughtLine> ThoughtsOf(ThoughtHandler handler, out float total)
        {
            total = 0f;
            List<List<Thought>> groups = handler.MoodThoughtGroupsAsComputed();
            var lines = new List<ThoughtLine>(groups.Count);
            for (int g = 0; g < groups.Count; g++)
            {
                List<Thought> members = groups[g];
                Thought head = members[0];
                float offset = ThoughtHandler.StackedMoodOffset(members);
                total += offset;

                int? ticksLeft = null;
                if (head is Thought_Memory)
                {
                    int youngest = int.MaxValue;
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (members[i] is Thought_Memory m && m.age < youngest) youngest = m.age;
                    }
                    ticksLeft = Math.Max(0, head.def.DurationTicks - youngest);
                }

                lines.Add(new ThoughtLine(
                    head.def.defName,
                    head.LabelCap,
                    head.CurStage.description ?? head.def.description ?? "",
                    offset,
                    members.Count,
                    head is Thought_Memory,
                    ticksLeft,
                    (head as Thought_Situational)?.reason));
            }

            // Worst first: the rows a player opens this for are the ones dragging the mood down. A stable sort,
            // so equal offsets keep the order the simulation holds them in.
            var ordered = new List<ThoughtLine>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                int at = ordered.Count;
                while (at > 0 && ordered[at - 1].MoodOffset > lines[i].MoodOffset) at--;
                ordered.Insert(at, lines[i]);
            }
            return ordered;
        }
    }
}
