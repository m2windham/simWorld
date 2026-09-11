using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>How ugly or pretty a citizen finds their surroundings (RimWorld: <c>RimWorld.BeautyCategory</c>).
    /// Read off the need's level, not off raw beauty, so a citizen who has just walked into squalor is still
    /// "Neutral" until the need has had time to fall — which is the whole point of beauty being a
    /// <see cref="Need_Seeker"/> rather than an instantaneous lookup.</summary>
    public enum BeautyCategory
    {
        Hideous,
        VeryUgly,
        Ugly,
        Neutral,
        Pretty,
        VeryPretty,
        Beautiful,
    }

    /// <summary>
    /// The beauty need (RimWorld: <c>RimWorld.Need_Beauty</c>): a <see cref="Need_Seeker"/> whose target is
    /// the average beauty of the citizen's surroundings, run through a curve into 0-1. A subclass of
    /// <see cref="Need_Environment"/> rather than a replacement for it — the sampling seam, the tier gate and
    /// the freeze rules are all the same for all four environment needs, and only the category readout below
    /// is beauty's own. The sampling itself lives in <see cref="MapEnvironmentSampler"/>, behind
    /// <see cref="IEnvironmentSampler"/>, so a host (or a test) can still substitute its own.
    /// </summary>
    public class Need_Beauty : Need_Environment
    {
        /// <summary>
        /// Average perceptible beauty to need level (RimWorld: <c>Need_Beauty</c>'s own private curve).
        /// <b>SimWorld's own points — RimWorld's could not be sourced here</b>, so
        /// <c>BeautyTests</c> pins the properties that matter (it rises monotonically, an unremarkable
        /// environment lands on the need's own base level, filth reads below a bare room and art above it)
        /// rather than any point on it (CLAUDE.md).
        /// <para/>
        /// Zero maps to exactly <c>0.5</c>, which is the <c>EnvironmentNeedBase</c> <c>baseLevel</c> every
        /// environment need starts at: a citizen standing in a bare, empty room is by definition "no opinion",
        /// and it would be strange for the sampler's answer there to differ from the level the need starts
        /// life at. The ends are asymmetric on purpose — a single pile of dirt underfoot is worth more of the
        /// scale than a single sculpture, matching how much easier it is to make a room ugly than beautiful.
        /// </summary>
        public static readonly SimpleCurve LevelFromBeautyCurve = new SimpleCurve
        {
            new CurvePoint(-10f, 0f),
            new CurvePoint(-2f, 0.25f),
            new CurvePoint(0f, 0.5f),
            new CurvePoint(2f, 0.7f),
            new CurvePoint(10f, 1f),
        };

        public Need_Beauty(Pawn pawn) : base(pawn)
        {
        }

        /// <summary>The band this need's current level falls in, which is all
        /// <see cref="ThoughtWorker_NeedBeauty"/> reads. <b>Thresholds are SimWorld's own</b> (RimWorld's own
        /// cut points were not sourceable); the tests pin the ordering — a worse environment is never a better
        /// category — rather than these numbers.</summary>
        public BeautyCategory CurCategory
        {
            get
            {
                float level = CurLevel;
                if (level < 0.01f) return BeautyCategory.Hideous;
                if (level < 0.15f) return BeautyCategory.VeryUgly;
                if (level < 0.3f) return BeautyCategory.Ugly;
                if (level < 0.7f) return BeautyCategory.Neutral;
                if (level < 0.85f) return BeautyCategory.Pretty;
                if (level < 0.99f) return BeautyCategory.VeryPretty;
                return BeautyCategory.Beautiful;
            }
        }

        /// <summary>Need level for an average perceptible beauty (RimWorld: <c>Need_Beauty.LevelFromBeauty</c>).</summary>
        public static float LevelFromBeauty(float beauty) => GenMath.Clamp01(LevelFromBeautyCurve.Evaluate(beauty));
    }
}
