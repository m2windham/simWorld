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
    /// Butchers a dead animal into meat and leather (RimWorld: <c>RimWorld.Recipe_ButcherCorpse</c>), applied
    /// to the dead <see cref="Pawn"/> rather than to the <see cref="Corpse"/> holding it. Yield scales with
    /// <see cref="Pawn.BodySize"/>, not a fixed <see cref="RecipeDef.products"/> list — see
    /// <see cref="HusbandryTuning"/> for the (unsourced) per-body-size constants.
    /// <para/>
    /// <b>Why the pawn and not the corpse is the argument.</b> Corpses now exist
    /// (<see cref="Things.CorpseMaker"/>), and a butchered body is always inside one — but every number this
    /// recipe reads (body size, race, meat and leather defs) lives on the pawn, and the two existing callers
    /// already hold one. So the pawn stays the argument and this method looks *out* to
    /// <see cref="Pawn.corpse"/> for the two things a despawned pawn no longer has of its own: which map it
    /// is on, and which cell. That keeps one yield formula for both butchery routes — the hunter's
    /// field-dressing at the kill site (<c>AI.JobDriver_Hunt</c>) and a butcher's work at a bench
    /// (<c>AI.JobDriver_ButcherCorpse</c>) — instead of a second one on the corpse side.
    /// </summary>
    public class Recipe_ButcherAnimal : RecipeWorker
    {
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawn.Dead) throw new InvalidOperationException("Recipe_ButcherAnimal applied to a living pawn " + pawn.Label + ".");

            // A dead pawn is normally not on the map itself: it is held by a Corpse standing on the cell it
            // died on. Either is accepted, so a caller that somehow has a still-spawned dead pawn (a test
            // that killed one before corpses existed, a pawn killed with no tick manager running) behaves
            // exactly as it did before.
            Corpse? corpse = pawn.corpse;
            Map.Map? map = pawn.Map ?? (corpse != null && corpse.Spawned ? corpse.Map : null);
            IntVec3 pos = pawn.Spawned ? pawn.Position : (corpse != null ? corpse.Position : IntVec3.Invalid);
            RaceProperties race = pawn.RaceProps;
            float bodySize = pawn.BodySize;

            int meatCount = (int)Math.Round(bodySize * HusbandryTuning.MeatPerBodySize, MidpointRounding.AwayFromZero);
            int leatherCount = race.leatherDef != null
                ? (int)Math.Round(bodySize * HusbandryTuning.LeatherPerBodySize, MidpointRounding.AwayFromZero)
                : 0;
            ThingDef? meatDef = race.meatDef ?? ThingDefOf.Meat_Generic;

            // Butchering removes the carcass outright (RimWorld: the Corpse is destroyed once fully
            // butchered); this port has no partial-butcher-over-multiple-bills step, so it always goes in
            // one call. Destroying the corpse destroys the body with it (Corpse.Destroy), so the pawn ends
            // up Destroyed either way round and no half-consumed body is left behind.
            if (corpse != null) corpse.Destroy(DestroyMode.Vanish);
            else pawn.Destroy(DestroyMode.Vanish);

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

                // system: filth — butchering makes a mess (RimWorld: Corpse.ButcherProducts spawns
                // RaceProps.BloodDef filth at the butcher's feet). A flesh carcass only; a mechanoid has no
                // blood to spill, which is the same test RimWorld's BloodDef being null does for it.
                if (race.IsFlesh)
                {
                    SimWorld.Filth.FilthMaker.TryMakeFilth(
                        pos, map, SimWorld.Filth.FilthDefOf.Filth_Blood, pawn.Label, count: 2);
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
