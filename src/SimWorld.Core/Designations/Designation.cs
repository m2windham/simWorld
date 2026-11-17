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

        /// <summary>For Scribe.</summary>
        public Designation()
        {
        }

        public Designation(LocalTargetInfo target, DesignationDef def)
        {
            this.target = target;
            this.def = def;
        }

        public void ExposeData()
        {
            DesignationDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!; // a def that did not load leaves this null, and DesignationManager drops it at PostLoadInit
            Scribe_Targets.Look(ref target, "target");
        }

        public override string ToString() => (def?.defName ?? "?") + " at " + target;
    }
}
