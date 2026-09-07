using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Pawns;

namespace SimWorld.Map
{
    /// <summary>Pawns currently spawned on the map (RimWorld: <c>Verse.MapPawns</c>).</summary>
    public sealed class MapPawns
    {
        private readonly List<Pawn> pawns = new List<Pawn>();

        /// <summary>Same as <see cref="AllPawnsSpawned"/> until caravans/pods exist to take a pawn off-map without despawning.</summary>
        public IReadOnlyList<Pawn> AllPawns => pawns;

        public IReadOnlyList<Pawn> AllPawnsSpawned => pawns;

        /// <summary>Placeholder until factions exist: every humanlike pawn on the map.</summary>
        public int ColonistCount => pawns.Count(p => p.RaceProps.Humanlike);

        public void RegisterPawn(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawns.Contains(pawn)) pawns.Add(pawn);
        }

        public void DeRegisterPawn(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            pawns.Remove(pawn);
        }
    }
}
