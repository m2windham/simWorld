using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Crafting
{
    /// <summary>
    /// The settlement decides for itself to smelt (system: crafting.materialladder): it works back from the
    /// metal its smithy wants to the recipes that make it, raises the furnace those recipes need, and keeps a
    /// standing bill on it for each step. RimWorld's player queues "smelt until you have X" at an electric
    /// smelter; there is no player here, the same translation <see cref="StonecutterInitiative"/> made for
    /// cutting stone.
    ///
    /// <para/><b>Demand, not stockpiling.</b> The only demand is <see cref="MaterialLadder.WeaponStuffWanted"/>:
    /// what the smithy's weapon bill still has to make, in metal, less what is already on the map. Nothing is
    /// smelted for its own sake, so no ore is dug and no furnace built until a citizen would carry the result.
    /// Each metal is resolved back through <see cref="MaterialLadder.BestRecipeFor"/> — bronze needs copper and
    /// tin, copper needs copper ore — and each step's bill is sized to the step after it. The ore at the bottom
    /// of the chain is <see cref="AI.MiningInitiative"/>'s to dig, which it already does for any bill's fixed
    /// ingredient.
    ///
    /// <para/><b>One bill per step, on one bench.</b> A step's bill goes on the first built bench that lists its
    /// recipe, newest technique first; when none is built or under way, the newest researched one is placed —
    /// beside the smithy, so the smith and the smelter share their metal (a bill's ingredients are only searched
    /// for within <see cref="WorkGiver_DoBill.IngredientSearchRadius"/> of its bench), with a small stockpile
    /// that takes the bench's inputs, as <see cref="SettlementWorkshops"/> keeps beside a smithy. Every other
    /// metallurgy bill is deleted, so a settlement that moves to the electric smelter stops cementing steel at
    /// its bloomery, and one whose need is met holds no bill at all.
    ///
    /// <para/>Stateless: bills save with their bench.
    /// </summary>
    public static class SmeltingInitiative
    {
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>How far from the smithy a furnace is placed. Unsourced; inside the bill reach of
        /// <see cref="WorkGiver_DoBill.IngredientSearchRadius"/> with room for the furnace's own stockpile, so
        /// one smith can reach the other's metal. Pinned by a test that the furnace lands within reach, not by
        /// this number.</summary>
        public const int NearSmithyRadius = 5;

        /// <summary>How far from its bench a furnace's stock pile may lie: half the bill's ingredient reach
        /// (<see cref="WorkGiver_DoBill.IngredientSearchRadius"/>), so the pile is always well inside it.</summary>
        public const int StockSearchRadius = WorkGiver_DoBill.IngredientSearchRadius / 2;

        /// <summary>Cells in the stockpile kept beside a furnace, as <see cref="SettlementWorkshops.BenchStockCells"/>.</summary>
        public const int StockCells = SettlementWorkshops.BenchStockCells;

        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated logic, public so a test can drive one pass.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Dictionary<RecipeDef, int> plan = Plan(map);
            foreach (KeyValuePair<RecipeDef, int> step in Ordered(plan))
            {
                ThingWithComps? bench = EnsureBench(map, step.Key);
                if (bench == null) continue;
                EnsureStock(map, bench);
                SetBill(bench.GetComp<CompBillGiver>()!, step.Key, step.Value);
            }
            DeleteOtherBills(map, plan);
            PutStockedBillsFirst(map);
            KeepMetalworkAtTheForge(map);
            MaterialLadder.UpgradeBlueprints(map);
        }

        /// <summary>
        /// Takes ore and metal out of the granary's filter once a bench stock pile beside a furnace or the smithy
        /// takes them, so haulers carry them there (a stack in storage that allows it is never moved again:
        /// <c>HaulAIUtility.IsInValidStorage</c>). The bill's ingredient reach is a port-wide 12 cells
        /// (<see cref="WorkGiver_DoBill.IngredientSearchRadius"/>; RimWorld's default is 999), and the granary sits
        /// wherever the compound put it: measured on seed 105, 24 iron ore lay in the granary 21 cells from the
        /// bloomery for sixty days while its bill stood. A runtime change to the zone, which Scribes with it; no
        /// edit to the stock initiative that owns the granary. Returns how many defs were withdrawn this pass.
        /// </summary>
        public static int KeepMetalworkAtTheForge(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Zone_Stockpile? granary = Economy.SettlementStockInitiative.GranaryOf(map);
            if (granary == null) return 0;

            var benchStock = new List<Zone_Stockpile>();
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int z = 0; z < zones.Count; z++)
            {
                if (zones[z] is Zone_Stockpile pile && pile.CellCount > 0 && !ReferenceEquals(pile, granary)
                    && pile.label != null && pile.label.EndsWith(" stock", StringComparison.Ordinal))
                {
                    benchStock.Add(pile);
                }
            }
            if (benchStock.Count == 0) return 0;

            int withdrawn = 0;
            IReadOnlyList<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int r = 0; r < recipes.Count; r++)
            {
                if (!MaterialLadder.IsMetallurgy(recipes[r])) continue;
                foreach (IngredientCount ingredient in recipes[r].ingredients!)
                {
                    if (Guild.FixedDefOf(ingredient) is ThingDef input) withdrawn += Withdraw(granary, benchStock, input);
                }
                foreach (ThingDefCountClass product in recipes[r].products!)
                {
                    withdrawn += Withdraw(granary, benchStock, product.thingDef);
                }
            }
            return withdrawn;
        }

        private static int Withdraw(Zone_Stockpile granary, List<Zone_Stockpile> benchStock, ThingDef def)
        {
            if (!granary.filter.Allows(def)) return 0;
            for (int i = 0; i < benchStock.Count; i++)
            {
                if (benchStock[i].filter.Allows(def))
                {
                    granary.filter.SetAllow(def, false);
                    return 1;
                }
            }
            return 0;
        }

        /// <summary>
        /// Every metallurgy step the settlement should be working, with the target count of its product: for
        /// each metal the smithies want, the best recipe for it, then the same for each of that recipe's inputs
        /// that is itself smelted, sized to what the step above still needs.
        /// </summary>
        public static Dictionary<RecipeDef, int> Plan(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var plan = new Dictionary<RecipeDef, int>();
            var deficits = new List<KeyValuePair<ThingDef, int>>(MaterialLadder.WeaponStuffWanted(map));
            deficits.Sort((a, b) => string.CompareOrdinal(a.Key.defName, b.Key.defName));
            foreach (KeyValuePair<ThingDef, int> want in deficits)
            {
                if (want.Value > 0) Resolve(map, want.Key, want.Value, MaterialLadder.ChainDepth, plan);
            }
            return plan;
        }

        private static void Resolve(Map.Map map, ThingDef metal, int deficit, int depth, Dictionary<RecipeDef, int> plan)
        {
            if (depth <= 0 || deficit <= 0) return;
            RecipeDef? recipe = MaterialLadder.BestRecipeFor(map, metal);
            if (recipe == null) return;
            int per = MaterialLadder.ProductCount(recipe, metal);
            if (per <= 0) return;
            int iterations = (deficit + per - 1) / per;

            // The target a TargetCount bill counts against: what is on the map now plus what is still short, so
            // the bill stops the moment the shortfall is made.
            int target = MaterialLadder.OnMap(map, metal) + deficit;
            plan[recipe] = plan.TryGetValue(recipe, out int already) ? Math.Max(already, target) : target;

            foreach (IngredientCount ingredient in recipe.ingredients!)
            {
                ThingDef? input = Guild.FixedDefOf(ingredient);
                if (input == null) continue;
                int need = iterations * (int)Math.Ceiling(ingredient.count) - MaterialLadder.OnMap(map, input);
                Resolve(map, input, need, depth - 1, plan);
            }
        }

        private static List<KeyValuePair<RecipeDef, int>> Ordered(Dictionary<RecipeDef, int> plan)
        {
            var list = new List<KeyValuePair<RecipeDef, int>>(plan);
            list.Sort((a, b) => string.CompareOrdinal(a.Key.defName, b.Key.defName));
            return list;
        }

        // -----------------------------------------------------------------------------------------------
        // Benches.
        // -----------------------------------------------------------------------------------------------

        /// <summary>The built bench <paramref name="recipe"/>'s bill goes on, or null while none is built — in
        /// which case one is placed (the newest researched bench that lists the recipe), unless one is already
        /// under way.</summary>
        public static ThingWithComps? EnsureBench(Map.Map map, RecipeDef recipe)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            ThingWithComps? built = BuiltBenchFor(map, recipe);
            if (built != null) return built;
            List<ThingDef> users = UsersNewestFirst(recipe);
            for (int u = 0; u < users.Count; u++)
            {
                if (SettlementWorksInitiative.CountBuiltOrPlanned(map, users[u]) > 0) return null;
            }
            for (int u = 0; u < users.Count; u++)
            {
                if (!users[u].IsResearchFinished || GenConstruct.BlueprintDefFor(users[u]) == null) continue;
                Place(map, users[u]);
                return null;
            }
            return null;
        }

        /// <summary>The bill-giving benches that list <paramref name="recipe"/>, the newest research first (then
        /// defName).</summary>
        public static List<ThingDef> UsersNewestFirst(RecipeDef recipe)
        {
            var users = new List<ThingDef>();
            if (recipe.recipeUsers == null) return users;
            foreach (ThingDef user in recipe.recipeUsers)
            {
                if (user?.comps != null && user.comps.Exists(c => c is CompProperties_BillGiver)) users.Add(user);
            }
            users.Sort((a, b) =>
            {
                int byResearch = ResearchCost(b).CompareTo(ResearchCost(a));
                return byResearch != 0 ? byResearch : string.CompareOrdinal(a.defName, b.defName);
            });
            return users;
        }

        private static float ResearchCost(ThingDef def)
        {
            float cost = 0f;
            if (def.researchPrerequisites == null) return cost;
            foreach (Research.ResearchProjectDef r in def.researchPrerequisites) cost = Math.Max(cost, r.baseCost);
            return cost;
        }

        /// <summary>Beside the smithy when one stands, so the furnace's metal is within the smith's reach; else at
        /// the middle of the compound, else the middle of the map. Never on ground the compound keeps.</summary>
        private static void Place(Map.Map map, ThingDef benchDef)
        {
            IntVec3 near = map.settlementCompound != null
                ? CenterOf(map.settlementCompound.Interior)
                : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            int radius = WorkGiver_DoBill.IngredientSearchRadius;
            ThingDef? smithy = DefDatabase<ThingDef>.GetNamedSilentFail(SettlementWorkshops.SmithyDefName);
            if (smithy != null)
            {
                IReadOnlyList<Thing> smithies = map.listerThings.ThingsOfDef(smithy);
                if (smithies.Count > 0)
                {
                    near = smithies[0].Position;
                    radius = NearSmithyRadius;
                }
            }

            foreach (IntVec3 c in CellRect.CenteredOn(near, radius).ClipInsideMap(map).Cells)
            {
                if (SettlementCompound.Refuses(map, benchDef, c)) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(benchDef, c, map, out _)) continue;
                var blueprint = (Blueprint)ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(benchDef)!);
                blueprint.stuffToUse = GenStuff.AffordableStuffFor(map, benchDef);
                GenSpawn.Spawn(blueprint, c, map);
                return;
            }
        }

        private static IntVec3 CenterOf(CellRect r) => new IntVec3(r.minX + r.Width / 2, 0, r.minZ + r.Height / 2);

        /// <summary>A stockpile of <see cref="StockCells"/> cells beside <paramref name="bench"/> taking its
        /// recipes' fixed inputs, so haulers bring the ore a miner dug to the furnace that smelts it, and banking
        /// leaves it while a bill wants it (<see cref="SettlementWorkshops"/>' bench stock, for a furnace).</summary>
        private static void EnsureStock(Map.Map map, Thing bench)
        {
            string label = bench.def.label + " stock";
            Zone_Stockpile? pile = null;
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int z = 0; z < zones.Count; z++)
            {
                if (zones[z] is Zone_Stockpile s && s.label == label) pile = s;
            }
            if (pile == null)
            {
                pile = new Zone_Stockpile { label = label };
                pile.filter.SetDisallowAll();
                IReadOnlyList<RecipeDef> recipes = bench.def.AllRecipes;
                for (int r = 0; r < recipes.Count; r++)
                {
                    if (recipes[r].ingredients == null) continue;
                    foreach (IngredientCount ingredient in recipes[r].ingredients!)
                    {
                        ThingDef? input = Guild.FixedDefOf(ingredient);
                        if (input != null) pile.filter.SetAllow(input, true);
                    }
                }
                if (pile.filter.AllowedDefCount == 0) return;
                map.zoneManager.RegisterZone(pile);
            }

            // Ring by ring outward from the bench, as far as its ingredient reach: a furnace raised beside the
            // smithy finds the cells next to it taken by the smithy's own stock (measured: seed 105 left the
            // bloomery's pile with no cells at radius 2, and 24 iron ore lay in the granary 21 cells away).
            CellRect occupied = bench.OccupiedRect();
            for (int radius = 2; radius <= StockSearchRadius && pile.CellCount < StockCells; radius++)
            {
                foreach (IntVec3 c in CellRect.CenteredOn(bench.Position, radius).ClipInsideMap(map).Cells)
                {
                    if (pile.CellCount >= StockCells) return;
                    if (occupied.Contains(c) || !GenGrid.Standable(c, map)) continue;
                    if (map.zoneManager.ZoneAt(c) != null || GenGrid.GetEdifice(c, map) != null) continue;
                    if (HasSiteAt(map, c)) continue;
                    map.zoneManager.AddCell(pile, c);
                }
            }
        }

        private static bool HasSiteAt(Map.Map map, IntVec3 c)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Blueprint || here[i] is Frame) return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------------------------------
        // Bills.
        // -----------------------------------------------------------------------------------------------

        private static void SetBill(CompBillGiver comp, RecipeDef recipe, int target)
        {
            Bill_Production? bill = BillFor(comp, recipe);
            if (bill == null)
            {
                bill = new Bill_Production(recipe) { repeatMode = BillRepeatMode.TargetCount };
                comp.BillStack.AddBill(bill);
            }
            bill.targetCount = target;
            // Work until the shortfall is made: the target already counts what is on the map.
            bill.unpauseWhenYouHave = target - 1;
        }

        /// <summary>Deletes every metallurgy bill on the map that is not this pass's plan for that bench.</summary>
        private static void DeleteOtherBills(Map.Map map, Dictionary<RecipeDef, int> plan)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!(buildings[b] is ThingWithComps bench) || bench.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                IReadOnlyList<Bill> bills = comp.BillStack.Bills;
                for (int i = bills.Count - 1; i >= 0; i--)
                {
                    Bill bill = bills[i];
                    if (!MaterialLadder.IsMetallurgy(bill.recipe)) continue;
                    if (plan.ContainsKey(bill.recipe) && ReferenceEquals(BuiltBenchFor(map, bill.recipe), bench)) continue;
                    comp.BillStack.Delete(bill);
                }
            }
        }

        /// <summary>
        /// Moves each furnace's metallurgy bills whose inputs are all on the map above those still waiting on
        /// theirs. <c>WorkGiver_DoBill.FindBill</c> takes the first bill a pawn may start and gives up if its
        /// ingredients are missing, where RimWorld's <c>TryStartNewDoBillJob</c> goes on to the next bill. So a
        /// cementation bill with no iron above the smelting bill that would make the iron stalls the chain
        /// (measured: seed 31337, 1,253 iron ore unsmelted beside the bloomery from day 150). Reordering each
        /// pass keeps the fix inside this lane; the shared work giver is left as it is.
        /// </summary>
        internal static void PutStockedBillsFirst(Map.Map map)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!(buildings[b] is ThingWithComps bench) || bench.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                BillStack stack = comp.BillStack;
                int top = 0;
                for (int i = 0; i < stack.Count; i++)
                {
                    Bill bill = stack[i];
                    if (!MaterialLadder.IsMetallurgy(bill.recipe) || !InputsOnMap(map, bill.recipe)) continue;
                    for (int moves = i - top; moves > 0; moves--) stack.Reorder(bill, -1);
                    top++;
                }
            }
        }

        private static bool InputsOnMap(Map.Map map, RecipeDef recipe)
        {
            if (recipe.ingredients == null) return false;
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                ThingDef? input = Guild.FixedDefOf(ingredient);
                if (input == null || MaterialLadder.OnMap(map, input) < (int)Math.Ceiling(ingredient.count)) return false;
            }
            return true;
        }

        /// <summary>The built bench a recipe's bill belongs on, without placing anything.</summary>
        public static ThingWithComps? BuiltBenchFor(Map.Map map, RecipeDef recipe)
        {
            List<ThingDef> users = UsersNewestFirst(recipe);
            for (int u = 0; u < users.Count; u++)
            {
                IReadOnlyList<Thing> built = map.listerThings.ThingsOfDef(users[u]);
                for (int i = 0; i < built.Count; i++)
                {
                    if (built[i].Spawned && built[i] is ThingWithComps bench && bench.GetComp<CompBillGiver>() != null) return bench;
                }
            }
            return null;
        }

        private static Bill_Production? BillFor(CompBillGiver comp, RecipeDef recipe)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i] is Bill_Production production && ReferenceEquals(production.recipe, recipe)) return production;
            }
            return null;
        }
    }
}
