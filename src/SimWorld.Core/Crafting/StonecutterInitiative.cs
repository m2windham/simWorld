using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Crafting
{
    /// <summary>Defs this module reads by name rather than through another Def's reference. Its own
    /// <c>[DefOf]</c> class rather than another field on <see cref="ThingCategoryDefOf"/>, per CLAUDE.md:
    /// <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type, so a binding in its own file is wired
    /// exactly as if it sat in the shared one and cannot conflict with a lane editing that file.</summary>
    [DefOf]
    public static class StonecuttingThingCategoryDefOf
    {
        public static ThingCategoryDef StoneBlocks = null!;
    }

    /// <summary>
    /// The settlement decides for itself to cut the stone it has mined: it raises a stonecutter's table where
    /// the chunks are, and keeps a standing bill on it for every kind of stone lying on the map (system:
    /// stonework — rock to usable material).
    ///
    /// <para/><b>Why this class exists at all.</b> Nothing in <c>src/</c> ever created a bill. Every
    /// <see cref="Bill_Production"/> in this repository was made by a test, so
    /// <see cref="WorkGiver_DoBill"/> — fully built, fully tested — found no bill on any bench and no citizen
    /// ever worked one. <c>AI.WorkGiver_ButcherCorpse</c> hit this same wall and routed around the bill
    /// system entirely; its own doc gives two reasons, and only one of them applies here. Butchery's products
    /// depend on the individual corpse, which <see cref="RecipeDef.products"/> (a fixed def-and-count list)
    /// cannot express — that is the reason stonecutting does not share, because one chunk of granite is
    /// exactly like the next. So stonecutting is the case the bill system fits, and what was missing was not
    /// a way around bills but somebody to queue one. This is that somebody, in the same shape
    /// <c>AI.HuntingInitiative</c> and <c>Building.SettlementConstructionInitiative</c> already established
    /// for "RimWorld asks the player, and there is no player".
    ///
    /// <para/><b>The bench goes to the stone, not the stone to the bench.</b>
    /// <see cref="WorkGiver_DoBill"/> only offers a bill whose ingredients already lie within
    /// <see cref="WorkGiver_DoBill.IngredientSearchRadius"/> cells of the bench (that giver's own deliberate
    /// split from a not-yet-built deliver-ingredients-to-a-bill driver). A table placed at a random free cell
    /// would therefore be a table nobody could ever work, unless a stockpile zone happened to exist beside
    /// it — and nothing in this port creates one of those either. So a new table is placed within
    /// <see cref="StonecuttingTuning.BenchPlacementRadius"/> of a chunk that is already on the ground, which
    /// is where a mason would put it.
    ///
    /// <para/><b>What counts as a stonecutting recipe, and as a stonecutter's table.</b> Read off the content
    /// that already says so, never off a new field some other lane would have to merge — the inversion
    /// <c>CompBillGiver</c> and <c>AI.WorkGiver_ButcherCorpse.IsButcherBench</c> both already use. A recipe
    /// is stonecutting when every product it makes is in the <c>StoneBlocks</c>
    /// <see cref="ThingCategoryDef"/>; a bench is a stonecutter's table when
    /// <see cref="Defs.ThingDef.AllRecipes"/> (which is just "who names me in recipeUsers") holds one of
    /// those and it carries a <see cref="CompProperties_BillGiver"/>. Add a fourth stone and its recipe in
    /// content and this finds it with no code change.
    ///
    /// <para/><b>Deliberately not modelled.</b> The civilization-scale half: a <see cref="Guild"/> can hold
    /// the same bills against a settlement's <see cref="Settlement.Stores"/> ledger and the shipped
    /// <c>MasonsGuild</c> already lists <c>Make_Blocks_Sandstone</c>, but nothing establishes a guild and —
    /// more to the point — nothing ever puts a mined chunk into <c>Stores</c>, so a guild queue would
    /// starve. Both are real gaps and neither is this lane's; see its report.
    /// </summary>
    public static class StonecutterInitiative
    {
        /// <summary>Civilization-wide entry point, called once per tick from <c>Sim.Game.WireTickHooks</c>
        /// and cheaply short-circuited by the gate below on every tick but the rare one it fires — the same
        /// shape <c>Building.SettlementConstructionInitiative.Tick</c> uses. A silent no-op with no world
        /// running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements) TickSettlement(settlement);
        }

        /// <summary>One settlement, reading its own <see cref="Settlement.InteriorMap"/>. A settlement nobody
        /// has ever entered has no map and therefore nowhere to put a bench, which is a real, expected state
        /// rather than an error — never a triggered generation, exactly as
        /// <c>SettlementConstructionInitiative</c> reads it.</summary>
        public static void TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            TickMap(map);
        }

        /// <summary>
        /// One map, self-gating on <see cref="StonecuttingTuning.IntervalTicks"/> — the single place every
        /// entry point funnels through, so the interval check has one home. Takes no settlement: everything
        /// this decides is read off the map (what stone is lying on it, what benches stand on it), which is
        /// also why it works unchanged on a map no settlement owns yet.
        /// </summary>
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % StonecuttingTuning.IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated logic. Public so a test (or a future caller entering a settlement scope) can
        /// drive one pass without arranging for the tick number to land on the interval.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            List<RecipeDef> recipes = CuttableRecipesOn(map);
            if (recipes.Count == 0) return; // No stone on the ground: nothing to cut, nothing to build for.

            EnsureBenches(map, recipes);
            EnsureBills(map, recipes);
        }

        // -----------------------------------------------------------------------------------------------
        // What the content says is cuttable, and what is actually lying on this map.
        // -----------------------------------------------------------------------------------------------

        /// <summary>True when every product <paramref name="recipe"/> makes is a stone block — see this
        /// class's own remarks for why this is read off the product category rather than a new Def
        /// field.</summary>
        public static bool IsStonecutting(RecipeDef recipe)
        {
            if (recipe?.products == null || recipe.products.Count == 0) return false;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                List<ThingCategoryDef>? categories = recipe.products[i].thingDef?.thingCategories;
                if (categories == null || !categories.Contains(StonecuttingThingCategoryDefOf.StoneBlocks)) return false;
            }
            return true;
        }

        /// <summary>A building this port would work a stonecutting bill at: it is named by a stonecutting
        /// recipe's <see cref="RecipeDef.recipeUsers"/> and it carries the bill-giving comp that makes a
        /// bench a bench.</summary>
        public static bool IsStonecutterBenchDef(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Building) return false;
            if (!HasBillGiverComp(def)) return false;
            IReadOnlyList<RecipeDef> recipes = def.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (IsStonecutting(recipes[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Every stonecutting recipe the civilization knows and has the stone for right now: researched
        /// (<see cref="RecipeDef.AvailableNow"/>), with at least one chunk of its own ingredient lying on
        /// <paramref name="map"/>, and with a bench Def that could work it. Ordered by defName so two runs of
        /// the same state queue the same bills in the same order — determinism is a feature, and this is the
        /// one place here that could otherwise depend on Def load order.
        /// </summary>
        public static List<RecipeDef> CuttableRecipesOn(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var found = new List<RecipeDef>();
            IReadOnlyList<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef recipe = all[i];
                if (!IsStonecutting(recipe) || !recipe.AvailableNow) continue;
                if (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0) continue;
                if (IngredientOnMap(map, recipe) == null) continue;
                found.Add(recipe);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            return found;
        }

        /// <summary>The first stack on <paramref name="map"/> that satisfies one of
        /// <paramref name="recipe"/>'s fixed ingredients, or null when none is there. Fixed ingredients only:
        /// a category-filter ingredient has no single def to look for, and no shipped stonecutting recipe has
        /// one (each names exactly one <c>Chunk*</c>).</summary>
        public static Thing? IngredientOnMap(Map.Map map, RecipeDef recipe)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (recipe?.ingredients == null) return null;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ThingDef? fixedDef = Guild.FixedDefOf(recipe.ingredients[i]);
                if (fixedDef == null) continue;
                IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(fixedDef);
                for (int s = 0; s < stacks.Count; s++)
                {
                    if (stacks[s].Spawned && stacks[s].stackCount > 0) return stacks[s];
                }
            }
            return null;
        }

        // -----------------------------------------------------------------------------------------------
        // Benches.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Raises a stonecutter's table where the stone is, when the settlement has none and knows how to cut
        /// (<see cref="StonecuttingTuning.TablesWanted"/> — see that class for the arithmetic saying one is
        /// enough at any settlement size). One blueprint per gated call.
        /// </summary>
        private static void EnsureBenches(Map.Map map, List<RecipeDef> recipes)
        {
            ThingDef? benchDef = BenchDefFor(recipes);
            if (benchDef == null || !benchDef.IsResearchFinished) return;
            if (CountBuiltOrPlanned(map, benchDef) >= StonecuttingTuning.TablesWanted) return;

            for (int i = 0; i < recipes.Count; i++)
            {
                Thing? chunk = IngredientOnMap(map, recipes[i]);
                if (chunk == null) continue;
                if (!TryFindPlacementCell(map, benchDef, chunk.Position, out IntVec3 cell)) continue;
                PlaceBlueprint(map, benchDef, cell);
                return;
            }
        }

        /// <summary>The bench Def these recipes are worked at — the first one any of them names, by defName
        /// order, so the choice does not move with Def load order. Content ships exactly one
        /// (<c>TableStonecutter</c>); more than one would mean a settlement picks the alphabetically first,
        /// which is a choice worth making properly the day a second stonecutting bench exists.</summary>
        public static ThingDef? BenchDefFor(List<RecipeDef> recipes)
        {
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));

            ThingDef? best = null;
            for (int i = 0; i < recipes.Count; i++)
            {
                List<ThingDef>? users = recipes[i].recipeUsers;
                if (users == null) continue;
                for (int u = 0; u < users.Count; u++)
                {
                    ThingDef user = users[u];
                    if (!IsStonecutterBenchDef(user)) continue;
                    if (best == null || string.CompareOrdinal(user.defName, best.defName) < 0) best = user;
                }
            }
            return best;
        }

        /// <summary>Already-built benches plus any Blueprint or Frame already under way for one, so a
        /// settlement never plants a second table for one it has already started — the same count
        /// <c>SettlementConstructionInitiative</c> takes before deciding it is short of something.</summary>
        private static int CountBuiltOrPlanned(Map.Map map, ThingDef entityDef)
        {
            int count = map.listerThings.ThingsOfDef(entityDef).Count;

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && ReferenceEquals(bp.EntityToBuild, entityDef)) count++;
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && ReferenceEquals(f.EntityToBuild, entityDef)) count++;
            }

            return count;
        }

        /// <summary>
        /// The first placeable cell within <see cref="StonecuttingTuning.BenchPlacementRadius"/> of
        /// <paramref name="near"/>, scanned in <see cref="CellRect.Cells"/> order rather than sampled at
        /// random: there is no need to spend the shared <see cref="Rand"/> stream on a decision with a
        /// perfectly good deterministic answer, and a bounded rect around a known chunk is already cheap.
        /// Validity is entirely <see cref="GenConstruct.CanPlaceBlueprintAt"/>'s call.
        /// </summary>
        private static bool TryFindPlacementCell(Map.Map map, ThingDef entityDef, IntVec3 near, out IntVec3 cell)
        {
            CellRect scan = CellRect.CenteredOn(near, StonecuttingTuning.BenchPlacementRadius).ClipInsideMap(map);
            foreach (IntVec3 candidate in scan.Cells)
            {
                if (GenConstruct.CanPlaceBlueprintAt(entityDef, candidate, map, out _))
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = default;
            return false;
        }

        private static void PlaceBlueprint(Map.Map map, ThingDef entityDef, IntVec3 cell)
        {
            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(entityDef);
            if (blueprintDef == null) return; // No Blueprint content authored for this Def — nothing to place.
            GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), cell, map);
        }

        // -----------------------------------------------------------------------------------------------
        // Bills.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Gives every stonecutter's table on the map a standing bill for every stone the map holds. A
        /// <see cref="BillRepeatMode.TargetCount"/> bill, so the settlement stops when it has enough and
        /// starts again when the pile runs down — RimWorld's own hysteresis, unchanged, measured by
        /// <c>CompBillGiver.CountProducts</c> against the whole map's stock. Never a second bill for a recipe
        /// the bench already carries: this runs every rare tick for the life of the settlement.
        /// </summary>
        private static void EnsureBills(Map.Map map, List<RecipeDef> recipes)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                Thing bench = buildings[b];
                if (!bench.Spawned) continue;
                CompBillGiver? comp = (bench as ThingWithComps)?.GetComp<CompBillGiver>();
                if (comp == null || !IsStonecutterBenchDef(bench.def)) continue;

                for (int r = 0; r < recipes.Count; r++)
                {
                    RecipeDef recipe = recipes[r];
                    if (!Lists(bench.def.AllRecipes, recipe) || HasBillFor(comp, recipe)) continue;

                    Bill_Production bill = StandingBillFor(recipe);

                    // A target of zero is a bill that would pause the instant it was read: nothing in content
                    // is built out of this product, so the settlement has no reason to want any of it yet.
                    // Better to queue nothing than to queue a bill that can never run and looks like one that
                    // should — the exact shape of defect this lane was opened to close.
                    if (bill.targetCount <= 0) continue;

                    comp.BillStack.AddBill(bill);
                }
            }
        }

        /// <summary>The bill this initiative queues for a recipe. Public because it is the whole content of
        /// the decision: a test that wants to know what the settlement asks for should read it here rather
        /// than restate it.</summary>
        public static Bill_Production StandingBillFor(RecipeDef recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            int wanted = 0;
            if (recipe.products != null)
            {
                for (int i = 0; i < recipe.products.Count; i++)
                {
                    wanted += StoneWallMaterials.BlocksWantedOf(recipe.products[i].thingDef);
                }
            }

            return new Bill_Production(recipe)
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = wanted,

                // Half the target, so masons work in batches rather than restarting the bench every time a
                // wall takes five blocks off the pile. The hysteresis itself is RimWorld's own, already
                // ported; only the width of the band is this port's, and it is a shape ("well below the
                // target") rather than a figure — StonecuttingTests pins that the bill stops when stocked and
                // starts again once stock has fallen well below, never this arithmetic.
                unpauseWhenYouHave = wanted / 2,
            };
        }

        private static bool Lists(IReadOnlyList<RecipeDef> recipes, RecipeDef recipe)
        {
            for (int i = 0; i < recipes.Count; i++)
            {
                if (ReferenceEquals(recipes[i], recipe)) return true;
            }
            return false;
        }

        private static bool HasBillFor(CompBillGiver comp, RecipeDef recipe)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (ReferenceEquals(bills[i].recipe, recipe)) return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------------------------------

        private static bool HasBillGiverComp(ThingDef def)
        {
            if (def.comps == null) return false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i] is CompProperties_BillGiver) return true;
            }
            return false;
        }
    }
}
