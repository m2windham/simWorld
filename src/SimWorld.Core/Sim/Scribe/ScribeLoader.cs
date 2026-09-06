using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Sim
{
    /// <summary>
    /// Reads a save document in three passes (RimWorld: <c>Verse.ScribeLoader</c>): LoadingVars builds
    /// objects and records reference ids; ResolvingCrossRefs hands each object its referents; PostLoadInit
    /// lets objects rebuild derived state. Problems are collected in <see cref="Errors"/>, never thrown,
    /// so a partly-broken save still loads as far as it can.
    /// </summary>
    public sealed class ScribeLoader
    {
        private XDocument? document;
        private XElement? curParent;
        private readonly Stack<XElement> parents = new Stack<XElement>();

        public CrossRefHandler crossRefs { get; } = new CrossRefHandler();
        public PostLoadIniter initer { get; } = new PostLoadIniter();
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Defs referenced by name resolve against this database.</summary>
        public DefDatabase Defs { get; private set; } = DefDatabase.Global;

        /// <summary>The object whose <see cref="IExposable.ExposeData"/> is running; keys recorded references.</summary>
        public IExposable? CurExposable { get; internal set; }

        public XElement? CurParent => curParent;

        public bool Active => document != null;

        public void InitLoading(string xml, DefDatabase? defs = null)
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));
            if (document != null) throw new ScribeException("InitLoading called while a load is in progress.");
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (XmlException e)
            {
                throw new ScribeException("Save is not valid XML: " + e.Message);
            }
            curParent = document.Root;
            parents.Clear();
            Errors.Clear();
            crossRefs.Clear();
            initer.Clear();
            CurExposable = null;
            Defs = defs ?? DefDatabase.Global;
            Scribe.mode = LoadSaveMode.LoadingVars;
        }

        public void FinalizeLoading()
        {
            if (document == null) throw new ScribeException("No load in progress.");
            if (parents.Count != 0)
            {
                Error("Load finished with " + parents.Count + " node(s) still entered.");
            }
            Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
            crossRefs.ResolveAllCrossReferences(this);
            Scribe.mode = LoadSaveMode.PostLoadInit;
            initer.DoAllPostLoadInits(this);
            Scribe.mode = LoadSaveMode.Inactive;
            document = null;
            curParent = null;
            CurExposable = null;
        }

        public bool EnterNode(string nodeName)
        {
            XElement? element = curParent?.Element(nodeName);
            if (element == null) return false;
            PushParent(element);
            return true;
        }

        public void ExitNode()
        {
            if (parents.Count == 0) throw new ScribeException("ExitNode without a matching EnterNode.");
            curParent = parents.Pop();
        }

        internal void PushParent(XElement element)
        {
            parents.Push(curParent ?? throw new ScribeException("No load in progress."));
            curParent = element;
        }

        internal void PopParent() => ExitNode();

        public void Error(string message)
        {
            Errors.Add(message);
        }
    }

    /// <summary>Every <see cref="ILoadReferenceable"/> loaded so far, by id (RimWorld: <c>Verse.LoadedObjectDirectory</c>).</summary>
    public sealed class LoadedObjectDirectory
    {
        private readonly Dictionary<string, ILoadReferenceable> byId = new Dictionary<string, ILoadReferenceable>(StringComparer.Ordinal);

        public int Count => byId.Count;

        public void RegisterLoaded(ILoadReferenceable obj, ScribeLoader loader)
        {
            string id = obj.GetUniqueLoadID();
            if (byId.ContainsKey(id))
            {
                loader.Error("Duplicate load id '" + id + "' (" + obj.GetType().Name + "); the later object wins.");
            }
            byId[id] = obj;
        }

        public T? ObjectWithLoadID<T>(string id) where T : ILoadReferenceable
        {
            if (byId.TryGetValue(id, out ILoadReferenceable obj) && obj is T typed)
            {
                return typed;
            }
            return default;
        }

        public bool Contains(string id) => byId.ContainsKey(id);

        public void Clear() => byId.Clear();
    }

    /// <summary>
    /// Records reference ids during LoadingVars and hands back resolved objects during ResolvingCrossRefs
    /// (RimWorld: <c>Verse.CrossRefHandler</c> + <c>LoadIDsWantedBank</c>). Ids are queued per owning object and
    /// label, in call order, so the second pass reads them back with identical <c>Look</c> calls.
    /// </summary>
    public sealed class CrossRefHandler
    {
        private readonly List<IExposable> crossReferencingExposables = new List<IExposable>();
        private readonly Dictionary<IExposable, Dictionary<string, Queue<string>>> wantedIds =
            new Dictionary<IExposable, Dictionary<string, Queue<string>>>(ReferenceComparer<IExposable>.Instance);
        private readonly Dictionary<IExposable, Dictionary<string, Queue<List<string>>>> wantedIdLists =
            new Dictionary<IExposable, Dictionary<string, Queue<List<string>>>>(ReferenceComparer<IExposable>.Instance);

        public LoadedObjectDirectory loadedObjectDirectory { get; } = new LoadedObjectDirectory();

        public int RegisteredCount => crossReferencingExposables.Count;

        public void RegisterForCrossRefResolution(IExposable exposable)
        {
            crossReferencingExposables.Add(exposable);
        }

        public void RegisterLoadIDReadFromXml(string loadId, string label, IExposable owner)
        {
            Bank(wantedIds, owner, label).Enqueue(loadId);
        }

        public void RegisterLoadIDListReadFromXml(List<string> loadIds, string label, IExposable owner)
        {
            Bank(wantedIdLists, owner, label).Enqueue(loadIds);
        }

        public T? TakeResolvedRef<T>(string label, IExposable owner, ScribeLoader loader) where T : ILoadReferenceable
        {
            if (!TryTake(wantedIds, owner, label, out string? id))
            {
                loader.Error("No recorded reference for '" + label + "' on " + owner.GetType().Name + " — the Look calls differ between passes.");
                return default;
            }
            return Resolve<T>(id, label, owner, loader);
        }

        public List<T>? TakeResolvedRefList<T>(string label, IExposable owner, ScribeLoader loader) where T : ILoadReferenceable
        {
            if (!TryTake(wantedIdLists, owner, label, out List<string>? ids))
            {
                return null;
            }
            var list = new List<T>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                list.Add(Resolve<T>(ids[i], label + "[" + i + "]", owner, loader)!);
            }
            return list;
        }

        public bool HasRecordedList(string label, IExposable owner) =>
            wantedIdLists.TryGetValue(owner, out var byLabel) && byLabel.TryGetValue(label, out var queue) && queue.Count > 0;

        public void ResolveAllCrossReferences(ScribeLoader loader)
        {
            for (int i = 0; i < crossReferencingExposables.Count; i++)
            {
                IExposable exposable = crossReferencingExposables[i];
                loader.CurExposable = exposable;
                exposable.ExposeData();
            }
            loader.CurExposable = null;
        }

        public void Clear()
        {
            crossReferencingExposables.Clear();
            wantedIds.Clear();
            wantedIdLists.Clear();
            loadedObjectDirectory.Clear();
        }

        private T? Resolve<T>(string id, string label, IExposable owner, ScribeLoader loader) where T : ILoadReferenceable
        {
            if (id == "null") return default;
            T? obj = loadedObjectDirectory.ObjectWithLoadID<T>(id);
            if (obj == null)
            {
                loader.Error("Could not resolve reference '" + id + "' for " + owner.GetType().Name + "." + label + ".");
            }
            return obj;
        }

        private static Queue<TItem> Bank<TItem>(Dictionary<IExposable, Dictionary<string, Queue<TItem>>> banks, IExposable owner, string label)
        {
            if (!banks.TryGetValue(owner, out Dictionary<string, Queue<TItem>> byLabel))
            {
                byLabel = new Dictionary<string, Queue<TItem>>(StringComparer.Ordinal);
                banks[owner] = byLabel;
            }
            if (!byLabel.TryGetValue(label, out Queue<TItem> queue))
            {
                queue = new Queue<TItem>();
                byLabel[label] = queue;
            }
            return queue;
        }

        private static bool TryTake<TItem>(Dictionary<IExposable, Dictionary<string, Queue<TItem>>> banks, IExposable owner, string label, out TItem item)
        {
            if (banks.TryGetValue(owner, out var byLabel) && byLabel.TryGetValue(label, out var queue) && queue.Count > 0)
            {
                item = queue.Dequeue();
                return true;
            }
            item = default!;
            return false;
        }
    }

    /// <summary>Runs the PostLoadInit pass over every loaded object (RimWorld: <c>Verse.PostLoadIniter</c>).</summary>
    public sealed class PostLoadIniter
    {
        private readonly List<IExposable> saveablesToPostLoad = new List<IExposable>();

        public int RegisteredCount => saveablesToPostLoad.Count;

        public void RegisterForPostLoadInit(IExposable exposable)
        {
            saveablesToPostLoad.Add(exposable);
        }

        public void DoAllPostLoadInits(ScribeLoader loader)
        {
            for (int i = 0; i < saveablesToPostLoad.Count; i++)
            {
                IExposable exposable = saveablesToPostLoad[i];
                loader.CurExposable = exposable;
                exposable.ExposeData();
            }
            loader.CurExposable = null;
        }

        public void Clear() => saveablesToPostLoad.Clear();
    }
}
