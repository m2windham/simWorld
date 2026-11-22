using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>Data half of <see cref="CompHeatPusher"/> (RimWorld: <c>Verse.CompProperties_HeatPusher</c>).</summary>
    public class CompProperties_HeatPusher : CompProperties
    {
        /// <summary>Heat energy pushed every <see cref="CompHeatPusher.HeatPushInterval"/> ticks: a room's
        /// temperature moves by this over its cell count (<see cref="RoomGroup.PushHeat"/>). Negative cools.</summary>
        public float heatPerSecond;

        /// <summary>No push once the ambient air is at or above this.</summary>
        public float heatPushMaxTemperature = 99999f;

        /// <summary>No push once the ambient air is at or below this.</summary>
        public float heatPushMinTemperature = -99999f;

        public CompProperties_HeatPusher()
        {
            compClass = typeof(CompHeatPusher);
        }
    }

    /// <summary>
    /// Pushes heat into the room it stands in while the air is inside its window (RimWorld:
    /// <c>Verse.CompHeatPusher</c>, ported line for line). It is the unpowered pusher a passive cooler or a
    /// campfire carries. The port's <see cref="CompHeatPusherPowered"/> is a different thing: the heater's own
    /// flat push toward a target, which RimWorld does with <c>CompTempControl</c>.
    /// </summary>
    public class CompHeatPusher : ThingComp
    {
        /// <summary>RimWorld: <c>CompHeatPusher.HeatPushInterval</c>.</summary>
        public const int HeatPushInterval = 60;

        /// <summary>RimWorld's <c>CompTickRare</c> multiplier: 250 ticks over 60.</summary>
        public const float RareTickFactor = 4.16666651f;

        public CompProperties_HeatPusher Props => (CompProperties_HeatPusher)props;

        protected virtual bool ShouldPushHeatNow
        {
            get
            {
                if (!parent.Spawned || parent.Map == null) return false;
                float ambient = RotUtility.AmbientTemperatureAt(parent.Map, parent.Position);
                return ambient < Props.heatPushMaxTemperature && ambient > Props.heatPushMinTemperature;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (HashInterval.IsHashIntervalTick(parent.thingIDNumber, HeatPushInterval) && ShouldPushHeatNow)
            {
                PushHeat(parent.Position, parent.Map!, Props.heatPerSecond);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (ShouldPushHeatNow)
            {
                PushHeat(parent.Position, parent.Map!, Props.heatPerSecond * RareTickFactor);
            }
        }

        /// <summary>
        /// Puts <paramref name="energy"/> into the room at <paramref name="c"/>, or, for a cell in no room (a
        /// wall), shares it out between the rooms of its eight neighbours (RimWorld:
        /// <c>GenTemperature.PushHeat(IntVec3, Map, float)</c>). False when no room took any.
        /// </summary>
        public static bool PushHeat(IntVec3 c, Map.Map map, float energy)
        {
            RoomGroup? group = map.roomTracker.RoomAt(c)?.Group;
            if (group != null) return group.PushHeat(energy);

            var neighbours = new System.Collections.Generic.List<RoomGroup>(8);
            for (int i = 0; i < 8; i++)
            {
                IntVec3 n = c + GenAdj.AdjacentCells[i];
                if (!GenGrid.InBounds(n, map)) continue;
                RoomGroup? g = map.roomTracker.RoomAt(n)?.Group;
                if (g != null) neighbours.Add(g);
            }
            float share = energy / neighbours.Count;
            for (int j = 0; j < neighbours.Count; j++) neighbours[j].PushHeat(share);
            return neighbours.Count > 0;
        }
    }

    /// <summary>
    /// A heat pusher that stops while its building has no power or no fuel (RimWorld:
    /// <c>Verse.CompHeatPusherPowered</c>, which is what the passive cooler's def names as its comp class).
    /// Renamed because this port's <see cref="CompHeatPusherPowered"/> already means the heater's flat push.
    /// The gate is RimWorld's less two parts the port has no system for: the flick switch
    /// (<c>FlickUtility.WantsToBeOn</c>) and breakdowns (<c>CompBreakdownable</c>).
    /// </summary>
    public class CompHeatPusherGated : CompHeatPusher
    {
        protected override bool ShouldPushHeatNow
        {
            get
            {
                if (!base.ShouldPushHeatNow) return false;
                // RimWorld's CompHeatPusherPowered asks FlickUtility.WantsToBeOn first.
                if (!FlickUtility.WantsToBeOn(parent)) return false;
                CompPowerTrader? power = parent.GetComp<CompPowerTrader>();
                if (power != null && !power.powerOn) return false;
                CompRefuelable? refuelable = parent.GetComp<CompRefuelable>();
                return refuelable == null || refuelable.HasFuel;
            }
        }
    }
}
