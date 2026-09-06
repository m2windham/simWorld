namespace SimWorld.Sim
{
    /// <summary>How often a thing wants to be ticked (RimWorld: <c>Verse.TickerType</c>).</summary>
    public enum TickerType
    {
        Never,
        /// <summary>Every tick.</summary>
        Normal,
        /// <summary>Every 250 ticks (~4 s).</summary>
        Rare,
        /// <summary>Every 2000 ticks (~33 s).</summary>
        Long,
    }

    /// <summary>Game speed (RimWorld: <c>Verse.TimeSpeed</c>).</summary>
    public enum TimeSpeed : byte
    {
        Paused = 0,
        Normal = 1,
        Fast = 2,
        Superfast = 3,
        Ultrafast = 4,
    }

    /// <summary>
    /// Anything the <see cref="TickManager"/> drives. Things, pawns and buildings implement this later;
    /// the id must be stable for the object's lifetime because it picks the tick bucket.
    /// </summary>
    public interface ITickable
    {
        /// <summary>Stable identity used to spread work across buckets (a Thing's id number).</summary>
        int TickId { get; }

        /// <summary>Destroyed tickables are skipped and dropped from their buckets.</summary>
        bool Destroyed { get; }

        TickerType TickerType { get; }

        void Tick();
        void TickRare();
        void TickLong();
    }
}
