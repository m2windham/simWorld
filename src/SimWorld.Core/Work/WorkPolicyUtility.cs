using System;
using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.Work
{
    /// <summary>
    /// The civilization-scale lever for <see cref="RoleDef"/> (<c>docs/status.json</c>'s <c>work.policy</c>
    /// translation): applies one role across many citizens at once, the same "act on the aggregate, not one
    /// grid at a time" shape <see cref="God.GodManager.Activate"/> gives edicts. Deliberately a bare static
    /// utility rather than a Find-registered manager — every bit of state a role assignment needs
    /// (<see cref="Pawn_WorkSettings.Role"/>, its manual-override set) already lives on the pawn it belongs to
    /// and Scribes with it, so there is nothing civilization-wide left to own or register with
    /// <c>Find.Reset()</c>.
    /// </summary>
    public static class WorkPolicyUtility
    {
        /// <summary>
        /// Assigns <paramref name="role"/> (or clears it, with null) to every citizen in <paramref name="citizens"/>
        /// who can work at all — dead pawns, animals and anyone never enabled for work
        /// (<see cref="Pawn_WorkSettings.EverWork"/> false) are silently skipped, matching
        /// <see cref="God.EdictWorker.AppliesTo"/>'s own default (a civilization's policy directs its citizens,
        /// not its livestock or its dead). A citizen who alone carries an office's role among
        /// <paramref name="citizens"/> is skipped too: their role is the seat. Several carriers of one office's role
        /// are not a seat but a policy (or impostors the next office sweep strips), so they are reached like anyone
        /// else; which pawns are skipped is decided before any role is written. Returns how many citizens the role
        /// actually reached, so a caller (or a test) can tell a policy that reached nobody from one that reached
        /// everyone.
        /// </summary>
        public static int ApplyRoleToPopulation(IReadOnlyList<Pawn> citizens, RoleDef? role)
        {
            if (citizens == null) throw new ArgumentNullException(nameof(citizens));

            // An office holder's role is their seat, not a work policy: overwriting it emptied the seat, and the next
            // office sweep re-seated the same citizen with a fresh "Accession" line each time.
            List<Pawn>? seated = null;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (HoldsSeatAlone(citizens[i], citizens)) (seated ??= new List<Pawn>()).Add(citizens[i]);
            }

            int applied = 0;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn? p = citizens[i];
                if (p == null || p.Dead || !p.RaceProps.Humanlike || !p.workSettings.EverWork) continue;
                if (seated != null && seated.Contains(p)) continue;
                p.workSettings.SetRole(role);
                applied++;
            }
            return applied;
        }

        private static bool HoldsSeatAlone(Pawn? pawn, IReadOnlyList<Pawn> citizens)
        {
            Offices.OfficeDef? office = Offices.OfficeManager.OfficeOf(pawn);
            if (office == null || pawn!.Dead) return false;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (citizens[i] != pawn && Offices.OfficeManager.Holds(citizens[i], office)) return false;
            }
            return true;
        }
    }
}
