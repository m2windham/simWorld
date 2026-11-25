using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Designations
{
    /// <summary>
    /// What the player may mark, and the marking itself — RimWorld's <c>Designator_*.CanDesignateCell</c>/
    /// <c>CanDesignateThing</c>/<c>DesignateSingleCell</c>/<c>DesignateThing</c>, one static per rule rather than
    /// one class per designator, because the designator classes in RimWorld are mostly UI (icon, sound, drag
    /// behaviour), which lives in the host. Only the validity rules and the act are ported.
    ///
    /// <para/>A refusal is a reason string; null means accepted. Every refusal is a physical one — the thing is
    /// not a plant, the animal is not wild, the rock is not there — never a judgement that the mark is unwise:
    /// a player may mark every tree on the map, or the settlement's last muffalo for slaughter
    /// (<c>Map.View.MapCommands</c>'s own rule).
    ///
    /// <para/>The rules, from RimWorld's designators (recalled, not re-read from a decompile; each is pinned by
    /// <c>PlayerDesignationTests</c>):
    /// <list type="bullet">
    /// <item><b>Mine</b> (cell): a mineable edifice stands there.</item>
    /// <item><b>CutPlant</b>: a plant. <b>HarvestPlant</b>: a plant ready to harvest now. The two replace each
    /// other on one plant, as RimWorld's do.</item>
    /// <item><b>Hunt</b>: a live wild animal (no faction) — never a person and never anybody's livestock.
    /// <b>Tame</b>: the same, and a race that can be tamed at all (wildness below 1). The two replace each other
    /// on one animal.</item>
    /// <item><b>Slaughter</b>: a live animal of the settlement's own faction.</item>
    /// <item><b>Deconstruct</b>: a building that is not natural rock (rock is mined) and belongs to the
    /// settlement or to nobody. RimWorld asks for the player's faction and offers to claim the rest; this port
    /// has no claiming, so an unowned building — a ruin, anything generated with the map — counts as
    /// claimable and is accepted.</item>
    /// <item><b>HaulUrgently</b>: an item that can be hauled at all.</item>
    /// </list>
    /// </summary>
    public static class DesignatorUtility
    {
        /// <summary>The verb a refusal reads in — "nothing here can be <i>hunted</i>".</summary>
        public static string Verb(DesignationDef def)
        {
            switch (def.defName)
            {
                case "Mine": return "mined";
                case "CutPlant": return "cleared";
                case "HarvestPlant": return "harvested";
                case "Hunt": return "hunted";
                case "Tame": return "tamed";
                case "Slaughter": return "slaughtered";
                case "Deconstruct": return "taken down";
                case "HaulUrgently": return "carried first";
                default: return "marked " + def.defName;
            }
        }

        /// <summary>Whether <paramref name="def"/> is one the player can place (has a rule here).</summary>
        public static bool IsPlayerDesignation(DesignationDef def) =>
            def == DesignationDefOf.Mine || def == PlayerDesignationDefOf.CutPlant
            || def == PlayerDesignationDefOf.HarvestPlant || def == PlayerDesignationDefOf.Hunt
            || def == PlayerDesignationDefOf.Tame || def == PlayerDesignationDefOf.Slaughter
            || def == PlayerDesignationDefOf.Deconstruct || def == PlayerDesignationDefOf.HaulUrgently;

        /// <summary>The cell rule (RimWorld: <c>Designator_Mine.CanDesignateCell</c>). Null when accepted. A
        /// cell the settlement already marked is accepted — <see cref="TryDesignateCell"/> claims it.</summary>
        public static string? CanDesignateCell(DesignationDef def, IntVec3 c, Map.Map map)
        {
            if (!GenGrid.InBounds(c, map)) return c + " is off the map";
            if (def != DesignationDefOf.Mine) throw new ArgumentException(def.defName + " aims at things, not cells.", nameof(def));
            Designation? existing = map.designationManager.DesignationAt(c, def);
            if (existing != null && existing.source == DesignationSource.Player) return "already marked";
            if (AI.WorkGiver_Miner.FirstMineable(c, map) == null) return "nothing here can be mined";
            return null;
        }

        /// <summary>The thing rule for <paramref name="def"/>; null when accepted. See the class doc for each.</summary>
        public static string? CanDesignateThing(DesignationDef def, Thing t, Map.Map map)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (!t.Spawned || t.Map != map) return t.Label + " is not on this map";
            Designation? existing = map.designationManager.DesignationOn(t, def);
            if (existing != null && existing.source == DesignationSource.Player) return t.Label + " is already marked";

            if (def == PlayerDesignationDefOf.CutPlant)
            {
                return t is Plant ? null : t.Label + " is not a plant";
            }
            if (def == PlayerDesignationDefOf.HarvestPlant)
            {
                if (!(t is Plant plant)) return t.Label + " is not a plant";
                return plant.HarvestableNow ? null : t.Label + " is not ready to harvest";
            }
            if (def == PlayerDesignationDefOf.Hunt || def == PlayerDesignationDefOf.Tame)
            {
                if (!(t is Pawn animal) || !animal.RaceProps.Animal) return t.Label + " is not an animal";
                if (animal.Dead) return t.Label + " is dead";
                if (animal.faction != null) return t.Label + " is not wild";
                if (def == PlayerDesignationDefOf.Tame && animal.RaceProps.wildness >= 1f) return t.Label + " cannot be tamed";
                return null;
            }
            if (def == PlayerDesignationDefOf.Slaughter)
            {
                if (!(t is Pawn animal) || !animal.RaceProps.Animal) return t.Label + " is not an animal";
                if (animal.Dead) return t.Label + " is dead";
                if (animal.faction == null || !IsColonyFaction(animal.faction, map)) return t.Label + " is not the settlement's own animal";
                return null;
            }
            if (def == PlayerDesignationDefOf.Deconstruct)
            {
                if (t.def.category != ThingCategory.Building) return t.Label + " is not a building";
                if (t.def.mineable) return t.Label + " is rock — it is mined, not taken down";
                if (t.Faction != null && !IsColonyFaction(t.Faction, map)) return t.Label + " belongs to someone else";
                return null;
            }
            if (def == PlayerDesignationDefOf.HaulUrgently)
            {
                return t.def.EverHaulable ? null : t.Label + " cannot be carried";
            }
            throw new ArgumentException(def.defName + " is not a designation the player can place.", nameof(def));
        }

        /// <summary>Marks <paramref name="c"/> (RimWorld: <c>Designator_Mine.DesignateSingleCell</c>). A cell the
        /// settlement had already marked becomes the player's.</summary>
        public static string? TryDesignateCell(DesignationDef def, IntVec3 c, Map.Map map)
        {
            string? refusal = CanDesignateCell(def, c, map);
            if (refusal != null) return refusal;
            Designation? existing = map.designationManager.DesignationAt(c, def);
            if (existing != null) map.designationManager.ClaimForPlayer(existing);
            else map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(c), def, DesignationSource.Player));
            return null;
        }

        /// <summary>Marks <paramref name="t"/> (RimWorld: <c>Designator.DesignateThing</c>), first removing the
        /// mark it replaces — Hunt and Tame, CutPlant and HarvestPlant, are either-or on one target.</summary>
        public static string? TryDesignateThing(DesignationDef def, Thing t, Map.Map map)
        {
            string? refusal = CanDesignateThing(def, t, map);
            if (refusal != null) return refusal;
            DesignationManager dm = map.designationManager;
            DesignationDef? rival = RivalOf(def);
            if (rival != null)
            {
                Designation? replaced = dm.DesignationOn(t, rival);
                if (replaced != null) dm.RemoveDesignation(replaced);
            }
            Designation? existing = dm.DesignationOn(t, def);
            if (existing != null) dm.ClaimForPlayer(existing);
            else dm.AddDesignation(new Designation(new LocalTargetInfo(t), def, DesignationSource.Player));
            return null;
        }

        /// <summary>
        /// RimWorld's multi-cell shape (<c>Designator.DesignateMultiCell</c>): for a cell designation the cell
        /// itself; for a thing designation every thing on the cell the rule accepts. Returns how many marks it
        /// placed; <paramref name="refusal"/> says why it placed none.
        /// </summary>
        public static int DesignateCellOrThingsIn(DesignationDef def, IntVec3 c, Map.Map map, out string? refusal)
        {
            if (!GenGrid.InBounds(c, map))
            {
                refusal = "off the map";
                return 0;
            }
            if (def.targetType == TargetType.Cell)
            {
                refusal = TryDesignateCell(def, c, map);
                return refusal == null ? 1 : 0;
            }

            // Copied first: designating never moves a thing, but the grid list is live and this must not
            // depend on that staying true.
            var here = new List<Thing>(map.thingGrid.ThingsListAt(c));
            int placed = 0;
            bool anyAlready = false;
            for (int i = 0; i < here.Count; i++)
            {
                Designation? existing = map.designationManager.DesignationOn(here[i], def);
                if (existing != null && existing.source == DesignationSource.Player)
                {
                    anyAlready = true;
                    continue;
                }
                if (TryDesignateThing(def, here[i], map) == null) placed++;
            }
            refusal = placed > 0 ? null : anyAlready ? "already marked" : "nothing here can be " + Verb(def);
            return placed;
        }

        /// <summary>Removes the marks of <paramref name="def"/> (every def when null) on <paramref name="c"/> and
        /// on every thing standing there, whoever placed them (RimWorld: <c>Designator_Cancel</c>).</summary>
        public static int RemoveDesignationsIn(DesignationDef? def, IntVec3 c, Map.Map map)
        {
            if (!GenGrid.InBounds(c, map)) return 0;
            DesignationManager dm = map.designationManager;
            int removed = 0;
            IReadOnlyList<Designation> all = dm.AllDesignations;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Designation des = all[i];
                if (def != null && des.def != def) continue;
                bool here = des.target.HasThing
                    ? des.target.Thing!.Spawned && des.target.Thing.Map == map && des.target.Thing.OccupiedRect().Contains(c)
                    : des.target.Cell == c;
                if (!here) continue;
                dm.RemoveDesignation(des);
                removed++;
            }
            return removed;
        }

        /// <summary>Removes the marks of <paramref name="def"/> (every def when null) aimed at <paramref name="t"/>.</summary>
        public static int RemoveDesignationsOn(DesignationDef? def, Thing t, Map.Map map)
        {
            DesignationManager dm = map.designationManager;
            int removed = 0;
            IReadOnlyList<Designation> all = dm.AllDesignations;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Designation des = all[i];
                if (def != null && des.def != def) continue;
                if (!ReferenceEquals(des.target.Thing, t)) continue;
                dm.RemoveDesignation(des);
                removed++;
            }
            return removed;
        }

        /// <summary>The settlement's own faction, for Slaughter and Deconstruct. A map no settlement owns — a
        /// bare test map — has no notion of "ours", so any faction counts there.</summary>
        public static bool IsColonyFaction(Faction faction, Map.Map map)
        {
            World.Settlement? settlement = AI.HuntingInitiative.SettlementFor(map);
            return settlement == null || ReferenceEquals(settlement.faction, faction);
        }

        private static DesignationDef? RivalOf(DesignationDef def)
        {
            if (def == PlayerDesignationDefOf.Hunt) return PlayerDesignationDefOf.Tame;
            if (def == PlayerDesignationDefOf.Tame) return PlayerDesignationDefOf.Hunt;
            if (def == PlayerDesignationDefOf.CutPlant) return PlayerDesignationDefOf.HarvestPlant;
            if (def == PlayerDesignationDefOf.HarvestPlant) return PlayerDesignationDefOf.CutPlant;
            return null;
        }
    }
}
