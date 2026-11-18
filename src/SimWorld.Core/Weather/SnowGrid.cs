using System;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Weather
{
    /// <summary>
    /// How deep the snow lies on every cell of one map, 0 (bare) to <see cref="MaxDepth"/> (RimWorld:
    /// <c>Verse.SnowGrid</c>). <see cref="SteadyEnvironmentEffects"/> adds to it while it snows and takes
    /// from it while it is above freezing; <see cref="JobDriver_ClearSnow"/> zeroes a cell by hand. What the
    /// depth does is <see cref="SnowUtility"/>'s: a category, and the ticks that category adds to walking a
    /// cell (<see cref="Map.PathGrid.CalculatedCostAt(IntVec3, bool)"/>).
    /// <para/>
    /// <b>Where it lives.</b> On <see cref="Map.Map.snowGrid"/>, built with the map's other grids and before
    /// its <see cref="Map.PathGrid"/> — the path grid reads it for every cell it costs, so it has to exist
    /// first, including on a load (see <see cref="Map.Map.ExposeData"/>, which decodes it before the path
    /// grid is rebuilt for exactly that reason).
    /// <para/>
    /// <b>What <see cref="CheckVisualOrPathCostChange"/> leaves out.</b> RimWorld's version also dirties the
    /// snow and thing map meshes, and does it on a <c>Rand.Value &lt; 0.0125f</c> roll for small changes so
    /// the drawn snow creeps up without a remesh every touch. This core draws nothing (it is engine-free), and
    /// a presentation-only draw from the seeded stream would shift every roll after it for no simulated
    /// effect — so the roll is not ported. In its place the host-facing snow version
    /// (<see cref="Map.View.MapViewTracker.SnowVersion"/>) moves on a deterministic rule: when a cell crosses
    /// one of <see cref="VisualSteps"/> fixed depth steps or changes <see cref="SnowCategory"/>. The path-cost
    /// half, which is simulation, is ported exactly.
    /// </summary>
    public sealed class SnowGrid
    {
        /// <summary>Deepest snow can lie (RimWorld: <c>SnowGrid.MaxDepth</c>).</summary>
        public const float MaxDepth = 1f;

        /// <summary>How many equal depth steps 0..<see cref="MaxDepth"/> is cut into for the host's redraw: a
        /// cell crossing from one to another bumps <see cref="Map.View.MapViewTracker.SnowVersion"/>. Not a
        /// RimWorld number — it replaces RimWorld's <c>Rand.Value &lt; 0.0125f</c> remesh roll (see the class
        /// doc) with a rule that draws nothing from the stream.</summary>
        public const int VisualSteps = 16;

        private readonly Map.Map map;
        private readonly float[] depthGrid;

        /// <summary>Sum of every cell's depth, kept as cells change (RimWorld: <c>SnowGrid.totalDepth</c>).
        /// Read by <see cref="WorkGiver_ClearSnow.ShouldSkip"/> and <see cref="SteadyEnvironmentEffects"/> so
        /// neither walks a snowless map.</summary>
        private double totalDepth;

        public SnowGrid(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            depthGrid = new float[map.cellIndices.NumGridCells];
        }

        public float TotalDepth => (float)totalDepth;

        public float GetDepth(IntVec3 c)
        {
            if (!GenGrid.InBounds(c, map)) return 0f;
            return depthGrid[map.cellIndices.CellToIndex(c)];
        }

        public SnowCategory GetCategory(IntVec3 c) => SnowUtility.GetSnowCategory(GetDepth(c));

        /// <summary>
        /// Whether snow can lie on this cell at all (RimWorld: <c>SnowGrid.CanHaveSnow</c>): not under a wall
        /// or anything else that fills its cell (<see cref="CanCoexistWithSnow"/>), and not on terrain that
        /// does not hold it (<see cref="TerrainDef.holdSnow"/> — water).
        /// </summary>
        private bool CanHaveSnow(IntVec3 c)
        {
            Thing? edifice = map.edificeGrid[c];
            if (edifice != null && !CanCoexistWithSnow(edifice.def)) return false;
            return map.terrainGrid.TerrainAt(c).holdSnow;
        }

        /// <summary>RimWorld: <c>SnowGrid.CanCoexistWithSnow</c> — a building that fills its cell keeps the
        /// snow off it.</summary>
        public static bool CanCoexistWithSnow(ThingDef def) =>
            !(def.category == ThingCategory.Building && def.Fillage == FillCategory.Full);

        /// <summary>
        /// Adds (or, negative, melts) snow on one cell, clamped to 0..<see cref="MaxDepth"/> (RimWorld:
        /// <c>SnowGrid.AddDepth</c>). A cell that cannot hold snow is zeroed instead.
        /// </summary>
        public void AddDepth(IntVec3 c, float depthToAdd)
        {
            int i = map.cellIndices.CellToIndex(c);
            float oldDepth = depthGrid[i];
            if (oldDepth <= 0f && depthToAdd < 0f) return;
            if (oldDepth >= 0.999f && depthToAdd > 0f) return;
            if (!CanHaveSnow(c))
            {
                ClearUnholdable(c, i, oldDepth);
                return;
            }

            float newDepth = GenMath.Clamp(oldDepth + depthToAdd, 0f, MaxDepth);
            float delta = newDepth - oldDepth;
            totalDepth += delta;
            if (Math.Abs(delta) > 0.0001f)
            {
                depthGrid[i] = newDepth;
                CheckVisualOrPathCostChange(c, oldDepth, newDepth);
            }
        }

        /// <summary>Sets one cell's depth outright, clamped (RimWorld: <c>SnowGrid.SetDepth</c>) — what clearing
        /// snow uses.</summary>
        public void SetDepth(IntVec3 c, float newDepth)
        {
            int i = map.cellIndices.CellToIndex(c);
            float oldDepth = depthGrid[i];
            if (!CanHaveSnow(c))
            {
                ClearUnholdable(c, i, oldDepth);
                return;
            }

            newDepth = GenMath.Clamp(newDepth, 0f, MaxDepth);
            depthGrid[i] = newDepth;
            totalDepth += newDepth - oldDepth;
            CheckVisualOrPathCostChange(c, oldDepth, newDepth);
        }

        /// <summary>RimWorld zeroes a cell that can no longer hold snow without taking it off
        /// <c>totalDepth</c>, so the total drifts upward for ever once a wall goes up on snow; this port keeps
        /// the total honest, since <see cref="WorkGiver_ClearSnow.ShouldSkip"/> trusts it.</summary>
        private void ClearUnholdable(IntVec3 c, int i, float oldDepth)
        {
            if (oldDepth == 0f) return;
            depthGrid[i] = 0f;
            totalDepth -= oldDepth;
            CheckVisualOrPathCostChange(c, oldDepth, 0f);
        }

        /// <summary>RimWorld: <c>SnowGrid.CheckVisualOrPathCostChange</c>. Only a change of
        /// <see cref="SnowCategory"/> moves what walking the cell costs, so only that re-costs it. The visual
        /// half bumps the host's snow version on that same change or on crossing a <see cref="VisualSteps"/>
        /// step — never on a roll (see the class doc) — so snow creeping up within one step touches nothing.</summary>
        private void CheckVisualOrPathCostChange(IntVec3 c, float oldDepth, float newDepth)
        {
            if (oldDepth == newDepth) return;
            bool categoryChanged = SnowUtility.GetSnowCategory(oldDepth) != SnowUtility.GetSnowCategory(newDepth);
            if (categoryChanged)
            {
                map.pathGrid?.RecalculatePerceivedPathCostAt(c);
            }
            if (categoryChanged || VisualStep(oldDepth) != VisualStep(newDepth))
            {
                map.mapView.Notify_SnowChanged();
            }
        }

        private static int VisualStep(float depth) => (int)(depth * VisualSteps);

        // ---- Scribe ----

        /// <summary>
        /// The grid as RimWorld saves it (<c>MapExposeUtility.ExposeUshort</c>, "depthGrid"): each cell's
        /// depth quantised to a <c>ushort</c> (0..65535 over 0..1), little-endian, base64. An all-bare map
        /// writes nothing at all, so a map that never saw snow saves exactly as it did before snow existed.
        /// Called by <see cref="Map.Map.ExposeData"/>, which needs the depth back before it rebuilds the path
        /// grid; the encode/decode halves are split out for that reason.
        /// </summary>
        public string Encode()
        {
            if (totalDepth <= 0.0) return "";
            var bytes = new byte[depthGrid.Length * 2];
            for (int i = 0; i < depthGrid.Length; i++)
            {
                ushort v = SnowFloatToShort(depthGrid[i]);
                bytes[i * 2] = (byte)(v & 0xFF);
                bytes[i * 2 + 1] = (byte)(v >> 8);
            }
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Restores what <see cref="Encode"/> wrote. A string of the wrong length is a save from a
        /// map of a different size; it is reported and the grid left bare rather than half-filled.</summary>
        public void Decode(string? encoded)
        {
            Array.Clear(depthGrid, 0, depthGrid.Length);
            totalDepth = 0.0;
            map.mapView.Notify_SnowChanged();
            if (string.IsNullOrEmpty(encoded)) return;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                Scribe.loader.Error("SnowGrid: depthGrid is not valid base64.");
                return;
            }
            if (bytes.Length != depthGrid.Length * 2)
            {
                Scribe.loader.Error("SnowGrid: depthGrid holds " + bytes.Length / 2 + " cells; the map has " + depthGrid.Length + ".");
                return;
            }
            for (int i = 0; i < depthGrid.Length; i++)
            {
                float depth = SnowShortToFloat((ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8)));
                depthGrid[i] = depth;
                totalDepth += depth;
            }
        }

        private static ushort SnowFloatToShort(float depth) =>
            (ushort)Math.Round(GenMath.Clamp(depth, 0f, MaxDepth) * 65535f, MidpointRounding.AwayFromZero);

        private static float SnowShortToFloat(ushort depth) => depth / 65535f;
    }

    /// <summary>How a depth of snow reads, and what it costs to walk (RimWorld: <c>RimWorld.SnowCategory</c>).</summary>
    public enum SnowCategory
    {
        None,
        Dusting,
        Thin,
        Medium,
        Thick,
    }

    /// <summary>What a depth of snow means (RimWorld: <c>RimWorld.SnowUtility</c>).</summary>
    public static class SnowUtility
    {
        /// <summary>
        /// The band a depth falls in (RimWorld: <c>SnowUtility.GetSnowCategory</c>): under 0.03 none, under
        /// 0.25 a dusting, under 0.5 thin, under 0.75 medium, thick above. Recalled from the decompile, not
        /// sourced in this sandbox; the tests pin the ordering and that the bands cover 0..1.
        /// </summary>
        public static SnowCategory GetSnowCategory(float snowDepth)
        {
            if (snowDepth < 0.03f) return SnowCategory.None;
            if (snowDepth < 0.25f) return SnowCategory.Dusting;
            if (snowDepth < 0.5f) return SnowCategory.Thin;
            if (snowDepth < 0.75f) return SnowCategory.Medium;
            return SnowCategory.Thick;
        }

        /// <summary>
        /// Ticks added to crossing a cell in this much snow (RimWorld: <c>SnowUtility.MovementTicksAddOn</c>:
        /// none and a dusting 0, thin 4, medium 8, thick 12). Recalled, not sourced; it matches the wiki's
        /// walk-speed table on bare soil (13 ticks a cell becomes 17, 21 and 25). Tests pin "deeper snow is
        /// slower, a dusting is free", not the literals.
        /// </summary>
        public static int MovementTicksAddOn(SnowCategory category) => category switch
        {
            SnowCategory.Thin => 4,
            SnowCategory.Medium => 8,
            SnowCategory.Thick => 12,
            _ => 0,
        };
    }
}
