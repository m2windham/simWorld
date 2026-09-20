using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.God.View
{
    /// <summary>
    /// The aggregate half of the work lever — <see cref="WorkPolicyUtility.ApplyRoleToPopulation"/>, wired up
    /// at last. <c>docs/design/player-first.md</c> §10 found it "built, tested, and never called from
    /// <c>src/</c>": spec §10 describes the exact shape ("policy acts on the aggregate, the same way
    /// <see cref="GodManager.Activate"/> does for an edict") and nothing ever gave a host a way to reach it.
    ///
    /// <para/><b>Roles, not a per-pawn grid — deliberately.</b> §6 of the same document spends its whole
    /// length on why: a per-citizen priority table does not survive scale (RimWorld's own needs mods past
    /// about fifteen pawns; Dwarf Fortress's needed an external application for a decade). Nothing here, or
    /// anywhere on this seam, exposes <see cref="Pawn_WorkSettings.SetPriority"/>. A <see cref="RoleDef"/> is
    /// the lever, applied to as many citizens at once as the player names — one settlement, or every
    /// settlement there is.
    ///
    /// <para/><b>A standing rule, not a one-time act.</b> Applying a role writes
    /// <see cref="Pawn_WorkSettings.Role"/>, which persists and re-derives priorities on its own
    /// (<see cref="Pawn_WorkSettings.SetRole"/>) — it is the "group, standing rule" / "global, standing rule"
    /// cells of §4's grid, matching an edict or a research focus, never the "global, one-time act" cell the
    /// same section says must stay empty.
    ///
    /// <para/><b>Never overwrites a citizen's own explicit choice.</b>
    /// <c>Pawn_WorkSettings.manualPriorities</c> already protects a priority a caller set directly with
    /// <see cref="Pawn_WorkSettings.SetPriority"/> from any later role re-application — see that class's own
    /// doc — and this command goes through <see cref="WorkPolicyUtility.ApplyRoleToPopulation"/>, which goes
    /// through exactly that path. Nothing here re-derives or bypasses the protection.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Applies <paramref name="roleDefName"/> to every working citizen of the settlement on
        /// <paramref name="tile"/> — or clears their role, with null, back to whatever
        /// <see cref="Pawn_WorkSettings.DefaultPriority"/> gives every non-disabled work type.
        /// <see cref="WorkPolicyUtility.ApplyRoleToPopulation"/>'s own doc says who is silently skipped (the
        /// dead, animals, anyone never enabled for work at all) and why.
        /// </summary>
        public static GodCommandResult ApplyRoleToSettlement(int tile, string? roleDefName)
        {
            World.Settlement? settlement = AttentionManager.SettlementAt(tile);
            if (settlement == null) return GodCommandResult.UnknownSettlement(tile);

            if (!TryResolveRole(roleDefName, out RoleDef? role, out GodCommandResult? refusal))
            {
                return refusal!;
            }

            int applied = WorkPolicyUtility.ApplyRoleToPopulation(settlement.Citizens, role);
            return GodCommandResult.Done(
                DescribeRole(role) + " applied to " + applied.ToString(CultureInfo.InvariantCulture) +
                " citizen(s) of " + settlement.name + ".");
        }

        /// <summary>
        /// Applies <paramref name="roleDefName"/> across every settlement in the running civilization at
        /// once — the "global, standing rule" reading of §4's grid, alongside an edict and a research focus.
        /// Refused only when no world is running at all; an empty civilization (a world with no settlement
        /// yet) is not a refusal, since applying a policy to nobody is not impossible, only vacuous, and it
        /// says so honestly by reporting zero citizens reached.
        /// </summary>
        public static GodCommandResult ApplyRoleToCivilization(string? roleDefName)
        {
            if (!TryResolveRole(roleDefName, out RoleDef? role, out GodCommandResult? refusal))
            {
                return refusal!;
            }

            World.World? world = Find.World;
            if (world == null)
            {
                return GodCommandResult.Refused("No game is running, so there is no civilization to apply a role across.");
            }

            int applied = 0;
            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is World.Settlement settlement)
                {
                    applied += WorkPolicyUtility.ApplyRoleToPopulation(settlement.Citizens, role);
                }
            }

            return GodCommandResult.Done(
                DescribeRole(role) + " applied to " + applied.ToString(CultureInfo.InvariantCulture) +
                " citizen(s) across the civilization.");
        }

        /// <summary>Null clears the role (a legitimate standing choice: nobody is emphasised); a non-null,
        /// non-empty name that does not resolve is the one thing this refuses.</summary>
        private static bool TryResolveRole(string? roleDefName, out RoleDef? role, out GodCommandResult? refusal)
        {
            if (roleDefName == null)
            {
                role = null;
                refusal = null;
                return true;
            }

            role = DefDatabase<RoleDef>.GetNamedSilentFail(roleDefName);
            if (role == null)
            {
                refusal = GodCommandResult.Refused("No role named '" + roleDefName + "'.");
                return false;
            }

            refusal = null;
            return true;
        }

        private static string DescribeRole(RoleDef? role) => role == null ? "No role (cleared)" : role.LabelCap;
    }
}
