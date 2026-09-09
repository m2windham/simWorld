using SimWorld.Things;

namespace SimWorld.Stats
{
    /// <summary>
    /// Multiplies a stat by a curve keyed on <see cref="Crafting.QualityCategory"/> (RimWorld:
    /// <c>RimWorld.StatPart_Quality</c>), 0 = Awful through 6 = Legendary. The first concrete <see cref="StatPart"/>
    /// this module ships — see that class's own remarks for why it took a live <see cref="CompQuality"/> on a
    /// spawned Thing to get here. A request with no Thing, or a Thing with no <see cref="CompQuality"/> (most
    /// Things: buildings, raw resources, meals), passes through unchanged.
    /// </summary>
    public class StatPart_Quality : StatPart
    {
        /// <summary>Quality-index (0..6) → multiplier. Content-authored per stat: MarketValue's curve is not
        /// sourced from RimWorld's exact per-stat tables — it only needs to read as "worse craftsmanship is
        /// worth less, better is worth more," which is what <c>QualityStatTests</c> pins.</summary>
        public SimpleCurve curve = new SimpleCurve();

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!req.HasThing || !(req.Thing is ThingWithComps twc)) return;
            CompQuality? comp = twc.TryGetComp<CompQuality>();
            if (comp == null) return;
            val *= curve.Evaluate((float)comp.Quality);
        }
    }
}
