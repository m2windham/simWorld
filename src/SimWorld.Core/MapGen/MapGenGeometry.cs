using System;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Turning "which neighbouring tile" into "which way on this map" — the one piece of geometry every gen
    /// step that carries something across the world/map seam needs. Roads and rivers both arrive from a real
    /// neighbour, and both used to answer that question differently: roads by real bearing, rivers by a coin
    /// flip. This is the shared answer.
    /// </summary>
    internal static class MapGenGeometry
    {
        /// <summary>True when <paramref name="tileId"/> is a tile this grid can actually locate. A
        /// <see cref="RoadLink"/>/<see cref="RiverLink"/> neighbour id comes from content or from a test
        /// fixture, so it is not guaranteed to index this grid at all.</summary>
        public static bool CanLocate(WorldGrid? grid, int tileId) =>
            grid != null && tileId >= 0 && tileId < grid.TilesCount;

        /// <summary>
        /// Where a straight ray from the map's centre, aimed at <paramref name="toTile"/>'s real compass
        /// bearing from <paramref name="fromTile"/>, exits the map's rectangular border.
        /// </summary>
        public static IntVec3 EdgePointTowards(Map.Map map, WorldGrid grid, int fromTile, int toTile)
        {
            (double lat1, double lon1) = grid.LongLatOf(fromTile);
            (double lat2, double lon2) = grid.LongLatOf(toTile);
            double bearing = InitialBearingRadians(lat1, lon1, lat2, lon2);
            return EdgePointAtBearing(map, bearing);
        }

        /// <summary>Where a ray from the map's centre at <paramref name="bearing"/> (radians clockwise from north) leaves the map.</summary>
        public static IntVec3 EdgePointAtBearing(Map.Map map, double bearing)
        {
            // North (bearing 0) is +z, east (bearing 90°) is +x — the same facing convention Rot4 already uses.
            double dirX = Math.Sin(bearing);
            double dirZ = Math.Cos(bearing);

            double cx = (map.Size.x - 1) / 2.0;
            double cz = (map.Size.z - 1) / 2.0;

            double t = double.PositiveInfinity;
            if (dirX > 1e-9) t = Math.Min(t, (map.Size.x - 1 - cx) / dirX);
            else if (dirX < -1e-9) t = Math.Min(t, (0 - cx) / dirX);
            if (dirZ > 1e-9) t = Math.Min(t, (map.Size.z - 1 - cz) / dirZ);
            else if (dirZ < -1e-9) t = Math.Min(t, (0 - cz) / dirZ);
            if (double.IsInfinity(t)) t = 0.0; // a zero-length bearing (the tile aimed at itself)

            int x = GenMath.Clamp((int)Math.Round(cx + dirX * t), 0, map.Size.x - 1);
            int z = GenMath.Clamp((int)Math.Round(cz + dirZ * t), 0, map.Size.z - 1);
            return new IntVec3(x, 0, z);
        }

        /// <summary>The compass bearing from <paramref name="fromTile"/> to <paramref name="toTile"/>, radians clockwise from north.</summary>
        public static double BearingBetween(WorldGrid grid, int fromTile, int toTile)
        {
            (double lat1, double lon1) = grid.LongLatOf(fromTile);
            (double lat2, double lon2) = grid.LongLatOf(toTile);
            return InitialBearingRadians(lat1, lon1, lat2, lon2);
        }

        /// <summary>Great-circle initial bearing from point 1 to point 2, in radians, clockwise from north (the standard navigation formula).</summary>
        public static double InitialBearingRadians(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = lat1 * Math.PI / 180.0;
            double phi2 = lat2 * Math.PI / 180.0;
            double deltaLambda = (lon2 - lon1) * Math.PI / 180.0;

            double y = Math.Sin(deltaLambda) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
            return Math.Atan2(y, x);
        }
    }
}
