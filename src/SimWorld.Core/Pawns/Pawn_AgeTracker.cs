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

        public void ExposeData()
        {
            Scribe_Values.Look(ref ageBiologicalTicks, "ageBiologicalTicks", 0L);
            Scribe_Values.Look(ref ageChronologicalTicks, "ageChronologicalTicks", 0L);
        }
    }
}
