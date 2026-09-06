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

        private readonly List<XmlInheritance.Node> pending = new List<XmlInheritance.Node>();
        private readonly List<DefLoadError> parseErrors = new List<DefLoadError>();

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

            XmlInheritance.ResolveAll(pending, errors);

            var crossRefs = new CrossRefRegistry();
            var mapper = new XmlObjectMapper(Types, errors, crossRefs);
            XmlLoadContext context = mapper.Context;
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
    }
}
