using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>
    /// The map's answer to <see cref="IEnvironmentSampler"/> — what a spawned citizen's surroundings actually
    /// offer a seeker need, read off the cells they are standing in.
    /// <para/>
    /// <b>Why this exists at all.</b> <see cref="IEnvironmentSampler"/>, <see cref="Need_Environment"/> and the
    /// <c>Beauty</c> NeedDef all shipped long before anything implemented the interface, so
    /// <c>Need_Environment.CurInstantLevel</c> fell through to <c>def.baseLevel</c> for every citizen in every
    /// game and beauty never moved — a whole system with passing tests and no driver, which is the fourth
    /// time this project has found one. <see cref="Need_Environment"/> now falls back to this sampler when no
    /// caller has substituted one, so the route is live with no per-pawn wiring to forget on spawn: nothing
    /// has to remember to set <c>pawn.environment</c>, and a citizen who walks onto (or off) a map starts (or
    /// stops) being sampled by that alone.
    /// <para/>
    /// <b>Stateless and shared.</b> Everything it needs comes off the pawn, so one instance serves every
    /// citizen on every map and it holds nothing to Scribe. Substituting a different sampler per pawn still
    /// works exactly as before through <c>Pawn.environment</c>, which takes priority (see
    /// <see cref="Need_Environment.CurInstantLevel"/>).
    /// <para/>
    /// <b>Cadence and cost, measured.</b> <see cref="InstantLevelFor"/> runs once per
    /// <see cref="Need.IntervalTicks"/> (150) per Full-tier citizen and never for anyone below that tier — 400
    /// samples per citizen per in-game day. Timed in a Release build over 200,000 calls after a warm-up, the
    /// method <c>docs/perf/baseline.md</c> itself uses:
    /// <list type="bullet">
    /// <item><description><b>1.6-1.7 µs</b> per sample for a citizen in a 64-cell room with 16 piles of filth
    /// in it — <b>0.7 ms per citizen-day</b>, and 0.35 s per in-game day for a full
    /// <see cref="TieringTuning.FullTierBudget"/> of 500.</description></item>
    /// <item><description><b>3.5 µs</b> for the 145-cell radial fallback outdoors — <b>1.4 ms per
    /// citizen-day</b>, 0.7 s per in-game day at 500.</description></item>
    /// </list>
    /// Against baseline §1's ~14 ms/pawn-day for a whole citizen and §2's 2.1 ms/pawn-day for all needs
    /// together, beauty is 5-10% of a citizen and the largest single line inside needs — worth knowing, and
    /// the reason the sample is bounded by construction (see <see cref="BeautyUtility"/>) rather than by how
    /// big the room happens to be.
    /// <para/>
    /// <b>Most of that is not new spending.</b> The <c>FilthyRoom</c> situational thought this module retired
    /// walked the same citizen's room for its own answer, at the same effective cadence (situational thoughts
    /// recalculate when mood is read, and mood is read on the same 150-tick need interval), and measures
    /// <b>1.27 µs</b> per call over the same room. Indoors the net addition is therefore a few tenths of a
    /// microsecond per sample; outdoors it is the whole 3.5 µs, since the retired worker answered "no room, no
    /// thought" there almost for free.
    /// <para/>
    /// <b>Only beauty is answered.</b> Comfort, Outdoors and RoomSize are environment needs in content too,
    /// and each returns null here — the need keeps its <c>baseLevel</c> exactly as before this class existed.
    /// That is deliberate rather than unfinished: comfort in RimWorld is not sampled from surroundings at all
    /// but pushed by whatever the pawn is sitting or lying on (a comfort <i>hook</i> in the job system, not a
    /// map sample), and outdoors/room-size want a roof-and-region reading that belongs with whoever ports
    /// them. Returning null keeps them exactly as dormant as they were rather than inventing numbers for
    /// them, and the day one is built it is one more branch here.
    /// </summary>
    public sealed class MapEnvironmentSampler : IEnvironmentSampler
    {
        /// <summary>The shared instance; the class is stateless, so there is never a reason for a second.</summary>
        public static readonly MapEnvironmentSampler Instance = new MapEnvironmentSampler();

        private MapEnvironmentSampler()
        {
        }

        /// <inheritdoc/>
        public float? InstantLevelFor(NeedDef need, Pawn pawn)
        {
            if (need == null || pawn == null) return null;

            // Off-map: a citizen in a caravan or a pod has no surroundings to read. Null, not zero — zero is
            // "I looked and it was unremarkable", and the need's own default is the honest answer instead.
            if (!pawn.Spawned) return null;

            if (need == EnvironmentNeedDefOf.Beauty)
            {
                return Need_Beauty.LevelFromBeauty(BeautyUtility.AverageBeautyPerceptible(pawn.Position, pawn.Map));
            }

            return null;
        }
    }
}
