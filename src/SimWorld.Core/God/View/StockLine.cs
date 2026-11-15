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
    /// <para/><b>A body is not goods.</b> A <see cref="Corpse"/> lying on the map is left out, for the reason
    /// banking leaves it alone: a settlement's dead are its people, not its stock.
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

        /// <summary>Everything <paramref name="settlement"/> holds, by def: its ledger plus every item on its
        /// interior map, when it has one. Never generates a map to look — a settlement nobody has entered
        /// has its ledger and nothing else.</summary>
        internal static Dictionary<ThingDef, int> Tally(World.Settlement settlement)
        {
            var held = new Dictionary<ThingDef, int>(settlement.Stores);

            Map.Map? map = settlement.InteriorMap;
            if (map == null) return held;

            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing item = items[i];
                if (item is Corpse || item.stackCount <= 0) continue;
                held.TryGetValue(item.def, out int have);
                held[item.def] = have + item.stackCount;
            }
            return held;
        }

        /// <summary>One line per def held, ordered by defName so the list reads the same way on every capture
        /// rather than in whatever order the ledger's dictionary happens to enumerate.</summary>
        internal static List<StockLine> AllOf(Dictionary<ThingDef, int> held)
        {
            var lines = new List<StockLine>(held.Count);
            foreach (KeyValuePair<ThingDef, int> entry in held)
            {
                if (entry.Value > 0) lines.Add(new StockLine(entry.Key.defName, entry.Key.LabelCap, entry.Value));
            }
            lines.Sort(static (a, b) => string.CompareOrdinal(a.DefName, b.DefName));
            return lines;
        }
    }
}
