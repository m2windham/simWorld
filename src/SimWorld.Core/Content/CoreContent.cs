using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SimWorld.Defs;

namespace SimWorld.Content
{
    /// <summary>
    /// Finds and loads the core content shipped beside the assembly (<c>Data/Core/Defs/**.xml</c>).
    /// The Unity host sees it inside the package folder; .NET builds copy it next to the DLL; tests
    /// fall back to walking up to the repository's <c>src/SimWorld.Core/Data</c>.
    /// </summary>
    public static class CoreContent
    {
        public const string PackName = "Core";
        public const string DataEnvironmentVariable = "SIMWORLD_DATA";

        private static string? cachedDataDirectory;

        /// <summary>The <c>Data</c> directory, or null when it cannot be found.</summary>
        public static string? DataDirectory
        {
            get
            {
                if (cachedDataDirectory != null) return cachedDataDirectory;
                cachedDataDirectory = Locate();
                return cachedDataDirectory;
            }
        }

        public static string? DefsDirectory
        {
            get
            {
                string? data = DataDirectory;
                return data == null ? null : Path.Combine(data, PackName, "Defs");
            }
        }

        /// <summary>Queues every core Def file on <paramref name="loader"/>. Throws when the content cannot be found.</summary>
        public static DefLoader AddCoreDefs(DefLoader loader)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            string? defs = DefsDirectory;
            if (defs == null || !Directory.Exists(defs))
            {
                throw new DirectoryNotFoundException("Core content not found. Set " + DataEnvironmentVariable + " or run from a build that ships Data/.");
            }
            loader.AddDirectory(defs, PackName);
            return loader;
        }

        /// <summary>Loads core content into <paramref name="database"/> and returns the result.</summary>
        public static DefLoadResult Load(DefDatabase database, DefTypeResolver? types = null, DefLoadOptions? options = null)
        {
            var loader = new DefLoader(database, types, options);
            AddCoreDefs(loader);
            return loader.Load();
        }

        /// <summary>
        /// Every content pack under a data directory: one per subfolder that carries content
        /// (<c>Defs/</c>, <c>Patches/</c> or an <c>About/</c>), read in ordinal folder order before load
        /// ordering re-sorts them.
        /// </summary>
        public static List<ModContentPack> DiscoverPacks(string? dataDirectory, DefTypeResolver types, List<DefLoadError> errors)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            var packs = new List<ModContentPack>();
            string? data = dataDirectory ?? DataDirectory;
            if (data == null || !Directory.Exists(data)) return packs;

            string[] folders = Directory.GetDirectories(data);
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (string folder in folders)
            {
                if (!Directory.Exists(Path.Combine(folder, ModContentPack.DefsFolder))
                    && !Directory.Exists(Path.Combine(folder, ModContentPack.PatchesFolder))
                    && !Directory.Exists(Path.Combine(folder, ModMetaData.DirectoryName)))
                {
                    continue;
                }
                ModContentPack? pack = ModContentPack.FromDirectory(folder, types, errors);
                if (pack != null) packs.Add(pack);
            }
            return packs;
        }

        /// <summary>
        /// Loads every discovered content pack in dependency order: all Defs from every pack first, then all
        /// patches, because a patch edits the combined document and must be able to reach content from a pack
        /// that loads after its own (RimWorld: <c>LoadedModManager.LoadAllActiveMods</c> works the same way).
        /// Errors from discovery and ordering are folded into the load result rather than thrown.
        /// </summary>
        public static DefLoadResult LoadAllPacks(
            DefDatabase database, DefTypeResolver? types = null, DefLoadOptions? options = null, string? dataDirectory = null)
        {
            DefTypeResolver resolver = types ?? new DefTypeResolver();
            var discoveryErrors = new List<DefLoadError>();
            List<ModContentPack> packs = ModLoadOrder.Resolve(DiscoverPacks(dataDirectory, resolver, discoveryErrors), discoveryErrors);

            var loader = new DefLoader(database, resolver, options);
            foreach (ModContentPack pack in packs)
            {
                loader.PackIdentifiers.Add(pack.PackageId);
                if (pack.Name != pack.PackageId) loader.PackIdentifiers.Add(pack.Name);
            }
            foreach (ModContentPack pack in packs)
            {
                if (pack.HasDefs) loader.AddDirectory(pack.DefsDirectory, pack.Name);
            }
            foreach (ModContentPack pack in packs)
            {
                if (pack.HasPatches) loader.AddPatchDirectory(pack.PatchesDirectory, pack.Name);
            }

            DefLoadResult result = loader.Load();
            if (discoveryErrors.Count == 0) return result;

            var all = new List<DefLoadError>(discoveryErrors);
            all.AddRange(result.Errors);
            return new DefLoadResult(result.Defs, all);
        }

        public static void ResetCache() => cachedDataDirectory = null;

        private static string? Locate()
        {
            string? env = Environment.GetEnvironmentVariable(DataEnvironmentVariable);
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            string? baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                string beside = Path.Combine(baseDir, "Data");
                if (Directory.Exists(Path.Combine(beside, PackName))) return beside;
                string? found = WalkUp(baseDir);
                if (found != null) return found;
            }

            string? assemblyDir = Path.GetDirectoryName(typeof(CoreContent).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                string beside = Path.Combine(assemblyDir, "Data");
                if (Directory.Exists(Path.Combine(beside, PackName))) return beside;
                string? found = WalkUp(assemblyDir);
                if (found != null) return found;
            }
            return null;
        }

        private static string? WalkUp(string start)
        {
            DirectoryInfo? dir = new DirectoryInfo(start);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "src", "SimWorld.Core", "Data");
                if (Directory.Exists(Path.Combine(candidate, PackName))) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
