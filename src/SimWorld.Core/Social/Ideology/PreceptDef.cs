using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// One specific rule an ideoligion holds — SimWorld's port of RimWorld's <c>RimWorld.PreceptDef</c>, the
    /// "precepts are the specific rules" half of this module's meme/precept split (see <see cref="MemeDef"/>'s
    /// own doc for the other half). Reaches a citizen's mood the same way an <see cref="God.EdictDef"/> reaches
    /// theirs — a situational <see cref="ThoughtDef"/> (<see cref="moodThought"/>) whose worker is
    /// <see cref="ThoughtWorker_UnderPrecept"/>, the precedent named directly in this module's own brief
    /// alongside <see cref="God.ThoughtWorker_UnderEdict"/>: "a worker reading state and producing a
    /// situational thought". <see cref="workerClass"/> mirrors <see cref="God.EdictDef.workerClass"/>'s own
    /// Class= polymorphism seam — the base <see cref="PreceptWorker"/> is unconditionally active for every
    /// member (a flavour/belonging precept), and content picks a subclass for anything conditional on the
    /// citizen's own state (see <see cref="PreceptWorkers"/>).
    /// </summary>
    public class PreceptDef : Def
    {
        /// <summary>Free-form topic tag, matched by plain string equality against a <see cref="MemeDef"/>'s
        /// own <see cref="MemeDef.preceptSlots"/> — see <see cref="MemeDef"/>'s doc for why this is a string
        /// rather than a dedicated Def type.</summary>
        public string issue = "";

        /// <summary>The mood consequence of this precept: a situational <see cref="ThoughtDef"/> (its
        /// <see cref="ThoughtDef.workerClass"/> must be <see cref="ThoughtWorker_UnderPrecept"/>) active on
        /// every citizen this precept's <see cref="Worker"/> applies to, for as long as the owning
        /// <see cref="Ideo"/> holds this precept and the worker's own condition is met. Optional — a precept
        /// that only grants a role (<see cref="grantsRole"/>) or unlocks a ritual
        /// (<see cref="unlocksRitual"/>) need not move mood on its own.</summary>
        public ThoughtDef? moodThought;

        /// <summary>Runtime compliance rule; defaults to the base class, which is unconditionally active for
        /// every applicable citizen (see <see cref="PreceptWorker"/>'s own doc). Subclass for anything
        /// conditional on the citizen's own state (a trait held, apparel worn, a role granted) — see
        /// <see cref="PreceptWorkers"/> for the ones this pass ships.</summary>
        public Type workerClass = typeof(PreceptWorker);

        /// <summary>Trait <see cref="PreceptWorkers.PreceptWorker_Trait"/> checks for. Ignored by every other
        /// worker.</summary>
        public TraitDef? trait;

        /// <summary>The ideoligion role this precept grants a position for (RimWorld:
        /// <c>PreceptDef.role</c>) — who actually holds it is a runtime choice
        /// (<see cref="Ideo.TryAssignRole"/>), not decided by this Def. Optional: most precepts grant no role.</summary>
        public IdeoRoleDef? grantsRole;

        /// <summary>The ritual this precept makes available to an ideoligion that holds it. Optional and
        /// purely informational at the Def level — nothing gates performing a <see cref="RitualDef"/> on this
        /// field today (see that Def's own doc); it records *why* the ritual exists in this ideoligion's
        /// content.</summary>
        public RitualDef? unlocksRitual;

        private PreceptWorker? workerInt;

        /// <summary>Lazily constructed, cached worker instance (same pattern as <see cref="God.EdictDef.Worker"/>).</summary>
        public PreceptWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (PreceptWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (issue.Length == 0) yield return "issue is empty.";
            if (!typeof(PreceptWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from PreceptWorker.";
            }
            if (moodThought != null && !moodThought.IsSituational)
            {
                yield return "moodThought must be a situational thought (a workerClass, not a durationDays) so " +
                    "it tracks this precept's membership in the active Ideo automatically instead of needing to " +
                    "be granted and revoked by hand.";
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            workerInt = null;
        }
    }
}
