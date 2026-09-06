using System;
using System.Collections.Generic;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Sim
{
    /// <summary>
    /// Saves lists, sets and dictionaries (RimWorld: <c>Verse.Scribe_Collections</c>). Elements are stored as
    /// <c>&lt;li&gt;</c> children in the given <see cref="LookMode"/>; dictionaries as parallel <c>keys</c>/<c>values</c>
    /// lists. Reference-mode collections are rebuilt during ResolvingCrossRefs.
    /// </summary>
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T>? list, string label, LookMode lookMode = LookMode.Undefined, params object[] ctorArgs)
        {
            LookList(ref list, label, label, lookMode, ctorArgs);
        }

        public static void Look<T>(ref HashSet<T>? set, string label, LookMode lookMode = LookMode.Undefined)
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                {
                    List<T>? list = set == null ? null : new List<T>(set);
                    LookList(ref list, label, label, lookMode, null);
                    break;
                }
                case LoadSaveMode.LoadingVars:
                {
                    List<T>? list = null;
                    LookList(ref list, label, label, lookMode, null);
                    set = list == null ? null : new HashSet<T>(list);
                    break;
                }
                case LoadSaveMode.ResolvingCrossRefs:
                {
                    if (ResolveMode<T>(lookMode, label) != LookMode.Reference) return;
                    List<T>? list = null;
                    LookList(ref list, label, label, lookMode, null);
                    if (list != null) set = new HashSet<T>(list);
                    break;
                }
            }
        }

        public static void Look<TKey, TValue>(ref Dictionary<TKey, TValue>? dict, string label, LookMode keyLookMode, LookMode valueLookMode)
            where TKey : notnull
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                {
                    if (dict == null)
                    {
                        Scribe.saver.WriteNullElement(label);
                        return;
                    }
                    var keys = new List<TKey>(dict.Keys);
                    var values = new List<TValue>(dict.Values);
                    Scribe.saver.EnterNode(label);
                    try
                    {
                        List<TKey>? k = keys;
                        List<TValue>? v = values;
                        LookList(ref k, "keys", label + "/keys", keyLookMode, null);
                        LookList(ref v, "values", label + "/values", valueLookMode, null);
                    }
                    finally
                    {
                        Scribe.saver.ExitNode();
                    }
                    break;
                }

                case LoadSaveMode.LoadingVars:
                {
                    ScribeLoader loader = Scribe.loader;
                    XElement? node = loader.CurParent?.Element(label);
                    if (node == null || ScribeExtractor.IsNull(node))
                    {
                        dict = null;
                        return;
                    }
                    List<TKey>? keys = null;
                    List<TValue>? values = null;
                    loader.PushParent(node);
                    try
                    {
                        LookList(ref keys, "keys", label + "/keys", keyLookMode, null);
                        LookList(ref values, "values", label + "/values", valueLookMode, null);
                    }
                    finally
                    {
                        loader.PopParent();
                    }
                    bool deferred = keyLookMode == LookMode.Reference || valueLookMode == LookMode.Reference;
                    if (deferred)
                    {
                        // Parts with references are completed in ResolvingCrossRefs; keep the rest until then.
                        PendingParts.Store(loader, label, keys, values);
                        dict = null;
                    }
                    else
                    {
                        dict = BuildDictionary(keys, values, label, loader);
                    }
                    break;
                }

                case LoadSaveMode.ResolvingCrossRefs:
                {
                    if (keyLookMode != LookMode.Reference && valueLookMode != LookMode.Reference) return;
                    ScribeLoader loader = Scribe.loader;
                    if (!PendingParts.Take(loader, label, out object? storedKeys, out object? storedValues)) return;
                    var keys = (List<TKey>?)storedKeys;
                    var values = (List<TValue>?)storedValues;
                    if (keyLookMode == LookMode.Reference) LookList(ref keys, "keys", label + "/keys", keyLookMode, null);
                    if (valueLookMode == LookMode.Reference) LookList(ref values, "values", label + "/values", valueLookMode, null);
                    dict = BuildDictionary(keys, values, label, loader);
                    break;
                }
            }
        }

        private static Dictionary<TKey, TValue>? BuildDictionary<TKey, TValue>(List<TKey>? keys, List<TValue>? values, string label, ScribeLoader loader)
            where TKey : notnull
        {
            if (keys == null || values == null)
            {
                loader.Error("Dictionary '" + label + "' is missing its keys or values list.");
                return null;
            }
            if (keys.Count != values.Count)
            {
                loader.Error("Dictionary '" + label + "' has " + keys.Count + " keys but " + values.Count + " values.");
            }
            var dict = new Dictionary<TKey, TValue>();
            int count = Math.Min(keys.Count, values.Count);
            for (int i = 0; i < count; i++)
            {
                if (keys[i] == null)
                {
                    loader.Error("Dictionary '" + label + "' has a null key at index " + i + "; entry skipped.");
                    continue;
                }
                dict[keys[i]] = values[i];
            }
            return dict;
        }

        private static LookMode ResolveMode<T>(LookMode requested, string label)
        {
            return ScribeExtractor.ResolveLookMode(typeof(T), requested, label, Scribe.mode == LoadSaveMode.Saving ? null : Scribe.loader);
        }

        /// <param name="xmlLabel">Element name in the document.</param>
        /// <param name="bankLabel">Key for recorded references; distinct per collection within an owner.</param>
        private static void LookList<T>(ref List<T>? list, string xmlLabel, string bankLabel, LookMode requestedMode, object[]? ctorArgs)
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                {
                    LookMode mode = ResolveMode<T>(requestedMode, xmlLabel);
                    if (list == null)
                    {
                        Scribe.saver.WriteNullElement(xmlLabel);
                        return;
                    }
                    Scribe.saver.EnterNode(xmlLabel);
                    try
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            SaveItem(list[i], mode);
                        }
                    }
                    finally
                    {
                        Scribe.saver.ExitNode();
                    }
                    break;
                }

                case LoadSaveMode.LoadingVars:
                {
                    ScribeLoader loader = Scribe.loader;
                    LookMode mode = ResolveMode<T>(requestedMode, xmlLabel);
                    XElement? node = loader.CurParent?.Element(xmlLabel);
                    if (node == null || ScribeExtractor.IsNull(node))
                    {
                        list = null;
                        return;
                    }
                    list = new List<T>();
                    List<string>? ids = mode == LookMode.Reference ? new List<string>() : null;
                    foreach (XElement item in node.Elements())
                    {
                        switch (mode)
                        {
                            case LookMode.Value:
                                list.Add(ScribeExtractor.ValueFromNode<T>(item, default!, xmlLabel, loader));
                                break;
                            case LookMode.Def:
                                list.Add((T)(object)LoadDef(typeof(T), item, xmlLabel, loader)!);
                                break;
                            case LookMode.Deep:
                                list.Add(LoadDeep<T>(item, ctorArgs, xmlLabel, loader)!);
                                break;
                            case LookMode.Reference:
                                ids!.Add(ScribeExtractor.IsNull(item) ? "null" : item.Value.Trim());
                                list.Add(default!);
                                break;
                        }
                    }
                    if (ids != null)
                    {
                        IExposable? owner = loader.CurExposable;
                        if (owner == null)
                        {
                            loader.Error("Reference list '" + xmlLabel + "' loaded outside an object's ExposeData.");
                        }
                        else
                        {
                            loader.crossRefs.RegisterLoadIDListReadFromXml(ids, bankLabel, owner);
                        }
                    }
                    break;
                }

                case LoadSaveMode.ResolvingCrossRefs:
                {
                    if (ResolveMode<T>(requestedMode, xmlLabel) != LookMode.Reference) return;
                    ScribeLoader loader = Scribe.loader;
                    IExposable? owner = loader.CurExposable;
                    if (owner == null || !loader.crossRefs.HasRecordedList(bankLabel, owner)) return;
                    list = TakeReferenceList<T>(bankLabel, owner, loader);
                    break;
                }
            }
        }

        private static void SaveItem<T>(T item, LookMode mode)
        {
            switch (mode)
            {
                case LookMode.Value:
                    if (item == null) Scribe.saver.WriteNullElement("li");
                    else Scribe.saver.WriteElement("li", ScribeExtractor.ValueToString(item));
                    break;
                case LookMode.Def:
                    Scribe.saver.WriteElement("li", item is Def def ? def.defName : "null");
                    break;
                case LookMode.Deep:
                {
                    var exposable = item as IExposable;
                    Scribe_Deep.Look(ref exposable, "li");
                    break;
                }
                case LookMode.Reference:
                    Scribe.saver.WriteElement("li", item is ILoadReferenceable r ? r.GetUniqueLoadID() : "null");
                    break;
            }
        }

        private static Def? LoadDef(Type defType, XElement item, string label, ScribeLoader loader)
        {
            string name = item.Value.Trim();
            if (name.Length == 0 || name == "null" || ScribeExtractor.IsNull(item)) return null;
            Def? def = loader.Defs.GetNamed(defType, name);
            if (def == null)
            {
                loader.Error("Could not load reference to " + defType.Name + " named '" + name + "' in list '" + label + "'.");
            }
            return def;
        }

        private static T? LoadDeep<T>(XElement item, object[]? ctorArgs, string label, ScribeLoader loader)
        {
            // T may be declared as a non-IExposable interface/base; the generic helper needs the constraint.
            var method = typeof(ScribeExtractor).GetMethod(nameof(ScribeExtractor.SaveableFromNode))!.MakeGenericMethod(typeof(T));
            return (T?)method.Invoke(null, new object?[] { item, ctorArgs, label, loader });
        }

        private static List<T>? TakeReferenceList<T>(string bankLabel, IExposable owner, ScribeLoader loader)
        {
            var method = typeof(CrossRefHandler).GetMethod(nameof(CrossRefHandler.TakeResolvedRefList))!.MakeGenericMethod(typeof(T));
            return (List<T>?)method.Invoke(loader.crossRefs, new object[] { bankLabel, owner, loader });
        }

        /// <summary>Dictionary halves parked between LoadingVars and ResolvingCrossRefs, per owner and label.</summary>
        private static class PendingParts
        {
            [ThreadStatic] private static Dictionary<IExposable, Dictionary<string, (object? keys, object? values)>>? store;

            public static void Store(ScribeLoader loader, string label, object? keys, object? values)
            {
                IExposable? owner = loader.CurExposable;
                if (owner == null)
                {
                    loader.Error("Reference dictionary '" + label + "' loaded outside an object's ExposeData.");
                    return;
                }
                store ??= new Dictionary<IExposable, Dictionary<string, (object?, object?)>>(ReferenceComparer<IExposable>.Instance);
                if (!store.TryGetValue(owner, out var byLabel))
                {
                    byLabel = new Dictionary<string, (object?, object?)>(StringComparer.Ordinal);
                    store[owner] = byLabel;
                }
                byLabel[label] = (keys, values);
            }

            public static bool Take(ScribeLoader loader, string label, out object? keys, out object? values)
            {
                keys = null;
                values = null;
                IExposable? owner = loader.CurExposable;
                if (owner == null || store == null || !store.TryGetValue(owner, out var byLabel) || !byLabel.TryGetValue(label, out var parts))
                {
                    return false;
                }
                byLabel.Remove(label);
                keys = parts.keys;
                values = parts.values;
                return true;
            }
        }
    }
}
