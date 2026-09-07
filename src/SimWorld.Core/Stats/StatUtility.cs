using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Stats
{
    /// <summary>
    /// Lookups over a <see cref="StatModifier"/> list — the shape a def's <c>statOffsets</c>/<c>statFactors</c>
    /// and a trait degree's or hediff stage's own offsets/factors all share (RimWorld: <c>RimWorld.StatUtility</c>).
    /// </summary>
    public static class StatUtility
    {
        public static float GetStatOffsetFromList(this List<StatModifier>? modList, StatDef stat) => GetStatValueFromList(modList, stat, 0f);

        public static float GetStatFactorFromList(this List<StatModifier>? modList, StatDef stat) => GetStatValueFromList(modList, stat, 1f);

        public static float GetStatValueFromList(this List<StatModifier>? modList, StatDef stat, float defaultValue)
        {
            if (modList != null)
            {
                for (int i = 0; i < modList.Count; i++)
                {
                    if (ReferenceEquals(modList[i].stat, stat)) return modList[i].value;
                }
            }
            return defaultValue;
        }

        public static bool StatListContains(this List<StatModifier>? modList, StatDef stat)
        {
            if (modList != null)
            {
                for (int i = 0; i < modList.Count; i++)
                {
                    if (ReferenceEquals(modList[i].stat, stat)) return true;
                }
            }
            return false;
        }
    }
}
