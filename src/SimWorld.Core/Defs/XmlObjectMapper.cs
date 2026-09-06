using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace SimWorld.Defs
{
    /// <summary>
    /// Implemented by types that read their own XML instead of field-by-name mapping
    /// (RimWorld: the <c>LoadDataFromXmlCustom</c> convention). See <see cref="StatModifier"/>.
    /// </summary>
    public interface IXmlCustomLoad
    {
        void LoadDataFromXmlCustom(XElement node, XmlLoadContext context);
    }

    /// <summary>State shared across one load pass; handed to <see cref="IXmlCustomLoad"/> implementations.</summary>
    public sealed class XmlLoadContext
    {
        public DefTypeResolver Types { get; }
        public List<DefLoadError> Errors { get; }
        public CrossRefRegistry CrossRefs { get; }
        public XmlObjectMapper Mapper { get; }

        /// <summary>File being loaded, for error messages.</summary>
        public string? FileName { get; set; }

        /// <summary>defName of the Def being loaded, for error messages.</summary>
        public string? CurrentDefName { get; set; }

        internal XmlLoadContext(DefTypeResolver types, List<DefLoadError> errors, CrossRefRegistry crossRefs, XmlObjectMapper mapper)
        {
            Types = types;
            Errors = errors;
            CrossRefs = crossRefs;
            Mapper = mapper;
        }

        public void Error(string message)
        {
            Errors.Add(new DefLoadError(message, FileName, CurrentDefName));
        }

        /// <summary>Defers setting <paramref name="fieldName"/> on <paramref name="target"/> to the Def named <paramref name="defName"/>.</summary>
        public void RegisterObjectWantsCrossRef(object target, string fieldName, string defName)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Error("No field '" + fieldName + "' on " + target.GetType().Name + " to receive cross-reference '" + defName + "'.");
                return;
            }
            CrossRefs.WantField(target, field, defName, Describe(target) + "." + fieldName, FileName);
        }

        internal string Describe(object target)
        {
            string owner = CurrentDefName != null ? " in " + CurrentDefName : "";
            return target.GetType().Name + owner;
        }
    }

    /// <summary>
    /// Builds objects from XML by reflection (RimWorld: <c>Verse.DirectXmlToObject</c>): public fields by
    /// element name, <c>&lt;li&gt;</c> lists, <c>&lt;li&gt;&lt;key/&gt;&lt;value/&gt;&lt;/li&gt;</c> dictionaries,
    /// <c>Class="..."</c> polymorphism, <c>Type</c>-valued fields, and deferred Def references.
    /// </summary>
    public sealed class XmlObjectMapper
    {
        private static readonly HashSet<string> IgnoredAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            XmlInheritance.NameAttribute, XmlInheritance.ParentNameAttribute, XmlInheritance.AbstractAttribute,
            XmlInheritance.InheritAttribute, "Class", "IsNull", "MayRequire",
        };

        private readonly Dictionary<Type, Dictionary<string, FieldInfo>> fieldCache = new Dictionary<Type, Dictionary<string, FieldInfo>>();

        public XmlLoadContext Context { get; }

        public XmlObjectMapper(DefTypeResolver types, List<DefLoadError> errors, CrossRefRegistry crossRefs)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (crossRefs == null) throw new ArgumentNullException(nameof(crossRefs));
            Context = new XmlLoadContext(types, errors, crossRefs, this);
        }

        public T? ObjectFromXml<T>(XElement node) where T : class => ObjectFromXml(node, typeof(T)) as T;

        /// <summary>Builds a <paramref name="type"/> from <paramref name="node"/>. Errors are recorded, never thrown.</summary>
        public object? ObjectFromXml(XElement node, Type type)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (XmlInheritance.IsTrue(node.Attribute("IsNull")?.Value))
            {
                return null;
            }

            string? className = node.Attribute("Class")?.Value;
            if (className != null)
            {
                Type? explicitType = Context.Types.GetTypeInAnyAssembly(className);
                if (explicitType == null)
                {
                    Context.Error("Could not find type '" + className + "' for <" + node.Name.LocalName + ">.");
                    return null;
                }
                if (!type.IsAssignableFrom(explicitType))
                {
                    Context.Error("Type '" + className + "' is not a " + type.Name + " (at <" + node.Name.LocalName + ">).");
                    return null;
                }
                type = explicitType;
            }

            if (typeof(Def).IsAssignableFrom(type))
            {
                if (node.HasElements)
                {
                    return BuildObject(node, type);
                }
                Context.Error("Def reference <" + node.Name.LocalName + "> must be registered as a cross-reference, not built inline.");
                return null;
            }

            if (type == typeof(Type))
            {
                string name = node.Value.Trim();
                Type? resolved = Context.Types.GetTypeInAnyAssembly(name);
                if (resolved == null)
                {
                    Context.Error("Could not find type '" + name + "' for <" + node.Name.LocalName + ">.");
                }
                return resolved;
            }

            if (type == typeof(string))
            {
                return node.Value.Trim();
            }

            if (ParseHelper.CanParse(type))
            {
                try
                {
                    return ParseHelper.FromString(node.Value, type);
                }
                catch (Exception e)
                {
                    Context.Error("Could not parse '" + node.Value.Trim() + "' as " + type.Name + " for <" + node.Name.LocalName + ">: " + e.Message);
                    return DefaultOf(type);
                }
            }

            if (typeof(IXmlCustomLoad).IsAssignableFrom(type))
            {
                object? custom = Create(type, node);
                if (custom != null)
                {
                    ((IXmlCustomLoad)custom).LoadDataFromXmlCustom(node, Context);
                }
                return custom;
            }

            if (IsGenericList(type))
            {
                return BuildList(node, type);
            }

            if (IsGenericDictionary(type))
            {
                return BuildDictionary(node, type);
            }

            if (type.IsArray)
            {
                Context.Error("Arrays are not supported (<" + node.Name.LocalName + ">); use a List.");
                return null;
            }

            return BuildObject(node, type);
        }

        private object? BuildObject(XElement node, Type type)
        {
            object? instance = Create(type, node);
            if (instance == null)
            {
                return null;
            }
            foreach (XElement child in node.Elements())
            {
                SetFieldFromXml(instance, type, child);
            }
            return instance;
        }

        private void SetFieldFromXml(object target, Type type, XElement child)
        {
            FieldInfo? field = FindField(type, child.Name.LocalName);
            if (field == null)
            {
                Context.Error("XML element <" + child.Name.LocalName + "> doesn't correspond to any field in type " + type.Name + ".");
                return;
            }

            Type fieldType = field.FieldType;
            if (typeof(Def).IsAssignableFrom(fieldType) && !child.HasElements)
            {
                Context.CrossRefs.WantField(target, field, child.Value.Trim(), Context.Describe(target) + "." + field.Name, Context.FileName);
                return;
            }

            object? value = ObjectFromXml(child, fieldType);
            if (value == null && fieldType.IsValueType && Nullable.GetUnderlyingType(fieldType) == null)
            {
                return; // error already recorded; leave the default in place
            }
            try
            {
                field.SetValue(target, value);
            }
            catch (Exception e)
            {
                Context.Error("Could not assign <" + child.Name.LocalName + "> to " + type.Name + "." + field.Name + ": " + e.Message);
            }
        }

        private object? BuildList(XElement node, Type listType)
        {
            Type itemType = listType.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(listType)!;
            if (!node.HasElements)
            {
                if (node.Value.Trim().Length > 0)
                {
                    Context.Error("<" + node.Name.LocalName + "> is a list and needs <li> children, not text.");
                }
                return list;
            }
            // Child element names are not checked: <li> is the convention, but RimWorld content also
            // uses the element name as data (<statBases><MaxHitPoints>100</MaxHitPoints></statBases>),
            // which IXmlCustomLoad item types read from the node they are handed.
            foreach (XElement item in node.Elements())
            {
                if (typeof(Def).IsAssignableFrom(itemType) && !item.HasElements)
                {
                    int index = list.Add(null);
                    Context.CrossRefs.WantListItem(list, index, itemType, item.Value.Trim(), "list <" + node.Name.LocalName + ">" + OwnerSuffix(), Context.FileName);
                    continue;
                }
                object? value = ObjectFromXml(item, itemType);
                if (value == null && !XmlInheritance.IsTrue(item.Attribute("IsNull")?.Value))
                {
                    continue; // the item failed to build; its error is already recorded
                }
                list.Add(value);
            }
            return list;
        }

        private object? BuildDictionary(XElement node, Type dictType)
        {
            Type[] args = dictType.GetGenericArguments();
            Type keyType = args[0];
            Type valueType = args[1];
            var dictionary = (IDictionary)Activator.CreateInstance(dictType)!;
            foreach (XElement item in node.Elements())
            {
                XElement? keyNode = item.Element("key");
                XElement? valueNode = item.Element("value");
                if (keyNode == null || valueNode == null)
                {
                    Context.Error("Dictionary entry in <" + node.Name.LocalName + "> needs both <key> and <value>.");
                    continue;
                }
                if (typeof(Def).IsAssignableFrom(keyType))
                {
                    Context.Error("Def-typed dictionary keys are not supported (<" + node.Name.LocalName + ">).");
                    continue;
                }
                object? key = ObjectFromXml(keyNode, keyType);
                if (key == null)
                {
                    continue;
                }
                if (typeof(Def).IsAssignableFrom(valueType) && !valueNode.HasElements)
                {
                    dictionary[key] = null;
                    Context.CrossRefs.WantDictionaryValue(dictionary, key, valueType, valueNode.Value.Trim(), "dictionary <" + node.Name.LocalName + ">" + OwnerSuffix(), Context.FileName);
                    continue;
                }
                object? value = ObjectFromXml(valueNode, valueType);
                if (value == null && !XmlInheritance.IsTrue(valueNode.Attribute("IsNull")?.Value))
                {
                    continue; // the value failed to build; its error is already recorded
                }
                dictionary[key] = value;
            }
            return dictionary;
        }

        private string OwnerSuffix() => Context.CurrentDefName != null ? " in " + Context.CurrentDefName : "";

        private object? Create(Type type, XElement node)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                Context.Error("Cannot instantiate abstract type " + type.Name + " for <" + node.Name.LocalName + ">; add a Class attribute.");
                return null;
            }
            try
            {
                return Activator.CreateInstance(type, nonPublic: true);
            }
            catch (Exception e)
            {
                Context.Error("Could not construct " + type.Name + " for <" + node.Name.LocalName + ">: " + (e.InnerException ?? e).Message);
                return null;
            }
        }

        private FieldInfo? FindField(Type type, string name)
        {
            if (!fieldCache.TryGetValue(type, out Dictionary<string, FieldInfo> fields))
            {
                fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (field.IsInitOnly)
                    {
                        continue;
                    }
                    var unsaved = field.GetCustomAttribute<UnsavedAttribute>();
                    if (unsaved != null && !unsaved.AllowLoading)
                    {
                        continue;
                    }
                    fields[field.Name] = field;
                    foreach (LoadAliasAttribute alias in field.GetCustomAttributes<LoadAliasAttribute>())
                    {
                        if (!fields.ContainsKey(alias.Alias))
                        {
                            fields[alias.Alias] = field;
                        }
                    }
                }
                fieldCache[type] = fields;
            }
            return fields.TryGetValue(name, out FieldInfo found) ? found : null;
        }

        private static bool IsGenericList(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

        private static bool IsGenericDictionary(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);

        private static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        /// <summary>Attributes that carry loader instructions rather than data.</summary>
        public static bool IsLoaderAttribute(XAttribute attribute) => IgnoredAttributes.Contains(attribute.Name.LocalName);
    }
}
