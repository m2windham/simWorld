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
    /// built next; nothing standing is rebuilt. When no plot on the street is left, the town opens a back street
    /// behind it (<see cref="TryOpenStreet"/>).
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
                Dwelling? dwelling = TryFindPlot(map, compound, def) ?? TryOpenStreet(map, compound, def);
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

        /// <summary>
        /// Plans the house that will replace <paramref name="old"/> on <paramref name="old"/>'s own plot, for a town
        /// with no free plot left: the household moves out at once to lodge in the dormitory, the old house is pulled
        /// down (<see cref="Demolish"/>), and the new one is planned on the cleared ground
        /// (<see cref="WaitsForOldToClear"/>) and is the household's from the start, so it moves in the moment it is
        /// whole. Returns the new house, or null when the plot cannot take <paramref name="def"/>; then nothing
        /// changes. The caller decides whether the household has somewhere to sleep meanwhile
        /// (<see cref="RenewalInitiative"/>).
        /// <para/>
        /// <b>Why.</b> A town builds new before old only while it has room. The plots along the street run out as it
        /// grows, and from then on renewal on a free plot never found one: the forty-strong arcs filled their street
        /// with pit houses in the first month and lived in them, unrenewed, to the Exotic age (renewal lane 2). A real
        /// town rebuilds on the same lot, and its household lodges with neighbours meanwhile. RimWorld has no such
        /// step (recalled: its player deconstructs and rebuilds by hand); this is the spec's own fallback (§7.7),
        /// done a house at a time rather than part by part, since the rungs' plans do not share their walls.
        /// </summary>
        public static Dwelling? RenewInPlace(Map.Map map, Dwelling old, DwellingDef def)
        {
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null || old.replacedById != 0 || old.beingDemolished || old.isHeritage) return null;
            (CellRect Plot, Rot4 Rot)? plot = PlotOf(map, compound, old);
            if (plot == null) return null;
            CellRect footprint = FootprintOn(plot.Value.Plot, plot.Value.Rot, def.Size);
            Area home = map.areaManager.Home;
            bool homeBinds = home.TrueCount > 0;
            IntVec3? band = SettlementReach.BandCell(map);
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            foreach (IntVec3 c in footprint.Cells)
            {
                if (!GenGrid.InBounds(c, map)) return null;
                if (old.footprint.Contains(c)) continue; // the old house's own ground: cleared before anything is planned on it
                if (!map.pathGrid.Walkable(c) || (homeBinds && !home[c]) || map.edificeGrid[c] != null) return null;
                if (!SettlementReach.CanReach(map, band, c)) return null;
                for (int i = 0; i < dwellings.Count; i++)
                {
                    if (!ReferenceEquals(dwellings[i], old) && dwellings[i].footprint.Contains(c)) return null;
                }
            }

            var replacement = new Dwelling(def, footprint, plot.Value.Rot);
            compound.dwellings.Add(replacement);
            compound.EnsureDwellingIds();
            replacement.replacesId = old.id;
            old.replacedById = replacement.id;
            replacement.householdId = old.householdId;
            old.householdId = Pawns.Pawn_RelationsTracker.None;
            old.beingDemolished = true;
            return replacement;
        }

        /// <summary>Whether <paramref name="dwelling"/> replaces a house on its own ground that is still coming
        /// down: nothing of it is planned until that ground is clear (<see cref="RenewInPlace"/>).</summary>
        internal static bool WaitsForOldToClear(Map.Map map, Dwelling dwelling)
        {
            if (dwelling.replacesId == 0) return false;
            Dwelling? old = map.settlementCompound?.DwellingById(dwelling.replacesId);
            return old != null && !old.footprint.ClipInsideRect(dwelling.footprint).IsEmpty;
        }

        /// <summary>The street plot <paramref name="d"/> stands on, and which way it faces, or null for a house off
        /// the street's plots.</summary>
        private static (CellRect Plot, Rot4 Rot)? PlotOf(Map.Map map, SettlementCompound compound, Dwelling d)
        {
            foreach ((CellRect plot, Rot4 rot) in AllPlots(map, compound))
            {
                if (plot.Contains(new IntVec3(d.footprint.minX, 0, d.footprint.minZ))
                    && plot.Contains(new IntVec3(d.footprint.maxX, 0, d.footprint.maxZ)))
                {
                    return (plot, rot);
                }
            }
            return null;
        }

        /// <summary>Where a plan of <paramref name="size"/> sits on <paramref name="plot"/>: at the street edge, so a
        /// smaller house leaves the back of the plot open.</summary>
        private static CellRect FootprintOn(CellRect plot, Rot4 rot, IntVec2 size) => rot == Rot4.South
            ? new CellRect(plot.maxX - size.x + 1, plot.maxZ - size.z + 1, size.x, size.z)
            : new CellRect(plot.minX, plot.minZ, size.x, size.z);

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
        /// <para/>
        /// <b>Only the roof people built.</b> A house can stand in a mined-out hall under a mountain, and there its
        /// walls hold up the rock: the roof over the footprint and the rock around it. Lifting a roof is a
        /// builder's job and a mountain is not one, so a natural roof stays. Any wall, door or frame whose going
        /// would still bring a roof down (<see cref="RoofCollapseUtility.WouldCollapseRoofIfRemoved"/>) is left
        /// standing, unmarked, and the house is struck off without it; the next pass asks again, since a piece
        /// that holds nothing now can come to as its neighbours go. The fourth playtest lost two people this way:
        /// the town's renewal pulled down an old house in a mined hall at night, this lifted the rock over its
        /// footprint as if it were thatch, the walls went, and the mountain came down on the two asleep beside it
        /// (the save's collapsed rock and the unroofed footprints in the middle of the hall are what is left).
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
                    if (map.roofGrid.RoofAt(c) is RoofDef roof && !roof.isNatural) map.roofGrid.SetRoof(c, null);
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
                        if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(thing))
                        {
                            Designations.Designation? held = map.designationManager.DesignationOn(thing, Designations.PlayerDesignationDefOf.Deconstruct);
                            if (held != null && held.source == Designations.DesignationSource.Settlement) map.designationManager.RemoveDesignation(held);
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
        /// null. A plot is free when no planned house overlaps it. The first street's plots come first, then
        /// those of every back street the town has opened (<see cref="TryOpenStreet"/>), in the order it opened
        /// them; a back-street plot must also stay off every zone, so a new street never takes a field.
        /// </summary>
        public static Dwelling? TryFindPlot(Map.Map map, SettlementCompound compound, DwellingDef def)
        {
            Area home = map.areaManager.Home;
            bool homeBinds = home.TrueCount > 0;
            IntVec3? band = SettlementReach.BandCell(map);
            IntVec2 size = def.Size;
            foreach ((CellRect plot, Rot4 rot) in compound.DwellingPlots(int.MaxValue, map.Size))
            {
                CellRect footprint = FootprintOn(plot, rot, size);
                if (Overlaps(map, footprint)) continue;
                if (!Open(map, footprint, homeBinds ? home : null, band)) continue;
                var dwelling = new Dwelling(def, footprint, rot);
                if (CanPlaceAll(map, dwelling)) return dwelling;
            }
            for (int street = 1; street <= compound.openedStreets; street++)
            {
                Dwelling? onBackStreet = TryFindBackStreetPlot(map, compound, def, street, homeBinds ? home : null, band);
                if (onBackStreet != null) return onBackStreet;
            }
            return null;
        }

        /// <summary>
        /// Opens back streets (<see cref="SettlementCompound.BackStreetPlots"/>) one after another until one takes a
        /// house of <paramref name="def"/>, and returns that house, or null once no street is left on the map. Only
        /// called when no plot on any street already open is free (<see cref="TryFindPlot"/>), so a town whose
        /// first street still has room places exactly as it did before back streets existed.
        /// <para/>
        /// <b>Opening a street claims its ground.</b> A plot must lie in the home area, and the home area is the
        /// village the compound claimed at its founding (<see cref="SettlementCompound.ClaimHome"/>) and what has
        /// grown round what was built since. That is what really bounded the first street: past its planned plots
        /// none is ever wholly home, so a ruin, an outcrop or the map edge on a few of the planned ones is a
        /// ceiling on houses. On the era arc's seed 201 it left room for about 19 houses for 26 households, and the
        /// Classical gate ("three in four sleep under their own roof") could not be met. Opening a street marks its
        /// first <see cref="SettlementCompound.PlannedDwellings"/> plots home, as the founding marked the village,
        /// once (<see cref="SettlementCompound.openedStreets"/> is saved), so a cell the player takes out of it
        /// later stays out. A street with no plot that could take the house is passed over and claims nothing.
        /// <para/>
        /// <b>Why a new street, not clearing the old one.</b> The first street is blocked by what the map holds:
        /// ruins, rock, water, the edge. Clearing would mean a town pulling down the ruins of the people before it
        /// and quarrying outcrops to build on, work no initiative here does and which takes the ruins out of the
        /// map's story, and an edge cannot be cleared at all. A real town in that place lays out its next street.
        /// RimWorld has nothing to port: its player lays out every base by hand.
        /// </summary>
        internal static Dwelling? TryOpenStreet(Map.Map map, SettlementCompound compound, DwellingDef def)
        {
            Area home = map.areaManager.Home;
            IntVec3? band = SettlementReach.BandCell(map);
            for (;;)
            {
                int street = compound.openedStreets + 1;
                CellRect block = compound.BackStreetBlock(street, map.Size);
                // Back streets alternate north and south and run outward, so once the next on both sides is off
                // the map, every later one is too.
                if (block.IsEmpty && compound.BackStreetBlock(street + 1, map.Size).IsEmpty) return null;
                compound.openedStreets = street;
                if (block.IsEmpty || TryFindBackStreetPlot(map, compound, def, street, null, band) == null) continue;
                bool homeBinds = home.TrueCount > 0;
                if (homeBinds)
                {
                    foreach (IntVec3 c in block.ExpandedBy(AutoHomeAreaMaker.BorderWidth).ClipInsideMap(map).Cells) home[c] = true;
                }
                Dwelling? dwelling = TryFindBackStreetPlot(map, compound, def, street, homeBinds ? home : null, band);
                if (dwelling != null) return dwelling;
            }
        }

        private static Dwelling? TryFindBackStreetPlot(Map.Map map, SettlementCompound compound, DwellingDef def, int street, Area? within, IntVec3? band)
        {
            IntVec2 size = def.Size;
            foreach ((CellRect plot, Rot4 rot) in compound.BackStreetPlots(street, map.Size))
            {
                CellRect footprint = FootprintOn(plot, rot, size);
                if (Overlaps(map, footprint)) continue;
                if (!Open(map, footprint, within, band) || OnAZone(map, plot)) continue;
                var dwelling = new Dwelling(def, footprint, rot);
                if (CanPlaceAll(map, dwelling)) return dwelling;
            }
            return null;
        }

        /// <summary>Every plot a house can stand on: the first street's, then each opened back street's.</summary>
        private static IEnumerable<(CellRect Plot, Rot4 Rot)> AllPlots(Map.Map map, SettlementCompound compound)
        {
            foreach ((CellRect, Rot4) plot in compound.DwellingPlots(int.MaxValue, map.Size)) yield return plot;
            for (int street = 1; street <= compound.openedStreets; street++)
            {
                foreach ((CellRect, Rot4) plot in compound.BackStreetPlots(street, map.Size)) yield return plot;
            }
        }

        private static bool OnAZone(Map.Map map, CellRect plot)
        {
            foreach (IntVec3 c in plot.Cells)
            {
                if (map.zoneManager.ZoneAt(c) != null) return true;
            }
            return false;
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
            if (WaitsForOldToClear(map, dwelling)) return 0;
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
