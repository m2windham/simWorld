using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.MindState
{
    /// <summary>
    /// An active mental state (RimWorld: <c>Verse.AI.MentalState</c>). Lifecycle only for now: it ends at
    /// the Def's max duration, or by a mean-time-between roll after the minimum. Behaviour (wandering,
    /// attacking) is the AI module's job.
    /// </summary>
    public class MentalState : IExposable
    {
        public Pawn pawn = null!;
        public MentalStateDef def = null!;
        public int age;
        public bool causedByMood;
        public string? reason;
        public int forceRecoverAfterTicks = -1;

        public MentalState()
        {
        }

        public MentalState(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public string InspectLine => def.baseInspectLine ?? def.LabelCap;

        public virtual void PreStart()
        {
        }

        public virtual void PostStart(string? startReason)
        {
            reason = startReason;
        }

        public virtual void PostEnd()
        {
        }

        public virtual void MentalStateTick()
        {
            age++;
            if (forceRecoverAfterTicks >= 0 && age >= forceRecoverAfterTicks)
            {
                RecoverFromState();
                return;
            }
            if (age >= def.maxTicksBeforeRecovery)
            {
                RecoverFromState();
                return;
            }
            if (age >= def.minTicksBeforeRecovery && Rand.MTBEventOccurs(def.recoveryMtbDays, GenDate.TicksPerDay, 1f))
            {
                RecoverFromState();
            }
        }

        public void RecoverFromState()
        {
            if (!ReferenceEquals(pawn.mindState.mentalStateHandler.CurState, this)) return;
            pawn.mindState.mentalStateHandler.ClearMentalStateDirect();
            if (causedByMood && def.moodRecoveryThought != null && pawn.needs.mood != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(def.moodRecoveryThought);
            }
            pawn.mindState.mentalBreaker.Notify_RecoveredFromMentalState();
            PostEnd();
        }

        public virtual void ExposeData()
        {
            MentalStateDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref age, "age");
            Scribe_Values.Look(ref causedByMood, "causedByMood");
            Scribe_Values.Look(ref reason, "reason");
            Scribe_Values.Look(ref forceRecoverAfterTicks, "forceRecoverAfterTicks", -1);
        }
    }

    /// <summary>Starts, ticks and ends a pawn's mental state (RimWorld: <c>Verse.AI.MentalStateHandler</c>).</summary>
    public class MentalStateHandler : IExposable
    {
        private readonly Pawn pawn;
        private MentalState? curState;

        public MentalStateHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public MentalState? CurState => curState;

        public MentalStateDef? CurStateDef => curState?.def;

        public bool InMentalState => curState != null;

        public void MentalStateHandlerTick()
        {
            if (curState == null) return;
            curState.MentalStateTick();
            if (curState != null && curState.def.recoverFromSleep && pawn.Asleep)
            {
                curState.RecoverFromState();
            }
            if (curState != null && curState.def.recoverFromDowned && pawn.Downed)
            {
                curState.RecoverFromState();
            }
        }

        public bool TryStartMentalState(MentalStateDef stateDef, string? reason = null, bool forced = false, bool causedByMood = false)
        {
            if (stateDef == null) throw new ArgumentNullException(nameof(stateDef));
            if (curState != null && curState.def == stateDef) return false;
            if (!forced && (pawn.Dead || pawn.Downed)) return false;
            if (stateDef.colonistsOnly && !pawn.RaceProps.Humanlike) return false;

            var state = (MentalState)Activator.CreateInstance(stateDef.stateClass, pawn)!;
            state.def = stateDef;
            state.causedByMood = causedByMood;
            state.PreStart();
            curState = state;
            state.PostStart(reason);
            pawn.needs.mood?.thoughts.situational.Notify_SituationalThoughtsDirty();
            return true;
        }

        public void ClearMentalStateDirect()
        {
            curState = null;
            pawn.needs.mood?.thoughts.situational.Notify_SituationalThoughtsDirty();
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref curState, "curState", pawn);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && curState != null)
            {
                curState.pawn = pawn;
            }
        }
    }

    /// <summary>
    /// Watches mood against the break thresholds and rolls for breaks (RimWorld: <c>RimWorld.MentalBreaker</c>).
    /// Thresholds: minor at the pawn's break threshold (0.35 base), major 0.15 below, extreme 0.30 below.
    /// Mood must sit below a band for 2000 ticks before its MTB roll starts; the pawn is immune for 15000
    /// ticks after recovering. Chosen break is weighted by commonality among the eligible breaks of the
    /// highest reached intensity (falling back to lower intensities).
    /// </summary>
    public class MentalBreaker : IExposable
    {
        public const int MinTicksBelowToBreak = 2000;
        public const int MinTicksSinceRecoveryToBreak = 15000;
        public const float MinorBreakMTBDays = 10f;
        public const float MajorBreakMTBDays = 3f;
        public const float ExtremeBreakMTBDays = 0.7f;
        public const float MajorThresholdOffset = 0.15f;
        public const float ExtremeThresholdOffset = 0.30f;
        public const int CheckIntervalTicks = 150;

        private readonly Pawn pawn;
        private int ticksBelowMinor;
        private int ticksBelowMajor;
        private int ticksBelowExtreme;
        private int ticksUntilCanDoMentalBreak;
        private readonly List<MentalBreakDef> tmpBreaks = new List<MentalBreakDef>();

        public MentalBreaker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public float BreakThresholdMinor => pawn.MentalBreakThreshold;
        public float BreakThresholdMajor => BreakThresholdMinor - MajorThresholdOffset;
        public float BreakThresholdExtreme => BreakThresholdMinor - ExtremeThresholdOffset;

        public int TicksBelowMinor => ticksBelowMinor;
        public int TicksBelowMajor => ticksBelowMajor;
        public int TicksBelowExtreme => ticksBelowExtreme;
        public int TicksUntilCanDoMentalBreak => ticksUntilCanDoMentalBreak;

        public float CurMood => pawn.needs.mood?.CurLevel ?? 0.5f;

        public bool CanDoRandomMentalBreaks => pawn.RaceProps.Humanlike && !pawn.Dead;

        public MentalBreakIntensity CurMoodBreakIntensity
        {
            get
            {
                float mood = CurMood;
                if (mood < BreakThresholdExtreme) return MentalBreakIntensity.Extreme;
                if (mood < BreakThresholdMajor) return MentalBreakIntensity.Major;
                if (mood < BreakThresholdMinor) return MentalBreakIntensity.Minor;
                return MentalBreakIntensity.None;
            }
        }

        public void MentalBreakerTick()
        {
            if (!CanDoRandomMentalBreaks || pawn.MentalStateDef != null || !pawn.IsHashIntervalTick(CheckIntervalTicks)) return;

            float mood = CurMood;
            ticksBelowExtreme = mood < BreakThresholdExtreme ? ticksBelowExtreme + CheckIntervalTicks : 0;
            ticksBelowMajor = mood < BreakThresholdMajor ? ticksBelowMajor + CheckIntervalTicks : 0;
            ticksBelowMinor = mood < BreakThresholdMinor ? ticksBelowMinor + CheckIntervalTicks : 0;

            if (TestMoodMentalBreak())
            {
                TryDoRandomMoodCausedMentalBreak();
            }
        }

        private bool TestMoodMentalBreak()
        {
            if (ticksUntilCanDoMentalBreak > 0)
            {
                ticksUntilCanDoMentalBreak -= CheckIntervalTicks;
                return false;
            }
            if (ticksBelowExtreme > MinTicksBelowToBreak) return Rand.MTBEventOccurs(ExtremeBreakMTBDays, GenDate.TicksPerDay, CheckIntervalTicks);
            if (ticksBelowMajor > MinTicksBelowToBreak) return Rand.MTBEventOccurs(MajorBreakMTBDays, GenDate.TicksPerDay, CheckIntervalTicks);
            if (ticksBelowMinor > MinTicksBelowToBreak) return Rand.MTBEventOccurs(MinorBreakMTBDays, GenDate.TicksPerDay, CheckIntervalTicks);
            return false;
        }

        /// <summary>Eligible breaks at the current intensity, falling back to lower intensities.</summary>
        public List<MentalBreakDef> CurrentPossibleMoodBreaks()
        {
            tmpBreaks.Clear();
            MentalBreakIntensity intensity = CurMoodBreakIntensity;
            while (intensity >= MentalBreakIntensity.Minor)
            {
                foreach (MentalBreakDef def in DefDatabase<MentalBreakDef>.AllDefsListForReading)
                {
                    if (def.intensity == intensity && def.Worker.BreakCanOccur(pawn)) tmpBreaks.Add(def);
                }
                if (tmpBreaks.Count > 0) break;
                intensity--;
            }
            return tmpBreaks;
        }

        public bool TryDoRandomMoodCausedMentalBreak()
        {
            if (!CanDoRandomMentalBreaks || pawn.Downed || !pawn.Awake() || pawn.InMentalState) return false;
            List<MentalBreakDef> possible = CurrentPossibleMoodBreaks();
            if (!GenCollection.TryRandomElementByWeight(possible, d => d.Worker.CommonalityFor(pawn, moodCaused: true), Rand.Current, out MentalBreakDef chosen))
            {
                return false;
            }
            return chosen.Worker.TryStart(pawn, "mood", causedByMood: true);
        }

        public void Notify_RecoveredFromMentalState()
        {
            ticksUntilCanDoMentalBreak = MinTicksSinceRecoveryToBreak;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ticksBelowMinor, "ticksBelowMinor");
            Scribe_Values.Look(ref ticksBelowMajor, "ticksBelowMajor");
            Scribe_Values.Look(ref ticksBelowExtreme, "ticksBelowExtreme");
            Scribe_Values.Look(ref ticksUntilCanDoMentalBreak, "ticksUntilCanDoMentalBreak");
        }
    }

    /// <summary>Per-pawn mind state (RimWorld: <c>Verse.AI.Pawn_MindState</c>): mental states and the breaker for now.</summary>
    public class Pawn_MindState : IExposable
    {
        private readonly Pawn pawn;
        public MentalStateHandler mentalStateHandler;
        public MentalBreaker mentalBreaker;

        public Pawn_MindState(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            mentalStateHandler = new MentalStateHandler(pawn);
            mentalBreaker = new MentalBreaker(pawn);
        }

        public void MindStateTick()
        {
            mentalStateHandler.MentalStateHandlerTick();
            mentalBreaker.MentalBreakerTick();
        }

        public void ExposeData()
        {
            MentalStateHandler? h = mentalStateHandler;
            Scribe_Deep.Look(ref h, "mentalStateHandler", pawn);
            mentalStateHandler = h ?? new MentalStateHandler(pawn);
            MentalBreaker? b = mentalBreaker;
            Scribe_Deep.Look(ref b, "mentalBreaker", pawn);
            mentalBreaker = b ?? new MentalBreaker(pawn);
        }
    }
}
