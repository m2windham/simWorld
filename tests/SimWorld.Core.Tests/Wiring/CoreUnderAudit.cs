using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SimWorld.Defs;

namespace SimWorld.Tests.Wiring
{
    /// <summary>
    /// The three things the audit needs, found once and shared: the repository's own <c>src/SimWorld.Core</c>
    /// sources, the types in the compiled core assembly, and the path of the reviewed baseline.
    ///
    /// <para/>Everything is located by walking up from the test assembly to the repository root, the same way
    /// <c>CoreContent</c> finds its content. The sources are read from <c>src/</c> rather than from anything
    /// under <c>artifacts/</c> deliberately: the question the audit asks is about the code in the tree, and
    /// the tree is where a fix will be made.
    ///
    /// <para/>Not finding the repository throws rather than returning an empty index. An audit that quietly
    /// examines nothing passes forever, which is the exact failure mode this whole file exists to prevent.
    /// </summary>
    public static class CoreUnderAudit
    {
        private static readonly Lazy<string> LazyRoot = new Lazy<string>(FindRepositoryRoot);
        private static readonly Lazy<SourceIndex> LazySource = new Lazy<SourceIndex>(() => SourceIndex.Load(CoreSourceRoot));
        private static readonly Lazy<IReadOnlyList<Type>> LazyTypes = new Lazy<IReadOnlyList<Type>>(LoadTypes);

        /// <summary>The repository root — the directory holding <c>src/SimWorld.Core/SimWorld.Core.csproj</c>.</summary>
        public static string RepositoryRoot => LazyRoot.Value;

        public static string CoreSourceRoot => Path.Combine(RepositoryRoot, "src", "SimWorld.Core");

        /// <summary>The reviewed list of seams that are dormant on purpose, with the reason for each.</summary>
        public static string BaselinePath =>
            Path.Combine(RepositoryRoot, "tests", "SimWorld.Core.Tests", "Wiring", "dormant-seams.txt");

        /// <summary>Every C# identifier occurrence in <c>src/SimWorld.Core</c>.</summary>
        public static SourceIndex Source => LazySource.Value;

        /// <summary>
        /// Every type in the core assembly, minus compiler-generated ones (closures, iterator state machines,
        /// anonymous types), which have no seams anyone can wire.
        /// </summary>
        public static IReadOnlyList<Type> Types => LazyTypes.Value;

        /// <summary>
        /// The defs the shipped content loaded, and only those — anything the loader gave a pack name to.
        ///
        /// <para/><b>Why not simply every def in the database.</b> Some def families are minted lazily by
        /// code: <c>CorpseDefGenerator</c> mints one <c>Corpse_&lt;race&gt;</c> ThingDef per race the first
        /// time anything needs a corpse, straight into <see cref="DefDatabase.Global"/>. Whether those exist
        /// therefore depends on whether some earlier test in the run happened to kill a pawn — and they carry
        /// comps of their own, so the content survey saw four extra <c>CompProperties_Rottable</c> in a full
        /// suite run and none when the audit ran alone. That is an order-dependent test failure, which is
        /// worse than no test: it appears on an unrelated commit and gets blamed on the wrong change.
        ///
        /// <para/>Filtering to shipped defs also sharpens the question. "No shipped Def sets this field" is a
        /// statement about content; an implied def is built by code, and its fields are that code's business.
        /// </summary>
        public static IReadOnlyList<Def> ShippedDefs(DefDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            return database.AllDefs.Where(d => d.packName != null).ToList();
        }

        private static IReadOnlyList<Type> LoadTypes() =>
            typeof(Def).Assembly.GetTypes()
                .Where(t => (t.FullName ?? t.Name).IndexOf('<') < 0)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

        private static string FindRepositoryRoot()
        {
            var searched = new List<string>();
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start)) continue;
                for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    searched.Add(dir.FullName);
                    if (File.Exists(Path.Combine(dir.FullName, "src", "SimWorld.Core", "SimWorld.Core.csproj")))
                    {
                        return dir.FullName;
                    }
                }
            }

            throw new DirectoryNotFoundException(
                "The wiring audit could not find the repository root (a directory holding "
                + "src/SimWorld.Core/SimWorld.Core.csproj). Searched upward from:" + Environment.NewLine
                + string.Join(Environment.NewLine, searched));
        }
    }
}
