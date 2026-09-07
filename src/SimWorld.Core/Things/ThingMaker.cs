using System;
using SimWorld.Defs;

namespace SimWorld.Things
{
    /// <summary>Constructs a fresh, unspawned Thing from a ThingDef (RimWorld: <c>Verse.ThingMaker</c>).</summary>
    public static class ThingMaker
    {
        /// <summary>
        /// <paramref name="stuff"/> names the material (steel, wood, ...) once a Stuff system exists; accepted
        /// now so call sites do not need to change later, but not yet applied to anything.
        /// </summary>
        public static Thing MakeThing(ThingDef def, ThingDef? stuff = null)
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
            thing.PostMake();
            return thing;
        }
    }
}
