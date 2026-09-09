using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Butchers a dead animal into meat and leather (RimWorld: <c>RimWorld.Recipe_ButcherCorpse</c>, applied
    /// straight to the dead pawn rather than to a separate Corpse thing — no Corpse type exists in this port
    /// yet, and a dead <see cref="Pawn"/> already stays spawned on its map exactly as one would). Yield scales
    /// with <see cref="Pawn.BodySize"/>, not a fixed <see cref="RecipeDef.products"/> list — see
    /// <see cref="HusbandryTuning"/> for the (unsourced) per-body-size constants. <b>Deviation:</b> no job
    /// driver walks a butcher to the corpse yet; <see cref="ButcherUtility.TryButcher"/> is the direct,
    /// no-job-driver call site this recipe is invoked through, the same shape <c>SurgeryUtility.PerformNextSurgery</c>
    /// already established for a recipe applied to a pawn.
    /// </summary>
    public class Recipe_ButcherAnimal : RecipeWorker
    {
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawn.Dead) throw new InvalidOperationException("Recipe_ButcherAnimal applied to a living pawn " + pawn.Label + ".");

            Map.Map? map = pawn.Map;
            IntVec3 pos = pawn.Position;
            RaceProperties race = pawn.RaceProps;
            float bodySize = pawn.BodySize;

            int meatCount = (int)Math.Round(bodySize * HusbandryTuning.MeatPerBodySize, MidpointRounding.AwayFromZero);
            int leatherCount = race.leatherDef != null
                ? (int)Math.Round(bodySize * HusbandryTuning.LeatherPerBodySize, MidpointRounding.AwayFromZero)
                : 0;
            ThingDef? meatDef = race.meatDef ?? ThingDefOf.Meat_Generic;

            // Butchering removes the carcass outright (RimWorld: the Corpse is destroyed once fully
            // butchered); this port has no partial-butcher-over-multiple-bills step, so it always goes in
            // one call.
            pawn.Destroy(DestroyMode.Vanish);

            if (map != null)
            {
                if (meatCount > 0 && meatDef != null)
                {
                    Thing meat = ThingMaker.MakeThing(meatDef);
                    meat.stackCount = meatCount;
                    GenSpawn.Spawn(meat, pos, map);
                }
                if (leatherCount > 0 && race.leatherDef != null)
                {
                    Thing leather = ThingMaker.MakeThing(race.leatherDef);
                    leather.stackCount = leatherCount;
                    GenSpawn.Spawn(leather, pos, map);
                }
            }

            if (recipe.workSkill != null && billDoer != null)
            {
                // Same lump-sum shape GenRecipe.MakeRecipeProducts uses for an ordinary recipe with no job
                // driver to tick work ticks against — see that method's own comment.
                billDoer.skills?.Learn(recipe.workSkill, recipe.WorkAmountTotal() * recipe.workSkillLearnFactor * 0.1f);
            }
        }
    }
}
