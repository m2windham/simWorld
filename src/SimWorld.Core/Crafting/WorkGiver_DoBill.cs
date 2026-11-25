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
    /// <b>Ingredients are searched for out to the bill's own radius and carried to the bench</b> (RimWorld:
    /// <c>WorkGiver_DoBill.TryFindBestBillIngredients</c> searches out to <see cref="Bill.ingredientSearchRadius"/>,
    /// 999 by default, and <c>JobDriver_DoBill</c>'s <c>CollectIngredientsToils</c> fetches each chosen stack).
    /// The chosen stacks ride on the job as <see cref="AI.Job.targetQueueB"/>/<see cref="AI.Job.countQueue"/>,
    /// nearest the bench first, and only stacks the crafter can walk to are candidates, so an ingredient in a
    /// sealed pocket is never chosen. This giver used to look only within 12 cells and the driver fetched
    /// nothing, so a bench whose stock lay further out was a bench nobody worked: on seed 40/201 a hide-coat
    /// bill wanted 38 coats with 360 to 497 leather on the map and none within 12 cells of either bench, and
    /// nothing was ever crafted (era-arc §12.3).
    /// <para/>
    /// <b>Each bill in order, the first whose ingredients can be found</b> (RimWorld:
    /// <c>WorkGiver_DoBill.StartOrResumeBillJob</c> goes on to the next bill when one's ingredients are
    /// missing). A first bill nobody could supply, such as a shirt with no cloth on the map, used to hold up
    /// every bill behind it on the same bench.
    /// </summary>
    public sealed class WorkGiver_DoBill : WorkGiver_Scanner
    {
        /// <summary>
        /// <b>Not this giver's search limit any more</b>: a bill searches out to its own
        /// <see cref="Bill.ingredientSearchRadius"/> and the crafter carries what it finds. This is the reach the
        /// settlement's own planners still site a bench within of its stock (<c>StonecutterInitiative</c>,
        /// <c>CookingInitiative</c>, <c>SmeltingInitiative</c>, <c>Building.SettlementWorkshops</c>): with no
        /// player to place it, a bench beside its materials saves the trips. The name is kept because those
        /// planners and their tests read it.
        /// </summary>
        public const int IngredientSearchRadius = 12;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>No bench of this giver's trade on the map, nothing to scan. That is exactly the case where
        /// <see cref="PotentialWorkThingsGlobal"/> would yield nothing, so skipping it changes no answer; it only
        /// spares the scan its setup.</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            IReadOnlyList<Thing> givers = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            for (int i = 0; i < givers.Count; i++)
            {
                if (ServesThisTrade(givers[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Benches of this giver's own trade (RimWorld: <c>PotentialWorkThingRequest</c> =
        /// <c>ThingRequest.ForGroup(PotentialBillGiver)</c>). Walks the bill-giver group rather than every
        /// building: once a settlement has a few hundred walls and storage huts, walking the whole Building group
        /// on every job search was most of the simulation. The group keeps the Building group's relative order,
        /// and a bench of another trade is the first thing <see cref="HasJobOnThing"/> rejects, so the nearest
        /// accepted bench — earliest-listed on a tie — is the same one the wider walk found.
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> givers = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            for (int i = 0; i < givers.Count; i++)
            {
                if (ServesThisTrade(givers[i])) yield return givers[i];
            }
        }

        private bool ServesThisTrade(Thing thing)
        {
            CompBillGiver? comp = BillGiverFor(thing);
            return comp != null && comp.Props.workType == def.workType;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompBillGiver? comp = BillGiverFor(thing);
            if (comp == null || comp.Props.workType != def.workType) return false;
            if (!thing.Spawned) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;

            return TryFindBillAndIngredients(pawn, comp, thing, out _, out _);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompBillGiver? comp = BillGiverFor(thing);
            if (comp == null) return null;
            if (!TryFindBillAndIngredients(pawn, comp, thing, out _, out List<(Thing thing, int count)> chosen))
            {
                return null;
            }

            // The chosen stacks ride along on the job so the driver spends those exact Things — and, before it
            // spends a tick of work, claims them (RimWorld: WorkGiver_DoBill.TryStartNewDoBillJob fills
            // targetQueueB/countQueue the same way, and JobDriver_DoBill reserves the queue).
            var job = new Job(CraftingJobDefOf.DoBill, thing)
            {
                targetQueueB = new List<LocalTargetInfo>(chosen.Count),
                countQueue = new List<int>(chosen.Count),
                haulMode = HaulMode.ToCellNonStorage,
            };
            for (int i = 0; i < chosen.Count; i++)
            {
                job.targetQueueB.Add(chosen[i].thing);
                job.countQueue.Add(chosen[i].count);
            }
            return job;
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
                if (bills[i] is Bill_Production bill && CanStart(pawn, bill)) return bill;
            }
            return null;
        }

        internal static bool CanStart(Pawn pawn, Bill_Production bill) =>
            bill.ShouldDoNow() && bill.recipe.AvailableNow && bill.PawnAllowedToStartAnew(pawn);

        /// <summary>
        /// The first bill on <paramref name="comp"/>'s stack that <paramref name="pawn"/> may start and whose
        /// ingredients can be found, with the stacks chosen for it (RimWorld:
        /// <c>WorkGiver_DoBill.StartOrResumeBillJob</c>'s loop over the bill stack, which moves on past a bill
        /// whose ingredients are missing rather than stopping there).
        /// </summary>
        internal static bool TryFindBillAndIngredients(Pawn pawn, CompBillGiver comp, Thing billGiver,
            out Bill_Production? found, out List<(Thing thing, int count)> chosen)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i] is not Bill_Production bill || !CanStart(pawn, bill)) continue;
                if (!TryFindBestBillIngredients(bill, pawn, billGiver, out chosen)) continue;
                found = bill;
                return true;
            }
            found = null;
            chosen = new List<(Thing, int)>();
            return false;
        }

        /// <summary>
        /// Finds real map Things within the bill's search radius of <paramref name="billGiver"/> that satisfy <paramref name="bill"/>'s
        /// recipe (RimWorld: <c>WorkGiver_DoBill.TryFindBestBillIngredients</c>), through the same
        /// <see cref="BillIngredientsFinder"/> a bench or a guild bill already resolves ingredients with —
        /// that finder answers in (def, piece-count) terms rather than "which physical stack", so this
        /// replays its totals per def against the same candidate list, in the same order, to decide which
        /// real Things actually get consumed.
        /// <para/>
        /// Only stacks <paramref name="pawn"/> could claim are candidates, exactly as RimWorld's own
        /// <c>baseValidator</c> ends in <c>pawn.CanReserve(t)</c>: a pile another pawn's job is already
        /// spending is not available to this one, however close to the bench it lies. The caller turns the
        /// answer into <see cref="Job.targetQueueB"/> and the driver then reserves it, which is what closes
        /// the window between a bill being offered and its ingredients being consumed.
        /// </summary>
        internal static bool TryFindBestBillIngredients(Bill_Production bill, Pawn pawn, Thing billGiver, out List<(Thing thing, int count)> takes)
        {
            takes = new List<(Thing, int)>();
            List<IngredientCount>? need = bill.recipe.ingredients;
            if (need == null || need.Count == 0) return true;

            Map.Map? map = billGiver.Map;
            if (map == null) return false;

            List<Thing> candidates = IngredientCandidates(bill, billGiver, t =>
                map.reservationManager.CanReserve(pawn, t) && Reachability.CanReach(pawn, t, PathEndMode.ClosestTouch));
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

        /// <summary>
        /// Every spawned item stack on <paramref name="billGiver"/>'s map that some ingredient of
        /// <paramref name="bill"/> could use, lying within the bill's <see cref="Bill.ingredientSearchRadius"/>
        /// of the bench and passing <paramref name="usable"/>, nearest the bench first (thing id breaks a tie, so
        /// two identical runs choose identically). RimWorld walks regions outward from the bench and stops once
        /// it has enough; the order it hands the finder is the same, nearest first.
        /// <para/>
        /// Walked by def through the lister rather than cell by cell, because the radius is now the whole map.
        /// </summary>
        internal static List<Thing> IngredientCandidates(Bill bill, Thing billGiver, Func<Thing, bool> usable)
        {
            var candidates = new List<Thing>();
            Map.Map? map = billGiver.Map;
            List<IngredientCount>? need = bill.recipe.ingredients;
            if (map == null || need == null) return candidates;

            float radiusSquared = bill.ingredientSearchRadius * bill.ingredientSearchRadius;
            IntVec3 bench = billGiver.Position;
            var seen = new HashSet<ThingDef>();
            for (int n = 0; n < need.Count; n++)
            {
                foreach (ThingDef def in need[n].filter.AllowedThingDefs)
                {
                    if (!seen.Add(def) || !bill.ingredientFilter.Allows(def)) continue;
                    IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(def);
                    for (int i = 0; i < stacks.Count; i++)
                    {
                        Thing t = stacks[i];
                        if (!t.Spawned || t.def.category != ThingCategory.Item || t.stackCount <= 0) continue;
                        if ((t.Position - bench).LengthHorizontalSquared > radiusSquared) continue;
                        if (!usable(t)) continue;
                        candidates.Add(t);
                    }
                }
            }
            candidates.Sort((a, b) =>
            {
                int byDistance = (a.Position - bench).LengthHorizontalSquared.CompareTo((b.Position - bench).LengthHorizontalSquared);
                return byDistance != 0 ? byDistance : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });
            return candidates;
        }
    }
}
