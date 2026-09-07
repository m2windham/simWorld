using System;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Thoughts
{
    /// <summary>An active thought on a pawn (RimWorld: <c>RimWorld.Thought</c>).</summary>
    public abstract class Thought : IExposable
    {
        public Pawn pawn = null!;
        public ThoughtDef def = null!;

        public abstract int CurStageIndex { get; }

        public ThoughtStage CurStage => def.stages[Math.Min(Math.Max(CurStageIndex, 0), def.stages.Count - 1)];

        public virtual bool VisibleInNeedsTab => CurStage.visible;

        public string LabelCap => CurStage.label != null ? CurStage.LabelCap : def.LabelCap;

        public virtual float BaseMoodOffset => CurStage.baseMoodEffect;

        public virtual float MoodOffset()
        {
            if (def.stages.Count == 0) return 0f;
            if (ThoughtUtility.ThoughtNullified(pawn, def)) return 0f;
            return BaseMoodOffset;
        }

        /// <summary>How this thought's current stage moves <see cref="pawn"/>'s opinion of <see cref="Thought_Memory.otherPawn"/>
        /// (RimWorld: <c>Thought.OpinionOffset</c>). Only memory thoughts carry an <c>otherPawn</c>, so only
        /// they are meaningful inputs to <see cref="Social.SocialUtility.OpinionOf"/>; a situational thought's
        /// offset is never read there.</summary>
        public virtual float BaseOpinionOffset => CurStage.baseOpinionOffset;

        public virtual float OpinionOffset()
        {
            if (def.stages.Count == 0) return 0f;
            if (ThoughtUtility.ThoughtNullified(pawn, def)) return 0f;
            return BaseOpinionOffset;
        }

        /// <summary>Thoughts that group together stack in the mood total.</summary>
        public virtual bool GroupsWith(Thought other) => other != null && def == other.def;

        public virtual void ExposeData()
        {
            ThoughtDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
        }

        public override string ToString() => "(" + (def?.defName ?? "?") + ")";
    }

    /// <summary>
    /// A thought that happened and fades (RimWorld: <c>RimWorld.Thought_Memory</c>). Ages 150 ticks per
    /// interval and is discarded after the Def's duration; adding one at the stack limit renews the oldest.
    /// </summary>
    public class Thought_Memory : Thought
    {
        public float moodPowerFactor = 1f;
        public int age;
        public Pawn? otherPawn;
        public int forcedStage = -1;

        public override int CurStageIndex => forcedStage >= 0 ? forcedStage : 0;

        public virtual bool ShouldDiscard => age > def.DurationTicks;

        public override float MoodOffset() => base.MoodOffset() * moodPowerFactor;

        public override float OpinionOffset() => base.OpinionOffset() * moodPowerFactor;

        public virtual void ThoughtInterval()
        {
            age += Need.IntervalTicks;
        }

        public void Renew()
        {
            age = 0;
        }

        public override bool GroupsWith(Thought other) => base.GroupsWith(other) && (other is Thought_Memory m) && ReferenceEquals(otherPawn, m.otherPawn);

        /// <summary>At the stack limit the oldest matching memory is renewed instead of adding another.</summary>
        public virtual bool TryMergeWithExistingMemory(out bool showBubble)
        {
            MemoryThoughtHandler memories = pawn.needs.mood!.thoughts.memories;
            if (memories.NumMemoriesInGroup(this) >= def.stackLimit)
            {
                Thought_Memory? oldest = memories.OldestMemoryInGroup(this);
                if (oldest != null)
                {
                    showBubble = oldest.age > oldest.def.DurationTicks / 2;
                    oldest.Renew();
                    return true;
                }
            }
            showBubble = true;
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref age, "age");
            Scribe_Values.Look(ref moodPowerFactor, "moodPowerFactor", 1f);
            Scribe_Values.Look(ref forcedStage, "forcedStage", -1);
            Scribe_References.Look(ref otherPawn, "otherPawn");
        }
    }

    /// <summary>A thought that is true of the pawn's situation right now (RimWorld: <c>RimWorld.Thought_Situational</c>).</summary>
    public class Thought_Situational : Thought
    {
        private int curStageIndex;
        private bool active;
        public string? reason;

        public override int CurStageIndex => curStageIndex;

        public bool Active => active;

        public void RecalculateState()
        {
            ThoughtState state = def.Worker!.CurrentState(pawn);
            active = state.Active;
            if (active)
            {
                curStageIndex = Math.Min(Math.Max(state.StageIndex, 0), def.stages.Count - 1);
                reason = state.Reason;
            }
        }
    }
}
