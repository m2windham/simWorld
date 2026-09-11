using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SimWorld.Defs;

namespace SimWorld.Content
{
    /// <summary>
    /// Finds and loads the core content shipped beside the assembly (<c>Data/Core/Defs/**.xml</c>).
    ///
    /// <para/>Four places are tried in order, and <see cref="DescribeSearch"/> reports what each one found:
    /// the <c>SIMWORLD_DATA</c> environment variable, a <c>Data/</c> directory beside the running assembly,
    /// a Unity package manifest naming this package by relative path
    /// (<see cref="TryResolveFromUnityPackageManifest"/>), and finally a walk up to the repository's own
    /// <c>src/SimWorld.Core/Data</c> for tests and .NET builds.
    ///
    /// <para/><b>The Unity case is the one that used to fail, and it failed silently.</b> A host consuming
    /// this as a local UPM package by relative path — <c>"com.simworld.core": "file:../../simWorld/src/SimWorld.Core"</c>
    /// — has its content in a <i>sibling repository</i>. Unity compiles the package's source into
    /// <c>Library/ScriptAssemblies/</c> inside the host project, so neither the assembly location nor any
    /// walk up from it ever leaves the host project, and the content is never found. The load then
    /// succeeds with zero defs, which reads exactly like a game that has not started — the same silent
    /// mode <see cref="God.View.GodViewSnapshot.ContentLoaded"/> exists to expose. Resolving the manifest
    /// closes it at the source: the manifest is the only artifact that knows where the package actually is.
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
        /// <param name="loader">The loader to queue onto.</param>
        /// <param name="dataDirectory">The <c>Data</c> directory to load from, bypassing the search entirely.
        /// Null uses <see cref="DataDirectory"/>. A host that knows where its content is should pass it here
        /// rather than setting <c>SIMWORLD_DATA</c>: an environment variable is process-global, has to be set
        /// before anything reads it, and is invisible at the call site that depends on it.</param>
        public static DefLoader AddCoreDefs(DefLoader loader, string? dataDirectory = null)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            string? defs = dataDirectory == null
                ? DefsDirectory
                : Path.Combine(dataDirectory, PackName, "Defs");
            if (defs == null || !Directory.Exists(defs))
            {
                throw new DirectoryNotFoundException(
                    "Core content not found. Set " + DataEnvironmentVariable + " or run from a build that ships Data/."
                    + Environment.NewLine + DescribeSearch());
            }

            // A directory that exists but holds no XML is the silent failure mode this whole class has to
            // avoid: the load would report success with zero defs, and a host cannot tell that apart from a
            // civilization that has not started. Core content always ships files, so an empty directory here
            // means the wrong directory was found — say so rather than returning an empty loader.
            if (!HasAnyDefFile(defs))
            {
                throw new DirectoryNotFoundException(
                    "Core content directory '" + defs + "' contains no Def files. It is almost certainly not the "
                    + "core's own Data directory." + Environment.NewLine + DescribeSearch());
            }

