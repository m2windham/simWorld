using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench
{
    /// <summary>Minimal Scribe root for the save/load measurement — mirrors PawnHolder in
    /// tests/SimWorld.Core.Tests/Pawns/PawnTests.cs.</summary>
    internal sealed class SaveHolder : IExposable
    {
        public List<Pawn>? pawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }
}
