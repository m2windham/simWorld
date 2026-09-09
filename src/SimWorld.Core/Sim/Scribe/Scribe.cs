using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Sim
{
    /// <summary>
    /// Entry point of the save system (RimWorld: <c>Verse.Scribe</c>). The <c>Scribe_*.Look</c> helpers read
    /// <see cref="mode"/> to decide whether to write, read, resolve, or init. State is per thread so tests
    /// and worker threads never share a session; the game uses it from the main thread like RimWorld does.
    /// Typical use: <see cref="SaveToString{T}"/> / <see cref="Load{T}"/>, or drive
    /// <see cref="saver"/> / <see cref="loader"/> directly for custom documents.
    /// </summary>
    public static class Scribe
    {
        [ThreadStatic] private static ScribeSaver? saverInst;
        [ThreadStatic] private static ScribeLoader? loaderInst;
        [ThreadStatic] private static LoadSaveMode modeInst;

        /// <summary>Current phase; the <c>Scribe_*</c> helpers branch on it.</summary>
        public static LoadSaveMode mode
        {
            get => modeInst;
            set => modeInst = value;
        }

        public static ScribeSaver saver => saverInst ??= new ScribeSaver();

        public static ScribeLoader loader => loaderInst ??= new ScribeLoader();

        /// <summary>Groups the Looks that follow under a child element. Returns false when loading and the node is absent.</summary>
        public static bool EnterNode(string nodeName)
        {
            switch (mode)
            {
                case LoadSaveMode.Saving:
                    return saver.EnterNode(nodeName);
                case LoadSaveMode.LoadingVars:
                    return loader.EnterNode(nodeName);
                case LoadSaveMode.ResolvingCrossRefs:
                case LoadSaveMode.PostLoadInit:
                    return true;
                default:
                    return false;
            }
        }

        public static void ExitNode()
        {
            switch (mode)
            {
                case LoadSaveMode.Saving:
                    saver.ExitNode();
                    break;
                case LoadSaveMode.LoadingVars:
                    loader.ExitNode();
                    break;
            }
        }

        /// <summary>Abandons any in-progress session on this thread, releasing a streaming save's writer
        /// and any file handle it owns before the saver itself is dropped.</summary>
        public static void ForceStop()
        {
            modeInst = LoadSaveMode.Inactive;
            saverInst?.Abort();
            saverInst = null;
            loaderInst = null;
        }

        public static XDocument SaveToXDocument<T>(T root, string rootLabel, string documentElementName = "savegame") where T : IExposable
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (mode != LoadSaveMode.Inactive) throw new ScribeException("A Scribe session is already active on this thread (mode " + mode + ").");
            saver.InitSaving(documentElementName);
            try
            {
                T? r = root;
                Scribe_Deep.Look(ref r, rootLabel);
                return saver.FinalizeSaving();
            }
            catch
            {
                ForceStop();
                throw;
            }
        }

        public static string SaveToString<T>(T root, string rootLabel, string documentElementName = "savegame") where T : IExposable
        {
            return SaveToXDocument(root, rootLabel, documentElementName).ToString();
        }

        /// <summary>
        /// Saves straight into <paramref name="stream"/> without ever holding the document: content reaches
        /// the stream as the writer fills, so peak memory is a buffer rather than the save. This is the path
        /// a civilization-sized world wants; <see cref="SaveToString{T}"/> stays the convenient one for a
        /// single object and a test.
        /// </summary>
        /// <param name="leaveOpen">When false the stream is disposed once the save closes.</param>
        public static void SaveToStream<T>(T root, string rootLabel, Stream stream, string documentElementName = "savegame", bool leaveOpen = true) where T : IExposable
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            RequireNoSession();
            saver.InitSaving(stream, documentElementName, leaveOpen);
            StreamRoot(root, rootLabel);
        }

        /// <summary>Saves straight into a file, creating or truncating it. Streams, like
        /// <see cref="SaveToStream{T}"/>; the file handle is closed when the save closes, including on a
        /// throw partway through.</summary>
        public static void SaveToFile<T>(T root, string rootLabel, string filePath, string documentElementName = "savegame") where T : IExposable
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            RequireNoSession();
            saver.InitSavingToFile(filePath, documentElementName);
            StreamRoot(root, rootLabel);
        }

        private static void StreamRoot<T>(T root, string rootLabel) where T : IExposable
        {
            try
            {
                T? r = root;
                Scribe_Deep.Look(ref r, rootLabel);
                saver.FinalizeSavingStreamed();
            }
            catch
            {
                ForceStop();
                throw;
            }
        }

        private static void RequireNoSession()
        {
            if (mode != LoadSaveMode.Inactive) throw new ScribeException("A Scribe session is already active on this thread (mode " + mode + ").");
        }

        /// <summary>Loads a saved root. Non-fatal problems are collected in <paramref name="errors"/>; a missing root is fatal.</summary>
        public static T Load<T>(string xml, string rootLabel, out IReadOnlyList<string> errors, DefDatabase? defs = null) where T : IExposable
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));
            if (mode != LoadSaveMode.Inactive) throw new ScribeException("A Scribe session is already active on this thread (mode " + mode + ").");
            loader.InitLoading(xml, defs);
            try
            {
                T? root = default;
                Scribe_Deep.Look(ref root, rootLabel);
                if (root == null)
                {
                    throw new ScribeException("Save has no <" + rootLabel + "> root element.");
                }
                loader.FinalizeLoading();
                errors = new List<string>(loader.Errors);
                return root;
            }
            catch
            {
                ForceStop();
                throw;
            }
        }

        public static T Load<T>(string xml, string rootLabel, DefDatabase? defs = null) where T : IExposable
        {
            return Load<T>(xml, rootLabel, out _, defs);
        }
    }
}
