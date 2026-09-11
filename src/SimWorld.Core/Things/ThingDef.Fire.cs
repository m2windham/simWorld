using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Defs
{
    /// <summary>
    /// Fire-layer half of <see cref="ThingDef"/>: how readily instances of this Def burn. Its own partial
    /// file beside <c>Things/ThingDef.Things.cs</c> and <c>Building/ThingDef.Building.cs</c>, which already
    /// split <see cref="ThingDef"/> the same way.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>
        /// This Def's flammability with no live Thing (RimWorld: <c>ThingDef.BaseFlammability</c>, which is
        /// literally <c>GetStatValueAbstract(StatDefOf.Flammability)</c>). Deliberately the <b>full</b> stat
        /// pipeline — <see cref="StatExtension.GetStatValue(ThingDef, StatDef, ThingDef, bool)"/>, not this
        /// class's own statBases-only <see cref="GetStatValueAbstract"/> — because two of the three things
        /// that decide whether something burns live outside <c>statBases</c>: the material it is built from
        /// (<c>stuffProps.statFactors</c>, so a stone wall does not burn while a wooden one does) and
        /// <see cref="Stats.StatPart_Flammability"/>. A live Thing asks
        /// <see cref="Things.FireUtility.FlammabilityOf"/> instead, which is the same pipeline with that
        /// Thing's own <see cref="Things.Thing.Stuff"/> in the request.
        /// <para/>
        /// Returns 0 rather than throwing when no content is loaded: a Def-less unit test must not be told
        /// everything is flammable.
        /// </summary>
        public float BaseFlammability
        {
            get
            {
                StatDef? stat = FireStatDefOf.Flammability;
                return stat == null ? 0f : this.GetStatValue(stat);
            }
        }
    }
}
