using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Factions;

namespace SimWorld.God.View
{
    /// <summary>
    /// One treaty as offered to one civilization: whether <see cref="GodCommands.SignTreaty"/> would sign it
    /// with them right now, and the sentence that says why not. Per treaty because one rule is: while at war
    /// only a pact of non-aggression can be signed (<see cref="Faction.SignTreaty"/>), so a trade agreement is
    /// refused where a non-aggression pact is not.
    /// </summary>
    public sealed class TreatyOfferView
    {
        internal TreatyOfferView(string defName, bool canSign, string reason)
        {
            DefName = defName;
            CanSign = canSign;
            Reason = reason;
        }

        /// <summary>The treaty's handle — a <see cref="TreatyOption.DefName"/>, what
        /// <see cref="GodCommands.SignTreaty"/> takes.</summary>
        public string DefName { get; }

        public bool CanSign { get; }

        /// <summary>The refusal <see cref="GodCommands.SignTreaty"/> would give, word for word, or
        /// "Ready to sign." — always populated.</summary>
        public string Reason { get; }

        internal static List<TreatyOfferView> AllFor(Faction player, Faction other)
        {
            // TreatyCatalogue.Treaties' own order (by defName), so the two lists line up row for row.
            var all = new List<TreatyDef>(DefDatabase<TreatyDef>.AllDefsListForReading);
            all.Sort(static (a, b) => string.CompareOrdinal(a.defName, b.defName));
            var offers = new List<TreatyOfferView>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                string? refusal = GodCommands.TreatyRefusal(player, other, all[i]);
                offers.Add(new TreatyOfferView(all[i].defName, refusal == null, refusal ?? "Ready to sign."));
            }
            return offers;
        }
    }
}
