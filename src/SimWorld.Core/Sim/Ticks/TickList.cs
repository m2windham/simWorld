using System;
using System.Collections.Generic;

namespace SimWorld.Sim
{
    /// <summary>
    /// One tick frequency's worth of tickables, spread over <see cref="TickInterval"/> buckets so that a
    /// rare ticker costs 1/250th of a normal one (RimWorld: <c>Verse.TickList</c>). Bucket = id mod interval,
    /// and bucket <c>t mod interval</c> runs on tick <c>t</c>. Registration during a tick is deferred until
    /// the next tick so the bucket being iterated is never mutated.
    /// </summary>
    public sealed class TickList
    {
        private readonly List<ITickable>[] buckets;
        private readonly List<ITickable> toRegister = new List<ITickable>();
        private readonly List<ITickable> toDeregister = new List<ITickable>();
        private bool ticking;

        public TickerType TickType { get; }
        public int TickInterval { get; }

        public TickList(TickerType tickType)
        {
            TickType = tickType;
            TickInterval = IntervalFor(tickType);
            buckets = new List<ITickable>[TickInterval];
            for (int i = 0; i < TickInterval; i++)
            {
                buckets[i] = new List<ITickable>();
            }
        }

        public static int IntervalFor(TickerType type)
        {
            switch (type)
            {
                case TickerType.Normal: return 1;
                case TickerType.Rare: return GenTicks.TickRareInterval;
                case TickerType.Long: return GenTicks.TickLongInterval;
                default: throw new ArgumentOutOfRangeException(nameof(type), "TickerType.Never has no TickList.");
            }
        }

        /// <summary>Registered tickables including ones queued during the current tick.</summary>
        public int Count
        {
            get
            {
                int n = toRegister.Count - toDeregister.Count;
                for (int i = 0; i < buckets.Length; i++) n += buckets[i].Count;
                return n;
            }
        }

        public int BucketOf(ITickable tickable)
        {
            int id = tickable.TickId % TickInterval;
            return id < 0 ? id + TickInterval : id;
        }

        /// <summary>
        /// Adds <paramref name="tickable"/> to its bucket, or does nothing if it is already there.
        ///
        /// <para/><b>Idempotent on purpose.</b> Two independent callers now decide membership:
        /// <see cref="Things.Thing.SpawnSetup"/>, for anything standing on a map, and
        /// <see cref="CitizenTickRegistry"/>, for the citizens who are on no map at all and whom the spawn path
        /// therefore never reaches. They overlap for exactly one moment — a citizen the registry has already
        /// seated being spawned onto a freshly generated interior — and a duplicate entry there would tick that
        /// pawn twice per tick for the rest of the game: needs falling at double rate, two think-tree passes, a
        /// silent 2x on everything. A membership set is the right shape for "is this thing ticking", and the
        /// alternative (teaching every caller to look first) is the rule that gets forgotten once.
        /// </summary>
        public void RegisterThing(ITickable tickable)
        {
            if (tickable == null) throw new ArgumentNullException(nameof(tickable));
            List<ITickable> bucket = buckets[BucketOf(tickable)];
            if (ticking)
            {
                if (!toRegister.Contains(tickable) && !bucket.Contains(tickable)) toRegister.Add(tickable);
            }
            else if (!bucket.Contains(tickable))
            {
                bucket.Add(tickable);
            }
        }

        public void DeregisterThing(ITickable tickable)
        {
            if (tickable == null) throw new ArgumentNullException(nameof(tickable));
            if (ticking)
            {
                toDeregister.Add(tickable);
            }
            else
            {
                buckets[BucketOf(tickable)].Remove(tickable);
                toRegister.Remove(tickable);
            }
        }

        public bool Contains(ITickable tickable) =>
            buckets[BucketOf(tickable)].Contains(tickable) || (toRegister.Contains(tickable) && !toDeregister.Contains(tickable));

        /// <summary>Runs the bucket for <paramref name="ticksGame"/>; destroyed entries are dropped.</summary>
        public void Tick(int ticksGame)
        {
            FlushQueues();
            List<ITickable> bucket = buckets[((ticksGame % TickInterval) + TickInterval) % TickInterval];
            ticking = true;
            try
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    ITickable t = bucket[i];
                    if (t.Destroyed)
                    {
                        toDeregister.Add(t);
                        continue;
                    }
                    switch (TickType)
                    {
                        case TickerType.Normal: t.Tick(); break;
                        case TickerType.Rare: t.TickRare(); break;
                        case TickerType.Long: t.TickLong(); break;
                    }
                }
            }
            finally
            {
                ticking = false;
            }
            FlushQueues();
        }

        public void Reset()
        {
            for (int i = 0; i < buckets.Length; i++) buckets[i].Clear();
            toRegister.Clear();
            toDeregister.Clear();
        }

        private void FlushQueues()
        {
            if (toDeregister.Count > 0)
            {
                for (int i = 0; i < toDeregister.Count; i++)
                {
                    ITickable t = toDeregister[i];
                    buckets[BucketOf(t)].Remove(t);
                    toRegister.Remove(t);
                }
                toDeregister.Clear();
            }
            if (toRegister.Count > 0)
            {
                for (int i = 0; i < toRegister.Count; i++)
                {
                    buckets[BucketOf(toRegister[i])].Add(toRegister[i]);
                }
                toRegister.Clear();
            }
        }
    }
}
