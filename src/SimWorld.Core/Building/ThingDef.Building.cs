namespace SimWorld.Defs
{
    /// <summary>
    /// Building-layer half of <see cref="ThingDef"/> (system 16: Building) — the fields <see cref="Building.Blueprint"/>,
    /// <see cref="Building.Frame"/> and <see cref="Building.GenConstruct"/> read. Kept in its own partial file,
    /// matching the split <c>ThingDef.Things.cs</c>/<c>ThingDef.Research.cs</c> already use.
    /// <b>Note:</b> a construction's material cost reuses the Crafting module's own <see cref="ThingDef.costList"/>
    /// field directly — real RimWorld's <c>ThingDef.costList</c> is the one list both a workbench recipe's
    /// "made from stuff" cost and a building's construction cost read, so this pass does not redeclare it.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>
        /// The buildable Def a Blueprint/Frame Def stands in for (RimWorld: auto-generated per-Def blueprint
        /// and frame Defs carry this as <c>BuildableDef entityDefToBuild</c>). <b>Deviation:</b> RimWorld
        /// generates one Blueprint/Frame ThingDef per buildable Def at startup
        /// (<c>ThingDefGenerator_Buildings</c>) so each carries the right graphic; this port has no graphics
        /// layer to generate for, so this pass hand-authors one <c>Blueprint_&lt;X&gt;</c>/<c>Frame_&lt;X&gt;</c>
        /// pair per buildable Def in content instead of generating them — same shape, mechanically identical,
        /// just data instead of code. See this module's report.
        /// </summary>
        public ThingDef? entityToBuild;

        /// <summary>Sum of every <see cref="costList"/> entry naming <paramref name="stuff"/>, or 0.</summary>
        public int CostListCountFor(ThingDef stuff)
        {
            if (costList == null) return 0;
            int total = 0;
            for (int i = 0; i < costList.Count; i++)
            {
                if (ReferenceEquals(costList[i].thingDef, stuff)) total += costList[i].count;
            }
            return total;
        }
    }
}
