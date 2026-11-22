using System;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>RimWorld: <c>RimWorld.CompProperties_TempControl</c>, field for field.</summary>
    public class CompProperties_TempControl : CompProperties
    {
        public float energyPerSecond = 12f;

        public float defaultTargetTemperature = 21f;

        public float minTargetTemperature = -50f;

        public float maxTargetTemperature = 50f;

        public float lowPowerConsumptionFactor = 0.1f;

        public CompProperties_TempControl()
        {
            compClass = typeof(CompTempControl);
        }
    }

    /// <summary>The temperature a cooler works toward (RimWorld: <c>RimWorld.CompTempControl</c>, less its gizmos,
    /// which only move <see cref="targetTemperature"/>).</summary>
    public class CompTempControl : ThingComp
    {
        public float targetTemperature = -99999f;

        public bool operatingAtHighPower;

        public CompProperties_TempControl Props => (CompProperties_TempControl)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (targetTemperature < -2000f) targetTemperature = Props.defaultTargetTemperature;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref targetTemperature, "targetTemperature", 0f);
        }
    }

    /// <summary>
    /// RimWorld's electric cooler (<c>RimWorld.Building_Cooler</c>, read from the 1.0 decompile; shelter lane).
    /// Set into a wall, it cools the room on its south side toward its target and pushes a quarter more heat than
    /// it removes into the north side, losing efficiency as the hot side's lead over the cold grows. Runs only
    /// while powered and switched on. Not carried: <c>PowerOutput</c>'s low-power draw while idle (this port's
    /// <see cref="CompPowerTrader.PowerOutput"/> is derived, not set), breakdowns, the place worker.
    /// <para/>
    /// <b>No settlement builds one.</b> Nothing in this port has a settlement build power, so a cooler a settlement
    /// raised would never run; <see cref="SettlementClimate"/> builds passive coolers. The def and its research
    /// (AirConditioning, Industrial) stand ready for the lane that brings settlement power.
    /// </summary>
    public class Building_Cooler : Building
    {
        private const float HeatOutputMultiplier = 1.25f;

        private const float EfficiencyLossPerDegreeDifference = 0.0076923077f;

        /// <summary>RimWorld's <c>4.16666651f</c>: a TickRare's 250 ticks of a per-second figure at 60 ticks a
        /// second.</summary>
        private const float RareTickSeconds = 4.16666651f;

        public override void TickRare()
        {
            base.TickRare();
            CompPowerTrader? power = GetComp<CompPowerTrader>();
            CompTempControl? control = GetComp<CompTempControl>();
            Map.Map? map = Map;
            if (power == null || control == null || map == null || !power.powerOn || !FlickUtility.WantsToBeOn(this))
            {
                if (control != null) control.operatingAtHighPower = false;
                return;
            }

            // RimWorld: IntVec3.South / North rotated by the cooler's rotation; North rotated is the facing cell.
            IntVec3 facing = Rotation.FacingCell;
            IntVec3 cold = Position - facing;
            IntVec3 hot = Position + facing;
            bool working = false;
            if (GenGrid.InBounds(cold, map) && GenGrid.InBounds(hot, map)
                && !GenGrid.Impassable(hot, map) && !GenGrid.Impassable(cold, map))
            {
                float hotTemp = TemperatureAt(map, hot);
                float coldTemp = TemperatureAt(map, cold);
                float lead = hotTemp - coldTemp;
                if (hotTemp - 40f > lead) lead = hotTemp - 40f;
                float efficiency = Math.Max(0f, 1f - lead * EfficiencyLossPerDegreeDifference);
                float energy = control.Props.energyPerSecond * efficiency * RareTickSeconds;
                RoomGroup? coldGroup = map.roomTracker.RoomAt(cold)?.Group;
                float change = ControlTemperatureTempChange(coldGroup, energy, control.targetTemperature);
                working = Math.Abs(change) > 1e-5f;
                if (working)
                {
                    coldGroup!.temperature += change;
                    CompHeatPusher.PushHeat(hot, map, -energy * HeatOutputMultiplier);
                }
            }
            control.operatingAtHighPower = working;
        }

        private static float TemperatureAt(Map.Map map, IntVec3 c) => map.roomTracker.RoomAt(c)?.Group.Temperature ?? map.outdoorTemperature;

        /// <summary>RimWorld: <c>GenTemperature.ControlTemperatureTempChange</c>.</summary>
        public static float ControlTemperatureTempChange(RoomGroup? group, float energyLimit, float targetTemperature)
        {
            if (group == null || group.UsesOutdoorTemperature) return 0f;
            float b = energyLimit / group.CellCount;
            float a = targetTemperature - group.Temperature;
            return energyLimit > 0f ? Math.Max(Math.Min(a, b), 0f) : Math.Min(Math.Max(a, b), 0f);
        }
    }
}
