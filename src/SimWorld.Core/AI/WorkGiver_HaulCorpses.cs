using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries a <see cref="Corpse"/> to storage (RimWorld: <c>RimWorld.WorkGiver_HaulCorpses</c>, which is
    /// its <c>WorkGiver_Haul</c> with the scan narrowed to corpses). <c>HaulCorpses</c>'s <c>giverClass</c>
    /// in <c>WorkGivers.xml</c> — until this module it was the placeholder <see cref="WorkGiver_Pending"/>,
    /// because no <c>Corpse</c> class existed for it to find.
    /// <para/>
    /// <b>The same hauling path, not a second one.</b> Destination search is
    /// <see cref="HaulAIUtility.TryFindBestStockpileCell"/> and the job is the same
    /// <see cref="JobDefOf.HaulToCell"/>/<see cref="JobDriver_HaulToCell"/> every other haul uses; a corpse
    /// is <see cref="Defs.ThingDef.EverHaulable"/> like any other Item-category Thing, so
    /// <see cref="WorkGiver_Haul"/> would in fact find one too. What this giver adds is the separation
    /// RimWorld ships it for: bodies are their own line of work, and <c>HaulCorpses</c> sits above
    /// <c>HaulGeneral</c> in <c>priorityInType</c> so a settlement with a backlog of loose wood still clears
    /// its dead first.
    /// <para/>
    /// It does not derive from <see cref="WorkGiver_Haul"/> the way RimWorld's does only because that class
    /// is <c>sealed</c> here, and unsealing it is an edit to a file other lanes may hold (CLAUDE.md). The
    /// shared logic lives in <see cref="HaulAIUtility"/> either way, which is the half worth sharing.
    /// </summary>
    public sealed class WorkGiver_HaulCorpses : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        /// <summary>
        /// Corpses, filtered out of the Item group — see <see cref="WorkGiver_ButcherCorpse"/> for why this
        /// port has no <c>ThingRequestGroup.Corpse</c> to ask for directly.
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
            if (!(thing is Corpse) || !thing.Spawned || thing.Destroyed) return false;
            if (HaulAIUtility.IsInValidStorage(thing)) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            return HaulAIUtility.TryFindBestStockpileCell(pawn, thing, out _);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!HaulAIUtility.TryFindBestStockpileCell(pawn, thing, out IntVec3 cell)) return null;
            return new Job(JobDefOf.HaulToCell, thing, cell) { haulMode = HaulMode.ToCellStorage };
        }
    }
}
