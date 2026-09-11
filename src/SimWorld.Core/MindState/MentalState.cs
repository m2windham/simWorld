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

        /// <summary>
        /// Entering the state. Raises the Def's begin letter, which is the only thing in this port that tells
        /// the player one of their people has broken down (RimWorld: <c>MentalState.PostStart</c> does this
        /// from exactly here, with the same two fields and the same fallback from
        /// <see cref="MentalStateDef.beginLetterLabel"/> to the state's label).
        ///
        /// <para/><b>Two gates, both RimWorld's.</b> No <see cref="MentalStateDef.beginLetter"/> means the
        /// state is not news — <c>SocialFighting</c> is the shipped example, a scuffle that is over in
        /// seconds. And <see cref="Pawns.PawnUtility.ShouldSendNotificationAbout"/> keeps the channel to the
        /// civilization's own people: a raider going berserk in a siege is not a letter, and one per raider
        /// is how a letter stack stops being read.
        ///
        /// <para/><paramref name="startReason"/> is appended when the caller supplied one, so "berserk" and
        /// "berserk, because they were denied a grave" are the same letter with a second paragraph — the
        /// same shape RimWorld appends its own reason in.
        /// </summary>
        public virtual void PostStart(string? startReason)
        {
            reason = startReason;
            SendBeginLetter();
        }

        private void SendBeginLetter()
        {
            if (string.IsNullOrEmpty(def.beginLetter)) return;
            if (!Pawns.PawnUtility.ShouldSendNotificationAbout(pawn)) return;

            string label = string.IsNullOrEmpty(def.beginLetterLabel) ? def.LabelCap : def.beginLetterLabel!;
            string text = Pawns.PawnUtility.FormatWithPawn(def.beginLetter, pawn);
            if (!string.IsNullOrEmpty(reason)) text = text + "\n\n" + reason;

            Find.LetterStack.ReceiveLetter(
                label + ": " + pawn.Label,
                text,
                def.beginLetterDef ?? Letters.LetterDefOf.NegativeEvent,
                new List<string> { pawn.GetUniqueLoadID() });
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
            // Prose, not the bare tag "mood" it used to be: MentalState.PostStart appends this to the letter
            // the player reads (RimWorld does the same, from a translated string), and "mood" on its own line
            // under "Maren is in a rage" reads as a debug artefact. causedByMood carries the machine-readable
            // half and is what every other reader consults.
            return chosen.Worker.TryStart(pawn, "Their mood has been at breaking point for some time.", causedByMood: true);
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

        // ---- Animals module (system: ai.animals) ----

        /// <summary>
        /// How bonded this individual animal is to its owner, 0 (never tamed) to 1 (freshly tamed) — RimWorld
        /// shape: alongside <see cref="Pawns.RaceProperties.wildness"/> (the species' default, constant per
        /// race), this is the mutable per-instance counterpart <see cref="Pawns.TameUtility"/> sets on a
        /// successful tame. <b>Scope call:</b> real RimWorld can also let a tamed animal drift back to wild
        /// over time; this port does not (yet) model that reversion — see this module's report — so once set
        /// it only ever changes via another explicit tame.
        /// </summary>
        public float tameness;

        /// <summary>
        /// Set by a failed taming roll (<see cref="Pawns.TameUtility.TryTame"/>): RimWorld's real consequence
        /// is a manhunter animal that attacks whoever tried, which needs Combat — out of this lane's boundary.
        /// This port's substitute consequence is behavioural instead: while non-null and before
        /// <see cref="angryUntilTick"/>, the animal think tree's <c>ThinkNode_ConditionalAngryAtHandler</c>
        /// tier pre-empts routine behaviour, the same way <c>ThinkNode_ConditionalInMentalState</c> does for
        /// the humanlike tree.
        /// <para/>
        /// <b>A property rather than the plain field it was</b> for the same reason
        /// <see cref="Pawns.Pawn.faction"/> is one: a grudge is symmetric — <c>AttackTargetsUtility.HostileTo</c>
        /// reads it from both sides — so the pawn holding one has to become visible to its victim, and no
        /// faction bucket in <c>AI.AttackTargetsCache</c> would ever show a wild animal to the hunter it
        /// turned on. Writing this while spawned files the holder in that index; writing null takes it out.
        /// </summary>
        public Pawn? angryAt
        {
            get => angryAtInt;
            set
            {
                if (ReferenceEquals(angryAtInt, value)) return;
                angryAtInt = value;
                pawn.Map?.mapPawns.AttackTargets.Notify_GrudgeChanged(pawn);
            }
        }

        private Pawn? angryAtInt;

        /// <summary><see cref="Sim.TickManager.TicksGame"/> after which <see cref="angryAt"/> no longer applies.</summary>
        public int angryUntilTick = -1;

        // ---- AI module (system 9: AI — duties) ----

        /// <summary>
        /// The standing goal this pawn is on this map to carry out, or null — which is every pawn who lives
        /// here (RimWorld: <c>Pawn_MindState.duty</c>, a <c>PawnDuty</c> there because RimWorld's also carries
        /// a focus target and a radius; this port stores the def alone until something reads more, see
        /// <see cref="AI.DutyDef"/>). Read by <see cref="AI.ThinkNode_Duty"/> as one tier of the main think
        /// tree, and written by whatever put the pawn on the map: a raid squad is handed
        /// <see cref="AI.DutyDefOf.AssaultSettlement"/> as it lands, and that is what walks it to the town.
        /// <para/>
        /// A plain field and not a property, unlike <see cref="angryAt"/> beside it: a duty is one pawn's own
        /// business and nothing indexes it, where a grudge has to be visible to the pawn it names.
        /// </summary>
        public AI.DutyDef? duty;

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

            Scribe_Values.Look(ref tameness, "tameness");
            Pawn? aa = angryAt;
            Scribe_References.Look(ref aa, "angryAt");
            angryAt = aa;
            Scribe_Values.Look(ref angryUntilTick, "angryUntilTick", -1);

            // A raid saved mid-approach has to still be a raid when it loads, or reloading would disarm every
            // squad on the map — the duty is the only thing that makes them march.
            AI.DutyDef? d = duty;
            Scribe_Defs.Look(ref d, "duty");
            duty = d;
        }
    }
}
