using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Wiring;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Content
{
    /// <summary>
    /// The banned-term list in <c>player-text-banned.txt</c>, loaded once and compiled into one matcher.
    ///
    /// <para/>The list lives in a text file rather than in this class so the host's EditMode test can read
    /// the very same file beside the core package it builds against: one list, two repositories enforcing it.
    /// </summary>
    public static class PlayerTextTerms
    {
        public static string ListPath =>
            Path.Combine(CoreUnderAudit.RepositoryRoot, "tests", "SimWorld.Core.Tests", "Content", "player-text-banned.txt");

        private static readonly Lazy<IReadOnlyList<string>> LazyTerms = new Lazy<IReadOnlyList<string>>(() =>
            File.ReadAllLines(ListPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                .ToList());

        private static readonly Lazy<Regex> LazyMatcher = new Lazy<Regex>(() => Compile(Terms));

        public static IReadOnlyList<string> Terms => LazyTerms.Value;

        /// <summary>
        /// Whole-word and case-insensitive: a letter, digit or underscore on either side is not a boundary, so
        /// a defName such as <c>ManhunterPack</c> or <c>Cassandra_Classic</c> never matches while "manhunter
        /// pack" does. A space in a term matches any run of whitespace.
        /// </summary>
        public static Regex Compile(IEnumerable<string> terms)
        {
            string alternatives = string.Join("|", terms
                .OrderByDescending(t => t.Length)
                .Select(t => string.Join(@"\s+", t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))));
            return new Regex(@"(?<![\p{L}\p{N}_])(?:" + alternatives + @")(?![\p{L}\p{N}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Every banned term in <paramref name="text"/>, lower-cased, or empty.</summary>
        public static IReadOnlyList<string> Find(string? text) =>
            string.IsNullOrEmpty(text)
                ? Array.Empty<string>()
                : LazyMatcher.Value.Matches(text).Select(m => Regex.Replace(m.Value, @"\s+", " ").ToLowerInvariant()).Distinct().ToList();
    }

    /// <summary>
    /// Nothing a player can read names RimWorld (Doug, 2026-11-24). Three doors carry text to a player, and
    /// each gets its own check: the shipped defs (labels, descriptions, letter text, name banks, storyteller
    /// blurbs, faction names — every string a def holds, however deeply nested), the calendar, and the
    /// English hard-coded in the core's C# (alerts, letters, command answers, name banks in code).
    /// </summary>
    public class PlayerTextTests : ContentTestBase
    {
        /// <summary>Fields that name a def or a file rather than show a player anything.</summary>
        private static readonly HashSet<string> InternalFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(Def.defName), nameof(Def.fileName), nameof(Def.packName),
        };

        public PlayerTextTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void The_banned_list_is_loaded_and_holds_the_terms_it_was_written_for()
        {
            Assert.True(File.Exists(PlayerTextTerms.ListPath), PlayerTextTerms.ListPath + " is missing.");
            Assert.True(PlayerTextTerms.Terms.Count > 50, "The banned list is nearly empty, so every check here would pass.");
            foreach (string term in new[] { "cassandra", "aprimay", "muffalo", "manhunter", "colonist", "rimworld", "plasteel" })
            {
                Assert.Contains(term, PlayerTextTerms.Terms);
            }
        }

        [Theory]
        [InlineData("Cassandra Classic", "cassandra classic")]
        [InlineData("told by Cassandra", "cassandra")]
        [InlineData("Day 3 of Aprimay, 5500", "aprimay")]
        [InlineData("A pack of 4 muffalo has turned manhunter.", "manhunter")]
        [InlineData("COLONISTS idle", "colonists")]
        [InlineData("a raid, a Manhunter\npack", "manhunter")]
        [InlineData("severe yayo withdrawal", "yayo")]
        [InlineData("Minor break risk", "break risk")]
        [InlineData("There is a fire in the Home  Area.", "home area")]
        [InlineData("Cooking a fine meal.", "fine meal")]
        [InlineData("Kind: Tribesperson", "tribesperson")]
        [InlineData("Constr.", "constr.")]
        public void The_matcher_finds_a_banned_term_in_any_case(string text, string term)
        {
            Assert.Contains(term, PlayerTextTerms.Find(text));
        }

        [Theory]
        [InlineData("ManhunterPack")]
        [InlineData("Cassandra_Classic")]
        [InlineData("classical")]
        [InlineData("a trimmed hedge")]
        [InlineData("snowflake")]
        [InlineData("maddened beasts")]
        [InlineData("a break in the risk")]
        [InlineData("the home of the area")]
        [InlineData("construction")]
        public void The_matcher_leaves_identifiers_and_ordinary_words_alone(string text)
        {
            Assert.Empty(PlayerTextTerms.Find(text));
        }

        [Fact]
        public void No_shipped_def_shows_a_player_a_banned_term()
        {
            var defNames = new HashSet<string>(Content.Database.AllDefs.Select(d => d.defName), StringComparer.Ordinal);
            var checkedTexts = new HashSet<string>(StringComparer.Ordinal);
            var offences = new List<string>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

            foreach (Def def in CoreUnderAudit.ShippedDefs(Content.Database))
            {
                Walk(def, def.GetType().Name + ":" + def.defName, root: true, defNames, checkedTexts, offences, seen);
            }

            // Prove the walk reached the nested places player text hides, or a broken walk passes forever.
            foreach (NameBankDef bank in DefDatabase<NameBankDef>.AllDefs)
            {
                // "Silver" and "Iron" are region-name parts and ThingDef defNames both; the walk skips
                // defName-shaped text, so those are the only names it may leave unread.
                Assert.All(bank.names.Where(n => !defNames.Contains(n)), n => Assert.Contains(n, checkedTexts));
            }
            foreach (ThoughtDef thought in DefDatabase<ThoughtDef>.AllDefs)
            {
                Assert.All(thought.stages.Where(s => !string.IsNullOrEmpty(s.label)), s => Assert.Contains(s.label!, checkedTexts));
            }
            Assert.True(checkedTexts.Count > 1000, "Only " + checkedTexts.Count + " strings were checked.");

            Assert.True(offences.Count == 0,
                "Player-facing def text names RimWorld:\n  " + string.Join("\n  ", offences));
        }

        [Fact]
        public void The_calendar_speaks_simWorld_own_month_names()
        {
            var labels = new List<string>();
            foreach (Quadrum q in Enum.GetValues(typeof(Quadrum)))
            {
                if (q == Quadrum.Undefined) continue;
                string label = GenDate.QuadrumLabel(q);
                Assert.Empty(PlayerTextTerms.Find(label));
                Assert.NotEqual("Undefined", label);
                labels.Add(label);
            }
            Assert.Equal(GenDate.QuadrumsPerYear, labels.Distinct().Count());

            for (int day = 0; day < GenDate.DaysPerYear; day++)
            {
                long ticks = (long)day * GenDate.TicksPerDay;
                Assert.Empty(PlayerTextTerms.Find(GenDate.DateFullStringAt(ticks, 0f)));
            }
        }

        [Fact]
        public void No_string_literal_in_the_core_names_RimWorld()
        {
            // A literal that is exactly a defName ("Pemmican") is a handle, not text a player reads.
            var defNames = new HashSet<string>(Content.Database.AllDefs.Select(d => d.defName), StringComparer.Ordinal);
            var offences = new List<string>();
            int literals = 0;
            foreach (string file in Directory.EnumerateFiles(CoreUnderAudit.CoreSourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                foreach ((int line, string text) in StringLiterals(File.ReadAllText(file)))
                {
                    literals++;
                    if (defNames.Contains(text)) continue;
                    IReadOnlyList<string> hits = PlayerTextTerms.Find(text);
                    if (hits.Count > 0)
                    {
                        offences.Add(Path.GetRelativePath(CoreUnderAudit.CoreSourceRoot, file) + ":" + line + " ["
                            + string.Join(", ", hits) + "] \"" + text + "\"");
                    }
                }
            }

            Assert.True(literals > 1000, "Only " + literals + " string literals were found; the scanner is broken.");
            Assert.True(offences.Count == 0, "Core C# literals name RimWorld:\n  " + string.Join("\n  ", offences));
        }

        [Fact]
        public void The_literal_scanner_skips_comments_and_reads_every_literal_form()
        {
            const string source = "// \"cassandra\" in a comment\n/* \"jugust\" */ var a = \"one\"; var b = @\"two \"\"q\"\"\";\n"
                + "var c = $\"three {x}\"; var d = '\"'; var e = \"esc \\\" four\";\n"
                + "var f = $\"five {(y ? \"six\" : \"seven\")} done\";";
            Assert.Equal(new[] { "one", "two \"q\"", "three {x}", "esc \\\" four", "five {(y ? \"six\" : \"seven\")} done", "six", "seven" },
                StringLiterals(source).Select(l => l.Text).ToArray());
        }

        private static void Walk(object value, string path, bool root, HashSet<string> defNames,
            HashSet<string> checkedTexts, List<string> offences, HashSet<object> seen)
        {
            switch (value)
            {
                case string text:
                    // A string equal to a defName is a reference held as text, not something a player reads.
                    if (defNames.Contains(text)) return;
                    checkedTexts.Add(text);
                    IReadOnlyList<string> hits = PlayerTextTerms.Find(text);
                    if (hits.Count > 0) offences.Add(path + " [" + string.Join(", ", hits) + "] \"" + text + "\"");
                    return;
                case Def _ when !root:
                    return; // Checked on its own; following references would also reach implied, unshipped defs.
                case Type _:
                    return;
            }

            if (!value.GetType().IsClass || !seen.Add(value)) return;

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    Walk(entry.Key, path + "{key}", false, defNames, checkedTexts, offences, seen);
                    if (entry.Value != null) Walk(entry.Value, path + "[" + entry.Key + "]", false, defNames, checkedTexts, offences, seen);
                }
                return;
            }
            if (value is IEnumerable sequence)
            {
                int i = 0;
                foreach (object? item in sequence)
                {
                    if (item != null) Walk(item, path + "[" + i + "]", false, defNames, checkedTexts, offences, seen);
                    i++;
                }
                return;
            }
            if (value.GetType().Assembly != typeof(Def).Assembly) return;

            foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (InternalFields.Contains(field.Name)) continue;
                object? fieldValue = field.GetValue(value);
                if (fieldValue != null) Walk(fieldValue, path + "." + field.Name, false, defNames, checkedTexts, offences, seen);
            }
        }

        /// <summary>
        /// The string literals of a C# source, with comments skipped: regular, verbatim (<c>@"…"</c>) and
        /// interpolated (<c>$"…"</c>, whose holes are kept as written). Char literals are stepped over so
        /// <c>'"'</c> does not open a string. Escapes are left as written; only the words matter here.
        /// </summary>
        internal static IEnumerable<(int Line, string Text)> StringLiterals(string src)
        {
            int i = 0, line = 1;
            while (i < src.Length)
            {
                char c = src[i];
                if (c == '\n') { line++; i++; continue; }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) end = src.Length - 2;
                    for (int k = i; k < end; k++) if (src[k] == '\n') line++;
                    i = end + 2;
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < src.Length && src[i] != '\'') i += src[i] == '\\' ? 2 : 1;
                    i++;
                    continue;
                }

                int start = i;
                bool verbatim = false, interpolated = false;
                while (i < src.Length && (src[i] == '$' || src[i] == '@'))
                {
                    verbatim |= src[i] == '@';
                    interpolated |= src[i] == '$';
                    i++;
                }
                if (i < src.Length && src[i] == '"')
                {
                    int startLine = line;
                    var text = new StringBuilder();
                    var nested = new List<(int, string)>();
                    i++;
                    while (i < src.Length)
                    {
                        char ch = src[i];
                        if (verbatim && ch == '"' && i + 1 < src.Length && src[i + 1] == '"') { text.Append('"'); i += 2; continue; }
                        if (!verbatim && ch == '\\' && i + 1 < src.Length) { text.Append(ch).Append(src[i + 1]); i += 2; continue; }
                        if (interpolated && ch == '{' && i + 1 < src.Length && src[i + 1] == '{') { text.Append("{{"); i += 2; continue; }
                        if (interpolated && ch == '{')
                        {
                            // A hole is code: copy it as written, but read any string inside it as a literal of
                            // its own so its closing quote cannot end the outer one.
                            int depth = 0, holeStart = i;
                            while (i < src.Length)
                            {
                                if (src[i] == '{') depth++;
                                else if (src[i] == '}' && --depth == 0) { i++; break; }
                                else if (src[i] == '"' || (src[i] == '$' && i + 1 < src.Length && src[i + 1] == '"'))
                                {
                                    int open = i;
                                    while (src[i] != '"') i++;
                                    int close = i + 1;
                                    while (close < src.Length && src[close] != '"') close += src[close] == '\\' ? 2 : 1;
                                    nested.AddRange(StringLiterals(src.Substring(open, close + 1 - open)).Select(l => (line, l.Text)));
                                    i = close + 1;
                                    continue;
                                }
                                i++;
                            }
                            text.Append(src, holeStart, i - holeStart);
                            continue;
                        }
                        if (ch == '"') break;
                        if (ch == '\n') line++;
                        text.Append(ch);
                        i++;
                    }
                    i++;
                    yield return (startLine, text.ToString());
                    foreach ((int, string) inner in nested) yield return inner;
                    continue;
                }
                i = start == i ? i + 1 : i;
            }
        }
    }
}
