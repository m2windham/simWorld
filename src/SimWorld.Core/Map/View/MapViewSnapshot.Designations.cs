using System;
using System.Collections.Generic;
using SimWorld.Designations;

namespace SimWorld.Map.View
{
    public sealed partial class MapViewSnapshot
    {
        /// <summary>The designation kinds a settlement has an initiative of its own for, in the order
        /// <see cref="DesignationsView.Initiatives"/> lists them — what <see cref="MapCommands.SetSettlementInitiative"/>
        /// accepts.</summary>
        internal static IReadOnlyList<DesignationDef> InitiativeKinds => new[]
        {
            DesignationDefOf.Mine, PlayerDesignationDefOf.CutPlant, PlayerDesignationDefOf.Hunt, PlayerDesignationDefOf.Tame,
        };

        /// <summary>
        /// Every designation on whichever settlement the god has open, and its initiative switches — see
        /// <see cref="DesignationsView"/>. Resolved off the focus, as <see cref="Capture()"/> is.
        /// </summary>
        public static DesignationsView DesignationsOf()
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return AbsentDesignations(reason!);
            SimWorld.Map.Map? map = InteriorOf(tile.Value, out string? interiorReason, out _);
            return map == null ? AbsentDesignations(interiorReason!) : DesignationsOf(map);
        }

        /// <summary>The same read on a map held directly — for the core's own tests and tools.</summary>
        public static DesignationsView DesignationsOf(SimWorld.Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            DesignationManager dm = map.designationManager;
            IReadOnlyList<Designation> all = dm.AllDesignations;
            var marks = new List<DesignationView>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                Designation des = all[i];
                Things.Thing? t = des.target.Thing;
                if (des.target.HasThing && (t == null || !t.Spawned || t.Map != map)) continue;
                marks.Add(new DesignationView(
                    des.def.defName,
                    des.target.HasThing,
                    t?.thingIDNumber ?? -1,
                    t != null ? t.Position : des.target.Cell,
                    des.source == DesignationSource.Player));
            }

            IReadOnlyList<DesignationDef> kinds = InitiativeKinds;
            var switches = new List<InitiativeSwitchView>(kinds.Count);
            for (int i = 0; i < kinds.Count; i++)
            {
                switches.Add(new InitiativeSwitchView(kinds[i].defName, dm.SettlementInitiativeOn(kinds[i])));
            }

            return new DesignationsView(true, null, map.mapView.SessionId, dm.Version, marks, switches);
        }

        private static DesignationsView AbsentDesignations(string reason) =>
            new DesignationsView(false, reason, 0, 0, Array.Empty<DesignationView>(), Array.Empty<InitiativeSwitchView>());
    }
}
