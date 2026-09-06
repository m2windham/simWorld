using System;
using System.Collections.Generic;
using System.Reflection;

namespace SimWorld.Defs
{
    /// <summary>
    /// Maps names in content files to CLR types (RimWorld: <c>Verse.GenTypes</c>):
    /// a Def element name (<c>&lt;ThingDef&gt;</c>), a <c>Class="..."</c> attribute, or a
    /// <c>Type</c>-valued field such as <c>compClass</c>. Searches the registered assemblies in
    /// registration order; the core assembly is always first.
    /// </summary>
    public sealed class DefTypeResolver
    {
        private readonly List<Assembly> assemblies = new List<Assembly>();
        private readonly Dictionary<string, Type?> typeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);
        private readonly Dictionary<string, Type?> defTypeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);
        private List<Type>? allTypes;

        public DefTypeResolver()
        {
            AddAssembly(typeof(Def).Assembly);
        }

        public IReadOnlyList<Assembly> Assemblies => assemblies;

        /// <summary>Registers an assembly to search (mods, the Unity host, tests). Idempotent.</summary>
        public DefTypeResolver AddAssembly(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (!assemblies.Contains(assembly))
            {
                assemblies.Add(assembly);
                typeCache.Clear();
                defTypeCache.Clear();
                allTypes = null;
            }
            return this;
        }

        /// <summary>Every loadable type in the registered assemblies, in a stable order.</summary>
        public IReadOnlyList<Type> AllTypes
        {
            get
            {
                if (allTypes == null)
                {
                    var list = new List<Type>();
                    foreach (Assembly assembly in assemblies)
                    {
                        list.AddRange(SafeGetTypes(assembly));
                    }
                    allTypes = list;
                }
                return allTypes;
            }
        }

        /// <summary>Types carrying <typeparamref name="TAttribute"/>.</summary>
        public IEnumerable<Type> AllTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        {
            foreach (Type type in AllTypes)
            {
                if (type.IsDefined(typeof(TAttribute), inherit: false))
                {
                    yield return type;
                }
            }
        }

        /// <summary>Def subclass named by an XML element (<c>ThingDef</c> or a full name). Null when unknown.</summary>
        public Type? GetDefType(string elementName)
        {
            if (elementName == null) throw new ArgumentNullException(nameof(elementName));
            if (defTypeCache.TryGetValue(elementName, out Type? cached))
            {
                return cached;
            }
            Type? found = null;
            foreach (Type type in AllTypes)
            {
                if (!typeof(Def).IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }
                if (type.Name == elementName || type.FullName == elementName)
                {
                    found = type;
                    break;
                }
            }
            defTypeCache[elementName] = found;
            return found;
        }

        /// <summary>
        /// Any type by short name, full name, or assembly-qualified name. Short names resolve to the
        /// first match in registration order.
        /// </summary>
        public Type? GetTypeInAnyAssembly(string typeName)
        {
            if (typeName == null) throw new ArgumentNullException(nameof(typeName));
            string name = typeName.Trim();
            if (typeCache.TryGetValue(name, out Type? cached))
            {
                return cached;
            }

            Type? found = Type.GetType(name, throwOnError: false);
            if (found == null)
            {
                foreach (Assembly assembly in assemblies)
                {
                    found = assembly.GetType(name, throwOnError: false);
                    if (found != null) break;
                }
            }
            if (found == null)
            {
                foreach (Type type in AllTypes)
                {
                    if (type.Name == name)
                    {
                        found = type;
                        break;
                    }
                }
            }
            typeCache[name] = found;
            return found;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                var loaded = new List<Type>();
                foreach (Type? t in e.Types)
                {
                    if (t != null) loaded.Add(t);
                }
                types = loaded.ToArray();
            }
            return types;
        }
    }
}
