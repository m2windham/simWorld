using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimWorld.Bench
{
    /// <summary>Tiny GitHub-flavored-markdown table writer so a suite's stdout can be pasted straight into
    /// docs/perf/baseline.md.</summary>
    internal static class Report
    {
        public static void Heading(string text)
        {
            Console.WriteLine();
            Console.WriteLine("## " + text);
            Console.WriteLine();
        }

        public static void SubHeading(string text)
        {
            Console.WriteLine();
            Console.WriteLine("### " + text);
            Console.WriteLine();
        }

        public static void Note(string text)
        {
            Console.WriteLine();
            Console.WriteLine("> " + text);
        }

        public static void Table(string[] headers, IEnumerable<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append('|');
            foreach (string h in headers) sb.Append(' ').Append(h).Append(" |");
            Console.WriteLine(sb.ToString());

            sb.Clear();
            sb.Append('|');
            foreach (string _ in headers) sb.Append(" --- |");
            Console.WriteLine(sb.ToString());

            foreach (string[] row in rows)
            {
                sb.Clear();
                sb.Append('|');
                foreach (string cell in row) sb.Append(' ').Append(cell).Append(" |");
                Console.WriteLine(sb.ToString());
            }
        }

        public static string Ms(double ms) => ms.ToString("N1", CultureInfo.InvariantCulture) + " ms";

        public static string Seconds(double ms) => (ms / 1000.0).ToString("N2", CultureInfo.InvariantCulture) + " s";

        public static string Num(double v, int decimals = 2) => v.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public static string Int(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
    }
}
