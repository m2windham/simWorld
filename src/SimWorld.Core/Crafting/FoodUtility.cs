using System;
using SimWorld.Pawns;

namespace SimWorld.Crafting
{
    /// <summary>Eating and food-poisoning helpers (RimWorld: <c>RimWorld.FoodUtility</c>, trimmed to what this port needs).</summary>
    public static class FoodUtility
    {
        /// <summary>
        /// Chance a cooked meal comes out poisoned, by the cook's skill (RimWorld's FoodPoisonChance stat).
        /// <b>Deviation:</b> RimWorld also factors in the kitchen room's cleanliness (dirt/filth raise this
        /// further); that half lands once Building/Room exist. This curve is the skill-only baseline —
        /// points taken from RimWorld's own tuning: 5% at skill 0, 2% at 5, 0.5% at 10, 0.1% at 20.
        /// </summary>
        public static readonly SimpleCurve FoodPoisonChanceFromCookCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.05f),
            new CurvePoint(5, 0.02f),
            new CurvePoint(10, 0.005f),
            new CurvePoint(20, 0.001f),
        });

        public static float FoodPoisonChanceFromCook(int cookSkillLevel) => FoodPoisonChanceFromCookCurve.Evaluate(cookSkillLevel);

        /// <summary>
        /// How many units of <paramref name="foodDef"/> <paramref name="pawn"/> will take in one sitting
        /// (RimWorld: <c>FoodUtility.WillIngestStackCountOf</c> — <c>min(maxNumToIngestAtOnce,
        /// ceil(nutritionWanted / nutritionPerUnit))</c>, ported as-is). Never less than one, so a pawn that
        /// only wants a crumb still finishes the unit it picked up, exactly as RimWorld's own does.
        ///
        /// <para/><b>The defect this closes, and why it is a starvation bug rather than a nicety.</b>
        /// <see cref="AI.JobDriver_Ingest"/> ate exactly one unit per job. One unit of every raw foodstuff
        /// this port ships is 0.05 nutrition, and a human stomach is 1.0 — so a citizen standing on a pile of
        /// food recovered 0.05 per reserve-walk-chew cycle while <see cref="Needs.Need_Food"/> took 1.6 a day
        /// off them. Even a settlement with a full larder starved: the food was there, the mouths were there,
        /// and the bite was two per cent of a meal. Meals (0.9) hid it completely, which is why every unit
        /// test of the eating path passed.
        /// </summary>
        public static int WillIngestStackCountOf(Pawn pawn, Defs.ThingDef foodDef)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (foodDef == null) throw new ArgumentNullException(nameof(foodDef));

            IngestibleProperties? props = foodDef.ingestible;
            float perUnit = props?.nutrition ?? 0f;
            if (props == null || perUnit <= 0f) return 1;

            float wanted = pawn.needs?.food?.NutritionWanted ?? 0f;
            int needed = (int)Math.Ceiling(wanted / perUnit);
            int capped = Math.Min(props.maxNumToIngestAtOnce, needed);
            return capped < 1 ? 1 : capped;
        }

        public static float NutritionForItem(ItemStack food)
        {
            if (food == null) throw new ArgumentNullException(nameof(food));
            return (food.Def.ingestible?.nutrition ?? 0f) * food.Count;
        }

        /// <summary>
        /// Feeds <paramref name="food"/> to <paramref name="pawn"/>: raises the food need by its nutrition
        /// (clamped to the need's max) and, if the stack is <see cref="ItemStack.Poisoned"/>, adds
        /// <see cref="CraftingHediffDefOf.FoodPoisoning"/>. Returns the count eaten.
        /// </summary>
        public static int Eat(Pawn pawn, ItemStack food)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (food == null) throw new ArgumentNullException(nameof(food));

            pawn.needs.food?.Eat(NutritionForItem(food));
            if (food.Poisoned)
            {
                pawn.health.AddHediff(CraftingHediffDefOf.FoodPoisoning);
            }
            return food.Count;
        }
    }
}
