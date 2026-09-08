using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Thoughts;
using SimWorld.Work;

namespace SimWorld.God
{
    /// <summary>
    /// A standing civilization-scale directive (<c>docs/spec/simworld-spec.md</c> §10: "the god issues edicts
    /// and goals that enter the think tree above routine work, instead of drafting individuals"). RimWorld has
    /// no direct equivalent — the player there drafts and orders individual colonists — so this is SimWorld's
    /// own translation of "player as overseer" into a def + worker pair, kept in the same declarative shape as
    /// every other def-driven system (<c>Class=</c> polymorphism via <see cref="workerClass"/>,
    /// exactly like <see cref="Work.WorkGiverDef.giverClass"/> and <see cref="Thoughts.ThoughtDef.workerClass"/>).
    /// </summary>
    public class EdictDef : Def
    {
        /// <summary>Work types this edict pushes to the front of routine work while active (see
        /// <c>SimWorld.AI.JobGiver_Edicts</c>) — never by editing <see cref="Pawn_WorkSettings"/> priorities,
        /// only by what the think tree tries first, so deactivating the edict leaves no trace.</summary>
        public List<WorkTypeDef> prioritizedWork = new List<WorkTypeDef>();

        /// <summary>Gates activation: null means every era can issue this edict. The era ladder controls which
        /// edicts a civilization can issue at all (<see cref="GodManager.CanActivate"/>).</summary>
        public EraDef? requiredEra;

        /// <summary>Set as <see cref="Research.ResearchManager.CurrentProj"/> on activation, when not already
        /// finished. Optional: not every edict directs research.</summary>
        public ResearchProjectDef? researchFocus;

        /// <summary>Runtime worker; defaults to the base class, which does only the declarative work described
        /// by this def's own fields. A subclass exists for anything that is not declarative — see
        /// <see cref="EdictWorker_ExemptMinors"/> for the one this pass ships.</summary>
        public Type workerClass = typeof(EdictWorker);

        /// <summary>
        /// The mood consequence of issuing this edict: a situational <see cref="ThoughtDef"/> (its
        /// <see cref="ThoughtDef.workerClass"/> must be <see cref="ThoughtWorker_UnderEdict"/> or another
        /// situational worker that checks this same activation) active on every citizen this edict's
        /// <see cref="EdictWorker.AppliesTo"/> applies to, for as long as the edict stays active. Situational
        /// rather than a granted memory specifically so it disappears on its own the moment
        /// <see cref="GodManager.Deactivate"/> runs — a memory would keep decaying for its own duration after
        /// the edict is gone, which would be a trace the "leaves no trace when deactivated" rule (this
        /// system's think-tree half) exists to rule out on the mood side too.
        /// </summary>
        public ThoughtDef? moodThought;

        private EdictWorker? workerInt;

        /// <summary>Lazily constructed, cached worker instance (same pattern as <see cref="Work.WorkGiverDef.Worker"/>).</summary>
        public EdictWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (EdictWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(EdictWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from EdictWorker.";
            }
            if (moodThought != null && !moodThought.IsSituational)
            {
                yield return "moodThought must be a situational thought (a workerClass, not a duration) so it " +
                    "tracks edict activation automatically instead of needing to be granted and revoked by hand.";
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            workerInt = null;
        }
    }
}
