using System;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Map
{
    /// <summary>
    /// One mark on the map: a <see cref="DesignationDef"/> aimed at a cell or a Thing (RimWorld:
    /// <c>Verse.Designation</c>). The record of intent a citizen acts on — <c>AI.WorkGiver_Miner</c> offers
    /// only cells that carry a <see cref="DesignationDefOf.Mine"/> one — kept on the map and saved with it, so
    /// a mark outlives the tick it was made on and is visible to anything that asks
    /// (<see cref="DesignationManager"/>).
    ///
    /// <para/>Ported minus the drawing (<c>DesignationDraw</c>, <c>DesignationDrawAltitude</c> — the host
    /// draws from a <c>defName</c>) and minus <c>Notify_Added</c>/<c>Notify_Removing</c>, whose only body in
    /// RimWorld is the haul designation's bookkeeping with <c>listerHaulables</c>, which this port has no
    /// counterpart of. Both come back with the first designation kind that needs one.
    /// </summary>
    public class Designation : IExposable
    {
        public DesignationManager? designationManager;

        public DesignationDef def = null!;

        public LocalTargetInfo target;

        /// <summary>For Scribe: <see cref="ExposeData"/> fills every field.</summary>
        public Designation()
        {
        }

        public Designation(LocalTargetInfo target, DesignationDef def)
        {
            this.target = target;
            this.def = def ?? throw new ArgumentNullException(nameof(def));
        }

        public void ExposeData()
        {
            DesignationDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Targets.Look(ref target, "target");
        }

        /// <summary>Takes this mark off its map (RimWorld: <c>Designation.Delete</c>). A mark not on a map
        /// has nothing to be taken off.</summary>
        public void Delete() => designationManager?.RemoveDesignation(this);

        public override string ToString() => "(" + def?.defName + " target=" + target + ")";
    }
}
