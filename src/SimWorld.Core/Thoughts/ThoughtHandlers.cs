using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Thoughts
{
    /// <summary>Trait/hediff gating shared by every thought (RimWorld: <c>RimWorld.ThoughtUtility</c>).</summary>
    public static class ThoughtUtility
    {
        public static bool ThoughtNullified(Pawn pawn, ThoughtDef def)
        {
            if (def.nullifyingTraits != null)
            {
                for (int i = 0; i < def.nullifyingTraits.Count; i++)
                {
                    if (pawn.story.traits.HasTrait(def.nullifyingTraits[i])) return true;
                }
            }
            if (def.requiredTraits != null)
            {
                bool any = false;
                for (int i = 0; i < def.requiredTraits.Count; i++)
                {
                    TraitDef trait = def.requiredTraits[i];
                    if (def.requiredTraitsDegree != int.MinValue ? pawn.story.traits.HasTrait(trait, def.requiredTraitsDegree) : pawn.story.traits.HasTrait(trait))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any) return true;
            }
            if (def.nullifyingHediffs != null)
            {
                for (int i = 0; i < def.nullifyingHediffs.Count; i++)
                {
                    if (pawn.HasHediff(def.nullifyingHediffs[i])) return true;
                }
            }
            return false;
        }
    }

    /// <summary>Memories a pawn carries (RimWorld: <c>RimWorld.MemoryThoughtHandler</c>).</summary>
    public class MemoryThoughtHandler : IExposable
    {
        public readonly Pawn pawn;
        private List<Thought_Memory> memories = new List<Thought_Memory>();

        public MemoryThoughtHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public IReadOnlyList<Thought_Memory> Memories => memories;

        public void MemoryThoughtInterval()
        {
            MemoryThoughtIntervalBulk(Needs.Need.IntervalTicks);
        }

        /// <summary>Ages every memory by a whole elapsed span at once — see
        /// <see cref="Thought_Memory.ThoughtIntervalBulk"/>. O(#memories) either way, so a coarse tick costs
        /// the same however long the span is.</summary>
        public void MemoryThoughtIntervalBulk(int elapsedTicks)
        {
            if (elapsedTicks <= 0) return;
            for (int i = 0; i < memories.Count; i++)
            {
                memories[i].ThoughtIntervalBulk(elapsedTicks);
            }
            RemoveExpiredMemories();
        }

        public void RemoveExpiredMemories()
        {
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                if (memories[i].ShouldDiscard) RemoveMemory(memories[i]);
            }
        }

        public Thought_Memory? TryGainMemory(ThoughtDef def, Pawn? otherPawn = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!def.IsMemory) throw new ArgumentException(def.defName + " is not a memory thought.", nameof(def));
            var memory = (Thought_Memory)Activator.CreateInstance(def.ThoughtClass)!;
            memory.def = def;
            memory.otherPawn = otherPawn;
            return TryGainMemory(memory) ? memory : null;
        }

        /// <summary>Returns false when the memory merged into (renewed) an existing one instead.</summary>
        public bool TryGainMemory(Thought_Memory memory)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            memory.pawn = pawn;
            if (ThoughtUtility.ThoughtNullified(pawn, memory.def)) return false;
            if (memory.TryMergeWithExistingMemory(out _)) return false;
            memories.Add(memory);
            EnforceStackLimits(memory.def);
            return true;
        }

        private void EnforceStackLimits(ThoughtDef def)
        {
            while (NumMemoriesOfDef(def) > def.stackLimit)
            {
                Thought_Memory? oldest = OldestMemoryOfDef(def);
                if (oldest == null) break;
                RemoveMemory(oldest);
            }
            if (def.stackLimitForSameOtherPawn >= 0)
            {
                for (int i = memories.Count - 1; i >= 0; i--)
                {
                    Thought_Memory m = memories[i];
                    if (m.def != def || m.otherPawn == null) continue;
                    while (NumMemoriesOfDefWithOtherPawn(def, m.otherPawn) > def.stackLimitForSameOtherPawn)
                    {
                        Thought_Memory? oldest = OldestMemoryOfDefWithOtherPawn(def, m.otherPawn);
                        if (oldest == null) break;
                        RemoveMemory(oldest);
                    }
                }
            }
        }

        public void RemoveMemory(Thought_Memory memory)
        {
            memories.Remove(memory);
        }

        public void RemoveMemoriesOfDef(ThoughtDef def)
        {
            memories.RemoveAll(m => m.def == def);
        }

        public int NumMemoriesOfDef(ThoughtDef def)
        {
            int n = 0;
            for (int i = 0; i < memories.Count; i++) if (memories[i].def == def) n++;
            return n;
        }

        public Thought_Memory? OldestMemoryOfDef(ThoughtDef def)
        {
            Thought_Memory? oldest = null;
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].def == def && (oldest == null || memories[i].age > oldest.age)) oldest = memories[i];
            }
            return oldest;
        }

        public int NumMemoriesInGroup(Thought_Memory group)
        {
            int n = 0;
            for (int i = 0; i < memories.Count; i++) if (memories[i].GroupsWith(group)) n++;
            return n;
        }

        public Thought_Memory? OldestMemoryInGroup(Thought_Memory group)
        {
            Thought_Memory? oldest = null;
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].GroupsWith(group) && (oldest == null || memories[i].age > oldest.age)) oldest = memories[i];
            }
            return oldest;
        }

        private int NumMemoriesOfDefWithOtherPawn(ThoughtDef def, Pawn other)
        {
            int n = 0;
            for (int i = 0; i < memories.Count; i++) if (memories[i].def == def && ReferenceEquals(memories[i].otherPawn, other)) n++;
            return n;
        }

        private Thought_Memory? OldestMemoryOfDefWithOtherPawn(ThoughtDef def, Pawn other)
        {
            Thought_Memory? oldest = null;
            for (int i = 0; i < memories.Count; i++)
            {
                Thought_Memory m = memories[i];
                if (m.def == def && ReferenceEquals(m.otherPawn, other) && (oldest == null || m.age > oldest.age)) oldest = m;
            }
            return oldest;
        }

        public void ExposeData()
        {
            List<Thought_Memory>? list = memories;
            Scribe_Collections.Look(ref list, "memories", LookMode.Deep);
            memories = list ?? new List<Thought_Memory>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                memories.RemoveAll(m => m == null || m.def == null);
                for (int i = 0; i < memories.Count; i++) memories[i].pawn = pawn;
            }
        }
    }

    /// <summary>
    /// Situational thoughts, recomputed at most every 10 ticks from every situational ThoughtDef
    /// (RimWorld: <c>RimWorld.SituationalThoughtHandler</c>).
    /// </summary>
    public class SituationalThoughtHandler
    {
        public const int RecalculateIntervalTicks = 10;

        public readonly Pawn pawn;
        private readonly Dictionary<ThoughtDef, Thought_Situational> cached = new Dictionary<ThoughtDef, Thought_Situational>();
        private int lastRecalculationTick = -99999;

        public SituationalThoughtHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public void AppendMoodThoughts(List<Thought> outThoughts)
        {
            CheckRecalculate();
            foreach (Thought_Situational thought in cached.Values)
            {
                if (thought.Active) outThoughts.Add(thought);
            }
        }

        public void CheckRecalculate()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastRecalculationTick < RecalculateIntervalTicks) return;
            lastRecalculationTick = now;
            Recalculate();
        }

        public void Recalculate()
        {
            foreach (ThoughtDef def in DefDatabase<ThoughtDef>.AllDefsListForReading)
            {
                if (!def.IsSituational) continue;
                if (!cached.TryGetValue(def, out Thought_Situational thought))
                {
                    thought = (Thought_Situational)Activator.CreateInstance(def.ThoughtClass)!;
                    thought.pawn = pawn;
                    thought.def = def;
                    cached[def] = thought;
                }
                thought.RecalculateState();
            }
        }

        /// <summary>Forces the next query to recompute (a need crossed a threshold, a trait changed…).</summary>
        public void Notify_SituationalThoughtsDirty()
        {
            lastRecalculationTick = -99999;
        }
    }

    /// <summary>
    /// All thoughts affecting a pawn's mood (RimWorld: <c>RimWorld.ThoughtHandler</c>). Groups matching thoughts
    /// and applies RimWorld's stacking rule: average offset × (1 + m + m² …) for <c>stackedEffectMultiplier</c> m.
    /// </summary>
    public class ThoughtHandler : IExposable
    {
        public readonly Pawn pawn;
        public MemoryThoughtHandler memories;
        public SituationalThoughtHandler situational;

        private readonly List<Thought> tmpThoughts = new List<Thought>();
        private readonly List<Thought> tmpGroups = new List<Thought>();
        private readonly List<Thought> tmpGroupMembers = new List<Thought>();

        public ThoughtHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            memories = new MemoryThoughtHandler(pawn);
            situational = new SituationalThoughtHandler(pawn);
        }

        public void ThoughtInterval()
        {
            memories.MemoryThoughtInterval();
        }

        /// <summary>One interval's worth of ageing for a whole elapsed span, for the Interval tier's bulk
        /// mood pass (<see cref="Needs.Need_Mood.NeedIntervalBulk"/>).</summary>
        public void ThoughtIntervalBulk(int elapsedTicks)
        {
            memories.MemoryThoughtIntervalBulk(elapsedTicks);
        }

        public void GetAllMoodThoughts(List<Thought> outThoughts)
        {
            outThoughts.Clear();
            for (int i = 0; i < memories.Memories.Count; i++)
            {
                Thought_Memory m = memories.Memories[i];
                if (m.MoodOffset() != 0f) outThoughts.Add(m);
            }
            situational.AppendMoodThoughts(outThoughts);
        }

        /// <summary>One representative thought per group, in first-seen order.</summary>
        public void GetDistinctMoodThoughtGroups(List<Thought> outGroups)
        {
            outGroups.Clear();
            GetAllMoodThoughts(tmpThoughts);
            for (int i = 0; i < tmpThoughts.Count; i++)
            {
                Thought t = tmpThoughts[i];
                bool grouped = false;
                for (int j = 0; j < outGroups.Count; j++)
                {
                    if (outGroups[j].GroupsWith(t)) { grouped = true; break; }
                }
                if (!grouped) outGroups.Add(t);
            }
        }

        public void GetMoodThoughts(Thought group, List<Thought> outMembers)
        {
            outMembers.Clear();
            GetAllMoodThoughts(tmpThoughts);
            for (int i = 0; i < tmpThoughts.Count; i++)
            {
                if (tmpThoughts[i].GroupsWith(group)) outMembers.Add(tmpThoughts[i]);
            }
        }

        public float MoodOffsetOfGroup(Thought group)
        {
            GetMoodThoughts(group, tmpGroupMembers);
            if (tmpGroupMembers.Count == 0) return 0f;
            float sum = 0f;
            float weight = 1f;
            float weightSum = 0f;
            for (int i = 0; i < tmpGroupMembers.Count; i++)
            {
                sum += tmpGroupMembers[i].MoodOffset();
                weightSum += weight;
                weight *= tmpGroupMembers[i].def.stackedEffectMultiplier;
            }
            float average = sum / tmpGroupMembers.Count;
            return average * weightSum;
        }

        public float TotalMoodOffset()
        {
            GetDistinctMoodThoughtGroups(tmpGroups);
            float total = 0f;
            for (int i = 0; i < tmpGroups.Count; i++)
            {
                total += MoodOffsetOfGroup(tmpGroups[i]);
            }
            return total;
        }

        public void ExposeData()
        {
            MemoryThoughtHandler? m = memories;
            Scribe_Deep.Look(ref m, "memories", pawn);
            memories = m ?? new MemoryThoughtHandler(pawn);
        }
    }
}
