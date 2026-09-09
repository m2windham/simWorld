using System;
using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;

namespace SimWorld.MindState
{
    /// <summary>
    /// A brief mutual scuffle (RimWorld: <c>RimWorld.MentalState_SocialFighting</c>). <see
    /// cref="SocialFightUtility.TryStartSocialFight"/> starts one instance per participant — each pawn's own
    /// <see cref="MentalStateHandler"/> owns its own state, exactly like any other mental state — then links
    /// the two together via <see cref="otherPawn"/> right after both exist (neither side knows the other at
    /// construction time: <see cref="MentalStateHandler.TryStartMentalState"/> only ever passes the one pawn
    /// it belongs to).
    /// <para/>
    /// Trades blows through the exact path any weapon uses — <see cref="MeleeVerbUtility.MakeVerb"/> building
    /// a <see cref="Verb_MeleeAttack"/> — rather than a second combat system: a bare-knuckled <see cref="Tool"/>
    /// with the <see cref="ToolCapacityDefOf.Blunt"/> capacity, resolved through the same "Smash" <see
    /// cref="ManeuverDef"/> a club would use (see <c>Data/Core/Defs/ManeuverDefs/Maneuvers.xml</c>). No Human
    /// ThingDef in this port defines natural "fists"/"teeth" tools yet (RimWorld's own <c>tools</c> on its
    /// Human race) — that's Combat module scope, not this one — so the fists <see cref="Tool"/> below is built
    /// in code instead of read from content; <see cref="SocialTuning.SocialFightFistPower"/> documents that
    /// choice.
    /// <para/>
    /// Ends at the Def's own duration/MTB recovery — inherited unchanged from <see cref="MentalState"/>, the
    /// same lifecycle every other mental state uses — or the moment either side goes down, dies, or is no
    /// longer in this same fight (its own instance recovered first), whichever happens first.
    /// </summary>
    public class MentalState_SocialFighting : MentalState
    {
        /// <summary>
        /// The other participant. Null only for the instant between <c>Activator.CreateInstance</c> (inside
        /// <see cref="MentalStateHandler.TryStartMentalState"/>) and <see
        /// cref="SocialFightUtility.TryStartSocialFight"/> setting it a moment later on the same call stack —
        /// every <see cref="MentalStateTick"/> after that sees it populated. Scribe'd as a cross-reference
        /// (same shape as <see cref="Thoughts.Thought_Memory.otherPawn"/>) since two independent <see
        /// cref="Pawn_MindState"/>s point at each other rather than one owning the other.
        /// </summary>
        public Pawn? otherPawn;

        /// <summary>Lazily built, never Scribe'd — <see cref="Verb"/> itself carries no <c>ExposeData</c>
        /// anywhere in this codebase; its mid-swing warmup/cooldown state is cheap to rebuild and does not
        /// need to survive a save.</summary>
        private Verb_MeleeAttack? verb;

        public MentalState_SocialFighting()
        {
        }

        public MentalState_SocialFighting(Pawn pawn) : base(pawn)
        {
        }

        private Verb_MeleeAttack EnsureVerb()
        {
            if (verb == null)
            {
                var fists = new Tool
                {
                    label = "fists",
                    capacities = new List<ToolCapacityDef> { ToolCapacityDefOf.Blunt },
                    power = SocialTuning.SocialFightFistPower,
                    cooldownTime = SocialTuning.SocialFightSwingCooldownSeconds,
                    armorPenetration = 0f,
                };
                verb = MeleeVerbUtility.MakeVerb(pawn, fists)
                    ?? throw new InvalidOperationException(
                        "Blunt has no ManeuverDef in content; MentalState_SocialFighting has nothing to swing with.");
            }
            return verb;
        }

        public override void MentalStateTick()
        {
            base.MentalStateTick();
            // base.MentalStateTick() may itself have just recovered this instance (duration elapsed, or the
            // MTB roll hit) — nothing left to do this tick if so.
            if (!ReferenceEquals(pawn.mindState.mentalStateHandler.CurState, this)) return;

            // A downed pawn cannot swing; MentalStateHandler.MentalStateHandlerTick recovers it (def sets
            // recoverFromDowned) right after this call returns, so there is nothing further to do here.
            if (pawn.Downed) return;

            if (otherPawn == null || otherPawn.Dead || otherPawn.Downed || otherPawn.MentalStateDef != def)
            {
                RecoverFromState();
                return;
            }

            Verb_MeleeAttack v = EnsureVerb();
            if (v.Available()) v.TryStartCastOn(otherPawn, 0f);
            v.VerbTick();
        }

        public override void PostEnd()
        {
            base.PostEnd();
            verb = null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Pawn? o = otherPawn;
            Scribe_References.Look(ref o, "otherPawn");
            otherPawn = o;
        }
    }
}
