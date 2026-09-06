using System;
using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>What to do when a second Def with the same defName is added to the same type.</summary>
    public enum DuplicateDefPolicy
    {
        /// <summary>Report an error and keep the first.</summary>
        Error,
        /// <summary>Later content replaces earlier content (mod load order semantics).</summary>
        LastWins,
        /// <summary>Silently keep the first.</summary>
        FirstWins,
    }

    /// <summary>Untyped view of one per-type registry.</summary>
    public interface IDefRegistry
    {
        Type DefType { get; }
        int Count { get; }
        IEnumerable<Def> AllDefsUntyped { get; }
        Def? GetNamedUntyped(string defName);
        bool ContainsName(string defName);
    }

    /// <summary>Defs of one type (and its subclasses), in registration order.</summary>
    public sealed class DefRegistry<T> : IDefRegistry where T : Def
    {
        private readonly List<T> defsList = new List<T>();
        private readonly Dictionary<string, T> defsByName = new Dictionary<string, T>(StringComparer.Ordinal);

        public Type DefType => typeof(T);
        public int Count => defsList.Count;
        public IReadOnlyList<T> AllDefsListForReading => defsList;
        public IEnumerable<T> AllDefs => defsList;
        IEnumerable<Def> IDefRegistry.AllDefsUntyped => defsList;

        public bool ContainsName(string defName) => defsByName.ContainsKey(defName);

        public T? GetNamedSilentFail(string defName)
        {
            if (defName == null) throw new ArgumentNullException(nameof(defName));
            return defsByName.TryGetValue(defName, out T def) ? def : null;
        }

        /// <summary>Throws <see cref="KeyNotFoundException"/> when absent.</summary>
        public T GetNamed(string defName)
        {
            T? def = GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new KeyNotFoundException("No " + typeof(T).Name + " named '" + defName + "' is loaded.");
            }
            return def;
        }

        Def? IDefRegistry.GetNamedUntyped(string defName) => GetNamedSilentFail(defName);

        internal void AddInternal(T def)
        {
            defsList.Add(def);
            defsByName[def.defName] = def;
        }

        internal bool RemoveInternal(Def def)
        {
            if (def is T typed && defsList.Remove(typed))
            {
                if (defsByName.TryGetValue(typed.defName, out T current) && ReferenceEquals(current, typed))
                {
                    defsByName.Remove(typed.defName);
                }
                return true;
            }
            return false;
        }

        internal void ClearInternal()
        {
            defsList.Clear();
            defsByName.Clear();
        }

        /// <summary>Reassigns <see cref="Def.index"/> to match list order (after removals).</summary>
        internal void ReindexInternal()
        {
            for (int i = 0; i < defsList.Count; i++)
            {
                defsList[i].index = checked((ushort)i);
            }
        }
    }

    /// <summary>
    /// Every loaded Def, indexed by type and defName (RimWorld: <c>Verse.DefDatabase&lt;T&gt;</c>).
    /// A Def is visible from the registry of its concrete type and every Def-derived base type,
    /// so <c>For&lt;ThingDef&gt;()</c> also returns instances of ThingDef subclasses.
    /// <see cref="Global"/> backs the static <see cref="DefDatabase{T}"/> facade; create instances for
    /// isolated content sets (tests, tools, alternate worlds).
    /// </summary>
    public sealed class DefDatabase
    {
        private static DefDatabase global = new DefDatabase();

        /// <summary>The database the static <see cref="DefDatabase{T}"/> facade reads. Swappable.</summary>
        public static DefDatabase Global
        {
            get => global;
            set => global = value ?? throw new ArgumentNullException(nameof(value));
        }

        private readonly Dictionary<Type, IDefRegistry> registries = new Dictionary<Type, IDefRegistry>();

        /// <summary>Registry for <typeparamref name="T"/> and its subclasses; created on first use.</summary>
        public DefRegistry<T> For<T>() where T : Def => (DefRegistry<T>)RegistryFor(typeof(T));

        /// <summary>All Defs of every type, in registration order.</summary>
        public IEnumerable<Def> AllDefs => For<Def>().AllDefs;

        public int DefCount => For<Def>().Count;

        /// <summary>Untyped lookup: the Def of (or derived from) <paramref name="defType"/> with this name.</summary>
        public Def? GetNamed(Type defType, string defName)
        {
            if (defType == null) throw new ArgumentNullException(nameof(defType));
            if (!typeof(Def).IsAssignableFrom(defType))
            {
                throw new ArgumentException(defType.FullName + " is not a Def type.", nameof(defType));
            }
            return registries.TryGetValue(defType, out IDefRegistry registry) ? registry.GetNamedUntyped(defName) : null;
        }

        public T GetNamed<T>(string defName) where T : Def => For<T>().GetNamed(defName);

        public T? GetNamedSilentFail<T>(string defName) where T : Def => For<T>().GetNamedSilentFail(defName);

        /// <summary>Registers with <see cref="DuplicateDefPolicy.LastWins"/>. Throws on a duplicate under Error policy.</summary>
        public void Add(Def def)
        {
            if (!Add(def, DuplicateDefPolicy.LastWins, out string? error) && error != null)
            {
                throw new InvalidOperationException(error);
            }
        }

        /// <summary>
        /// Registers <paramref name="def"/> in the registry of its concrete type and every Def-derived
        /// base type. Returns false when the Def was not added (a duplicate under Error/FirstWins).
        /// </summary>
        public bool Add(Def def, DuplicateDefPolicy policy, out string? error)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            error = null;
            Type concrete = def.GetType();
            IDefRegistry concreteRegistry = RegistryFor(concrete);

            if (concreteRegistry.ContainsName(def.defName))
            {
                switch (policy)
                {
                    case DuplicateDefPolicy.Error:
                        error = "Duplicate " + concrete.Name + " defName '" + def.defName + "'.";
                        return false;
                    case DuplicateDefPolicy.FirstWins:
                        return false;
                    case DuplicateDefPolicy.LastWins:
                        Def existing = concreteRegistry.GetNamedUntyped(def.defName)!;
                        Remove(existing);
                        break;
                }
            }

            for (Type? t = concrete; t != null && typeof(Def).IsAssignableFrom(t); t = t.BaseType)
            {
                InvokeAdd(RegistryFor(t), def);
            }
            def.index = checked((ushort)(concreteRegistry.Count - 1));
            return true;
        }

        /// <summary>Removes from every registry that holds it. Reindexes the concrete registry.</summary>
        public bool Remove(Def def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            bool removed = false;
            foreach (IDefRegistry registry in registries.Values)
            {
                removed |= InvokeRemove(registry, def);
            }
            if (removed && registries.TryGetValue(def.GetType(), out IDefRegistry concrete))
            {
                InvokeReindex(concrete);
            }
            return removed;
        }

        /// <summary>Removes every Def assignable to <paramref name="defType"/>.</summary>
        public int RemoveAll(Type defType)
        {
            if (defType == null) throw new ArgumentNullException(nameof(defType));
            var victims = new List<Def>();
            foreach (Def def in AllDefs)
            {
                if (defType.IsAssignableFrom(def.GetType()))
                {
                    victims.Add(def);
                }
            }
            foreach (Def def in victims)
            {
                Remove(def);
            }
            return victims.Count;
        }

        public void Clear()
        {
            foreach (IDefRegistry registry in registries.Values)
            {
                InvokeClear(registry);
            }
        }

        /// <summary>Calls <see cref="Def.ResolveReferences"/> on every Def; exceptions become errors.</summary>
        public void ResolveAllReferences(List<DefLoadError> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            foreach (Def def in AllDefs)
            {
                try
                {
                    def.ResolveReferences();
                }
                catch (Exception e)
                {
                    errors.Add(new DefLoadError("ResolveReferences threw: " + e.Message, def.fileName, def.defName));
                }
            }
        }

        /// <summary>Collects <see cref="Def.ConfigErrors"/> from every Def not marked ignoreConfigErrors.</summary>
        public void ErrorCheckAllDefs(List<DefLoadError> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            foreach (Def def in AllDefs)
            {
                CollectConfigErrors(def, errors);
            }
        }

        internal static void CollectConfigErrors(Def def, List<DefLoadError> errors)
        {
            if (def.ignoreConfigErrors)
            {
                return;
            }
            try
            {
                foreach (string message in def.ConfigErrors())
                {
                    errors.Add(new DefLoadError("Config error in " + def.GetType().Name + " " + def.defName + ": " + message, def.fileName, def.defName));
                }
            }
            catch (Exception e)
            {
                errors.Add(new DefLoadError("ConfigErrors threw: " + e.Message, def.fileName, def.defName));
            }
        }

        private IDefRegistry RegistryFor(Type defType)
        {
            if (!registries.TryGetValue(defType, out IDefRegistry registry))
            {
                registry = (IDefRegistry)Activator.CreateInstance(typeof(DefRegistry<>).MakeGenericType(defType))!;
                registries[defType] = registry;
            }
            return registry;
        }

        private static void InvokeAdd(IDefRegistry registry, Def def)
        {
            registry.GetType().GetMethod("AddInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(registry, new object[] { def });
        }

        private static bool InvokeRemove(IDefRegistry registry, Def def)
        {
            return (bool)registry.GetType().GetMethod("RemoveInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(registry, new object[] { def })!;
        }

        private static void InvokeClear(IDefRegistry registry)
        {
            registry.GetType().GetMethod("ClearInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(registry, Array.Empty<object>());
        }

        private static void InvokeReindex(IDefRegistry registry)
        {
            registry.GetType().GetMethod("ReindexInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(registry, Array.Empty<object>());
        }
    }

    /// <summary>
    /// Static facade over <see cref="DefDatabase.Global"/> preserving RimWorld call sites:
    /// <c>DefDatabase&lt;ThingDef&gt;.GetNamed("Wall")</c>.
    /// </summary>
    public static class DefDatabase<T> where T : Def
    {
        public static IEnumerable<T> AllDefs => DefDatabase.Global.For<T>().AllDefs;
        public static IReadOnlyList<T> AllDefsListForReading => DefDatabase.Global.For<T>().AllDefsListForReading;
        public static int DefCount => DefDatabase.Global.For<T>().Count;

        public static void Add(T def) => DefDatabase.Global.Add(def);

        public static void Add(IEnumerable<T> defs)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            foreach (T def in defs)
            {
                DefDatabase.Global.Add(def);
            }
        }

        public static T GetNamed(string defName) => DefDatabase.Global.For<T>().GetNamed(defName);
        public static T? GetNamedSilentFail(string defName) => DefDatabase.Global.For<T>().GetNamedSilentFail(defName);

        /// <summary>Removes every <typeparamref name="T"/> (and subclass instances) from the global database.</summary>
        public static void Clear() => DefDatabase.Global.RemoveAll(typeof(T));
    }
}
