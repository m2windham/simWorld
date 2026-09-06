using System;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Work
{
    /// <summary>
    /// One skill's level and XP for one pawn (RimWorld: <c>Verse.SkillRecord</c>). Levels run 0–20; XP required
    /// per level-up follows <see cref="XpForLevelUpCurve"/>; passion and a global learning factor scale gains;
    /// levels 10+ decay when unused (see <see cref="Interval"/>).
    /// </summary>
    public class SkillRecord : IExposable
    {
        public const int MinLevel = 0;
        public const int MaxLevel = 20;

        /// <summary>XP earned per day past which further direct learning is throttled (see <see cref="LearningSaturatedToday"/>).</summary>
        public const float MaxFullRateXpPerDay = 4000f;

        /// <summary>Level at and above which a pawn counts as a specialist for that skill.</summary>
        public const int MasterSkillThreshold = 14;

        /// <summary>Learn-rate multiplier applied once <see cref="LearningSaturatedToday"/> is true.</summary>
        public const float SaturatedLearningPercentage = 0.2f;

        /// <summary>XP required to go from level N to N+1 (RimWorld's tuning curve).</summary>
        public static readonly SimpleCurve XpForLevelUpCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0, 1000),
            new CurvePoint(9, 10000),
            new CurvePoint(19, 30000),
        });

        /// <summary>Per-level XP decay applied by <see cref="Interval"/>; index 0–9 never decay.</summary>
        private static readonly float[] DecayPerIntervalByLevel =
        {
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0.1f, 0.2f, 0.4f, 0.65f, 1.0f, 1.5f, 2.0f, 3.0f, 4.0f, 6.0f, 8.0f,
        };

        private readonly Pawn pawn;
        public SkillDef def = null!;

        /// <summary>Backing field for <see cref="Level"/> (RimWorld keeps this public too — direct manipulation is expected).</summary>
        public int levelInt;

        public float xpSinceLastLevel;
        public float xpSinceMidnight;
        public Passion passion;

        private bool cachedTotallyDisabled;
        private bool cachedTotallyDisabledDirty = true;

        public SkillRecord(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public SkillRecord(Pawn pawn, SkillDef def) : this(pawn)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
        }

        public static int XpRequiredToLevelUpFrom(int level) => (int)XpForLevelUpCurve.Evaluate(level);

        public int XpRequiredForLevelUp => XpRequiredToLevelUpFrom(levelInt);

        public float XpProgressPercent
        {
            get
            {
                int required = XpRequiredForLevelUp;
                return required <= 0 ? 0f : GenMath.Clamp01(xpSinceLastLevel / required);
            }
        }

        /// <summary>Total XP ever earned toward the current level, including every level already banked.</summary>
        public float XpTotalEarned
        {
            get
            {
                float total = xpSinceLastLevel;
                for (int level = 0; level < levelInt; level++)
                {
                    total += XpRequiredToLevelUpFrom(level);
                }
                return total;
            }
        }

        /// <summary>
        /// True when work tags or (transitively) every relevant work type bar this skill entirely; a totally
        /// disabled skill reports level 0 and ignores XP. Cached; call <see cref="Notify_SkillDisablesChanged"/>
        /// when the pawn's traits or disabled work types change.
        /// </summary>
        public bool TotallyDisabled
        {
            get
            {
                if (cachedTotallyDisabledDirty)
                {
                    cachedTotallyDisabled = def.IsDisabled(pawn.CombinedDisabledWorkTags, pawn.GetDisabledWorkTypes());
                    cachedTotallyDisabledDirty = false;
                }
                return cachedTotallyDisabled;
            }
        }

        public void Notify_SkillDisablesChanged() => cachedTotallyDisabledDirty = true;

        public int Level
        {
            get => TotallyDisabled ? 0 : levelInt;
            set
            {
                levelInt = GenMath.Clamp(value, MinLevel, MaxLevel);
                xpSinceLastLevel = 0f;
            }
        }

        /// <summary>Past this much XP in a day, further direct learning is throttled (RimWorld's diminishing returns).</summary>
        public bool LearningSaturatedToday => xpSinceMidnight > MaxFullRateXpPerDay;

        /// <summary>
        /// Passion multiplier (None 0.35, Minor 1.0, Major 1.5). Indirect learning (<paramref name="direct"/> false,
        /// the default — passive learning-by-doing) is further scaled by <see cref="Pawn.GlobalLearningFactor"/>
        /// and throttled by <see cref="SaturatedLearningPercentage"/> once saturated for the day.
        /// </summary>
        public float LearnRateFactor(bool direct = false)
        {
            float factor = passion switch
            {
                Passion.Minor => 1.0f,
                Passion.Major => 1.5f,
                _ => 0.35f,
            };
            if (!direct)
            {
                factor *= pawn.GlobalLearningFactor;
                if (LearningSaturatedToday)
                {
                    factor *= SaturatedLearningPercentage;
                }
            }
            return factor;
        }

        /// <summary>
        /// Applies XP, handling level-ups (and, symmetrically, level-downs for negative XP). A totally disabled
        /// skill, or zero XP, is a no-op. Positive XP is scaled by <see cref="LearnRateFactor"/> unless
        /// <paramref name="ignoreLearnRate"/> is set (used by <see cref="Interval"/>'s decay, which is negative
        /// anyway and so never scaled regardless).
        /// </summary>
        public void Learn(float xp, bool direct = false, bool ignoreLearnRate = false)
        {
            if (TotallyDisabled || xp == 0f)
            {
                return;
            }
            if (xp > 0f && !ignoreLearnRate)
            {
                xp *= LearnRateFactor(direct);
            }

            xpSinceLastLevel += xp;
            if (xp > 0f)
            {
                xpSinceMidnight += xp;
            }

            while (xpSinceLastLevel >= XpRequiredForLevelUp && levelInt < MaxLevel)
            {
                xpSinceLastLevel -= XpRequiredForLevelUp;
                levelInt++;
            }
            if (levelInt >= MaxLevel)
            {
                float cap = XpRequiredForLevelUp - 1f;
                if (xpSinceLastLevel > cap) xpSinceLastLevel = cap;
            }

            while (xpSinceLastLevel < 0f && levelInt > MinLevel)
            {
                levelInt--;
                xpSinceLastLevel += XpRequiredForLevelUp;
            }
            if (levelInt <= MinLevel && xpSinceLastLevel < 0f)
            {
                xpSinceLastLevel = 0f;
            }
        }

        /// <summary>Called every 200 ticks by <see cref="Pawn_SkillTracker.SkillsTick"/>: unused levels 10+ decay.</summary>
        public void Interval()
        {
            if (TotallyDisabled || levelInt < 10)
            {
                return;
            }
            float decay = DecayPerIntervalByLevel[levelInt];
            if (decay > 0f)
            {
                Learn(-decay, direct: true, ignoreLearnRate: true);
            }
        }

        public string LevelDescriptor
        {
            get
            {
                int level = Level;
                if (level <= 0) return "none";
                if (level <= 2) return "awful";
                if (level <= 5) return "poor";
                if (level <= 8) return "average";
                if (level <= 11) return "good";
                if (level <= 14) return "very good";
                if (level <= 17) return "excellent";
                if (level <= 19) return "master";
                return "legendary";
            }
        }

        public void ExposeData()
        {
            SkillDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref levelInt, "level");
            Scribe_Values.Look(ref xpSinceLastLevel, "xpSinceLastLevel");
            Scribe_Values.Look(ref xpSinceMidnight, "xpSinceMidnight");
            Scribe_Values.Look(ref passion, "passion", Passion.None);
        }

        public override string ToString() => (def?.defName ?? "SkillRecord") + " " + Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
