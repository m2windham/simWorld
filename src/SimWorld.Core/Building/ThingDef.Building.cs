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

        private System.Collections.Generic.Dictionary<ThingDef, System.Collections.Generic.List<Crafting.ThingDefCountClass>>? cachedCostListsByStuff;
        private System.Collections.Generic.List<Crafting.ThingDefCountClass>? cachedCostListNoStuff;

        /// <summary>
        /// What building this out of <paramref name="stuff"/> costs (RimWorld: <c>BuildableDef.CostListAdjusted</c>):
        /// <see cref="costList"/>, plus <see cref="costStuffCount"/> divided by the stuff's
        /// <see cref="VolumePerUnit"/> (rounded, at least 1) of the stuff itself — merged into the costList entry
        /// when that already names the stuff, otherwise inserted first. A def that is not made from stuff costs
        /// its costList whatever is passed.
        /// <para/>
        /// RimWorld logs an error for null stuff on a stuff-built def and prices it in
        /// <see cref="Crafting.GenStuff.DefaultStuffFor"/> instead; this port takes the default without the log,
        /// since every caller here that can hold null (a blueprint placed before it carried a stuff) means "the
        /// default". Cached per stuff, as RimWorld caches per (def, stuff) pair; never invalidated, like
        /// <see cref="AllRecipes"/>, because a def's costs do not change after load.
        /// </summary>
        public System.Collections.Generic.List<Crafting.ThingDefCountClass> CostListAdjusted(ThingDef? stuff)
        {
            if (!MadeFromStuff) stuff = null;
            else stuff ??= Crafting.GenStuff.DefaultStuffFor(this);

            if (stuff == null)
            {
                return cachedCostListNoStuff ??= costList != null
                    ? new System.Collections.Generic.List<Crafting.ThingDefCountClass>(costList)
                    : new System.Collections.Generic.List<Crafting.ThingDefCountClass>();
            }

            cachedCostListsByStuff ??= new System.Collections.Generic.Dictionary<ThingDef, System.Collections.Generic.List<Crafting.ThingDefCountClass>>();
            if (cachedCostListsByStuff.TryGetValue(stuff, out var cached)) return cached;

            int stuffCount = (int)System.Math.Round(costStuffCount / stuff.VolumePerUnit, System.MidpointRounding.ToEven);
            if (stuffCount < 1) stuffCount = 1; // RimWorld: a made-from-stuff thing always takes at least one unit.

            var result = new System.Collections.Generic.List<Crafting.ThingDefCountClass>();
            bool merged = false;
            if (costList != null)
            {
                foreach (Crafting.ThingDefCountClass entry in costList)
                {
                    if (ReferenceEquals(entry.thingDef, stuff))
                    {
                        result.Add(new Crafting.ThingDefCountClass(entry.thingDef, entry.count + stuffCount));
                        merged = true;
                    }
                    else
                    {
                        result.Add(entry);
                    }
                }
            }
            if (!merged) result.Insert(0, new Crafting.ThingDefCountClass(stuff, stuffCount));
            cachedCostListsByStuff[stuff] = result;
            return result;
        }

        /// <summary>How many of <paramref name="material"/> building this out of <paramref name="stuff"/> costs in
        /// all (<see cref="CostListAdjusted"/>), or 0. Null stuff on a stuff-built def is its default stuff.</summary>
        public int CostListCountFor(ThingDef material, ThingDef? stuff = null)
        {
            System.Collections.Generic.List<Crafting.ThingDefCountClass> cost = CostListAdjusted(stuff);
            int total = 0;
            for (int i = 0; i < cost.Count; i++)
            {
                if (ReferenceEquals(cost[i].thingDef, material)) total += cost[i].count;
            }
            return total;
        }
    }
}
