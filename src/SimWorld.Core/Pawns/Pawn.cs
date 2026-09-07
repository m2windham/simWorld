using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A person or creature (RimWorld: <c>Verse.Pawn</c>). Every citizen is one of these — the trackers hung
    /// off it are the per-agent systems (health, needs, story, mind). Map presence, hit points and ids are
    /// inherited from <see cref="Thing"/>/<see cref="ThingWithComps"/>; jobs and combat come with their modules.
    /// </summary>
    public class Pawn : ThingWithComps
    {
        public string? name;

        public Pawn_HealthTracker health = null!;
        public Pawn_NeedsTracker needs = null!;
        public Pawn_StoryTracker story = null!;
        public Pawn_MindState mindState = null!;
        public Pawn_SkillTracker skills = null!;
        public Pawn_WorkSettings workSettings = null!;
        public Pawn_AgeTracker ageTracker = null!;

        public Gender gender;
        public PawnKindDef? kindDef;
        public Name? Name;

        /// <summary>Environment sampler for seeker needs (beauty, comfort, outdoors, room); the map supplies it later.</summary>
        public IEnvironmentSampler? environment;

        public Pawn()
        {
        }

        public Pawn(ThingDef def, string? name = null)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            if (def.race == null) throw new ArgumentException("ThingDef " + def.defName + " has no race properties.", nameof(def));
            this.name = name;
            thingIDNumber = AllocateThingId();
            PostMake();
            InitializeTrackers();
            needs.AddOrRemoveNeedsAsAppropriate();
            if (RaceProps.Humanlike) workSettings.EnableAndInitialize();
        }

        public RaceProperties RaceProps => def.race!;

        /// <summary>A generated pawn's short name, else the nickname it was constructed with, else the species label.</summary>
        public override string Label => Name?.ToStringShort ?? name ?? def.label ?? def.defName;

        // ---- state other systems set; kept as plain flags until those systems land ----

        /// <summary>Set by the job system while the pawn is in bed asleep.</summary>
        public bool Asleep { get; set; }

        /// <summary>The body cannot act: pain shock, unconsciousness, no working legs, or forced (see <see cref="Pawn_HealthTracker.ForceDowned"/>).</summary>
        public bool Downed => health != null && health.Downed;

        public bool Dead => health != null && health.Dead;

        /// <summary>In a caravan or a pod: needs freeze.</summary>
        public bool Suspended { get; set; }

        public bool InMentalState => mindState.mentalStateHandler.InMentalState;

        public MentalStateDef? MentalStateDef => mindState.mentalStateHandler.CurStateDef;

        public bool Awake() => !Asleep && !Dead;

        /// <summary>Not standing: in bed or downed. Speeds healing (RimWorld posture).</summary>
        public bool Lying => Asleep || Downed;

        public float BodySize => RaceProps.baseBodySize * (ageTracker?.CurLifeStage?.bodySizeFactor ?? 1f);

        /// <summary>Multiplies every body part's hit points (RimWorld: <c>Pawn.HealthScale</c>), scaled by the current life stage.</summary>
        public virtual float HealthScale => RaceProps.baseHealthScale * (ageTracker?.CurLifeStage?.healthScaleFactor ?? 1f);

        /// <summary>Species hunger rate × hediff hunger factors × the current life stage's hunger factor.</summary>
        public virtual float HungerRate => RaceProps.baseHungerRate * HungerRateFactorFromHealth * (ageTracker?.CurLifeStage?.hungerRateFactor ?? 1f);

        public virtual float HungerRateFactorFromHealth => health.hediffSet.HungerRateFactor;

        public virtual float RestFallFactorFromHealth => health.hediffSet.RestFallFactor;

        /// <summary>Rest gain multiplier; the stats module supplies bed and trait effects later.</summary>
        public virtual float RestRateMultiplier => 1f;

        /// <summary>Immunity gain multiplier while sick; beds and traits raise it later.</summary>
        public virtual float ImmunityGainSpeed => 1f;

        /// <summary>Pain level that downs the pawn.</summary>
        public virtual float PainShockThreshold => HealthTuning.DefaultPainShockThreshold;

        /// <summary>Mood level under which minor breaks become possible; traits and stats adjust it later.</summary>
        public virtual float MentalBreakThreshold => RaceProps.mentalBreakThreshold;

        /// <summary>Scales indirect (learning-by-doing) skill XP; stats and conditions adjust it later.</summary>
        public virtual float GlobalLearningFactor => 1f;

        /// <summary>
        /// Work tags disabled by traits and, later, backstories. RimWorld also folds in genes and health
        /// (e.g. a missing arm barring Violent-tagged work); those join here once their systems land.
        /// </summary>
        public virtual WorkTags CombinedDisabledWorkTags => story.DisabledWorkTagsBackstoryAndTraits;

        public bool WorkTagIsDisabled(WorkTags tags) => (CombinedDisabledWorkTags & tags) != WorkTags.None;

        /// <summary>True for every non-Humanlike pawn, or when the combined disabled tags hit this work type's
        /// own tags or <see cref="WorkTags.AllWork"/>.</summary>
        public bool WorkTypeIsDisabled(WorkTypeDef workType)
        {
            if (workType == null) throw new ArgumentNullException(nameof(workType));
            if (!RaceProps.Humanlike) return true;
            WorkTags combined = CombinedDisabledWorkTags;
            if ((combined & WorkTags.AllWork) != WorkTags.None) return true;
            return (workType.workTags & combined) != WorkTags.None;
        }

        public IEnumerable<WorkTypeDef> GetDisabledWorkTypes()
        {
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (WorkTypeIsDisabled(workType)) yield return workType;
            }
        }

        public bool HasHediff(HediffDef hediffDef) => health.hediffSet.HasHediff(hediffDef);

        /// <summary>Starvation builds malnutrition each food interval; eating again lets it fade at the same pace.</summary>
        public virtual void Notify_StarvationInterval(bool starving)
        {
            if (Dead || HediffDefOf.Malnutrition == null) return;
            float delta = starving ? HealthTuning.MalnutritionSeverityPerInterval : -HealthTuning.MalnutritionSeverityPerInterval;
            HealthUtility.AdjustSeverity(this, HediffDefOf.Malnutrition, delta);
        }

        public virtual void Notify_TraitsChanged()
        {
            skills?.Notify_SkillDisablesChanged();
            workSettings?.Notify_DisabledWorkTypesChanged();
        }

        public virtual void Notify_Downed()
        {
        }

        public virtual void Notify_Died()
        {
            mindState?.mentalStateHandler.ClearMentalStateDirect();
        }

        /// <summary>Spreads per-pawn periodic work across ticks (RimWorld: <c>Gen.IsHashIntervalTick</c>).</summary>
        public bool IsHashIntervalTick(int interval)
        {
            return GenMath.PositiveMod(Find.TickManager.TicksGame + HashOffsetTicks(), interval) == 0;
        }

        public int HashOffsetTicks() => thingIDNumber * 3;

        protected virtual void InitializeTrackers()
        {
            health ??= new Pawn_HealthTracker(this);
            needs ??= new Pawn_NeedsTracker(this);
            story ??= new Pawn_StoryTracker(this);
            mindState ??= new Pawn_MindState(this);
            skills ??= new Pawn_SkillTracker(this);
            workSettings ??= new Pawn_WorkSettings(this);
            ageTracker ??= new Pawn_AgeTracker(this);
        }

        // ---- ITickable ----

        public override void Tick()
        {
            base.Tick();
            if (Dead || Suspended) return;
            health.HealthTick();
            if (Dead) return;
            needs.NeedsTrackerTick();
            mindState.MindStateTick();
            skills.SkillsTick();
            ageTracker.AgeTick();
        }

        // ---- Scribe ----

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref name, "name");
            bool asleep = Asleep, suspended = Suspended;
            Scribe_Values.Look(ref asleep, "asleep");
            Scribe_Values.Look(ref suspended, "suspended");
            Asleep = asleep; Suspended = suspended;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                InitializeTrackers();
            }
            Pawn_HealthTracker? h = health;
            Scribe_Deep.Look(ref h, "healthTracker", this);
            health = h ?? new Pawn_HealthTracker(this);
            Pawn_NeedsTracker? n = needs;
            Scribe_Deep.Look(ref n, "needs", this);
            needs = n ?? new Pawn_NeedsTracker(this);
            Pawn_StoryTracker? s = story;
            Scribe_Deep.Look(ref s, "story", this);
            story = s ?? new Pawn_StoryTracker(this);
            Pawn_MindState? m = mindState;
            Scribe_Deep.Look(ref m, "mindState", this);
            mindState = m ?? new Pawn_MindState(this);
            Pawn_SkillTracker? sk = skills;
            Scribe_Deep.Look(ref sk, "skills", this);
            skills = sk ?? new Pawn_SkillTracker(this);
            Pawn_WorkSettings? ws = workSettings;
            Scribe_Deep.Look(ref ws, "workSettings", this);
            workSettings = ws ?? new Pawn_WorkSettings(this);
            Pawn_AgeTracker? at = ageTracker;
            Scribe_Deep.Look(ref at, "ageTracker", this);
            ageTracker = at ?? new Pawn_AgeTracker(this);
            Scribe_Values.Look(ref gender, "gender", Gender.None);
            PawnKindDef? kd = kindDef;
            Scribe_Defs.Look(ref kd, "kindDef");
            kindDef = kd;
            Name? nm = Name;
            Scribe_Deep.Look(ref nm, "fullName");
            Name = nm;
        }
    }

    /// <summary>What the surroundings offer a seeker need; the map supplies real values later.</summary>
    public interface IEnvironmentSampler
    {
        /// <summary>Instant level in [0, 1] for an environment-driven need, or null to use the need's default.</summary>
        float? InstantLevelFor(NeedDef need, Pawn pawn);
    }
}
