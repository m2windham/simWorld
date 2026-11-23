using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Weather;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// How many storage huts a settlement wants: enough to hold what is in its
    /// <see cref="Settlement.Stores"/>, measured the way storage is measured everywhere else here, in stacks.
    /// Both construction paths read this one answer (<see cref="SettlementConstructionInitiative"/> on a
    /// map, <see cref="AbstractSettlementConstruction"/> without one), so they cannot disagree about it.
    ///
    /// <para/><b>The defect this closes.</b> The need used to be one hut per fifty <i>units</i> in the
    /// ledger, whatever the units were. A founding band is provisioned with some fifty-seven pemmican a
    /// head, and pemmican stacks two hundred to a cell: a band of twenty held 1,146 of it by its first
    /// morning, which is six stacks, and was asked for twenty-three huts to keep it in. By day ten it had
    /// forty huts for twenty-two stacks of goods, and a band of forty had seventy-one by day two. The need
    /// did follow the stock, as it was meant to; it counted the stock in a unit no store holds things in. A
    /// stockpile cell holds a stack (<c>ThingDef.stackLimit</c>), <c>Economy.SettlementStockInitiative</c>
    /// sizes its granary in stacks, and a hut is now sized the same way.
    ///
    /// <para/><b>Nothing here is a cap.</b> The count still rises with what the settlement holds and falls
    /// when it holds less; a settlement that stockpiles for a year will line its ring with huts, and that
    /// is the stock showing. What changed is that a sack of pemmican no longer counts as fifty-seven things.
    ///
    /// <para/><b>Only what the weather would ruin.</b> RimWorld's reason to roof a store is deterioration:
    /// a thing whose <c>DeteriorationRate</c> is above nothing loses hit points under open sky
    /// (<c>SteadyEnvironmentEffects.FinalDeteriorationRate</c>), and a thing without one keeps in an open
    /// stockpile for ever, which is where a RimWorld colony leaves its chunks, blocks and steel. So a hut is
    /// wanted for the stacks that need covering (<see cref="StacksNeedingCover"/>), not for every stack.
    /// Counting the whole ledger was the defect: a 40-strong band (<c>tools/bench --suite arc</c>, seed 201)
    /// held 23,448 raw rice by day 30, which is about a month of food and 313 stacks, and was asked for 105
    /// huts for it, which took the wood and the hands its houses needed. Food carries no deterioration rate
    /// here; it spoils by rotting, which a roof does not stop (RimWorld's answer for food is a cold room, not a
    /// shed).
    /// </summary>
    public static class StorageHutNeed
    {
        /// <summary>
        /// Stacks one <c>StorageHut</c> holds. Three is what RimWorld's shelf holds to the cell since 1.4
        /// (recalled, not quoted: the game's defs are not reachable from here), and a hut is one cell.
        /// Pinned by <c>StorageHutNeedTests</c> as "enough huts for the stock, and no more than that",
        /// never by this literal.
        /// </summary>
        public const int StacksPerStorageHut = 3;

        /// <summary>The stacks <paramref name="settlement"/>'s ledger would make if it were laid out on the
        /// ground: each def's count over its <c>stackLimit</c>, rounded up, since two defs never share a
        /// stack.</summary>
        public static int StacksHeld(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            int stacks = 0;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
            {
                if (kv.Value <= 0) continue;
                int limit = Math.Max(1, kv.Key.stackLimit);
                stacks += (kv.Value + limit - 1) / limit;
            }
            return stacks;
        }

        /// <summary>The part of <see cref="StacksHeld"/> that would wear away left outside: stacks of every
        /// def that can deteriorate and has a <c>DeteriorationRate</c> above zero.</summary>
        public static int StacksNeedingCover(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            int stacks = 0;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
            {
                if (kv.Value <= 0 || !NeedsCover(kv.Key)) continue;
                int limit = Math.Max(1, kv.Key.stackLimit);
                stacks += (kv.Value + limit - 1) / limit;
            }
            return stacks;
        }

        private static bool NeedsCover(ThingDef def) =>
            def.CanEverDeteriorate && def.GetStatValueAbstract(DeteriorationDefOf.DeteriorationRate) > 0f;

        /// <summary>Huts enough to hold <see cref="StacksNeedingCover"/>: none while nothing in the ledger
        /// would spoil in the open, one as soon as something would, another each time the last one is
        /// full.</summary>
        public static int Target(Settlement settlement)
        {
            int stacks = StacksNeedingCover(settlement);
            return (stacks + StacksPerStorageHut - 1) / StacksPerStorageHut;
        }
    }
}
