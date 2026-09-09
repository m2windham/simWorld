using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// Who holds which <see cref="IdeoRoleDef"/> in one <see cref="Ideo"/> (RimWorld: <c>Precept_Role</c>'s own
    /// chosen-pawns list, one per role there; folded into a single pawn→role map here since a citizen can hold
    /// at most one ideoligion role at a time in this port). Deliberately dumb: it only enforces
    /// <see cref="IdeoRoleDef.maxHolders"/>; whether a role is actually grantable at all (some precept in the
    /// owning ideoligion must name it via <see cref="PreceptDef.grantsRole"/>) is <see cref="Ideo.TryAssignRole"/>'s
    /// own gate, the same "Def says what could apply, the manager says what actually is" split
    /// <see cref="God.EdictDef"/>/<see cref="God.GodManager"/> already draw.
    /// </summary>
    public sealed class IdeoRoleTracker : IExposable
    {
        private Dictionary<Pawn, IdeoRoleDef> holders = new Dictionary<Pawn, IdeoRoleDef>();

        public IdeoRoleDef? RoleOf(Pawn pawn) => pawn != null && holders.TryGetValue(pawn, out IdeoRoleDef? role) ? role : null;

        public int CountHolding(IdeoRoleDef role)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            int n = 0;
            foreach (KeyValuePair<Pawn, IdeoRoleDef> kv in holders)
            {
                if (kv.Value == role) n++;
            }
            return n;
        }

        /// <summary>Grants <paramref name="role"/> to <paramref name="pawn"/>, replacing any other role they
        /// held. False (no-op) when <paramref name="role"/> is already at <see cref="IdeoRoleDef.maxHolders"/>
        /// and <paramref name="pawn"/> is not themselves one of the current holders.</summary>
        public bool TryAssign(Pawn pawn, IdeoRoleDef role)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (role == null) throw new ArgumentNullException(nameof(role));

            if (holders.TryGetValue(pawn, out IdeoRoleDef? existing) && existing == role) return false;
            if (CountHolding(role) >= role.maxHolders) return false;

            holders[pawn] = role;
            return true;
        }

        /// <summary>Strips whatever role <paramref name="pawn"/> holds, if any. Returns false when they held none.</summary>
        public bool Unassign(Pawn pawn) => pawn != null && holders.Remove(pawn);

        public void ExposeData()
        {
            Dictionary<Pawn, IdeoRoleDef>? dict = holders;
            Scribe_Collections.Look(ref dict, "holders", LookMode.Reference, LookMode.Def);
            holders = dict ?? new Dictionary<Pawn, IdeoRoleDef>();
        }
    }
}
