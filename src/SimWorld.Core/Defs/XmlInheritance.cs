using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace SimWorld.Defs
{
    /// <summary>
    /// Resolves <c>ParentName</c> inheritance between Def XML nodes before they are turned into objects
    /// (RimWorld: <c>Verse.XmlInheritance</c>).
    /// <list type="bullet">
    /// <item><c>Name="X"</c> makes a node addressable as a parent; <c>Abstract="True"</c> nodes are templates only.</item>
    /// <item>A child starts as a deep copy of its resolved parent, then its own elements are applied:
    /// leaf values override, objects merge recursively, lists (<c>&lt;li&gt;</c> children) append.</item>
    /// <item><c>Inherit="False"</c> on a child element replaces the parent's element wholesale.</item>
    /// </list>
    /// </summary>
    public static class XmlInheritance
    {
        public const string NameAttribute = "Name";
        public const string ParentNameAttribute = "ParentName";
        public const string AbstractAttribute = "Abstract";
        public const string InheritAttribute = "Inherit";
        private const string ListItem = "li";

        public sealed class Node
        {
            public XElement Element { get; }
            public string File { get; }
            public string Pack { get; }
            public XElement? Resolved { get; internal set; }
            internal bool Resolving;

            public Node(XElement element, string file, string pack)
            {
                Element = element ?? throw new ArgumentNullException(nameof(element));
                File = file ?? throw new ArgumentNullException(nameof(file));
                Pack = pack ?? throw new ArgumentNullException(nameof(pack));
            }

            public string? Name => Element.Attribute(NameAttribute)?.Value;
            public string? ParentName => Element.Attribute(ParentNameAttribute)?.Value;
            public bool IsAbstract => IsTrue(Element.Attribute(AbstractAttribute)?.Value);
        }

        public static bool IsTrue(string? attributeValue) =>
            attributeValue != null && string.Equals(attributeValue.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        public static bool IsFalse(string? attributeValue) =>
            attributeValue != null && string.Equals(attributeValue.Trim(), "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Fills <see cref="Node.Resolved"/> for every node. Nodes with an unknown parent or an inheritance
        /// cycle get an error and resolve to their own element unchanged.
        /// </summary>
        public static void ResolveAll(IReadOnlyList<Node> nodes, List<DefLoadError> errors)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            var byName = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (Node node in nodes)
            {
                string? name = node.Name;
                if (name == null) continue;
                if (byName.ContainsKey(name))
                {
                    errors.Add(new DefLoadError("Duplicate inheritance Name '" + name + "'; later definition wins.", node.File));
                }
                byName[name] = node;
            }

            foreach (Node node in nodes)
            {
                Resolve(node, byName, errors);
            }
        }

        private static XElement Resolve(Node node, Dictionary<string, Node> byName, List<DefLoadError> errors)
        {
            if (node.Resolved != null)
            {
                return node.Resolved;
            }
            if (node.Resolving)
            {
                errors.Add(new DefLoadError("Inheritance cycle through node '" + (node.Name ?? node.Element.Name.LocalName) + "'.", node.File));
                node.Resolved = new XElement(node.Element);
                return node.Resolved;
            }

            string? parentName = node.ParentName;
            if (parentName == null)
            {
                node.Resolved = new XElement(node.Element);
                return node.Resolved;
            }

            node.Resolving = true;
            try
            {
                if (!byName.TryGetValue(parentName, out Node parent))
                {
                    errors.Add(new DefLoadError("Could not find parent node named '" + parentName + "' for " + Describe(node) + ".", node.File));
                    node.Resolved = new XElement(node.Element);
                    return node.Resolved;
                }
                XElement resolvedParent = Resolve(parent, byName, errors);
                node.Resolved = Merge(resolvedParent, node.Element);
                return node.Resolved;
            }
            finally
            {
                node.Resolving = false;
            }
        }

        /// <summary>Applies <paramref name="child"/> on top of a copy of <paramref name="parent"/>. Child attributes win.</summary>
        public static XElement Merge(XElement parent, XElement child)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (child == null) throw new ArgumentNullException(nameof(child));

            var result = new XElement(child.Name);
            foreach (XAttribute attribute in child.Attributes())
            {
                result.SetAttributeValue(attribute.Name, attribute.Value);
            }
            foreach (XElement parentChild in parent.Elements())
            {
                result.Add(new XElement(parentChild));
            }
            if (!child.HasElements)
            {
                if (!parent.HasElements)
                {
                    result.Value = child.Value;
                }
                return result;
            }

            foreach (XElement childElement in child.Elements())
            {
                bool replace = IsFalse(childElement.Attribute(InheritAttribute)?.Value);
                XElement? existing = result.Element(childElement.Name);
                if (existing == null)
                {
                    result.Add(new XElement(childElement));
                }
                else if (replace)
                {
                    existing.ReplaceWith(new XElement(childElement));
                }
                else if (IsList(childElement) && IsList(existing))
                {
                    foreach (XElement item in childElement.Elements())
                    {
                        existing.Add(new XElement(item));
                    }
                }
                else if (childElement.HasElements && existing.HasElements)
                {
                    existing.ReplaceWith(Merge(existing, childElement));
                }
                else
                {
                    existing.ReplaceWith(new XElement(childElement));
                }
            }
            return result;
        }

        private static bool IsList(XElement element)
        {
            if (!element.HasElements) return false;
            foreach (XElement child in element.Elements())
            {
                if (child.Name.LocalName != ListItem) return false;
            }
            return true;
        }

        private static string Describe(Node node)
        {
            string defName = node.Element.Element("defName")?.Value ?? node.Name ?? "?";
            return node.Element.Name.LocalName + " '" + defName + "'";
        }
    }
}
