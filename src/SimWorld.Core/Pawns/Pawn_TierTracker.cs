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
    /// <b>One of the four can be held back, and only one.</b> Attention is a property of the camera rather
    /// than of the person, so the whole roster of an open settlement holds it at once and it alone can
    /// outgrow what Full tier costs. <see cref="Notify_AttentionChanged(bool,bool)"/> therefore carries the
    /// Full-tier budget's answer with it: a citizen the budget could not seat is still
    /// <see cref="Attending"/>, but that attention does not count toward <see cref="Significant"/>
    /// (<see cref="God.AttentionBudget"/> decides who, in significance order). Role, chronicle mention and
    /// relation are properties of the person, are held by few, and are never withheld by anything — the cap
    /// holds back the merely-looked-at, never the leader.
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
        private bool attentionWithheld;
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

        /// <summary>The player is attending this citizen's settlement. Says nothing about whether that
        /// attention is <i>counting</i> for them — a citizen the Full-tier budget held back is still attended,
        /// and still reports true here. See <see cref="AttentionWithheld"/>.</summary>
        public bool Attending => attending;

        /// <summary>This citizen is attended, but the Full-tier budget gave their seat to someone more
        /// significant, so attention does not count toward <see cref="Significant"/> for them
        /// (<see cref="TieringTuning.FullTierBudget"/>, filled by <see cref="God.AttentionBudget"/>). Only ever
        /// true while <see cref="Attending"/> is — the two-argument
        /// <see cref="Notify_AttentionChanged(bool,bool)"/> clears it on the way out of attention, so a
        /// citizen can never carry a stale withholding into a settlement nobody is looking at.</summary>
        public bool AttentionWithheld => attentionWithheld;

        public bool HasRole => hasRole;
        public bool ChronicleNamed => chronicleNamed;
        public bool RelatedToPromoted => relatedToPromoted;

        /// <summary>
        /// Any of the four promotion reasons currently holds. Attention is the one of the four that can be
        /// held back: it counts only while it is within the Full-tier budget, because it is the only reason
        /// the whole roster holds at once (see <see cref="God.AttentionBudget"/> for the ordering and why
        /// attention is the reason that yields). The other three are properties of the person and are never
        /// withheld by anything.
        /// </summary>
        public bool Significant => AttentionCounts || hasRole || chronicleNamed || relatedToPromoted;

        /// <summary>Attention, after the budget has had its say.</summary>
        private bool AttentionCounts => attending && !attentionWithheld;

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
        /// Full back to Interval. Attention granted this way always counts — see the overload for the form the
        /// Full-tier budget uses.</summary>
        public void Notify_AttentionChanged(bool isAttending)
        {
            Notify_AttentionChanged(isAttending, withinBudget: true);
        }

        /// <summary>
        /// The same notification, carrying the Full-tier budget's answer alongside it:
        /// <paramref name="withinBudget"/> false means the player is attending this citizen's settlement but
        /// the budget gave their seat to someone more significant, so attention does not count toward
        /// <see cref="Significant"/> and they stay where they are rather than being promoted to
        /// <see cref="PawnTier.Full"/>.
        ///
        /// <para/><b>Why a second argument rather than the director simply not notifying them.</b> Passing
        /// <c>false</c> for <paramref name="isAttending"/> to a citizen whose settlement is open would be the
        /// smaller change — no field, no Scribe line — and it is wrong: this method means what it says, and
        /// <see cref="Attending"/> is read as "the player is looking at this citizen's settlement", which
        /// would become a lie for exactly the citizens the cap touches. It would also silently hand them to
        /// <see cref="God.AttentionManager"/>'s settle policy, which keys off "not attended" and would sink
        /// the overflow of an <i>open</i> settlement into the Statistical cohort a year later — thinning what
        /// the chronicle can ever say about them (§11.4) as a side effect of a performance cap. Keeping the
        /// two facts separate — attended, and within budget — keeps both readable and keeps the demotion a
        /// demotion rather than a lie about where the camera is.
        ///
        /// <para/>Withholding is never sticky: leaving attention clears it, so the flag cannot outlive the
        /// focus that produced it.
        ///
        /// <para/>One consequence worth stating rather than discovering: a withheld citizen is insignificant
        /// by <see cref="Significant"/>, so their <see cref="InsignificantSinceTick"/> clock runs while they
        /// are held back. Nothing settles them while the settlement is open (the settle policy keys off the
        /// settlement being unattended), but a citizen held back for longer than
        /// <see cref="TieringTuning.IntervalSettleTicks"/> can settle on the first sweep <i>after</i> the god
        /// looks away rather than serving a fresh year first. That is the honest reading of the clock — they
        /// genuinely had no individual significance for that whole span — and not an accident of where the
        /// flag lives.
        /// </summary>
        public void Notify_AttentionChanged(bool isAttending, bool withinBudget)
        {
            attending = isAttending;
            attentionWithheld = isAttending && !withinBudget;
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
        /// deserves individual distinction, not routine demographic bookkeeping. One-directional: a chronicle
        /// entry is never un-written, so this never clears.
        /// <para/>
        /// <b>Two directors now answer "what qualifies", and both read <see cref="Director.MomentCurator"/>
        /// rather than the chronicle itself</b> — the curated landmarks, not the rolling news:
        /// <see cref="Director.Storyteller.RecordDeath"/> names a pawn whose death the curator flagged as a
        /// moment, and <see cref="Director.ChronicleFame"/> names the living citizen who has outlived every
        /// life the civilization has ever known. The second exists because the first is a policy for the dead
        /// and none at all for the living, and a tier that decides how much simulation a citizen gets has
        /// nothing to say about someone who has stopped being simulated. See <c>ChronicleFame</c>'s own doc
        /// for why a record, of everything the curator knows, is the one shape that stays rare for ever.
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
            Scribe_Values.Look(ref attentionWithheld, "attentionWithheld");
            Scribe_Values.Look(ref hasRole, "hasRole");
            Scribe_Values.Look(ref chronicleNamed, "chronicleNamed");
            Scribe_Values.Look(ref relatedToPromoted, "relatedToPromoted");
            Scribe_Values.Look(ref insignificantSinceTick, "insignificantSinceTick", NeverInsignificant);
            Scribe_Values.Look(ref lastProcessedTick, "lastProcessedTick");
            Scribe_Values.Look(ref lastSampledHealthFraction, "lastSampledHealthFraction", 1f);
        }
    }
}
