using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Content
{
    /// <summary>
    /// What a content pack says about itself (RimWorld: <c>Verse.ModMetaData</c>, read from <c>About/About.xml</c>).
    /// Trimmed to the fields that change how content loads — the Steam/workshop and UI fields RimWorld carries
    /// (preview image, url, descriptions per version) have nothing to serve here yet.
    /// </summary>
    public sealed class ModMetaData
    {
        public const string FileName = "About.xml";
        public const string DirectoryName = "About";
        public const string RootElementName = "ModMetaData";

        /// <summary>Unique identifier, conventionally <c>author.modname</c>. Falls back to the folder name.</summary>
        public string packageId = "";

        public string name = "";
        public string author = "";
        public string description = "";

        /// <summary>Packs that must be present; a missing one is a load error rather than a silent skip.</summary>
        public List<string> modDependencies = new List<string>();

        /// <summary>Packs this one must load after, if they are present at all.</summary>
        public List<string> loadAfter = new List<string>();

        /// <summary>Packs this one must load before, if they are present at all.</summary>
        public List<string> loadBefore = new List<string>();
    }

    /// <summary>
    /// One folder of content: its metadata, its Defs and its patches (RimWorld: <c>Verse.ModContentPack</c>).
    /// A pack is the unit of load order and the unit of attribution — every Def and every patch operation
    /// remembers which pack it came from, which is what makes "later pack wins" and patch provenance
    /// meaningful.
    /// </summary>
    public sealed class ModContentPack
    {
        public const string DefsFolder = "Defs";
        public const string PatchesFolder = "Patches";

        public ModContentPack(string rootDirectory, ModMetaData metaData)
        {
            RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            MetaData = metaData ?? throw new ArgumentNullException(nameof(metaData));
        }

        public string RootDirectory { get; }
        public ModMetaData MetaData { get; }

        public string PackageId => MetaData.packageId;

        /// <summary>The name content and errors refer to this pack by — its <c>name</c>, else its package id.</summary>
        public string Name => MetaData.name.Length > 0 ? MetaData.name : MetaData.packageId;

        public string DefsDirectory => Path.Combine(RootDirectory, DefsFolder);
        public string PatchesDirectory => Path.Combine(RootDirectory, PatchesFolder);

        public bool HasDefs => Directory.Exists(DefsDirectory);
        public bool HasPatches => Directory.Exists(PatchesDirectory);

        /// <summary>The base game's own content, which always loads first (RimWorld: <c>ModContentPack.IsCoreMod</c>).</summary>
        public bool IsCoreMod => string.Equals(Name, CoreContent.PackName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads a pack from a folder. A folder with no <c>About/About.xml</c> is still a pack — it gets
        /// metadata derived from its own name — because requiring the file would make the simplest possible
        /// content folder (just <c>Defs/</c>) unloadable for no gain.
        /// </summary>
        public static ModContentPack? FromDirectory(string directory, DefTypeResolver types, List<DefLoadError> errors)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (!Directory.Exists(directory)) return null;

            string folderName = new DirectoryInfo(directory).Name;
            string aboutPath = Path.Combine(directory, ModMetaData.DirectoryName, ModMetaData.FileName);
            ModMetaData meta = File.Exists(aboutPath)
                ? ReadMetaData(aboutPath, types, errors) ?? new ModMetaData()
                : new ModMetaData();

            if (meta.name.Length == 0) meta.name = folderName;
            if (meta.packageId.Length == 0) meta.packageId = folderName;
            return new ModContentPack(directory, meta);
        }

        private static ModMetaData? ReadMetaData(string path, DefTypeResolver types, List<DefLoadError> errors)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(File.ReadAllText(path), LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                errors.Add(new DefLoadError("XML parse error: " + e.Message, path));
                return null;
            }

            XElement? root = document.Root;
            if (root == null || root.Name.LocalName != ModMetaData.RootElementName)
            {
                errors.Add(new DefLoadError("Root element must be <" + ModMetaData.RootElementName + ">.", path));
                return null;
            }

            // Built through the same object mapper every other content file goes through, so About.xml gets
            // the same list/li and field-name semantics as a Def rather than a second, subtly different parser.
            var mapper = new XmlObjectMapper(types, errors, new CrossRefRegistry());
            mapper.Context.FileName = path;
            return mapper.ObjectFromXml<ModMetaData>(root);
        }

        public override string ToString() => Name + " (" + PackageId + ")";
    }

    /// <summary>
    /// Orders content packs for loading (RimWorld: <c>Verse.ModsConfig</c>'s ordering plus
    /// <c>ModMetaData.loadAfter/loadBefore</c>). Core first, then a stable topological order honouring every
    /// pack's declared constraints, with the declared order as the tie-break so the same folder set always
    /// loads the same way.
    /// </summary>
    public static class ModLoadOrder
    {
        /// <summary>
        /// Returns the packs in load order. A missing hard dependency and a constraint cycle are both
        /// recorded as errors and then survived — content loading reports what is wrong and keeps going, the
        /// same way an unresolvable cross-reference does, rather than throwing the whole load away.
        /// </summary>
        public static List<ModContentPack> Resolve(IReadOnlyList<ModContentPack> packs, List<DefLoadError> errors)
        {
            if (packs == null) throw new ArgumentNullException(nameof(packs));
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            var byIdentifier = new Dictionary<string, ModContentPack>(StringComparer.OrdinalIgnoreCase);
            foreach (ModContentPack pack in packs)
            {
                byIdentifier[pack.PackageId] = pack;
                byIdentifier[pack.Name] = pack;
            }

            foreach (ModContentPack pack in packs)
            {
                foreach (string dependency in pack.MetaData.modDependencies)
                {
                    if (!byIdentifier.ContainsKey(dependency))
                    {
                        errors.Add(new DefLoadError(
                            "Content pack '" + pack.Name + "' depends on '" + dependency + "', which is not loaded.",
                            pack.RootDirectory));
                    }
                }
            }

            // Edges point from "loads earlier" to "loads later". A dependency is also an ordering constraint:
            // a pack that needs another's content cannot load before it.
            var after = new Dictionary<ModContentPack, HashSet<ModContentPack>>();
            foreach (ModContentPack pack in packs) after[pack] = new HashSet<ModContentPack>();

            foreach (ModContentPack pack in packs)
            {
                foreach (string earlier in Concat(pack.MetaData.modDependencies, pack.MetaData.loadAfter))
                {
                    if (byIdentifier.TryGetValue(earlier, out ModContentPack? other) && other != pack) after[pack].Add(other);
                }
                foreach (string later in pack.MetaData.loadBefore)
                {
                    if (byIdentifier.TryGetValue(later, out ModContentPack? other) && other != pack) after[other].Add(pack);
                }
                if (!pack.IsCoreMod)
                {
                    foreach (ModContentPack core in packs)
                    {
                        if (core.IsCoreMod) after[pack].Add(core);
                    }
                }
            }

            var ordered = new List<ModContentPack>(packs.Count);
            var placed = new HashSet<ModContentPack>();
            var remaining = new List<ModContentPack>(packs);

            while (remaining.Count > 0)
            {
                // Declared order is the tie-break: scan for the first pack whose predecessors are all placed.
                int index = remaining.FindIndex(p => after[p].Count == 0 || after[p].IsSubsetOf(placed));
                if (index < 0)
                {
                    // Everything left is in a cycle. Report it once, then place the rest in declared order so
                    // the load still happens — an unloadable game is a worse answer than a mis-ordered one.
                    errors.Add(new DefLoadError(
                        "Content pack load order has a cycle among: " + string.Join(", ", remaining.ConvertAll(p => p.Name)) + ".",
                        remaining[0].RootDirectory));
                    ordered.AddRange(remaining);
                    break;
                }
                ModContentPack next = remaining[index];
                remaining.RemoveAt(index);
                ordered.Add(next);
                placed.Add(next);
            }

            return ordered;
        }

        private static IEnumerable<string> Concat(List<string> a, List<string> b)
        {
            foreach (string s in a) yield return s;
            foreach (string s in b) yield return s;
        }
    }
}
