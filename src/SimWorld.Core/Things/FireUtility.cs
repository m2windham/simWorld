using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;

namespace SimWorld.Things
{
    /// <summary>
    /// Starting fires, finding them, and asking whether something is on fire (RimWorld:
    /// <c>RimWorld.FireUtility</c>). Every ignition in this port goes through
    /// <see cref="TryStartFireIn"/> or <see cref="TryAttachFire"/>, so the "one fire per cell" and "only
    /// flammable things burn" invariants hold wherever a fire comes from.
    /// </summary>
    public static class FireUtility
    {
        /// <summary>Below this a Thing is treated as not flammable at all (RimWorld: <c>Thing.FlammableNow</c>'s
        /// own 0.01 floor), so floating-point dust never counts as fuel.</summary>
        public const float MinFlammability = 0.01f;

        /// <summary>Fire size a fresh ignition starts at (RimWorld: <c>DamageWorker_Flame</c>'s own
        /// <c>Rand.Range(0.15f, 0.25f)</c> for a thing set alight by flame damage).</summary>
        public static readonly FloatRange FireSizeOnIgnite = new FloatRange(0.15f, 0.25f);

        /// <summary>
        /// How readily <paramref name="thing"/> burns right now, through the full stat pipeline so that the
        /// material it was built from counts (RimWorld: <c>thing.GetStatValue(StatDefOf.Flammability)</c>).
        /// </summary>
        public static float FlammabilityOf(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            StatDef? stat = FireStatDefOf.Flammability;
            return stat == null ? 0f : thing.GetStatValue(stat);
        }

        /// <summary>True when this Thing could catch fire at all (RimWorld: <c>Thing.FlammableNow</c>).</summary>
        public static bool FlammableNow(this Thing thing) =>
            thing != null && !thing.Destroyed && FlammabilityOf(thing) >= MinFlammability;

        /// <summary>Every fire currently on <paramref name="map"/>. <see cref="Map.ListerThings"/> already
        /// indexes Things by def, so this needs no registry of its own.</summary>
        public static IReadOnlyList<Thing> AllFires(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            ThingDef? fireDef = FireThingDefOf.Fire;
            return fireDef == null ? Array.Empty<Thing>() : map.listerThings.ThingsOfDef(fireDef);
        }

