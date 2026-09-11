using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>
    /// The patient, as the owner of their own surgery bill stack (<see cref="BillStack"/> needs an
    /// <see cref="IBillGiver"/>; RimWorld's <c>Pawn</c> implements that interface itself). Kept as a small
    /// adapter rather than putting the interface on <c>Pawn</c>, because a pawn is not a workbench: it counts
    /// no products and holds no production queue, and the two things a bill giver is asked for are exactly
    /// those.
    /// </summary>
    public sealed class SurgeryBillGiver : IBillGiver
    {
        private readonly Pawn pawn;

        public SurgeryBillGiver(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        /// <summary>Nothing counts a patient's "products": a surgery is done once and removed, never repeated
        /// to a target count, which is why <see cref="Bill_Medical.ShouldDoNow"/> ignores counts entirely.</summary>
        public IProductCounter? ProductCounter => null;

        public string LabelCap => pawn.Label;
    }

    /// <summary>
    /// A surgery queued on a pawn (RimWorld: <c>RimWorld.Bill_Medical</c>). Unlike a production bill, which
    /// lives on a workbench and repeats, a medical bill lives on the patient, names the body part to operate
    /// on, and is done exactly once.
    /// </summary>
    public class Bill_Medical : Bill
    {
        /// <summary>The part the surgeon operates on; null for a recipe that does not target one.</summary>
        public BodyPartRecord? part;

        public Bill_Medical()
        {
        }

        public Bill_Medical(RecipeDef recipe, BodyPartRecord? part = null) : base(recipe)
        {
            this.part = part;
        }

        /// <summary>A medical bill is done once, so it is available until something removes it.</summary>
        public override bool ShouldDoNow() => !suspended;

        public override void ExposeData()
        {
            base.ExposeData();

            // A BodyPartRecord is a node in a body tree built at load, not a saved object: save the address
            // (which body def, which part index) and resolve it back, the same way RimWorld does.
            string? partLabel = part?.def.defName;
            int partIndex = part?.Index ?? -1;
            Scribe_Values.Look(ref partLabel, "partDef", null);
            Scribe_Values.Look(ref partIndex, "partIndex", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                pendingPartDef = partLabel;
                pendingPartIndex = partIndex;
            }
        }

        private string? pendingPartDef;
        private int pendingPartIndex = -1;

        /// <summary>
        /// Re-attaches <see cref="part"/> to the loaded pawn's own body tree. Called by
        /// <see cref="Pawn_HealthTracker"/> once the pawn exists, because a part record is only meaningful
        /// against a body, and a bill has no way to reach its patient on its own.
        /// </summary>
        public void ResolvePartAfterLoad(Pawn pawn)
        {
            if (pendingPartDef == null || pendingPartIndex < 0) return;
            foreach (BodyPartRecord candidate in pawn.RaceProps.body!.AllParts)
            {
                if (candidate.Index == pendingPartIndex && candidate.def.defName == pendingPartDef)
                {
                    part = candidate;
                    break;
                }
            }
            pendingPartDef = null;
            pendingPartIndex = -1;
        }
    }

    /// <summary>
    /// Tuning for surgery outcomes. SimWorld's own numbers: RimWorld resolves a surgeon's competence through
    /// its <c>MedicalSurgerySuccessChance</c> stat, whose value comes from <c>SkillNeed</c> curves this port
    /// does not have yet (the open <c>work.stats</c> item). Until it does, competence is read straight off the
    /// Medicine skill here, and the tests pin the trend — a better surgeon fails less often — rather than any
    /// of these literals.
    /// </summary>
    public static class SurgeryTuning
    {
        /// <summary>Success chance for a surgeon with no Medicine skill at all.</summary>
        public const float SuccessChanceAtSkillZero = 0.35f;

        /// <summary>Success chance at the maximum skill level, before the recipe's own factor.</summary>
        public const float SuccessChanceAtMaxSkill = 0.98f;

        /// <summary>The skill level <see cref="SuccessChanceAtMaxSkill"/> is reached at (RimWorld's own skill ceiling).</summary>
        public const int MaxSkillLevel = 20;

        /// <summary>Damage a failed operation does to the part being operated on.</summary>
        public const float FailureDamage = 12f;
    }

    /// <summary>
    /// A recipe performed on a pawn (RimWorld: <c>RimWorld.Recipe_Surgery</c>). Rolls the operation against
    /// the surgeon's competence and this recipe's own difficulty, then either does the thing or hurts the
    /// patient. Subclasses say what "the thing" is.
    /// </summary>
    public abstract class Recipe_Surgery : RecipeWorker
    {
        /// <summary>
        /// Chance <paramref name="surgeon"/> gets this operation right: their Medicine skill lerped across
        /// <see cref="SurgeryTuning"/>'s band, times the recipe's own
        /// <see cref="RecipeDef.surgerySuccessChanceFactor"/>. A null surgeon (a scenario or a debug call
        /// applying a recipe with nobody performing it) never fails — there is no one to be bad at it.
        /// </summary>
        public float SuccessChance(Pawn? surgeon)
        {
            if (surgeon == null) return 1f;
            int level = surgeon.skills?.GetSkill(Work.SkillDefOf.Medicine)?.Level ?? 0;
            float t = GenMath.Clamp01(level / (float)SurgeryTuning.MaxSkillLevel);
            float baseChance = GenMath.Lerp(SurgeryTuning.SuccessChanceAtSkillZero, SurgeryTuning.SuccessChanceAtMaxSkill, t);
            return GenMath.Clamp01(baseChance * recipe.surgerySuccessChanceFactor);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            // system: filth — operating in a filthy room goes wrong more often (RimWorld:
            // Recipe_Surgery.CheckSurgeryFail multiplies the surgeon's chance by the room's
            // RoomStatDefOf.SurgerySuccessChanceFactor, which is derived from its cleanliness). The whole
            // curve and the room walk live in the filth module so this stays one factor on one line; a
            // patient in a clean room, or on no map at all, gets exactly 1 and nothing changes.
            if (billDoer != null
                && !Rand.Chance(SuccessChance(billDoer) * SimWorld.Filth.RoomCleanlinessUtility.SurgerySuccessFactorFor(pawn)))
            {
                OnSurgeryFailed(pawn, part);
                return;
            }
            OnSurgerySuccess(pawn, part, billDoer, ingredients);
        }

        protected abstract void OnSurgerySuccess(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients);

        /// <summary>
        /// A botched operation hurts the patient where the surgeon was working, and may kill them outright when
        /// the recipe is dangerous enough to say so. It never silently does nothing: a failure the player cannot
        /// see is indistinguishable from a bug.
        /// </summary>
        protected virtual void OnSurgeryFailed(Pawn pawn, BodyPartRecord? part)
        {
            BodyPartRecord? target = part ?? pawn.RaceProps.body?.corePart;

            // SurgicalCut, not Cut: the same wound, classified as what it is. A death here must not read as
            // a killing — see Damages_Surgery.xml and DamageDef.externalViolence's readers.
            var dinfo = new DamageInfo(SurgeryDamageDefOf.SurgicalCut, SurgeryTuning.FailureDamage, hitPart: target);
            SurgeryDamageDefOf.SurgicalCut.Worker.Apply(dinfo, pawn);

            if (!pawn.Dead && recipe.deathOnFailedSurgeryChance > 0f && Rand.Chance(recipe.deathOnFailedSurgeryChance))
            {
                pawn.health.Kill(dinfo, null);
            }
        }

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!recipe.targetsBodyPart) yield break;

            foreach (BodyPartRecord part in pawn.RaceProps.body!.AllParts)
            {
                if (recipe.appliedOnFixedBodyParts != null && !recipe.appliedOnFixedBodyParts.Contains(part.def)) continue;
                if (!IsValidTarget(pawn, part)) continue;
                yield return part;
            }
        }

        protected virtual bool IsValidTarget(Pawn pawn, BodyPartRecord part) => true;
    }

    /// <summary>Amputation (RimWorld: <c>RimWorld.Recipe_RemoveBodyPart</c>): the part comes off and stays off.</summary>
    public class Recipe_RemoveBodyPart : Recipe_Surgery
    {
        protected override void OnSurgerySuccess(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
            if (part == null) return;

            // HediffSet destroys a part only as a consequence of damage (CheckPartDestroyed, private by
            // design). An amputation is the same end state reached deliberately: clear the part and everything
            // below it, then put the missing-part hediff on it — which is what that private path does too.
            pawn.health.RestorePart(part);
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, part);
        }

        /// <summary>A part already gone cannot be removed again, and neither can the one the pawn dies without.</summary>
        protected override bool IsValidTarget(Pawn pawn, BodyPartRecord part) =>
            !part.IsCorePart && !pawn.health.hediffSet.PartIsMissing(part);
    }

    /// <summary>
    /// Fitting a prosthetic or implant (RimWorld: <c>RimWorld.Recipe_InstallBodyPart</c>): whatever was there
    /// is replaced, and <see cref="RecipeDef.addsHediff"/> takes its place on the part.
    /// </summary>
    public class Recipe_InstallBodyPart : Recipe_Surgery
    {
        protected override void OnSurgerySuccess(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
            if (part == null || recipe.addsHediff == null) return;

            // Whatever the part carried — an old prosthetic, an injury, the part itself — is gone once the new
            // one is in. RimWorld restores the part first for exactly this reason.
            pawn.health.RestorePart(part);
            pawn.health.AddHediff(recipe.addsHediff, part);
        }

        protected override bool IsValidTarget(Pawn pawn, BodyPartRecord part) => !part.IsCorePart;
    }

    /// <summary>
    /// Cutting something out (RimWorld: <c>RimWorld.Recipe_RemoveHediff</c>): the named hediff comes off the
    /// operated part. What the surgeon is for — an infection that immunity is losing to can be excised.
    /// </summary>
    public class Recipe_RemoveHediff : Recipe_Surgery
    {
        protected override void OnSurgerySuccess(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
            if (recipe.removesHediff == null) return;

            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].def != recipe.removesHediff) continue;
                if (recipe.targetsBodyPart && part != null && !ReferenceEquals(all[i].Part, part)) continue;
                pawn.health.RemoveHediff(all[i]);
            }
        }

        protected override bool IsValidTarget(Pawn pawn, BodyPartRecord part)
        {
            if (recipe.removesHediff == null) return false;
            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].def == recipe.removesHediff && (!recipe.targetsBodyPart || ReferenceEquals(all[i].Part, part))) return true;
            }
            return false;
        }
    }

    /// <summary>Queueing and performing the surgeries on one pawn's own bill stack.</summary>
    public static class SurgeryUtility
    {
        /// <summary>
        /// Performs the first medical bill on <paramref name="patient"/>, as <paramref name="surgeon"/>, and
        /// removes it. Returns false when there was nothing queued, or when the bill's recipe names
        /// <see cref="RecipeDef.ingredients"/> (health.prosthetic-items: installing a prosthetic consumes the
        /// item) and none of <paramref name="ingredientsOnHand"/> satisfy them — the bill stays queued rather
        /// than firing for free. The job driver that will eventually walk a surgeon to a patient, haul the
        /// prosthetic there and spend work doing this is not built; this is the effect that driver will call,
        /// kept separate from it exactly as <see cref="GenRecipe"/> is for production — <paramref name="ingredientsOnHand"/>
        /// stands in for whatever it brings, the same way its <c>worker</c>/<c>ingredients</c> stand in here.
        /// </summary>
        public static bool PerformNextSurgery(Pawn patient, Pawn? surgeon, IReadOnlyList<Things.ThingWithComps>? ingredientsOnHand = null)
        {
            if (patient == null) throw new ArgumentNullException(nameof(patient));

            BillStack stack = patient.health.surgeryBills;
            for (int i = 0; i < stack.Count; i++)
            {
                if (!(stack[i] is Bill_Medical bill) || !bill.ShouldDoNow()) continue;
                if (!TryConsumeIngredients(bill.recipe, ingredientsOnHand)) continue;
                bill.recipe.Worker.ApplyOnPawn(patient, bill.part, surgeon, null);
                stack.Delete(bill);
                return true;
            }
            return false;
        }

        /// <summary>
        /// A plain non-surgery recipe's ingredients are matched and reserved by <c>BillIngredientsFinder</c>
        /// against <c>ItemStack</c>s on a stockpile; a medical bill has no stockpile, only whatever the surgeon
        /// carried in, so this does the same per-slot match directly against live Things and decrements/destroys
        /// whichever one satisfies each slot. A recipe with no <see cref="RecipeDef.ingredients"/> (every
        /// surgery this port shipped before health.prosthetic-items) always succeeds, unchanged.
        /// </summary>
        private static bool TryConsumeIngredients(RecipeDef recipe, IReadOnlyList<Things.ThingWithComps>? onHand)
        {
            if (recipe.ingredients == null || recipe.ingredients.Count == 0) return true;
            if (onHand == null) return false;

            foreach (IngredientCount slot in recipe.ingredients)
            {
                Things.ThingWithComps? match = null;
                for (int i = 0; i < onHand.Count; i++)
                {
                    if (slot.filter.Allows(onHand[i].def)) { match = onHand[i]; break; }
                }
                if (match == null) return false;

                int needed = (int)slot.GetBaseCount();
                match.stackCount -= needed;
                if (match.stackCount <= 0) match.Destroy();
            }
            return true;
        }
    }
}
