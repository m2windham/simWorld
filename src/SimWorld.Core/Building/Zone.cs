using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// A named, player-designated set of cells on a map (RimWorld: <c>Verse.Zone</c>). A zone owns no
    /// behaviour of its own beyond its cell set and label; <see cref="ZoneManager"/> is what enforces that a
    /// cell belongs to at most one zone and what a <see cref="AI.WorkGiver_Scanner"/> reads to find work.
    /// <b>Not</b> the same concept as <see cref="Area"/> — RimWorld keeps those two deliberately distinct
    /// (a Zone is exclusive per cell and always serves one purpose; an Area is a non-exclusive boolean
    /// region several can overlap, like the home area) and this port keeps that split too.
    /// </summary>
    public abstract class Zone : IExposable
    {
        public string label;

        internal readonly List<IntVec3> cells = new List<IntVec3>();

        /// <summary>Set by <see cref="ZoneManager.RegisterZone"/>/<see cref="ZoneManager.DeregisterZone"/>; null
        /// for a zone that has not been (or is no longer) registered with a map.</summary>
        public Map.Map? Map { get; internal set; }

        protected Zone(string defaultLabel)
        {
            label = defaultLabel;
        }

        public IReadOnlyList<IntVec3> Cells => cells;

        public int CellCount => cells.Count;

        internal void AddCellRaw(IntVec3 c) => cells.Add(c);

        internal void RemoveCellRaw(IntVec3 c) => cells.Remove(c);

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", "Zone");

            List<int>? xs = Scribe.mode == LoadSaveMode.Saving ? CellsX() : null;
            List<int>? zs = Scribe.mode == LoadSaveMode.Saving ? CellsZ() : null;
            Scribe_Collections.Look(ref xs, "cellsX", LookMode.Value);
            Scribe_Collections.Look(ref zs, "cellsZ", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                cells.Clear();
                if (xs != null && zs != null)
                {
                    int n = Math.Min(xs.Count, zs.Count);
                    for (int i = 0; i < n; i++)
                    {
                        cells.Add(new IntVec3(xs[i], 0, zs[i]));
                    }
                }
            }
        }

        private List<int> CellsX()
        {
            var list = new List<int>(cells.Count);
            for (int i = 0; i < cells.Count; i++) list.Add(cells[i].x);
            return list;
        }

        private List<int> CellsZ()
        {
            var list = new List<int>(cells.Count);
            for (int i = 0; i < cells.Count; i++) list.Add(cells[i].z);
            return list;
        }

        public override string ToString() => label + " (" + cells.Count + " cells)";
    }

    /// <summary>A stockpile zone: hauling targets it under a <see cref="ThingFilter"/> (RimWorld: <c>RimWorld.Zone_Stockpile</c>).
    /// <b>Scope:</b> this pass ports the shape only — no <c>WorkGiver</c> reads it yet, since general item
    /// hauling into a stockpile is not itself built here (see this module's report: <c>HaulGeneral</c> stays
    /// <c>WorkGiver_Pending</c>, same as before this pass).</summary>
    public sealed class Zone_Stockpile : Zone
    {
        public ThingFilter filter = new ThingFilter();

        public Zone_Stockpile() : base("Stockpile")
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ThingFilter? f = filter;
            Scribe_Deep.Look(ref f, "filter");
            filter = f ?? new ThingFilter();
        }
    }

    /// <summary>A growing zone: carries which plant to sow, if any (RimWorld: <c>RimWorld.Zone_Growing</c>).
    /// An empty cell in an active growing zone is <see cref="WorkGiver_GrowerSow"/>'s sowing work; a mature
    /// <see cref="Plant"/> anywhere on the map (not only in a zone — matching RimWorld, a zone controls
    /// sowing, not harvesting) is <see cref="WorkGiver_GrowerHarvest"/>'s harvest work.</summary>
    public sealed class Zone_Growing : Zone
    {
        /// <summary>Null means "sow nothing" — an inactive growing zone, same as RimWorld's own
        /// <c>Zone_Growing</c> with no <c>PlantDefToGrow</c> chosen yet.</summary>
        public ThingDef? plantDefToGrow;

        public bool allowSow = true;

        public Zone_Growing() : base("Growing zone")
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ThingDef? p = plantDefToGrow;
            Scribe_Defs.Look(ref p, "plantDefToGrow");
            plantDefToGrow = p;
            Scribe_Values.Look(ref allowSow, "allowSow", true);
        }
    }
}
