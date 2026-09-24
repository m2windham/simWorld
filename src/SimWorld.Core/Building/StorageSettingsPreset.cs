using System;
using SimWorld.Crafting;
using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>
    /// The storage settings a new stockpile starts with (RimWorld: <c>RimWorld.StorageSettingsPreset</c>, applied
    /// by <c>new Zone_Stockpile(preset, zoneManager)</c> through <c>StorageSettings.SetFromPreset</c> and
    /// <c>ThingFilter.SetFromPreset</c>). Applied here by <see cref="StorageSettingsPresetUtility.SetFromPreset"/>.
    ///
    /// <para/><b>What RimWorld ships.</b> Two presets, each behind its own zone designator:
    /// <c>DefaultStockpile</c> (the ordinary "Stockpile zone") allows the categories <c>Foods</c>,
    /// <c>Manufactured</c>, <c>ResourcesRaw</c>, <c>Items</c>, <c>Buildings</c>, <c>Weapons</c>, <c>Apparel</c> and
    /// <c>BodyParts</c> and nothing else; <c>DumpingStockpile</c> allows <c>Corpses</c> and <c>Chunks</c> and
    /// nothing else. So an ordinary stockpile has never taken a body in RimWorld: the dead go to a dumping
    /// stockpile or a grave, and a player who wants them somewhere else says so on the storage tab.
    ///
    /// <para/><b>Only the first is ported.</b> <c>DumpingStockpile</c> lands with the work that gives the dead a
    /// destination (graves, burial), which is where somewhere-to-put-a-body belongs; until then nothing in
    /// this port would paint one, and a player can still open any stockpile to bodies by naming them
    /// (<c>Map.View.MapCommands.SetStockpileFilter</c>).
    /// </summary>
    public enum StorageSettingsPreset : byte
    {
        DefaultStockpile,
    }

    /// <summary>Categories <see cref="StorageSettingsPreset"/> reads by name (RimWorld: the same fields on
    /// <c>ThingCategoryDefOf</c>). Its own <c>[DefOf]</c> class rather than another field on
    /// <see cref="Crafting.ThingCategoryDefOf"/>, per CLAUDE.md: a binding in its own file cannot conflict with a
    /// lane editing the shared one.</summary>
    [DefOf]
    public static class StorageThingCategoryDefOf
    {
        /// <summary>Parent of every corpse category (<c>ThingCategories_Corpses.xml</c>): every generated
        /// <c>Corpse_&lt;race&gt;</c> def sits under it, whatever races content turns out to have.</summary>
        public static ThingCategoryDef Corpses = null!;
    }

    public static class StorageSettingsPresetUtility
    {
        /// <summary>
        /// Replaces <paramref name="filter"/>'s allowances with <paramref name="preset"/>'s (RimWorld:
        /// <c>ThingFilter.SetFromPreset</c>; an extension here only so that <c>Crafting/ThingFilter.cs</c>, a file
        /// every module reads, needs no edit — the call shape is RimWorld's).
        ///
        /// <para/><b>Translation — the complement, not the list.</b> RimWorld builds
        /// <see cref="StorageSettingsPreset.DefaultStockpile"/> as an allow-list of eight top-level categories.
        /// This port's category tree is trimmed to what its recipes needed (<c>ThingCategories.xml</c>): it has
        /// no <c>Weapons</c>, <c>Apparel</c>, <c>Buildings</c> or <c>BodyParts</c> category, and every weapon in
        /// content carries no category at all. An allow-list would therefore quietly refuse whatever content
        /// never gave a category, and a settlement's granary would stop taking its own weapons. What the
        /// RimWorld list actually <i>does</i> for a body is leave it out, and that is expressible exactly:
        /// everything, minus the <see cref="StorageThingCategoryDefOf.Corpses"/> category. Excluding the
        /// category rather than naming defs means a race added later is refused with no edit here, because
        /// its generated corpse def joins the category.
        ///
        /// <para/><b>Chunks are not excluded, deliberately.</b> RimWorld's list also leaves out its
        /// <c>Chunks</c> category (stone chunks go to the dumping stockpile). This port files chunks under
        /// <c>ResourcesRaw</c> and has no <c>Chunks</c> category, and a chunk resting in the granary is what
        /// <c>Economy.SettlementStockInitiative</c> banks for the masons' guild to cut, so the granary taking
        /// stone is load-bearing here rather than an oversight.
        /// </summary>
        public static void SetFromPreset(this ThingFilter filter, StorageSettingsPreset preset)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            switch (preset)
            {
                case StorageSettingsPreset.DefaultStockpile:
                    filter.SetAllowAll(null);
                    filter.SetAllow(StorageThingCategoryDefOf.Corpses, false);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, "Not a ported storage preset.");
            }
        }
    }
}
