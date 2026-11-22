using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Factions;

namespace SimWorld.God.View
{
    /// <summary>One treaty a civilization could sign, as a Diplomacy screen offers it — the catalogue half of
    /// <see cref="GodCommands.SignTreaty"/>, where <see cref="TreatyView"/> is the history half (treaties
    /// already signed).</summary>
    public sealed class TreatyOption
    {
        internal TreatyOption(
            string defName, string label, string description, bool nonAggression, bool tradeAccess, int durationDays)
        {
            DefName = defName;
            Label = label;
            Description = description;
            NonAggression = nonAggression;
            TradeAccess = tradeAccess;
            DurationDays = durationDays;
        }

        /// <summary>The handle to pass to <see cref="GodCommands.SignTreaty"/>. Not a Def — see
        /// <see cref="GodViewSnapshot"/> on why the seam is a string.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Description { get; }

        /// <summary>While it holds, neither side may declare war; signed mid-war, it ends the war
        /// (<see cref="TreatyDef.nonAggression"/>).</summary>
        public bool NonAggression { get; }

        /// <summary>While it holds, trade between the two goes at a better price
        /// (<see cref="TreatyDef.tradeAccess"/>). The size of that gain is deliberately not on the seam: it is a
        /// multiplier to solve, not a choice to make — the same reason <see cref="StorytellerCatalogue"/> orders
        /// difficulties by threat scale without handing the scale out.</summary>
        public bool TradeAccess { get; }

        /// <summary>Days it stays in force once signed; 0 when it never lapses on its own
        /// (<see cref="TreatyDef.durationDays"/> &lt;= 0).</summary>
        public int DurationDays { get; }

        internal static TreatyOption For(TreatyDef def) => new TreatyOption(
            def.defName,
            def.LabelCap,
            def.description ?? "",
            def.nonAggression,
            def.tradeAccess,
            def.durationDays > 0 ? def.durationDays : 0);
    }

    /// <summary>
    /// The treaties <see cref="GodCommands.SignTreaty"/> accepts, listed without a game — the same shape and
    /// reasoning as <see cref="StorytellerCatalogue"/>: content does not change during a run, so this needs no
    /// snapshot, and it lists without selecting (signing is <see cref="GodCommands.SignTreaty"/>, the one
    /// write path).
    ///
    /// <para/><b>Whether a treaty can be signed with a given civilization</b> is not here, because it is not a
    /// fact about the treaty: <see cref="Faction.SignTreaty"/> refuses by who the two parties are, never by
    /// which treaty it is. It is on each <see cref="FactionRelationView"/> as
    /// <see cref="FactionRelationView.CanSignTreaty"/> and <see cref="FactionRelationView.TreatyReason"/>,
    /// read from the same check the command makes.
    ///
    /// <para/>Empty rather than throwing when no content is loaded.
    /// </summary>
    public static class TreatyCatalogue
    {
        /// <summary>
        /// Every treaty in content, ordered by defName (ordinal). <see cref="TreatyDef"/> has no display-order
        /// field and no single field that ranks one treaty above another, so the order is simply total and
        /// stable rather than dependent on file load order.
        /// </summary>
        public static IReadOnlyList<TreatyOption> Treaties()
        {
            IReadOnlyList<TreatyDef> all = DefDatabase<TreatyDef>.AllDefsListForReading;
            var defs = new List<TreatyDef>(all.Count);
            for (int i = 0; i < all.Count; i++) defs.Add(all[i]);

            defs.Sort(static (a, b) => string.CompareOrdinal(a.defName, b.defName));

            var options = new List<TreatyOption>(defs.Count);
            for (int i = 0; i < defs.Count; i++) options.Add(TreatyOption.For(defs[i]));
            return options;
        }
    }
}
