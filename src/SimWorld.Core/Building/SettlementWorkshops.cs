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
    /// <item>a tailor's table, once cloth or leather is in stock (a stack of a <c>Fabric</c> or
    /// <c>Leathery</c> stuff) and a citizen is wearing nothing (<see cref="Pawn_ApparelTracker.WornApparel"/>);</item>
    /// <item>a smithy, once metal is in stock (a stack of a <c>Metallic</c> stuff).</item>
    /// </list>
    /// One of each, like the stove (<c>CookingTuning.StovesWanted</c>) and the stonecutter, for the same
    /// reason: the bench is not the bottleneck. Research prerequisites are honoured.
    ///
    /// <para/><b>Built without the benches' construction content.</b> Their Blueprint and Frame defs land with
    /// another lane. Every bench is looked up by name, and one with no Blueprint content is skipped, so this
    /// does nothing until that content exists and needs no change when it does.
    ///
    /// <para/><b>Where.</b> The yard site first, when the material lies within a crafter's reach of it
    /// (<see cref="WorkGiver_DoBill.IngredientSearchRadius"/>, the reach a bill's ingredients are searched
    /// over), and otherwise the first free cell within that reach of the material, never on ground the
    /// compound keeps (<see cref="SettlementCompound.Refuses"/>). A bench out of reach of its material is a
    /// bench nobody can work.
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

            Ensure(map, ButcherDefName, SettlementCompound.WorkSite.Butcher, IsButcherableCarcass);
            if (SomeoneWearsNothing(settlement)) Ensure(map, TailorDefName, SettlementCompound.WorkSite.Tailor, IsTailoringStock);
            Ensure(map, SmithyDefName, SettlementCompound.WorkSite.Smithy, IsMetalStock);
        }

        private static void Ensure(Map.Map map, string benchDefName, SettlementCompound.WorkSite site, Func<Thing, bool> wants)
        {
            ThingDef? bench = DefDatabase<ThingDef>.GetNamedSilentFail(benchDefName);
            if (bench == null || !bench.IsResearchFinished) return;
            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(bench);
            if (blueprintDef == null) return;
            if (SettlementWorksInitiative.CountBuiltOrPlanned(map, bench) > 0) return;

            Thing? material = FirstWanted(map, wants);
            if (material == null) return;

            int reach = WorkGiver_DoBill.IngredientSearchRadius;
            if (!TryYardSite(map, site, bench, reach, wants, out IntVec3 cell) && !TryNear(map, bench, material.Position, reach, out cell)) return;
            GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), cell, map);
        }

        private static bool TryYardSite(Map.Map map, SettlementCompound.WorkSite site, ThingDef bench, int reach, Func<Thing, bool> wants, out IntVec3 cell)
        {
            cell = default;
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null || !compound.TryFindWorkSite(map, site, bench, out IntVec3 found)) return false;
            foreach (IntVec3 c in CellRect.CenteredOn(found, reach).ClipInsideMap(map).Cells)
            {
                IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                for (int i = 0; i < here.Count; i++)
                {
                    if (!wants(here[i])) continue;
                    cell = found;
                    return true;
                }
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

        private static Thing? FirstWanted(Map.Map map, Func<Thing, bool> wants)
        {
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Spawned && wants(items[i])) return items[i];
            }
            return null;
        }

        private static bool IsButcherableCarcass(Thing t) => t is Corpse corpse && corpse.IsButcherable;

        private static bool IsTailoringStock(Thing t) => IsStuff(t, "Fabric") || IsStuff(t, "Leathery");

        private static bool IsMetalStock(Thing t) => IsStuff(t, "Metallic");

        private static bool IsStuff(Thing t, string category)
        {
            if (t.def.category != ThingCategory.Item || t.stackCount <= 0) return false;
            List<StuffCategoryDef>? categories = t.def.stuffProps?.categories;
            if (categories == null) return false;
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i].defName == category) return true;
            }
            return false;
        }

        private static bool SomeoneWearsNothing(Settlement settlement)
        {
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (!p.Dead && p.apparel != null && p.apparel.WornApparel.Count == 0) return true;
            }
            return false;
        }
    }
}
