using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Works the first runnable bill on a bench-shaped <see cref="Things.CompBillGiver"/> (RimWorld:
    /// <c>RimWorld.WorkGiver_DoBill</c>). One class for every work type a bench can belong to —
    /// <c>DoBillsSmith</c>, <c>DoBillsTailor</c>, <c>DoBillsArt</c>, <c>DoBillsCraft</c> and <c>CookMeals</c>
    /// all point <c>giverClass</c> at this same type in content (<c>WorkGiverDefs/WorkGivers.xml</c>).
    /// RimWorld itself ships several near-duplicate <c>WorkGiver_DoBill</c>/<c>WorkGiver_DoBill_Cook</c>
    /// subclasses for this; porting each 1:1 would only be this class copied five times with a different
    /// scan filter, so this pass collapses them into the one the actual content difference calls for —
    /// which bench a WorkGiverDef may use.
    /// <para/>
    /// <b>Which bench belongs to which WorkGiverDef:</b> RimWorld threads this through
    /// <c>WorkGiverDef.fixedBillGiverDefs</c>, a list living on the *giver's* own def. That field would have
    /// to be added to <see cref="Work.WorkGiverDef"/> — a file this lane does not own, and which the
    /// hauling/research/doctoring lanes are concurrently editing for their own <c>giverClass</c> wiring in the
    /// same pass (<c>docs/WORK-REGISTER.md</c>). This port inverts the relationship instead: the *bench*
    /// names the <see cref="Work.WorkTypeDef"/> it serves (<see cref="CompProperties_BillGiver.workType"/>),
    /// and this giver matches a candidate bench against its own <see cref="Work.WorkGiverDef.workType"/>,
    /// which every WorkGiverDef already carries. The content-facing result is identical — a bench only offers
    /// work to pawns of the matching trade — this class just reads the fact off the building being visited
    /// instead of off the giver doing the visiting. See <see cref="CompBillGiver"/>'s own remarks for the
    /// comp-composition half of the same decision.
    /// <para/>
    /// <b>Ingredients, and the seam this leaves for hauling:</b> a bill's ingredients must already be lying
    /// within <see cref="IngredientSearchRadius"/> cells of the bench — this giver does not itself walk a
    /// pawn out to fetch them first. RimWorld's own <c>JobDriver_DoBill</c> carries a queue of chosen
    /// ingredient Things and hauls each to the bench before working the recipe; this port's <see cref="AI.Job"/>
    /// has room for exactly three fixed targets (see its own remarks) and has no queue to carry that in, and
    /// a second lane is concurrently building the general hauling path (<c>HaulGeneral</c>'s
    /// <see cref="AI.WorkGiver_Haul"/>) this pass must not duplicate. The result mirrors
    /// <see cref="Building.WorkGiver_ConstructFinishFrame"/>'s own split from
    /// <c>ConstructDeliverResourcesToFrames</c>: this giver only finishes work whose materials are already
    /// staged, and getting raw ingredients from wherever they are produced to a stockpile cell beside the
    /// bench is <see cref="AI.WorkGiver_Haul"/>'s job, already built and merged separately. A future
    /// "deliver ingredients to a bill" driver — the bill-side counterpart of
    /// <c>ConstructDeliverResourcesToFrames</c> — is the seam left open for a bench whose stock sits further
    /// out than that.
    /// </summary>
    public sealed class WorkGiver_DoBill : WorkGiver_Scanner
    {
        /// <summary>
        /// How far from the bench this giver looks for ingredients, in cells. Deliberately not RimWorld's own
        /// <see cref="Bill.ingredientSearchRadius"/> (default 999 — effectively "anywhere reachable"):
        /// RimWorld pays for that width with a job that physically carries whatever it finds back to the
        /// bench, which this port does not build (see class remarks). Capping the scan keeps "ingredients are
        /// in reach" meaning what it says — placed at the workstation, typically by a stockpile
        /// <see cref="AI.WorkGiver_Haul"/> keeps filled — instead of quietly summoning a pile from across the
        /// map. Only ever narrows a bill's own (possibly smaller) radius, never widens it.
        /// </summary>
        public const int IngredientSearchRadius = 12;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Building))
            {
                if (BillGiverFor(t) != null) yield return t;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompBillGiver? comp = BillGiverFor(thing);
            if (comp == null || comp.Props.workType != def.workType) return false;
            if (!thing.Spawned) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;

            Bill_Production? bill = FindBill(pawn, comp);
            return bill != null && TryFindIngredients(thing, bill, out _);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompBillGiver? comp = BillGiverFor(thing);
            if (comp == null) return null;
            Bill_Production? bill = FindBill(pawn, comp);
            if (bill == null || !TryFindIngredients(thing, bill, out _)) return null;
            return new Job(CraftingJobDefOf.DoBill, thing);
        }

        internal static CompBillGiver? BillGiverFor(Thing thing) => (thing as ThingWithComps)?.GetComp<CompBillGiver>();

        /// <summary>The first bill this pawn may start now. Suspended, an out-of-range skill, an unmet
        /// <see cref="SkillRequirement"/> or an unresearched recipe are all screened out by <see cref="Bill"/>
        /// itself (<see cref="Bill.PawnAllowedToStartAnew"/>, <see cref="RecipeDef.AvailableNow"/>); this only
        /// walks the stack in order.</summary>
        internal static Bill_Production? FindBill(Pawn pawn, CompBillGiver comp)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i] is Bill_Production bill && bill.ShouldDoNow()
                    && bill.recipe.AvailableNow && bill.PawnAllowedToStartAnew(pawn))
                {
                    return bill;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds real map Things near <paramref name="billGiver"/> that satisfy <paramref name="bill"/>'s
        /// recipe, through the same <see cref="BillIngredientsFinder"/> a bench or a guild bill already
        /// resolves ingredients with — that finder answers in (def, piece-count) terms rather than "which
        /// physical stack", so this replays its totals per def against the same candidate list, in the same
        /// order, to decide which real Things actually get consumed.
        /// <para/>
        /// Re-run fresh both at job-offer time and again when the work toil finishes; nothing reserves an
        /// individual ingredient stack in between (only the bench itself is reserved), so a second consumer
        /// of the same pile in that window is a known, accepted gap — see this module's own report.
        /// </summary>
        internal static bool TryFindIngredients(Thing billGiver, Bill_Production bill, out List<(Thing thing, int count)> takes)
        {
            takes = new List<(Thing, int)>();
            List<IngredientCount>? need = bill.recipe.ingredients;
            if (need == null || need.Count == 0) return true;

            Map.Map? map = billGiver.Map;
            if (map == null) return false;

            var candidates = new List<Thing>();
            CellRect scan = CellRect.CenteredOn(billGiver.Position, IngredientSearchRadius).ClipInsideMap(map);
            foreach (IntVec3 cell in scan.Cells)
            {
                foreach (Thing t in map.thingGrid.ThingsAt(cell))
                {
                    if (t.def.category == ThingCategory.Item && t.stackCount > 0) candidates.Add(t);
                }
            }
            if (candidates.Count == 0) return false;

            var candidateStacks = new List<ItemStack>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                candidateStacks.Add(new ItemStack(candidates[i].def, candidates[i].stackCount));
            }

            var chosen = new List<ItemStack>();
            if (!BillIngredientsFinder.TryFindBestIngredients(bill, candidateStacks, chosen)) return false;

            var neededByDef = new Dictionary<ThingDef, int>();
            foreach (ItemStack stack in chosen)
            {
                neededByDef.TryGetValue(stack.Def, out int have);
                neededByDef[stack.Def] = have + stack.Count;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Thing t = candidates[i];
                if (!neededByDef.TryGetValue(t.def, out int remaining) || remaining <= 0) continue;
                int take = Math.Min(remaining, t.stackCount);
                if (take <= 0) continue;
                takes.Add((t, take));
                neededByDef[t.def] = remaining - take;
            }
            return true;
        }
    }
}
