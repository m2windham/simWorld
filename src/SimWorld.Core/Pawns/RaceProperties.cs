using System.Collections.Generic;

namespace SimWorld.Pawns
{
    /// <summary>Cognitive tier; gates which needs, thoughts and breaks apply (RimWorld: <c>Verse.Intelligence</c>).</summary>
    public enum Intelligence
    {
        Animal,
        ToolUser,
        Humanlike,
    }

    /// <summary>
    /// Species-level facts on a pawn's ThingDef (RimWorld: <c>Verse.RaceProperties</c>). Only the fields the
    /// ported systems read so far; later modules extend it (body, life stages, diet, combat).
    /// </summary>
    public class RaceProperties
    {
        public Intelligence intelligence = Intelligence.Humanlike;

        /// <summary>Scales food capacity, hit points and more.</summary>
        public float baseBodySize = 1f;

        /// <summary>Nutrition burned per day is 1.6 × this.</summary>
        public float baseHungerRate = 1f;

        /// <summary>Food level below which the pawn wants to eat; half of it is "urgently hungry".</summary>
        public float foodLevelPercentageWantEat = 0.3f;

        public bool needsRest = true;

        /// <summary>Flesh creatures bleed, feel pain and get sick; mechanoids do not.</summary>
        public bool isFlesh = true;

        public float lifeExpectancy = 80f;

        /// <summary>Base mood level below which minor mental breaks become possible.</summary>
        public float mentalBreakThreshold = 0.35f;

        public bool Humanlike => intelligence >= Intelligence.Humanlike;

        public bool ToolUser => intelligence >= Intelligence.ToolUser;

        public bool Animal => intelligence == Intelligence.Animal;

        public bool IsFlesh => isFlesh;

        public virtual IEnumerable<string> ConfigErrors()
        {
            if (baseBodySize <= 0f) yield return "baseBodySize must be positive.";
            if (foodLevelPercentageWantEat <= 0f || foodLevelPercentageWantEat > 1f) yield return "foodLevelPercentageWantEat must be in (0, 1].";
        }
    }
}
