using System;
using System.Collections.Generic;
using System.Globalization;

namespace SimWorld.Defs
{
    /// <summary>
    /// Text → value conversion for leaf XML nodes (RimWorld: <c>Verse.ParseHelper</c>).
    /// Always culture-invariant so content files load identically on every machine.
    /// Extend with <see cref="RegisterParser{T}"/>.
    /// </summary>
    public static class ParseHelper
    {
        private static readonly Dictionary<Type, Func<string, object>> Parsers = new Dictionary<Type, Func<string, object>>();
        private static readonly object Gate = new object();

        static ParseHelper()
        {
            RegisterParser(s => s);
            RegisterParser(s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => short.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => byte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => sbyte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => ushort.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
            RegisterParser(s => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
            RegisterParser(s => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
            RegisterParser(s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture));
            RegisterParser(ParseBool);
            RegisterParser(ParseChar);
            RegisterParser(IntRange.FromString);
            RegisterParser(FloatRange.FromString);
            RegisterParser(CurvePoint.FromString);
        }

        public static void RegisterParser<T>(Func<string, T> parser)
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            lock (Gate)
            {
                Parsers[typeof(T)] = s => parser(s)!;
            }
        }

        /// <summary>True when <see cref="FromString"/> can produce <paramref name="type"/> directly from text.</summary>
        public static bool CanParse(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (target.IsEnum) return true;
            lock (Gate)
            {
                return Parsers.ContainsKey(target);
            }
        }

        public static T FromString<T>(string text) => (T)FromString(text, typeof(T))!;

        /// <summary>Converts trimmed text to <paramref name="type"/>. Throws on unsupported types or bad input.</summary>
        public static object? FromString(string text, Type type)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (type == null) throw new ArgumentNullException(nameof(type));

            string s = text.Trim();
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (s.Length == 0 || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                type = underlying;
            }

            if (type.IsEnum)
            {
                // Enum.Parse accepts "A, B" for [Flags] enums, matching RimWorld content.
                return Enum.Parse(type, s, ignoreCase: false);
            }

            Func<string, object>? parser;
            lock (Gate)
            {
                Parsers.TryGetValue(type, out parser);
            }
            if (parser == null)
            {
                throw new ArgumentException("No parser registered for type " + type.FullName + ".");
            }
            return parser(s);
        }

        private static bool ParseBool(string s)
        {
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new FormatException("Expected 'true' or 'false' but got '" + s + "'.");
        }

        private static char ParseChar(string s)
        {
            if (s.Length != 1)
            {
                throw new FormatException("Expected a single character but got '" + s + "'.");
            }
            return s[0];
        }
    }
}
