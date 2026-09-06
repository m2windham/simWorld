using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace SimWorld.Defs
{
    /// <summary>
    /// Deferred Def-to-Def references collected while objects are built from XML and resolved once
    /// every Def is registered (RimWorld: <c>Verse.DirectXmlCrossRefLoader</c>). Content can therefore
    /// reference Defs defined later or in other files.
    /// </summary>
    public sealed class CrossRefRegistry
    {
        private abstract class WantedRef
        {
            public readonly Type DefType;
            public readonly string DefName;
            public readonly string Context;
            public readonly string? File;

            protected WantedRef(Type defType, string defName, string context, string? file)
            {
                DefType = defType;
                DefName = defName;
                Context = context;
                File = file;
            }

            public abstract void Apply(Def def);
        }

        private sealed class FieldRef : WantedRef
        {
            private readonly object target;
            private readonly FieldInfo field;

            public FieldRef(object target, FieldInfo field, string defName, string context, string? file)
                : base(field.FieldType, defName, context, file)
            {
                this.target = target;
                this.field = field;
            }

            public override void Apply(Def def) => field.SetValue(target, def);
        }

        private sealed class ListRef : WantedRef
        {
            private readonly IList list;
            private readonly int index;

            public ListRef(IList list, int index, Type defType, string defName, string context, string? file)
                : base(defType, defName, context, file)
            {
                this.list = list;
                this.index = index;
            }

            public override void Apply(Def def) => list[index] = def;
        }

        private sealed class DictValueRef : WantedRef
        {
            private readonly IDictionary dictionary;
            private readonly object key;

            public DictValueRef(IDictionary dictionary, object key, Type defType, string defName, string context, string? file)
                : base(defType, defName, context, file)
            {
                this.dictionary = dictionary;
                this.key = key;
            }

            public override void Apply(Def def) => dictionary[key] = def;
        }

        private readonly List<WantedRef> wanted = new List<WantedRef>();

        public int PendingCount => wanted.Count;

        public void WantField(object target, FieldInfo field, string defName, string context, string? file)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (!typeof(Def).IsAssignableFrom(field.FieldType))
            {
                throw new ArgumentException("Field " + field.Name + " is not Def-typed.", nameof(field));
            }
            wanted.Add(new FieldRef(target, field, defName, context, file));
        }

        public void WantListItem(IList list, int index, Type defType, string defName, string context, string? file)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            wanted.Add(new ListRef(list, index, defType, defName, context, file));
        }

        public void WantDictionaryValue(IDictionary dictionary, object key, Type defType, string defName, string context, string? file)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));
            if (key == null) throw new ArgumentNullException(nameof(key));
            wanted.Add(new DictValueRef(dictionary, key, defType, defName, context, file));
        }

        /// <summary>Applies every pending reference from <paramref name="database"/>; unresolvable ones become errors.</summary>
        public void ResolveAll(DefDatabase database, List<DefLoadError> errors)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            foreach (WantedRef want in wanted)
            {
                Def? def = database.GetNamed(want.DefType, want.DefName);
                if (def == null)
                {
                    errors.Add(new DefLoadError(
                        "Could not resolve cross-reference: no " + want.DefType.Name + " named '" + want.DefName + "' found to give to " + want.Context + ".",
                        want.File));
                    continue;
                }
                want.Apply(def);
            }
            wanted.Clear();
        }
    }
}
