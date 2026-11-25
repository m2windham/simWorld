using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// The pace of renewal (<c>docs/design/era-progression.md</c> §7.5). RimWorld has nothing to port here: its
    /// colonies renew only by the player's hand. These are the spec's own figures, unsourced; the arc runs are what
    /// decide them, and the lever to move is <see cref="MaxUnderway"/>, not the scores.
    /// </summary>
    public static class RenewalTuning
    {
        /// <summary>Renewal is slow and its pass cheap: once a day. A multiple of
        /// <see cref="ConstructionInitiativeTuning.IntervalTicks"/>, since it runs inside that pass.</summary>
        public const int PassIntervalTicks = GenDate.TicksPerDay;

        /// <summary>One renewal under way per this many citizens, and at least one: a band renews one thing at a
        /// time, a city several (the shape of <see cref="DwellingTuning.MaxUnderway"/>).</summary>
        public const int CitizensPerRenewal = 15;

        /// <summary>No more than this share of homes is being replaced at once.</summary>
        public const float MaxHomesUnderwayShare = 0.25f;

        /// <summary>A renewal never takes the last of a material: the stock must hold this many times the new
        /// house's cost, over what blueprints and frames already claim.</summary>
        public const float MaterialReserve = 1.5f;

        /// <summary>A form is due once it is this many eras behind; each era further behind adds as much again.</summary>
        public const int ObsoleteAfterEras = 1;

        /// <summary>Renewal waits while the stores hold fewer days of food than this (or the era's own food
        /// condition, if that asks for less).</summary>
        public const float MinFoodDays = 5f;

        /// <summary>Heritage is capped at about one structure in this many, so the town is not frozen (§7.6).</summary>
        public const int StructuresPerHeritage = 10;

        /// <summary>How much crowding (members over beds, past one per bed) weighs against an era behind.</summary>
        public const float CrowdingWeight = 0.5f;

        /// <summary>How much a house below half its walls' hit points weighs against an era behind.</summary>
        public const float ConditionWeight = 0.5f;

        /// <summary>Per year of age: small, so age only breaks ties between houses equally behind.</summary>
        public const float AgeWeightPerYear = 0.05f;

        /// <summary>The upkeep life of a form, in years, when its def names none: twice its era's
        /// <see cref="Research.EraDef.intendedYears"/> (§7.7).</summary>
        public const float ServiceYearsPerIntendedYear = 2f;

        public static int MaxUnderway(int citizens) => System.Math.Max(1, citizens / CitizensPerRenewal);

        public static int MaxHomesUnderway(int homes) => System.Math.Max(1, (int)(homes * MaxHomesUnderwayShare));
    }
}
