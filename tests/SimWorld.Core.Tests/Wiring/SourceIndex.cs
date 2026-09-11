using System;
using System.Collections.Generic;
using System.IO;

namespace SimWorld.Tests.Wiring
{
    /// <summary>
    /// A tokenised index of every C# file under a source root, built to answer the one question a compiled
    /// assembly cannot: <i>does anything in the shipped code actually reach this?</i>
    ///
    /// <para/>Reflection knows a method exists; it does not know whether a single line in
    /// <c>src/SimWorld.Core</c> calls it. Roslyn would answer properly, but pulling a compiler into the test
    /// project to ask "is this identifier ever used" is a large dependency for a small question. So this
    /// walks the text instead, with one pass that strips comments, strings and char literals — which matters
    /// more than it sounds, because in this codebase a dormant seam is usually <i>discussed at length in a
    /// doc comment</i> right next to the code that fails to call it. A plain grep finds those mentions and
    /// concludes the seam is wired.
    ///
    /// <para/>What is recorded for each identifier occurrence is just enough context to tell a call from a
    /// declaration and a read from a write: the previous non-space character on the same line, the previous
    /// identifier on that line, and whether the next thing is an argument list or an assignment operator.
    ///
    /// <para/><b>Known limits, deliberately accepted.</b> The index is keyed by bare identifier, with no
    /// notion of the declaring type: two methods sharing a name share an entry, so a call to either counts
    /// for both. It skips interpolated strings whole, so a call appearing <i>only</i> inside <c>$"{…}"</c>
    /// is invisible. Both errors point the same way — they make a seam look more wired than it is, so the
    /// audit under-reports rather than crying wolf. That is the right direction for a test that has to stay
    /// worth reading.
    /// </summary>
    public sealed class SourceIndex
    {
        /// <summary>One identifier occurrence in the indexed sources.</summary>
        public readonly struct Occurrence
        {
            public Occurrence(
                int file, int line, char prev, string? prevToken, bool invoked, bool assigned, bool afterNew, bool afterRef)
            {
                File = file;
                Line = line;
                Prev = prev;
                PrevToken = prevToken;
                Invoked = invoked;
                Assigned = assigned;
                AfterNew = afterNew;
                AfterRef = afterRef;
            }

            /// <summary>Index into <see cref="SourceIndex.Files"/>.</summary>
            public int File { get; }

            public int Line { get; }

            /// <summary>Previous non-whitespace character on the same line; <c>'\0'</c> when first on its line.</summary>
            public char Prev { get; }

            /// <summary>
            /// The most recent identifier earlier on the same line, punctuation ignored — so both the return
            /// type in <c>void Notify_X(</c> and the keyword in <c>typeof(Foo)</c> are visible.
            /// </summary>
            public string? PrevToken { get; }

            /// <summary>Followed by an argument list — <c>Foo(</c> or <c>Foo&lt;T&gt;(</c>.</summary>
            public bool Invoked { get; }

            /// <summary>Followed by <c>=</c> (not <c>==</c> or <c>=&gt;</c>), a compound assignment, or <c>++</c>/<c>--</c>.</summary>
            public bool Assigned { get; }

            /// <summary>
            /// Part of a <c>new …</c> expression, dotted qualification included, so
            /// <c>new SimWorld.Scenario.ScenarioContext()</c> counts as constructing <c>ScenarioContext</c>.
            /// </summary>
            public bool AfterNew { get; }

            /// <summary>
            /// Part of a <c>ref</c>/<c>out</c> argument, dotted qualification included, so both
            /// <c>Look(ref tally, …)</c> and <c>Look(ref tracker.tally, …)</c> count as writing the field.
            /// </summary>
            public bool AfterRef { get; }

            /// <summary>Reached through a member access — <c>thing.Foo</c>.</summary>
            public bool MemberAccess => Prev == '.';

            /// <summary>Written through a <c>ref</c>/<c>out</c> argument, which is how Scribe assigns fields.</summary>
            public bool ByRef => AfterRef;
        }

        /// <summary>
        /// Keywords that can legally sit immediately before an expression. Without them,
        /// <c>return Foo(x);</c> reads exactly like the declaration <c>void Foo(x)</c>: an identifier,
        /// whitespace, then the name and an argument list.
        /// </summary>
        private static readonly HashSet<string> ExpressionKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "return", "await", "throw", "yield", "case", "when", "else", "in", "is", "as", "out", "ref", "new",
            "not", "and", "or", "from", "where", "select", "orderby", "let", "checked", "unchecked", "default",
            "stackalloc", "sizeof", "typeof", "nameof", "lock", "using", "while", "if", "switch", "foreach",
            "for", "catch", "do", "goto",
        };

