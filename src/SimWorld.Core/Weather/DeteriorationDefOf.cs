using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Stats;

namespace SimWorld.Weather
{
    /// <summary>What <see cref="SteadyEnvironmentEffects"/> reads and deals to wear things left outside down
    /// (RimWorld: <c>StatDefOf.DeteriorationRate</c>, <c>DamageDefOf.Deterioration</c>), in their own
    /// <c>[DefOf]</c> class for the same reason <see cref="SnowStatDefOf"/> is (CLAUDE.md).</summary>
    [DefOf]
    public static class DeteriorationDefOf
    {
        /// <summary>Hit points lost per day outdoors; ships in <c>Stats_Deterioration.xml</c>.</summary>
        public static StatDef DeteriorationRate = null!;

        /// <summary>Ships in <c>Damages_Deterioration.xml</c>.</summary>
        public static DamageDef Deterioration = null!;
    }
}
