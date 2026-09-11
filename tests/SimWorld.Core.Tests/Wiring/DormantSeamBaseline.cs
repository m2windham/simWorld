using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SimWorld.Tests.Wiring
{
    /// <summary>
    /// The reviewed list of seams that are dormant on purpose, read from <c>dormant-seams.txt</c>.
    ///
    /// <para/>The baseline is what keeps the audit worth running. Without it the test fails on 130-odd
    /// existing seams from the day it lands, someone marks it skipped, and the defect class it was built to
    /// catch comes back. With it the audit is green today and fails loudly the moment a <i>new</i> seam goes
    /// dark — and the file itself becomes the to-do list, one line shorter each time a system is wired up.
    ///
    /// <para/>One line per seam: <c>check identity | why it is dormant</c>. The reason is mandatory and the
    /// tests enforce it, because a baseline of bare identifiers is indistinguishable from a suppression list
    /// and ages into one.
    /// </summary>
    public sealed class DormantSeamBaseline
    {
        private readonly Dictionary<string, string> reasons = new Dictionary<string, string>(StringComparer.Ordinal);

        private DormantSeamBaseline(string path)
        {
            Path = path;
        }

        public string Path { get; }

        /// <summary>Every key the baseline lists.</summary>
        public IReadOnlyCollection<string> Keys => reasons.Keys;

        /// <summary>Keys whose line carried no reason, or a reason too short to be one.</summary>
        public IReadOnlyList<string> Unexplained { get; private set; } = new string[0];

        /// <summary>Keys listed more than once.</summary>
        public IReadOnlyList<string> Duplicates { get; private set; } = new string[0];

        public bool Contains(string key) => reasons.ContainsKey(key);

        /// <summary>The shortest reason that still says something. Anything shorter is a placeholder.</summary>
        public const int MinimumReasonLength = 25;

        public static DormantSeamBaseline Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The wiring audit's baseline is missing. It should sit at " + path
                    + " and list every seam that is dormant on purpose.", path);
            }

            var baseline = new DormantSeamBaseline(path);
            var unexplained = new List<string>();
            var duplicates = new List<string>();

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int bar = line.IndexOf('|');
                string key = Normalise(bar < 0 ? line : line.Substring(0, bar));
                string reason = bar < 0 ? string.Empty : line.Substring(bar + 1).Trim();

                if (key.Length == 0) continue;
                if (baseline.reasons.ContainsKey(key))
                {
                    duplicates.Add(key);
                    continue;
                }
                if (reason.Length < MinimumReasonLength) unexplained.Add(key);
                baseline.reasons.Add(key, reason);
            }

            baseline.Unexplained = unexplained;
            baseline.Duplicates = duplicates;
            return baseline;
        }

        /// <summary>A line ready to paste into the baseline, with the reason left for a person to write.</summary>
        public static string TemplateLineFor(Seam seam) =>
            seam.Key + " | WHY IS THIS DORMANT? (" + seam.Detail + ")";

        /// <summary>Collapses runs of whitespace so a re-wrapped line still matches the key it names.</summary>
        private static string Normalise(string key) =>
            string.Join(" ", key.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
