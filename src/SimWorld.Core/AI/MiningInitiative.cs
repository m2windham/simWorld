using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.AI
{
    public static class MiningTuning
    {
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>Most Mine designations a settlement keeps open at once. Not sourced: RimWorld's player
        /// designates as much as they like. Enough faces for several miners to work side by side, few enough that
        /// a need for stone puts a work party on it rather than the whole band.</summary>
        public const int MaxOutstandingDesignations = 12;

        /// <summary>How many iterations of a Forever bill count as demand. Not sourced: a Forever bill has no end,
        /// and two iterations keep its bench supplied without asking for a mountain.</summary>
        public const int ForeverBillIterations = 2;
    }

    /// <summary>
    /// The settlement as the player who places <c>Mine</c> designations (RimWorld: the player, with the
    /// <c>Designator_Mine</c>). <see cref="WorkGiver_Miner"/> only mines designated rock, as RimWorld's does, so
    /// this decides where a settlement digs and how much, on the rare tick, the same shape as
    /// <see cref="HuntingInitiative"/> deciding whether it hunts. Two reasons to dig, in this order:
    /// <list type="number">
    /// <item><b>Clearing.</b> Rock standing on the ground the compound has planned (<see cref="SettlementCompound.Village"/>:
    /// the ring, the work yard, the dwelling plots) is in the way of the settlement's own plan.</item>
    /// <item><b>Need.</b> What the blueprints and frames on the map still need, and what the bills on its benches
    /// will consume, less what is already on the map and in <see cref="Settlement.Stores"/>, counted in the
    /// things rock yields when mined (chunks, ore). A need for something a recipe makes out of a mined yield
    /// (stone blocks, cut from chunks) counts as a need for that yield. Each short yield gets as many cells as
    /// its expected yield per cell says it takes, less what designations already open will bring.</item>
    /// </list>
    /// Only rock a miner can stand next to now is designated (a cell deeper in the face waits until the rock in
    /// front of it is gone), never rock that would bring a roof down if removed, nearest the compound first, and
    /// never more than <see cref="MiningTuning.MaxOutstandingDesignations"/> open at once. A designation that no
    /// longer has rock under it, or whose rock now holds a roof up, is withdrawn.
    /// </summary>
    public static class MiningInitiative
    {
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % MiningTuning.IntervalTicks != 0) return;
            Run(map);
        }

        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Settlement? settlement = HuntingInitiative.SettlementFor(map);
            if (settlement != null) Run(settlement, map);
        }

        public static void Run(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            DesignationManager dm = map.designationManager;
            Withdraw(map);
            int room = MiningTuning.MaxOutstandingDesignations - dm.SpawnedDesignationsOfDef(DesignationDefOf.Mine).Count;
            if (room <= 0) return;

            SettlementCompound? compound = map.settlementCompound;
            IntVec3 anchor = compound != null ? Center(compound.Interior) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            List<Thing> faces = OpenFacesByDistance(map, anchor);

            if (compound != null)
            {
                CellRect village = compound.Village;
                room = Designate(map, faces, room, rock => village.Contains(rock.Position), int.MaxValue);
            }

            foreach (KeyValuePair<ThingDef, int> need in YieldShortfall(settlement, map))
            {
                if (room <= 0) break;
                float perCell = ExpectedYieldPerCell(need.Key);
                if (perCell <= 0f) continue;
                float coming = DesignatedCellsYielding(map, need.Key) * perCell;
                int cells = (int)Math.Ceiling((need.Value - coming) / perCell);
                if (cells <= 0) continue;
                room = Designate(map, faces, room, rock => rock.def.mineableThing == need.Key, cells);
            }
        }

        /// <summary>
        /// What the map's blueprints, frames and bills still need, in things rock yields, less what the map and
        /// the settlement's stores already hold; only the positive shortfalls, ordered by def name.
        /// </summary>
        public static List<KeyValuePair<ThingDef, int>> YieldShortfall(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var gross = new Dictionary<ThingDef, int>();
            foreach (KeyValuePair<ThingDef, int> need in MaterialsNeeded(map))
            {
                if (IsMinedYield(need.Key))
                {
                    Add(gross, need.Key, need.Value);
                    continue;
                }
                // Made from a mined yield (blocks from chunks): what the map does not already hold of the made
                // thing becomes a need for the yield it is made from.
                int shortOfMade = need.Value - Available(settlement, map, need.Key);
                if (shortOfMade <= 0 || !TryFindRecipeFromYield(need.Key, out RecipeDef? recipe, out ThingDef? yield, out int perIteration, out int yieldPerIteration)) continue;
                int iterations = (shortOfMade + perIteration - 1) / perIteration;
                Add(gross, yield!, iterations * yieldPerIteration);
            }

            var result = new List<KeyValuePair<ThingDef, int>>();
            foreach (KeyValuePair<ThingDef, int> kv in gross)
            {
                int shortfall = kv.Value - Available(settlement, map, kv.Key);
                if (shortfall > 0) result.Add(new KeyValuePair<ThingDef, int>(kv.Key, shortfall));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Key.defName, b.Key.defName));
            return result;
        }

        /// <summary>Every material the blueprints and frames on the map still need delivered, plus the fixed
        /// ingredients of every bill a bench would start now (RepeatCount: its remaining count; TargetCount: up to
        /// its target; Forever: <see cref="MiningTuning.ForeverBillIterations"/>).</summary>
        public static Dictionary<ThingDef, int> MaterialsNeeded(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var needs = new Dictionary<ThingDef, int>();
            IReadOnlyList<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t is Blueprint blueprint)
                {
                    foreach (ThingDefCountClass c in blueprint.TotalMaterialCost()) Add(needs, c.thingDef, c.count);
                }
                else if (t is Frame frame)
                {
                    foreach (ThingDefCountClass c in frame.TotalMaterialCost()) Add(needs, c.thingDef, frame.MaterialStillNeeded(c.thingDef));
                }
                else if (t is ThingWithComps withComps && withComps.GetComp<CompBillGiver>() is CompBillGiver giver)
                {
                    foreach (Bill bill in giver.BillStack.Bills)
                    {
                        int iterations = IterationsWanted(bill, giver);
                        if (iterations <= 0 || bill.recipe.ingredients == null) continue;
                        foreach (IngredientCount ingredient in bill.recipe.ingredients)
                        {
                            ThingDef? fixedDef = Guild.FixedDefOf(ingredient);
                            if (fixedDef != null) Add(needs, fixedDef, (int)Math.Ceiling(ingredient.count) * iterations);
                        }
                    }
                }
            }
            return needs;
        }

        /// <summary>Is <paramref name="def"/> what some rock yields when mined?</summary>
        public static bool IsMinedYield(ThingDef def)
        {
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].mineable && all[i].mineableThing == def) return true;
            }
            return false;
        }

        /// <summary>A cell's yield on average: <c>mineableYield</c> times <c>mineableDropChance</c> times the
        /// difficulty's mine yield factor (<see cref="MineableUtility.YieldFor"/> without the miner's stat).</summary>
        public static float ExpectedYieldPerCell(ThingDef yield)
        {
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            float best = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef rock = all[i];
                if (!rock.mineable || rock.mineableThing != yield || rock.mineableYield <= 0) continue;
                best = Math.Max(best, rock.mineableYield * rock.mineableDropChance * (Find.Storyteller.difficulty?.mineYieldFactor ?? 1f));
            }
            return best;
        }

        private static int IterationsWanted(Bill bill, CompBillGiver giver)
        {
            if (bill.suspended || !(bill is Bill_Production production) || production.paused) return 0;
            switch (production.repeatMode)
            {
                case BillRepeatMode.RepeatCount:
                    return production.repeatCount;
                case BillRepeatMode.TargetCount:
                {
                    int owned = giver.CountProducts(production);
                    int perIteration = bill.recipe.products != null && bill.recipe.products.Count > 0 ? Math.Max(1, bill.recipe.products[0].count) : 1;
                    return Math.Max(0, (production.targetCount - owned + perIteration - 1) / perIteration);
                }
                default:
                    return MiningTuning.ForeverBillIterations;
            }
        }

        /// <summary>The first recipe, by def name, that makes <paramref name="made"/> out of a fixed ingredient that is
        /// a mined yield.</summary>
        private static bool TryFindRecipeFromYield(ThingDef made, out RecipeDef? recipe, out ThingDef? yield, out int perIteration, out int yieldPerIteration)
        {
            recipe = null;
            yield = null;
            perIteration = yieldPerIteration = 0;
            IReadOnlyList<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef r = all[i];
                if (r.products == null || r.ingredients == null) continue;
                ThingDefCountClass? product = r.products.Find(p => p.thingDef == made);
                if (product == null || product.count <= 0) continue;
                foreach (IngredientCount ingredient in r.ingredients)
                {
                    ThingDef? fixedDef = Guild.FixedDefOf(ingredient);
                    if (fixedDef == null || !IsMinedYield(fixedDef)) continue;
                    if (recipe != null && string.CompareOrdinal(r.defName, recipe.defName) >= 0) continue;
                    recipe = r;
                    yield = fixedDef;
                    perIteration = product.count;
                    yieldPerIteration = (int)Math.Ceiling(ingredient.count);
                }
            }
            return recipe != null;
        }

        private static int Available(Settlement? settlement, Map.Map map, ThingDef def)
        {
            int total = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) total += stacks[i].stackCount;
            }
            if (settlement != null && settlement.Stores.TryGetValue(def, out int stored)) total += stored;
            return total;
        }

        private static int DesignatedCellsYielding(Map.Map map, ThingDef yield)
        {
            int n = 0;
            IReadOnlyList<Designation> mine = map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine);
            for (int i = 0; i < mine.Count; i++)
            {
                if (WorkGiver_Miner.FirstMineable(mine[i].target.Cell, map)?.def.mineableThing == yield) n++;
            }
            return n;
        }

        /// <summary>Withdraws every Mine designation with no rock under it any more, or whose rock now holds a roof
        /// up and so could not be mined anyway.</summary>
        private static void Withdraw(Map.Map map)
        {
            DesignationManager dm = map.designationManager;
            IReadOnlyList<Designation> mine = dm.SpawnedDesignationsOfDef(DesignationDefOf.Mine);
            for (int i = mine.Count - 1; i >= 0; i--)
            {
                Thing? rock = WorkGiver_Miner.FirstMineable(mine[i].target.Cell, map);
                if (rock == null || RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock)) dm.RemoveDesignation(mine[i]);
            }
        }

        /// <summary>Undesignated mineable rock a miner could stand next to now, nearest <paramref name="anchor"/>
        /// first (ties in list order).</summary>
        private static List<Thing> OpenFacesByDistance(Map.Map map, IntVec3 anchor)
        {
            var faces = new List<Thing>();
            var keys = new List<long>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            DesignationManager dm = map.designationManager;
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing t = buildings[i];
                if (!t.def.mineable || !t.Spawned || !WorkGiver_Miner.MayBeAccessible(t.Position, map)) continue;
                if (dm.DesignationAt(t.Position, DesignationDefOf.Mine) != null) continue;
                keys.Add(((long)(t.Position - anchor).LengthHorizontalSquared << 32) | (uint)faces.Count);
                faces.Add(t);
            }
            keys.Sort();
            var ordered = new List<Thing>(faces.Count);
            for (int i = 0; i < keys.Count; i++) ordered.Add(faces[(int)(keys[i] & 0xFFFFFFFF)]);
            return ordered;
        }

        /// <summary>Designates up to <paramref name="wanted"/> of <paramref name="faces"/> that <paramref name="accepts"/>,
        /// in order, skipping any whose removal would bring a roof down; returns the room left under the cap.</summary>
        private static int Designate(Map.Map map, List<Thing> faces, int room, Func<Thing, bool> accepts, int wanted)
        {
            for (int i = 0; i < faces.Count && room > 0 && wanted > 0; i++)
            {
                Thing rock = faces[i];
                if (!accepts(rock) || RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock)) continue;
                map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(rock.Position), DesignationDefOf.Mine));
                faces.RemoveAt(i--);
                room--;
                wanted--;
            }
            return room;
        }

        private static IntVec3 Center(CellRect r) => new IntVec3(r.minX + r.Width / 2, 0, r.minZ + r.Height / 2);

        private static void Add(Dictionary<ThingDef, int> d, ThingDef def, int count)
        {
            if (count <= 0) return;
            d[def] = (d.TryGetValue(def, out int n) ? n : 0) + count;
        }
    }
}
