using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Offices;
using SimWorld.Work;

namespace SimWorld.God.View
{
    /// <summary>One standing work role a god can apply, as a roles screen offers it — the catalogue half of
    /// <see cref="GodCommands.ApplyRoleToSettlement"/> and <see cref="GodCommands.ApplyRoleToCivilization"/>.
    /// Who holds one now is <see cref="CitizenLine.RoleDefName"/> and <see cref="WorkPriorityRow.RoleDefName"/>.</summary>
    public sealed class RoleOption
    {
        internal RoleOption(
            string defName, string label, string description,
            IReadOnlyList<string> emphasizedWorkTypeDefNames, IReadOnlyList<string> emphasizedWorkTypeLabels,
            string? officeDefName)
        {
            DefName = defName;
            Label = label;
            Description = description;
            EmphasizedWorkTypeDefNames = emphasizedWorkTypeDefNames;
            EmphasizedWorkTypeLabels = emphasizedWorkTypeLabels;
            OfficeDefName = officeDefName;
        }

        /// <summary>The handle to pass to <see cref="GodCommands.ApplyRoleToSettlement"/> or
        /// <see cref="GodCommands.ApplyRoleToCivilization"/>. Not a Def.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Description { get; }

        /// <summary>The work types it lifts to first-attempted priority (<see cref="RoleDef.emphasizedWorkTypes"/>),
        /// in content's order — the same defNames <see cref="WorkTypeColumn.DefName"/> carries, so a host can
        /// highlight the matching work-tab columns.</summary>
        public IReadOnlyList<string> EmphasizedWorkTypeDefNames { get; }

        /// <summary>The same work types' labels, index for index with <see cref="EmphasizedWorkTypeDefNames"/>.</summary>
        public IReadOnlyList<string> EmphasizedWorkTypeLabels { get; }

        /// <summary>
        /// The office whose holder carries this role (<see cref="OfficeDef.role"/>), or null for an ordinary
        /// role. Applying an office's role is not refused — the command accepts every <see cref="RoleDef"/> —
        /// but carrying it is what holding that seat <i>means</i> (<see cref="OfficeManager.OfficeOf"/>), and the
        /// office sweep reconciles who holds it afterwards, so a host will usually want to offer these
        /// differently, or not at all, on a roles screen.
        /// </summary>
        public string? OfficeDefName { get; }

        internal static RoleOption For(RoleDef def, OfficeDef? office)
        {
            var defNames = new string[def.emphasizedWorkTypes.Count];
            var labels = new string[def.emphasizedWorkTypes.Count];
            for (int i = 0; i < def.emphasizedWorkTypes.Count; i++)
            {
                defNames[i] = def.emphasizedWorkTypes[i].defName;
                labels[i] = def.emphasizedWorkTypes[i].LabelCap;
            }

            return new RoleOption(def.defName, def.LabelCap, def.description ?? "", defNames, labels, office?.defName);
        }
    }

    /// <summary>
    /// The roles <see cref="GodCommands.ApplyRoleToSettlement"/> and
    /// <see cref="GodCommands.ApplyRoleToCivilization"/> accept, listed without a game — the same shape and
    /// reasoning as <see cref="StorytellerCatalogue"/>. It lists; applying is the commands' job. Clearing a role
    /// is not an entry here: it is a null <c>roleDefName</c> to either command.
    ///
    /// <para/>Every option listed is one both commands accept — they refuse a role only for a name that does
    /// not resolve, and every name here was read off a loaded def. Empty rather than throwing when no content
    /// is loaded.
    /// </summary>
    public static class RoleCatalogue
    {
        /// <summary>
        /// Every <see cref="RoleDef"/> in content: ordinary roles first, then those an office carries (see
        /// <see cref="RoleOption.OfficeDefName"/>), each group by defName (ordinal). <see cref="RoleDef"/> has no
        /// display-order field, so the order is total and stable rather than dependent on file load order.
        /// </summary>
        public static IReadOnlyList<RoleOption> Roles()
        {
            IReadOnlyList<RoleDef> all = DefDatabase<RoleDef>.AllDefsListForReading;
            IReadOnlyList<OfficeDef> offices = DefDatabase<OfficeDef>.AllDefsListForReading;

            var options = new List<RoleOption>(all.Count);
            for (int i = 0; i < all.Count; i++) options.Add(RoleOption.For(all[i], OfficeCarrying(all[i], offices)));

            options.Sort(static (a, b) =>
            {
                int byOffice = (a.OfficeDefName != null).CompareTo(b.OfficeDefName != null);
                return byOffice != 0 ? byOffice : string.CompareOrdinal(a.DefName, b.DefName);
            });
            return options;
        }

        private static OfficeDef? OfficeCarrying(RoleDef role, IReadOnlyList<OfficeDef> offices)
        {
            for (int i = 0; i < offices.Count; i++)
            {
                if (ReferenceEquals(offices[i].role, role)) return offices[i];
            }
            return null;
        }
    }
}
