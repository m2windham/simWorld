using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// What a pawn notices lying about as it goes through its day (RimWorld: <c>RimWorld.PawnObserver</c>,
    /// ported 1:1 apart from the two draws noted below). Every <see cref="IntervalsBetweenObservations"/>
    /// need-intervals (4, give or take one, so a settlement's people do not all look on the same tick) a
    /// spawned pawn that can see samples the <see cref="SampleNumCells"/> cells nearest it, and from each cell
    /// it has a clear line of sight to it asks every Thing there whether it has a thought to give
    /// (<see cref="IThoughtGiver"/>). The first thing that does is a human body lying in the open.
    /// <para/>
    /// <b>Where it runs, and what it costs.</b> From <see cref="Needs.Need_Mood.NeedInterval"/>, which RimWorld
    /// calls it from too: once per 150-tick need interval of a Full-tier pawn, and a look every fourth of
    /// those, so each pawn looks about once per 600 ticks. A look is a hundred Bresenham walks over a radius
    /// of about five cells, and a list read per visible cell. There is no per-tick scan and nothing is looked
    /// at by a pawn that is not spawned (an Interval- or Statistical-tier citizen has no surroundings to see:
    /// their mood advances in bulk and never reaches this, by design — spec §11.3).
    /// <para/>
    /// <b>Deviation, recorded: the two draws are seeded per pawn, not taken from the shared stream.</b>
    /// RimWorld draws <c>Rand.Range(0, 4)</c> in the constructor and <c>Rand.RangeInclusive(-1, 1)</c> at every
    /// look. Both exist only to put the pawns out of step with each other. Taken from the ambient stream they
    /// would also move every other system's draws, by one per pawn built and one per look, in a game whose
    /// every seeded outcome is already pinned; a draw seeded by (pawn id, tick) puts the pawns out of step just
    /// as well and perturbs nothing else.
    /// </summary>
    public sealed class PawnObserver
    {
        public const int IntervalsBetweenObservations = 4;

        /// <summary>How many of the cells nearest the pawn it looks at (RimWorld: <c>SampleNumCells</c>, 100).</summary>
        public const int SampleNumCells = 100;

        private readonly Pawn pawn;
        private int intervalsUntilObserve;

        public PawnObserver(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            intervalsUntilObserve = Rand.RangeSeeded(0, 4, pawn.thingIDNumber);
        }

        public void ObserverInterval()
        {
            if (!pawn.Spawned) return;
            intervalsUntilObserve--;
            if (intervalsUntilObserve <= 0)
            {
                ObserveSurroundingThings();
                int now = Find.TickManager?.TicksGame ?? 0;
                intervalsUntilObserve = IntervalsBetweenObservations + Rand.RangeSeeded(-1, 2, unchecked(pawn.thingIDNumber * 7919 + now));
            }
        }

        /// <summary>Public so a test can make a pawn look now rather than wait out the interval.</summary>
        public void ObserveSurroundingThings()
        {
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Sight)) return;
            Map.Map? map = pawn.Map;
            MemoryThoughtHandler? memories = pawn.needs.mood?.thoughts.memories;
            if (map == null || memories == null) return;

            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < SampleNumCells; i++)
            {
                IntVec3 cell = pawn.Position + pattern[i];
                if (!GenGrid.InBounds(cell, map)) continue;
                if (!GenSight.LineOfSight(cell, pawn.Position, map, skipFirstCell: true)) continue;

                IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int t = 0; t < things.Count; t++)
                {
                    if (!(things[t] is IThoughtGiver giver)) continue;
                    Thought_Memory? thought = giver.GiveObservedThought();
                    if (thought != null) memories.TryGainMemory(thought);
                }
            }
        }
    }
}
