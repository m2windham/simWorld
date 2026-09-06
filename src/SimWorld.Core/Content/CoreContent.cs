using System;
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
