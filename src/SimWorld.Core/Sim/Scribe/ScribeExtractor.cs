using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Sim
{
    /// <summary>Shared read/write primitives for the <c>Scribe_*</c> helpers (RimWorld: <c>Verse.ScribeExtractor</c>).</summary>
    internal static class ScribeExtractor
    {
        private static readonly Dictionary<string, Type?> TypeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);
        private static readonly object TypeGate = new object();

        public static bool IsNull(XElement node) => XmlInheritance.IsTrue(node.Attribute("IsNull")?.Value);

        /// <summary>Invariant text for a saved value; parsable back by <see cref="ParseHelper"/>.</summary>
        public static string ValueToString(object value)
        {
            switch (value)
            {
                case string s: return s;
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case bool b: return b ? "True" : "False";
                case Enum e: return e.ToString();
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? "";
            }
        }

        public static T ValueFromNode<T>(XElement? node, T defaultValue, string label, ScribeLoader loader)
        {
            if (node == null) return defaultValue;
            if (IsNull(node)) return default!;
            if (typeof(T) == typeof(string)) return (T)(object)node.Value;
            try
            {
                return (T)ParseHelper.FromString(node.Value, typeof(T))!;
            }
            catch (Exception e)
            {
                loader.Error("Could not parse '" + node.Value + "' as " + typeof(T).Name + " for '" + label + "': " + e.Message);
                return defaultValue;
            }
        }

        public static T? DefFromNode<T>(XElement? node, string label, ScribeLoader loader) where T : Def
        {
            if (node == null) return null;
            string name = node.Value.Trim();
            if (name.Length == 0 || name == "null" || IsNull(node)) return null;
            var def = loader.Defs.GetNamed(typeof(T), name) as T;
            if (def == null)
            {
                loader.Error("Could not load reference to " + typeof(T).Name + " named '" + name + "' for '" + label + "'.");
            }
            return def;
        }

        /// <summary>
        /// Builds a deep-saved object: picks the type (a <c>Class</c> attribute or <typeparamref name="T"/>),
        /// constructs it, runs its LoadingVars pass, and registers it for the later passes.
        /// </summary>
        public static T? SaveableFromNode<T>(XElement? node, object[]? ctorArgs, string label, ScribeLoader loader) where T : IExposable
        {
            if (node == null) return default;
            if (IsNull(node)) return default;

            Type type = typeof(T);
            string? className = node.Attribute("Class")?.Value;
            if (className != null)
            {
                Type? resolved = ResolveType(className);
                if (resolved == null)
                {
                    loader.Error("Could not find type '" + className + "' for '" + label + "'.");
                    return default;
                }
                if (!typeof(T).IsAssignableFrom(resolved))
                {
                    loader.Error("Type '" + className + "' is not a " + typeof(T).Name + " (for '" + label + "').");
                    return default;
                }
                type = resolved;
            }
            if (type.IsAbstract || type.IsInterface)
            {
                loader.Error("Cannot instantiate " + type.Name + " for '" + label + "'; the save has no Class attribute.");
                return default;
            }

            T instance;
            try
            {
                object created = ctorArgs != null && ctorArgs.Length > 0
                    ? Activator.CreateInstance(type, ctorArgs)!
                    : Activator.CreateInstance(type, nonPublic: true)!;
                instance = (T)created;
            }
            catch (Exception e)
            {
                loader.Error("Could not construct " + type.Name + " for '" + label + "': " + (e.InnerException ?? e).Message);
                return default;
            }

            IExposable? previous = loader.CurExposable;
            loader.PushParent(node);
            loader.CurExposable = instance;
            try
            {
                instance.ExposeData();
            }
            finally
            {
                loader.CurExposable = previous;
                loader.PopParent();
            }

            if (instance is ILoadReferenceable referenceable)
            {
                loader.crossRefs.loadedObjectDirectory.RegisterLoaded(referenceable, loader);
            }
            loader.crossRefs.RegisterForCrossRefResolution(instance);
            loader.initer.RegisterForPostLoadInit(instance);
            return instance;
        }

        /// <summary>Type by full name across every loaded assembly, cached.</summary>
        public static Type? ResolveType(string typeName)
        {
            lock (TypeGate)
            {
                if (TypeCache.TryGetValue(typeName, out Type? cached)) return cached;
            }
            Type? found = Type.GetType(typeName, throwOnError: false);
            if (found == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    found = assembly.GetType(typeName, throwOnError: false);
                    if (found != null) break;
                }
            }
            lock (TypeGate)
            {
                TypeCache[typeName] = found;
            }
            return found;
        }

        public static LookMode ResolveLookMode(Type type, LookMode requested, string label, ScribeLoader? loader)
        {
            if (requested != LookMode.Undefined) return requested;
            if (typeof(Def).IsAssignableFrom(type)) return LookMode.Def;
            if (typeof(IExposable).IsAssignableFrom(type)) return LookMode.Deep;
            if (type == typeof(string) || ParseHelper.CanParse(type)) return LookMode.Value;
            string message = "Cannot infer LookMode for " + type.Name + " in '" + label + "'; pass one explicitly (references always need LookMode.Reference).";
            if (loader != null)
            {
                loader.Error(message);
                return LookMode.Undefined;
            }
            throw new ScribeException(message);
        }
    }
}
