using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>Mobile → Down → Dead (RimWorld: <c>Verse.PawnHealthState</c>).</summary>
    public enum PawnHealthState
    {
        Mobile,
        Down,
        Dead,
    }

    /// <summary>
    /// A pawn's health (RimWorld: <c>Verse.Pawn_HealthTracker</c>): owns the hediff set, capacities, immunity
    /// and summary; ticks hediffs, natural healing and bleeding; derives downed and dead from the body's state.
    /// </summary>
    public class Pawn_HealthTracker : IExposable
    {
        private readonly Pawn pawn;
        public HediffSet hediffSet;
        public PawnCapacitiesHandler capacities;
        public ImmunityHandler immunity;
        public SummaryHealthHandler summaryHealth;

        /// <summary>
        /// Surgeries queued on this pawn (RimWorld: <c>Pawn_HealthTracker.surgeryBills</c>). A medical bill
        /// lives on the patient, not on a workbench — the operation happens wherever the patient is, and
        /// queueing one is a decision about a person rather than about a production line.
        /// </summary>
        public BillStack surgeryBills;

        private PawnHealthState healthState = PawnHealthState.Mobile;
        private bool forceDowned;
        private int deathTick = -1;
        private HediffDef? deathCauseHediff;
        private DamageDef? deathCauseDamage;
        private bool killedByPawn;
        private readonly List<Hediff_Injury> tmpInjuries = new List<Hediff_Injury>();

        public Pawn_HealthTracker(Pawn pawn)
        {
            surgeryBills = new BillStack(new SurgeryBillGiver(pawn));
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            hediffSet = new HediffSet(pawn);
            capacities = new PawnCapacitiesHandler(pawn);
            immunity = new ImmunityHandler(pawn);
            summaryHealth = new SummaryHealthHandler(pawn);
        }

        public PawnHealthState State => healthState;

        public bool Downed => healthState == PawnHealthState.Down;

        public bool Dead => healthState == PawnHealthState.Dead;

        public int DeathTick => deathTick;

        public HediffDef? DeathCauseHediff => deathCauseHediff;

        public DamageDef? DeathCauseDamage => deathCauseDamage;

        /// <summary>
        /// Somebody killed this pawn, as opposed to something (RimWorld records who in its <c>BattleLog</c>
        /// and <c>TaleRecorder</c>, neither of which this port has built; this is the one bit of that answer
        /// anything here currently needs). False for a living pawn, for age, hunger and disease, and for
        /// damage with no instigator.
        ///
        /// <para/><b>Why it exists.</b> <see cref="DeathCauseDamage"/> alone cannot tell a citizen beaten to
        /// death by another citizen from one crushed under a roof they mined out from under themselves:
        /// both are <c>Blunt</c>, and <c>Blunt</c>'s own <c>deathMessage</c> is "{0} has been beaten to
        /// death". That ambiguity is not hypothetical — it is how <c>docs/WORK-REGISTER.md</c> §9a came to
        /// record <i>every</i> death in a founded settlement's first week as a citizen murdered by another
        /// citizen, and the integration test that checks the settlement is not killing itself needs to be
        /// able to tell the two apart or it measures the wrong system.
        ///
        /// <para/>Scribed with the rest of the death record, so it survives a save.
        /// </summary>
        public bool KilledByAnotherPawn => Dead && killedByPawn;

        /// <summary>Downs the pawn regardless of its body (RimWorld's <c>forceIncap</c>); cleared by the caller.</summary>
        public bool ForceDowned
        {
            get => forceDowned;
            set
            {
                if (forceDowned == value) return;
                forceDowned = value;
                CheckForStateChange(null, null);
            }
        }

        /// <summary>Pain at or above the pawn's shock threshold downs it.</summary>
        public bool InPainShock => hediffSet.PainTotal >= pawn.PainShockThreshold;

        /// <summary>
        /// This pawn is too young to be on its feet at all (<see cref="Pawns.LifeStageDef.alwaysDowned"/>,
        /// which <c>HumanlikeBaby</c> is the one shipped stage to set). An infant is not injured and is not
        /// unconscious; it simply cannot stand yet, and the stage it is in says so.
        ///
        /// <para/><b>Why this is a gate here and not a capacity answer.</b> The obvious alternative is to have
        /// <see cref="PawnCapacityWorker_Moving"/> return zero for such a stage, and it would cooperate with
        /// more of the simulation for free — everything that already asks "can this body move" would agree
        /// without being told. It is still the wrong place, because of what a capacity <i>is</i> here: every
        /// <see cref="PawnCapacityWorker"/> is handed a <see cref="HediffSet"/> and nothing else, and computes
        /// its answer from the parts in it. Zeroing Moving would encode "too young to walk" as "the legs do
        /// not work", and from then on every consumer of that number is reasoning about an injury that is not
        /// there: <see cref="ShouldBeDeadFromRequiredCapacity"/> is one content edit (<c>lethalFlesh</c> on
        /// Moving) away from killing every newborn, a surgeon looking for what to repair finds a whole body
        /// and a dead capacity, and the pawn's own health summary reports a cripple. A baby's legs are fine.
        /// So it sits beside <see cref="ForceDowned"/> instead — the seam that already exists for "down for a
        /// reason the body does not know about" — and the capacity system keeps telling the truth.
        ///
        /// <para/><b>And it is not permanent, which is the other half of getting it right.</b> The stage is
        /// what holds the pawn down, so the pawn gets up by leaving the stage: <c>HumanlikeBaby</c> ends at
        /// age three and this returns false from that tick on. <see cref="Pawns.Pawn_AgeTracker"/> raises
        /// <see cref="Pawn.Notify_LifeStageStarted"/> at the crossing, which re-runs
        /// <see cref="CheckForStateChange"/>, so the pawn stands up at the boundary rather than whenever
        /// something else next happens to disturb its health.
        /// </summary>
        public bool LifeStageForcesDowned => pawn.ageTracker?.CurLifeStage?.alwaysDowned ?? false;

        // ---- adding and removing ----

        public Hediff AddHediff(HediffDef def, BodyPartRecord? part = null)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, pawn, part);
            AddHediff(hediff, part);
            return hediff;
        }

        public void AddHediff(Hediff hediff, BodyPartRecord? part = null, DamageInfo? dinfo = null)
        {
            if (hediff == null) throw new ArgumentNullException(nameof(hediff));
            if (part != null) hediff.Part = part;
            hediffSet.AddDirect(hediff, dinfo);
            CheckForStateChange(dinfo, hediff);
        }

        public bool RemoveHediff(Hediff hediff)
        {
            bool removed = hediffSet.Remove(hediff);
            if (removed) CheckForStateChange(null, hediff);
            return removed;
        }

        /// <summary>
        /// Clears every hediff on the part and its children, making the part whole again
        /// (RimWorld: <c>Pawn_HealthTracker.RestorePart</c>); used before installing a replacement.
        ///
        /// <para/><b><paramref name="checkStateChange"/> is RimWorld's own parameter, and it was missing.</b>
        /// One caller passes false there and it matters: <see cref="Hediff_MissingPart.PostAdd"/> calls this
        /// to strip a part that a blow has just destroyed, and it is <i>inside</i> the
        /// <see cref="AddHediff(Hediff, BodyPartRecord?, DamageInfo?)"/> that is carrying the
        /// <see cref="DamageInfo"/> for that blow. Running the state change from here, where there is no
        /// dinfo to pass, meant a pawn killed by having a vital organ destroyed died with
        /// <see cref="DeathCauseDamage"/> null — the death record simply did not know what had killed them,
        /// so <see cref="DiedViolently"/> read false for a pawn beaten or shot to death and
        /// <see cref="KilledByAnotherPawn"/> could not name their killer. Deferring to the caller, as
        /// RimWorld does, lets the blow that landed be the blow that is recorded.
        /// </summary>
        public void RestorePart(BodyPartRecord part, Hediff? diffException = null, bool checkStateChange = true)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            for (int i = hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffSet.hediffs[i];
                if (ReferenceEquals(hediff, diffException) || hediff.Part == null) continue;
                if (ReferenceEquals(hediff.Part, part) || part.IsAncestorOf(hediff.Part))
                {
                    hediffSet.hediffs.RemoveAt(i);
                    hediff.PostRemoved();
                }
            }
            hediffSet.DirtyCache();
            if (checkStateChange) CheckForStateChange(null, null);
        }

        // ---- notifications ----

        /// <summary>
        /// A hediff changed; re-derive Downed/Dead. <paramref name="capacitiesMayHaveChanged"/> is true for
        /// every caller except <see cref="Hediff.Severity"/>'s setter, which already knows a within-stage
        /// severity nudge on a non-injury hediff cannot have moved anything the capacity/pain/bleed/core-part
        /// caches read (docs/perf/baseline.md §10) and says so — skipping <see cref="HediffSet.DirtyCache"/>
        /// there, not skipping this method, so <see cref="CheckForStateChange"/> (and so
        /// <see cref="Hediff.CauseDeathNow"/>, which is never cached) still runs every time.
        /// </summary>
        public void Notify_HediffChanged(Hediff? hediff, bool capacitiesMayHaveChanged = true)
        {
            if (capacitiesMayHaveChanged) hediffSet.DirtyCache();
            CheckForStateChange(null, hediff);
        }

        public void Notify_HediffSetChanged()
        {
            capacities.Notify_CapacityLevelsDirty();
            summaryHealth.Notify_HealthChanged();
        }

        /// <summary>
        /// One completed application of damage to this pawn, injury and all (RimWorld:
        /// <c>Pawn_HealthTracker.PostApplyDamage</c>, reached there from <c>Thing.TakeDamage</c> and here from
        /// <see cref="DamageWorker.Apply"/>, this port's only funnel for damage to a pawn).
        ///
        /// <para/>Its one job today is telling the AI layer, which is what lets being shot interrupt whatever
        /// the victim was doing (<see cref="SimWorld.AI.Pawn_JobTracker.Notify_DamageTaken"/>). A hit that
        /// killed outright notifies nothing: a dead pawn has already had its job ended by
        /// <see cref="Pawn.Notify_Died"/>, and starting it another would strand the reservations that ending
        /// released. RimWorld also raises <c>Pawn_MindState.Notify_DamageTaken</c> from here for its
        /// harm-driven mental breaks; this port's <see cref="SimWorld.MindState.Pawn_MindState"/> has no such
        /// hook, and inventing one is the mood module's call, not this one's.
        /// </summary>
        /// <param name="dinfo">The hit, including whatever dealt it — the AI layer reads
        /// <see cref="DamageInfo.Instigator"/> to tell "someone new is shooting me" from "the enemy I am
        /// already fighting hit me back".</param>
        /// <param name="totalDamageDealt">How much actually landed after armor; 0 for a deflected hit, which
        /// still counts as being shot at and still interrupts.</param>
        public void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (dinfo == null) throw new ArgumentNullException(nameof(dinfo));
            if (Dead) return;
            pawn.jobs?.Notify_DamageTaken(dinfo);
        }

        // ---- ticking ----

        public void HealthTick()
        {
            if (Dead) return;

            List<Hediff> hediffs = hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (i >= hediffs.Count) continue;
                hediffs[i].Tick();
                if (Dead) return;
            }

            bool removedAny = false;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (hediffs[i].ShouldRemove)
                {
                    Hediff removed = hediffs[i];
                    hediffs.RemoveAt(i);
                    removed.PostRemoved();
                    removedAny = true;
                }
            }
            if (removedAny) hediffSet.DirtyCache();

            immunity.ImmunityHandlerTick();

            if (pawn.RaceProps.IsFlesh)
            {
                if (pawn.IsHashIntervalTick(HealthTuning.HealInterval))
                {
                    TryNaturalHealing();
                    if (Dead) return;
                }
                if (pawn.IsHashIntervalTick(HealthTuning.BleedInterval))
                {
                    float bleedRate = hediffSet.BleedRateTotal;
                    if (bleedRate >= HealthTuning.MinBleedRateToBleed)
                    {
                        HealthUtility.AdjustSeverity(pawn, HediffDefOf.BloodLoss, bleedRate * HealthTuning.BloodLossPerBleedUnitPerInterval);
                        // system: filth — a bleeding pawn leaves blood where it stands (RimWorld:
                        // Pawn_HealthTracker.DropBloodFilth, called from this same bleed branch). The whole
                        // roll lives in the filth module so this stays one line.
                        SimWorld.Filth.BloodFilthUtility.DropBloodFilth(pawn, bleedRate);
                    }
                }
            }

            CheckForStateChange(null, null);
        }

        /// <summary>
        /// Every 600 ticks one random healable wound closes by 8/day (+4 lying down), scaled by health scale and
        /// hediff healing factors; a tended wound additionally heals 22 × tend quality per day unless the pawn is
        /// starving (RimWorld: <c>Pawn_HealthTracker.HealthTick</c>).
        /// </summary>
        private void TryNaturalHealing()
        {
            bool healed = false;
            // Indexed loops rather than GetHediffs<T>(): that helper is a `yield return` iterator and
            // allocates an enumerator per call even when the pawn has no injuries at all. This runs on
            // every heal interval for every pawn regardless (docs/perf/baseline.md §4).
            tmpInjuries.Clear();
            List<Hediff> all = hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Hediff_Injury injury && injury.CanHealNaturally()) tmpInjuries.Add(injury);
            }
            if (tmpInjuries.Count > 0)
            {
                float perDay = HealthTuning.BaseHealPerDay;
                if (pawn.Lying) perDay += HealthTuning.LyingDownHealBonusPerDay;
                perDay *= hediffSet.NaturalHealingFactor;
                Hediff_Injury target = tmpInjuries[Rand.Range(0, tmpInjuries.Count)];
                target.Heal(perDay * pawn.HealthScale / HealthTuning.HealIntervalsPerDay);
                healed = true;
            }

            bool starving = pawn.needs.food != null && pawn.needs.food.Starving;
            if (!starving)
            {
                tmpInjuries.Clear();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is Hediff_Injury injury && injury.IsTended && !injury.IsPermanent) tmpInjuries.Add(injury);
                }
                if (tmpInjuries.Count > 0)
                {
                    Hediff_Injury target = tmpInjuries[Rand.Range(0, tmpInjuries.Count)];
                    target.Heal(HealthTuning.TendedHealPerDay * target.TendQuality * pawn.HealthScale / HealthTuning.HealIntervalsPerDay);
                    healed = true;
                }
            }
            tmpInjuries.Clear();

            if (healed)
            {
                for (int i = hediffSet.hediffs.Count - 1; i >= 0; i--)
                {
                    if (hediffSet.hediffs[i] is Hediff_Injury injury && injury.ShouldRemove)
                    {
                        hediffSet.hediffs.RemoveAt(i);
                        injury.PostRemoved();
                    }
                }
                hediffSet.DirtyCache();
            }
        }

        // ---- state ----

        public void CheckForStateChange(DamageInfo? dinfo, Hediff? hediff)
        {
            if (Dead) return;
            if (ShouldBeDead())
            {
                Kill(dinfo, hediff);
                return;
            }
            if (!Downed)
            {
                if (ShouldBeDowned()) MakeDowned();
            }
            else if (!ShouldBeDowned())
            {
                MakeUndowned();
            }
        }

        public bool ShouldBeDead()
        {
            if (Dead) return true;
            List<Hediff> hediffs = hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].CauseDeathNow()) return true;
            }
            if (ShouldBeDeadFromRequiredCapacity() != null) return true;
            if (hediffSet.CorePartEfficiency <= HealthTuning.MinCapableLevel) return true;
            return ShouldBeDeadFromLethalDamageThreshold();
        }

        /// <summary>The first lethal capacity the pawn no longer has, or null.</summary>
        public PawnCapacityDef? ShouldBeDeadFromRequiredCapacity()
        {
            bool flesh = pawn.RaceProps.IsFlesh;
            IReadOnlyList<PawnCapacityDef> all = DefDatabase<PawnCapacityDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                bool lethal = flesh ? all[i].lethalFlesh : all[i].lethalMechanoids;
                if (lethal && !capacities.CapableOf(all[i])) return all[i];
            }
            return null;
        }

        public bool ShouldBeDeadFromLethalDamageThreshold() => hediffSet.TotalInjurySeverityCached >= HealthTuning.LethalDamageThreshold;

        public bool ShouldBeDowned()
        {
            if (forceDowned) return true;
            if (LifeStageForcesDowned) return true;
            if (InPainShock) return true;
            if (!capacities.CanBeAwake) return true;
            return !capacities.CapableOf(PawnCapacityDefOf.Moving);
        }

        /// <summary>
        /// Guarded against re-entry, not only against its caller's own <c>if (!Downed)</c>: reading a life
        /// stage can recompute it (<see cref="Pawns.Pawn_AgeTracker.CurLifeStage"/> is lazy), and recomputing
        /// it into a new stage calls <see cref="CheckForStateChange"/> back — so this can be reached from
        /// inside <see cref="ShouldBeDowned"/>'s own evaluation. Without the guard that pawn would be
        /// announced downed twice, and the storyteller charged twice for one event.
        /// </summary>
        private void MakeDowned()
        {
            if (healthState != PawnHealthState.Mobile) return;
            healthState = PawnHealthState.Down;
            pawn.Notify_Downed();

            // The storyteller hears about it too (RimWorld: MakeDowned raises the same adaptation event from
            // exactly here). Reached through Director only to ask it the question — that class decides whether
            // this pawn is one of the civilization's, because this funnel runs for raiders and animals as well;
            // see StorytellerPawnEvents for why the test is roster membership and not Pawn.faction.
            //
            // Except when the life stage is what put them down. Adaptation counts casualties — "how badly is
            // this colony being hurt" — and an infant being an infant is not a casualty. Nothing harmed it,
            // nothing can heal it, and it will get up on its third birthday; charging the curve for every
            // baby born would read the arrival of a child as a colonist going down under fire, which is the
            // same kind of backwards this hook's own doc warns about for downed raiders.
            if (!LifeStageForcesDowned) SimWorld.Director.StorytellerPawnEvents.Notify_PawnDowned(pawn);
        }

        private void MakeUndowned()
        {
            if (healthState != PawnHealthState.Down) return;
            healthState = PawnHealthState.Mobile;
        }

        /// <summary>
        /// This pawn was killed rather than merely dying (RimWorld: the meaning of
        /// <see cref="DamageDef.externalViolence"/>). False for a living pawn, for age, starvation and
        /// disease — which arrive with no damage at all — and for damage that declares itself non-violent,
        /// which is how a surgery that kills the patient stays a tragedy rather than a murder.
        ///
        /// <para/>Answered from <see cref="DeathCauseDamage"/>, which is Scribed, so it survives a save and a
        /// reload along with the rest of the death record.
        /// </summary>
        public bool DiedViolently => Dead && deathCauseDamage != null && deathCauseDamage.externalViolence;

        public void Kill(DamageInfo? dinfo, Hediff? exactCulprit)
        {
            if (Dead) return;
            healthState = PawnHealthState.Dead;
            deathTick = Find.TickManager?.TicksGame ?? -1;
            deathCauseDamage = dinfo?.Def;
            deathCauseHediff = exactCulprit?.def;
            killedByPawn = dinfo?.Instigator is Pawn killer && !ReferenceEquals(killer, pawn);
            hediffSet.DirtyCache();

            // Everyone who needs to know, before the body is made. Ordered: the people who watched get their
            // memory while the corpse-maker has not yet taken the pawn off the map (Notify_Died does that,
            // and an unspawned victim has no witnesses), and the narrator hears about it either way.
            //
            // Reached through Director exactly as MakeDowned reaches StorytellerPawnEvents, and for the same
            // reason: this funnel runs for raiders and animals too, so somebody has to ask "is this one of
            // ours" and that question belongs on the Director side of the line, not here.
            SimWorld.Thoughts.PawnDiedThoughtsUtility.Notify_PawnDied(pawn, dinfo);
            SimWorld.Director.StorytellerDeathEvents.Notify_PawnDied(pawn, dinfo, exactCulprit);

            pawn.Notify_Died();
        }

        // ---- Scribe ----

        public void ExposeData()
        {
            Scribe_Values.Look(ref healthState, "healthState", PawnHealthState.Mobile);
            Scribe_Values.Look(ref forceDowned, "forceDowned");
            Scribe_Values.Look(ref deathTick, "deathTick", -1);
            Scribe_Defs.Look(ref deathCauseHediff, "deathCauseHediff");
            Scribe_Defs.Look(ref deathCauseDamage, "deathCauseDamage");
            Scribe_Values.Look(ref killedByPawn, "killedByPawn");
            HediffSet? set = hediffSet;
            Scribe_Deep.Look(ref set, "hediffSet", pawn);
            hediffSet = set ?? new HediffSet(pawn);
            ImmunityHandler? imm = immunity;
            Scribe_Deep.Look(ref imm, "immunity", pawn);
            immunity = imm ?? new ImmunityHandler(pawn);
            BillStack? bills = surgeryBills;
            Scribe_Deep.Look(ref bills, "surgeryBills", new SurgeryBillGiver(pawn));
            surgeryBills = bills ?? new BillStack(new SurgeryBillGiver(pawn));

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hediffSet.DirtyCache();

                // A Bill_Medical saves its body part as an address, not an object — see that class. The pawn
                // is the only thing that can turn the address back into a part of a real body.
                for (int i = 0; i < surgeryBills.Count; i++)
                {
                    (surgeryBills[i] as Bill_Medical)?.ResolvePartAfterLoad(pawn);
                }
            }
        }
    }

    /// <summary>
    /// One number for "how hurt is this pawn" (RimWorld: <c>Verse.SummaryHealthHandler</c>). Wounds scale it by
    /// their fraction of the part's hit points weighted by the part's share of the body; missing parts remove
    /// their share outright. RimWorld's exact weighting is not reproduced, only its shape.
    /// </summary>
    public class SummaryHealthHandler
    {
        private readonly Pawn pawn;
        private float cached = -1f;

        public SummaryHealthHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public float SummaryHealthPercent
        {
            get
            {
                if (pawn.Dead) return 0f;
                if (cached < 0f) cached = Calculate();
                return cached;
            }
        }

        public void Notify_HealthChanged()
        {
            cached = -1f;
        }

        private float Calculate()
        {
            HediffSet set = pawn.health.hediffSet;
            float health = 1f;
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && injury.Part != null && injury.Visible)
                {
                    float max = injury.Part.def.GetMaxHealth(pawn);
                    if (max > 0f) health *= 1f - GenMath.Clamp01(injury.Severity / max) * injury.Part.coverageAbsWithChildren;
                }
            }
            health *= set.GetCoverageOfNotMissingNaturalParts();
            return GenMath.Clamp01(health);
        }
    }
}
