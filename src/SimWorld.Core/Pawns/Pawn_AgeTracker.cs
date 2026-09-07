using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A pawn's age and life stage (RimWorld: <c>Verse.Pawn_AgeTracker</c>). Biological ticks drive
    /// <see cref="CurLifeStage"/> and, through it, the body-size/health-scale/hunger-rate factors <see cref="Pawn"/>
    /// reads; chronological ticks are the same clock unless something later (cryptosleep, time dilation) decouples
    /// them — nothing does yet, so both simply advance together every tick.
    /// </summary>
    public class Pawn_AgeTracker : IExposable
    {
        private readonly Pawn pawn;

        public long ageBiologicalTicks;
        public long ageChronologicalTicks;

        // ---- Hidden lifespan budget (death from age) ----
        //
        // Deliberate design, not an oversight: this pawn's age of death is rolled once (RollLifespanBudget,
        // called from PawnGenerator — cheaper than a per-tick mortality roll across a large population, and
        // trivially save-compatible) and lives ONLY here, as a private field. Nothing above this tracker may
        // read it: no DiesOnTick, no YearsRemaining, no public accessor of any kind — only the boolean
        // ShouldDieOfAge(), which FamilyManager's demography sweep evaluates periodically, and the mutator
        // AdjustLifespan(), which nutrition, injury, disease and (the era hook) medical-technology research can
        // call. A player who could read "this pawn dies on tick N" would experience mortality as a countdown;
        // hiding it is what keeps it emergent instead — see docs/research/epoch-inspiration.md §5 and the task
        // brief this module was built from. long.MaxValue is the "never rolled" sentinel: a pawn built directly
        // (`new Pawn(def, name)`, not through PawnGenerator — most of this codebase's own tests do this) never
        // dies of age, since it never opted into the roll.
        private long deathAgeBudgetTicks = long.MaxValue;

        /// <summary>Short log of what nudged the budget and by how much, for <see cref="DescribeLifespanForChronicle"/>
        /// to summarize when the pawn actually dies — never exposed as raw data, only as that one summary string.</summary>
        private readonly List<string> lifespanAdjustmentReasons = new List<string>();

        private const int MaxLifespanAdjustmentReasonsKept = 8;

        public Pawn_AgeTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));

            // Deviation from RimWorld: every Pawn there is expected to pass through PawnGenerator, which always
            // rolls an age. Here, `new Pawn(def, name)` is also a first-class, widely-used way to get a pawn (most
            // existing tests build one directly) with no generator involved, so a fresh tracker defaults to the
            // race's oldest life stage — fully grown — rather than age 0, matching every property that read
            // baseBodySize/baseHealthScale/baseHungerRate unscaled before life stages existed. PawnGenerator
            // overwrites this with a real rolled age immediately afterward.
            List<LifeStageAge>? stages = pawn.RaceProps.lifeStageAges;
            if (stages != null && stages.Count > 0)
            {
                long ticks = (long)(stages[stages.Count - 1].minAge * GenDate.TicksPerYear);
                ageBiologicalTicks = ticks;
                ageChronologicalTicks = ticks;
            }
        }

        public int AgeBiologicalYears => (int)(ageBiologicalTicks / GenDate.TicksPerYear);

        public float AgeBiologicalYearsFloat => (float)ageBiologicalTicks / GenDate.TicksPerYear;

        public int AgeChronologicalYears => (int)(ageChronologicalTicks / GenDate.TicksPerYear);

        private List<LifeStageAge>? RaceLifeStages => pawn.RaceProps.lifeStageAges;

        /// <summary>Index into the race's <see cref="RaceProperties.lifeStageAges"/> of the stage the pawn's
        /// current biological age has reached; -1 when the race defines none.</summary>
        public int CurLifeStageIndex
        {
            get
            {
                List<LifeStageAge>? stages = RaceLifeStages;
                if (stages == null || stages.Count == 0) return -1;
                float ageYears = AgeBiologicalYearsFloat;
                int best = 0;
                for (int i = 0; i < stages.Count; i++)
                {
                    if (stages[i].minAge <= ageYears) best = i;
                    else break; // lifeStageAges is ascending by minAge
                }
                return best;
            }
        }

        public LifeStageDef? CurLifeStage
        {
            get
            {
                int index = CurLifeStageIndex;
                List<LifeStageAge>? stages = RaceLifeStages;
                return index >= 0 && stages != null ? stages[index].def : null;
            }
        }

        /// <summary>The (def, minAge) pair itself, for callers that also want the threshold the pawn crossed.</summary>
        public LifeStageAge? CurLifeStageRace
        {
            get
            {
                int index = CurLifeStageIndex;
                List<LifeStageAge>? stages = RaceLifeStages;
                return index >= 0 && stages != null ? stages[index] : null;
            }
        }

        /// <summary>Advances both clocks by one tick (RimWorld: <c>Pawn_AgeTracker.AgeTick</c>); fires
        /// <see cref="BirthdayBiological"/> exactly on each biological year boundary.</summary>
        public void AgeTick()
        {
            ageBiologicalTicks++;
            ageChronologicalTicks++;
            if (ageBiologicalTicks % GenDate.TicksPerYear == 0)
            {
                BirthdayBiological();
            }
        }

        /// <summary>Advances both clocks by <paramref name="interval"/> ticks at once, for pawns off the active
        /// tick list (RimWorld: caravans, cryptosleep). Skips over any year boundaries without firing them.</summary>
        public void AgeTickMothballed(int interval)
        {
            if (interval < 0) throw new ArgumentOutOfRangeException(nameof(interval));
            ageBiologicalTicks += interval;
            ageChronologicalTicks += interval;
        }

        /// <summary>Fires once per biological year. No-op until a later system (growth points, aging health
        /// conditions) subscribes to it.</summary>
        public virtual void BirthdayBiological()
        {
        }

        /// <summary>Test/debug helper: sets both clocks to the same age.</summary>
        public void DebugSetAge(float years)
        {
            long ticks = (long)(years * GenDate.TicksPerYear);
            ageBiologicalTicks = ticks;
            ageChronologicalTicks = ticks;
        }

        // ---- Hidden lifespan budget (death from age) ----

        /// <summary>
        /// Rolls this pawn's hidden death-age budget once (RimWorld: no equivalent; SimWorld's own — see the
        /// class doc's "Hidden lifespan budget" section for why it is hidden). Called from
        /// <see cref="Generation.PawnGenerator"/> after age generation finalizes, for every humanlike pawn
        /// (regular generation and newborns alike) — never re-rolled afterward, only nudged by
        /// <see cref="AdjustLifespan"/>. The budget lands in
        /// [lifeExpectancy - <see cref="DemographyTuning.LifespanSpreadYears"/>, lifeExpectancy + spread]
        /// (humans: [65, 95] against an 80-year lifeExpectancy), floored at one year past the pawn's current
        /// age so a colonist generated old is never immediately overdue.
        /// </summary>
        internal void RollLifespanBudget(RaceProperties race)
        {
            if (race == null) throw new ArgumentNullException(nameof(race));
            float rolledAgeYears = race.lifeExpectancy - DemographyTuning.LifespanSpreadYears
                + Rand.Value * (2f * DemographyTuning.LifespanSpreadYears);
            float minAgeYears = AgeBiologicalYearsFloat + 1f;
            if (rolledAgeYears < minAgeYears) rolledAgeYears = minAgeYears;
            deathAgeBudgetTicks = (long)(rolledAgeYears * GenDate.TicksPerYear);
            lifespanAdjustmentReasons.Clear();
        }

        /// <summary>
        /// The only behaviour the hidden budget exposes: whether this pawn's current biological age has
        /// reached it. Returns false for a pawn whose budget was never rolled (see the "never rolled" sentinel
        /// on <c>deathAgeBudgetTicks</c>) — most pawns built directly rather than through
        /// <see cref="Generation.PawnGenerator"/> never die of age, by the same logic a pawn that never got a
        /// birthday never has one.
        /// </summary>
        public bool ShouldDieOfAge() => ageBiologicalTicks >= deathAgeBudgetTicks;

        /// <summary>
        /// Moves the hidden budget by <paramref name="days"/> (positive extends life, negative shortens it) and
        /// records why — nutrition/malnutrition history, a serious or permanent injury, chronic disease, or (the
        /// era hook) the civilization's medical technology all call this over a pawn's life, so the budget is a
        /// living figure shaped by circumstance rather than a verdict handed down at birth. No-op on a pawn whose
        /// budget was never rolled (nothing to adjust). Internal: only <c>SimWorld.Core</c> systems (health,
        /// research, this module) call it — never a UI, never outside code.
        /// </summary>
        internal void AdjustLifespan(float days, string reason)
        {
            if (deathAgeBudgetTicks == long.MaxValue) return;
            long deltaTicks = (long)(days * GenDate.TicksPerDay);
            deathAgeBudgetTicks = Math.Max(deathAgeBudgetTicks + deltaTicks, ageBiologicalTicks);

            lifespanAdjustmentReasons.Add((reason ?? "unspecified") + " (" + days.ToString("+0.#;-0.#;0", System.Globalization.CultureInfo.InvariantCulture) + "d)");
            if (lifespanAdjustmentReasons.Count > MaxLifespanAdjustmentReasonsKept)
            {
                lifespanAdjustmentReasons.RemoveAt(0);
            }
        }

        /// <summary>A short, human-readable summary of what shaped this pawn's lifespan — for the Chronicle to
        /// say why someone lived long or died young at the moment they actually die, never before. Empty when
        /// nothing ever adjusted the budget (the common case), so a plain death reads as a plain death rather
        /// than manufacturing commentary. Internal: the raw log itself is never exposed, only this rolled-up
        /// string, and only to callers inside Core.</summary>
        internal string DescribeLifespanForChronicle()
        {
            return lifespanAdjustmentReasons.Count == 0 ? "" : string.Join("; ", lifespanAdjustmentReasons);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ageBiologicalTicks, "ageBiologicalTicks", 0L);
            Scribe_Values.Look(ref ageChronologicalTicks, "ageChronologicalTicks", 0L);
            Scribe_Values.Look(ref deathAgeBudgetTicks, "deathAgeBudgetTicks", long.MaxValue);
            List<string>? reasons = lifespanAdjustmentReasons;
            Scribe_Collections.Look(ref reasons, "lifespanAdjustmentReasons", LookMode.Value);
            lifespanAdjustmentReasons.Clear();
            if (reasons != null) lifespanAdjustmentReasons.AddRange(reasons);
        }
    }
}
