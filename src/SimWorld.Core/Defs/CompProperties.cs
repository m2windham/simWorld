using System;
using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>
    /// Data half of the Comp pattern (RimWorld: <c>Verse.CompProperties</c>): a ThingDef composes behaviour
    /// from a list of these, each naming the runtime component class to instantiate per Thing.
    /// Subclasses add tunables and set <see cref="compClass"/> in their constructor. XML:
    /// <code>&lt;comps&gt;&lt;li Class="CompProperties_Power"&gt;&lt;basePowerConsumption&gt;100&lt;/basePowerConsumption&gt;&lt;/li&gt;&lt;/comps&gt;</code>
    /// </summary>
    public class CompProperties
    {
        /// <summary>Runtime component type created for each Thing using this Def.</summary>
        public Type? compClass;

        public CompProperties()
        {
        }

        public CompProperties(Type compClass)
        {
            this.compClass = compClass ?? throw new ArgumentNullException(nameof(compClass));
        }

        /// <summary>Called after all cross-references resolve; <paramref name="parentDef"/> owns this entry.</summary>
        public virtual void ResolveReferences(ThingDef parentDef)
        {
        }

        public virtual IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            if (compClass == null)
            {
                yield return GetType().Name + " has no compClass.";
            }
        }
    }
}
