using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Stats;

namespace SimWorld.Weather
{
    /// <summary>The JobDef <see cref="WorkGiver_ClearSnow"/> issues (RimWorld: <c>JobDefOf.ClearSnow</c>), in its
    /// own <c>[DefOf]</c> class for the same reason <see cref="WeatherDefOf"/> is (CLAUDE.md).</summary>
    [DefOf]
    public static class SnowJobDefOf
    {
        public static JobDef ClearSnow = null!;
    }

    /// <summary>The stat <see cref="JobDriver_ClearSnow"/> counts work by (RimWorld:
    /// <c>StatDefOf.GeneralLaborSpeed</c>); ships in <c>Stats_GeneralLabor.xml</c>.</summary>
    [DefOf]
    public static class SnowStatDefOf
    {
        public static StatDef GeneralLaborSpeed = null!;
    }
}
