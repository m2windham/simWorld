using System.Collections.Generic;
using System.Globalization;

namespace SimWorld.Defs
{
    /// <summary>
    /// Base of every data definition (RimWorld: <c>Verse.Def</c>). Public fields are populated
    /// from XML by name; lifecycle hooks run in the order
    /// <see cref="PostLoad"/> → cross-reference resolution → <see cref="ResolveReferences"/> → <see cref="ConfigErrors"/>.
    /// </summary>
    public class Def
    {
        public const string DefaultDefName = "UnnamedDef";

        /// <summary>Unique within its Def type. Referenced from other Defs by this name.</summary>
        public string defName = DefaultDefName;

        /// <summary>Human-readable name; falls back to defName.</summary>
        public string? label;

        public string? description;

        /// <summary>Position in the database for this Def's concrete type; assigned on registration.</summary>
        [Unsaved] public ushort index;

        /// <summary>Content file this Def was loaded from, when known.</summary>
        [Unsaved(true)] public string? fileName;

        /// <summary>Content pack (core, expansion, mod) this Def was loaded from.</summary>
        [Unsaved(true)] public string? packName;

        /// <summary>Suppresses <see cref="ConfigErrors"/> reporting for this Def.</summary>
        public bool ignoreConfigErrors;

        public string LabelCap
        {
            get
            {
                string s = label ?? defName;
                if (s.Length == 0) return s;
                return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s.Substring(1);
            }
        }

        /// <summary>Called immediately after the object is populated from XML, before cross-references resolve.</summary>
        public virtual void PostLoad()
        {
        }

        /// <summary>Called after every cross-reference has been resolved; safe to read other Defs here.</summary>
        public virtual void ResolveReferences()
        {
        }

        /// <summary>Yields validation errors. Runs last; every other Def is resolved by then.</summary>
        public virtual IEnumerable<string> ConfigErrors()
        {
            if (defName == DefaultDefName)
            {
                yield return "defName is missing (" + DefaultDefName + ").";
            }
        }

        /// <summary>Drops any derived, cached state so it is recomputed on next use.</summary>
        public virtual void ClearCachedData()
        {
        }

        public override string ToString() => defName;
    }
}
