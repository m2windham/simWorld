using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;

namespace SimWorld.Tests.Wiring
{
    /// <summary>What one content-settable field looks like across everything the shipped content loaded.</summary>
    public sealed class FieldUsage
    {
        public FieldUsage(FieldInfo field)
        {
            Field = field;
        }

        public FieldInfo Field { get; }

        /// <summary>How many loaded objects carry this field.</summary>
        public int Instances { get; internal set; }

        /// <summary>Some loaded object differs from a freshly constructed one — content, or code, reached it.</summary>
        public bool SetByContent { get; internal set; }

        /// <summary>Some loaded object holds anything other than null / false / zero / an empty collection.</summary>
        public bool EverNonDefault { get; internal set; }

        public string Id => (Field.DeclaringType?.FullName ?? "?") + "." + Field.Name;
    }

    /// <summary>
    /// A walk of everything the shipped content actually loaded — every Def and every object hanging off one
    /// (<c>RaceProperties</c>, <c>HediffStage</c>, each <c>CompProperties</c>, every <c>TraitDegreeData</c>) —
    /// recording for each content field whether anything ever set it, and what the loader builds.
    ///
    /// <para/>Walking the loaded objects rather than parsing the XML is what makes the answer trustworthy: a
    /// value written by <c>ResolveReferences</c> counts as reached, which it is, and inheritance through
    /// <c>ParentName</c> needs no special handling because the merged result is what the simulation will see.
    ///
    /// <para/>The set of types met on the walk is worth as much as the field data. Those types are built by
    /// the XML loader through reflection, so "no line of <c>src/</c> ever constructs one" says nothing about
    /// them — the wiring checks that look for a missing writer have to know to stay quiet there.
    /// </summary>
    public sealed class ContentSurvey
    {
        private readonly Dictionary<FieldInfo, FieldUsage> fields = new Dictionary<FieldInfo, FieldUsage>();
        private readonly Dictionary<Type, object?> blanks = new Dictionary<Type, object?>();
        private readonly HashSet<Type> loaderBuilt = new HashSet<Type>();
        private readonly HashSet<Type> declaredContentTypes = new HashSet<Type>();
        private readonly HashSet<object> visited = new HashSet<object>(ReferenceComparer.Instance);
        private readonly Assembly core;

        private ContentSurvey(Assembly core)
        {
            this.core = core;
        }

        /// <summary>Every content field met on the walk.</summary>
        public IEnumerable<FieldUsage> Fields => fields.Values;

        /// <summary>Full names of every type shipped content names as a worker/driver/class.</summary>
        public ISet<string> WorkerTypeNames { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <param name="defs">Every loaded Def; each is a root of the walk.</param>
        /// <param name="scope">Assembly whose types the walk descends into. Defaults to the core; the audit's
        /// own tests pass their fixture assembly so the checks can be exercised on a content set they control.</param>
        public static ContentSurvey Of(IEnumerable<Def> defs, Assembly? scope = null)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            var survey = new ContentSurvey(scope ?? typeof(Def).Assembly);
            foreach (Def def in defs) survey.Visit(def, 0);
            return survey;
        }

        /// <summary>True when the loader, not a line of code, is responsible for filling this type in.</summary>
        public bool IsContentLoaded(Type type)
        {
            for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (loaderBuilt.Contains(t) || declaredContentTypes.Contains(t)) return true;
            }
            return false;
        }

        private void Visit(object obj, int depth)
        {
            const int MaxDepth = 8;
            if (depth > MaxDepth) return;

            Type type = obj.GetType();
            if (type.Assembly != core) return;
            loaderBuilt.Add(type);

            object? blank = Blank(type);
            foreach (FieldInfo field in WiringAudit.ContentFields(type))
            {
                declaredContentTypes.Add(field.FieldType);
                foreach (Type argument in field.FieldType.IsGenericType ? field.FieldType.GetGenericArguments() : Type.EmptyTypes)
                {
                    declaredContentTypes.Add(argument);
                }

                object? value = field.GetValue(obj);
                FieldUsage usage = UsageFor(field);
                usage.Instances++;
                if (blank != null && !SameValue(value, field.GetValue(blank))) usage.SetByContent = true;
                if (!IsTypeDefault(value)) usage.EverNonDefault = true;

                if (field.FieldType == typeof(Type) && value is Type named && named.FullName != null)
                {
                    WorkerTypeNames.Add(named.FullName);
                }

                VisitValue(value, depth + 1);
            }
        }

        private void VisitValue(object? value, int depth)
        {
            if (value == null) return;
            if (value is Def || value is Type || value is string) return;

            if (value is IEnumerable items)
            {
                foreach (object? item in items) VisitValue(item, depth + 1);
                return;
            }

            Type type = value.GetType();
            if (type.IsValueType || type.Assembly != core) return;   // ranges and curves are leaf data
            if (!visited.Add(value)) return;
            Visit(value, depth);
        }

        private FieldUsage UsageFor(FieldInfo field)
        {
            FieldUsage? usage;
            if (!fields.TryGetValue(field, out usage))
            {
                usage = new FieldUsage(field);
                fields.Add(field, usage);
            }
            return usage;
        }

        private object? Blank(Type type)
        {
            object? blank;
            if (blanks.TryGetValue(type, out blank)) return blank;
            try
            {
                blank = Activator.CreateInstance(type);
            }
            catch (Exception)
            {
                // No usable parameterless constructor: there is no "unset" to compare against. Skipping the
                // type loses a check; inventing a default would lose the truth.
                blank = null;
            }
            blanks.Add(type, blank);
            return blank;
        }

        /// <summary>Whether a loaded value still matches a freshly built object of the same type.</summary>
        internal static bool SameValue(object? loaded, object? blank)
        {
            if (loaded == null || blank == null) return loaded == null && blank == null;

            var loadedItems = loaded as ICollection;
            var blankItems = blank as ICollection;
            if (loadedItems != null && blankItems != null) return loadedItems.Count == blankItems.Count;
            if (loadedItems != null) return loadedItems.Count == 0;

            return Equals(loaded, blank);
        }

        /// <summary>Null, false, zero, an empty string or an empty collection — the value a field has when nobody speaks.</summary>
        internal static bool IsTypeDefault(object? value)
        {
            if (value == null) return true;
            if (value is string text) return text.Length == 0;
            if (value is ICollection items) return items.Count == 0;

            Type type = value.GetType();
            if (!type.IsValueType) return false;
            return value.Equals(Activator.CreateInstance(type));
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
