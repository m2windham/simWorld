using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;

namespace SimWorld.Bench
{
    /// <summary>Generates the humanlike colonist population every measurement runs against.</summary>
    internal static class PawnFactory
    {
        /// <summary>Generates <paramref name="count"/> adult colonists (kind Colonist, fixed biological age 30
        /// so generation is deterministic given the seed on <see cref="SimWorld.Sim.Rand.Current"/>).</summary>
        public static List<Pawn> GenerateColonists(int count)
        {
            var pawns = new List<Pawn>(count);
            for (int i = 0; i < count; i++)
            {
                var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f);
                pawns.Add(PawnGenerator.GeneratePawn(request));
            }
            return pawns;
        }
    }
}
