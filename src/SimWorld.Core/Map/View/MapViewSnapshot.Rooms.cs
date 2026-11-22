using System;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The room temperature read (REQ 007, ask 2): <see cref="TemperatureAt(IntVec3)"/> and its overloads. Its own
    /// file so the per-frame capture in <c>MapViewSnapshot.cs</c> stays as it was; see
    /// <see cref="CellTemperatureView"/> for why this is a click-time query rather than a layer.
    /// </summary>
    public sealed partial class MapViewSnapshot
    {
        /// <summary>How warm <paramref name="cell"/> is, inside and out, in whichever settlement the god has open:
        /// the same focus <see cref="Capture()"/> resolves.</summary>
        public static CellTemperatureView TemperatureAt(IntVec3 cell)
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return CellTemperatureView.Absent(reason!);
            return TemperatureAt(tile.Value, cell);
        }

        /// <summary>How warm <paramref name="cell"/> is in the settlement on <paramref name="tile"/>.</summary>
        public static CellTemperatureView TemperatureAt(int tile, IntVec3 cell)
        {
            SimWorld.Map.Map? map = InteriorOf(tile, out string? reason, out _);
            if (map == null) return CellTemperatureView.Absent(reason!);
            return TemperatureAt(map, cell);
        }

        /// <summary>How warm <paramref name="cell"/> is on a map held directly, for the core's own tests and
        /// tools, as <see cref="Capture(SimWorld.Map.Map)"/> is.</summary>
        public static CellTemperatureView TemperatureAt(SimWorld.Map.Map map, IntVec3 cell)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return CellTemperatureView.Of(map, cell);
        }
    }
}
