using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// An unarmed pawn able to fight takes up the nearest free weapon within <see cref="maxDistance"/>
    /// (RimWorld: <c>RimWorld.JobGiver_PickUpOpportunisticWeapon</c>). Same gates as RimWorld's: nothing
    /// for a pawn already holding a primary (its <c>AlreadySatisfiedWithCurrentWeapon</c>), nothing for one
    /// incapable of violence, and only weapons it can reserve and reach.
    ///
    /// <para/><b>Recorded translation.</b> RimWorld puts this giver in raider and guest trees, at a recalled
    /// eight-cell radius; colonists there are armed by the player. This civilization has no player to hand
    /// out knives, so the humanlike tree carries it too, with a radius the size of a map
    /// (<c>ThinkTrees_Humanlike.xml</c>): a citizen walks to the smithy's output the way a raider grabs a
    /// gun at its feet. The field's default stays RimWorld's recalled eight.
    ///
    /// <para/><b>Trading up (crafting.materialladder).</b> RimWorld's colonist keeps whatever the player hands
    /// it; here the settlement is the player, and a smithy that has moved from flint to bronze is making blades
    /// for the people who already carry a worse one. So a citizen of the player's civilization (or a pawn of
    /// none, as a test's is) holding a melee weapon also takes up a strictly better one
    /// (<see cref="Crafting.MaterialLadder.IsBetterWeapon"/>), and drops the old one where it stands
    /// (<c>JobDriver_Equip</c>). A ranged weapon is never traded for a blade; a raider never trades up.
    /// </summary>
    public sealed class JobGiver_PickUpOpportunisticWeapon : ThinkNode_JobGiver
    {
        /// <summary>How far to look, in cells (RimWorld's eight is recalled, not sourced).</summary>
        public float maxDistance = 8f;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (pawn.equipment == null || pawn.Map == null || !pawn.RaceProps.Humanlike) return null;
            Thing? held = pawn.equipment.Primary;
            if (held != null && !MayTradeUp(pawn, held)) return null;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return null;

            float maxDistSq = maxDistance * maxDistance;
            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> items = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not ThingWithComps weapon || !weapon.def.IsWeapon || !weapon.Spawned) continue;
                if (held != null && !Crafting.MaterialLadder.IsBetterWeapon(weapon, held)) continue;
                int distSq = (weapon.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq > maxDistSq || distSq >= bestDistSq) continue;
                if (!pawn.Map.reservationManager.CanReserve(pawn, weapon)) continue;
                if (!Reachability.CanReach(pawn, weapon, PathEndMode.OnCell)) continue;
                best = weapon;
                bestDistSq = distSq;
            }
            return best != null ? new Job(ApparelJobDefOf.Equip, best) : null;
        }

        private static bool MayTradeUp(Pawn pawn, Thing held) =>
            Crafting.MaterialLadder.IsMeleeWeapon(held.def)
            && (pawn.Faction == null || ReferenceEquals(pawn.Faction, Sim.Find.FactionManager.OfPlayer));
    }
}
