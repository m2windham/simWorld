using System;

using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Why the roof now falling fell, for the letter about whoever it falls on. RimWorld's death letter for a
    /// collapse is the damage's own "{0} has been crushed to death." and nothing more; there a player pulled
    /// the support out and saw it go. Here the settlement does its own building, mining and pulling down, and
    /// the fourth playtest's two crush deaths said nothing about which of those it was. So the collapse
    /// carries its cause the way it carries its map: per thread, for exactly as long as the drop runs.
    ///
    /// <para/>Three things set it. <see cref="Map.EdificeGrid.DeRegister"/> names the holder that just left
    /// (<see cref="HolderGone"/>); <see cref="RoofCollapserImmediate"/> names the roof coming down on each
    /// cell (<see cref="Falling"/>); and an event that brings roofs down wholesale, an earthquake, names
    /// itself (<see cref="During"/>), which outranks the holder because the holder is only how it did it.
    /// Nothing is described until somebody dies of it: <see cref="Describe"/> is read by the death letter
    /// alone, so the despawns that drop no roof (nearly all of them) pay a few field writes and no text.
    /// </summary>
    public static class RoofCollapseCause
    {
        [ThreadStatic] private static Thing? holder;
        [ThreadStatic] private static Map.Map? holderMap;
        [ThreadStatic] private static string? occasion;
        [ThreadStatic] private static RoofDef? falling;

        /// <summary>Restores what was set before it, so scopes nest.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly Thing? savedHolder;
            private readonly Map.Map? savedMap;
            private readonly string? savedOccasion;
            private readonly RoofDef? savedFalling;

            internal Scope(Thing? holder, Map.Map? map, string? occasion, RoofDef? falling)
            {
                savedHolder = holder;
                savedMap = map;
                savedOccasion = occasion;
                savedFalling = falling;
            }

            public void Dispose()
            {
                holder = savedHolder;
                holderMap = savedMap;
                occasion = savedOccasion;
                falling = savedFalling;
            }
        }

        private static Scope Saved() => new Scope(holder, holderMap, occasion, falling);

        /// <summary><paramref name="edifice"/>, which held roof, has just left <paramref name="map"/>. A holder
        /// lost inside a collapse already under way (rock the falling roof destroyed) does not replace the one
        /// that started it.</summary>
        public static Scope HolderGone(Thing edifice, Map.Map map)
        {
            Scope saved = Saved();
            if (holder == null)
            {
                holder = edifice;
                holderMap = map;
            }
            return saved;
        }

        /// <summary>Everything that falls inside this scope falls <paramref name="phrase"/> ("in the
        /// earthquake").</summary>
        public static Scope During(string phrase)
        {
            Scope saved = Saved();
            occasion ??= phrase;
            return saved;
        }

        /// <summary><paramref name="roof"/> is coming down on the cell being dropped.</summary>
        internal static Scope Falling(RoofDef roof)
        {
            Scope saved = Saved();
            falling = roof;
            return saved;
        }

        /// <summary>
        /// "the roof of the old house fell in", "the rock overhead fell in when the sandstone holding it up was
        /// mined out": the clause after "crushed to death when". Null unless a roof is falling right now.
        /// </summary>
        public static string? Describe()
        {
            if (falling == null) return null;
            bool rock = falling.isNatural;
            string overhead = rock ? "the rock overhead" : "the roof";
            if (occasion != null) return overhead + " fell in " + occasion;
            if (holder == null) return overhead + " fell in";

            if (holderMap?.settlementCompound is SettlementCompound compound && DwellingAt(compound, holder.Position) is Dwelling d)
            {
                string house = d.beingDemolished ? "the old house" : "the house";
                return rock ? "the rock over " + house + " fell in as it came down" : "the roof of " + house + " fell in";
            }
            if (holder.def.mineable) return overhead + " fell in when the " + holder.def.label + " holding it up was mined out";
            if (holder is Frame frame) return overhead + " fell in when the unfinished " + frame.EntityToBuild.label + " holding it up was taken down";
            return overhead + " fell in when the " + holder.def.label + " holding it up came down";
        }

        private static Dwelling? DwellingAt(SettlementCompound compound, IntVec3 c)
        {
            for (int i = 0; i < compound.dwellings.Count; i++)
            {
                if (compound.dwellings[i].footprint.Contains(c)) return compound.dwellings[i];
            }
            return null;
        }
    }
}
