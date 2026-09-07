using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Crafting
{
    /// <summary>How a production bill decides it still wants doing (RimWorld: <c>RimWorld.BillRepeatModeDef</c>, folded into a plain enum here).</summary>
    public enum BillRepeatMode
    {
        RepeatCount,
        TargetCount,
        Forever,
    }

    /// <summary>Counts how many of a bill's product the owner already has on hand, for TargetCount bills.</summary>
    public interface IProductCounter
    {
        int CountProducts(Bill_Production bill);
    }

    /// <summary>Whatever hosts a <see cref="BillStack"/> — a workbench, once Buildings model one (RimWorld: <c>RimWorld.IBillGiver</c>).</summary>
    public interface IBillGiver
    {
        IProductCounter? ProductCounter { get; }
        string LabelCap { get; }
    }

    /// <summary>
    /// One queued unit of work against a <see cref="RecipeDef"/> (RimWorld: <c>RimWorld.Bill</c>). Abstract
    /// because RimWorld also has non-production bills (medical operations) this port does not model yet;
    /// <see cref="Bill_Production"/> is the only concrete kind so far.
    /// </summary>
    public abstract class Bill : IExposable
    {
        public RecipeDef recipe = null!;
        public bool suspended;
        public ThingFilter ingredientFilter = null!;
        public float ingredientSearchRadius = 999f;
        public IntRange allowedSkillRange = new IntRange(0, 20);

        /// <summary>Set by <see cref="BillStack.AddBill"/> and relinked after a Scribe load; never itself Scribed.</summary>
        public BillStack? billStack;

        protected Bill()
        {
        }

        protected Bill(RecipeDef recipe)
        {
            this.recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            ingredientFilter = new ThingFilter();
            ingredientFilter.CopyAllowancesFrom(recipe.EffectiveDefaultIngredientFilter);
        }

        public string LabelCap => recipe.LabelCap;

        /// <summary>Suspended, an out-of-range work skill, or an unmet <see cref="SkillRequirement"/> all block a pawn from starting this bill anew.</summary>
        public bool PawnAllowedToStartAnew(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (suspended) return false;
            if (recipe.workSkill != null)
            {
                int level = pawn.skills?.GetSkill(recipe.workSkill)?.Level ?? 0;
                if (!allowedSkillRange.Includes(level)) return false;
            }
            return recipe.PawnSatisfiesSkillRequirements(pawn);
        }

        public abstract bool ShouldDoNow();

        /// <summary>Called once a full work cycle finishes producing <paramref name="ingredients"/>' worth of product; RepeatCount bills count down here.</summary>
        public virtual void Notify_IterationCompleted(Pawn worker, List<ItemStack> ingredients)
        {
        }

        public virtual void ExposeData()
        {
            RecipeDef? r = recipe;
            Scribe_Defs.Look(ref r, "recipe");
            recipe = r!;
            Scribe_Values.Look(ref suspended, "suspended");
            ThingFilter? f = ingredientFilter;
            Scribe_Deep.Look(ref f, "ingredientFilter");
            ingredientFilter = f ?? new ThingFilter();
            Scribe_Values.Look(ref ingredientSearchRadius, "ingredientSearchRadius", 999f);
            Scribe_Values.Look(ref allowedSkillRange, "allowedSkillRange", new IntRange(0, 20));
        }

        public override string ToString() => recipe?.defName ?? GetType().Name;
    }

    /// <summary>The common case: repeat a recipe until some stopping condition (RimWorld: <c>RimWorld.Bill_Production</c>).</summary>
    public class Bill_Production : Bill
    {
        public BillRepeatMode repeatMode = BillRepeatMode.RepeatCount;
        public int repeatCount = 1;
        public int targetCount = 10;

        /// <summary>Once paused at <see cref="targetCount"/>, a TargetCount bill resumes only once owned stock falls to this level or below.</summary>
        public int unpauseWhenYouHave;

        public bool paused;

        public Bill_Production()
        {
        }

        public Bill_Production(RecipeDef recipe) : base(recipe)
        {
        }

        public override bool ShouldDoNow()
        {
            if (suspended) return false;
            switch (repeatMode)
            {
                case BillRepeatMode.RepeatCount:
                    return repeatCount > 0;
                case BillRepeatMode.TargetCount:
                    return ShouldDoNowTargetCount();
                case BillRepeatMode.Forever:
                default:
                    return true;
            }
        }

        private bool ShouldDoNowTargetCount()
        {
            int owned = billStack?.Owner?.ProductCounter?.CountProducts(this) ?? 0;
            if (owned >= targetCount)
            {
                paused = true;
                return false;
            }
            if (paused && owned > unpauseWhenYouHave)
            {
                return false;
            }
            paused = false;
            return true;
        }

        public override void Notify_IterationCompleted(Pawn worker, List<ItemStack> ingredients)
        {
            base.Notify_IterationCompleted(worker, ingredients);
            if (repeatMode == BillRepeatMode.RepeatCount && repeatCount > 0) repeatCount--;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref repeatMode, "repeatMode", BillRepeatMode.RepeatCount);
            Scribe_Values.Look(ref repeatCount, "repeatCount", 1);
            Scribe_Values.Look(ref targetCount, "targetCount", 10);
            Scribe_Values.Look(ref unpauseWhenYouHave, "unpauseWhenYouHave");
            Scribe_Values.Look(ref paused, "paused");
        }
    }

    /// <summary>The ordered queue of bills on one workbench (RimWorld: <c>RimWorld.BillStack</c>).</summary>
    public class BillStack : IExposable
    {
        private List<Bill> bills = new List<Bill>();

        public IBillGiver Owner { get; private set; } = null!;

        public BillStack(IBillGiver owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public int Count => bills.Count;

        public Bill this[int index] => bills[index];

        public IReadOnlyList<Bill> Bills => bills;

        public void AddBill(Bill bill)
        {
            if (bill == null) throw new ArgumentNullException(nameof(bill));
            bill.billStack = this;
            bills.Add(bill);
        }

        public void Delete(Bill bill) => bills.Remove(bill);

        /// <summary>Moves <paramref name="bill"/> by <paramref name="direction"/> slots (-1 up, +1 down); a no-op past either end or for an unlisted bill.</summary>
        public void Reorder(Bill bill, int direction)
        {
            int index = bills.IndexOf(bill);
            if (index < 0) return;
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= bills.Count) return;
            bills.RemoveAt(index);
            bills.Insert(newIndex, bill);
        }

        public Bill? FirstShouldDoNow => bills.FirstOrDefault(b => b.ShouldDoNow());

        public bool AnyShouldDoNow => FirstShouldDoNow != null;

        public void ExposeData()
        {
            List<Bill>? list = bills;
            Scribe_Collections.Look(ref list, "bills", LookMode.Deep);
            bills = list ?? new List<Bill>();
            foreach (Bill bill in bills)
            {
                bill.billStack = this;
            }
        }
    }
}
