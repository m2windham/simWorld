using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a butcherable <see cref="Corpse"/> and a butcher bench to take it to (RimWorld reaches the same
    /// end through <c>WorkGiver_DoBill</c> on a butcher table with a <c>ButcherCorpseFlesh</c> bill).
    /// <c>ButcherCorpses</c>'s <c>giverClass</c> in <c>WorkGiverDefs/WorkGivers_Butchery.xml</c>.
    /// <para/>
    /// <b>Translation — a work giver, not a bill.</b> RimWorld butchers through the bill system, and this
    /// port has one (<see cref="WorkGiver_DoBill"/>, <see cref="Bill_Production"/>). It is not used here, for
    /// two reasons that are about this port rather than about taste.
    /// <list type="number">
    /// <item>A bill's products come from <see cref="RecipeDef.products"/> — a fixed def-and-count list. What a
    /// carcass yields depends on the individual inside it (<see cref="Pawn.BodySize"/>, its race's meat and
    /// leather defs), which a def-level product list cannot express. RimWorld solves this with
    /// <c>specialProducts</c> + <c>Thing.ButcherProducts</c>, a per-*Thing* hook; this port's product
    /// pipeline (<see cref="GenRecipe.MakeRecipeProducts"/>) is expressed over <see cref="ItemStack"/>s and
    /// never sees the ingredient Thing at all, so the hook has nowhere to attach without rebuilding it.</item>
    /// <item>Nothing in this port ever creates a bill. Bills are added by the player in RimWorld and by a
    /// test here; no settlement-level system queues one. A butchery that only ran when a bill existed would
    /// be content that can never fire — exactly the "unreachable in practice" that made the hunting lane
    /// butcher at the kill site in the first place.</item>
    /// </list>
    /// So the settlement decides for itself that a body near its people should be butchered, the same shape
    /// <see cref="HuntingInitiative"/> and <c>Building.SettlementConstructionInitiative</c> already
    /// established for the other two "RimWorld asks the player, and there is no player" gaps. The yield is
    /// still the shipped <c>ButcherAnimal</c> <see cref="RecipeDef"/> applied through
    /// <see cref="ButcherUtility.TryButcher"/> — one formula, shared with the hunt's kill-site path, no
    /// second set of numbers.
    /// <para/>
    /// <b>What it will not offer.</b> Only animal corpses, and only while they are still worth the walk:
    /// <see cref="Corpse.IsButcherable"/> refuses a dessicated one (RimWorld's own rule that a husk yields
    /// nothing, moved to the offer so nobody crosses the map for nothing) and refuses a humanlike body
    /// outright. RimWorld allows butchering humanlike corpses and prices it in mood and ideology; this port
    /// has neither of those reactions wired to butchery, so shipping the act without the horror it should
    /// cause would be the wrong half to port first. See this module's report.
    /// </summary>
    public sealed class WorkGiver_ButcherCorpse : WorkGiver_Scanner
    {
        /// <summary>The pawn walks to the corpse first and carries it to the bench, so this is the corpse's
        /// own approach mode; the bench is reached by the driver's second goto.</summary>
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        /// <summary>No bench, no butchery — and that check is far cheaper than scanning every item on the
        /// map for corpses, so it runs first (RimWorld: <c>WorkGiver_DoBill</c> scans bill givers for the
        /// same reason).</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || !AnyButcherBenchOn(pawn.Map);

        /// <summary>
        /// Every corpse on the map. Walked out of the Item group and filtered rather than asked for
        /// directly: RimWorld has a <c>ThingRequestGroup.Corpse</c> and this port's
        /// <see cref="ThingRequestGroup"/> does not — adding one means editing both that enum and
        /// <see cref="ListerThings"/>, two shared files, to save a type test on a list this giver only walks
        /// after <see cref="ShouldSkip"/> has confirmed there is a bench to use.
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is Corpse) yield return items[i];
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Corpse corpse) || !corpse.Spawned || corpse.Destroyed) return false;
            if (!corpse.IsButcherable) return false;
            if (!Reachability.CanReach(pawn, corpse, PathEndMode)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, corpse)) return false;
            return TryFindButcherBench(pawn, out _);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!TryFindButcherBench(pawn, out Thing? bench)) return null;
            return new Job(CorpseWorkDefOf.ButcherCorpse, thing, bench!);
        }

        /// <summary>
        /// A building that butchery recipes are worked at. Read off the content that already says so —
        /// <see cref="Defs.ThingDef.AllRecipes"/> holds every <see cref="RecipeDef"/> whose
        /// <see cref="RecipeDef.recipeUsers"/> names this def, and <c>ButcherAnimal</c> already names
        /// <c>TableButcher</c> — rather than adding a field to a ThingDef or a WorkGiverDef that another lane
        /// would have to merge (CLAUDE.md's own "invert the relationship" rule; <c>CompBillGiver</c> made the
        /// same call for which work type a bench serves).
        /// </summary>
        public static bool IsButcherBench(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.def.category != ThingCategory.Building) return false;
            IReadOnlyList<RecipeDef> recipes = thing.def.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i].isButchery) return true;
            }
            return false;
        }

        public static bool AnyButcherBenchOn(Map.Map map)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (IsButcherBench(buildings[i])) return true;
            }
            return false;
        }

        /// <summary>The nearest reachable, claimable butcher bench, or false when there is none for this pawn
        /// right now (another butcher is already working the only one).</summary>
        public static bool TryFindButcherBench(Pawn pawn, out Thing? bench)
        {
            bench = null;
            Map.Map? map = pawn.Map;
            if (map == null) return false;

            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing candidate = buildings[i];
                if (!IsButcherBench(candidate)) continue;
                if (!Reachability.CanReach(pawn, candidate, PathEndMode.Touch)) continue;
                if (!map.reservationManager.CanReserve(pawn, candidate)) continue;

                int distSq = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                bench = candidate;
                bestDistSq = distSq;
            }
            return bench != null;
        }
    }
}
