using System;
using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.World;

namespace SimWorld.Caravans
{
    /// <summary>Builds a new <see cref="Caravan"/> and registers it into the world (RimWorld: <c>RimWorld.CaravanMaker</c>).</summary>
    public static class CaravanMaker
    {
        public static Caravan MakeCaravan(IEnumerable<Pawn> pawns, Faction? faction, int startTile, global::SimWorld.World.World world)
        {
            if (pawns == null) throw new ArgumentNullException(nameof(pawns));
            if (world == null) throw new ArgumentNullException(nameof(world));

            var caravan = new Caravan(WorldObjectDefOf.Caravan, startTile, faction);
            foreach (Pawn pawn in pawns)
            {
                caravan.AddPawn(pawn);
            }
            world.worldObjects.Add(caravan);
            return caravan;
        }
    }
}
