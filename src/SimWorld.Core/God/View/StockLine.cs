using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.God.View
{
    /// <summary>
    /// One kind of goods a settlement holds, and how many — what a status bar needs to answer "do we have
    /// enough" before the player chooses what to do next (<c>docs/spec/simworld-spec.md</c> §12a).
    ///
    /// <para/><b>Both books, because a settlement keeps two.</b> A unit of goods is either a count in
    /// <see cref="World.Settlement.Stores"/> or a <c>Thing</c> on the settlement's interior map, never both
    /// (<c>Economy.SettlementStockInitiative</c>'s invariant), so the sum of the two is the settlement's
    /// holdings with nothing counted twice. Reading the ledger alone would be wrong in exactly the settlement
    /// the player is looking at: opening one moves its larder out of the ledger and onto the map, where
    /// people eat, so a ledger-only readout shows food falling to nothing in a town that has just been fed.
    /// This is the same total <c>AI.HuntingInitiative.NutritionAvailable</c> calls "the honest total of both
    /// books", carried per def rather than only as nutrition.
    ///
    /// <para/><b>Resources only, the way RimWorld's readout counts them.</b> A def is listed only when it
    /// <see cref="ThingDef.CountAsResource"/> (RimWorld: <c>ResourceCounter</c> filters by the same flag), in
    /// both books alike. That leaves out gear and rubble — the knives a founding band carried in, the rock
    /// chunks a freshly generated map is strewn with — which a settlement has lying about but does not store
    /// against need, and which would otherwise crowd the things a player is actually watching run down.
    ///
    /// <para/><b>One deliberate departure: not only what is in storage.</b> RimWorld's counter also requires
    /// an item to be resting in a stockpile, and this does not. In this port a watched settlement's larder is
    /// issued onto the map loose, on purpose (<c>Economy.SettlementStockInitiative.IssueFromStores</c>: "left
    /// loose rather than zoned"), so a storage-only count would show a town that has just been opened — and
    /// fed — as holding no food at all until somebody had hauled it back indoors.
    ///
    /// <para/><b>A body is not goods.</b> A <see cref="Corpse"/> lying on the map is left out whatever its def
    /// says, for the reason banking leaves it alone: a settlement's dead are its people, not its stock.
    ///
    /// <para/>A value and a defName, like every other line on this seam; sparse, so a def the settlement
    /// holds none of is absent rather than listed at zero.
    /// </summary>
    public sealed class StockLine
    {
        internal StockLine(string defName, string label, int count)
        {
            DefName = defName;
            Label = label;
            Count = count;
        }

        /// <summary>The handle — a <c>ThingDef</c>'s defName, never the def.</summary>
        public string DefName { get; }

        public string Label { get; }

        /// <summary>Units held across both books. Always positive.</summary>
        public int Count { get; }

        /// <summary>Every resource <paramref name="settlement"/> holds, by def: what its ledger lists plus
        /// what lies on its interior map, when it has one, each filtered by
        /// <see cref="ThingDef.CountAsResource"/>. Never generates a map to look — a settlement nobody has
        /// entered has its ledger and nothing else.</summary>
        internal static Dictionary<ThingDef, int> Tally(World.Settlement settlement)
        {
            var held = new Dictionary<ThingDef, int>();
            foreach (KeyValuePair<ThingDef, int> stored in settlement.Stores)
            {
                if (stored.Key.CountAsResource && stored.Value > 0) held[stored.Key] = stored.Value;
            }

            Map.Map? map = settlement.InteriorMap;
            if (map == null) return held;

            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing item = items[i];
                if (item is Corpse || !item.def.CountAsResource || item.stackCount <= 0) continue;
                held.TryGetValue(item.def, out int have);
                held[item.def] = have + item.stackCount;
            }
            return held;
        }

        /// <summary>One line per def held, highest <see cref="ThingDef.resourceReadoutPriority"/> first and
        /// by defName within a priority (RimWorld's readout leads with what matters most), so the list reads
        /// the same way on every capture rather than in whatever order a dictionary happens to enumerate.</summary>
        internal static List<StockLine> AllOf(Dictionary<ThingDef, int> held)
        {
            var defs = new List<ThingDef>(held.Keys);
            defs.Sort(static (a, b) =>
            {
                int byPriority = b.resourceReadoutPriority.CompareTo(a.resourceReadoutPriority);
                return byPriority != 0 ? byPriority : string.CompareOrdinal(a.defName, b.defName);
            });

            var lines = new List<StockLine>(defs.Count);
            for (int i = 0; i < defs.Count; i++)
            {
                lines.Add(new StockLine(defs[i].defName, defs[i].LabelCap, held[defs[i]]));
            }
            return lines;
        }
    }
}
