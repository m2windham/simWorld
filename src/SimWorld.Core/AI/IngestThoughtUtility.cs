using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Thoughts;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>The memories a meal leaves (comfort lane; content in <c>Thoughts_Comfort.xml</c> and
    /// <c>Thoughts_Memories.xml</c>).</summary>
    [DefOf]
    public static class IngestThoughtDefOf
    {
        public static ThoughtDef AteRawFood = null!;
        public static ThoughtDef AteFineMeal = null!;
        public static ThoughtDef AteLavishMeal = null!;
        public static ThoughtDef AteWithoutTable = null!;
        public static ThoughtDef AteInImpressiveDiningRoom = null!;
    }

    /// <summary>
    /// What eating does to mood, and how that steers what a citizen eats (RimWorld: <c>FoodUtility.ThoughtsFromIngesting</c>,
    /// <c>FoodUtility.FoodOptimality</c> and <c>Toils_Ingest.CarryIngestibleToChewSpot</c>; all <b>recalled, not read</b>
    /// from a decompile in this lane).
    ///
    /// <para/><b>The gap.</b> <c>AteRawFood</c>, <c>AteFineMeal</c> and <c>AteWithoutTable</c> shipped in content with no
    /// caller anywhere in <c>src/</c>, and a hungry citizen ate whatever food lay nearest, so a hearty meal was never
    /// sought out and never felt. The settlement's cooks made better meals as the eras turned and nobody noticed.
    ///
    /// <para/><b>Ported.</b> A raw foodstuff (<see cref="FoodPreferability.RawBad"/>) leaves "ate it raw", a hearty
    /// meal and a feast platter their good memories. A meal eaten with no table leaves "no table to eat at", and one
    /// eaten at a table in a room better than ordinary leaves a memory of that room, staged by its impressiveness
    /// (<see cref="RoomStats"/>). Food is chosen by RimWorld's optimality: 300, less the distance, plus what the meal
    /// will do to mood through <see cref="OptimalityFromMood"/>, so a citizen walks further for a better meal and
    /// eats raw food when nothing else is near.
    ///
    /// <para/><b>Shaped by the arc.</b> RimWorld's colonists miss a table from the first day. Here nobody misses a
    /// table who has never had one: "no table to eat at" applies once the town knows how to make one
    /// (<see cref="FurnitureThingDefOf.DiningTable"/>'s research), which is when its houses start to have them. Only a
    /// meal wants a table; a handful of berries or a strip of trail cake does not.
    /// </summary>
    public static class IngestThoughtUtility
    {
        /// <summary>How far a citizen carries a meal to a table (RimWorld's chew-spot search, recalled).</summary>
        public const float TableSearchRadius = 32f;

        /// <summary>RimWorld's base food optimality, before distance and mood (recalled).</summary>
        public const float BaseOptimality = 300f;

        /// <summary>RimWorld's <c>FoodOptimalityEffectFromMoodCurve</c>, recalled: a meal's mood worth turned into
        /// how much further a citizen will walk for it.</summary>
        public static readonly SimpleCurve OptimalityFromMood = new SimpleCurve
        {
            new CurvePoint(-100f, -600f),
            new CurvePoint(-10f, -100f),
            new CurvePoint(-5f, -70f),
            new CurvePoint(-1f, -50f),
            new CurvePoint(0f, 0f),
            new CurvePoint(100f, 800f),
        };

        /// <summary>The food memory <paramref name="food"/> leaves, table and room aside, or null. One at most: a
        /// raw foodstuff, a hearty meal or a feast platter.</summary>
        public static ThoughtDef? FoodThought(ThingDef food)
        {
            switch (food?.ingestible?.preferability)
            {
                case FoodPreferability.RawBad: return IngestThoughtDefOf.AteRawFood;
                case FoodPreferability.MealFine: return IngestThoughtDefOf.AteFineMeal;
                case FoodPreferability.MealLavish: return IngestThoughtDefOf.AteLavishMeal;
                default: return null;
            }
        }

        /// <summary>The mood <paramref name="pawn"/> would get from eating <paramref name="food"/>, its own traits
        /// considered.</summary>
        public static float MoodFromEating(Pawn pawn, ThingDef food)
        {
            if (pawn.needs?.mood == null) return 0f;
            ThoughtDef? t = FoodThought(food);
            return t == null || ThoughtUtility.ThoughtNullified(pawn, t) ? 0f : t.stages[0].baseMoodEffect;
        }

        /// <summary>RimWorld's food optimality: base, less distance, plus the mood worth of the meal.</summary>
        public static float FoodOptimality(Pawn pawn, Thing food, float distance) =>
            BaseOptimality - distance + OptimalityFromMood.Evaluate(MoodFromEating(pawn, food.def));

        /// <summary>Whether a meal of <paramref name="food"/> is better at a table: any meal, once the town knows
        /// what a table is.</summary>
        public static bool TableDesired(ThingDef food) =>
            food.IsMeal && FurnitureThingDefOf.DiningTable.IsResearchFinished;

        /// <summary>The nearest table <paramref name="pawn"/> can reach within <see cref="TableSearchRadius"/> of
        /// <paramref name="from"/>, or null. Only a citizen with a mood looks for one.</summary>
        public static Thing? TableFor(Pawn pawn, IntVec3 from)
        {
            Map.Map? map = pawn.Map;
            if (map == null || pawn.needs?.mood == null || !pawn.RaceProps.Humanlike) return null;
            IReadOnlyList<Thing> tables = map.listerThings.ThingsOfDef(FurnitureThingDefOf.DiningTable);
            Thing? best = null;
            float bestDistSq = TableSearchRadius * TableSearchRadius;
            for (int i = 0; i < tables.Count; i++)
            {
                Thing t = tables[i];
                if (!t.Spawned) continue;
                float distSq = (t.Position - from).LengthHorizontalSquared;
                if (distSq > bestDistSq) continue;
                if (!Reachability.CanReach(pawn, t, PathEndMode.Touch)) continue;
                best = t;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>Every memory a meal leaves (RimWorld: <c>FoodUtility.ThoughtsFromIngesting</c>), given the table
        /// it was eaten at, if any.</summary>
        public static void ApplyThoughts(Pawn pawn, ThingDef food, Thing? table)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            MemoryThoughtHandler? memories = pawn.needs?.mood?.thoughts.memories;
            if (memories == null || food == null) return;

            ThoughtDef? eaten = FoodThought(food);
            if (eaten != null) memories.TryGainMemory(eaten);

            if (table == null)
            {
                if (TableDesired(food)) memories.TryGainMemory(IngestThoughtDefOf.AteWithoutTable);
                return;
            }

            Map.Map? map = table.Map;
            Room? room = map?.roomTracker.RoomAt(table.Position);
            if (map == null || !RoomStats.IsEnclosed(room)) return;
            int stage = RoomStats.StageOf(RoomStats.Impressiveness(room!, map));
            if (stage <= RoomStats.MediocreStage) return;
            memories.RemoveMemoriesOfDef(IngestThoughtDefOf.AteInImpressiveDiningRoom);
            var memory = (Thought_Memory)Activator.CreateInstance(IngestThoughtDefOf.AteInImpressiveDiningRoom.ThoughtClass)!;
            memory.def = IngestThoughtDefOf.AteInImpressiveDiningRoom;
            memory.forcedStage = Math.Min(stage - RoomStats.MediocreStage - 1, memory.def.stages.Count - 1);
            memories.TryGainMemory(memory);
        }
    }
}
