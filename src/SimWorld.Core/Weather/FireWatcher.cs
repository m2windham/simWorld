using System;
using System.Collections.Generic;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Weather
{
    /// <summary>
    /// Watches how much of a map is burning, so that a big enough fire brings the rain (RimWorld:
    /// <c>RimWorld.FireWatcher</c>, read by <c>WeatherDecider</c>). With rain taking something off every
    /// unroofed fire each interval (<see cref="Fire.TryExtinguishFromRain"/>) this is the map-scale remedy
    /// for a wildfire: nobody has to reach it.
    ///
    /// <para/><b>Sourced</b> (rimworldwiki.com/wiki/Weather, "Fire"): "The firewatcher process checks every
    /// 426 ticks (7.1 secs) for fires on the map", "it sets the variable firedanger to 0.5 + the size of the
    /// fire (usually 0.1) for every fire on the map", "A large fire is declared once firedanger reaches 90".
    /// What a large fire then does to the weather is in <see cref="WeatherDecider"/>.
    ///
    /// <para/><b>Where it lives.</b> RimWorld hangs it off the map (<c>map.fireWatcher</c>). Here the
    /// weather manager owns it, because the weather decider is its only reader and <c>Map/Map.cs</c> is a
    /// file every lane edits. Nothing is saved, as in RimWorld: the danger is re-read on first use after a
    /// load.
    /// </summary>
    public sealed class FireWatcher
    {
        /// <summary>RimWorld: <c>UpdateObservationsInterval</c>.</summary>
        public const int UpdateObservationsInterval = 426;

        /// <summary>RimWorld: <c>BaseDangerPerFire</c>.</summary>
        public const float BaseDangerPerFire = 0.5f;

        /// <summary>The danger above which a fire is "large". With new fires at size 0.1 that is 150 of
        /// them; fully grown ones (1.75) get there at 41.</summary>
        public const float LargeFireDanger = 90f;

        private readonly Map.Map map;

        private float fireDanger = -1f;

        public FireWatcher(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public float FireDanger => fireDanger;

        public bool LargeFireDangerPresent
        {
            get
            {
                if (fireDanger < 0f) UpdateObservations();
                return fireDanger > LargeFireDanger;
            }
        }

        public void FireWatcherTick()
        {
            if (Find.TickManager.TicksGame % UpdateObservationsInterval == 0) UpdateObservations();
        }

        public void UpdateObservations()
        {
            fireDanger = 0f;
            IReadOnlyList<Thing> fires = FireUtility.AllFires(map);
            for (int i = 0; i < fires.Count; i++)
            {
                if (fires[i] is Fire fire) fireDanger += BaseDangerPerFire + fire.fireSize;
            }
        }
    }
}
