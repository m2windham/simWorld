using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SimWorld.Sim
{
    /// <summary>
    /// Writes the save document as the <c>Scribe_*</c> helpers push into it (RimWorld: <c>Verse.ScribeSaver</c>).
    ///
    /// <para/>RimWorld's own saver only ever streams: it holds an <see cref="XmlWriter"/> over the save file
    /// and a stack of node names, and never materialises the document. This port originally built an
    /// <see cref="XDocument"/> in memory instead, which is fine for a colony and wrong for a civilization —
    /// a world of a million citizens would have to fit its whole save, as live XML objects, in memory beside
    /// the simulation that produced it.
    ///
    /// <para/>So the writer is now an <see cref="XmlWriter"/> in both modes, and the two entry points differ
    /// only in what that writer is attached to:
    /// <list type="bullet">
    /// <item><see cref="InitSaving(string)"/> attaches it to a fresh <see cref="XDocument"/> via
    /// <see cref="XContainer.CreateWriter"/>. <see cref="FinalizeSaving"/> hands the document back, so
    /// <see cref="Scribe.SaveToXDocument{T}"/> and <see cref="Scribe.SaveToString{T}"/> behave exactly as
    /// before — same call shape, same text.</item>
    /// <item><see cref="InitSaving(Stream, string, bool)"/> and <see cref="InitSavingToFile"/> attach it to a
    /// stream, and bytes leave for the sink as the writer's buffer fills rather than at the end. Nothing
    /// larger than that buffer is ever held on the save's behalf; peak memory stops tracking save size.</item>
    /// </list>
    ///
    /// <para/><b>The one rule streaming adds:</b> an attribute must be written before the node it belongs to
    /// gets any child content — an <see cref="XmlWriter"/> cannot reopen a start tag it has already closed,
    /// where <see cref="XElement.SetAttributeValue"/> could. Every writer in this port already obeys it
    /// (<c>Scribe_Deep.Look</c> writes its <c>Class</c> attribute on the line after <see cref="EnterNode"/>),
    /// and <see cref="WriteAttribute"/> now says so out loud: out of order, it throws here rather than
    /// producing a save that silently lost an attribute.
    ///
    /// <para/><b>What this does not change:</b> loading. <see cref="ScribeLoader"/> still parses the whole
    /// document into memory, because the three-pass load (LoadingVars → ResolvingCrossRefs → PostLoadInit)
    /// re-reads nodes after cross-references resolve and a forward-only reader cannot serve that. A streaming
    /// load is a different, larger change and is not this one.
    /// </summary>
    public sealed class ScribeSaver
    {
        private XmlWriter? writer;
        private XDocument? document;
        private Stream? ownedStream;
        private readonly Stack<string> nodeStack = new Stack<string>();
        private bool nodeHasContent;

        /// <summary>True between an <c>InitSaving</c> and its finalizer.</summary>
        public bool Active => writer != null;

        /// <summary>Nodes entered and not yet exited. Zero once the document element itself is exited.</summary>
        public int Depth => nodeStack.Count;

        /// <summary>
        /// Starts an in-memory save. The document is built through the same <see cref="XmlWriter"/> the
        /// streaming path uses, so both modes exercise one code path; <see cref="FinalizeSaving"/> returns it.
        /// </summary>
        public void InitSaving(string documentElementName)
        {
            RequireInactive();
            var doc = new XDocument();
            XmlWriter w = doc.CreateWriter();
            document = doc;
            Begin(w, documentElementName);
        }

        /// <summary>
        /// Starts a streaming save into <paramref name="stream"/>. Content reaches the stream as the writer
        /// fills, not at the end, so the document is never held whole. Finish with
        /// <see cref="FinalizeSavingStreamed"/>.
        /// </summary>
        /// <param name="leaveOpen">When false the stream is disposed with the writer.</param>
        public void InitSaving(Stream stream, string documentElementName, bool leaveOpen = true)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            RequireInactive();
            Begin(XmlWriter.Create(stream, StreamSettings()), documentElementName, declaration: true);
            if (!leaveOpen) ownedStream = stream;
        }

        /// <summary>Starts a streaming save into a file, creating or truncating it. The file handle is owned
        /// here and closed by <see cref="FinalizeSavingStreamed"/> or <see cref="Abort"/>.</summary>
        public void InitSavingToFile(string filePath, string documentElementName)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            RequireInactive();
            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            try
            {
                Begin(XmlWriter.Create(stream, StreamSettings()), documentElementName, declaration: true);
                ownedStream = stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Closes an in-memory save and returns the document. Throws if the save is a streaming one — there
        /// is no document to return, which is the whole point of it.
        /// </summary>
        public XDocument FinalizeSaving()
        {
            if (writer == null) throw new ScribeException("No save in progress.");
            if (document == null) throw new ScribeException("This save is streaming to a sink; finish it with FinalizeSavingStreamed().");
            XDocument doc = document;
            Close();
            return doc;
        }

        /// <summary>Closes a streaming save, flushing the tail of the document to its sink.</summary>
        public void FinalizeSavingStreamed()
        {
            if (writer == null) throw new ScribeException("No save in progress.");
            if (document != null) throw new ScribeException("This save builds a document in memory; finish it with FinalizeSaving().");
            Close();
        }

        /// <summary>
        /// Abandons an in-progress save, releasing the writer and any file handle it owns. Called by
        /// <see cref="Scribe.ForceStop"/> so a throw partway through <c>ExposeData</c> cannot leave a half
        /// written file locked open.
        /// </summary>
        public void Abort()
        {
            try
            {
                writer?.Dispose();
            }
            catch (Exception)
            {
                // Disposing mid-document makes the writer complain about unclosed elements. Abort is the
                // path where that has already happened; releasing the handle is the only thing left to want.
            }
            finally
            {
                ownedStream?.Dispose();
                writer = null;
                document = null;
                ownedStream = null;
                nodeStack.Clear();
                nodeHasContent = false;
            }
        }

        public bool EnterNode(string nodeName)
        {
            RequireActive().WriteStartElement(nodeName);
            nodeStack.Push(nodeName);
            nodeHasContent = false;
            return true;
        }

        public void ExitNode()
        {
            if (nodeStack.Count <= 1) throw new ScribeException("ExitNode without a matching EnterNode.");
            RequireActive().WriteEndElement();
            nodeStack.Pop();
            // The node just closed is content of the node that contained it, so its parent's start tag is
            // now shut for attributes too.
            nodeHasContent = true;
        }

        public void WriteElement(string name, string value)
        {
            RequireActive().WriteElementString(name, value);
            nodeHasContent = true;
        }

        public void WriteNullElement(string name)
        {
            XmlWriter w = RequireActive();
            w.WriteStartElement(name);
            w.WriteAttributeString("IsNull", "True");
            w.WriteEndElement();
            nodeHasContent = true;
        }

        /// <summary>
        /// Sets an attribute on the node most recently entered. Must come before that node has any child
        /// content: a streaming writer has already closed the start tag by then and cannot go back.
        /// </summary>
        public void WriteAttribute(string name, string value)
        {
            if (nodeHasContent)
            {
                throw new ScribeException(
                    "WriteAttribute(\"" + name + "\") after <" + (nodeStack.Count > 0 ? nodeStack.Peek() : "?") +
                    "> already had child content. Write attributes immediately after EnterNode — a streaming save cannot reopen a start tag.");
            }
            RequireActive().WriteAttributeString(name, value);
        }

        private void Begin(XmlWriter w, string documentElementName, bool declaration = false)
        {
            if (documentElementName == null) throw new ArgumentNullException(nameof(documentElementName));
            writer = w;
            nodeStack.Clear();
            nodeHasContent = false;
            try
            {
                if (declaration) w.WriteStartDocument();
                w.WriteStartElement(documentElementName);
                nodeStack.Push(documentElementName);
                Scribe.mode = LoadSaveMode.Saving;
            }
            catch
            {
                Abort();
                throw;
            }
        }

        private void Close()
        {
            if (nodeStack.Count != 1)
            {
                int stranded = nodeStack.Count - 1;
                Abort();
                throw new ScribeException("Save finished with " + stranded + " node(s) still entered.");
            }
            XmlWriter w = writer!;
            w.WriteEndElement();
            w.Flush();
            w.Dispose();
            ownedStream?.Dispose();
            writer = null;
            document = null;
            ownedStream = null;
            nodeStack.Clear();
            nodeHasContent = false;
            Scribe.mode = LoadSaveMode.Inactive;
        }

        private XmlWriter RequireActive() => writer ?? throw new ScribeException("No save in progress.");

        private void RequireInactive()
        {
            if (writer != null) throw new ScribeException("InitSaving called while a save is in progress.");
        }

        /// <summary>
        /// Two-space indentation to match what <see cref="XDocument"/>'s own writer produces, so a streamed
        /// save and an in-memory one differ only by the XML declaration a file gets and a string does not.
        /// UTF-8 without a byte order mark: the loader parses text, and a BOM is only ever in its way.
        /// <c>CloseOutput</c> stays false so stream ownership is decided in one place — here, by whoever
        /// passed the stream in — rather than half by the writer's settings.
        /// </summary>
        private static XmlWriterSettings StreamSettings() => new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
        };
    }
}
