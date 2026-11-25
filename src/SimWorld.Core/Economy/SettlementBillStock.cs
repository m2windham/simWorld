using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// <b>The ledger supplies a bench</b>: what a standing bill on a watched map needs and the map cannot give,
    /// <see cref="SettlementStockInitiative"/> issues out of <see cref="Settlement.Stores"/> onto the map beside
    /// the bench.
    ///
    /// <para/><b>Why.</b> RimWorld has no ledger: every unit a colony owns is a Thing on its map, and a crafter
    /// fetches a bill's ingredients from wherever they lie (<c>WorkGiver_DoBill</c>). This port banks what rests
    /// in storage into the ledger (<see cref="SettlementStockInitiative.BankStoredGoods"/>), and only food and
    /// medicine were ever issued back. Banking already keeps a standing bill's ingredients on the map, but
    /// anything banked before the bill was queued, or that reached the ledger by trade, tribute or a guild,
    /// could never reach the bench. A settlement that researched hide coats with its leather in the ledger made
    /// none. This is the bill's half of the crossing, shaped like the food half: the ledger stands in for the
    /// colony's storeroom, and a crafter still walks to the stack and carries it.
    ///
    /// <para/><b>Bounded by one iteration per bill.</b> A bill is supplied only when the map, counting every
    /// stack in its search radius the band can walk to whoever has claimed it, cannot pay for one iteration;
    /// then each ingredient the map is short of is issued for one iteration. The next pass sees it on the map and
    /// issues nothing more until it is spent, so a large ledger cannot empty itself onto the map. Counting
    /// claimed stacks too means a crafter already at work on the last batch does not draw a second.
    ///
    /// <para/><b>What it leaves alone.</b> Food, which <see cref="SettlementStockInitiative.IssueFromStores"/>
    /// issues against the larder the settlement wants and banking takes back above it; issuing it for a cooking
    /// bill as well would have the two rules move the same meal back and forth. A bill whose every short
    /// ingredient the ledger cannot cover issues nothing: half a recipe on the map is a pile nobody can use. A map
    /// none of the settlement's citizens stands on issues nothing either, since nobody is there to craft.
    ///
    /// <para/><b>Where it lands.</b> On free standable cells nearest the bench that the band can walk to, one
    /// stack per cell, as <c>PlaceIssued</c> places food around the granary. A sealed pocket beside the bench
    /// gets nothing. Deterministic: benches in lister order, bills in stack order, ledger defs by count then
    /// defName, cells in <see cref="GenRadial"/>'s fixed pattern, no draw.
    /// </summary>
    public static class SettlementBillStock
    {
        /// <summary>Issues what every standing bill on <paramref name="map"/> is short of for one iteration and
        /// returns the units issued.</summary>
        public static int IssueBillIngredientsFromStores(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (settlement.Stores.Count == 0) return 0;
            IntVec3? band = SettlementReach.BandCell(map);
            if (band == null) return 0;

            int issued = 0;
            IReadOnlyList<Thing> benches = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            for (int b = 0; b < benches.Count; b++)
            {
                Thing bench = benches[b];
                CompBillGiver? comp = WorkGiver_DoBill.BillGiverFor(bench);
                if (comp == null || !bench.Spawned) continue;
                IReadOnlyList<Bill> bills = comp.BillStack.Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    if (bills[i] is not Bill_Production bill || !bill.ShouldDoNow() || !bill.recipe.AvailableNow) continue;
                    issued += SupplyOneIteration(settlement, map, bench, bill, band.Value);
                }
            }
            return issued;
        }

        private static int SupplyOneIteration(Settlement settlement, Map.Map map, Thing bench, Bill_Production bill, IntVec3 band)
        {
            List<IngredientCount>? ingredients = bill.recipe.ingredients;
            if (ingredients == null || ingredients.Count == 0) return 0;

            List<Thing> onMap = WorkGiver_DoBill.IngredientCandidates(bill, bench, t => map.reachability.CanReachCells(band, t.Position));
            var stacks = new List<ItemStack>(onMap.Count);
            for (int i = 0; i < onMap.Count; i++) stacks.Add(new ItemStack(onMap[i].def, onMap[i].stackCount));
            if (BillIngredientsFinder.TryFindBestIngredients(bill, stacks, new List<ItemStack>())) return 0;

            // Plan every short ingredient before placing any, so a recipe the ledger cannot finish issues nothing.
            var plan = new List<(ThingDef def, int units)>();
            IngredientValueGetter value = bill.recipe.IngredientValueGetter;
            for (int s = 0; s < ingredients.Count; s++)
            {
                IngredientCount slot = ingredients[s];
                if (SlotMet(bill, slot, stacks, value)) continue;

                ThingDef? best = null;
                int bestUnits = 0;
                foreach (KeyValuePair<ThingDef, int> held in settlement.Stores)
                {
                    ThingDef def = held.Key;
                    if (def.IsNutritionGivingIngestible || !slot.filter.Allows(def) || !bill.ingredientFilter.Allows(def)) continue;
                    float per = value.ValuePerUnitOf(def);
                    if (per <= 0f) continue;
                    int units = Math.Max(1, (int)Math.Ceiling(slot.GetBaseCount() / per - 0.0001f));
                    if (held.Value - Planned(plan, def) < units) continue;
                    if (best == null || held.Value > settlement.StoreCountOf(best)
                        || (held.Value == settlement.StoreCountOf(best) && string.CompareOrdinal(def.defName, best.defName) < 0))
                    {
                        best = def;
                        bestUnits = units;
                    }
                }
                if (best == null) return 0;
                plan.Add((best, bestUnits));
            }

            int issued = 0;
            for (int p = 0; p < plan.Count; p++)
            {
                int placed = PlaceBeside(plan[p].def, plan[p].units, map, bench, band, bill.ingredientSearchRadius);
                // Debit in the same step that spawns, as every crossing of the ledger does.
                settlement.AddStore(plan[p].def, -placed);
                issued += placed;
            }
            return issued;
        }

        /// <summary>Whether the map's stacks alone could fill <paramref name="slot"/>: one def enough on its own,
        /// or all of them together where the recipe mixes.</summary>
        private static bool SlotMet(Bill bill, IngredientCount slot, List<ItemStack> stacks, IngredientValueGetter value)
        {
            float need = slot.GetBaseCount();
            var byDef = new Dictionary<ThingDef, float>();
            float total = 0f;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (!slot.filter.Allows(stacks[i]) || !bill.ingredientFilter.Allows(stacks[i])) continue;
                float v = stacks[i].Count * value.ValuePerUnitOf(stacks[i].Def);
                byDef.TryGetValue(stacks[i].Def, out float had);
                byDef[stacks[i].Def] = had + v;
                total += v;
            }
            if (bill.recipe.allowMixingIngredients) return total >= need - 0.0001f;
            foreach (float v in byDef.Values)
            {
                if (v >= need - 0.0001f) return true;
            }
            return false;
        }

        private static int Planned(List<(ThingDef def, int units)> plan, ThingDef def)
        {
            int n = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].def == def) n += plan[i].units;
            }
            return n;
        }

        /// <summary>Spawns up to <paramref name="count"/> of <paramref name="def"/> on free standable cells nearest
        /// <paramref name="bench"/> that the band can walk to, never above the def's stack limit, inside the
        /// bill's reach of the bench. Returns the units placed.</summary>
        private static int PlaceBeside(ThingDef def, int count, Map.Map map, Thing bench, IntVec3 band, float radius)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            int remaining = count;
            int placed = 0;
            for (int i = 0; i < pattern.Count && remaining > 0; i++)
            {
                if (pattern[i].LengthHorizontalSquared > radius * radius) break;
                IntVec3 cell = bench.Position + pattern[i];
                if (!GenGrid.InBounds(cell, map) || !GenGrid.Standable(cell, map)) continue;
                if (bench.OccupiedRect().Contains(cell) || HoldsItem(map, cell)) continue;
                if (!map.reachability.CanReachCells(band, cell)) continue;

                int stack = Math.Min(remaining, Math.Max(1, def.stackLimit));
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = stack;
                GenSpawn.Spawn(thing, cell, map);
                remaining -= stack;
                placed += stack;
            }
            return placed;
        }

        private static bool HoldsItem(Map.Map map, IntVec3 c)
        {
            foreach (Thing t in map.thingGrid.ThingsAt(c))
            {
                if (t.def.category == ThingCategory.Item) return true;
            }
            return false;
        }
    }
}
