using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>Stats the armoury reads by name. Its own <c>[DefOf]</c> class, per CLAUDE.md.</summary>
    [DefOf]
    public static class ArmouryDefOf
    {
        public static StatDef ArmorRating_Sharp = null!;
        public static StatDef ArmorRating_Blunt = null!;
        public static StatDef ArmorRating_Heat = null!;
    }

    /// <summary>
    /// The settlement's armoury: which bow, crossbow or gun and which armour its people should be making and
    /// wearing now (era-arc finding 5, "defence scales with neither"; system crafting.billcreators).
    ///
    /// <para/><b>The RimWorld idea.</b> Research unlocks better weapons and armour at a bench; the player adds the
    /// bills and hands the new gear out, and colonists put on armour by <c>JobGiver_OptimizeApparel</c>'s score.
    /// There is no player here, so this class is that choice, the ranged and armour half of what
    /// <see cref="MaterialLadder"/> already does for blades.
    ///
    /// <para/><b>Shaped by the arc.</b> Each era's research puts a better bow, crossbow or gun and a better coat of
    /// armour within reach (<c>ThingDefs_Weapons/Weapons_*.xml</c>, <c>ThingDefs_Apparel/Apparel_Armour.xml</c>).
    /// A bench makes the best of them whose materials the settlement holds or can get, for the citizens it would
    /// improve — counted in people, as every outfitting bill is — and a citizen trades up to it. Nothing here names
    /// an era: a settlement that researched and smelted carries the arms of its age, one that did not carries what
    /// it started with.
    ///
    /// <para/><b>Ranged weapons are ranked</b> by damage per second at short-to-medium range, armour penetration
    /// counted as extra damage: projectile damage x burst x (1 + penetration) x the mean of short and medium
    /// accuracy, over warmup plus cooldown plus the burst's gaps. SimWorld's own reading; RimWorld's info card
    /// shows no ranged DPS, and its players read the same numbers this way.
    ///
    /// <para/><b>Armour</b> is apparel whose own sharp armour reaches <see cref="ArmourSharpFloor"/> (a coat of
    /// mail, not a shirt). It is scored the way <see cref="JobGiver_OptimizeApparel"/> scores what it puts on, so
    /// the bench makes what the citizen would actually wear. Insulation is not weighed here or there: the cold is
    /// the exposure lane's.
    ///
    /// <para/>Stateless; the bills it sizes are saved by their bench.
    /// </summary>
    public static class Armoury
    {
        /// <summary>Sharp armour from which a piece of apparel counts as armour. Not sourced: above every
        /// garment (a parka's 0.08), below the lightest armour (a hide coat).</summary>
        public const float ArmourSharpFloor = 0.1f;

        // ---------------------------------------------------------------------------------------------------
        // Ranged weapons.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>A weapon whose first verb launches a projectile.</summary>
        public static bool IsRangedWeapon(ThingDef def) =>
            def != null && def.IsWeapon && def.verbs != null && def.verbs.Count > 0
            && !def.verbs[0].IsMeleeAttack && def.verbs[0].defaultProjectile?.projectile != null;

        /// <summary>Damage per second at short-to-medium range (see the class remarks). Zero for anything that
        /// is not a ranged weapon.</summary>
        public static float RangedScore(ThingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!IsRangedWeapon(def)) return 0f;
            VerbProperties verb = def.verbs![0];
            ProjectileProperties shot = verb.defaultProjectile!.projectile!;
            int burst = Math.Max(1, verb.burstShotCount);
            float cycle = verb.warmupTime + verb.defaultCooldownTime + (burst - 1) * verb.ticksBetweenBurstShots / 60f;
            float accuracy = (verb.accuracyShort + verb.accuracyMedium) / 2f;
            return shot.damageAmountBase * burst * (1f + shot.armorPenetrationBase) * accuracy / Math.Max(0.1f, cycle);
        }

        /// <summary>Whether a citizen holding <paramref name="current"/> should take up <paramref name="candidate"/>,
        /// both ranged: only for a strictly better one.</summary>
        public static bool IsBetterRanged(ThingDef candidate, ThingDef current) =>
            IsRangedWeapon(candidate) && IsRangedWeapon(current)
            && RangedScore(candidate) > RangedScore(current) * MaterialLadder.TradeUpMargin;

        /// <summary>A recipe making exactly one ranged weapon.</summary>
        public static bool IsRangedRecipe(RecipeDef recipe) =>
            recipe?.products != null && recipe.products.Count == 1 && recipe.products[0].thingDef != null
            && IsRangedWeapon(recipe.products[0].thingDef);

        /// <summary>Whether every ingredient of <paramref name="recipe"/> has some allowed def lying on the map, held
        /// in the settlement's ledger (which <c>Economy.SettlementBillStock</c> issues to a standing bill's bench),
        /// or within the settlement's reach (<see cref="MaterialLadder.CanGet"/>: a vein, a smelting chain).</summary>
        public static bool IngredientsInReach(Map.Map map, RecipeDef recipe)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (recipe.ingredients == null) return true;
            SimWorld.World.Settlement? settlement = HuntingInitiative.SettlementFor(map);
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                bool any = false;
                foreach (ThingDef def in recipe.ingredients[i].filter.AllowedThingDefs)
                {
                    if (MaterialLadder.OnMap(map, def) > 0 || (settlement?.StoreCountOf(def) ?? 0) > 0
                        || MaterialLadder.CanGet(map, def)) { any = true; break; }
                }
                if (!any) return false;
            }
            return true;
        }

        /// <summary>Whether one product's worth of every ingredient of <paramref name="recipe"/> lies on the map or in
        /// the settlement's ledger now: the bill could start today, not once a furnace has worked.</summary>
        public static bool IngredientsOnHand(Map.Map map, RecipeDef recipe)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (recipe.ingredients == null) return true;
            SimWorld.World.Settlement? settlement = HuntingInitiative.SettlementFor(map);
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                int per = (int)Math.Ceiling(recipe.ingredients[i].count);
                bool any = false;
                foreach (ThingDef def in recipe.ingredients[i].filter.AllowedThingDefs)
                {
                    if (MaterialLadder.OnMap(map, def) + (settlement?.StoreCountOf(def) ?? 0) >= per) { any = true; break; }
                }
                if (!any) return false;
            }
            return true;
        }

        /// <summary>The ranged weapon <paramref name="benchDef"/> should make: of its researched ranged recipes whose
        /// ingredients are in reach, the best product (defName settles a tie). Null when there is none.</summary>
        public static RecipeDef? BestRangedRecipe(Map.Map map, ThingDef benchDef) => BestRangedRecipe(map, benchDef, onHandOnly: false);

        /// <summary><see cref="BestRangedRecipe(Map.Map, ThingDef)"/>, counting with <paramref name="onHandOnly"/>
        /// only recipes whose ingredients are on hand (<see cref="IngredientsOnHand"/>).</summary>
        public static RecipeDef? BestRangedRecipe(Map.Map map, ThingDef benchDef, bool onHandOnly)
        {
            if (benchDef == null) throw new ArgumentNullException(nameof(benchDef));
            RecipeDef? best = null;
            float bestScore = 0f;
            IReadOnlyList<RecipeDef> recipes = benchDef.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef r = recipes[i];
                if (!IsRangedRecipe(r) || !r.AvailableNow || !IngredientsInReach(map, r)) continue;
                if (onHandOnly && !IngredientsOnHand(map, r)) continue;
                float score = RangedScore(r.products![0].thingDef);
                if (best == null || score > bestScore || (score == bestScore && string.CompareOrdinal(r.defName, best.defName) < 0))
                {
                    best = r;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>Citizens who could fight and would take up <paramref name="weapon"/>: those holding a worse
        /// ranged weapon, and the empty-handed who shoot at least as well as they fight (the others are the blade
        /// ladder's).</summary>
        public static int WouldTakeUpRanged(ThingDef weapon, IReadOnlyList<Pawn> citizens)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            int n = 0;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.equipment == null || p.WorkTagIsDisabled(WorkTags.Violent)) continue;
                Thing? held = p.equipment.Primary;
                if (held == null ? !MaterialLadder.FightsBetterThanShoots(p) : IsBetterRanged(weapon, held.def)) n++;
            }
            return n;
        }

        /// <summary>
        /// The hook <see cref="OutfittingInitiative"/> calls for a ranged weapon recipe. False when
        /// <paramref name="recipe"/> makes no ranged weapon. Otherwise <paramref name="lacking"/> is the bill's
        /// target: the citizens who would take up the bench's best weapon in reach, and, while that one's materials
        /// are still being smelted, the best one whose materials are on hand (<see cref="IngredientsOnHand"/>);
        /// zero for every other ranged recipe. The stop-gap is the material ladder's own (a copper knife now beats
        /// a bronze one in a season): a town whose rifle waits on a bloomery still trades its short bows for great
        /// bows meanwhile, where before the rifle bill zeroed every lesser one and nothing was made for years.
        /// </summary>
        public static bool TryPlanRangedBill(Map.Map map, ThingDef benchDef, RecipeDef recipe, IReadOnlyList<Pawn> citizens, out int lacking)
        {
            lacking = 0;
            if (!IsRangedRecipe(recipe)) return false;
            RecipeDef? best = BestRangedRecipe(map, benchDef);
            if (best == null) return true;
            if (ReferenceEquals(best, recipe))
            {
                lacking = WouldTakeUpRanged(recipe.products![0].thingDef, citizens);
                return true;
            }
            if (IngredientsOnHand(map, best)) return true;
            if (ReferenceEquals(BestRangedRecipe(map, benchDef, onHandOnly: true), recipe)) lacking = WouldTakeUpRanged(recipe.products![0].thingDef, citizens);
            return true;
        }

        /// <summary>
        /// Units of metal the armoury's standing bills want, by metal and before what lies on the map: for each
        /// bow, gun or armour bill still short, the products to make times each metal ingredient, less what of the
        /// ingredient's other metals is held, charged to the metal the settlement should smelt for it
        /// (<see cref="GoalMetal"/>). <see cref="MaterialLadder.MetalWanted"/> adds the blades' and takes off the
        /// map's stock; <see cref="SmeltingInitiative"/> works back from the total.
        /// </summary>
        public static Dictionary<ThingDef, int> MetalWanted(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var wanted = new Dictionary<ThingDef, int>();
            SimWorld.World.Settlement? settlement = HuntingInitiative.SettlementFor(map);
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            var metals = new List<ThingDef>();
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned || !(buildings[b] is ThingWithComps bench) || bench.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                foreach (Bill bill in comp.BillStack.Bills)
                {
                    if (!(bill is Bill_Production production) || production.suspended || production.repeatMode != BillRepeatMode.TargetCount) continue;
                    if (!IsRangedRecipe(bill.recipe) && !IsArmourRecipe(bill.recipe)) continue;
                    int toMake = production.targetCount - comp.CountProducts(production);
                    if (toMake <= 0 || bill.recipe.ingredients == null) continue;
                    foreach (IngredientCount ingredient in bill.recipe.ingredients)
                    {
                        metals.Clear();
                        foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                        {
                            if (IsMetal(def) && bill.ingredientFilter.Allows(def)) metals.Add(def);
                        }
                        ThingDef? goal = GoalMetal(map, bill.recipe, metals);
                        if (goal == null) continue;
                        int need = toMake * (int)Math.Ceiling(ingredient.count) - (settlement?.StoreCountOf(goal) ?? 0);
                        foreach (ThingDef other in metals)
                        {
                            if (!ReferenceEquals(other, goal)) need -= MaterialLadder.OnMap(map, other) + (settlement?.StoreCountOf(other) ?? 0);
                        }
                        if (need > 0) wanted[goal] = (wanted.TryGetValue(goal, out int n) ? n : 0) + need;
                    }
                }
            }
            return wanted;
        }

        private static bool IsMetal(ThingDef def) =>
            def.stuffProps?.categories != null && def.stuffProps.categories.Contains(MaterialLadderDefOf.Metallic);

        /// <summary>Whether any ingredient of <paramref name="recipe"/> may be a metal.</summary>
        public static bool TakesMetal(RecipeDef recipe)
        {
            if (recipe?.ingredients == null) return false;
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                {
                    if (IsMetal(def)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Narrows <paramref name="bill"/>'s metal ingredients to the one the settlement smelts for it
        /// (<see cref="GoalMetal"/>), leaving its other ingredients alone; with no metal in reach, it is left as it
        /// is. The armoury's half of <see cref="MaterialLadder.RestrictBill"/>: without it a helmet bill that may
        /// take any metal ate the iron the bloomery was cementing into plate, and on a generated medieval map the
        /// town's only iron vein went into helmets and mail and no plate was ever made.
        /// </summary>
        public static void RestrictToGoalMetal(Map.Map map, Bill bill)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (bill?.recipe?.ingredients == null) return;
            var metals = new List<ThingDef>();
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                metals.Clear();
                foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                {
                    if (IsMetal(def)) metals.Add(def);
                }
                ThingDef? goal = metals.Count > 1 ? GoalMetal(map, bill.recipe, metals) : null;
                if (goal == null) continue;
                foreach (ThingDef metal in metals) bill.ingredientFilter.SetAllow(metal, ReferenceEquals(metal, goal));
            }
        }

        /// <summary>
        /// Which of <paramref name="metals"/> (one ingredient's) the settlement should smelt for
        /// <paramref name="recipe"/>, of those it can get: for armour that takes its stuff from the metal, the best
        /// (steel mail over iron, as the ladder picks a blade's metal); for a weapon whose metal is only a part,
        /// the cheapest, since the product is the same whatever it is made of. Null when none can be got.
        ///
        /// <para/>The best gives way to a bill that can take nothing else (<see cref="HeldMetals"/>): while plate,
        /// which only steel makes, is still short, a helmet or a mail shirt is made of the next best metal. A
        /// helmet that takes fifteen steel is always ready before a plate coat that takes fifty, so with both
        /// asking for steel the helmets were made first, every time: on a generated medieval map twenty steel
        /// helmets used up the town's only iron vein and no plate was ever made.
        /// </summary>
        public static ThingDef? GoalMetal(Map.Map map, RecipeDef recipe, IReadOnlyList<ThingDef> metals)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            ThingDef? goal = null;
            ThingDef? spare = null;
            HashSet<ThingDef>? held = recipe.productHasIngredientStuff && metals.Count > 1 ? HeldMetals(map) : null;
            for (int i = 0; i < metals.Count; i++)
            {
                ThingDef metal = metals[i];
                if (!MaterialLadder.CanGet(map, metal)) continue;
                if (goal == null || MaterialLadder.IsSharperThan(metal, goal) == recipe.productHasIngredientStuff) goal = metal;
                if (held != null && !held.Contains(metal) && (spare == null || MaterialLadder.IsSharperThan(metal, spare))) spare = metal;
            }
            return spare ?? goal;
        }

        /// <summary>
        /// The metals a standing armoury bill on <paramref name="map"/> can be made of and nothing else: some
        /// ingredient of a short, unsuspended target bill whose recipe allows that one metal only (plate's steel, a
        /// rifle's).
        /// </summary>
        public static HashSet<ThingDef> HeldMetals(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var held = new HashSet<ThingDef>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned || !(buildings[b] is ThingWithComps bench) || bench.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                foreach (Bill bill in comp.BillStack.Bills)
                {
                    if (!(bill is Bill_Production production) || production.suspended || production.repeatMode != BillRepeatMode.TargetCount) continue;
                    if (bill.recipe.ingredients == null || (!IsRangedRecipe(bill.recipe) && !IsArmourRecipe(bill.recipe))) continue;
                    if (production.targetCount - comp.CountProducts(production) <= 0) continue;
                    foreach (IngredientCount ingredient in bill.recipe.ingredients)
                    {
                        ThingDef? only = null;
                        int count = 0;
                        foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                        {
                            if (!IsMetal(def)) continue;
                            only = def;
                            count++;
                        }
                        if (count == 1) held.Add(only!);
                    }
                }
            }
            return held;
        }

        // Best ranged weapon lying on a map, cached per map and tick: every armed citizen's think asks it, and
        // the answer only changes when an item does. Thread-static like MaterialLadder's own cache.
        [ThreadStatic] private static Map.Map? lyingMap;
        [ThreadStatic] private static int lyingTick;
        [ThreadStatic] private static int lyingCount;
        [ThreadStatic] private static float lyingBest;

        /// <summary>The best <see cref="RangedScore"/> of any spawned ranged weapon lying on <paramref name="map"/>;
        /// 0 when there is none. Lets a citizen with a bow skip the item scan when nothing better lies about.</summary>
        public static float BestLyingRangedScore(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            int tick = Find.TickManager.TicksGame;
            if (ReferenceEquals(lyingMap, map) && lyingTick == tick && lyingCount == items.Count) return lyingBest;
            float best = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].Spawned || !IsRangedWeapon(items[i].def)) continue;
                best = Math.Max(best, RangedScore(items[i].def));
            }
            lyingMap = map;
            lyingTick = tick;
            lyingCount = items.Count;
            lyingBest = best;
            return best;
        }

        // ---------------------------------------------------------------------------------------------------
        // Armour.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Apparel whose own sharp armour reaches <see cref="ArmourSharpFloor"/>.</summary>
        public static bool IsArmour(ThingDef def) =>
            def != null && def.IsApparel && def.GetStatValueAbstract(ArmouryDefOf.ArmorRating_Sharp) >= ArmourSharpFloor;

        /// <summary>A new piece's worth as <see cref="JobGiver_OptimizeApparel.ApparelScoreRaw"/> reads a worn one:
        /// 0.1 plus its sharp, blunt and heat armour, at full hit points.</summary>
        public static float ArmourScore(ThingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return 0.1f + def.GetStatValueAbstract(ArmouryDefOf.ArmorRating_Sharp)
                + def.GetStatValueAbstract(ArmouryDefOf.ArmorRating_Blunt)
                + def.GetStatValueAbstract(ArmouryDefOf.ArmorRating_Heat);
        }

        /// <summary>A recipe making exactly one piece of armour.</summary>
        public static bool IsArmourRecipe(RecipeDef recipe) =>
            recipe?.products != null && recipe.products.Count == 1 && recipe.products[0].thingDef != null
            && IsArmour(recipe.products[0].thingDef);

        /// <summary>
        /// The armour recipes the settlement should work now: every researched armour recipe at a bench standing on
        /// <paramref name="map"/> whose ingredients are in reach, less any outscored by another of them worn in the
        /// same place (plate over mail over hide). Each list entry is the best of its slot.
        /// </summary>
        public static List<RecipeDef> BestArmourRecipes(Map.Map map)
        {
            List<RecipeDef> available = AvailableArmourRecipes(map);
            var best = new List<RecipeDef>();
            for (int i = 0; i < available.Count; i++)
            {
                if (!IsOutscored(available[i], available)) best.Add(available[i]);
            }
            return best;
        }

        /// <summary>Every researched armour recipe at a bench standing on <paramref name="map"/> whose ingredients
        /// are in reach.</summary>
        private static List<RecipeDef> AvailableArmourRecipes(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var available = new List<RecipeDef>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned || (buildings[b] as ThingWithComps)?.GetComp<CompBillGiver>() == null) continue;
                IReadOnlyList<RecipeDef> recipes = buildings[b].def.AllRecipes;
                for (int r = 0; r < recipes.Count; r++)
                {
                    RecipeDef recipe = recipes[r];
                    if (available.Contains(recipe) || !IsArmourRecipe(recipe) || !recipe.AvailableNow) continue;
                    if (IngredientsInReach(map, recipe)) available.Add(recipe);
                }
            }
            return available;
        }

        private static bool IsOutscored(RecipeDef recipe, List<RecipeDef> others)
        {
            ThingDef product = recipe.products![0].thingDef;
            float score = ArmourScore(product);
            for (int i = 0; i < others.Count; i++)
            {
                if (ReferenceEquals(others[i], recipe)) continue;
                ThingDef other = others[i].products![0].thingDef;
                if (!other.apparel!.ConflictsWith(product.apparel!)) continue;
                float otherScore = ArmourScore(other);
                if (otherScore > score || (otherScore == score && string.CompareOrdinal(others[i].defName, recipe.defName) < 0)) return true;
            }
            return false;
        }

        /// <summary>Citizens who would put <paramref name="armour"/> on: it is worth more to them than what it would
        /// replace, by <see cref="JobGiver_OptimizeApparel"/>'s own reckoning and threshold.</summary>
        public static int WouldWear(ThingDef armour, IReadOnlyList<Pawn> citizens)
        {
            if (armour?.apparel == null) throw new ArgumentException("not apparel", nameof(armour));
            float score = ArmourScore(armour);
            int n = 0;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.apparel == null) continue;
                float gain = score;
                bool replaces = false;
                IReadOnlyList<ThingWithComps> worn = p.apparel.WornApparel;
                for (int w = 0; w < worn.Count; w++)
                {
                    if (worn[w].def.apparel == null || !worn[w].def.apparel!.ConflictsWith(armour.apparel)) continue;
                    gain -= JobGiver_OptimizeApparel.ApparelScoreRaw(worn[w]);
                    replaces = true;
                }
                if (!replaces) gain *= 10f;
                if (gain >= JobGiver_OptimizeApparel.MinScoreGainToCare) n++;
            }
            return n;
        }

        /// <summary>
        /// The armour recipes to work while a slot's best waits on its metal: for each recipe in
        /// <see cref="BestArmourRecipes"/> whose ingredients are not on hand (<see cref="IngredientsOnHand"/>), the
        /// best in-reach recipe of the same place whose ingredients are. Hide coats go on being made while the
        /// bloomery smelts the plate's steel; measured before this, seed 20/101 stood at 16 of 28 in armour for
        /// four years behind a plate bill nothing could fill.
        /// </summary>
        public static List<RecipeDef> StopGapArmourRecipes(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            List<RecipeDef> best = BestArmourRecipes(map);
            var onHand = new List<RecipeDef>();
            foreach (RecipeDef recipe in AvailableArmourRecipes(map))
            {
                if (!best.Contains(recipe) && IngredientsOnHand(map, recipe)) onHand.Add(recipe);
            }
            var stopGaps = new List<RecipeDef>();
            foreach (RecipeDef recipe in onHand)
            {
                if (IsOutscored(recipe, onHand)) continue;
                ApparelProperties worn = recipe.products![0].thingDef.apparel!;
                bool waiting = false;
                foreach (RecipeDef b in best)
                {
                    if (!b.products![0].thingDef.apparel!.ConflictsWith(worn)) continue;
                    if (IngredientsOnHand(map, b)) { waiting = false; break; }
                    // Mail made while plate waits on steel eats the iron the steel is made from.
                    if (TakesMetal(recipe) && TakesMetal(b)) { waiting = false; break; }
                    waiting = true;
                }
                if (waiting) stopGaps.Add(recipe);
            }
            return stopGaps;
        }

        /// <summary>
        /// The hook <see cref="OutfittingInitiative"/> calls for an armour recipe. False when
        /// <paramref name="recipe"/> makes no armour. Otherwise <paramref name="lacking"/> is the bill's target:
        /// the citizens who would wear it while it is the best of its slot (<see cref="BestArmourRecipes"/>) or the
        /// stop-gap for a best still waiting on metal (<see cref="StopGapArmourRecipes"/>), and zero otherwise.
        /// </summary>
        public static bool TryPlanArmourBill(Map.Map map, RecipeDef recipe, IReadOnlyList<Pawn> citizens, out int lacking)
        {
            lacking = 0;
            if (!IsArmourRecipe(recipe)) return false;
            List<RecipeDef> best = BestArmourRecipes(map);
            if (best.Contains(recipe) || StopGapArmourRecipes(map).Contains(recipe))
            {
                lacking = WouldWear(recipe.products![0].thingDef, citizens);
                RecipeDef? first = KitFirst(recipe, best, citizens);
                if (first != null) lacking = WouldWear(recipe.products![0].thingDef, Wearing(first.products![0].thingDef, citizens));
            }
            return true;
        }

        /// <summary>
        /// The armour <paramref name="recipe"/> waits behind: another of the best, for another slot, made of one
        /// metal only (<see cref="HeldMetals"/>) and still wanted by someone, while <paramref name="recipe"/> takes
        /// metal too. Null when it waits on none. While plate is short, a metal helmet goes to a citizen already in
        /// plate, so a soldier is kitted out whole before the next is started: a fifteen-metal helmet is always
        /// ready before a fifty-steel coat, and helmets for the whole town used up a generated medieval map's only
        /// iron vein with no plate made (measured: nineteen helmets and four mail shirts, no plate, in fifteen days).
        /// </summary>
        private static RecipeDef? KitFirst(RecipeDef recipe, List<RecipeDef> best, IReadOnlyList<Pawn> citizens)
        {
            if (!TakesMetal(recipe) || OneMetalOnly(recipe)) return null;
            ApparelProperties worn = recipe.products![0].thingDef.apparel!;
            foreach (RecipeDef other in best)
            {
                if (ReferenceEquals(other, recipe) || !OneMetalOnly(other)) continue;
                ThingDef product = other.products![0].thingDef;
                if (product.apparel!.ConflictsWith(worn)) continue;
                if (WouldWear(product, citizens) > 0) return other;
            }
            return null;
        }

        private static bool OneMetalOnly(RecipeDef recipe)
        {
            if (recipe.ingredients == null) return false;
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                int count = 0;
                foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                {
                    if (IsMetal(def)) count++;
                }
                if (count == 1) return true;
            }
            return false;
        }

        private static List<Pawn> Wearing(ThingDef apparel, IReadOnlyList<Pawn> citizens)
        {
            var wearing = new List<Pawn>();
            foreach (Pawn p in citizens)
            {
                if (p.apparel == null) continue;
                foreach (ThingWithComps w in p.apparel.WornApparel)
                {
                    if (ReferenceEquals(w.def, apparel)) { wearing.Add(p); break; }
                }
            }
            return wearing;
        }
    }
}
