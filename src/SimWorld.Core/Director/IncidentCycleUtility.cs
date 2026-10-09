using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// On/off cycle math shared by cycle-shaped storyteller comps (RimWorld: <c>RimWorld.IncidentCycleUtility</c>),
    /// ported as RimWorld writes it.
    ///
    /// <para/><b>What this replaced, and why it matters.</b> The port's first version rolled a mean-time-between
    /// check on every interval inside the on-phase, tuned so that "roughly" <c>numIncidentsRange</c> incidents
    /// landed per cycle. That is a different animal. A Poisson count has no floor and no ceiling (a cycle could
    /// hand out four threats or none), <c>minSpacingDays</c> was only a floor on the mean and bound nothing,
    /// and the cycle's rhythm — which is the whole of what makes Cassandra Cassandra and Phoebe Phoebe — was
    /// a statement about averages. RimWorld's is a plan: for each cycle, seeded from the world, the target and
    /// the comp, it decides how many incidents the on-phase holds (<c>RoundRandom</c> of a draw from the
    /// range), puts each at a random interval inside the on-phase, then pushes them apart until none is closer
    /// than <c>minSpacingDays</c> to the one before it — across the cycle boundary too, so the last hit of one
    /// on-phase and the first of the next also keep their distance. A threat's day is therefore decided
    /// before the game reaches it, identically after a save and a load, and a player who has read the wiki can
    /// know it.
    ///
    /// <para/>Each call replays the plan from the first cycle to the current one, as RimWorld does: the previous
    /// cycle's last hit is the next one's <c>fixedHit</c>, so the plan cannot be evaluated one cycle in
    /// isolation. The cost is one small seeded draw per cycle elapsed, per comp, per storyteller interval.
    /// </summary>
    public static class IncidentCycleUtility
    {
        /// <summary>RimWorld's retry cap when relaxing a cycle's hits to satisfy the spacing fails.</summary>
        private const int MaxRelaxAttempts = 100;

        /// <summary>
        /// How many incidents a cycle-shaped comp fires in the interval the clock is in right now.
        /// </summary>
        /// <param name="randSeedSalt">The comp's index in the storyteller's comp list (RimWorld: <c>storytellerComps.IndexOf</c>),
        /// so two cycle comps on one storyteller do not share a plan.</param>
        /// <param name="minDaysPassed">The first on-phase begins here.</param>
        /// <param name="minSpacingDays">No two hits within one plan are closer than this, boundary included.</param>
        /// <param name="acceptFraction">Below 1, each cycle's plan is thinned to that fraction of its hits.</param>
        public static int IncidentCountThisInterval(
            IIncidentTarget target, int randSeedSalt, float minDaysPassed, float onDays, float offDays,
            float minSpacingDays, float minIncidents, float maxIncidents, float acceptFraction = 1f)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            int sinceStart = QueueIntervalsPassed - DaysToIntervals(minDaysPassed);
            if (sinceStart < 0) return 0;

            IReadOnlyList<int> hits = PlannedHitsOfCycleContaining(
                target, randSeedSalt, onDays, offDays, minSpacingDays, minIncidents, maxIncidents, acceptFraction, sinceStart);

            int count = 0;
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i] == sinceStart) count++;
            }
            return count;
        }

        /// <summary>
        /// The intervals, counted from the cycle's start day (<c>minDaysPassed</c>), at which this comp fires
        /// in the cycle that contains <paramref name="intervalSinceStart"/> — the plan itself rather than a
        /// sample of it. <see cref="IncidentCountThisInterval"/> is this, asked one interval at a time; a test
        /// or a tool that wants the schedule reads it here. Replays every earlier cycle first, because each
        /// cycle's plan keeps its distance from the last hit of the one before it.
        /// </summary>
        public static IReadOnlyList<int> PlannedHitsOfCycleContaining(
            IIncidentTarget target, int randSeedSalt, float onDays, float offDays, float minSpacingDays,
            float minIncidents, float maxIncidents, float acceptFraction, int intervalSinceStart)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            int onIntervals = DaysToIntervals(onDays);
            int cycleIntervals = onIntervals + DaysToIntervals(offDays);
            if (onIntervals <= 0 || cycleIntervals <= 0 || intervalSinceStart < 0) return Array.Empty<int>();
            int minSpacingIntervals = DaysToIntervals(minSpacingDays);

            int worldSeed = Find.World?.info?.seedString is string seedString
                ? World.GenText.StableStringHash(seedString)
                : 0;

            int lastCycle = intervalSinceStart / cycleIntervals;
            int fixedHit = NoPreviousHit;
            var hits = new List<int>();
            for (int cycle = 0; cycle <= lastCycle; cycle++)
            {
                int seed = MurmurHash.Combine(MurmurHash.Combine(worldSeed, target.ConstantRandSeed, randSeedSalt), cycle);
                if (hits.Count > 0) fixedHit = hits[hits.Count - 1];
                hits.Clear();
                GenerateHitList(hits, seed, cycle * cycleIntervals, onIntervals, minIncidents, maxIncidents, minSpacingIntervals, acceptFraction, fixedHit);
            }
            return hits;
        }

        /// <summary>RimWorld's sentinel: no earlier hit exists to keep a distance from.</summary>
        private const int NoPreviousHit = -9999999;

        private static int QueueIntervalsPassed => Find.TickManager.TicksGame / Storyteller.IncidentCycleLengthTicks;

        private static void GenerateHitList(
            List<int> hits, int seed, int start, int length, float minIncidents, float maxIncidents,
            int minSpacingIntervals, float acceptFraction, int fixedHit)
        {
            var rand = new RandomStream(seed);
            int wanted = GenMath.RoundRandom(rand.Range(minIncidents, maxIncidents), rand);

            // Try again from fresh draws until the relaxed plan still fits inside the on-phase. RimWorld
            // gives up after a hundred tries and logs that minSpacingDays is too high for the range; the
            // content test for that (StorytellerCompProperties_OnOffCycle.ConfigErrors) catches it earlier.
            // Out of tries means an empty plan for this cycle, exactly as RimWorld's does (its loop clears the
            // list and then gives up).
            int attempts = 0;
            while (true)
            {
                hits.Clear();
                if (attempts++ > MaxRelaxAttempts) break;
                for (int i = 0; i < wanted; i++) hits.Add(rand.Range(0, length) + start);
                hits.Sort();
                if (RelaxToSatisfyMinDiff(hits, minSpacingIntervals, fixedHit, start + length)) break;
            }

            if (acceptFraction < 1f)
            {
                int keep = GenMath.RoundRandom(hits.Count * acceptFraction, rand);
                Shuffle(hits, rand);
                hits.RemoveRange(keep, hits.Count - keep);
            }
        }

        /// <summary>Pushes each hit later until it is at least <paramref name="minDiff"/> after the one before it (the first, after
        /// <paramref name="fixedValue"/>), and says whether the last one still lies within <paramref name="max"/>.</summary>
        private static bool RelaxToSatisfyMinDiff(List<int> values, int minDiff, int fixedValue, int max)
        {
            if (values.Count == 0) return true;
            for (int i = 0; i < values.Count; i++)
            {
                int diff = i != 0 ? Math.Abs(values[i] - values[i - 1]) : Math.Abs(values[i] - fixedValue);
                if (diff < minDiff)
                {
                    values[i] += minDiff - diff;
                    for (int j = i + 1; j < values.Count; j++)
                    {
                        if (values[j] < values[i] + j) values[j] = values[i] + j;
                    }
                }
            }
            return values[values.Count - 1] <= max;
        }

        private static void Shuffle(List<int> list, RandomStream rand)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rand.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static int DaysToIntervals(float days) => (int)Math.Round(days * 60f, MidpointRounding.ToEven);
    }
}
