using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Lays houses out on the village's plots and keeps them standing (shelter lane; translation:
    /// citizen-initiated construction). RimWorld's player draws a house wall by wall and the colonists raise it;
    /// the settlement is the player here, so this draws it from its <see cref="DwellingDef"/> plan in one go and
    /// the builders raise it through the same blueprints and frames as everything else.
    /// <para/>
    /// <b>New houses.</b> Once every citizen has a bed, built or planned, a household without a house gets one, in
    /// the newest rung the civilization knows (<see cref="DwellingUtility.PreferredDwellingDef"/>), on the first
    /// plot along the street (<see cref="SettlementCompound.DwellingPlots"/>) where the whole plan fits, with at
    /// most <see cref="DwellingTuning.MaxUnderway"/> houses going up at once. A rung learned later changes what is
    /// built next; nothing standing is rebuilt.
    /// <para/>
    /// <b>Repair.</b> Every pass, any wall, door or bed of a planned house that is missing and has nothing planned
    /// in its place is planned again, in whatever the settlement builds with by then. So a house burned, broken by
    /// raiders or shaken down goes back up, and its household keeps it meanwhile (<see cref="DwellingUtility.AssignHomes"/>).
    /// <para/>
    /// <b>Renewal</b> (<c>docs/design/era-progression.md</c> §7). A house standing in an older rung is replaced by
    /// <see cref="Renew"/>, which the daily <see cref="RenewalInitiative"/> calls: the new house goes up on a free
    /// plot while the household lives on in the old one, the household moves when the new one is finished
    /// (<see cref="DwellingUtility.AssignHomes"/>), and only then is the old one pulled down (<see cref="Demolish"/>).
    /// <para/>
    /// Nothing here draws from <see cref="Rand"/>.
    /// </summary>
    public static class DwellingPlanner
    {
        /// <summary>Plans what is missing and what is due; returns how many blueprints it placed.
        /// <paramref name="dormitoryCovered"/>: every citizen a house does not sleep has a bed built or planned.</summary>
        public static int Plan(Settlement settlement, Map.Map map, bool dormitoryCovered)
        {
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null) return 0;

            compound.EnsureDwellingIds();
            NoteCompletions(map, compound);
            Demolish(map, compound);
            int placed = Repair(map);

            DwellingDef? def = DwellingUtility.PreferredDwellingDef();
            if (def == null || !dormitoryCovered) return placed;
            int households = DwellingUtility.Households(settlement).Count;
            while (DwellingUtility.CountBuiltOrPlanned(map) < households && DwellingUtility.CountUnderway(map) < DwellingTuning.MaxUnderway)
            {
                Dwelling? dwelling = TryFindPlot(map, compound, def);
                if (dwelling == null) break;
                compound.dwellings.Add(dwelling);
                placed += PlaceMissingParts(map, dwelling);
            }
            return placed;
        }

        /// <summary>Plans again every part of every house that is neither standing nor planned.</summary>
        public static int Repair(Map.Map map)
        {
            int placed = 0;
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                if (dwellings[i].beingDemolished) continue;
                placed += PlaceMissingParts(map, dwellings[i]);
            }
            return placed;
        }

        /// <summary>
        /// Plans the house that will replace <paramref name="old"/>: a <paramref name="def"/> on the first free plot
        /// along the street (<see cref="TryFindPlot"/>), linked both ways, its parts planned at once. The household
        /// stays in <paramref name="old"/> meanwhile. Returns the new house, or null when no plot is free; then
        /// nothing is planned and the old house stands as it is.
        /// <para/>
        /// RimWorld has no such step: a player who wants a better house builds it and moves the beds (recalled).
        /// The order, the new before the old, is the one that keeps everybody under a roof.
        /// </summary>
        public static Dwelling? Renew(Map.Map map, Dwelling old, DwellingDef def)
        {
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null || old.replacedById != 0 || old.beingDemolished || old.isHeritage) return null;
            Dwelling? replacement = TryFindPlot(map, compound, def);
            if (replacement == null) return null;
            compound.dwellings.Add(replacement);
            compound.EnsureDwellingIds();
            replacement.replacesId = old.id;
            old.replacedById = replacement.id;
            PlaceMissingParts(map, replacement);
            return replacement;
        }

        /// <summary>Stamps the tick a house first stood whole, and the founding house: the first one to.</summary>
        private static void NoteCompletions(Map.Map map, SettlementCompound compound)
        {
            List<Dwelling> dwellings = compound.dwellings;
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.completedTick != Building.UnknownBuiltTick || !d.IsComplete(map)) continue;
                d.completedTick = Find.TickManager.TicksGame;
                if (compound.foundingDwellingId == 0)
                {
                    compound.foundingDwellingId = d.id;
                    RenewalInitiative.MarkHeritage(map, d);
                }
            }
        }

        /// <summary>
        /// Pulls down every house whose household has moved out to its replacement: its roof is lifted, then
        /// everything built on its footprint is marked for deconstruction (RimWorld's own <c>Deconstruct</c>
        /// designation, worked by <c>WorkGiver_Deconstruct</c>), and once nothing of it stands it is struck off the
        /// plan, which frees its plot.
        /// <para/>
        /// <b>The roof is lifted at once.</b> RimWorld's player paints a no-roof area and builders take a roof down
        /// (recalled); this port has neither, and a roof whose last wall is deconstructed falls on whoever is under
        /// it (<see cref="RoofCollapseUtility"/>). So the roof and its build-roof marks go when demolition starts.
        /// </summary>
        internal static void Demolish(Map.Map map, SettlementCompound compound)
        {
            List<Dwelling> dwellings = compound.dwellings;
            for (int i = dwellings.Count - 1; i >= 0; i--)
            {
                Dwelling d = dwellings[i];
                if (!d.beingDemolished) continue;
                bool standing = false;
                foreach (IntVec3 c in d.footprint.Cells)
                {
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (map.roofGrid.Roofed(c)) map.roofGrid.SetRoof(c, null);
                    map.areaManager.BuildRoof[c] = false;
                    IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                    for (int t = here.Count - 1; t >= 0; t--)
                    {
                        Thing thing = here[t];
                        if (thing is Blueprint)
                        {
                            thing.Destroy(DestroyMode.Cancel);
                            continue;
                        }
                        if (thing is Frame frame)
                        {
                            frame.Cancel();
                            continue;
                        }
                        if (thing.def.category != ThingCategory.Building || thing.def.mineable) continue;
                        standing = true;
                        if (map.designationManager.DesignationOn(thing, Designations.PlayerDesignationDefOf.Deconstruct) == null)
                        {
                            map.designationManager.AddDesignation(new Designations.Designation(new LocalTargetInfo(thing), Designations.PlayerDesignationDefOf.Deconstruct));
                        }
                    }
                }
                if (standing) continue;
                dwellings.RemoveAt(i);
                Dwelling? replacement = compound.DwellingById(d.replacedById);
                if (replacement != null) replacement.replacesId = 0;
                RenewalInitiative.Notify_DwellingDemolished(map, d);
            }
        }

        /// <summary>
        /// The house of <paramref name="def"/> on the first free plot where every cell of it is open, walkable
        /// ground in the home area that the band can walk to, and every wall, door and bed can be planned, or
        /// null. A plot is free when no planned house overlaps it.
        /// </summary>
        public static Dwelling? TryFindPlot(Map.Map map, SettlementCompound compound, DwellingDef def)
        {
            Area home = map.areaManager.Home;
            bool homeBinds = home.TrueCount > 0;
            IntVec3? band = SettlementReach.BandCell(map);
            IntVec2 size = def.Size;
            foreach ((CellRect plot, Rot4 rot) in compound.DwellingPlots(int.MaxValue, map.Size))
            {
                // The plan sits at the street edge of its plot: a smaller house leaves the back of the plot open.
                CellRect footprint = rot == Rot4.South
                    ? new CellRect(plot.maxX - size.x + 1, plot.maxZ - size.z + 1, size.x, size.z)
                    : new CellRect(plot.minX, plot.minZ, size.x, size.z);
                if (Overlaps(map, footprint)) continue;
                if (!Open(map, footprint, homeBinds ? home : null, band)) continue;
                var dwelling = new Dwelling(def, footprint, rot);
                if (CanPlaceAll(map, dwelling)) return dwelling;
            }
            return null;
        }

        private static bool Overlaps(Map.Map map, CellRect footprint)
        {
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                if (!footprint.ClipInsideRect(dwellings[i].footprint).IsEmpty) return true;
            }
            return false;
        }

        private static bool Open(Map.Map map, CellRect footprint, Area? within, IntVec3? band)
        {
            foreach (IntVec3 c in footprint.Cells)
            {
                if (!GenGrid.InBounds(c, map) || !map.pathGrid.Walkable(c)) return false;
                if (within != null && !within[c]) return false;
                if (map.edificeGrid[c] != null) return false;
                if (!SettlementReach.CanReach(map, band, c)) return false;
            }
            return true;
        }

        private static bool CanPlaceAll(Map.Map map, Dwelling dwelling)
        {
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 rot) in dwelling.Parts)
            {
                (ThingDef def, ThingDef? _) = DwellingUtility.PartDef(map, dwelling, kind);
                if (!GenConstruct.CanPlaceBlueprintAt(def, cell, map, out _, rot)) return false;
            }
            return true;
        }

        private static int PlaceMissingParts(Map.Map map, Dwelling dwelling)
        {
            int placed = 0;
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 rot) in dwelling.Parts)
            {
                if (Dwelling.IsBuilt(map, kind, cell) || SiteAt(map, cell)) continue;
                (ThingDef def, ThingDef? stuff) = DwellingUtility.PartDef(map, dwelling, kind);
                if (!GenConstruct.CanPlaceBlueprintAt(def, cell, map, out _, rot)) continue;
                ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(def);
                if (blueprintDef == null) continue;
                var blueprint = (Blueprint)ThingMaker.MakeThing(blueprintDef);
                blueprint.stuffToUse = stuff;
                GenSpawn.Spawn(blueprint, cell, map, rot);
                placed++;
            }
            return placed;
        }

        /// <summary>Whether a blueprint or frame already stands on <paramref name="cell"/>.</summary>
        internal static bool SiteAt(Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Blueprint || here[i] is Frame) return true;
            }
            return false;
        }
    }
}
