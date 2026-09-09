using System;
using SimWorld.Crafting;
using SimWorld.Defs;

namespace SimWorld.Things
{
    /// <summary>Constructs a fresh, unspawned Thing from a ThingDef (RimWorld: <c>Verse.ThingMaker</c>).</summary>
    public static class ThingMaker
    {
        /// <summary>
        /// <paramref name="stuff"/> names the material (steel, wood, ...) for a <c>def.MadeFromStuff</c> Thing —
        /// set via <see cref="Thing.SetStuffDirect"/> so it flows into <see cref="Stats.StatRequest.For(Thing)"/>.
        /// <paramref name="quality"/> is applied to a freshly-added <see cref="CompQuality"/> when the def
        /// carries one (RimWorld folds this into the same call rather than a separate step, since quality is
        /// only ever known at the moment of creation — a recipe's roll, a trader's stock, map generation).
        /// </summary>
        public static Thing MakeThing(ThingDef def, ThingDef? stuff = null, QualityCategory? quality = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.thingClass == null)
            {
                throw new InvalidOperationException("ThingDef " + def.defName + " has no thingClass.");
            }
            if (!typeof(Thing).IsAssignableFrom(def.thingClass))
            {
                throw new InvalidOperationException("ThingDef " + def.defName + "'s thingClass " + def.thingClass + " does not derive from Thing.");
            }

            var thing = (Thing)Activator.CreateInstance(def.thingClass)!;
            thing.def = def;
            thing.thingIDNumber = Thing.AllocateThingId();
            if (stuff != null) thing.SetStuffDirect(stuff);
            thing.PostMake();
            if (quality.HasValue && thing is ThingWithComps twc)
            {
                twc.GetComp<CompQuality>()?.SetQuality(quality.Value);
            }
            return thing;
        }
    }
}
