using System;
using System.Collections.Generic;

using SimWorld.Defs;
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
        /// How many items mining <paramref name="def"/> yields, or 0 for nothing at all (RimWorld:
        /// <c>Mineable.TrySpawnYield</c>'s count). Three multipliers, in RimWorld's own order of concerns:
        /// <list type="number">
        /// <item><description><c>mineableDropChance</c> decides whether anything drops. Ore always pays;
        /// plain rock pays a chunk only sometimes.</description></item>
        /// <item><description>The miner's <c>MiningYield</c> stat wastes part of a wasteable yield, so a
        /// better miner recovers more of the same vein. Skipped for an unattended destruction
        /// (<paramref name="miner"/> null) and for anything whose def says its yield is not wasteable.</description></item>
        /// <item><description><see cref="Director.DifficultyDef.mineYieldFactor"/>, the difficulty preset's
        /// thumb on the scale. <b>Where RimWorld itself applies this factor could not be checked here</b>, so
        /// this port applies it to the same amount the final rounding sees — which is the behaviour the
        /// preset's own description promises ("worse yields").</description></item>
        /// </list>
        /// The result never rounds below 1 once something has dropped, matching RimWorld: a bad miner on a
        /// hard difficulty recovers less of the vein, never none of it.
        /// </summary>
        public static int YieldFor(ThingDef def, Pawn? miner, RandomStream rand)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (def.mineableThing == null || def.mineableYield <= 0) return 0;
            if (!rand.Chance(def.mineableDropChance)) return 0;

            float yield = def.mineableYield;
            if (def.mineableYieldWasteable && miner != null)
            {
                yield *= miner.GetStatValue(MiningStatDefOf.MiningYield);
            }
            yield *= Find.Storyteller.difficulty?.mineYieldFactor ?? 1f;

            return Math.Max(1, GenMath.RoundRandom(yield, rand));
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
