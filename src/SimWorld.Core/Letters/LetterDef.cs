using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Letters
{
    /// <summary>
    /// Data half of one letter kind (RimWorld: <c>RimWorld.LetterDef</c>): which runtime class to build and,
    /// eventually, how it should draw. SimWorld has no UI layer yet, so the visual knobs RimWorld carries
    /// (bounce/flash animation, the letter's icon color, <c>arriveSound</c>) are not modelled — only
    /// <see cref="letterClass"/>, which decides the object <see cref="Letters.LetterStack"/> builds, survives.
    /// </summary>
    public class LetterDef : Def
    {
        public Type letterClass = typeof(StandardLetter);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (letterClass == null || !typeof(Letter).IsAssignableFrom(letterClass))
            {
                yield return "letterClass must derive from Letter.";
            }
        }
    }
}
