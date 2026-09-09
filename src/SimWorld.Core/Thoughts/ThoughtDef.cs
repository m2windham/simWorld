using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;

namespace SimWorld.Thoughts
{
    /// <summary>One stage of a thought: its text and mood effect (RimWorld: <c>RimWorld.ThoughtStage</c>).</summary>
    public class ThoughtStage
    {
        public string? label;
        public string? description;
        public float baseMoodEffect;
        public float baseOpinionOffset;
        public bool visible = true;

        public string LabelCap => label == null || label.Length == 0 ? "" : char.ToUpperInvariant(label[0]) + label.Substring(1);
    }

    /// <summary>
    /// A thought a pawn can hold (RimWorld: <c>RimWorld.ThoughtDef</c>). Memories have a duration and stack;
    /// situational thoughts are recomputed by a <see cref="ThoughtWorker"/>.
    /// </summary>
    public class ThoughtDef : Def
    {
        public Type? thoughtClass;
        public Type? workerClass;
        public List<ThoughtStage> stages = new List<ThoughtStage>();
        public float durationDays;
        public int stackLimit = 1;
        public float stackedEffectMultiplier = 0.75f;
        public int stackLimitForSameOtherPawn = -1;
        public bool showBubble;
        public List<TraitDef>? nullifyingTraits;
        public List<TraitDef>? requiredTraits;
        public int requiredTraitsDegree = int.MinValue;

        /// <summary>Hediffs that suppress this thought.</summary>
        public List<HediffDef>? nullifyingHediffs;

        /// <summary>For <see cref="ThoughtWorker_Hediff"/>: the hediff whose stage drives this thought.</summary>
        public HediffDef? hediff;

        /// <summary>For <see cref="ThoughtWorker_HasDirectRelation"/>: active while the pawn has at least one
        /// <see cref="Social.DirectPawnRelation"/> of this kind with anyone (RimWorld's own spatial "a friend
        /// is nearby"/"a rival is present" situational thoughts, translated — no Map/room concept reaches this
        /// module yet, see <c>Social.SocialInteractionManager</c>'s own note, so "present" reads as "exists in
        /// this pawn's relations" rather than "in the same room"). Content: <c>HasFriend</c>/<c>HasRival</c>
        /// in <c>Data/Core/Defs/ThoughtDefs/Thoughts_Social.xml</c>.</summary>
        public PawnRelationDef? requiredDirectRelation;

        private ThoughtWorker? workerInt;

        public int DurationTicks => (int)(durationDays * GenDate.TicksPerDay);

        public bool IsMemory => durationDays > 0f || (thoughtClass != null && typeof(Thought_Memory).IsAssignableFrom(thoughtClass));

        public bool IsSituational => Worker != null;

        /// <summary>
        /// Whether this Def's memories carry an opinion effect on the specific <see
        /// cref="Thought_Memory.otherPawn"/> they're about (RimWorld: <c>ThoughtDef.IsSocial</c> — there, a
        /// distinct <c>Thought_MemorySocial</c> subclass marks it; this port folds "social" and "mood-only"
        /// memories into one <see cref="Thought_Memory"/> class with a nullable <c>otherPawn</c> instead, so
        /// social-ness reads off the stages' own numbers rather than off the runtime type). True when any
        /// stage moves opinion at all; the only reader of the per-instance, per-current-stage <see
        /// cref="Thought.OpinionOffset"/> this property summarizes is <see
        /// cref="Social.SocialUtility.SocialMemoryOpinionOffset"/>.
        /// </summary>
        public bool IsSocial => stages.Exists(s => s.baseOpinionOffset != 0f);

        public Type ThoughtClass => thoughtClass ?? (IsMemory ? typeof(Thought_Memory) : typeof(Thought_Situational));

        public ThoughtWorker? Worker
        {
            get
            {
                if (workerInt == null && workerClass != null)
                {
                    workerInt = (ThoughtWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (stages.Count == 0) yield return "thought has no stages.";
            if (stackLimit < 1) yield return "stackLimit must be at least 1.";
            if (thoughtClass != null && !typeof(Thought).IsAssignableFrom(thoughtClass)) yield return "thoughtClass must derive from Thought.";
            if (workerClass != null && !typeof(ThoughtWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from ThoughtWorker.";
            if (!IsMemory && workerClass == null) yield return "a thought needs either a durationDays (memory) or a workerClass (situational).";
            if (IsMemory && workerClass != null) yield return "a thought cannot be both a memory and situational.";
        }
    }

    /// <summary>Result of evaluating a situational thought (RimWorld: <c>RimWorld.ThoughtState</c>).</summary>
    public readonly struct ThoughtState
    {
        public bool Active { get; }
        public int StageIndex { get; }
        public string? Reason { get; }

        private ThoughtState(bool active, int stageIndex, string? reason)
        {
            Active = active;
            StageIndex = stageIndex;
            Reason = reason;
        }

        public static ThoughtState Inactive => new ThoughtState(false, -1, null);

        public static ThoughtState ActiveDefault => new ThoughtState(true, 0, null);

        public static ThoughtState ActiveAtStage(int stageIndex, string? reason = null) => new ThoughtState(true, stageIndex, reason);

        public static implicit operator ThoughtState(bool active) => active ? ActiveDefault : Inactive;
    }

    /// <summary>Decides whether a situational thought applies and at which stage (RimWorld: <c>RimWorld.ThoughtWorker</c>).</summary>
    public abstract class ThoughtWorker
    {
        public ThoughtDef def = null!;

        public ThoughtState CurrentState(Pawn pawn)
        {
            ThoughtState state = CurrentStateInternal(pawn);
            if (state.Active && ThoughtUtility.ThoughtNullified(pawn, def))
            {
                return ThoughtState.Inactive;
            }
            return state;
        }

        protected abstract ThoughtState CurrentStateInternal(Pawn pawn);
    }
}
