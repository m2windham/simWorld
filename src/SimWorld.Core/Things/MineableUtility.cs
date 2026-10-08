using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;

namespace SimWorld.Things
{
    /// <summary>
    /// How much a mined-out edifice pays, and which vein map generation places (RimWorld: the body of
    /// <c>Verse.Mineable.TrySpawnYield</c> plus <c>GenStep_ScatterLumpsMineable</c>'s commonality pick).
    ///
    /// <para/><b>Why this is a utility rather than more lines in <see cref="Mineable"/>.</b> The yield is the
    /// one number this whole system exists to produce, and it is a pure function of a def, a miner and a
    /// random stream — so it can be asserted directly, over a thousand samples, without spawning a map or
    /// running a job. <see cref="Mineable.DestroyMined"/> is then only "destroy, spawn what this says".
    ///
    /// <para/><b>What was dormant before this.</b> <c>ThingDef.mineableThing</c> and
    /// <c>ThingDef.mineableYield</c> were read by no line of <c>src/</c>: <c>JobDriver_Mine</c>'s completion
    /// toil destroyed the rock and spawned nothing, while its own doc claimed otherwise. This is the reader
    /// those two fields never had — and with it <see cref="Director.DifficultyDef.mineYieldFactor"/>, which
    /// had nothing to scale.
    /// </summary>
    public static class MineableUtility
    {
        /// <summary>
        /// How many items mining <paramref name="def"/> yields once <paramref name="yieldPct"/> of it has been
        /// credited to its miners, or 0 for nothing at all (RimWorld: the body of
        /// <c>Mineable.TrySpawnYield</c>). In RimWorld's own order:
        /// <list type="number">
        /// <item><description><c>mineableDropChance</c> decides whether anything drops. Ore always pays;
        /// plain rock pays a chunk only sometimes.</description></item>
        /// <item><description>The Def's <c>mineableYield</c>, scaled by the difficulty preset's
        /// <see cref="Director.DifficultyDef.mineYieldFactor"/> and rounded to a whole number — the preset's
        /// thumb on the scale ("worse yields").</description></item>
        /// <item><description>For a wasteable yield only, that amount is scaled by
        /// <paramref name="yieldPct"/> — what the pick hits credited, i.e. how much of the vein the miners
        /// recovered rather than wasting in the rubble — and rounded at random, so a half-recovered yield of 5
        /// pays 2 or 3 and averages 2.5.</description></item>
        /// </list>
        /// The result never rounds below 1 once something has dropped, matching RimWorld: a bad miner on a
        /// hard difficulty recovers less of the vein, never none of it.
        /// </summary>
        public static int YieldFromCredit(ThingDef def, float yieldPct, RandomStream rand)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (def.mineableThing == null || def.mineableYield <= 0) return 0;
            if (!rand.Chance(def.mineableDropChance)) return 0;

            float factor = Find.Storyteller.difficulty?.mineYieldFactor ?? 1f;
            int amount = Math.Max(1, (int)Math.Round(def.mineableYield * factor));
            if (def.mineableYieldWasteable)
            {
                amount = Math.Max(1, GenMath.RoundRandom(amount * yieldPct, rand));
            }
            return amount;
        }

        /// <summary>
        /// What <paramref name="miner"/> would recover if they dug the whole of one <paramref name="def"/>
        /// by themselves: <see cref="YieldFromCredit"/> at the credit a single miner earns for all of it,
        /// their <c>MiningYield</c> stat (or everything, for no miner). The question "how good is this miner
        /// at this vein" without spawning a rock or running a job, which is what the tests over a thousand
        /// samples need.
        /// </summary>
        public static int YieldFor(ThingDef def, Pawn? miner, RandomStream rand)
        {
            float credit = miner == null ? 1f : miner.GetStatValue(MiningStatDefOf.MiningYield);
            return YieldFromCredit(def, credit, rand);
        }

        /// <summary>The first <see cref="Mineable"/> standing in <paramref name="c"/>, or null (RimWorld:
        /// <c>GridsUtility.GetFirstMineable</c>).</summary>
        public static Mineable? GetFirstMineable(IntVec3 c, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(c, map)) return null;
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Mineable mineable) return mineable;
            }
            return null;
        }

        /// <summary>
        /// Picks a vein def weighted by <see cref="ThingDef.mineableScatterCommonality"/>, or null when no
        /// loaded content declares one (RimWorld: <c>GenStep_ScatterLumpsMineable.ChooseThingDef</c>).
        /// Read from the database on every call rather than cached: tests re-point
        /// <see cref="DefDatabase.Global"/> between runs, and a cache that outlived one of those swaps would
        /// hand back defs from a database nobody is using any more.
        /// </summary>
        public static ThingDef? RandomVeinDef(RandomStream rand)
        {
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            float total = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].mineable && all[i].mineableScatterCommonality > 0f) total += all[i].mineableScatterCommonality;
            }
            if (total <= 0f) return null;

            float roll = rand.Value * total;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (!def.mineable || def.mineableScatterCommonality <= 0f) continue;
                roll -= def.mineableScatterCommonality;
                if (roll <= 0f) return def;
            }

            // Floating-point slack only: the loop above consumes the whole weight in all but a rounding edge.
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].mineable && all[i].mineableScatterCommonality > 0f) return all[i];
            }
            return null;
        }
    }
}
