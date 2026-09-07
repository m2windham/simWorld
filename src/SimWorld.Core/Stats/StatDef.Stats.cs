using System;
using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>
    /// Pipeline half of <see cref="StatDef"/>: everything <see cref="Stats.StatWorker"/> needs to resolve a
    /// value. Kept in its own partial file so the Defs-layer file stays Def-only concerns — see
    /// <c>Things/ThingDef.Things.cs</c> for the identical split on <see cref="ThingDef"/>.
    /// </summary>
    public partial class StatDef
    {
        /// <summary>Computes this stat's value; defaults to the base pipeline (RimWorld: <c>StatDef.workerClass</c>).</summary>
        public Type workerClass = typeof(Stats.StatWorker);

        /// <summary>Extra transforms applied after factors, before the post-process curve (RimWorld: <c>StatDef.parts</c>).</summary>
        public List<Stats.StatPart>? parts;

        /// <summary>How much a pawn's health capacities bend the final value — e.g. RestRateMultiplier's
        /// BloodPumping/Metabolism/Breathing (RimWorld: <c>StatDef.capacityFactors</c>).</summary>
        public List<Stats.PawnCapacityFactor>? capacityFactors;

        /// <summary>Display grouping for a (future) stats UI (RimWorld: <c>StatDef.category</c>).</summary>
        public Stats.StatCategoryDef? category;

        /// <summary>Show this stat even when a def sets no <c>statBases</c> entry for it (RimWorld: <c>StatDef.showIfUndefined</c>).</summary>
        public bool showIfUndefined = true;

        private Stats.StatWorker? workerInt;

        /// <summary>Lazily constructed and cached, one per StatDef (RimWorld: <c>StatDef.Worker</c>).</summary>
        public Stats.StatWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (Stats.StatWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.stat = this;
                }
                return workerInt;
            }
        }
    }
}