        /// <summary>
        /// Stands in <see cref="Occurrence.Prev"/> for punctuation that can only precede an expression, where
        /// the raw character would read as part of a declaration.
        /// </summary>
        private const char Operator = '~';

        private static readonly IReadOnlyList<Occurrence> None = new Occurrence[0];

        private readonly List<string> files = new List<string>();
        private readonly Dictionary<string, List<Occurrence>> byName = new Dictionary<string, List<Occurrence>>(StringComparer.Ordinal);

        private SourceIndex()
        {
        }

        /// <summary>Paths of the indexed files, relative to the root they were loaded from.</summary>
        public IReadOnlyList<string> Files => files;

        /// <summary>Total identifier occurrences indexed; a cheap guard against an index that silently loaded nothing.</summary>
        public int TokenCount { get; private set; }

        /// <summary>Indexes every <c>*.cs</c> under <paramref name="root"/>, recursively.</summary>
        public static SourceIndex Load(string root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var index = new SourceIndex();
            string[] paths = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                index.files.Add(relative.Replace('\\', '/'));
                index.IndexText(index.files.Count - 1, File.ReadAllText(path));
            }
            return index;
        }

        /// <summary>Indexes in-memory source texts. Used by the audit's own tests, which need a source tree they control.</summary>
        public static SourceIndex FromTexts(params string[] namesAndTexts)
        {
            if (namesAndTexts == null) throw new ArgumentNullException(nameof(namesAndTexts));
            if (namesAndTexts.Length % 2 != 0) throw new ArgumentException("Expected name/text pairs.", nameof(namesAndTexts));
            var index = new SourceIndex();
            for (int i = 0; i < namesAndTexts.Length; i += 2)
            {
                index.files.Add(namesAndTexts[i]);
                index.IndexText(index.files.Count - 1, namesAndTexts[i + 1]);
            }
            return index;
        }

        public IReadOnlyList<Occurrence> Occurrences(string name)
        {
            List<Occurrence>? list;
            return byName.TryGetValue(name, out list) ? list : None;
        }

        /// <summary>"AI/JobGiver_Work.cs:123", for putting a real line number in a failure message.</summary>
        public string Describe(Occurrence occurrence) =>
            files[occurrence.File] + ":" + occurrence.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// True when some line calls <paramref name="method"/>. A declaration is not a call: the name sitting
        /// after a return type (<c>void Notify_X(</c>) or a modifier is the definition, and the whole point of
        /// the audit is that a definition with no call is what dormancy looks like.
        /// </summary>
        public bool HasCallSite(string method, out string site)
        {
            foreach (Occurrence occurrence in Occurrences(method))
            {
                if (!occurrence.Invoked) continue;
                if (occurrence.PrevToken == "nameof" || occurrence.PrevToken == "typeof") continue;
                if (IsDeclarationShaped(occurrence)) continue;
                site = Describe(occurrence);
                return true;
            }
            site = string.Empty;
            return false;
        }

        /// <summary>True when <c>new <paramref name="typeName"/>(</c> appears anywhere in the indexed sources.</summary>
        public bool IsConstructed(string typeName, out string site)
        {
            foreach (Occurrence occurrence in Occurrences(typeName))
            {
                if (!occurrence.Invoked || !occurrence.AfterNew) continue;
                site = Describe(occurrence);
                return true;
            }
            site = string.Empty;
            return false;
        }

        /// <summary>
        /// True when <c>typeof(<paramref name="typeName"/>)</c> appears. Not proof of a live instance, but a
        /// type handed to <c>Activator</c> or a registry has a plausible route to one, and the audit would
        /// rather stay quiet than report a seam it cannot stand behind.
        /// </summary>
        public bool IsNamedInTypeof(string typeName, out string site)
        {
            foreach (Occurrence occurrence in Occurrences(typeName))
            {
                if (occurrence.PrevToken != "typeof") continue;
                site = Describe(occurrence);
                return true;
            }
            site = string.Empty;
            return false;
        }

        /// <summary>
        /// True when the member's value is read: through a member access (<c>thing.foo</c>) or bare inside its
        /// own type (<c>if (workerClass != null)</c>), which is how a Def reads its own fields and was the
        /// first thing this check got wrong.
        ///
        /// <para/>A bare local variable that happens to share the name counts as a read of the member. That
        /// inflates "something reads it", so a genuinely unread field can hide behind a same-named local —
        /// an under-report, which is the direction the whole audit leans.
        /// </summary>
        public bool IsRead(string member, out string site) => IsRead(member, null, out site);

        /// <summary>
        /// As <see cref="IsRead(string, out string)"/>, but only in files that mention one of
        /// <paramref name="withinTypes"/>.
        ///
        /// <para/>Field names are short and repeat — <c>value</c>, <c>degree</c>, <c>minValue</c>,
        /// <c>listOrder</c> — and a bare identifier index cannot tell <c>QuestNode_Set.value</c> from the
        /// <c>value</c> keyword in a property setter three systems away. Requiring the read to sit in a file
        /// that at least names the declaring type removes that whole class of mistaken sighting.
        /// </summary>
        public bool IsRead(string member, IReadOnlyCollection<string>? withinTypes, out string site)
        {
            HashSet<int>? scope = withinTypes == null ? null : FilesMentioning(withinTypes);
            foreach (Occurrence occurrence in Occurrences(member))
            {
                if (occurrence.Assigned || occurrence.ByRef) continue;
                if (!occurrence.MemberAccess && IsDeclarationShaped(occurrence)) continue;
                if (scope != null && !scope.Contains(occurrence.File)) continue;
                site = Describe(occurrence);
                return true;
            }
            site = string.Empty;
            return false;
        }

        /// <summary>The files in which any of these names appears.</summary>
        private HashSet<int> FilesMentioning(IReadOnlyCollection<string> names)
        {
            var scope = new HashSet<int>();
            foreach (string name in names)
            {
                foreach (Occurrence occurrence in Occurrences(name)) scope.Add(occurrence.File);
            }
            return scope;
        }

        /// <summary>
        /// True when anything assigns the member: <c>x.f = …</c>, a bare <c>f = …</c> inside its own type,
        /// a compound assignment, <c>++</c>, or a <c>ref</c>/<c>out</c> argument (which is how
        /// <c>Scribe_Values.Look(ref f, …)</c> writes a field on load). Broad on purpose: a missed write would
        /// report a live field as dormant, and that is the mistake this audit cannot afford.
        /// </summary>
        public bool IsWritten(string member, out string site)
        {
            foreach (Occurrence occurrence in Occurrences(member))
            {
                if (!occurrence.Assigned && !occurrence.ByRef) continue;
                site = Describe(occurrence);
                return true;
            }
            site = string.Empty;
            return false;
        }

        private static bool IsDeclarationShaped(Occurrence occurrence)
        {
            char p = occurrence.Prev;
            if (p == '\0') return false;                                   // first on the line: a statement call
            if (p == '.') return false;                                    // member access
            if (!(char.IsLetterOrDigit(p) || p == '_' || p == '>' || p == ']' || p == '?')) return false;
            return occurrence.PrevToken == null || !ExpressionKeywords.Contains(occurrence.PrevToken);
        }

        private void Add(string name, Occurrence occurrence)
        {
            List<Occurrence>? list;
            if (!byName.TryGetValue(name, out list))
            {
                list = new List<Occurrence>();
                byName.Add(name, list);
            }
            list.Add(occurrence);
            TokenCount++;
        }

        private void IndexText(int file, string text)
        {
            int i = 0;
            int n = text.Length;
            int line = 1;
            char prev = '\0';
            string? prevToken = null;

            // Whether the identifier about to be read continues a "new Some.Qualified.Name" chain. Tracked
            // across dots so a fully-qualified construction still names the type it builds.
            bool afterNew = false;
            bool afterRef = false;

            while (i < n)
            {
                char c = text[i];

                if (c == '\n')
                {
                    line++;
                    prev = '\0';
                    prevToken = null;
                    i++;
                    continue;
                }
                if (c == '\r' || c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        if (text[i] == '\n') { line++; prev = '\0'; prevToken = null; }
                        i++;
                    }
                    i = Math.Min(n, i + 2);
                    continue;
                }
                if (c == '"' || c == '\'' || (c == '@' && i + 1 < n && text[i + 1] == '"')
                    || (c == '$' && i + 1 < n && (text[i + 1] == '"' || text[i + 1] == '@')))
                {
                    i = SkipLiteral(text, i, ref line);
                    prev = '"';
                    afterNew = false;
                    afterRef = false;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string name = text.Substring(start, i - start);
                    bool invoked, assigned;
                    Lookahead(text, i, out invoked, out assigned);
                    Add(name, new Occurrence(file, line, prev, prevToken, invoked, assigned, afterNew, afterRef));
                    prev = text[i - 1];
                    prevToken = name;
                    if (name == "new") afterNew = true;
                    if (name == "ref" || name == "out") afterRef = true;
                    continue;
                }
                if (char.IsDigit(c))
                {
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    prev = text[i - 1];
                    afterNew = false;
                    afterRef = false;
                    continue;
                }

                // "=>" opens an expression body or a lambda, so what follows is an expression, never a
                // declaration. Left as a bare '>' it looks exactly like the end of a generic return type,
                // and every expression-bodied member in the core — there are hundreds — reads as a
                // declaration instead of the call or field read it is.
                if (c == '=' && i + 1 < n && text[i + 1] == '>')
                {
                    prev = Operator;
                    afterNew = false;
                    afterRef = false;
                    i += 2;
                    continue;
                }

                // '?' is a nullable type marker when it hugs the type ("Thing? thing") and a ternary
                // otherwise ("hurt ? a : b"). Only the first is part of a declaration.
                if (c == '?')
                {
                    char before = i > 0 ? text[i - 1] : '\0';
                    prev = char.IsLetterOrDigit(before) || before == '_' || before == '>' || before == ']' ? '?' : Operator;
                    afterNew = false;
                    afterRef = false;
                    i++;
                    continue;
                }

                prev = c;
                if (c != '.')
                {
                    afterNew = false;
                    afterRef = false;
                }
                i++;
            }
        }

        /// <summary>Skips a string, verbatim string, interpolated string or char literal whole.</summary>
        private static int SkipLiteral(string text, int i, ref int line)
        {
            int n = text.Length;
            bool verbatim = false;
            while (i < n && (text[i] == '$' || text[i] == '@'))
            {
                if (text[i] == '@') verbatim = true;
                i++;
            }
            if (i >= n) return n;

            char quote = text[i];
            i++;
            while (i < n)
            {
                char c = text[i];
                if (c == '\n')
                {
                    line++;

                    // A non-verbatim literal cannot span lines; an unterminated one means the scanner lost its
                    // place, and running off the end of the file is worse than stopping at the newline.
                    if (!verbatim) return i;
                }
                if (verbatim && quote == '"')
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && text[i + 1] == '"') { i += 2; continue; }
                        return i + 1;
                    }
                }
                else
                {
                    if (c == '\\') { i += 2; continue; }
                    if (c == quote) return i + 1;
                }
                i++;
            }
            return n;
        }

        private static void Lookahead(string text, int j, out bool invoked, out bool assigned)
        {
            invoked = false;
            assigned = false;
            int n = text.Length;
            while (j < n && char.IsWhiteSpace(text[j])) j++;
            if (j >= n) return;

            char d = text[j];
            if (d == '(')
            {
                invoked = true;
                return;
            }
            if (d == '<')
            {
                int after = SkipTypeArguments(text, j);
                if (after > 0)
                {
                    while (after < n && char.IsWhiteSpace(text[after])) after++;
                    if (after < n && text[after] == '(') invoked = true;
                }
                return;
            }
            if (d == '=')
            {
                // "==" is a comparison and "=>" is a lambda or an expression body; neither writes anything.
                assigned = j + 1 >= n || (text[j + 1] != '=' && text[j + 1] != '>');
                return;
            }
            if ((d == '+' || d == '-') && j + 1 < n && (text[j + 1] == '=' || text[j + 1] == d))
            {
                assigned = true;
                return;
            }
            if ((d == '*' || d == '/' || d == '|' || d == '&' || d == '^' || d == '%') && j + 1 < n && text[j + 1] == '=')
            {
                assigned = true;
                return;
            }
            if (d == '?' && j + 2 < n && text[j + 1] == '?' && text[j + 2] == '=') assigned = true;
        }

        /// <summary>
        /// Returns the index just past a type-argument list starting at <c>&lt;</c>, or -1 when the text is a
        /// comparison rather than a generic argument list.
        /// </summary>
        private static int SkipTypeArguments(string text, int j)
        {
            int n = text.Length;
            int depth = 0;
            int limit = Math.Min(n, j + 160);
            for (int k = j; k < limit; k++)
            {
                char c = text[k];
                if (c == '<') depth++;
                else if (c == '>')
                {
                    depth--;
                    if (depth == 0) return k + 1;
                }
                else if (!(char.IsLetterOrDigit(c) || c == '_' || c == ',' || c == '.' || c == '?'
                           || c == '[' || c == ']' || char.IsWhiteSpace(c)))
                {
                    return -1;
                }
            }
            return -1;
        }
    }
}
