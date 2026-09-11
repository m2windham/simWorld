using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>The art a settlement can raise, bound by defName in its own <c>[DefOf]</c> class — a new
    /// binding goes in its own class, never appended to a shared one (CLAUDE.md), the same call
    /// <see cref="StoneworkThingDefOf"/> already made next door. <c>Sculpture</c> ships in
    /// <c>Buildings_Art.xml</c>; the two mineral ones in <c>Buildings_Sculpture.xml</c>.</summary>
    [DefOf]
    public static class ArtThingDefOf
    {
        public static ThingDef Sculpture = null!;
        public static ThingDef SculptureGold = null!;
        public static ThingDef SculptureJade = null!;
    }

    /// <summary>
    /// Which sculpture a settlement carves next, and how many of them it wants (system: mining — what a
    /// mined mineral is finally made out of; system: needs.beauty — what makes the number the beauty sampler
    /// reads non-zero).
    ///
    /// <para/><b>The gap this closes.</b> Two at once, and they close each other. Ten minerals came out of
    /// the ground and no recipe ingredient or <see cref="Defs.ThingDef.costList"/> in all of content named
    /// one of them (see <c>Buildings_Sculpture.xml</c> for which two are wired here and why those two);
    /// separately, <c>Sculpture</c> had a Blueprint/Frame pair and nothing in <c>src/</c> so much as
    /// mentioned it, so the one thing this port ships that exists to be looked at could never appear in a
    /// game. Gold and jade now buy the art, and the art is what spends them.
    ///
    /// <para/><b>Deliberately the same shape as <see cref="StoneWallMaterials"/>, not a copy of it.</b> That
    /// class answers "which stone is this wall cut from" for a need
    /// <see cref="SettlementConstructionInitiative"/> already computes; this one owns both halves, because
    /// nothing anywhere computed a need for art. Where the two overlap the reasoning is that class's and is
    /// not restated here: why per-material Defs at all (this port costs a building through
    /// <see cref="Defs.ThingDef.costList"/>, and a costList entry names one ThingDef), and why
    /// <see cref="EquivalentsOf"/> has to exist once two Defs can fill one need.
    ///
    /// <para/><b>The count is derived, never chosen</b> — see <see cref="SculpturesWantedOf"/>. It has to be,
    /// because the arithmetic is not obvious and the obvious guess ("one per citizen") is wrong in a way
    /// nobody would notice: beauty is an <i>average over the cells a citizen can see</i>
    /// (<see cref="BeautyUtility.AverageBeautyPerceptible"/>), so a sculpture per citizen spread across a map
    /// moves that average by almost nothing. Density is the only thing that reads, which is also why
    /// <see cref="SettlementWorksInitiative"/> anchors each new one beside the last.
    /// </summary>
    public static class SculptureMaterials
    {
        /// <summary>
        /// Every Def that fills the settlement's want for art, most beautiful first. Order is read off the
        /// content (<see cref="BeautyStatDefOf.Beauty"/>, with defName settling a tie) rather than written
        /// down here, so a fourth material added in XML needs no code change and cannot disagree with the
        /// numbers it ships — the same call <see cref="StoneWallMaterials.AllWallDefs"/> makes for toughness.
        /// </summary>
        public static IReadOnlyList<ThingDef> AllSculptureDefs
        {
            get
            {
                var art = new List<ThingDef>
                {
                    ArtThingDefOf.SculptureJade,
                    ArtThingDefOf.SculptureGold,
                    ArtThingDefOf.Sculpture,
                };
                art.Sort(ByBeautyThenName);
                return art;
            }
        }

        /// <summary>Whether <paramref name="def"/> is one of the Defs that fill the want for art.</summary>
        public static bool IsSculpture(ThingDef? def)
        {
            if (def == null) return false;
            IReadOnlyList<ThingDef> art = AllSculptureDefs;
            for (int i = 0; i < art.Count; i++)
            {
                if (ReferenceEquals(art[i], def)) return true;
            }
            return false;
        }

        /// <summary>The Defs that count toward the same want as <paramref name="def"/> — every sculpture for a
        /// sculpture, and just itself for everything else. Without it a settlement that had carved its gold
        /// would go straight on wanting the same number of wooden ones.</summary>
        public static IReadOnlyList<ThingDef> EquivalentsOf(ThingDef def) =>
            IsSculpture(def) ? AllSculptureDefs : new[] { def };

        /// <summary>
        /// The sculpture the settlement should carve next on <paramref name="map"/>: the most beautiful
        /// material it has a whole sculpture's worth of, or the wooden <see cref="ArtThingDefOf.Sculpture"/>
        /// when it has none. "A whole sculpture's worth" is the Def's own
        /// <see cref="Defs.ThingDef.costList"/> rather than a number written here, for the reason
        /// <see cref="StoneWallMaterials.PreferredWallDef"/> gives: a blueprint the settlement cannot finish
        /// would sit on a cell forever, counting toward a want nobody is filling.
        /// </summary>
        public static ThingDef PreferredSculptureDef(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            IReadOnlyList<ThingDef> art = AllSculptureDefs;
            for (int i = 0; i < art.Count; i++)
            {
                ThingDef candidate = art[i];
                if (ReferenceEquals(candidate, ArtThingDefOf.Sculpture)) continue;
                if (!candidate.IsResearchFinished) continue;
                if (CanAffordOne(map, candidate)) return candidate;
            }
            return ArtThingDefOf.Sculpture;
        }

        /// <summary>
        /// How many of <paramref name="sculptureDef"/> a settlement wants standing together.
        ///
        /// <para/><b>Derived, and here is the derivation.</b> A citizen's beauty is the <i>average</i>
        /// <see cref="BeautyStatDefOf.Beauty"/> of the cells they perceive
        /// (<see cref="BeautyUtility.AverageBeautyPerceptible"/>), which outdoors is the
        /// <see cref="BeautyUtility.SampleRadius"/> disc — <see cref="GenRadial.NumCellsInRadius"/> puts 149
        /// cells in it. <see cref="Need_Beauty.LevelFromBeautyCurve"/> marks the first step above neutral at
        /// its first positive point, so lifting a citizen's surroundings one band above "nothing here either
        /// way" takes that point's beauty × those cells, and one sculpture supplies its own Beauty stat. Hence
        /// <c>ceil(step × cells ÷ beautyOfOne)</c>: twelve of the wooden sculpture (25 beauty), five of the
        /// gold one (60). Every input is read live off the sampler, the curve and the content, so retuning any
        /// of those three moves this and no literal has to be found and changed.
        ///
        /// <para/><b>No population term, and that is the arithmetic talking.</b> Beauty is a property of a
        /// <i>place</i>, not of a headcount: the same five sculptures read identically to the tenth citizen who
        /// walks past them and to the thousandth, because the average over the cells does not divide by the
        /// people looking. A settlement of two thousand therefore wants exactly what a settlement of twenty
        /// wants, and scaling this with <c>Settlement.Citizens</c> would be a constant invented to solve a
        /// shortage the arithmetic says does not exist — the same conclusion <c>StonecuttingTuning</c> reached
        /// for bench counts, by the same route. What a civilization-scale settlement would really want is
        /// <i>several</i> such clusters, one per quarter of the town; that needs a notion of districts this
        /// port does not have, and is named in this lane's report rather than faked with a multiplier.
        ///
        /// <para/>Zero when the Def has no beauty at all, which is the honest answer to "how many of these
        /// should we carve" for something nobody would look at.
        /// </summary>
        public static int SculpturesWantedOf(ThingDef sculptureDef)
        {
            if (sculptureDef == null) throw new ArgumentNullException(nameof(sculptureDef));

            float beautyOfOne = sculptureDef.GetStatValueAbstract(BeautyStatDefOf.Beauty);
            if (beautyOfOne <= 0f) return 0;

            float step = FirstStepAboveNeutral();
            if (step <= 0f) return 0;

            int cells = GenRadial.NumCellsInRadius(BeautyUtility.SampleRadius);
            return (int)Math.Ceiling(step * cells / beautyOfOne);
        }

        /// <summary>
        /// The average perceptible beauty at which <see cref="Need_Beauty.LevelFromBeautyCurve"/> first says
        /// something better than neutral — its first point above zero beauty. Read off the curve rather than
        /// restated as a literal here: the curve's own doc records that those cut points are SimWorld's own
        /// and are pinned as an ordering, so a lane retuning them must not have to know this file exists.
        /// </summary>
        public static float FirstStepAboveNeutral()
        {
            SimpleCurve curve = Need_Beauty.LevelFromBeautyCurve;
            for (int i = 0; i < curve.PointsCount; i++)
            {
                if (curve[i].x > 0f) return curve[i].x;
            }
            return 0f;
        }

        /// <summary>Whether every entry of <paramref name="sculptureDef"/>'s cost is on the map in full,
        /// counting loose stacks wherever they lie — exactly as
        /// <see cref="WorkGiver_ConstructDeliverResources"/> does when it goes looking for them.</summary>
        private static bool CanAffordOne(Map.Map map, ThingDef sculptureDef)
        {
            List<ThingDefCountClass>? cost = sculptureDef.costList;
            if (cost == null || cost.Count == 0) return false;

            for (int i = 0; i < cost.Count; i++)
            {
                int have = 0;
                IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(cost[i].thingDef);
                for (int s = 0; s < stacks.Count; s++)
                {
                    if (stacks[s].Spawned) have += stacks[s].stackCount;
                }
                if (have < cost[i].count) return false;
            }
            return true;
        }

        private static int ByBeautyThenName(ThingDef a, ThingDef b)
        {
            int byBeauty = b.GetStatValueAbstract(BeautyStatDefOf.Beauty)
                .CompareTo(a.GetStatValueAbstract(BeautyStatDefOf.Beauty));
            return byBeauty != 0 ? byBeauty : string.CompareOrdinal(a.defName, b.defName);
        }
    }
}
