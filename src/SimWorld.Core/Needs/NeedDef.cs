using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>A need a pawn can have (RimWorld: <c>RimWorld.NeedDef</c>).</summary>
    public class NeedDef : Def
    {
        /// <summary>
        /// RimWorld's own default for <see cref="fallPerDay"/> (<c>RimWorld.NeedDef.fallPerDay = 0.5f</c>).
        /// Named rather than written twice because <see cref="ConfigErrors"/> has to tell "a content author
        /// set this" from "nobody has touched it", which was the whole problem with the zero it used to hold.
        /// </summary>
        public const float DefaultFallPerDay = 0.5f;

        /// <summary>Runtime class; must derive from <see cref="Need"/>, be concrete, and take a Pawn constructor argument.</summary>
        public Type needClass = typeof(Need);

        public Intelligence minIntelligence = Intelligence.Animal;
        public int listPriority;
        public bool major;
        public bool showOnNeedList = true;

        /// <summary>Starting level for the base class; subclasses may pick their own.</summary>
        public float baseLevel = 0.5f;

        /// <summary>
        /// <b>Read by nothing in this port, and that is RimWorld's shape rather than a gap.</b> In RimWorld
        /// this field has exactly one reader — <c>Need_Chemical.ChemicalFallPerTick =&gt; def.fallPerDay /
        /// 60000f</c> — and it is set only by the chemical (addiction) needs, which are blocked on drugs,
        /// addiction hediffs and withdrawal. None of the eight needs this port ships is a chemical need, and
        /// in RimWorld none of their Defs sets <c>fallPerDay</c> either: Food, Rest and Joy carry per-tick or
        /// per-interval constants inside their own classes, and the four seeker needs chase a target at
        /// <see cref="seekerRisePerHour"/>/<see cref="seekerFallPerHour"/>.
        /// <para/>
        /// It used to default to <c>0f</c> and be consulted by an invented generic decay in
        /// <see cref="Need.NeedInterval"/>, which could therefore never fire for anybody (see that class's
        /// remarks). The default is back to RimWorld's <see cref="DefaultFallPerDay"/> and the generic path is
        /// gone; <see cref="ConfigErrors"/> now rejects content that sets this field, so "I set fallPerDay and
        /// nothing happened" is a load error instead of a silent nothing. Delete that check along with this
        /// comment the day <c>Need_Chemical</c> lands.
        /// </summary>
        public float fallPerDay = DefaultFallPerDay;

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
            else if (needClass.IsAbstract)
            {
                // Not a RimWorld check: RimWorld only rejects a null needClass. Worth having because
                // Pawn_NeedsTracker.AddNeed builds the need by reflection, so an abstract class here is a
                // crash the first time a pawn is given the need rather than a complaint at load.
                yield return "needClass " + needClass.Name + " is abstract; name a concrete Need subclass.";
            }

            if (baseLevel < 0f || baseLevel > 1f) yield return "baseLevel must be in [0, 1].";

            // RimWorld: "seeker rise/fall rates not set", but it asks needClass == typeof(Need_Seeker)
            // exactly. Widened here to every Need_Seeker subclass — a translation, and the one that matters:
            // no NeedDef anywhere names the abstract base directly, so RimWorld's own check cannot fire for
            // any real need, which is precisely how an unfilled seeker rate stays invisible. A seeker with a
            // zero rate does not decay slowly, it does not move at all.
            if (needClass != null && typeof(Need_Seeker).IsAssignableFrom(needClass)
                && (seekerRisePerHour == 0f || seekerFallPerHour == 0f))
            {
                yield return "seeker rise/fall rates not set (needClass " + needClass.Name
                    + " chases a target and would never move).";
            }

            if (fallPerDay != DefaultFallPerDay)
            {
                yield return "fallPerDay is set (" + fallPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ") but nothing reads it: in RimWorld only Need_Chemical does, and chemical needs are not"
                    + " ported. Give the need class its own rate instead — see NeedDef.fallPerDay.";
            }
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
