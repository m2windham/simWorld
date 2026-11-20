using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Designations
{
    /// <summary>One mark on the map asking for work on a cell or a thing (RimWorld: <c>Verse.Designation</c>).</summary>
    public class Designation : IExposable
    {
        public DesignationManager designationManager = null!;
        public DesignationDef def = null!;
        public LocalTargetInfo target;

        /// <summary>Who placed it — see <see cref="DesignationSource"/>. Not in RimWorld, where only the player
        /// designates.</summary>
        public DesignationSource source;

        /// <summary>For Scribe.</summary>
        public Designation()
        {
        }

        public Designation(LocalTargetInfo target, DesignationDef def, DesignationSource source = DesignationSource.Settlement)
        {
            this.target = target;
            this.def = def;
            this.source = source;
        }

        public void ExposeData()
        {
            DesignationDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!; // a def that did not load leaves this null, and DesignationManager drops it at PostLoadInit
            Scribe_Targets.Look(ref target, "target");
            // Defaulted, so a settlement's mark writes exactly what it wrote before this field existed.
            Scribe_Values.Look(ref source, "source", DesignationSource.Settlement);
        }

        public override string ToString() => (def?.defName ?? "?") + " at " + target;
    }
}
