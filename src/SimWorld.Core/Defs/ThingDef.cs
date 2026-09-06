using System;
using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>Broad kind of Thing a ThingDef describes (RimWorld: <c>Verse.ThingCategory</c>).</summary>
    public enum ThingCategory
    {
        None,
        Item,
        Pawn,
        Building,
        Plant,
        Projectile,
        Filth,
        Gas,
        Attachment,
        Mote,
        Ethereal,
    }

    /// <summary>
    /// Definition of a placeable/spawnable Thing (RimWorld: <c>Verse.ThingDef</c>). Deliberately minimal at
    /// this stage: the Things module extends it. What is here is the Def-layer contract every later
    /// module builds on — stat bases and the Comp list.
    /// </summary>
    public class ThingDef : Def
    {
        /// <summary>Runtime class spawned for this Def; resolved by name from XML.</summary>
        public Type? thingClass;

        public ThingCategory category;

        /// <summary>Which tick list instances of this Def join; see <see cref="Sim.TickManager"/>.</summary>
        public Sim.TickerType tickerType = Sim.TickerType.Never;

        /// <summary>Base stat values, keyed by StatDef. See <see cref="StatModifier"/> for the XML idiom.</summary>
        public List<StatModifier>? statBases;

        /// <summary>Composable behaviour; each entry becomes one runtime component per Thing.</summary>
        public List<CompProperties>? comps;

        /// <summary>Base value of <paramref name="stat"/> from <see cref="statBases"/>, else the stat's default.</summary>
        public float GetStatValueAbstract(StatDef stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            if (statBases != null)
            {
                for (int i = 0; i < statBases.Count; i++)
                {
                    if (ReferenceEquals(statBases[i].stat, stat))
                    {
                        return statBases[i].value;
                    }
                }
            }
            return stat.defaultBaseValue;
        }

        public bool StatBaseDefined(StatDef stat)
        {
            if (statBases == null) return false;
            for (int i = 0; i < statBases.Count; i++)
            {
                if (ReferenceEquals(statBases[i].stat, stat)) return true;
            }
            return false;
        }

        public T? GetCompProperties<T>() where T : CompProperties
        {
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is T typed) return typed;
                }
            }
            return null;
        }

        public bool HasComp(Type compClass)
        {
            if (compClass == null) throw new ArgumentNullException(nameof(compClass));
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i].compClass == compClass) return true;
                }
            }
            return false;
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    comps[i].ResolveReferences(this);
                }
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (statBases != null)
            {
                var seen = new HashSet<StatDef>();
                foreach (StatModifier modifier in statBases)
                {
                    if (modifier.stat == null)
                    {
                        yield return "statBases entry has no stat.";
                    }
                    else if (!seen.Add(modifier.stat))
                    {
                        yield return "statBases defines " + modifier.stat.defName + " more than once.";
                    }
                }
            }
            if (comps != null)
            {
                foreach (CompProperties comp in comps)
                {
                    foreach (string error in comp.ConfigErrors(this))
                    {
                        yield return error;
                    }
                }
            }
        }
    }
}
