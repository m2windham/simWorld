using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
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

        /// <summary>
        /// The same pawns again, indexed the way a fight asks for them (RimWorld: <c>Map.attackTargetsCache</c>,
        /// kept here because everything this port can attack is a <see cref="Pawn"/> — see
        /// <see cref="AttackTargetsCache"/>'s own remarks). Maintained by <see cref="RegisterPawn"/> and
        /// <see cref="DeRegisterPawn"/> below, which is why finding an enemy costs nothing in peacetime.
        /// </summary>
        public AttackTargetsCache AttackTargets { get; } = new AttackTargetsCache();

        /// <summary>Placeholder until factions exist: every humanlike pawn on the map.</summary>
        public int ColonistCount => pawns.Count(p => p.RaceProps.Humanlike);

        public void RegisterPawn(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (pawns.Contains(pawn)) return;
            pawns.Add(pawn);
            AttackTargets.RegisterTarget(pawn);
        }

        public void DeRegisterPawn(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            // Conditional where this used to be an unguarded Remove: the attack-target index must not be told
            // a pawn left twice, and a pawn that was never here must not take a registered one's place out of
            // the index by accident.
            if (!pawns.Remove(pawn)) return;
            AttackTargets.DeregisterTarget(pawn);
        }
    }
}