        /// <summary>The free-standing fire sitting on <paramref name="c"/>, if any.</summary>
        public static Fire? StaticFireIn(IntVec3 c, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(c, map)) return null;
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Fire fire && fire.parent == null) return fire;
            }
            return null;
        }

        /// <summary>The fire riding <paramref name="thing"/>, if it is alight (RimWorld:
        /// <c>Thing.GetAttachment(ThingDefOf.Fire)</c> via <c>CompAttachBase</c>; see
        /// <see cref="AttachableThing"/> for why this searches the cell instead).</summary>
        public static Fire? GetAttachedFire(this Thing thing)
        {
            if (thing == null || !thing.Spawned) return null;
            IReadOnlyList<Thing> here = thing.Map!.thingGrid.ThingsListAt(thing.Position);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Fire fire && ReferenceEquals(fire.parent, thing)) return fire;
            }
            return null;
        }

        /// <summary>
        /// Whether <paramref name="thing"/> is on fire — the guard RimWorld's own work givers use
        /// (<c>t.IsBurning()</c>) to refuse to walk into a burning building. A pawn counts only when a fire
        /// is actually riding them; anything else counts when a free-standing fire stands in any cell of its
        /// footprint (RimWorld: <c>FireUtility.IsBurning</c>'s own Pawn/Thing split).
        /// </summary>
        public static bool IsBurning(this Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned) return false;
            if (thing.GetAttachedFire() != null) return true;
            if (thing is Pawn) return false;

            Map.Map map = thing.Map!;
            foreach (IntVec3 c in thing.OccupiedRect().Cells)
            {
                if (StaticFireIn(c, map) != null) return true;
            }
            return false;
        }

        /// <summary>
        /// Chance a spark landing on <paramref name="c"/> catches (RimWorld:
        /// <c>FireUtility.ChanceToStartFireIn</c>): the flammability of the most flammable thing standing
        /// there, 0 on water and 0 where a fire already burns. Bare ground holds nothing flammable and so
        /// returns 0 — that, not a special case, is what stops fire crossing an empty field.
        /// </summary>
        public static float ChanceToStartFireIn(IntVec3 c, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(c, map)) return 0f;
            if (GenGrid.GetTerrain(c, map).IsWater) return 0f;

            float chance = 0f;
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Fire) return 0f;
                float flammability = FlammabilityOf(here[i]);
                if (flammability > chance) chance = flammability;
            }
            return chance < MinFlammability ? 0f : chance;
        }

        /// <summary>
        /// Starts a free-standing fire on <paramref name="c"/> if anything there can burn and nothing there
        /// is burning already (RimWorld: <c>FireUtility.TryStartFireIn</c>).
        /// </summary>
        /// <returns>True if a fire was spawned.</returns>
        public static bool TryStartFireIn(IntVec3 c, Map.Map map, float fireSize)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (ChanceToStartFireIn(c, map) <= 0f) return false;
            if (FireThingDefOf.Fire == null) return false;

            var fire = (Fire)ThingMaker.MakeThing(FireThingDefOf.Fire);
            fire.fireSize = Math.Max(Fire.MinFireSize, fireSize);
            GenSpawn.Spawn(fire, c, map);
            return true;
        }

        /// <summary>Whether a fire could ever ride <paramref name="thing"/> (RimWorld:
        /// <c>Thing.CanEverAttachFire</c>).</summary>
        public static bool CanEverAttachFire(this Thing thing) =>
            thing != null && !thing.Destroyed && thing.Spawned && thing.FlammableNow();

        /// <summary>
        /// Sets <paramref name="thing"/> itself alight, so the fire travels with it (RimWorld:
        /// <c>Thing.TryAttachFire</c>). A thing already burning is left alone; a burning pawn drops whatever
        /// it was doing, exactly as RimWorld's does.
        /// </summary>
        /// <returns>True if a new fire was attached.</returns>
        public static bool TryAttachFire(this Thing thing, float fireSize)
        {
            if (!thing.CanEverAttachFire()) return false;
            if (thing.GetAttachedFire() != null) return false;
            if (FireThingDefOf.Fire == null) return false;

            var fire = (Fire)ThingMaker.MakeThing(FireThingDefOf.Fire);
            fire.fireSize = Math.Max(Fire.MinFireSize, fireSize);
            fire.AttachTo(thing);
            GenSpawn.Spawn(fire, thing.Position, thing.Map!);

            if (thing is Pawn pawn && pawn.jobs?.curJob != null)
            {
                pawn.jobs.EndCurrentJob(AI.JobCondition.InterruptForced, startNewJob: false);
            }
            return true;
        }

        /// <summary>
        /// Rolls every unroofed fire on <paramref name="map"/> against the rain (RimWorld: the rain branch of
        /// <c>Fire.DoComplexCalcs</c>). Called by <c>Weather.WeatherManager.WeatherManagerTick</c> once per
        /// <see cref="Fire.ComplexCalcsInterval"/> while <c>RainRate</c> is above zero — see
        /// <see cref="Fire.TryExtinguishFromRain"/>.
        /// </summary>
        /// <returns>How many fires the rain put out.</returns>
        public static int ExtinguishFiresFromRain(Map.Map map, float rainRate)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            // Copy first: extinguishing de-spawns, which mutates the very list AllFires hands back.
            var fires = new List<Thing>(AllFires(map));
            int extinguished = 0;
            for (int i = 0; i < fires.Count; i++)
            {
                if (fires[i] is Fire fire && fire.TryExtinguishFromRain(rainRate)) extinguished++;
            }
            return extinguished;
        }
    }
}
