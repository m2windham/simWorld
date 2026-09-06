using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace SimWorld.Sim
{
    /// <summary>Builds the save document as the <c>Scribe_*</c> helpers write into it (RimWorld: <c>Verse.ScribeSaver</c>).</summary>
    public sealed class ScribeSaver
    {
        private XDocument? document;
        private XElement? curParent;
        private readonly Stack<XElement> parents = new Stack<XElement>();

        public XElement CurParent => curParent ?? throw new ScribeException("No save in progress.");

        public bool Active => document != null;

        public void InitSaving(string documentElementName)
        {
            if (document != null) throw new ScribeException("InitSaving called while a save is in progress.");
            document = new XDocument(new XElement(documentElementName));
            curParent = document.Root;
            parents.Clear();
            Scribe.mode = LoadSaveMode.Saving;
        }

        public XDocument FinalizeSaving()
        {
            XDocument? doc = document;
            if (doc == null) throw new ScribeException("No save in progress.");
            if (parents.Count != 0) throw new ScribeException("Save finished with " + parents.Count + " node(s) still entered.");
            document = null;
            curParent = null;
            Scribe.mode = LoadSaveMode.Inactive;
            return doc;
        }

        public void SaveToFile(string path)
        {
            FinalizeSaving().Save(path);
        }

        public void SaveToStream(Stream stream)
        {
            FinalizeSaving().Save(stream);
        }

        public bool EnterNode(string nodeName)
        {
            var element = new XElement(nodeName);
            CurParent.Add(element);
            parents.Push(CurParent);
            curParent = element;
            return true;
        }

        public void ExitNode()
        {
            if (parents.Count == 0) throw new ScribeException("ExitNode without a matching EnterNode.");
            curParent = parents.Pop();
        }

        public void WriteElement(string name, string value)
        {
            CurParent.Add(new XElement(name, value));
        }

        public void WriteNullElement(string name)
        {
            CurParent.Add(new XElement(name, new XAttribute("IsNull", "True")));
        }

        /// <summary>Sets an attribute on the node most recently entered.</summary>
        public void WriteAttribute(string name, string value)
        {
            CurParent.SetAttributeValue(name, value);
        }
    }
}
