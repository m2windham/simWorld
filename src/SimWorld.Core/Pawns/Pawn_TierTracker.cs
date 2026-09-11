using System;
using SimWorld.AI;
using SimWorld.Needs;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A citizen's simulation tier (<c>docs/spec/simworld-spec.md</c> §11.3) — SimWorld's own; RimWorld's nearest
    /// relative is the unlabeled distinction between a pawn on an active map and a "world pawn", which this
    /// generalises from <i>on the map or not</i> to <i>significant or not</i>.
    /// <para/>
    /// <b>Promotion is by significance, never proximity or elapsed time.</b> Four notifications drive it —
    /// <see cref="Notify_AttentionChanged"/> (the player is looking at this citizen's settlement),
    /// <see cref="Notify_RoleChanged"/> (they hold a role — leader, founder, great worker), <see cref="Notify_ChronicleNamed"/>
    /// (the chronicle singled them out — see the doc on that method for why this is deliberately <i>not</i> wired
    /// to every routine birth/death entry), and <see cref="Notify_RelatedToPromoted"/> (a relationship attaches
    /// them to someone already promoted — see <see cref="FamilyManager.PromoteRelativesOf"/>). Any of the four
    /// promotes straight to <see cref="PawnTier.Full"/>, skipping <see cref="PawnTier.Interval"/> entirely, per
    /// the spec's own flowchart (§11.4's mermaid: <c>Set --&gt; Full</c> and <c>Interval --&gt;|role, chronicle
    /// mention, relation| Full</c> both bypass a two-step climb). Demotion is the mirror and is lossless in
    /// identity: <see cref="PawnTier.Full"/> falls to <see cref="PawnTier.Interval"/> the instant none of the
    /// four hold, but the further fall to <see cref="PawnTier.Statistical"/> is never automatic — see
    /// <see cref="DemoteToStatistical"/>.
    /// <para/>
    /// <b>The one thing here that knows about elapsed time is <see cref="InsignificantSinceTick"/>, and it can
    /// only ever push a citizen down.</b> It records <i>when</i> this citizen last stopped being significant so
    /// that a director (<see cref="God.AttentionManager"/> is the one that exists) can express "insignificant
    /// for long enough" without this class inventing how long that is. It is read by
    /// <see cref="HasBeenInsignificantFor"/> and by nothing else; no promotion path consults it, so the rule
    /// that promotion is by significance and never by elapsed time is untouched.
    /// </summary>
    public sealed class Pawn_TierTracker : IExposable
    {
        /// <summary>Sentinel <see cref="InsignificantSinceTick"/>: this citizen's insignificance clock is not
        /// running — either they are significant right now, or nothing has ever told them they are not (a
        /// freshly generated citizen starts <see cref="PawnTier.Full"/> with no notification behind it, and a
        /// director that never looks at them never starts their clock).</summary>
        public const int NeverInsignificant = -1;

        private readonly Pawn pawn;

        private PawnTier tier = PawnTier.Full;
        private bool attending;
        private bool hasRole;
        private bool chronicleNamed;
        private bool relatedToPromoted;

        /// <summary>See <see cref="InsignificantSinceTick"/>.</summary>
        private int insignificantSinceTick = NeverInsignificant;

        /// <summary>The game tick this citizen's non-Full state was last brought current — by a coarse tick
        /// (<see cref="CoarseTick"/>) or a tier change. Meaningless while <see cref="PawnTier.Full"/> (that tier
        /// advances every tick through the ordinary trackers, not through this bookkeeping).</summary>
        private int lastProcessedTick;

        private float lastSampledHealthFraction = 1f;

        public Pawn_TierTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public PawnTier Tier => tier;

        public bool Attending => attending;
        public bool HasRole => hasRole;
        public bool ChronicleNamed => chronicleNamed;
        public bool RelatedToPromoted => relatedToPromoted;

        /// <summary>Any of the four promotion reasons currently holds.</summary>
        public bool Significant => attending || hasRole || chronicleNamed || relatedToPromoted;

        /// <summary>
        /// The game tick at which this citizen last stopped being <see cref="Significant"/>, or
        /// <see cref="NeverInsignificant"/> while they are significant or while nothing has ever said
        /// otherwise. Stamped by <see cref="Recompute"/> on the transition into insignificance and cleared on
        /// the way back out, so a citizen who is briefly significant again starts the clock over rather than
        /// accumulating a total — "has been insignificant continuously for N ticks", not "has spent N ticks
        /// insignificant". Exposed because the <i>policy</i> that reads it lives outside this class on
        /// purpose (see <see cref="DemoteToStatistical"/>); <see cref="HasBeenInsignificantFor"/> is the
        /// question a director actually wants to ask.
        /// </summary>
        public int InsignificantSinceTick => insignificantSinceTick;

        /// <summary>True when this citizen has been continuously insignificant for at least
        /// <paramref name="ticks"/>. False whenever the clock is not running at all — a significant citizen,
        /// or one no director has ever considered.</summary>
        public bool HasBeenInsignificantFor(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (Significant || insignificantSinceTick == NeverInsignificant) return false;
            return Find.TickManager.TicksGame - insignificantSinceTick >= ticks;
        }

        /// <summary>A cohort-sampled "how hale is this citizen" readout for the Statistical tier (refreshed each
        /// <see cref="CoarseTick"/>; see <see cref="TieringTuning"/>). Deliberately not wired into
        /// <see cref="Health.Pawn_HealthTracker"/>'s real hediff/capacity system: manufacturing specific injuries
        /// nobody ever gave this citizen would be the exact anti-pattern §11.4 warns against for the chronicle,
        /// applied instead to the engine's own state. Meaningless outside <see cref="PawnTier.Statistical"/>.</summary>
        public float SampledHealthFraction => lastSampledHealthFraction;

        // ---- significance notifications ----

        /// <summary>The player is (or is no longer) attending this citizen's settlement. The one trigger that
        /// can promote directly and also the one whose absence, combined with no role/mention/relation, demotes
        /// Full back to Interval.</summary>
        public void Notify_AttentionChanged(bool isAttending)
        {
            attending = isAttending;
            Recompute();
        }

        /// <summary>The citizen took (or left) a role — leader, founder, great worker. No Role system exists yet
        /// to drive this automatically; it is the hook such a system calls once it does.</summary>
        public void Notify_RoleChanged(bool nowHasRole)
        {
            hasRole = nowHasRole;
            Recompute();
        }

        /// <summary>
        /// The chronicle singled this citizen out by name. Deliberately a separate, explicitly-invoked flag
        /// rather than something wired to every <c>Storyteller.RecordChronicle</c>/<c>RecordDeath</c> call:
        /// <see cref="FamilyManager.DoBirth"/> and <see cref="FamilyManager.HandleDeath"/> already chronicle
        /// <i>every</i> birth and death unconditionally, so treating "has a chronicle entry" as "the chronicle
        /// named them" would make every citizen significant from the moment they are born — the tier would
        /// never demote anyone, which defeats the module. Call this only when something decides a citizen
        /// deserves individual distinction, not routine demographic bookkeeping; deciding what qualifies is a
        /// director-policy question this module deliberately leaves open (see the class doc and the module's
        /// own report). One-directional: a chronicle entry is never un-written, so this never clears.
        /// </summary>
        public void Notify_ChronicleNamed()
        {
            if (chronicleNamed) return;
            chronicleNamed = true;
            Recompute();
        }

        /// <summary>A relationship attaches (or no longer attaches) this citizen to someone already promoted.
        /// See <see cref="FamilyManager.PromoteRelativesOf"/> for the one-hop family walk that calls this.</summary>
        public void Notify_RelatedToPromoted(bool related)
        {
            relatedToPromoted = related;
            Recompute();
        }

        private void Recompute()
        {
            if (Significant)
            {
                insignificantSinceTick = NeverInsignificant;
                if (tier != PawnTier.Full) PromoteTo(PawnTier.Full);
            }
            else
            {
                // Start the clock on the transition into insignificance, never restart it while it is already
                // running: every sweep re-asserts Notify_AttentionChanged(false) on the same unattended
                // citizens, and a stamp taken on each of those would hold the clock permanently at zero.
                if (insignificantSinceTick == NeverInsignificant) insignificantSinceTick = Find.TickManager.TicksGame;
                if (tier == PawnTier.Full) Demote(PawnTier.Interval);
            }
        }

        /// <summary>
        /// The one demotion step this tracker never takes on its own: Interval settling to Statistical once a
        /// citizen has been insignificant long enough that carrying their full state is no longer worth it.
        /// "Long enough" is a director/game-loop policy question (<c>docs/spec/simworld-spec.md</c> §11.5: "how
        /// the abstract clock and the director interact" is explicitly left open) — inventing a number here
        /// would mean inventing that policy, and the brief this module was built from is explicit that demotion
        /// is by significance, never elapsed time. So this is a mechanism, not a policy: it succeeds, silently
        /// no-oping otherwise, only when the citizen is already Interval and currently insignificant, and
        /// whoever calls it is the one deciding when.
        /// <para/>
        /// That decision now has an owner: <see cref="God.AttentionManager"/> settles a citizen who has been
        /// insignificant for <see cref="TieringTuning.IntervalSettleTicks"/>, asking
        /// <see cref="HasBeenInsignificantFor"/> rather than being handed a rule from in here. The split is
        /// the point — this class still refuses to know how long "long enough" is, so a different director
        /// can answer differently without touching the tier machinery.
        /// </summary>
        public void DemoteToStatistical()
        {
            if (tier == PawnTier.Interval && !Significant)
            {
                Demote(PawnTier.Statistical);
            }
        }

        // ---- promotion / demotion mechanics ----

        private void PromoteTo(PawnTier target)
        {
            if (tier == target) return;
            int now = Find.TickManager.TicksGame;
            if (tier != PawnTier.Full)
            {
                // Catch-up: bring age/needs/health current as of *this* tick before the pawn starts being
                // ticked per-tick again — see the class doc and the module's report for what this can and
                // cannot reconstruct.
                ApplyElapsed(now);
            }
            SetTierAndReRegister(target);
            lastProcessedTick = now;

            if (target == PawnTier.Full)
            {
                // A citizen arriving at Full should never carry a job left over from decades at a coarser
                // tier — Interval and Statistical never start one (no jobs, per spec), but be defensive.
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.pather?.StopDead();
            }
        }

        private void Demote(PawnTier target)
        {
            if (tier == target) return;
            if (tier == PawnTier.Full)
            {
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.pather?.StopDead();
            }
            SetTierAndReRegister(target);
            lastProcessedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Moves the pawn between tick lists by changing which one <see cref="Pawn.TickerType"/> answers with
        /// (RimWorld's world-pawn precedent generalised: a demoted pawn leaves the Normal list entirely rather
        /// than being skipped inside it — <c>docs/spec/simworld-spec.md</c> §11.3, and the module's brief).
        /// Deregisters using the OLD tier's list, flips the field, then registers using the NEW tier's list —
        /// in that order, since <see cref="TickManager.DeRegisterAllTickabilityFor"/>/<see cref="TickManager.RegisterAllTickabilityFor"/>
        /// both key off <c>pawn.TickerType</c>, which reads this very field.
        /// </summary>
        private void SetTierAndReRegister(PawnTier newTier)
        {
            TickerType oldType = TickerTypeFor(tier);
            TickerType newType = TickerTypeFor(newTier);
            if (oldType == newType)
            {
                // Interval <-> Statistical share TickerType.Long: no list to move between.
                tier = newTier;
                return;
            }

            TickManager tm = Find.TickManager;
            TickList? oldList = tm.TickListFor(oldType);
            bool wasRegistered = oldList != null && oldList.Contains(pawn);
            if (wasRegistered) tm.DeRegisterAllTickabilityFor(pawn);

            tier = newTier;

            if (wasRegistered) tm.RegisterAllTickabilityFor(pawn);
        }

        /// <summary>Full ticks every tick; Interval and Statistical both sit on the Long bucket (~2000 ticks) and
        /// are told apart only by what <see cref="CoarseTick"/> does once there — see the class doc for why
        /// Statistical does not simply leave the tick list altogether.</summary>
        public static TickerType TickerTypeFor(PawnTier t) => t == PawnTier.Full ? TickerType.Normal : TickerType.Long;

        // ---- coarse ticking (Interval and Statistical) ----

        /// <summary>
        /// Called from <see cref="Pawn.TickLong"/> when this pawn is not Full. Brings age (always real, via
        /// <see cref="Pawn_AgeTracker.AgeTickMothballed"/> — exact regardless of the gap) and, at Interval, needs
        /// (real bulk decay, <see cref="Need.NeedIntervalBulk"/>) or, at Statistical, a fresh cohort sample
        /// current as of now. This is what keeps a citizen sitting at either tier able to participate in
        /// demography (<see cref="FamilyManager.DemographyTick"/> reads <c>ageTracker</c> and <c>needs</c>
        /// directly and does not care which tier produced them) without costing anything per real tick.
        /// </summary>
        public void CoarseTick()
        {
            if (tier == PawnTier.Full) return;
            ApplyElapsed(Find.TickManager.TicksGame);
        }

        private void ApplyElapsed(int now)
        {
            int elapsed = now - lastProcessedTick;
            if (elapsed <= 0)
            {
                lastProcessedTick = now;
                return;
            }

            pawn.ageTracker.AgeTickMothballed(elapsed);

            if (tier == PawnTier.Interval)
            {
                var needsList = pawn.needs?.AllNeeds;
                if (needsList != null)
                {
                    for (int i = 0; i < needsList.Count; i++)
                    {
                        needsList[i].NeedIntervalBulk(elapsed);
                    }
                }
                // Health: no bulk hediff physics (bleeding/healing/immunity) at this tier — hediffs already
                // present stay exactly as they were, which is the module's one honest limitation for Interval
                // (see the module's report). Still worth a cheap, safety-net state re-check: a life-stage
                // crossing during AgeTickMothballed above can change core-part efficiency with no hediff
                // changing at all (docs/perf/baseline.md §8), so re-evaluate downed/dead rather than assume
                // nothing could have changed.
                pawn.health?.CheckForStateChange(null, null);
            }
            else // Statistical
            {
                ApplyCohortSample(now);
            }

            lastProcessedTick = now;
        }

        /// <summary>
        /// Draws this citizen's needs and health fresh from the cohort's distribution, deterministically from
        /// the seeded stream (<see cref="RandomStream.RangeSeeded(float,float,int)"/> — a pure function of
        /// (pawn id, tick, need), not the shared mutable <see cref="Rand"/> sequence, so sampling a citizen
        /// costs nothing to any other system's draws and reproduces exactly for the same seed regardless of
        /// call order). Writes into the real <see cref="Need"/> objects (kept, not deleted, across tiers) so
        /// anything already reading them — <see cref="FamilyManager"/>'s mood/food checks among them — sees a
        /// plausible current value with no special-casing for tier.
        /// </summary>
        private void ApplyCohortSample(int atTick)
        {
            var needsList = pawn.needs?.AllNeeds;
            if (needsList != null)
            {
                for (int i = 0; i < needsList.Count; i++)
                {
                    Need need = needsList[i];
                    int seed = MurmurHash.Combine(pawn.thingIDNumber, atTick, need.def.index);
                    need.CurLevelPercentage = RandomStream.RangeSeeded(
                        TieringTuning.StatisticalNeedSampleMin, TieringTuning.StatisticalNeedSampleMax, seed);
                }
            }

            int healthSeed = MurmurHash.Combine(pawn.thingIDNumber, atTick, unchecked((int)0x4EA17BEEu));
            lastSampledHealthFraction = RandomStream.RangeSeeded(
                TieringTuning.StatisticalHealthFractionMin, TieringTuning.StatisticalHealthFractionMax, healthSeed);
        }

        // ---- Scribe ----

        public void ExposeData()
        {
            Scribe_Values.Look(ref tier, "tier", PawnTier.Full);
            Scribe_Values.Look(ref attending, "attending");
            Scribe_Values.Look(ref hasRole, "hasRole");
            Scribe_Values.Look(ref chronicleNamed, "chronicleNamed");
            Scribe_Values.Look(ref relatedToPromoted, "relatedToPromoted");
            Scribe_Values.Look(ref insignificantSinceTick, "insignificantSinceTick", NeverInsignificant);
            Scribe_Values.Look(ref lastProcessedTick, "lastProcessedTick");
            Scribe_Values.Look(ref lastSampledHealthFraction, "lastSampledHealthFraction", 1f);
        }
    }
}