            loader.AddDirectory(defs, PackName);
            return loader;
        }

        /// <summary>Loads core content into <paramref name="database"/> and returns the result.</summary>
        public static DefLoadResult Load(
            DefDatabase database, DefTypeResolver? types = null, DefLoadOptions? options = null, string? dataDirectory = null)
        {
            var loader = new DefLoader(database, types, options);
            AddCoreDefs(loader, dataDirectory);
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

        /// <summary>The Unity project manifest that names every package a project consumes. Relative
        /// <c>file:</c> paths in it resolve against the folder this file sits in, which is why the folder
        /// matters and not just the file.</summary>
        private const string UnityManifestRelativePath = "Packages/manifest.json";

        /// <summary>
        /// Resolves the content directory through a Unity project's <c>Packages/manifest.json</c>, walking up
        /// from <paramref name="startDirectory"/> to find one.
        ///
        /// <para/>Every <c>file:</c> dependency in the manifest is resolved (relative paths against the
        /// <c>Packages</c> folder, per Unity's own rule) and tested for a <c>Data/</c> directory holding this
        /// pack. Deliberately <b>not</b> keyed on the package being named <c>com.simworld.core</c>: a host may
        /// rename it, and testing the candidate for the content we actually want is both simpler and
        /// self-validating — a path that has the content is the right path whatever it is called.
        ///
        /// <para/>The manifest is read as text rather than parsed as JSON. That is a deliberate limit, not an
        /// oversight: parsing it properly would mean either a dependency this engine-free core does not have
        /// or a hand-written JSON parser, to extract string values that a single unambiguous token already
        /// identifies. The cost is that a <c>file:</c> path inside a comment or a string that is not a
        /// dependency would also be tried — which is harmless, because every candidate has to actually
        /// contain the content to be accepted.
        ///
        /// <para/>Public because a host that cannot find its content needs to be able to say why, and this is
        /// the step most likely to be the reason.
        /// </summary>
        /// <param name="startDirectory">Where to start walking up from — typically the running assembly's
        /// directory.</param>
        /// <param name="manifestPath">The manifest that was read, or null when no manifest was found at all.
        /// Set even when resolution fails, so a caller can tell "no Unity project here" from "the project's
        /// manifest does not point at the content".</param>
        /// <returns>The <c>Data</c> directory, or null.</returns>
        public static string? TryResolveFromUnityPackageManifest(string startDirectory, out string? manifestPath)
        {
            manifestPath = null;
            if (string.IsNullOrEmpty(startDirectory)) return null;

            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(startDirectory);
            }
            catch (ArgumentException)
            {
                return null;
            }

            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Packages", "manifest.json");
                if (File.Exists(candidate))
                {
                    manifestPath = candidate;
                    return ResolveFromManifestFile(candidate);
                }
                dir = dir.Parent;
            }
            return null;
        }

        private static string? ResolveFromManifestFile(string manifestFile)
        {
            string text;
            try
            {
                text = File.ReadAllText(manifestFile);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            string packagesFolder = Path.GetDirectoryName(manifestFile) ?? string.Empty;

            const string token = "file:";
            int at = 0;
            while (true)
            {
                at = text.IndexOf(token, at, StringComparison.Ordinal);
                if (at < 0) return null;

                int valueStart = at + token.Length;
                int end = valueStart;
                // A JSON string value ends at the closing quote; nothing else can appear inside one unescaped.
                while (end < text.Length && text[end] != '"') end++;

                string raw = text.Substring(valueStart, end - valueStart).Trim();
                at = end;

                if (raw.Length == 0) continue;

                string? resolved = ResolveCandidate(packagesFolder, raw);
                if (resolved != null) return resolved;
            }
        }

        private static string? ResolveCandidate(string packagesFolder, string rawPath)
        {
            string full;
            try
            {
                // Unity resolves a relative file: path against the Packages folder, not the project root.
                full = Path.GetFullPath(Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(packagesFolder, rawPath));
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }

            string data = Path.Combine(full, "Data");
            return Directory.Exists(Path.Combine(data, PackName)) ? data : null;
        }

        private static bool HasAnyDefFile(string defsDirectory)
        {
            try
            {
                using IEnumerator<string> files =
                    Directory.EnumerateFiles(defsDirectory, "*.xml", SearchOption.AllDirectories).GetEnumerator();
                return files.MoveNext();
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Every place <see cref="Locate"/> looks, in order, and what is actually there — one line each.
        ///
        /// <para/>This exists to be printed in an error. "Core content not found" tells a host nothing it can
        /// act on; a list saying the environment variable is unset, no Data sits beside the assembly, the
        /// Unity manifest at this path names no package that carries the content, and the repository walk
        /// found nothing, tells them exactly which assumption is wrong. Recomputed on each call rather than
        /// cached, because it is a diagnostic and a stale one would mislead.
        /// </summary>
        public static string DescribeSearch()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Searched for ").Append(PackName).Append(" content in:").Append(Environment.NewLine);

            string? env = Environment.GetEnvironmentVariable(DataEnvironmentVariable);
            sb.Append("  1. $").Append(DataEnvironmentVariable).Append(" = ")
              .Append(string.IsNullOrEmpty(env) ? "(unset)" : env)
              .Append(string.IsNullOrEmpty(env) ? string.Empty : (Directory.Exists(env) ? " [exists]" : " [missing]"))
              .Append(Environment.NewLine);

            string? baseDir = AppContext.BaseDirectory;
            string? assemblyDir = Path.GetDirectoryName(typeof(CoreContent).Assembly.Location);
            AppendBeside(sb, "  2. beside AppContext.BaseDirectory", baseDir);
            AppendBeside(sb, "  3. beside the assembly", assemblyDir);

            string? manifest = null;
            string? viaManifest = null;
            foreach (string? start in new[] { baseDir, assemblyDir })
            {
                if (string.IsNullOrEmpty(start)) continue;
                viaManifest = TryResolveFromUnityPackageManifest(start!, out manifest);
                if (manifest != null) break;
            }
            sb.Append("  4. Unity ").Append(UnityManifestRelativePath).Append(" = ")
              .Append(manifest ?? "(no Unity project found above either directory)");
            if (manifest != null)
            {
                sb.Append(viaManifest != null
                    ? " -> " + viaManifest
                    : " [no file: dependency in it carries " + PackName + " content]");
            }
            sb.Append(Environment.NewLine);

            sb.Append("  5. repository walk for src/SimWorld.Core/Data = ")
              .Append((assemblyDir != null ? WalkUp(assemblyDir) : null) ?? "(not found)");
            return sb.ToString();
        }

        private static void AppendBeside(System.Text.StringBuilder sb, string label, string? directory)
        {
            sb.Append(label).Append(" = ");
            if (string.IsNullOrEmpty(directory))
            {
                sb.Append("(unavailable)").Append(Environment.NewLine);
                return;
            }
            string beside = Path.Combine(directory!, "Data");
            sb.Append(beside)
              .Append(Directory.Exists(Path.Combine(beside, PackName)) ? " [found]" : " [missing]")
              .Append(Environment.NewLine);
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
            }

            // Unity: the assembly lives in the host project's Library/ScriptAssemblies and the content lives
            // wherever the manifest says the package is, which may be outside the project entirely. Try this
            // before the repository walk, since a host checkout can sit inside a directory that happens to
            // contain an unrelated src/SimWorld.Core.
            foreach (string start in new[] { baseDir, assemblyDir })
            {
                if (string.IsNullOrEmpty(start)) continue;
                string? viaManifest = TryResolveFromUnityPackageManifest(start!, out _);
                if (viaManifest != null) return viaManifest;
            }

            if (!string.IsNullOrEmpty(assemblyDir))
            {
                string? found = WalkUp(assemblyDir!);
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
