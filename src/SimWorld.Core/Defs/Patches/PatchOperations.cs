using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SimWorld.Defs
{
    /// <summary>Where <see cref="PatchOperationAdd"/> and <see cref="PatchOperationInsert"/> put their content (RimWorld: <c>Verse.PatchOperation.Order</c>).</summary>
    public enum PatchOrder
    {
        Append,
        Prepend,
    }

    /// <summary>Adds children inside every matched node (RimWorld: <c>Verse.PatchOperationAdd</c>).</summary>
    public sealed class PatchOperationAdd : PatchOperationPathed
    {
        public XmlContainer? value;
        public PatchOrder order = PatchOrder.Append;

        protected override bool ApplyWorker(XDocument document)
        {
            List<XElement> matches = Select(document);
            foreach (XElement match in matches)
            {
                List<XElement> content = value?.CloneChildren().ToList() ?? new List<XElement>();
                if (order == PatchOrder.Prepend) match.AddFirst(content);
                else match.Add(content);
            }
            return matches.Count > 0;
        }
    }

    /// <summary>Inserts content beside every matched node (RimWorld: <c>Verse.PatchOperationInsert</c>).</summary>
    public sealed class PatchOperationInsert : PatchOperationPathed
    {
        public XmlContainer? value;

        /// <summary>RimWorld's convention, kept: <see cref="PatchOrder.Prepend"/> inserts <i>before</i> the match, Append <i>after</i> it.</summary>
        public PatchOrder order = PatchOrder.Prepend;

        protected override bool ApplyWorker(XDocument document)
        {
            List<XElement> matches = Select(document);
            foreach (XElement match in matches)
            {
                List<XElement> content = value?.CloneChildren().ToList() ?? new List<XElement>();
                if (order == PatchOrder.Prepend) match.AddBeforeSelf(content);
                else match.AddAfterSelf(content);
            }
            return matches.Count > 0;
        }
    }

    /// <summary>Removes every matched node (RimWorld: <c>Verse.PatchOperationRemove</c>).</summary>
    public sealed class PatchOperationRemove : PatchOperationPathed
    {
        protected override bool ApplyWorker(XDocument document)
        {
            List<XElement> matches = Select(document);
            foreach (XElement match in matches) match.Remove();
            return matches.Count > 0;
        }
    }

    /// <summary>Replaces every matched node with the operation's content (RimWorld: <c>Verse.PatchOperationReplace</c>).</summary>
    public sealed class PatchOperationReplace : PatchOperationPathed
    {
        public XmlContainer? value;

        protected override bool ApplyWorker(XDocument document)
        {
            List<XElement> matches = Select(document);
            foreach (XElement match in matches)
            {
                List<XElement> content = value?.CloneChildren().ToList() ?? new List<XElement>();
                match.ReplaceWith(content);
            }
            return matches.Count > 0;
        }
    }

    /// <summary>Adds an attribute where it is not already present (RimWorld: <c>Verse.PatchOperationAttributeAdd</c>).</summary>
    public sealed class PatchOperationAttributeAdd : PatchOperationPathed
    {
        public string attribute = "";
        public string value = "";

        protected override bool ApplyWorker(XDocument document)
        {
            if (attribute.Length == 0) return false;
            bool any = false;
            foreach (XElement match in Select(document))
            {
                if (match.Attribute(attribute) != null) continue;
                match.SetAttributeValue(attribute, value);
                any = true;
            }
            return any;
        }
    }

    /// <summary>Sets an attribute, overwriting any existing value (RimWorld: <c>Verse.PatchOperationAttributeSet</c>).</summary>
    public sealed class PatchOperationAttributeSet : PatchOperationPathed
    {
        public string attribute = "";
        public string value = "";

        protected override bool ApplyWorker(XDocument document)
        {
            if (attribute.Length == 0) return false;
            List<XElement> matches = Select(document);
            foreach (XElement match in matches) match.SetAttributeValue(attribute, value);
            return matches.Count > 0;
        }
    }

    /// <summary>Removes an attribute (RimWorld: <c>Verse.PatchOperationAttributeRemove</c>).</summary>
    public sealed class PatchOperationAttributeRemove : PatchOperationPathed
    {
        public string attribute = "";

        protected override bool ApplyWorker(XDocument document)
        {
            if (attribute.Length == 0) return false;
            bool any = false;
            foreach (XElement match in Select(document))
            {
                XAttribute? existing = match.Attribute(attribute);
                if (existing == null) continue;
                existing.Remove();
                any = true;
            }
            return any;
        }
    }

    /// <summary>Renames every matched node (RimWorld: <c>Verse.PatchOperationSetName</c>).</summary>
    public sealed class PatchOperationSetName : PatchOperationPathed
    {
        public string name = "";

        protected override bool ApplyWorker(XDocument document)
        {
            if (name.Length == 0) return false;
            List<XElement> matches = Select(document);
            foreach (XElement match in matches) match.Name = name;
            return matches.Count > 0;
        }
    }

    /// <summary>
    /// Runs a list of operations in order (RimWorld: <c>Verse.PatchOperationSequence</c>). Stops at the first
    /// failure, which is what makes a sequence usable as a guard: put the test first and the edits after it.
    /// </summary>
    public sealed class PatchOperationSequence : PatchOperation
    {
        public List<PatchOperation> operations = new List<PatchOperation>();

        protected override bool ApplyWorker(XDocument document)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                if (!operations[i].Apply(document)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Runs one branch or the other depending on whether the XPath matches anything (RimWorld:
    /// <c>Verse.PatchOperationConditional</c>). Neither branch is required; a conditional with only
    /// <see cref="match"/> is the ordinary "patch this if it is there" shape.
    /// </summary>
    public sealed class PatchOperationConditional : PatchOperationPathed
    {
        public PatchOperation? match;
        public PatchOperation? nomatch;

        protected override bool ApplyWorker(XDocument document)
        {
            bool matched = !string.IsNullOrWhiteSpace(xpath) && document.XPathSelectElements(xpath).Any();
            PatchOperation? branch = matched ? match : nomatch;
            return branch == null || branch.Apply(document);
        }
    }

    /// <summary>
    /// Runs one branch or the other depending on whether named content packs are loaded (RimWorld:
    /// <c>Verse.PatchOperationFindMod</c>). Matches on a pack's package id or its name, since content refers
    /// to other content by whichever the author knows.
    /// </summary>
    public sealed class PatchOperationFindMod : PatchOperation
    {
        public List<string> mods = new List<string>();
        public PatchOperation? match;
        public PatchOperation? nomatch;

        /// <summary>
        /// The packs a patch can see. Set by <see cref="DefLoader"/> for the duration of a load, because a
        /// patch operation is built from XML by the object mapper and has no other way to be told what else
        /// is loaded. Static for the same reason RimWorld's <c>ModsConfig</c> is: there is exactly one load
        /// in flight, and threading it through every operation's constructor would buy nothing.
        /// </summary>
        public static IReadOnlyCollection<string> LoadedPackIdentifiers { get; internal set; } = Array.Empty<string>();

        protected override bool ApplyWorker(XDocument document)
        {
            bool found = mods.Any(m => LoadedPackIdentifiers.Contains(m, StringComparer.OrdinalIgnoreCase));
            PatchOperation? branch = found ? match : nomatch;
            return branch == null || branch.Apply(document);
        }
    }
}
