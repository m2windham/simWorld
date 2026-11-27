using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// What a room is like to be in (RimWorld: the room stats <c>Wealth</c>, <c>Beauty</c>, <c>Space</c>,
    /// <c>Cleanliness</c> and <c>Impressiveness</c>, each a <c>RoomStatWorker</c>). Impressiveness is the one
    /// the thoughts read: where a citizen slept, and where they ate.
    ///
    /// <para/><b>The problem it solves, shaped by the arc.</b> RimWorld's colonists expect more as the colony
    /// grows rich and are answered by what the colony builds for them: a good bedroom, a fine dining room. Here
    /// the era sets what people expect (<c>Thoughts.ThoughtWorker_Expectations</c>), and before this nothing
    /// answered it, so a town that rebuilt itself in stone and brick lived no better for it than its pit houses
    /// had. The house rungs (<c>Dwellings.xml</c>) are where the era shows: their walls, their size and what each
    /// one is furnished with (<see cref="DwellingPartKind.TableSpot"/>, <see cref="DwellingPartKind.ArtSpot"/>).
    ///
    /// <para/><b>Sources.</b> The formula and its constants are RimWorld's, <b>recalled, not read</b> from a
    /// decompile in this lane: wealth over 1500, beauty over 3, space over 125, each through
    /// <see cref="GetFactor"/>; their mean pulled 35% of the way to the worst of them; then pulled 75% of the way
    /// down to five times the space factor when it is above that (a cramped room is never impressive). Space
    /// counts 1.4 per standable cell and 0.5 per occupied one, capped at 350. A building's value is its
    /// <c>MarketValue</c> where content sets one, else what it cost (RimWorld derives a building's value the same
    /// way, <c>StatWorker_MarketValue.CalculatedBaseMarketValue</c>): its <c>costList</c>, its stuff and its work
    /// at 0.0036 a unit. Tests pin the order (a better room scores higher), never these literals.
    ///
    /// <para/><b>Trimmed.</b> Terrain adds nothing to wealth or beauty, as it adds nothing to beauty already
    /// (<see cref="BeautyUtility"/>'s remarks): this port ships no floors.
    /// </summary>
    public static class RoomStats
    {
        public const float WealthScale = 1500f;
        public const float BeautyScale = 3f;
        public const float SpaceScale = 125f;
        public const float CleanlinessScale = 2.5f;
        public const float WorstPull = 0.35f;
        public const float CrampedPull = 0.75f;
        public const float CrampedSpaceFactor = 5f;
        public const float SpacePerOpenCell = 1.4f;
        public const float SpacePerOccupiedCell = 0.5f;
        public const float MaxSpace = 350f;
        public const float ValuePerWork = 0.0036f;

        /// <summary>
        /// Lower bound of each impressiveness stage, worst first (RimWorld's <c>Impressiveness</c> score
        /// stages, recalled): awful, dull, mediocre, decent, slightly, somewhat, very, extremely, unbelievably
        /// and wondrously impressive. A thought with one stage per entry reads <see cref="StageOf"/> directly.
        /// </summary>
        public static readonly float[] ImpressivenessStages = { float.NegativeInfinity, 20f, 30f, 40f, 50f, 65f, 85f, 120f, 170f, 240f };

        /// <summary>The stage an ordinary room sits at: the one whose thought is neither good nor bad.</summary>
        public const int MediocreStage = 2;

        /// <summary>RimWorld's <c>RoomStatWorker_Impressiveness.GetFactor</c>: linear inside ±1, logarithmic
        /// beyond, so doubling a room's wealth matters less the richer it already is.</summary>
        public static float GetFactor(float baseFactor)
        {
            if (Math.Abs(baseFactor) < 1f) return baseFactor;
            return baseFactor > 0f ? 1f + (float)Math.Log(baseFactor) : -1f - (float)Math.Log(-baseFactor);
        }

        /// <summary>Whether <paramref name="room"/> is a room at all for these stats: enclosed and roofed. The
        /// outdoors, or a house whose roof is not on yet, has no impressiveness.</summary>
        public static bool IsEnclosed(Room? room) => room != null && !room.TouchesOutside && room.Cells.Count > 0;

        /// <summary>Value of every building inside the room and in its walls, each counted once.</summary>
        public static float Wealth(Room room, Map.Map map)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (map == null) throw new ArgumentNullException(nameof(map));
            var counted = new HashSet<Thing>();
            float total = 0f;
            IReadOnlyList<IntVec3> cells = room.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t.def.category == ThingCategory.Building && counted.Add(t)) total += BuildingValue(t);
                }
            }
            foreach (Thing edifice in room.boundaryEdifices)
            {
                if (counted.Add(edifice)) total += BuildingValue(edifice);
            }
            return total;
        }

        /// <summary>A building's value: its content <c>MarketValue</c>, else its cost in stuff, items and work.</summary>
        public static float BuildingValue(Thing building)
        {
            if (building == null) throw new ArgumentNullException(nameof(building));
            ThingDef def = building.def;
            float stated = def.GetStatValue(StatDefOf.MarketValue, building.Stuff);
            if (stated > 0f) return stated;

            float value = 0f;
            if (def.costList != null)
            {
                for (int i = 0; i < def.costList.Count; i++)
                {
                    ThingDefCountClass c = def.costList[i];
                    if (c.thingDef != null) value += c.thingDef.GetStatValue(StatDefOf.MarketValue) * c.count;
                }
            }
            if (building.Stuff != null && def.costStuffCount > 0)
            {
                value += building.Stuff.GetStatValue(StatDefOf.MarketValue) * def.costStuffCount;
            }
            value += def.GetStatValue(StatDefOf.WorkToBuild, building.Stuff) * ValuePerWork;
            return value;
        }

        /// <summary>How roomy the room is: open floor counts more than floor under furniture.</summary>
        public static float Space(Room room, Map.Map map)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            float space = 0f;
            IReadOnlyList<IntVec3> cells = room.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                space += GenGrid.Standable(cells[i], map) ? SpacePerOpenCell : SpacePerOccupiedCell;
            }
            return Math.Min(space, MaxSpace);
        }

        public static float Beauty(Room room, Map.Map map) => BeautyUtility.RoomBeauty(room, map);

        public static float Cleanliness(Room room, Map.Map map) => RoomCleanlinessUtility.CleanlinessOf(room, map);

        /// <summary>The room's impressiveness score (RimWorld: <c>RoomStatWorker_Impressiveness</c>).</summary>
        public static float Impressiveness(Room room, Map.Map map)
        {
            float wealth = GetFactor(Wealth(room, map) / WealthScale);
            float beauty = GetFactor(Beauty(room, map) / BeautyScale);
            float space = GetFactor(Space(room, map) / SpaceScale);
            float cleanliness = GetFactor(1f + Cleanliness(room, map) / CleanlinessScale);
            float mean = (wealth + beauty + space + cleanliness) / 4f;
            float worst = Math.Min(Math.Min(wealth, beauty), Math.Min(space, cleanliness));
            float score = mean + (worst - mean) * WorstPull;
            float cramped = space * CrampedSpaceFactor;
            if (score > cramped) score += (cramped - score) * CrampedPull;
            return score * 100f;
        }

        /// <summary>The stage index of <paramref name="score"/> in <see cref="ImpressivenessStages"/>.</summary>
        public static int StageOf(float score)
        {
            int stage = 0;
            for (int i = 1; i < ImpressivenessStages.Length; i++)
            {
                if (score >= ImpressivenessStages[i]) stage = i;
            }
            return stage;
        }
    }
}
