using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Translation: <b>a settlement tailors and smiths for the citizens who lack apparel or a weapon</b>,
    /// because there is no player to add the bill (crafting.billcreators). The sibling of
    /// <see cref="CookingInitiative"/>, which spelled out the rule this class waited on: a bill whose target
    /// is zero must not be queued, because nothing consumes the product. Apparel and weapons now have their
    /// consumers — <see cref="JobGiver_OptimizeApparel"/> puts lying apparel on, and
    /// <see cref="JobGiver_PickUpOpportunisticWeapon"/> arms an unarmed citizen — so the demand is real and is
    /// counted in people, not guessed.
    ///
    /// <para/><b>The demand figure.</b> For an apparel recipe: citizens on the map wearing nothing that would
    /// conflict with the product (nothing on that layer over those body parts), so it is a gap, not an
    /// upgrade. For a weapon recipe: citizens on the map holding no primary and able to do violence. The bill's
    /// <see cref="Bill_Production.targetCount"/> is that head count, re-derived every pass; it counts
    /// <i>unclaimed</i> products lying on the map (<see cref="CompBillGiver.CountProducts"/>), so a shirt made
    /// and not yet put on is counted against the next citizen, and the moment it is worn both the stock and
    /// the need drop by one together.
    ///
    /// <para/><b>Never a zero target.</b> No bill is queued while nobody lacks the product, and a standing one
    /// is deleted when the last gap closes, so a bench never holds a bill nothing will consume.
    ///
    /// <para/><b>No bench is placed here.</b> Raising a smithy or a tailoring bench is the dwellings lane's
    /// business (compound work sites); this uses whichever bench already stands. Recipes are found by product
    /// (<see cref="ThingDef.IsApparel"/> / <see cref="ThingDef.IsWeapon"/>), benches by their own recipe list,
    /// so new content needs no code. Stateless: everything is re-derived from the map, nothing to Scribe
    /// beyond the bills themselves, which the bench already saves.
    /// </summary>
    public static class OutfittingInitiative
    {
        /// <summary>Self-gate cadence: the rare bucket every initiative here uses.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>One map, self-gating on <see cref="IntervalTicks"/>. Called once per map per tick from
        /// <see cref="Map.Map.MapTick"/>.</summary>
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

            List<Pawn> citizens = CitizensOn(map);
            // Benches only: every CompBillGiver building, in Building's order, without the rock (perf, tick-cost lane).
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned) continue;
                CompBillGiver? comp = (buildings[b] as ThingWithComps)?.GetComp<CompBillGiver>();
                if (comp == null) continue;

                IReadOnlyList<RecipeDef> recipes = buildings[b].def.AllRecipes;
                for (int r = 0; r < recipes.Count; r++)
                {
                    RecipeDef recipe = recipes[r];
                    if (!IsOutfitting(recipe) || !recipe.AvailableNow) continue;
                    // A stuff-made melee weapon is the material ladder's to size: one weapon bill per bench, for
                    // the best weapon in the best stuff the era and stock allow, counting the citizens who would
                    // trade up to it as well as the unarmed (crafting.materialladder).
                    if (MaterialLadder.TryPlanWeaponBill(map, buildings[b].def, recipe, citizens, out int lacking, out ThingDef? stuff))
                    {
                        UpdateBill(comp, recipe, lacking);
                        Bill_Production? weaponBill = BillFor(comp, recipe);
                        if (weaponBill != null && stuff != null) MaterialLadder.RestrictBill(weaponBill, stuff);
                        continue;
                    }
                    // Bows, crossbows and guns, and armour: the best within reach, for whoever it would improve
                    // (Crafting.Armoury, era-arc finding 5).
                    if (Armoury.TryPlanRangedBill(map, buildings[b].def, recipe, citizens, out int rangedLacking))
                    {
                        UpdateBill(comp, recipe, rangedLacking);
                        continue;
                    }
                    if (Armoury.TryPlanArmourBill(map, recipe, citizens, out int armourLacking))
                    {
                        UpdateBill(comp, recipe, armourLacking);
                        continue;
                    }
                    UpdateBill(comp, recipe, Lacking(recipe, citizens));
                }
            }

            // Once every bill stands: which metal a bill is held to depends on the others (Armoury.HeldMetals).
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned) continue;
                CompBillGiver? comp = (buildings[b] as ThingWithComps)?.GetComp<CompBillGiver>();
                if (comp == null) continue;
                IReadOnlyList<Bill> bills = comp.BillStack.Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    RecipeDef recipe = bills[i].recipe;
                    if ((Armoury.IsRangedRecipe(recipe) || Armoury.IsArmourRecipe(recipe)) && Armoury.TakesMetal(recipe))
                    {
                        Armoury.RestrictToGoalMetal(map, bills[i]);
                    }
                }
            }
        }

        /// <summary>True when <paramref name="recipe"/> makes apparel that is not armour: what everybody wears.</summary>
        private static bool IsClothing(RecipeDef recipe) =>
            IsOutfitting(recipe) && recipe.products!.TrueForAll(p => p.thingDef.IsApparel) && !Armoury.IsArmourRecipe(recipe);

        /// <summary>True when every product <paramref name="recipe"/> makes is apparel or a weapon.</summary>
        public static bool IsOutfitting(RecipeDef recipe)
        {
            if (recipe?.products == null || recipe.products.Count == 0) return false;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDef? product = recipe.products[i].thingDef;
                if (product == null || !(product.IsApparel || product.IsWeapon)) return false;
            }
            return true;
        }

        /// <summary>How many of <paramref name="citizens"/> lack what <paramref name="recipe"/> makes: the
        /// target its bill is set to.</summary>
        public static int Lacking(RecipeDef recipe, IReadOnlyList<Pawn> citizens)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            int lacking = 0;
            for (int c = 0; c < citizens.Count; c++)
            {
                if (LacksAny(recipe, citizens[c])) lacking++;
            }
            return lacking;
        }

        private static bool LacksAny(RecipeDef recipe, Pawn pawn)
        {
            if (recipe.products == null) return false;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDef? product = recipe.products[i].thingDef;
                if (product == null) continue;
                if (product.IsApparel)
                {
                    if (pawn.apparel != null && !pawn.apparel.WouldConflictWithWorn(product)) return true;
                }
                else if (pawn.equipment != null && pawn.equipment.Primary == null
                    && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    return true;
                }
            }
            return false;
        }

        private static void UpdateBill(CompBillGiver comp, RecipeDef recipe, int lacking)
        {
            Bill_Production? bill = BillFor(comp, recipe);
            if (lacking <= 0)
            {
                if (bill != null) comp.BillStack.Delete(bill);
                return;
            }
            if (bill == null)
            {
                bill = new Bill_Production(recipe) { repeatMode = BillRepeatMode.TargetCount };
                comp.BillStack.AddBill(bill);
                if (IsClothing(recipe))
                {
                    // Clothes before arms (exposure4 lane). A bench works its bills top down (RimWorld's
                    // WorkGiver_DoBill), and the hide coat, a dozen deep for the band's fighters, came first and took
                    // every hide, so the children's parkas waited on the armoury all winter. A RimWorld player drags the
                    // winter clothes to the top; the settlement does the same.
                    for (int at = comp.BillStack.Count - 1; at > 0; at--)
                    {
                        RecipeDef above = comp.BillStack[at - 1].recipe;
                        if (IsClothing(above) || !IsOutfitting(above)) break;
                        comp.BillStack.Reorder(bill, -1);
                    }
                }
            }
            bill.targetCount = lacking;

            // One below the target: as soon as a citizen takes one up, the bench makes the next. A batch band
            // (CookingInitiative's half) would leave people bare for want of a single shirt.
            bill.unpauseWhenYouHave = lacking - 1;
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

        /// <summary>The living citizens standing on <paramref name="map"/>: its settlement's, or with no
        /// settlement behind the map, every humanlike spawned on it (the fallback
        /// <see cref="HuntingInitiative"/> uses). Only people on the map can walk to what the bench makes.</summary>
        public static List<Pawn> CitizensOn(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var found = new List<Pawn>();
            Settlement? settlement = HuntingInitiative.SettlementFor(map);
            if (settlement != null)
            {
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int i = 0; i < citizens.Count; i++)
                {
                    Pawn p = citizens[i];
                    if (!p.Dead && p.Spawned && ReferenceEquals(p.Map, map)) found.Add(p);
                }
                return found;
            }

            IReadOnlyList<Pawn> onMap = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < onMap.Count; i++)
            {
                if (onMap[i].RaceProps.Humanlike && !onMap[i].Dead) found.Add(onMap[i]);
            }
            return found;
        }
    }
}
