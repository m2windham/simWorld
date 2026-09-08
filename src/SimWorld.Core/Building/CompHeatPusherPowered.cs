using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>Data half of <see cref="CompHeatPusherPowered"/> (RimWorld: <c>RimWorld.CompProperties_HeatPusherPowered</c>).</summary>
    public class CompProperties_HeatPusherPowered : CompProperties
    {
        /// <summary>
        /// Degrees pushed per tick while powered and short of <see cref="targetTemperature"/> (RimWorld's
        /// real heater instead has a wattage-derived push and a max-temperature cutoff; not sourced here —
        /// this port's own flat per-tick push, pinned by a test on the trend, not the literal). Positive
        /// heats a room up toward the target; negative (a cooler) pulls it down toward the target — the
        /// same signed-field idiom <see cref="CompProperties_PowerTrader.basePowerConsumption"/> uses.
        /// </summary>
        public float heatPerTick = 0.5f;

        public float targetTemperature = 23f;

        public CompProperties_HeatPusherPowered()
        {
            compClass = typeof(CompHeatPusherPowered);
        }
    }

    /// <summary>
    /// Pushes its room's temperature toward a target while powered (RimWorld: <c>RimWorld.CompHeatPusherPowered</c>).
    /// A sibling <see cref="CompPowerTrader"/> comp gates it — two independent comps on the same Def, matching
    /// RimWorld's own composition rather than one merged "powered heater" class.
    /// </summary>
    public class CompHeatPusherPowered : Things.ThingComp
    {
        public CompProperties_HeatPusherPowered Properties => (CompProperties_HeatPusherPowered)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map?.roomTracker.RegisterHeatPusher(this);
        }

        public override void PostDeSpawn(Map.Map map)
        {
            map.roomTracker.DeregisterHeatPusher(this);
            base.PostDeSpawn(map);
        }

        /// <summary>Called once per tick by <see cref="RoomTracker.RoomTrackerTick"/>.</summary>
        public void PushHeat(RoomTracker tracker)
        {
            CompPowerTrader? power = parent.GetComp<CompPowerTrader>();
            if (power != null && !power.powerOn) return;

            Room? room = tracker.RoomAt(parent.Position);
            if (room == null || room.TouchesOutside) return;

            RoomGroup group = room.Group;
            float target = Properties.targetTemperature;
            float perTick = Properties.heatPerTick;

            if (perTick > 0f && group.temperature < target)
            {
                group.temperature = System.Math.Min(target, group.temperature + perTick);
            }
            else if (perTick < 0f && group.temperature > target)
            {
                group.temperature = System.Math.Max(target, group.temperature + perTick);
            }
        }
    }
}
