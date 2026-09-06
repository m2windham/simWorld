using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>A need a pawn can have (RimWorld: <c>RimWorld.NeedDef</c>).</summary>
    public class NeedDef : Def
    {
        /// <summary>Runtime class; must derive from <see cref="Need"/> and take a Pawn constructor argument.</summary>
        public Type needClass = typeof(Need);

        public Intelligence minIntelligence = Intelligence.Animal;
        public int listPriority;
        public bool major;
        public bool showOnNeedList = true;

        /// <summary>Starting level for the base class; subclasses may pick their own.</summary>
        public float baseLevel = 0.5f;

        /// <summary>Generic decay for needs without their own rule, in level per day.</summary>
        public float fallPerDay;

        public float seekerRisePerHour;
        public float seekerFallPerHour;
        public bool freezeWhileSleeping;
        public bool freezeInMentalState;
        public bool colonistAndPrisonersOnly;
        public bool colonistsOnly;
        public bool neverOnPrisoner;
        public bool scaleBar;

        /// <summary>UI threshold markers, as fractions of the bar.</summary>
        public List<float>? threshPercents;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (needClass == null || !typeof(Need).IsAssignableFrom(needClass))
            {
                yield return "needClass must derive from Need.";
            }
            if (baseLevel < 0f || baseLevel > 1f) yield return "baseLevel must be in [0, 1].";
        }
    }

    [DefOf]
    public static class NeedDefOf
    {
        public static NeedDef Food = null!;
        public static NeedDef Rest = null!;
        public static NeedDef Joy = null!;
        public static NeedDef Mood = null!;
    }
}
