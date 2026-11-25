namespace SimWorld.Defs
{
    /// <summary>
    /// English plurals for labels counted in player text ("4 woolly oxen", "12 walls"). RimWorld's English
    /// language worker makes a label plural by rule unless the def carries its own plural (recalled, not quoted);
    /// this is that rule, for a def with no plural of its own.
    /// </summary>
    public static class LabelPlural
    {
        /// <summary><paramref name="label"/> for one, or <paramref name="plural"/> (else the rule) for any other count.</summary>
        public static string For(string label, int count, string? plural = null) =>
            count == 1 ? label : plural ?? Of(label);

        /// <summary>"-es" after a hiss, "-ies" for a consonant and y, else "-s".</summary>
        public static string Of(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            if (word.EndsWith("s") || word.EndsWith("x") || word.EndsWith("z") || word.EndsWith("ch") || word.EndsWith("sh")) return word + "es";
            if (word.Length > 1 && word.EndsWith("y") && "aeiou".IndexOf(word[word.Length - 2]) < 0) return word.Substring(0, word.Length - 1) + "ies";
            return word + "s";
        }
    }
}
