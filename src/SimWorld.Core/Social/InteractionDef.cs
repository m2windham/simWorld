using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Social
{
    /// <summary>
    /// A kind of social interaction (RimWorld: <c>RimWorld.InteractionDef</c>): chitchat, deep talk, insult,
    /// slight. <see cref="SocialInteractionManager"/> picks one per attempted interaction by weight (see
    /// <see cref="InteractionWorker.RandomSelectionWeight"/>), gated on opinion and mood by the worker itself
    /// rather than by anything on this Def, matching RimWorld's own split (the Def just names the interaction
    /// and its worker; the worker holds the logic).
    /// </summary>
    public class InteractionDef : Def
    {
        public Type workerClass = typeof(InteractionWorker);

        /// <summary>
        /// Chance this interaction tips its recipient into a social fight, before
        /// <see cref="SocialFightUtility.SocialFightChance"/>'s chain of factors (RimWorld:
        /// <c>InteractionDef.socialFightBaseChance</c>, read by <c>Pawn_InteractionsTracker.SocialFightChance</c>).
        /// 0 for every interaction that cannot start a fight. Insult 4% and Slight 0.5% in content; both
        /// figures are the RimWorld wiki's (Social fight: "Every insult has a 4 % chance of starting social
        /// fight, slights 0.5 %"), a secondary source rather than the def itself.
        /// </summary>
        public float socialFightBaseChance;

        private InteractionWorker? workerInt;

        public InteractionWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (InteractionWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(InteractionWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from InteractionWorker.";
            }
        }
    }

    /// <summary>
    /// Selects and executes one <see cref="InteractionDef"/> (RimWorld: <c>RimWorld.InteractionWorker</c>).
    /// <see cref="RandomSelectionWeight"/> both gates eligibility (0 = never selectable right now) and weighs
    /// it against every other interaction's weight for the same pair; <see cref="Interacted"/> applies the
    /// social thoughts (and, for a fight-eligible interaction, rolls the escalation) once chosen.
    /// </summary>
    public abstract class InteractionWorker
    {
        public InteractionDef def = null!;

        public virtual float RandomSelectionWeight(Pawn initiator, Pawn recipient) => 1f;

        public abstract void Interacted(Pawn initiator, Pawn recipient);

        /// <summary>Gives <paramref name="def"/> as a social memory to both pawns, each about the other — the
        /// shared shape behind chitchat and deep talk (both come away with a memory of the exchange).</summary>
        protected static void GainMemoryBothWays(Pawn initiator, Pawn recipient, Thoughts.ThoughtDef memoryDef)
        {
            initiator.needs.mood?.thoughts.memories.TryGainMemory(memoryDef, recipient);
            recipient.needs.mood?.thoughts.memories.TryGainMemory(memoryDef, initiator);
        }
    }
}
