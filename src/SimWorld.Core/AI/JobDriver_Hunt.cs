using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Crafting;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Closes to weapon range, shoots (or swings) until the animal is dead, then butchers it where it fell
    /// (RimWorld: <c>RimWorld.JobDriver_Hunt</c>).
    /// <para/>
    /// <b>Combat really is driven from a toil.</b> Nothing needed adding to the Combat module: a
    /// <see cref="Verb"/> is an explicit per-tick state machine (<see cref="Verb.TryStartCastOn"/> arms it,
    /// <see cref="Verb.VerbTick"/> advances warmup → burst → cooldown and is what actually fires), so a
    /// <see cref="ToilCompleteMode.Never"/> toil ticking one verb is exactly the shape
    /// <see cref="MindState.MentalState_SocialFighting"/> already uses to trade blows — this is that same
    /// pattern hung off a job instead of a mental state. Damage, armour, body-part selection, downing and
    /// death all come from the existing pipeline (<c>DamageWorker</c> → <c>Pawn_HealthTracker.CheckForStateChange</c>);
    /// no kill is reimplemented here.
    /// <para/>
    /// <b>Ranged versus melee.</b> <see cref="HuntUtility.MakeHuntVerb"/> picks the wielded weapon's ranged
    /// verb when there is one and a melee <see cref="Tool"/> otherwise, and the approach loop stops at
    /// <see cref="HuntUtility.EffectiveRange"/> — so a bow hunter opens fire from ~26 cells and a knife
    /// hunter has to walk all the way in. Only the ranged case is reachable through
    /// <see cref="WorkGiver_Hunt"/> (RimWorld's <c>HasHuntingWeapon</c> requires a ranged weapon); the melee
    /// branch exists because RimWorld's own driver casts through a verb-agnostic path too, and because a
    /// directed order could hand this driver a melee hunter.
    /// <para/>
    /// <b>Translation — what a kill yields.</b> RimWorld's hunt ends by hauling a <c>Corpse</c> to storage
    /// for someone else to butcher at a bench. This port's hunter butchers where the animal fell.
    /// <para/>
    /// That began as a workaround for there being no <c>Corpse</c> class at all. There is one now
    /// (<see cref="Things.Corpse"/>), and the route RimWorld takes exists alongside this one:
    /// <see cref="WorkGiver_HaulCorpses"/> carries a body to storage and
    /// <see cref="WorkGiver_ButcherCorpse"/> has a cook fetch it and butcher it at a bench. <b>The kill-site
    /// toil is kept anyway</b>, deliberately, because it is the only route that closes with nothing else
    /// built: it needs no butcher bench, no stockpile that accepts corpses, and no second pawn. A settlement
    /// that has not built a <c>TableButcher</c> would otherwise hunt, kill, and starve beside the body.
    /// <b>When to retire it:</b> the day a settlement reliably builds and staffs a butcher bench, delete this
    /// driver's last toil and the hunt simply ends at the kill — the corpse givers already cover the rest,
    /// and they fetch a body from anywhere reachable rather than needing it staged.
    /// <para/>
    /// Both routes run the same butchery: the shipped <c>ButcherAnimal</c> <see cref="RecipeDef"/> through
    /// <see cref="ButcherUtility.TryButcher"/>, yield scaled by <c>Crafting.HusbandryTuning</c>'s body-size
    /// constants. There is no second yield formula anywhere. Meat and leather land as ordinary item stacks on
    /// the kill cell, from where <c>HaulGeneral</c>'s <see cref="WorkGiver_Haul"/> carries them to a
    /// stockpile.
    /// <para/>
    /// <b>Deliberate gap:</b> RimWorld awards Shooting/Melee XP for hunting and this does not — nothing in
    /// this port's Combat module awards combat XP from a verb cast at all, so doing it here would invent a
    /// hunting-only rule. Butchering's own Cooking XP (<c>Recipe_ButcherAnimal</c>) is awarded, unchanged.
    /// </summary>
    public sealed class JobDriver_Hunt : JobDriver
    {
        /// <summary>Built on first use and kept for the life of the job: a <see cref="Verb"/> carries
        /// warmup/burst/cooldown state, so rebuilding it per tick would mean a shot that never lands. Never
        /// Scribed — no <see cref="Verb"/> in this codebase is (see
        /// <see cref="MindState.MentalState_SocialFighting"/>'s own note): a fresh driver after a load simply
        /// re-aims, which costs one warmup.</summary>
        private Verb? verb;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override void Notify_Ending()
        {
            base.Notify_Ending();
            verb = null;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil hunt = MakeHuntToil();
            yield return hunt;

            yield return Toils_General.Do(Butcher);
        }

        /// <summary>
        /// Approach-and-attack, as one <see cref="ToilCompleteMode.Never"/> toil rather than a goto followed
        /// by a cast: prey moves (<see cref="JobGiver_AnimalFlee"/> walks it away from any nearby humanlike),
        /// so "am I in range?" has to be re-asked every tick, and a separate goto toil would have to finish
        /// before anyone could ask. Advances to the butchery toil the moment the target is dead.
        /// <para/>
        /// <b>No <c>FailOnDespawnedOrNull</c> on the prey.</b> It carried one until corpses existed, when a
        /// dead animal stayed spawned; now death takes the body off the map and into a
        /// <see cref="Things.Corpse"/> the same tick, so that condition would fail the job on the winning
        /// shot and the kill would never be butchered. Death is detected below instead, which is where it
        /// always was — the guard only ever covered prey vanishing some *other* way, and
        /// <see cref="HuntTick"/>'s own null/destroyed checks still cover that.
        /// </summary>
        private Toil MakeHuntToil()
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            // Deliberately no FailOn(pather.Failed), unlike Toils_Goto.GotoCell. That toil is safe with it
            // because its own initAction calls StartPath (which clears Failed) before any tick evaluates the
            // condition; this one starts paths from its tickAction instead, and Pawn_PathFollower.Failed
            // survives both StopDead and the end of a job — so a stale true from a previous job would fail
            // this one on its very first tick, the giver would re-offer the same animal, and the pair would
            // churn forever. Approach failure is handled inside HuntTick instead, after StartPath has had
            // its say.
            toil.tickAction = HuntTick;
            return toil;
        }

        private void HuntTick()
        {
            if (!(job.GetTarget(TargetIndex.A).Thing is Pawn animal))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (animal.Dead)
            {
                pawn.pather.StopDead();
                ReadyForNextToil();
                return;
            }

            Verb? v = verb ??= HuntUtility.MakeHuntVerb(pawn);
            if (v == null)
            {
                // Disarmed mid-hunt (or handed this job without a weapon at all): there is nothing to attack
                // with, and this port has no natural-weapon fallback — see HuntUtility.MakeHuntVerb.
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            float distance = (animal.Position - pawn.Position).LengthHorizontal;
            if (distance > HuntUtility.EffectiveRange(v))
            {
                // Re-path only once the previous path has run out: StartPath keeps an in-flight path to the
                // same target rather than recomputing it, so a hunter walks to where the animal was, then
                // re-aims from there. Cheap, and close enough while prey moves a cell at a time. It also
                // clears a failure left by the last path, so a route blocked mid-walk is simply retried from
                // where the hunter now stands; only a target it genuinely cannot get near ends the job.
                pawn.pather.StartPath(animal, PathEndMode.Touch);
                if (!pawn.pather.Moving) EndJobWith(JobCondition.Incompletable); // nowhere to stand within reach
                return;
            }

            if (pawn.pather.Moving) pawn.pather.StopDead();

            if (v.Available()) v.TryStartCastOn(animal, distance);
            v.VerbTick();

            if (HuntUtility.WoundedThisTick(v) && HuntUtility.TryProvokeRevenge(animal, pawn))
            {
                // Provoked prey is no longer prey: break off. WorkGiver_Hunt refuses an angry animal, so the
                // next job search picks something else rather than walking straight back into this.
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (animal.Dead) ReadyForNextToil();
        }

        /// <summary>
        /// Butchers the carcass where it lies — see this class's own doc for why here as well as at a bench.
        /// The target is still the animal <see cref="Pawn"/>: it is no longer on the map (its body is inside
        /// the <see cref="Things.Corpse"/> death left on the kill cell), and
        /// <see cref="ButcherUtility.TryButcher"/> reaches through to it either way. A no-op if something
        /// else got to the body first — a hauler carrying it to storage takes it off the map, and a corpse
        /// already destroyed or already butchered fails the same checks.
        /// </summary>
        private void Butcher()
        {
            if (!(job.GetTarget(TargetIndex.A).Thing is Pawn animal)) return;
            if (animal.Destroyed || !animal.Dead) return;

            Things.Corpse? corpse = animal.corpse;
            bool bodyIsHere = animal.Spawned || (corpse != null && corpse.Spawned && !corpse.Destroyed);
            if (!bodyIsHere) return;

            ButcherUtility.TryButcher(animal, pawn, HuntingDefOf.ButcherAnimal);
        }
    }
}
