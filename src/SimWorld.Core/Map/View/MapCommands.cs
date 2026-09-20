using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>What happened when the host asked for something at settlement scale.</summary>
    public enum MapCommandOutcome
    {
        /// <summary>The simulation did what was asked.</summary>
        Done,

        /// <summary>No ThingDef in content carries that defName. Separate from <see cref="Refused"/> for the
        /// same reason <c>GodCommandOutcome.UnknownEdict</c> is: a host holding a stale name across a content
        /// change wants to be told that, not offered a rule to reimplement.</summary>
        UnknownDef,

        /// <summary>No settlement has the god's attention, or the attended settlement has no interior yet.
        /// <see cref="MapCommandResult.Reason"/> says which. The map-scale sibling of
        /// <c>GodCommandOutcome.UnknownSettlement</c> — a host reading <c>Map/View</c> never holds a live
        /// <c>Map</c>, so it cannot itself tell "nobody is looking at a settlement" from "the settlement has
        /// no interior yet"; this does.</summary>
        NoMap,

        /// <summary>The cell (or one cell of a multi-cell request) falls outside the open map's bounds.</summary>
        OffMap,

        /// <summary>The cell cannot take what was asked of it right now: a Blueprint, a Frame or a building
        /// already stands there, or (for a zone) the cell already belongs to a different zone. This is a
        /// <i>physical</i> refusal — see this class's own doc — never a judgement that the placement is
        /// unwise.</summary>
        Occupied,

        /// <summary>Refused for a reason that is not one of the physical checks above — content named by
        /// <c>defName</c> exists but cannot do what was asked of it here at all (a crop def with no
        /// <see cref="PlantProperties"/>, an entity def with no Blueprint content authored for it). Still a
        /// physical impossibility, not a quality judgement; see the class doc.</summary>
        Refused,

        /// <summary>Nothing to do — cancelling a designation where none stands.</summary>
        NoChange,
    }

    /// <summary>The outcome of one command, with a sentence explaining it. Mirrors
    /// <c>God.View.GodCommandResult</c> exactly, one scale down.</summary>
    public sealed class MapCommandResult
    {
        internal MapCommandResult(MapCommandOutcome outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        public MapCommandOutcome Outcome { get; }

        /// <summary>Always populated, success included, so a host can surface the same field either way.</summary>
        public string Reason { get; }

        /// <summary>True only for <see cref="MapCommandOutcome.Done"/> — see
        /// <c>GodCommandResult.Changed</c>'s own remark on why a refusal and a no-op stay distinct in
        /// <see cref="Outcome"/> rather than collapsing into a bool.</summary>
        public bool Changed => Outcome == MapCommandOutcome.Done;

        internal static MapCommandResult Done(string reason) => new MapCommandResult(MapCommandOutcome.Done, reason);

        internal static MapCommandResult UnknownDef(string? defName) =>
            new MapCommandResult(MapCommandOutcome.UnknownDef, "No def named '" + defName + "'.");

        internal static MapCommandResult NoMap(string reason) => new MapCommandResult(MapCommandOutcome.NoMap, reason);

        internal static MapCommandResult OffMap(IntVec3 cell) =>
            new MapCommandResult(MapCommandOutcome.OffMap, cell + " is off the map.");

        internal static MapCommandResult Occupied(string reason) => new MapCommandResult(MapCommandOutcome.Occupied, reason);

        internal static MapCommandResult Refused(string reason) => new MapCommandResult(MapCommandOutcome.Refused, reason);

        internal static MapCommandResult NoChange(string reason) => new MapCommandResult(MapCommandOutcome.NoChange, reason);
    }

    /// <summary>
    /// Everything a player can do to the settlement they currently have open, and the only way a host can do
    /// it — the settlement-scale twin of <see cref="God.View.GodCommands"/>, matching that class's shape on
    /// purpose: the same result type, the same named-outcome discipline, the same "say why in the caller's
    /// own terms" rule, and the same stance that widening what a player may do means adding a method here,
    /// deliberately, rather than a host discovering it can already reach further.
    ///
    /// <para/><b>Which map.</b> Whichever settlement currently has the god's attention
    /// (<see cref="God.AttentionManager.FocusedSettlement"/>), exactly as <see cref="MapViewSnapshot.Capture()"/>
    /// reads it and for the same reason given there: the host is told never to carry its own notion of which
    /// settlement is selected, so every command here asks the simulation instead of taking a tile or a map
    /// handle. <see cref="God.View.GodCommands.OpenSettlement"/> is what puts a settlement there in the first
    /// place. A command reached with nobody's attention on a settlement, or on a settlement with no interior
    /// generated yet, comes back <see cref="MapCommandOutcome.NoMap"/> rather than doing nothing silently.
    ///
    /// <para/><b>Before this class, the settlement view had no command surface at all</b> — only
    /// <see cref="MapViewSnapshot"/>/<see cref="MapViewDelta"/>/<see cref="MapViewTracker"/>, which read but
    /// never write. Every Blueprint, <see cref="Building.Zone_Growing"/> and <see cref="Building.Zone_Stockpile"/>
    /// in the game was created by a settlement's own initiative
    /// (<see cref="Building.SettlementConstructionInitiative"/>, <see cref="Building.FarmingInitiative"/>,
    /// <see cref="Economy.SettlementStockInitiative"/>) — the player, at settlement scale, was a spectator.
    /// This does not add a new mechanism: a Blueprint still becomes a <see cref="Building.Frame"/> becomes a
    /// building through the exact same hauling and <c>JobDriver_ConstructFinishFrame</c> pipeline those
    /// initiatives already drive, and a <see cref="Building.Zone_Growing"/> is still sown and harvested by the
    /// same <c>WorkGiver_GrowerSow</c>/<c>WorkGiver_GrowerHarvest</c>. This class only gives the player the
    /// same door the initiatives already use.
    ///
    /// <para/><b>The rule that matters most: a bad decision must be allowed to be bad.</b> Every method here
    /// refuses <i>only</i> what is physically impossible — off the map, a cell already occupied, no such
    /// def, no map open, a "crop" that cannot be grown at all (no <see cref="PlantProperties"/> on the def
    /// named) — and never because the placement is unwise. A wall in a useless place gets built. A field on
    /// poor-but-sowable ground gets sown and grows slower for it, exactly as <c>WorkGiver_GrowerSow</c>
    /// already lets it (that worker's own <see cref="Defs.PlantProperties.sowMinFertility"/> check is the
    /// actual physical floor — ground below it is what "cannot be grown at all" means here; ground above it,
    /// however poor, is the player's call and grows on schedule, just slowly). A stockpile a mile from the
    /// workshop makes hauling slow. None of that is refused, because refusing it would be the simulation
    /// second-guessing the player's logic instead of applying it. <b>Do not "helpfully" add a fertility, a
    /// distance or a need check to any of these.</b> If a future need genuinely calls for one, it belongs in
    /// the read model (so the host can warn the player before they commit), never in the command that carries
    /// the player's decision out.
    ///
    /// <para/><b>Where the player and a settlement's own initiative would both act, they are left to share
    /// the same ground rather than being made to fight over it — and each one already does, unmodified:</b>
    /// <list type="bullet">
    /// <item><b>Construction.</b> <see cref="Building.SettlementConstructionInitiative.CountBuiltOrPlanned"/>
    /// counts every <see cref="Building.Blueprint"/>/<see cref="Building.Frame"/> already on the map for a
    /// def, regardless of who placed it — it walks <c>ThingRequestGroup.Blueprint</c>/<c>BuildingFrame</c>
    /// with no notion of authorship. A bed the player designates with <see cref="PlaceBlueprint"/> therefore
    /// reduces the initiative's own shortfall by exactly one, the same as if the initiative had placed it
    /// itself; the two draw from one shared count instead of duplicating. <see cref="Building.GenConstruct.CanPlaceBlueprintAt"/>
    /// is the same physical check the initiative's own <c>TryFindPlacementCell</c> uses, so the initiative
    /// will never plan a second Blueprint on a cell the player already claimed — it simply cannot pass that
    /// check there.</item>
    /// <item><b>Growing zones.</b> <see cref="Building.FarmingInitiative"/> paints and grows exactly one zone
    /// of its own, labelled <c>"Fields"</c> (<see cref="Building.FarmingInitiative.FieldLabel"/>); a zone
    /// <see cref="MarkGrowingZone"/> creates keeps <see cref="Building.Zone"/>'s own default label and is a
    /// different zone. <see cref="Building.ZoneManager"/>'s one-zone-per-cell rule means the two can never
    /// claim the same ground, and <see cref="Building.FarmingInitiative.CanSowIn"/> already skips any cell
    /// zoned by anyone, so the initiative grows around a player's field rather than through it. <b>This is
    /// deliberate, not an oversight:</b> if the settlement still needs more food than the player's field
    /// supplies, the initiative keeps growing its own field elsewhere rather than silently annexing or
    /// resizing the player's — a player who paints a field gets exactly that field, and the settlement still
    /// feeds itself if that is not enough. The cost is that two fields can stand side by side; that is judged
    /// the right side to be wrong on, because the alternative is the AI overriding a placement the player
    /// made on purpose.</item>
    /// <item><b>Stockpiles.</b> <c>AI.HaulAIUtility.TryFindBestStockpileCell</c> already scans every
    /// <see cref="Building.Zone_Stockpile"/> on the map, not only <see cref="Economy.SettlementStockInitiative"/>'s
    /// own granary (labelled <c>"Granary"</c>) — so a stockpile <see cref="MarkStockpile"/> creates starts
    /// receiving hauled goods immediately, on equal footing with the settlement's own. The two are otherwise
    /// independent for the same one-zone-per-cell reason a growing zone is.</item>
    /// </list>
    ///
    /// <para/><b>Why this is not a queue.</b> Same answer <see cref="God.View.GodCommands"/> gives its own
    /// version of this question: a command applies immediately, on the caller's thread, as if the simulation
    /// had done it. See that class's doc for what would have to change first.
    /// </summary>
    public static class MapCommands
    {
        /// <summary>
        /// Places a Blueprint for <paramref name="defName"/> at <paramref name="cell"/> on the open
        /// settlement's map — "build this here". From here the ordinary pipeline takes over exactly as it
        /// does for a Blueprint <see cref="Building.SettlementConstructionInitiative"/> placed: a citizen
        /// hauls materials to it, it becomes a <see cref="Building.Frame"/>, and a citizen finishes it into
        /// the real building.
        ///
        /// <para/>Refuses only what <see cref="Building.GenConstruct.CanPlaceBlueprintAt"/> itself refuses —
        /// out of bounds, terrain that cannot take the building, or a Blueprint/Frame/building already there
        /// — reported as <see cref="MapCommandOutcome.OffMap"/> or <see cref="MapCommandOutcome.Occupied"/> in
        /// its own words. Never refuses because the spot is a bad one; see the class doc.
        /// </summary>
        public static MapCommandResult PlaceBlueprint(string defName, IntVec3 cell)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            ThingDef? entityDef = ResolveThingDef(defName);
            if (entityDef == null) return MapCommandResult.UnknownDef(defName);

            if (!GenGrid.InBounds(cell, map)) return MapCommandResult.OffMap(cell);

            if (!GenConstruct.CanPlaceBlueprintAt(entityDef, cell, map, out string? failReason))
            {
                return MapCommandResult.Occupied(failReason ?? "That cell cannot take " + entityDef.LabelCap + ".");
            }

            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(entityDef);
            if (blueprintDef == null)
            {
                // Content has no Blueprint_X authored for this entity at all — nothing this call can spawn.
                // Physically impossible in the same sense an unsowable "crop" is: there is no path from here
                // to the thing the player asked for, ever, not merely a bad one.
                return MapCommandResult.Refused("No Blueprint is authored for " + entityDef.LabelCap + ".");
            }

            Thing blueprint = ThingMaker.MakeThing(blueprintDef);
            GenSpawn.Spawn(blueprint, cell, map);
            return MapCommandResult.Done("Blueprint for " + entityDef.LabelCap + " placed at " + cell + ".");
        }

        /// <summary>
        /// Cancels whatever <see cref="Building.Blueprint"/> stands at <paramref name="cell"/> on the open
        /// settlement's map — the undo for <see cref="PlaceBlueprint"/>. A <see cref="Building.Frame"/>
        /// already under construction is left alone: materials are already committed to it, so cancelling a
        /// mere designation is not the same act as abandoning work in progress, and this method's name says
        /// designation on purpose.
        ///
        /// <para/>Cancelling a cell with nothing designated on it is <see cref="MapCommandOutcome.NoChange"/>,
        /// not a failure — the world already matches what was asked for, the same treatment
        /// <c>GodCommands.RescindEdict</c> gives an edict nobody issued.
        /// </summary>
        public static MapCommandResult CancelDesignation(IntVec3 cell)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            if (!GenGrid.InBounds(cell, map)) return MapCommandResult.OffMap(cell);

            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Blueprint blueprint)
                {
                    string label = blueprint.EntityToBuild.LabelCap;
                    blueprint.Destroy(DestroyMode.Cancel);
                    return MapCommandResult.Done("Cancelled the " + label + " designation at " + cell + ".");
                }
            }
            return MapCommandResult.NoChange("Nothing is designated at " + cell + ".");
        }

        /// <summary>
        /// Marks a new <see cref="Building.Zone_Growing"/> over <paramref name="cells"/>, sowing
        /// <paramref name="cropDefName"/> — "farm this". See the class doc for how this coexists with
        /// <see cref="Building.FarmingInitiative"/>'s own field.
        ///
        /// <para/>Refuses only: no such def (<see cref="MapCommandOutcome.UnknownDef"/>); a def that carries
        /// no <see cref="PlantProperties"/> at all, i.e. cannot be grown under any circumstances
        /// (<see cref="MapCommandOutcome.Refused"/>); any cell off the map
        /// (<see cref="MapCommandOutcome.OffMap"/>); any cell already claimed by another zone
        /// (<see cref="MapCommandOutcome.Occupied"/>) — <see cref="Building.ZoneManager"/>'s own
        /// one-zone-per-cell rule, checked up front so the command is all-or-nothing rather than leaving a
        /// half-claimed zone behind. <b>Never</b> refuses for soil that is merely poor — see the class doc.
        /// </summary>
        public static MapCommandResult MarkGrowingZone(string cropDefName, IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? cellsBad = ValidateCells(cells, map);
            if (cellsBad != null) return cellsBad;

            ThingDef? crop = ResolveThingDef(cropDefName);
            if (crop == null) return MapCommandResult.UnknownDef(cropDefName);
            if (crop.plant == null)
            {
                return MapCommandResult.Refused(crop.LabelCap + " cannot be grown — it has no plant properties.");
            }

            var zone = new Zone_Growing { plantDefToGrow = crop, allowSow = true };
            map.zoneManager.RegisterZone(zone);
            for (int i = 0; i < cells.Count; i++) map.zoneManager.AddCell(zone, cells[i]);

            return MapCommandResult.Done(
                "Growing zone for " + crop.LabelCap + " marked over " + cells.Count + " cell(s).");
        }

        /// <summary>
        /// Marks a new <see cref="Building.Zone_Stockpile"/> over <paramref name="cells"/>, accepting
        /// everything — "store things here". See the class doc for how this coexists with
        /// <see cref="Economy.SettlementStockInitiative"/>'s own granary; unlike the granary and the field,
        /// hauling reads every stockpile on the map without regard to label, so this starts working the
        /// moment it is placed.
        ///
        /// <para/>Refuses only: any cell off the map (<see cref="MapCommandOutcome.OffMap"/>); any cell
        /// already claimed by another zone (<see cref="MapCommandOutcome.Occupied"/>), checked up front for
        /// the same all-or-nothing reason <see cref="MarkGrowingZone"/> gives. Never refuses for distance
        /// from anything — see the class doc.
        /// </summary>
        public static MapCommandResult MarkStockpile(IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? cellsBad = ValidateCells(cells, map);
            if (cellsBad != null) return cellsBad;

            var zone = new Zone_Stockpile();
            zone.filter.SetAllowAll(null);
            map.zoneManager.RegisterZone(zone);
            for (int i = 0; i < cells.Count; i++) map.zoneManager.AddCell(zone, cells[i]);

            return MapCommandResult.Done("Stockpile marked over " + cells.Count + " cell(s).");
        }

        // -----------------------------------------------------------------------------------------------
        // Resolution — shared by every command above, never exposed.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The interior of whichever settlement the god currently has open, or null with a reason a host can
        /// show. Deliberately the same resolution <see cref="MapViewSnapshot.Capture()"/> uses (see that
        /// method's own remarks): the host is never trusted to remember a tile, so neither is this class —
        /// every command asks <see cref="God.AttentionManager.FocusedSettlement"/> fresh.
        /// </summary>
        private static SimWorld.Map.Map? ResolveMap(out string? reason)
        {
            if (Find.CurrentGame == null)
            {
                reason = "No game is running, so no map is open.";
                return null;
            }

            World.Settlement? focused = Find.God.Attention.FocusedSettlement;
            if (focused == null)
            {
                reason = "No settlement is open — the god is at civilization scope.";
                return null;
            }

            SimWorld.Map.Map? map = focused.InteriorMap;
            if (map == null)
            {
                reason = focused.name + " has no interior yet — open it first.";
                return null;
            }

            reason = null;
            return map;
        }

        private static ThingDef? ResolveThingDef(string? defName) =>
            string.IsNullOrEmpty(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);

        /// <summary>Bounds and one-zone-per-cell, checked over the whole batch before either zone command
        /// claims a single cell — see those methods' own docs for why this is all-or-nothing.</summary>
        private static MapCommandResult? ValidateCells(IReadOnlyList<IntVec3> cells, SimWorld.Map.Map map)
        {
            if (cells == null || cells.Count == 0) return MapCommandResult.Refused("No cells given.");

            for (int i = 0; i < cells.Count; i++)
            {
                if (!GenGrid.InBounds(cells[i], map)) return MapCommandResult.OffMap(cells[i]);
            }
            for (int i = 0; i < cells.Count; i++)
            {
                if (map.zoneManager.ZoneAt(cells[i]) != null)
                {
                    return MapCommandResult.Occupied(cells[i] + " already belongs to another zone.");
                }
            }
            return null;
        }
    }
}
