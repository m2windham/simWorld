using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Delivers material to the walls and doors of houses before anything else (translation: citizen-initiated
    /// construction). RimWorld has no such giver, because its player decides which site gets a short material by
    /// drawing it first or forbidding the rest; here the settlement is the player and draws beds, storage and
    /// houses together, and <see cref="WorkGiver_ConstructDeliverResources"/> takes the nearest site.
    /// <para/>
    /// On a wood-poor map that left houses open for want of one door's wood while the same trees went into
    /// spare beds and storage huts. A house is shelter only once its ring closes: a room with one unroofed cell
    /// is outdoor air (<see cref="RoomTracker"/>). It is an order, not a reserve: whatever the waiting shells
    /// cannot use goes on to the other sites as before, so a blocked or finished shell holds nothing back.
    /// </summary>
    public sealed class WorkGiver_ConstructDeliverResourcesToShells : WorkGiver_ConstructDeliverResources
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in dwellings[i].Parts)
                {
                    if (kind == DwellingPartKind.Bed) continue;
                    IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
                    for (int t = 0; t < here.Count; t++)
                    {
                        if (here[t] is Blueprint || here[t] is Frame) yield return here[t];
                    }
                }
            }
        }
    }
}
