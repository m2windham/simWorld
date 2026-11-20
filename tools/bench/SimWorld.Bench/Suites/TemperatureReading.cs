using System.Collections.Generic;
using System.Globalization;

using SimWorld.AI;
using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Whether a run ever got cold or hot enough to hurt anybody: the watched map's outdoor extremes, the
    /// coldest and hottest air a citizen actually stood in, the worst hypothermia and heatstroke any of them
    /// reached, how many reached the serious stage, and how many times one was sent after safe air. Sampled
    /// hourly by <see cref="StorytellerSuite"/>; printed as one line after the dwellings line.
    /// </summary>
    internal sealed class TemperatureReading
    {
        public const int SampleTicks = 2500;

        private float outdoorMin = float.MaxValue, outdoorMax = float.MinValue;
        private float airMin = float.MaxValue, airMax = float.MinValue;
        private float worstCold, worstHeat;
        private readonly HashSet<int> seriousCold = new HashSet<int>();
        private readonly HashSet<int> seriousHeat = new HashSet<int>();
        private readonly HashSet<int> seekingSeen = new HashSet<int>();
        private int seekJobs;

        public void Sample(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            if (map.outdoorTemperature < outdoorMin) outdoorMin = map.outdoorTemperature;
            if (map.outdoorTemperature > outdoorMax) outdoorMax = map.outdoorTemperature;
            foreach (Pawn pawn in settlement.Citizens)
            {
                if (pawn.Dead || !pawn.Spawned || pawn.Map != map) continue;
                float air = TemperatureInjuryUtility.AmbientTemperature(pawn);
                if (air < airMin) airMin = air;
                if (air > airMax) airMax = air;
                Hediff? cold = pawn.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia);
                Hediff? heat = pawn.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Heatstroke);
                if (cold != null)
                {
                    if (cold.Severity > worstCold) worstCold = cold.Severity;
                    if (cold.CurStageIndex >= (int)TemperatureInjuryStage.Serious) seriousCold.Add(pawn.thingIDNumber);
                }
                if (heat != null)
                {
                    if (heat.Severity > worstHeat) worstHeat = heat.Severity;
                    if (heat.CurStageIndex >= (int)TemperatureInjuryStage.Serious) seriousHeat.Add(pawn.thingIDNumber);
                }
                JobDef? job = pawn.jobs?.curJob?.def;
                bool seeking = job == SafeTemperatureJobDefOf.GotoSafeTemperature || job == SafeTemperatureJobDefOf.Wait_SafeTemperature;
                if (seeking && seekingSeen.Add(pawn.thingIDNumber)) seekJobs++;
                if (!seeking) seekingSeen.Remove(pawn.thingIDNumber);
            }
        }

        public string Line()
        {
            if (outdoorMin == float.MaxValue) return "temperature: (no watched map)";
            return "temperature: outdoor " + F(outdoorMin) + ".." + F(outdoorMax) + " C; citizens stood in "
                + F(airMin) + ".." + F(airMax) + " C; worst hypothermia " + F2(worstCold) + ", heatstroke " + F2(worstHeat)
                + "; serious: " + seriousCold.Count + " cold, " + seriousHeat.Count + " heat; safe-temperature episodes " + seekJobs;
        }

        private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
