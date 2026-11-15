using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Raises the butcher's table, the tailor's table and the smithy, each once something on the map wants
    /// it, in the settlement's work yard (<see cref="SettlementCompound.WorkSite"/>). RimWorld's player places
    /// every bench; this is the settlement doing it, the same translation as <see cref="SettlementWorksInitiative"/>,
    /// which calls this on its own gated pass.
    ///
    /// <para/><b>What wants each one</b>, read from state that already exists rather than a new economy:
    /// <list type="bullet">
    /// <item>a butcher's table, once an animal carcass lies on the map that could be butchered
    /// (<see cref="Corpse.IsButcherable"/>);</item>
    /// <item>a tailor's table or a smithy, once one of its outfitting recipes is available
    /// (<see cref="RecipeDef.AvailableNow"/>) and a citizen on the map lacks what it makes
    /// (<see cref="OutfittingInitiative.Lacking"/>), the head count that bench's bill is then set to. A bench
    /// nobody lacks anything from would hold no bill (<see cref="OutfittingInitiative"/> never queues a zero
    /// target), and with no tailor's table standing no cotton field is sown either, so this is the gate that
    /// starts the clothing chain.</item>
    /// </list>
    /// One of each, like the stove (<c>CookingTuning.StovesWanted</c>) and the stonecutter, for the same
    /// reason: the bench is not the bottleneck. Research prerequisites are honoured.
    ///
    /// <para/><b>Content looked up by name.</b> Every bench is found by defName and one with no Blueprint
    /// content is skipped, so a pack without the workshops simply gets none.
    ///
    /// <para/><b>Where.</b> The yard site first, when the material lies within a crafter's reach of it
    /// (<see cref="WorkGiver_DoBill.IngredientSearchRadius"/>, the reach a bill's ingredients are searched
    /// over), and otherwise the first free cell within that reach of the material, never on ground the
    /// compound keeps (<see cref="SettlementCompound.Refuses"/>). A bench out of reach of its material is a
    /// bench nobody can work. For the tailor's table and the smithy the material is any stack one of the
    /// wanted recipes takes and any field growing a crop harvested as one (cotton for cloth); with none on
    /// the map yet the bench goes in the yard, beside the storage whatever is harvested is hauled to.
    /// </summary>
    public static class SettlementWorkshops
    {
        public const string ButcherDefName = "TableButcher";
        public const string TailorDefName = "TableTailor";
        public const string SmithyDefName = "Smithy";

        public static void Run(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            EnsureButcher(map);
            List<Pawn>? citizens = null;
            EnsureOutfitting(map, TailorDefName, SettlementCompound.WorkSite.Tailor, ref citizens);
            EnsureOutfitting(map, SmithyDefName, SettlementCompound.WorkSite.Smithy, ref citizens);
        }

        private static void EnsureButcher(Map.Map map)
        {
            if (!TryBench(map, ButcherDefName, out ThingDef bench, out ThingDef blueprintDef)) return;
            List<IntVec3> material = MaterialCells(map, t => t is Corpse corpse && corpse.IsButcherable);
            if (material.Count == 0) return;
            Place(map, SettlementCompound.WorkSite.Butcher, bench, blueprintDef, material);
        }

        /// <summary>The tailor's table or the smithy, once a citizen on the map lacks what one of its
        /// available recipes makes. <paramref name="citizens"/> is read once a pass, on first need.</summary>
        private static void EnsureOutfitting(Map.Map map, string benchDefName, SettlementCompound.WorkSite site, ref List<Pawn>? citizens)
        {
            if (!TryBench(map, benchDefName, out ThingDef bench, out ThingDef blueprintDef)) return;

            var wanted = new List<RecipeDef>();
            IReadOnlyList<RecipeDef> recipes = bench.AllRecipes;
            for (int r = 0; r < recipes.Count; r++)
            {
                RecipeDef recipe = recipes[r];
                if (!OutfittingInitiative.IsOutfitting(recipe) || !recipe.AvailableNow) continue;
                citizens ??= OutfittingInitiative.CitizensOn(map);
                if (OutfittingInitiative.Lacking(recipe, citizens) > 0) wanted.Add(recipe);
            }
            if (wanted.Count == 0) return;

            List<IntVec3> material = MaterialCells(map, t => t.def.category == ThingCategory.Item && TakesAny(wanted, t.def));
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int z = 0; z < zones.Count; z++)
            {
                // A field growing what a recipe takes is the stock still to come.
                if (zones[z] is Zone_Growing field && field.plantDefToGrow?.plant?.harvestedThingDef is ThingDef crop && TakesAny(wanted, crop))
                {
                    material.AddRange(field.Cells);
                }
            }
            Place(map, site, bench, blueprintDef, material);
        }

        private static bool TakesAny(List<RecipeDef> recipes, ThingDef def)
        {
            for (int r = 0; r < recipes.Count; r++)
            {
                List<IngredientCount>? ingredients = recipes[r].ingredients;
                if (ingredients == null) continue;
                for (int i = 0; i < ingredients.Count; i++)
                {
                    if (ingredients[i].filter.Allows(def)) return true;
                }
            }
            return false;
        }

        /// <summary>The bench and its blueprint, when the content exists, its research is done and none is
        /// built or planned yet.</summary>
        private static bool TryBench(Map.Map map, string benchDefName, out ThingDef bench, out ThingDef blueprintDef)
        {
            bench = null!;
            blueprintDef = null!;
            ThingDef? found = DefDatabase<ThingDef>.GetNamedSilentFail(benchDefName);
            if (found == null || !found.IsResearchFinished) return false;
            ThingDef? blueprint = GenConstruct.BlueprintDefFor(found);
            if (blueprint == null) return false;
            if (SettlementWorksInitiative.CountBuiltOrPlanned(map, found) > 0) return false;
            bench = found;
            blueprintDef = blueprint;
            return true;
        }

        /// <summary>The yard site, when <paramref name="material"/> is empty or some of it lies within reach
        /// of the site; else a free cell within reach of the first of it.</summary>
        private static void Place(Map.Map map, SettlementCompound.WorkSite site, ThingDef bench, ThingDef blueprintDef, List<IntVec3> material)
        {
            int reach = WorkGiver_DoBill.IngredientSearchRadius;
            SettlementCompound? compound = map.settlementCompound;
            IntVec3 cell = default;
            bool placed = compound != null && compound.TryFindWorkSite(map, site, bench, out cell)
                && (material.Count == 0 || WithinReach(cell, material, reach));
            if (!placed && material.Count > 0) placed = TryNear(map, bench, material[0], reach, out cell);
            if (placed) GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), cell, map);
        }

        private static bool WithinReach(IntVec3 site, List<IntVec3> material, int reach)
        {
            CellRect around = CellRect.CenteredOn(site, reach);
            for (int i = 0; i < material.Count; i++)
            {
                if (around.Contains(material[i])) return true;
            }
            return false;
        }

        private static bool TryNear(Map.Map map, ThingDef bench, IntVec3 near, int reach, out IntVec3 cell)
        {
            foreach (IntVec3 c in CellRect.CenteredOn(near, reach).ClipInsideMap(map).Cells)
            {
                if (SettlementCompound.Refuses(map, bench, c)) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(bench, c, map, out _)) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        private static List<IntVec3> MaterialCells(Map.Map map, Func<Thing, bool> wants)
        {
            var cells = new List<IntVec3>();
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Spawned && wants(items[i])) cells.Add(items[i].Position);
            }
            return cells;
        }
    }
}
