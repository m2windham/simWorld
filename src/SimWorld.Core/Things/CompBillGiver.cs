using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Things
{
    /// <summary>
    /// Data half of <see cref="CompBillGiver"/>: which <see cref="WorkTypeDef"/> a bench belongs to (RimWorld:
    /// <c>ThingDef.recipes</c> plus <c>WorkGiverDef.fixedBillGiverDefs</c> together decide this; see
    /// <see cref="CompBillGiver"/>'s own remarks for why this port inverts that onto the building's own def
    /// instead).
    /// </summary>
    public class CompProperties_BillGiver : CompProperties
    {
        public WorkTypeDef workType = null!;

        public CompProperties_BillGiver()
        {
            compClass = typeof(CompBillGiver);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (workType == null) yield return "CompProperties_BillGiver on " + parentDef.defName + " has no workType.";
        }
    }

    /// <summary>
    /// Makes a map building a <see cref="IBillGiver"/> (RimWorld: <c>RimWorld.Building_WorkTable</c>, one of
    /// only two concrete <c>Building</c> subclasses whose entire job is holding a <see cref="BillStack"/>).
    /// <b>Deviation, deliberate:</b> this port's Building module composes every map building from comps
    /// rather than subclassing <c>Building_*</c> per mechanic — <c>CompPower</c>/<c>CompTurretGun</c>/
    /// <c>CompTrap</c> already made that call for power, turrets and traps (see their own remarks), and a
    /// workbench is the same shape of problem: "this Building also holds a bill queue" composes onto the one
    /// plain <see cref="Building"/> thingClass exactly as cleanly as "this Building also shoots" does, without
    /// adding a fourth concrete Building subclass content has to remember to pick.
    /// <para/>
    /// <b>Which bench a <see cref="Crafting.WorkGiver_DoBill"/> may use:</b> RimWorld threads this through
    /// <c>WorkGiverDef.fixedBillGiverDefs</c> (a list of ThingDefs on the *giver's* own def). That field would
    /// have to live on <see cref="Work.WorkGiverDef"/> — a file this pass does not own and, concurrently with
    /// this one, the hauling/research/doctoring lanes are also touching for their own giverClass wiring (see
    /// <c>docs/WORK-REGISTER.md</c>). So this port inverts the relationship: the *bench* names the
    /// <see cref="WorkTypeDef"/> it serves (<see cref="CompProperties_BillGiver.workType"/>), and
    /// <see cref="Crafting.WorkGiver_DoBill"/> — one class shared by <c>DoBillsSmith</c>, <c>DoBillsTailor</c>,
    /// <c>DoBillsArt</c>, <c>DoBillsCraft</c> and <c>CookMeals</c> — matches a bench against its own
    /// <c>WorkGiverDef.workType</c>, which every <see cref="WorkGiverDef"/> already carries. Content-facing
    /// result is identical (a bench only offers work to the pawns of the right trade); this port just puts
    /// the fact on the thing being visited instead of the thing doing the visiting.
    /// </summary>
    public sealed class CompBillGiver : ThingComp, IBillGiver, IProductCounter
    {
        private BillStack? billStack;

        public CompProperties_BillGiver Props => (CompProperties_BillGiver)props;

        public BillStack BillStack => billStack ??= new BillStack(this);

        public IProductCounter? ProductCounter => this;

        public string LabelCap => parent.Label;

        /// <summary>
        /// How many of a <see cref="Bill_Production"/>'s product this bench's own map already holds — the
        /// map-scale reading of "how many do we have" a <see cref="Bill.TargetCount"/> bill asks
        /// (RimWorld: <c>Map.resourceCounter</c>, which counts every matching Thing on the map regardless of
        /// where it is stored; this port has no <c>ResourceCounter</c> of its own yet, so this sums
        /// <see cref="Map.ListerThings.ThingsOfDef"/> directly — same answer, computed on demand instead of
        /// cached).
        /// </summary>
        public int CountProducts(Bill_Production bill)
        {
            if (bill?.recipe?.products == null) return 0;
            Map.Map? map = parent.Map;
            if (map == null) return 0;

            int total = 0;
            for (int i = 0; i < bill.recipe.products.Count; i++)
            {
                IReadOnlyList<Thing> things = map.listerThings.ThingsOfDef(bill.recipe.products[i].thingDef);
                for (int t = 0; t < things.Count; t++) total += things[t].stackCount;
            }
            return total;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            BillStack? stack = billStack;
            Scribe_Deep.Look(ref stack, "billStack", this);
            billStack = stack ?? new BillStack(this);
        }
    }
}
