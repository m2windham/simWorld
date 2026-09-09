using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace SimWorld.Defs
{
    /// <summary>Knobs for one <see cref="DefLoader"/>.</summary>
    public sealed class DefLoadOptions
    {
        /// <summary>Later content packs override earlier ones by default (mod load-order semantics).</summary>
        public DuplicateDefPolicy DuplicatePolicy = DuplicateDefPolicy.LastWins;

        /// <summary>Throw <see cref="DefLoadException"/> from <see cref="DefLoader.Load"/> when any error was recorded.</summary>
        public bool ThrowOnError;

        /// <summary>Bind <see cref="DefOfAttribute"/> classes after loading.</summary>
        public bool BindDefOfs = true;

        /// <summary>When set, only these <see cref="DefOfAttribute"/> classes are bound instead of scanning assemblies.</summary>
        public List<Type>? DefOfTypes;

        /// <summary>Run <see cref="Def.ConfigErrors"/> on every loaded Def.</summary>
        public bool RunConfigErrors = true;
    }

    /// <summary>
    /// Loads Defs from XML content (RimWorld: <c>Verse.DirectXmlLoader</c> + <c>PlayDataLoader</c>).
    /// Queue every file with <see cref="AddXml"/>/<see cref="AddFile"/>/<see cref="AddDirectory"/>, then call
    /// <see cref="Load"/> once so cross-references can span files and packs. The pass runs:
    /// inheritance → instantiate + <see cref="Def.PostLoad"/> → register → resolve cross-refs → bind DefOfs →
    /// <see cref="Def.ResolveReferences"/> → <see cref="Def.ConfigErrors"/>.
    /// </summary>
    public sealed class DefLoader
    {
        public const string RootElementName = "Defs";

        /// <summary>Root element of a patch file (RimWorld uses the same name).</summary>
        public const string PatchRootElementName = "Patch";

        private readonly List<XmlInheritance.Node> pending = new List<XmlInheritance.Node>();
        private readonly List<PatchEntry> pendingPatches = new List<PatchEntry>();
        private readonly List<DefLoadError> parseErrors = new List<DefLoadError>();

        /// <summary>Identifiers (package id and name) of every content pack in this load, for
        /// <see cref="PatchOperationFindMod"/> to test against.</summary>
        public List<string> PackIdentifiers { get; } = new List<string>();

        /// <summary>One queued <c>&lt;Operation&gt;</c> element, kept as XML until <see cref="Load"/> builds it —
        /// the object mapper that turns it into a <see cref="PatchOperation"/> only exists during a load.</summary>
        private sealed class PatchEntry
        {
            public PatchEntry(XElement element, string file, string pack)
            {
                Element = element;
                File = file;
                Pack = pack;
            }

            public XElement Element { get; }
            public string File { get; }
            public string Pack { get; }
        }

        /// <summary>Which file and pack a Def element came from, carried on the element itself while patches
        /// run. An annotation rather than an attribute so it cannot be seen by an XPath patch, cannot collide
        /// with content, and cannot leak into the object mapper.</summary>
        private sealed class DefSource
        {
            public DefSource(string file, string pack)
            {
                File = file;
                Pack = pack;
            }

            public string File { get; }
            public string Pack { get; }
        }

        public DefDatabase Database { get; }
        public DefTypeResolver Types { get; }
        public DefLoadOptions Options { get; }

        public DefLoader(DefDatabase? database = null, DefTypeResolver? types = null, DefLoadOptions? options = null)
        {
            Database = database ?? DefDatabase.Global;
            Types = types ?? new DefTypeResolver();
            Options = options ?? new DefLoadOptions();
        }

        /// <summary>Number of Def nodes queued for the next <see cref="Load"/>.</summary>
        public int PendingCount => pending.Count;

        /// <summary>Queues every Def element under a <c>&lt;Defs&gt;</c> root.</summary>
        public DefLoader AddXml(string xml, string fileName = "<string>", string packName = "Core")
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));
            XDocument document;
            try
            {
                document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                parseErrors.Add(new DefLoadError("XML parse error: " + e.Message, fileName));
                return this;
            }
            XElement? root = document.Root;
            if (root == null || root.Name.LocalName != RootElementName)
            {
                parseErrors.Add(new DefLoadError("Root element must be <" + RootElementName + ">.", fileName));
                return this;
            }
            foreach (XElement element in root.Elements())
            {
                pending.Add(new XmlInheritance.Node(element, fileName, packName));
            }
            return this;
        }

        /// <summary>
        /// Queues every <c>&lt;Operation&gt;</c> under a <c>&lt;Patch&gt;</c> root. Patches apply to the whole
        /// combined document before inheritance resolves, so a pack can edit content it does not own —
        /// including abstract parents, which is how one patch reaches every def that inherits from one.
        /// </summary>
        public DefLoader AddPatchXml(string xml, string fileName = "<string>", string packName = "Core")
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));
            XDocument document;
            try
            {
                document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                parseErrors.Add(new DefLoadError("XML parse error: " + e.Message, fileName));
                return this;
            }
            XElement? root = document.Root;
            if (root == null || root.Name.LocalName != PatchRootElementName)
            {
                parseErrors.Add(new DefLoadError("Root element must be <" + PatchRootElementName + ">.", fileName));
                return this;
            }
            foreach (XElement element in root.Elements())
            {
                pendingPatches.Add(new PatchEntry(element, fileName, packName));
            }
            return this;
        }

        public DefLoader AddPatchFile(string path, string? packName = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return AddPatchXml(File.ReadAllText(path), path, packName ?? "Core");
        }

        /// <summary>Queues every <c>*.xml</c> patch under <paramref name="directory"/> in ordinal path order.</summary>
        public DefLoader AddPatchDirectory(string directory, string? packName = null, bool recursive = true)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            string[] files = Directory.GetFiles(directory, "*.xml", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                AddPatchFile(file, packName);
            }
            return this;
        }

        public DefLoader AddFile(string path, string? packName = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return AddXml(File.ReadAllText(path), path, packName ?? "Core");
        }

        /// <summary>Queues every <c>*.xml</c> under <paramref name="directory"/> in ordinal path order (deterministic).</summary>
        public DefLoader AddDirectory(string directory, string? packName = null, bool recursive = true)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            string[] files = Directory.GetFiles(directory, "*.xml", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                AddFile(file, packName);
            }
            return this;
        }

        /// <summary>Runs the full load pass over everything queued since the last call.</summary>
        public DefLoadResult Load()
        {
            var errors = new List<DefLoadError>(parseErrors);
            parseErrors.Clear();

            var crossRefs = new CrossRefRegistry();
            var mapper = new XmlObjectMapper(Types, errors, crossRefs);
            XmlLoadContext context = mapper.Context;

            ApplyPatches(mapper, errors);

            XmlInheritance.ResolveAll(pending, errors);

            var loaded = new List<Def>();

            foreach (XmlInheritance.Node node in pending)
            {
                if (node.IsAbstract)
                {
                    continue;
                }
                XElement element = node.Resolved ?? node.Element;
                context.FileName = node.File;
                context.CurrentDefName = element.Element("defName")?.Value.Trim();

                Type? defType = Types.GetDefType(element.Name.LocalName);
                if (defType == null)
                {
                    context.Error("Could not find a Def type named '" + element.Name.LocalName + "'.");
                    continue;
                }

                if (!(mapper.ObjectFromXml(element, defType) is Def def))
                {
                    continue;
                }
                def.fileName = node.File;
                def.packName = node.Pack;

                try
                {
                    def.PostLoad();
                }
                catch (Exception e)
                {
                    errors.Add(new DefLoadError("PostLoad threw: " + e.Message, node.File, def.defName));
                }

                if (Database.Add(def, Options.DuplicatePolicy, out string? addError))
                {
                    loaded.Add(def);
                }
                else if (addError != null)
                {
                    errors.Add(new DefLoadError(addError, node.File, def.defName));
                }
            }
            pending.Clear();
            context.FileName = null;
            context.CurrentDefName = null;

            // Under LastWins a later pack can replace a Def loaded earlier in this same pass;
            // the replaced object is gone from the database and must not be resolved or checked.
            loaded.RemoveAll(def => !ReferenceEquals(Database.GetNamed(def.GetType(), def.defName), def));

            crossRefs.ResolveAll(Database, errors);

            if (Options.BindDefOfs)
            {
                if (Options.DefOfTypes != null)
                {
                    foreach (Type type in Options.DefOfTypes)
                    {
                        DefOfHelper.BindDefsFor(type, Database, errors);
                    }
                }
                else
                {
                    DefOfHelper.RebindAllDefOfs(Database, Types, errors);
                }
            }

            foreach (Def def in loaded)
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

            if (Options.RunConfigErrors)
            {
                foreach (Def def in loaded)
                {
                    DefDatabase.CollectConfigErrors(def, errors);
                }
            }

            var result = new DefLoadResult(loaded, errors);
            if (Options.ThrowOnError && errors.Count > 0)
            {
                throw new DefLoadException(errors);
            }
            return result;
        }

        /// <summary>
        /// Runs every queued patch against one document holding all queued Def elements, then rebuilds the
        /// pending list from the patched result (RimWorld: <c>LoadedModManager.ApplyPatches</c>).
        /// <para/>
        /// The combine is skipped entirely when nothing queued a patch, so the ordinary load path does not pay
        /// to clone every Def element for a feature it is not using.
        /// </summary>
        private void ApplyPatches(XmlObjectMapper mapper, List<DefLoadError> errors)
        {
            if (pendingPatches.Count == 0)
            {
                pendingPatches.Clear();
                return;
            }

            var root = new XElement(RootElementName);
            foreach (XmlInheritance.Node node in pending)
            {
                var clone = new XElement(node.Element);
                clone.AddAnnotation(new DefSource(node.File, node.Pack));
                root.Add(clone);
            }
            var document = new XDocument(root);

            PatchOperationFindMod.LoadedPackIdentifiers = new List<string>(PackIdentifiers);
            XmlLoadContext context = mapper.Context;
            try
            {
                foreach (PatchEntry entry in pendingPatches)
                {
                    context.FileName = entry.File;
                    context.CurrentDefName = null;

                    PatchOperation? operation = mapper.ObjectFromXml<PatchOperation>(entry.Element);
                    if (operation == null)
                    {
                        // ObjectFromXml already recorded why.
                        continue;
                    }

                    bool applied;
                    try
                    {
                        applied = operation.Apply(document);
                    }
                    catch (Exception e)
                    {
                        errors.Add(new DefLoadError(operation + " threw: " + e.Message, entry.File));
                        continue;
                    }

                    if (!applied)
                    {
                        errors.Add(new DefLoadError(operation + " did not apply to anything.", entry.File));
                    }

                    // Anything this patch introduced has no source of its own, so it belongs to the patch that
                    // wrote it. Attributing after every operation (rather than once at the end) keeps a Def
                    // added by one pack's patch from being credited to whichever pack patched last.
                    foreach (XElement element in root.Elements())
                    {
                        if (element.Annotation<DefSource>() == null)
                        {
                            element.AddAnnotation(new DefSource(entry.File, entry.Pack));
                        }
                    }
                }
            }
            finally
            {
                context.FileName = null;
                PatchOperationFindMod.LoadedPackIdentifiers = Array.Empty<string>();
                pendingPatches.Clear();
            }

            pending.Clear();
            foreach (XElement element in root.Elements())
            {
                DefSource source = element.Annotation<DefSource>() ?? new DefSource("<patch>", "Core");
                pending.Add(new XmlInheritance.Node(element, source.File, source.Pack));
            }
        }
    }
}
