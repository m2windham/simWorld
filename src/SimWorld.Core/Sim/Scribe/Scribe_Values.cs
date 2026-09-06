using System.Collections.Generic;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Sim
{
    /// <summary>Saves and loads plain values: primitives, strings, enums, and anything <see cref="ParseHelper"/> parses.</summary>
    public static class Scribe_Values
    {
        /// <summary>
        /// Values equal to <paramref name="defaultValue"/> are omitted from the save (and restored to it on load)
        /// unless <paramref name="forceSave"/> is set — RimWorld's space-saving default.
        /// </summary>
        public static void Look<T>(ref T value, string label, T defaultValue = default!, bool forceSave = false)
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                    if (!forceSave && EqualityComparer<T>.Default.Equals(value, defaultValue))
                    {
                        return;
                    }
                    if (value == null)
                    {
                        Scribe.saver.WriteNullElement(label);
                    }
                    else
                    {
                        Scribe.saver.WriteElement(label, ScribeExtractor.ValueToString(value));
                    }
                    break;

                case LoadSaveMode.LoadingVars:
                    ScribeLoader loader = Scribe.loader;
                    XElement? node = loader.CurParent?.Element(label);
                    value = ScribeExtractor.ValueFromNode(node, defaultValue, label, loader);
                    break;
            }
        }
    }

    /// <summary>Saves a Def by name and loads it from the session's <see cref="DefDatabase"/>.</summary>
    public static class Scribe_Defs
    {
        public static void Look<T>(ref T? value, string label) where T : Def
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                    Scribe.saver.WriteElement(label, value == null ? "null" : value.defName);
                    break;

                case LoadSaveMode.LoadingVars:
                    ScribeLoader loader = Scribe.loader;
                    value = ScribeExtractor.DefFromNode<T>(loader.CurParent?.Element(label), label, loader);
                    break;
            }
        }
    }

    /// <summary>
    /// Saves an owned object inline as a child element and reconstructs it on load. The runtime type is
    /// written as a <c>Class</c> attribute when it differs from the declared type, so polymorphic fields
    /// (a <c>Thing</c> field holding a <c>Pawn</c>) round-trip.
    /// </summary>
    public static class Scribe_Deep
    {
        public static void Look<T>(ref T? target, string label, params object[] ctorArgs) where T : IExposable
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                    if (target == null)
                    {
                        Scribe.saver.WriteNullElement(label);
                        return;
                    }
                    Scribe.saver.EnterNode(label);
                    try
                    {
                        System.Type runtimeType = target.GetType();
                        if (runtimeType != typeof(T))
                        {
                            Scribe.saver.WriteAttribute("Class", runtimeType.FullName ?? runtimeType.Name);
                        }
                        target.ExposeData();
                    }
                    finally
                    {
                        Scribe.saver.ExitNode();
                    }
                    break;

                case LoadSaveMode.LoadingVars:
                    ScribeLoader loader = Scribe.loader;
                    target = ScribeExtractor.SaveableFromNode<T>(loader.CurParent?.Element(label), ctorArgs, label, loader);
                    break;
            }
        }
    }

    /// <summary>
    /// Saves a pointer to an object that is deep-saved elsewhere, by its load id, and re-links it during
    /// ResolvingCrossRefs — after every object in the save exists. Forward references are fine.
    /// </summary>
    public static class Scribe_References
    {
        public static void Look<T>(ref T? refee, string label) where T : ILoadReferenceable
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                    Scribe.saver.WriteElement(label, refee == null ? "null" : refee.GetUniqueLoadID());
                    break;

                case LoadSaveMode.LoadingVars:
                {
                    ScribeLoader loader = Scribe.loader;
                    IExposable? owner = loader.CurExposable;
                    if (owner == null)
                    {
                        loader.Error("Scribe_References.Look('" + label + "') called outside an object's ExposeData.");
                        return;
                    }
                    XElement? node = loader.CurParent?.Element(label);
                    string id = node == null || ScribeExtractor.IsNull(node) ? "null" : node.Value.Trim();
                    loader.crossRefs.RegisterLoadIDReadFromXml(id, label, owner);
                    break;
                }

                case LoadSaveMode.ResolvingCrossRefs:
                {
                    ScribeLoader loader = Scribe.loader;
                    IExposable? owner = loader.CurExposable;
                    if (owner == null) return;
                    refee = loader.crossRefs.TakeResolvedRef<T>(label, owner, loader);
                    break;
                }
            }
        }
    }
}
