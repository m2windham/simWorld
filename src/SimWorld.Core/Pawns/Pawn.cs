using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Pawns.Genes;
using SimWorld.Sim;
using SimWorld.Stats;
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
        public Pawn_RelationsTracker relations = null!;

        /// <summary>This pawn's genes — Biotech's own module (system: pawngen.genes). Instantiated for every
        /// pawn like <see cref="training"/>; empty (Baseliner) unless <see cref="Generation.PawnGenerationRequest.Xenotype"/>
        /// asked for one at generation or <see cref="Genes.GeneInheritanceUtility"/> passed some down at birth.</summary>
        public Pawn_GeneTracker genes = null!;

        /// <summary>Which <see cref="TrainableDef"/>s this pawn has learned (system: ai.animals). Instantiated
        /// for every pawn like RimWorld's own field, but only meaningful for an Animal — see
        /// <see cref="Pawn_TrainingTracker.CanBeTrained"/>.</summary>
        public Pawn_TrainingTracker training = null!;

        /// <summary>How much of this pawn's state is computed per tick (system 11: tiering, §11.3).</summary>
        public Pawn_TierTracker tier = null!;

        /// <summary>Current job, its driver and the directed-order queue (system 9: AI).</summary>
        public Pawn_JobTracker jobs = null!;

        /// <summary>Cell-to-cell movement along whatever path the current job asked for (system 9: AI).</summary>
        public Pawn_PathFollower pather = null!;

        /// <summary>Weapons this pawn carries (RimWorld: <c>Pawn.equipment</c>).</summary>
        public Pawn_EquipmentTracker equipment = null!;

        /// <summary>Apparel this pawn is wearing (RimWorld: <c>Pawn.apparel</c>); see <see cref="Pawn_ApparelTracker"/>.</summary>
        public Pawn_ApparelTracker apparel = null!;

        public Gender gender;
        public PawnKindDef? kindDef;
        public Name? Name;

        /// <summary>The civilization/faction this pawn belongs to, if any (RimWorld: <c>Pawn.Faction</c>). Set
        /// by <see cref="Generation.PawnGenerator"/> from <see cref="Generation.PawnGenerationRequest.Faction"/>;
        /// a pawn built directly (<c>new Pawn(def, name)</c>) has none.</summary>
        public Faction? faction;

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

        /// <summary>Species hunger rate × hediff hunger factors × the current life stage's hunger factor × a
        /// gene-metabolism factor (<see cref="Genes.GeneTuning.HungerRateFactorFromMetabolism"/>) — the same
        /// seam every other hunger-rate contributor already joins, rather than a parallel calculation.</summary>
        public virtual float HungerRate => RaceProps.baseHungerRate * HungerRateFactorFromHealth
            * (ageTracker?.CurLifeStage?.hungerRateFactor ?? 1f)
            * (genes != null ? GeneTuning.HungerRateFactorFromMetabolism(genes.MetabolismTotal) : 1f);

        public virtual float HungerRateFactorFromHealth => health.hediffSet.HungerRateFactor;

        public virtual float RestFallFactorFromHealth => health.hediffSet.RestFallFactor;

        /// <summary>Rest gain multiplier (RimWorld: <c>StatDefOf.RestRateMultiplier</c>) — capacity-factored by
        /// BloodPumping/Metabolism/Breathing, so a wounded heart or lungs slow rest gain; beds raise it later
        /// through <see cref="Need_Rest.lastRestEffectiveness"/>, which this stat does not carry (RimWorld
        /// applies bed effectiveness as a separate multiplier alongside the stat, not through it).</summary>
        public virtual float RestRateMultiplier => this.GetStatValue(StatDefOf.RestRateMultiplier);

        /// <summary>Immunity gain multiplier while sick (RimWorld: <c>StatDefOf.ImmunityGainSpeed</c>).</summary>
        public virtual float ImmunityGainSpeed => this.GetStatValue(StatDefOf.ImmunityGainSpeed);

        /// <summary>Pain level that downs the pawn (RimWorld: <c>StatDefOf.PainShockThreshold</c>).</summary>
        public virtual float PainShockThreshold => this.GetStatValue(StatDefOf.PainShockThreshold);

        /// <summary>Mood level under which minor breaks become possible (RimWorld: <c>StatDefOf.MentalBreakThreshold</c>,
        /// a race's own <c>statBases</c> entry rather than a <see cref="RaceProperties"/> field — see the Human
        /// ThingDef's content).</summary>
        public virtual float MentalBreakThreshold => this.GetStatValue(StatDefOf.MentalBreakThreshold);

        /// <summary>Scales indirect (learning-by-doing) skill XP (RimWorld: <c>StatDefOf.GlobalLearningFactor</c>).</summary>
        public virtual float GlobalLearningFactor => this.GetStatValue(StatDefOf.GlobalLearningFactor);

        /// <summary>
        /// Work tags disabled by traits, backstories and genes. RimWorld also folds in health (e.g. a missing
        /// arm barring Violent-tagged work); that joins here once its system lands.
        /// </summary>
        public virtual WorkTags CombinedDisabledWorkTags =>
            story.DisabledWorkTagsBackstoryAndTraits | (genes?.CombinedDisabledWorkTags ?? WorkTags.None);

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

        /// <summary>
        /// Starvation builds malnutrition each food interval; eating again lets it fade at the same pace.
        /// Hunger also spends the pawn's hidden lifespan budget: malnutrition is survivable and still costs
        /// years, so a civilization that starves its people repeatedly buries them younger.
        /// </summary>
        public virtual void Notify_StarvationInterval(bool starving)
        {
            if (Dead || HediffDefOf.Malnutrition == null) return;
            float delta = starving ? HealthTuning.MalnutritionSeverityPerInterval : -HealthTuning.MalnutritionSeverityPerInterval;
            HealthUtility.AdjustSeverity(this, HediffDefOf.Malnutrition, delta);
            if (starving)
            {
                ageTracker?.AdjustLifespan(-DemographyTuning.StarvationLifespanPenaltyDays, "hunger");
            }
        }

        public virtual void Notify_TraitsChanged()
        {
            skills?.Notify_SkillDisablesChanged();
            workSettings?.Notify_DisabledWorkTypesChanged();
        }

        public virtual void Notify_Downed()
        {
            // A downed pawn cannot walk (CapableOf(Moving) fails), but a job with no pathing step left
            // (already-arrived toils, an in-progress wait) would otherwise keep ticking to completion.
            if (jobs?.curJob != null) jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: false);
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
            relations ??= new Pawn_RelationsTracker(this);
            genes ??= new Pawn_GeneTracker(this);
            training ??= new Pawn_TrainingTracker(this);
            tier ??= new Pawn_TierTracker(this);
            jobs ??= new Pawn_JobTracker(this);
            pather ??= new Pawn_PathFollower(this);
            equipment ??= new Pawn_EquipmentTracker(this);
            apparel ??= new Pawn_ApparelTracker(this);
        }

        // ---- ITickable ----

        /// <summary>Full ticks every tick (system 9: AI's <c>Normal</c> list); Interval and Statistical sit on
        /// the <c>Long</c> list instead (<see cref="Pawn_TierTracker.TickerTypeFor"/>) — the dispatch by tier
        /// the brief this module was built from asked for, done by which tick list the pawn is <i>on</i> rather
        /// than by a per-tracker <c>if</c> inside this method.</summary>
        public override TickerType TickerType => tier != null ? Pawn_TierTracker.TickerTypeFor(tier.Tier) : base.TickerType;

        public override void Tick()
        {
            base.Tick();
            if (Dead || Suspended) return;
            // Defensive, not the mechanism: a Full-only pawn should never reach here from anywhere but the
            // Normal tick list, which TickerType above already restricts to Full. Guards a list/tier desync
            // rather than doing the tier's own work — see the class doc on TickerType.
            if (tier.Tier != PawnTier.Full) return;
            health.HealthTick();
            if (Dead) return;
            needs.NeedsTrackerTick();
            mindState.MindStateTick();
            skills.SkillsTick();
            ageTracker.AgeTick();
            training.TrainingTrackerTick();
            // Jobs run last: a job's think-tree choice and its toils' FailOn checks should see this tick's
            // fresh needs/health/mind-state numbers rather than last tick's, and nothing else this tick
            // reacts to a job starting, ending, or a pawn moving, so nothing needs to run after it.
            jobs.JobTrackerTick();
        }

        /// <summary>Where a non-Full pawn actually advances (system 11: tiering) — see <see cref="Pawn_TierTracker.CoarseTick"/>.</summary>
        public override void TickLong()
        {
            base.TickLong();
            if (Dead || Suspended) return;
            tier.CoarseTick();
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
            Pawn_RelationsTracker? rel = relations;
            Scribe_Deep.Look(ref rel, "relations", this);
            relations = rel ?? new Pawn_RelationsTracker(this);
            Pawn_GeneTracker? gn = genes;
            Scribe_Deep.Look(ref gn, "genes", this);
            genes = gn ?? new Pawn_GeneTracker(this);
            Pawn_TrainingTracker? tr = training;
            Scribe_Deep.Look(ref tr, "training", this);
            training = tr ?? new Pawn_TrainingTracker(this);
            Pawn_TierTracker? tt = tier;
            Scribe_Deep.Look(ref tt, "tier", this);
            tier = tt ?? new Pawn_TierTracker(this);
            Pawn_JobTracker? j = jobs;
            Scribe_Deep.Look(ref j, "jobs", this);
            jobs = j ?? new Pawn_JobTracker(this);
            Pawn_PathFollower? pf = pather;
            Scribe_Deep.Look(ref pf, "pather", this);
            pather = pf ?? new Pawn_PathFollower(this);
            Pawn_EquipmentTracker? eq = equipment;
            Scribe_Deep.Look(ref eq, "equipment", this);
            equipment = eq ?? new Pawn_EquipmentTracker(this);
            Pawn_ApparelTracker? ap = apparel;
            Scribe_Deep.Look(ref ap, "apparel", this);
            apparel = ap ?? new Pawn_ApparelTracker(this);
            Scribe_Values.Look(ref gender, "gender", Gender.None);
            PawnKindDef? kd = kindDef;
            Scribe_Defs.Look(ref kd, "kindDef");
            kindDef = kd;
            Name? nm = Name;
            Scribe_Deep.Look(ref nm, "fullName");
            Name = nm;
            Faction? f = faction;
            Scribe_References.Look(ref f, "faction");
            faction = f;
        }
    }

    /// <summary>What the surroundings offer a seeker need; the map supplies real values later.</summary>
    public interface IEnvironmentSampler
    {
        /// <summary>Instant level in [0, 1] for an environment-driven need, or null to use the need's default.</summary>
        float? InstantLevelFor(NeedDef need, Pawn pawn);
    }
}
