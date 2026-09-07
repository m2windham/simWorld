using System;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Stats
{
    /// <summary>
    /// Call-site sugar over <see cref="StatWorker.GetValue"/> (RimWorld: <c>RimWorld.StatExtension</c>).
    /// </summary>
    public static class StatExtension
    {
        /// <summary>A live Thing's value for <paramref name="stat"/>, through the full pipeline.</summary>
        public static float GetStatValue(this Thing thing, StatDef stat, bool applyPostProcess = true)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return stat.Worker.GetValue(StatRequest.For(thing), applyPostProcess);
        }

        /// <summary>
        /// A def's value for <paramref name="stat"/> with no live Thing, optionally as if built from
        /// <paramref name="stuff"/>, through the full pipeline (RimWorld: <c>BuildableDef.GetStatValueAbstract</c>).
        /// Named <c>GetStatValue</c> rather than <c>GetStatValueAbstract</c> so it cannot collide with
        /// <see cref="ThingDef.GetStatValueAbstract"/> — this codebase's pre-existing statBases-only lookup that
        /// <c>BaseMaxHitPoints</c> and <c>BaseMarketValue</c> still use deliberately (a lookup with no pawn,
        /// trait, hediff or capacity concerns has no reason to pay for the full pipeline).
        /// </summary>
        public static float GetStatValue(this ThingDef def, StatDef stat, ThingDef? stuff = null, bool applyPostProcess = true)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return stat.Worker.GetValue(StatRequest.For(def, stuff), applyPostProcess);
        }
    }
}
