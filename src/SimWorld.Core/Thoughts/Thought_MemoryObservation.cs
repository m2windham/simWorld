using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// A memory of having seen a particular Thing (RimWorld: <c>RimWorld.Thought_MemoryObservation</c>): the
    /// body lying by the well, as distinct from "a body". Seeing the <i>same</i> Thing again renews the memory
    /// that is already held instead of adding another, so a pawn who walks past one corpse all day carries one
    /// thought about it, kept fresh; seeing a <i>different</i> one adds a second, and the two stack in the
    /// mood the way any group of like thoughts does.
    /// <para/>
    /// <b>Stacking, as RimWorld has it.</b> This class overrides <see cref="Thought_Memory.TryMergeWithExistingMemory"/>
    /// completely, so the base class's "at the stack limit, renew the oldest" rule does not apply to it: what
    /// merges is decided by the target and nothing else. The shipped defs set a stack limit high enough that
    /// the handler's own cap (<see cref="MemoryThoughtHandler.TryGainMemory(Thought_Memory)"/>) never binds
    /// either; see <c>Thoughts_Observation.xml</c>.
    /// </summary>
    public class Thought_MemoryObservation : Thought_Memory
    {
        private int targetThingID;

        /// <summary>What was seen. Only its id is kept (RimWorld keeps the same): the Thing may be destroyed
        /// or carried off before the memory fades, and the memory is the pawn's, not the Thing's.</summary>
        public Thing Target
        {
            set => targetThingID = value.thingIDNumber;
        }

        public int TargetThingID => targetThingID;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetThingID, "targetThingID", 0);
        }

        public override bool TryMergeWithExistingMemory(out bool showBubble)
        {
            MemoryThoughtHandler memories = pawn.needs.mood!.thoughts.memories;
            Thought_MemoryObservation? existing = null;
            for (int i = 0; i < memories.Memories.Count; i++)
            {
                if (memories.Memories[i] is Thought_MemoryObservation other
                    && other.def == def
                    && other.targetThingID == targetThingID
                    && (existing == null || other.age > existing.age))
                {
                    existing = other;
                }
            }

            if (existing != null)
            {
                showBubble = existing.age > existing.def.DurationTicks / 2;
                existing.Renew();
                return true;
            }
            showBubble = true;
            return false;
        }
    }
}
