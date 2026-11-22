using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Weather;
using SimWorld.World;

namespace SimWorld.Conditions
{
    /// <summary>
    /// A temperature condition that only fits some of the year (RimWorld: <c>RimWorld.IncidentWorker_HeatWave</c>
    /// and <c>IncidentWorker_ColdSnap</c>, read from the 1.0 decompile: each adds one test of
    /// <c>map.mapTemperature.SeasonalTemp</c> to <see cref="IncidentWorker_MakeGameCondition"/>). Without it a cold
    /// snap could break out in midsummer and a heat wave in the dead of winter.
    /// <para/>
    /// <b>Whose season.</b> RimWorld asks the map the incident targets. Here the target is the civilization and the
    /// condition lies over all of it (<see cref="GameConditionManager"/>), so it is asked of the seat
    /// (<see cref="CivilizationTarget.Seat"/>). A civilization with no settlement has no season to ask about, and
    /// the base answer stands. As in RimWorld the test is of whether the storyteller may choose the incident;
    /// <c>TryExecute</c> does not repeat it.
    /// </summary>
    public abstract class IncidentWorker_SeasonalTemperature : IncidentWorker_MakeGameCondition
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms)) return false;
            Settlement? seat = (parms.target as CivilizationTarget)?.Seat;
            if (seat == null) return true;
            MapClimate? climate = MapClimate.ForTile(Find.World!, seat.tile);
            return climate == null || SeasonAllows(GenTemperature.SeasonalTemperatureAt(climate, Find.TickManager.TicksAbs));
        }

        protected abstract bool SeasonAllows(float seasonalTemperature);
    }

    /// <summary>RimWorld: <c>RimWorld.IncidentWorker_HeatWave</c>.</summary>
    public sealed class IncidentWorker_HeatWave : IncidentWorker_SeasonalTemperature
    {
        protected override bool SeasonAllows(float seasonalTemperature) => seasonalTemperature >= 20f;
    }

    /// <summary>RimWorld: <c>RimWorld.IncidentWorker_ColdSnap</c>.</summary>
    public sealed class IncidentWorker_ColdSnap : IncidentWorker_SeasonalTemperature
    {
        protected override bool SeasonAllows(float seasonalTemperature) => seasonalTemperature > 0f && seasonalTemperature < 15f;
    }
}
