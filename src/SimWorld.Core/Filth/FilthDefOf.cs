using SimWorld.Defs;
using SimWorld.Stats;

namespace SimWorld.Filth
{
    /// <summary>
    /// The filth ThingDefs this module's own sources spawn (RimWorld: the <c>Filth_*</c> members of
    /// <c>RimWorld.ThingDefOf</c>). Its own <c>[DefOf]</c> class in its own file rather than new fields on the
    /// shared <c>ThingDefOf</c>: <see cref="DefOfAttribute"/> binding scans every <c>[DefOf]</c> type, so a new
    /// class costs nothing and cannot collide with a lane mid-edit on the shared one (CLAUDE.md).
    /// </summary>
    [DefOf]
    public static class FilthDefOf
    {
        /// <summary>Tracked-in dirt: what soil, gravel, sand and dirt roads leave underfoot. Never expires on
        /// its own — someone has to clean it.</summary>
        public static ThingDef Filth_Dirt = null!;

        /// <summary>Blood, from a bleeding wound or a butcher's block. Dries up on its own after a few days.</summary>
        public static ThingDef Filth_Blood = null!;

        /// <summary>What an animal leaves behind indoors.</summary>
        public static ThingDef Filth_AnimalFilth = null!;
    }

    /// <summary>
    /// The stat room cleanliness is summed from (RimWorld: <c>RimWorld.StatDefOf.Cleanliness</c>, and
    /// <c>RoomStatDefOf.Cleanliness</c> which averages it over a room's cells). Bound in its own class for the
    /// same reason as <see cref="FilthDefOf"/>; the StatDef itself ships in this module's own
    /// <c>Stats_Cleanliness.xml</c> rather than being appended to a shared stat file.
    /// </summary>
    [DefOf]
    public static class FilthStatDefOf
    {
        /// <summary>How much a Thing standing in a cell helps or hurts that room's cleanliness. Negative for
        /// filth; a sterile floor would be positive, once this port has floors that claim to be.</summary>
        public static StatDef Cleanliness = null!;
    }
}
