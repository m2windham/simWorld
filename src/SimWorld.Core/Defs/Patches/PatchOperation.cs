using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SimWorld.Defs
{
    /// <summary>
    /// A field that keeps its XML instead of being mapped into objects (RimWorld: <c>Verse.XmlContainer</c>).
    /// Patch operations that carry replacement content (<see cref="PatchOperationAdd.value"/> and friends)
    /// need the raw nodes, not a typed object — the content they inject is a fragment of some other Def whose
    /// type this operation knows nothing about.
    /// </summary>
    public sealed class XmlContainer : IXmlCustomLoad
    {
        /// <summary>The <c>&lt;value&gt;</c> element itself; the injected content is its children.</summary>
        public XElement? node;

        public void LoadDataFromXmlCustom(XElement node, XmlLoadContext context)
        {
            this.node = new XElement(node);
        }

        /// <summary>Fresh copies of the contained children, safe to insert into a document.</summary>
        public IEnumerable<XElement> CloneChildren() => node == null
            ? Enumerable.Empty<XElement>()
            : node.Elements().Select(e => new XElement(e));
    }

    /// <summary>
    /// How an operation's own success is reported (RimWorld: <c>Verse.PatchOperation.success</c>). A patch that
    /// legitimately does nothing on some content — an optional tweak, or one half of a conditional — needs to
    /// say so, or every load would report an error for a patch working exactly as written.
    /// </summary>
    public enum PatchOperationSuccess
    {
        /// <summary>Report what actually happened: matching nothing is an error.</summary>
        Normal,

        /// <summary>Always report success, whatever happened.</summary>
        Always,

        /// <summary>Report the opposite of what happened — for a patch that asserts something is absent.</summary>
        Invert,

        /// <summary>Always report failure. RimWorld keeps this for debugging a patch chain.</summary>
        Never,
    }

    /// <summary>
    /// One edit applied to the combined content XML before it becomes Defs (RimWorld:
    /// <c>Verse.PatchOperation</c>). Operations are selected by <c>Class=</c> exactly like every other
    /// polymorphic node in this content system, and run against the whole document, so a pack can edit
    /// another pack's content without owning its files.
    /// <para/>
    /// Patches run <b>before</b> inheritance resolves, the same order RimWorld uses: a patch sees the raw
    /// authored XML — abstract parents, <c>ParentName</c> links and all — rather than the flattened result,
    /// so editing a base def still reaches everything that inherits from it.
    /// </summary>
    public abstract class PatchOperation
    {
        /// <summary>See <see cref="PatchOperationSuccess"/>.</summary>
        public PatchOperationSuccess success = PatchOperationSuccess.Normal;

        /// <summary>Applies the edit and reports whether it did anything.</summary>
        protected abstract bool ApplyWorker(XDocument document);

        /// <summary>Runs the operation and folds <see cref="success"/> into the reported result.</summary>
        public bool Apply(XDocument document)
        {
            bool applied = ApplyWorker(document);
            return success switch
            {
                PatchOperationSuccess.Always => true,
                PatchOperationSuccess.Never => false,
                PatchOperationSuccess.Invert => !applied,
                _ => applied,
            };
        }

        public override string ToString() => GetType().Name;
    }

    /// <summary>
    /// A patch operation addressing nodes by XPath (RimWorld: <c>Verse.PatchOperationPathed</c>). Every
    /// operation that edits existing content derives from this; the ones that only compose others
    /// (<see cref="PatchOperationSequence"/>, <see cref="PatchOperationFindMod"/>) do not.
    /// </summary>
    public abstract class PatchOperationPathed : PatchOperation
    {
        public string xpath = "";

        /// <summary>
        /// The matched elements, materialised before the edit runs. Materialising matters: XPath selection is
        /// lazy, and an operation that removes or replaces what it matched would otherwise be mutating the
        /// collection it is still iterating.
        /// </summary>
        protected List<XElement> Select(XDocument document) =>
            string.IsNullOrWhiteSpace(xpath) ? new List<XElement>() : document.XPathSelectElements(xpath).ToList();
    }
}
