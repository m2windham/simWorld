using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>Defs the material ladder reads by name. Its own <c>[DefOf]</c> class, per CLAUDE.md.</summary>
    [DefOf]
    public static class MaterialLadderDefOf
    {
        public static StatDef SharpDamageMultiplier = null!;
        public static StuffCategoryDef Metallic = null!;
        public static StuffCategoryDef Woody = null!;
        public static ThingDef Door = null!;
    }

    /// <summary>One weapon the smithy should make now: which recipe, out of which stuff, for how many citizens.</summary>
    public sealed class WeaponPlan
    {
        public WeaponPlan(RecipeDef recipe, ThingDef stuff, float score)
        {
            Recipe = recipe;
            Stuff = stuff;
            Score = score;
        }

        /// <summary>The best weapon recipe the bench can work now.</summary>
        public RecipeDef Recipe { get; }

        /// <summary>The best stuff that recipe can be made of that the settlement holds or can get: the metal
        /// the settlement is working toward.</summary>
        public ThingDef Stuff { get; }

        /// <summary><see cref="MaterialLadder.WeaponScore(ThingDef, ThingDef?)"/> of the product in <see cref="Stuff"/>.</summary>
        public float Score { get; }

        /// <summary>Stuff one product takes: the recipe's single ingredient count.</summary>
        public int StuffPerProduct => Recipe.ingredients != null && Recipe.ingredients.Count > 0 ? (int)Math.Ceiling(Recipe.ingredients[0].count) : 0;
    }

    /// <summary>
    /// The material ladder (system: crafting.materialladder): which material is better than which, and so what a
    /// settlement smiths, smelts and builds out of as its eras turn.
    ///
    /// <para/><b>The RimWorld idea.</b> A thing made from stuff is as good as its stuff: RimWorld multiplies a
    /// blade's Cut and Stab damage by its material's <c>SharpDamageMultiplier</c> and a building's hit points by
    /// its <c>MaxHitPoints</c> factor, and its player picks the best material in stock in the bill and the
    /// architect menu. There is no player here, so this class is that choice, read off the same two numbers.
    ///
    /// <para/><b>Shaped by the arc.</b> In RimWorld the best metal is in the ground from day one. Here it is
    /// what the era can make: a material counts once the settlement holds a whole product's worth of it, or can
    /// get it — a vein of it a miner can reach, or a researched recipe whose own inputs it can get in turn
    /// (copper ore to copper to bronze). So the same rule gives flint knives before copper, bronze spears in the
    /// bronze age, iron spears in the iron age and steel longswords in the medieval one, with no era written
    /// down anywhere in this class.
    ///
    /// <para/><b>Melee weapons are ranked</b> by their tools' mean power per second of cooldown — RimWorld's own
    /// way of reading a melee weapon, its DPS, unweighted by tool choice — times the stuff's
    /// <c>SharpDamageMultiplier</c>. A stuff-made weapon with no stuff (a raider's, a scenario's) is ranked as
    /// steel, RimWorld's default melee stuff. The verb does not yet apply the multiplier in a fight (see
    /// <c>Stats_MeleeStuff.xml</c>); the ranking does, so the settlement already arms itself the way it will
    /// matter.
    ///
    /// <para/><b>Stateless.</b> Everything is re-derived from the map on the rare tick; the only state is the
    /// bills it writes, which their bench saves.
    /// </summary>
    public static partial class MaterialLadder
    {
        /// <summary>How much better a weapon has to be for a citizen to trade up to it: strictly better, with a
        /// sliver of float slack so two identical knives never trade places.</summary>
        public const float TradeUpMargin = 1.001f;

        /// <summary>How many recipe steps back from a metal its inputs are looked for (ore to copper to bronze is
        /// two). Bounds the recursion; the shipped ladder is never deeper than two.</summary>
        public const int ChainDepth = 4;

        // ---------------------------------------------------------------------------------------------------
        // Ranking.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>The stuff's <c>SharpDamageMultiplier</c>; 1 (steel) for no stuff.</summary>
        public static float Sharpness(ThingDef? stuff) =>
            stuff == null ? 1f : stuff.GetStatValueAbstract(MaterialLadderDefOf.SharpDamageMultiplier);

        /// <summary>A building material's toughness: its <c>MaxHitPoints</c> stuff factor (RimWorld's own ranking
        /// of steel against silver against wood).</summary>
        public static float Toughness(ThingDef stuff)
        {
            if (stuff == null) throw new ArgumentNullException(nameof(stuff));
            return stuff.stuffProps?.statFactors.GetStatFactorFromList(StatDefOf.MaxHitPoints) ?? 1f;
        }

        /// <summary>A weapon with melee tools and no ranged verb.</summary>
        public static bool IsMeleeWeapon(ThingDef def) =>
            def != null && def.IsWeapon && (def.verbs == null || def.verbs.Count == 0) && def.tools != null && def.tools.Count > 0;

        /// <summary>Mean tool power per second of cooldown, times the stuff's sharpness. Zero for anything that is
        /// not a melee weapon.</summary>
        public static float WeaponScore(ThingDef def, ThingDef? stuff)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!IsMeleeWeapon(def)) return 0f;
            float sum = 0f;
            for (int i = 0; i < def.tools!.Count; i++)
            {
                Combat.Tool tool = def.tools[i];
                sum += tool.power / Math.Max(0.1f, tool.cooldownTime);
            }
            return sum / def.tools.Count * Sharpness(stuff);
        }

        public static float WeaponScore(Thing weapon) => WeaponScore(weapon.def, weapon.Stuff);

        /// <summary>Whether a citizen holding <paramref name="current"/> should trade it for
        /// <paramref name="candidate"/>: never away from a ranged weapon (a bow is a hunter's tool, not a worse
        /// knife), always from empty hands, otherwise only for a strictly better melee weapon.</summary>
        public static bool IsBetterWeapon(Thing candidate, Thing? current, Pawn? holder = null)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (current == null) return true;
            // A bow for a better bow or a gun (Crafting.Armoury); never a blade for a bow here.
            if (Armoury.IsRangedWeapon(candidate.def)) return Armoury.IsBetterRanged(candidate.def, current.def);
            if (!IsMeleeWeapon(candidate.def)) return false;
            if (!IsMeleeWeapon(current.def)) return holder != null && TradesBowForBlade(holder, candidate.Stuff);
            return WeaponScore(candidate) > WeaponScore(current) * TradeUpMargin;
        }

        /// <summary>
        /// Whether <paramref name="pawn"/>, holding a ranged weapon, would put it down for a melee weapon made of
        /// <paramref name="bladeStuff"/>. Every founder carries a bow (a hunting band: Tribesperson's
        /// weaponTags), so without this a settlement never wants a blade and the smith has nobody to arm. The
        /// bow is a hunter's tool (HuntUtility.HasHuntingWeapon), so it stays with whoever shoots at least as
        /// well as they fight — the RimWorld player's own rule for who gets the bow — and only a forged metal
        /// blade replaces it: a flint knife is a tool, not an answer to a bow. The bow left on the ground is
        /// there for the next unarmed citizen. Translation recorded in crafting.materialladder.
        /// </summary>
        public static bool TradesBowForBlade(Pawn pawn, ThingDef? bladeStuff)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            return bladeStuff?.stuffProps?.categories != null && bladeStuff.stuffProps.categories.Contains(MaterialLadderDefOf.Metallic)
                && FightsBetterThanShoots(pawn);
        }

        /// <summary>Melee skill strictly above Shooting: a tie keeps the bow, since hunting needs one.</summary>
        public static bool FightsBetterThanShoots(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            SkillRecord? melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
            SkillRecord? shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting);
            return melee != null && shooting != null && melee.Level > shooting.Level;
        }

        /// <summary>A weapon recipe this class plans: its product is a stuff-made melee weapon that takes its
        /// stuff from the ingredient.</summary>
        public static bool IsLadderWeaponRecipe(RecipeDef recipe)
        {
            if (recipe?.products == null || recipe.products.Count != 1 || !recipe.productHasIngredientStuff) return false;
            ThingDef product = recipe.products[0].thingDef;
            return product != null && product.MadeFromStuff && IsMeleeWeapon(product)
                && recipe.ingredients != null && recipe.ingredients.Count == 1;
        }

        /// <summary>A recipe whose every product is Metallic stuff: smelting and alloying.</summary>
        public static bool IsMetallurgy(RecipeDef recipe)
        {
            if (recipe?.products == null || recipe.products.Count == 0) return false;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                List<StuffCategoryDef>? categories = recipe.products[i].thingDef?.stuffProps?.categories;
                if (categories == null || !categories.Contains(MaterialLadderDefOf.Metallic)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------------------
        // What the settlement holds and can get.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Units of <paramref name="def"/> in spawned stacks on the map.</summary>
        public static int OnMap(Map.Map map, ThingDef def)
        {
            int have = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) have += stacks[i].stackCount;
            }
            return have;
        }

        /// <summary>Whether some vein yielding <paramref name="def"/> stands where a miner could reach it now.</summary>
        public static bool HasReachableVein(Map.Map map, ThingDef def)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return ReachableYields(map).Contains(def);
        }

        // One walk of the map's buildings per pass rather than one per question: a planning pass asks about
        // every stuff on every weapon recipe, recursively, and natural rock is most of a map's buildings. Keyed
        // on the map, the tick and the building count, so a vein mined out or spawned between two questions in
        // the same tick (a test, a player) is never answered from a stale walk. Holds no game state; thread-static
        // like Find's own services, since test classes run on parallel threads.
        [ThreadStatic] private static Map.Map? reachableMap;
        [ThreadStatic] private static int reachableTick;
        [ThreadStatic] private static int reachableBuildings;
        [ThreadStatic] private static HashSet<ThingDef>? reachable;

        private static HashSet<ThingDef> ReachableYields(Map.Map map)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            int tick = Find.TickManager.TicksGame;
            if (reachable != null && ReferenceEquals(reachableMap, map) && reachableTick == tick && reachableBuildings == buildings.Count) return reachable;

            reachable ??= new HashSet<ThingDef>();
            reachable.Clear();
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing rock = buildings[i];
                ThingDef? yield = rock.def.mineableThing;
                if (!rock.def.mineable || yield == null || reachable.Contains(yield) || !rock.Spawned) continue;
                if (WorkGiver_Miner.MayBeAccessible(rock.Position, map)) reachable.Add(yield);
            }
            reachableMap = map;
            reachableTick = tick;
            reachableBuildings = buildings.Count;
            return reachable;
        }

        /// <summary>Whether the settlement can come by more of <paramref name="def"/>: a reachable vein of it, or a
        /// researched metallurgy recipe making it whose fixed inputs are each on the map or can be come by in turn.</summary>
        public static bool CanGet(Map.Map map, ThingDef def, int depth = ChainDepth)
        {
            if (HasReachableVein(map, def)) return true;
            return BestRecipeFor(map, def, depth) != null;
        }

        /// <summary>
        /// The recipe the settlement makes <paramref name="metal"/> with, or null when it can make none: researched
        /// metallurgy recipes whose fixed inputs are each on the map or can be come by, the newest technique first
        /// (the highest-cost research it needs — the electric smelter's steel over the bloomery's), then the most
        /// metal out per unit in (tin bronze over arsenical while there is tin), then defName.
        /// </summary>
        public static RecipeDef? BestRecipeFor(Map.Map map, ThingDef metal, int depth = ChainDepth)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (depth <= 0) return null;
            RecipeDef? best = null;
            IReadOnlyList<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef r = all[i];
                if (!IsMetallurgy(r) || !r.AvailableNow || ProductCount(r, metal) <= 0) continue;
                if (!InputsGettable(map, r, depth - 1)) continue;
                if (best == null || CompareRecipes(r, best, metal) < 0) best = r;
            }
            return best;
        }

        private static bool InputsGettable(Map.Map map, RecipeDef recipe, int depth)
        {
            if (recipe.ingredients == null) return false;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                ThingDef? input = Guild.FixedDefOf(recipe.ingredients[i]);
                if (input == null) return false;
                if (OnMap(map, input) > 0) continue;
                if (!CanGet(map, input, depth)) return false;
            }
            return true;
        }

        /// <summary>Negative when <paramref name="a"/> is the better way to make <paramref name="metal"/>.</summary>
        private static int CompareRecipes(RecipeDef a, RecipeDef b, ThingDef metal)
        {
            int byResearch = NewestResearchCost(b).CompareTo(NewestResearchCost(a));
            if (byResearch != 0) return byResearch;
            int byYield = Yield(b, metal).CompareTo(Yield(a, metal));
            if (byYield != 0) return byYield;
            return string.CompareOrdinal(a.defName, b.defName);
        }

        private static float NewestResearchCost(RecipeDef r)
        {
            float cost = 0f;
            if (r.researchPrerequisites == null) return cost;
            for (int i = 0; i < r.researchPrerequisites.Count; i++) cost = Math.Max(cost, r.researchPrerequisites[i].baseCost);
            return cost;
        }

        private static float Yield(RecipeDef r, ThingDef metal)
        {
            float units = 0f;
            if (r.ingredients != null)
            {
                for (int i = 0; i < r.ingredients.Count; i++) units += r.ingredients[i].count;
            }
            return units <= 0f ? 0f : ProductCount(r, metal) / units;
        }

        /// <summary>How many of <paramref name="def"/> one iteration of <paramref name="recipe"/> makes.</summary>
        public static int ProductCount(RecipeDef recipe, ThingDef def)
        {
            if (recipe.products == null) return 0;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                if (ReferenceEquals(recipe.products[i].thingDef, def)) return recipe.products[i].count;
            }
            return 0;
        }

        // ---------------------------------------------------------------------------------------------------
        // Weapons.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The best stuff <paramref name="recipe"/> can be worked in now: the sharpest of its ingredient's stuffs
        /// that lies on the map in a whole product's worth or that the settlement can get (a vein, a smelting
        /// chain). Null when there is none.
        /// </summary>
        public static ThingDef? GoalStuff(Map.Map map, RecipeDef recipe)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!IsLadderWeaponRecipe(recipe)) return null;
            int per = (int)Math.Ceiling(recipe.ingredients![0].count);
            ThingDef? best = null;
            foreach (ThingDef stuff in recipe.ingredients[0].filter.AllowedThingDefs)
            {
                if (!stuff.IsStuff) continue;
                if (best != null && !IsSharperThan(stuff, best)) continue;
                if (OnMap(map, stuff) >= per || CanGet(map, stuff)) best = stuff;
            }
            return best;
        }

        private static bool IsSharperThan(ThingDef a, ThingDef b)
        {
            int bySharp = Sharpness(a).CompareTo(Sharpness(b));
            return bySharp != 0 ? bySharp > 0 : string.CompareOrdinal(a.defName, b.defName) < 0;
        }

        /// <summary>
        /// The weapon <paramref name="benchDef"/> should make: of its researched ladder weapon recipes, the one
        /// whose product in its <see cref="GoalStuff"/> scores best (defName settles a tie). Null when it can make
        /// none.
        /// </summary>
        public static WeaponPlan? BestWeaponPlan(Map.Map map, ThingDef benchDef)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (benchDef == null) throw new ArgumentNullException(nameof(benchDef));
            WeaponPlan? best = null;
            IReadOnlyList<RecipeDef> recipes = benchDef.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef r = recipes[i];
                if (!IsLadderWeaponRecipe(r) || !r.AvailableNow) continue;
                ThingDef? stuff = GoalStuff(map, r);
                if (stuff == null) continue;
                float score = WeaponScore(r.products![0].thingDef, stuff);
                if (best == null || score > best.Score
                    || (score == best.Score && string.CompareOrdinal(r.defName, best.Recipe.defName) < 0))
                {
                    best = new WeaponPlan(r, stuff, score);
                }
            }
            return best;
        }

        /// <summary>Citizens who could fight and would be better armed by <paramref name="plan"/>'s product:
        /// the unarmed, and those holding a melee weapon it outranks.</summary>
        public static int WouldTradeUp(WeaponPlan plan, IReadOnlyList<Pawn> citizens, bool unarmedOnly = false)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            int n = 0;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.equipment == null || p.WorkTagIsDisabled(WorkTags.Violent)) continue;
                Thing? held = p.equipment.Primary;
                if (held == null) n++;
                else if (unarmedOnly) continue;
                else if (IsMeleeWeapon(held.def) ? plan.Score > WeaponScore(held) * TradeUpMargin : TradesBowForBlade(p, plan.Stuff)) n++;
            }
            return n;
        }

        /// <summary>
        /// The hook <see cref="OutfittingInitiative"/> calls for a weapon recipe. False when
        /// <paramref name="recipe"/> is not one this class plans (the caller then sizes its bill as before).
        /// Otherwise <paramref name="lacking"/> is the bill's target — zero for every weapon recipe but the
        /// bench's best, so a bench holds one weapon bill — and <paramref name="stuff"/> the one stuff the bill
        /// may take: the plan's goal metal, or, while the goal is not yet on the map and somebody has nothing at
        /// all, the best stuff that is (a copper knife now beats a bronze one in a season), sized to the unarmed
        /// alone so the stop-gap is never made for someone who would not take it up.
        /// </summary>
        public static bool TryPlanWeaponBill(Map.Map map, ThingDef benchDef, RecipeDef recipe, IReadOnlyList<Pawn> citizens,
            out int lacking, out ThingDef? stuff)
        {
            lacking = 0;
            stuff = null;
            if (!IsLadderWeaponRecipe(recipe)) return false;
            WeaponPlan? plan = BestWeaponPlan(map, benchDef);
            // No stuff in sight for any blade: the cheapest one is sized the way every outfitting bill is, by
            // the unarmed, and waits for material, so the smithy still says what it is short of; the others
            // hold no bill.
            if (plan == null) return !ReferenceEquals(CheapestWeaponRecipe(benchDef), recipe);
            if (!ReferenceEquals(plan.Recipe, recipe)) return true;

            int per = plan.StuffPerProduct;
            if (OnMap(map, plan.Stuff) >= per)
            {
                lacking = WouldTradeUp(plan, citizens);
                stuff = plan.Stuff;
                return true;
            }

            ThingDef? stopGap = BestOnMap(map, recipe, per);
            int unarmed = WouldTradeUp(plan, citizens, unarmedOnly: true);
            if (stopGap != null && unarmed > 0)
            {
                lacking = unarmed;
                stuff = stopGap;
                return true;
            }
            lacking = WouldTradeUp(plan, citizens);
            stuff = plan.Stuff;
            return true;
        }

        /// <summary>The researched ladder weapon recipe at <paramref name="benchDef"/> taking the least stuff
        /// (defName settles a tie): the knife.</summary>
        public static RecipeDef? CheapestWeaponRecipe(ThingDef benchDef)
        {
            RecipeDef? cheapest = null;
            IReadOnlyList<RecipeDef> recipes = benchDef.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef r = recipes[i];
                if (!IsLadderWeaponRecipe(r) || !r.AvailableNow) continue;
                if (cheapest == null || r.ingredients![0].count < cheapest.ingredients![0].count
                    || (r.ingredients[0].count == cheapest.ingredients[0].count && string.CompareOrdinal(r.defName, cheapest.defName) < 0))
                {
                    cheapest = r;
                }
            }
            return cheapest;
        }

        private static ThingDef? BestOnMap(Map.Map map, RecipeDef recipe, int per)
        {
            ThingDef? best = null;
            foreach (ThingDef stuff in recipe.ingredients![0].filter.AllowedThingDefs)
            {
                if (!stuff.IsStuff || OnMap(map, stuff) < per) continue;
                if (best == null || IsSharperThan(stuff, best)) best = stuff;
            }
            return best;
        }

        /// <summary>Narrows <paramref name="bill"/>'s ingredient filter to <paramref name="stuff"/> alone.</summary>
        public static void RestrictBill(Bill bill, ThingDef stuff)
        {
            if (bill == null) throw new ArgumentNullException(nameof(bill));
            if (stuff == null) throw new ArgumentNullException(nameof(stuff));
            if (bill.ingredientFilter.AllowedDefCount == 1 && bill.ingredientFilter.Allows(stuff)) return;
            bill.ingredientFilter.SetDisallowAll();
            bill.ingredientFilter.SetAllow(stuff, true);
        }

        /// <summary>
        /// Units of metal the settlement wants smelted for its smithies, by metal: for each standing ladder
        /// weapon bill, its products still to make times the stuff each takes, less what of that stuff already
        /// lies on the map. The demand <see cref="SmeltingInitiative"/> works back from.
        /// </summary>
        public static Dictionary<ThingDef, int> WeaponStuffWanted(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var wanted = new Dictionary<ThingDef, int>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned || !(buildings[b] is ThingWithComps bench) || bench.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                foreach (Bill bill in comp.BillStack.Bills)
                {
                    if (!(bill is Bill_Production production) || production.suspended || !IsLadderWeaponRecipe(bill.recipe)) continue;
                    if (production.repeatMode != BillRepeatMode.TargetCount || bill.ingredientFilter.AllowedDefCount != 1) continue;
                    int toMake = production.targetCount - comp.CountProducts(production);
                    if (toMake <= 0) continue;
                    foreach (ThingDef stuff in bill.ingredientFilter.AllowedThingDefs)
                    {
                        int units = toMake * (int)Math.Ceiling(bill.recipe.ingredients![0].count);
                        wanted[stuff] = (wanted.TryGetValue(stuff, out int n) ? n : 0) + units;
                    }
                }
            }
            var keys = new List<ThingDef>(wanted.Keys);
            foreach (ThingDef stuff in keys) wanted[stuff] = Math.Max(0, wanted[stuff] - OnMap(map, stuff));
            return wanted;
        }

        /// <summary>
        /// The mined, unsmelted stuff the smithies want and no recipe makes (flint for a stone-age knife), for
        /// <see cref="MiningInitiative"/> to dig — a bill's multi-stuff ingredient names no single def for it
        /// to read, so this hands it over.
        /// </summary>
        public static Dictionary<ThingDef, int> RawWanted(Map.Map map)
        {
            var raw = new Dictionary<ThingDef, int>();
            foreach (KeyValuePair<ThingDef, int> kv in WeaponStuffWanted(map))
            {
                if (kv.Value <= 0 || !MiningInitiative.IsMinedYield(kv.Key)) continue;
                raw[kv.Key] = kv.Value;
            }
            return raw;
        }

        // ---------------------------------------------------------------------------------------------------
        // Building stuff.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The metal <paramref name="def"/> (a wall or a door) should be built of on <paramref name="map"/>, or
        /// null to leave the caller's choice alone. The shared hook "best wall stuff for this era": the toughest
        /// Metallic stuff the def can be made of that the settlement holds a whole one's worth of to spare — after
        /// what its smithies want (<see cref="WeaponStuffWanted"/> is a deficit, so any of that metal on the map
        /// is spoken for while it is positive) and what other blueprints and frames already wait on. A wall takes
        /// only bulk metal (<c>stuffProps.commonality</c> 1 or more, RimWorld's own reading of a bulk material:
        /// iron and steel, never copper or bronze, which went into blades and gates, not ramparts). Stone is not
        /// offered: <see cref="StoneWallMaterials"/> owns stone walls and their research gate.
        /// </summary>
        public static ThingDef? BestBuildingMetal(Map.Map map, ThingDef def)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (def == null || !def.MadeFromStuff) return null;
            bool isWall = StoneWallMaterials.IsWall(def);
            Dictionary<ThingDef, int> smithyWants = WeaponStuffWanted(map);
            ThingDef? best = null;
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef stuff = all[i];
                if (stuff.stuffProps?.categories == null || !stuff.stuffProps.categories.Contains(MaterialLadderDefOf.Metallic)) continue;
                if (!GenStuff.CanMake(stuff, def)) continue;
                if (isWall && stuff.stuffProps.commonality < 1f) continue;
                if (smithyWants.TryGetValue(stuff, out int stillShort) && stillShort > 0) continue;
                if (FeedsStandingSmelting(map, stuff)) continue;
                if (Spare(map, stuff) < def.CostListCountFor(stuff, stuff)) continue;
                if (best == null || Toughness(stuff) > Toughness(best)
                    || (Toughness(stuff) == Toughness(best) && string.CompareOrdinal(stuff.defName, best.defName) < 0))
                {
                    best = stuff;
                }
            }
            return best;
        }

        /// <summary>Whether a metallurgy bill on the map still working takes <paramref name="stuff"/> in: iron
        /// waiting to become steel is not spare (measured: seed 12345 walled with iron while its cementation bill
        /// stood short of it).</summary>
        private static bool FeedsStandingSmelting(Map.Map map, ThingDef stuff)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (buildings[b] is not ThingWithComps bench || bench.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                IReadOnlyList<Bill> bills = comp.BillStack.Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    if (bills[i] is not Bill_Production bill || !IsMetallurgy(bill.recipe) || !bill.ShouldDoNow()) continue;
                    foreach (IngredientCount ingredient in bill.recipe.ingredients!)
                    {
                        if (ReferenceEquals(Guild.FixedDefOf(ingredient), stuff)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>On the map, less what blueprints and frames already placed still wait for.</summary>
        internal static int Spare(Map.Map map, ThingDef stuff)
        {
            int have = OnMap(map, stuff);
            if (have <= 0) return 0;
            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp) have -= bp.ThingCountNeeded(stuff);
            }
            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame frame) have -= frame.MaterialStillNeeded(stuff);
            }
            return have;
        }

        /// <summary>
        /// Re-plans not-yet-started wall and door blueprints in <see cref="BestBuildingMetal"/>, when the
        /// settlement has a metal to spare and the blueprint asked for wood. A blueprint holds no material
        /// (delivery makes it a frame first), so changing its stuff costs nothing; a blueprint already in a metal,
        /// or in stone, is left alone. Returns how many changed. This is how the shared hook reaches the walls and
        /// doors the construction initiative and the dwellings plan, without editing either.
        /// </summary>
        public static int UpgradeBlueprints(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int changed = 0;
            var blueprints = new List<Thing>(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (!(blueprints[i] is Blueprint bp) || !bp.Spawned) continue;
                ThingDef entity = bp.EntityToBuild;
                if (!ReferenceEquals(entity, ConstructionThingDefOf.Wall) && !ReferenceEquals(entity, MaterialLadderDefOf.Door)) continue;
                ThingDef? current = bp.stuffToUse ?? GenStuff.DefaultStuffFor(entity);
                if (current?.stuffProps?.categories == null || !current.stuffProps.categories.Contains(MaterialLadderDefOf.Woody)) continue;
                ThingDef? metal = BestBuildingMetal(map, entity);
                if (metal == null) continue;
                bp.stuffToUse = metal;
                changed++;
            }
            return changed;
        }
    }
}
