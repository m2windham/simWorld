using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// <b>The ledger supplies a building site</b>: what the blueprints and frames on a watched map still wait for
    /// and the map does not hold, <see cref="SettlementStockInitiative"/> issues out of
    /// <see cref="Settlement.Stores"/> onto the map, where builders fetch it as they fetch anything else
    /// (<see cref="WorkGiver_ConstructDeliverResources"/>).
    ///
    /// <para/><b>Why.</b> RimWorld has no ledger: a colony's cut stone and steel lie in its stockpiles and a builder
    /// carries them to the frame. Here whatever rests in storage is banked
    /// (<see cref="SettlementStockInitiative.BankStoredGoods"/>), which keeps a material only while a site already
    /// wants it. Anything banked before the site was planned could never come back: the ten-year arcs' towns held
    /// hundreds of logs and every chunk they mined in the ledger, none on the map, and the material gates that read
    /// the map alone never saw a stone wall's or a renewal's worth (renewal lane 2). The bill half of the same
    /// crossing is <see cref="SettlementBillStock"/>; this is the building half.
    ///
    /// <para/><b>Bounded by what the sites need.</b> Per material, the sum every site still waits for, less what
    /// lies on the map already, wherever it lies and whoever has claimed it, and no more than the ledger holds. The
    /// next pass sees it on the map and issues nothing until it is spent. Banking cannot take it back while a site
    /// wants it, and takes any surplus back once none does.
    ///
    /// <para/><b>Where it lands.</b> Around the granary, as food is issued (<see cref="SettlementStockInitiative"/>'s
    /// placement), on cells the band can walk to. Deterministic: materials in defName order, no draw.
    /// </summary>
    public static class SettlementBuildStock
    {
        /// <summary>Issues what the sites on <paramref name="map"/> wait for and the map lacks; returns the units
        /// issued.</summary>
        public static int IssueConstructionMaterialsFromStores(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (settlement.Stores.Count == 0) return 0;

            var wanted = new SortedDictionary<string, (ThingDef Def, int Count)>(StringComparer.Ordinal);
            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (!(blueprints[i] is Blueprint bp)) continue;
                List<ThingDefCountClass> cost = bp.TotalMaterialCost();
                for (int c = 0; c < cost.Count; c++) Add(wanted, cost[c].thingDef, bp.ThingCountNeeded(cost[c].thingDef));
            }
            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (!(frames[i] is Frame frame)) continue;
                List<ThingDefCountClass> cost = frame.TotalMaterialCost();
                for (int c = 0; c < cost.Count; c++) Add(wanted, cost[c].thingDef, frame.MaterialStillNeeded(cost[c].thingDef));
            }

            int issued = 0;
            foreach ((ThingDef def, int count) in wanted.Values)
            {
                int short_ = Math.Min(count - MaterialLadder.OnMap(map, def), settlement.StoreCountOf(def));
                if (short_ <= 0) continue;
                int placed = SettlementStockInitiative.PlaceIssued(def, short_, map);
                // Debit in the same step that spawns, as every crossing of the ledger does.
                settlement.AddStore(def, -placed);
                issued += placed;
            }
            return issued;
        }

        private static void Add(SortedDictionary<string, (ThingDef Def, int Count)> wanted, ThingDef def, int count)
        {
            if (count <= 0) return;
            wanted[def.defName] = (def, (wanted.TryGetValue(def.defName, out (ThingDef Def, int Count) had) ? had.Count : 0) + count);
        }
    }
}
