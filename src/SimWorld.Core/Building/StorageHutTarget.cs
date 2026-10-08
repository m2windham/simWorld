using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// How many <c>StorageHut</c>s a settlement wants. <b>The one place this is decided</b>: the map path
    /// (<see cref="SettlementConstructionInitiative"/>) and the off-map path
    /// (<see cref="AbstractSettlementConstruction"/>) both ask here, so a settlement wants the same number of huts
    /// whether or not anybody has ever opened its map. Until this class each path carried its own copy of the
    /// arithmetic ("restated verbatim against the same constant"), which is exactly the arrangement that lets two
    /// answers drift; one function cannot.
    /// <para/>
    /// Translation: RimWorld has no storage hut. Its colony keeps things in stockpile zones and on shelves, and
    /// "storage" is whatever the player has laid out. The hut is this port's stand-in for "a settlement keeps its
    /// goods somewhere", and what it stands in for scales with how many people there are to keep goods, not
    /// with how much they have gathered.
    /// <para/>
    /// <b>The target follows people.</b> <c>ceil(citizens / <see cref="ConstructionInitiativeTuning.CitizensPerStorageHut"/>)</c>,
    /// once the ledger holds anything at all, and zero while it is empty (a settlement with nothing to keep has
    /// nothing to build storage for). It is at least one whenever it is not zero, so a settlement with goods and
    /// no one on its roster yet still wants a shed. Citizens are <see cref="World.Settlement.Citizens"/> only,
    /// never <see cref="World.Settlement.StatisticalPopulation"/>, the same line the bed and wall needs draw and
    /// for the same reason.
    /// <para/>
    /// <b>It does not follow goods.</b> It used to: one hut per 50 units in <see cref="World.Settlement.Stores"/>,
    /// uncapped. A hut physically holds nothing (the ledger is off the map), so that rule spent 25 wood on a
    /// marker for every 50 units gathered and rose with every harvest: 453 huts for 26 people by day 20 of seed
    /// 777. Already-built surplus is left standing; the target simply stops asking for more
    /// (<see cref="SettlementConstructionInitiative"/> counts built and planned huts against it).
    /// </summary>
    public static class StorageHutTarget
    {
        /// <summary>The target for a settlement as it stands: its real citizens and its ledger.</summary>
        public static int For(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            int totalStored = 0;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores) totalStored += kv.Value;
            return For(settlement.Citizens.Count, totalStored);
        }

        /// <summary>The target for <paramref name="citizens"/> people and <paramref name="totalStored"/> units in
        /// the ledger. <paramref name="totalStored"/> decides only whether there is anything to keep, never how
        /// many huts: that is the whole point of this class.</summary>
        public static int For(int citizens, int totalStored)
        {
            if (totalStored <= 0) return 0;
            int perHut = Math.Max(1, ConstructionInitiativeTuning.CitizensPerStorageHut);
            return Math.Max(1, (Math.Max(0, citizens) + perHut - 1) / perHut);
        }
    }
}
