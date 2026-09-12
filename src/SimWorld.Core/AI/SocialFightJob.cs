using System.Collections.Generic;

using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The JobDef a social fight runs on. Its own <c>[DefOf]</c> class in its own file, per CLAUDE.md.
    /// </summary>
    [DefOf]
    public static class SocialFightJobDefOf
    {
        /// <summary>Throw fists at the other party in a social fight (RimWorld: <c>JobDefOf.SocialFight</c>).</summary>
        public static JobDef SocialFight = null!;
    }

    /// <summary>
    /// Hands a brawling pawn the job of actually hitting the person it is brawling with (RimWorld:
    /// <c>RimWorld.JobGiver_SocialFighting</c>). It sits inside the humanlike think tree's mental-state
    /// tier, above the wander that tier used to be the whole of.
    ///
    /// <para/><b>Its two null returns are the point of the class.</b> RimWorld's giver refuses when
    /// <c>!otherPawn.Spawned || otherPawn.Map != pawn.Map</c>, and a social fight in RimWorld is therefore
    /// something that can only happen between two people standing on the same map. Before this existed,
    /// <see cref="MentalState_SocialFighting"/> swung at range zero from its own tick with no map at all —
    /// see that class for the measurement. Everything else about the fight (who starts one, how long it
    /// lasts, when it stops) is unchanged and still lives on the mental state.
    /// </summary>
    public sealed class JobGiver_SocialFighting : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!(pawn.mindState.mentalStateHandler.CurState is MentalState_SocialFighting fight)) return null;

            Pawn? other = fight.otherPawn;
            if (other == null || other.Dead || other.Downed) return null;
            if (!pawn.Spawned || !other.Spawned || !ReferenceEquals(pawn.Map, other.Map)) return null;
            if (AttackVerbUtility.NaturalWeaponFor(pawn) == null) return null;

            return new Job(SocialFightJobDefOf.SocialFight, other);
        }
    }

    /// <summary>
    /// Walk up to the other party and hit them with your fists until one of you is on the floor (RimWorld:
    /// <c>JobDefOf.SocialFight</c>, driven by the same melee machinery an ordinary fight uses).
    ///
    /// <para/><b>Fists, never the weapon on your belt.</b> RimWorld draws the fight's verb from
    /// <c>pawn.verbTracker.AllVerbs</c> — a pawn's <i>natural</i> tools — and an equipped weapon's verbs live
    /// on the weapon's own tracker, not there, which is why a colonist carrying a knife still brawls
    /// bare-knuckled. So this asks <see cref="AttackVerbUtility.NaturalWeaponFor"/> directly rather than
    /// <see cref="AttackVerbUtility.TryGetMeleeVerb"/>, which would prefer the weapon —
    /// <see cref="JobDriver_AttackMelee"/> wants that preference and a social fight must not have it. The
    /// tribal band this port ships founds settlements carrying knives and bows.
    ///
    /// <para/>Reuses <see cref="Toils_Combat.AttackTarget"/> unchanged, so a scuffle approaches, reaches and
    /// swings on the verb's own cooldown exactly as a raider does, and stops on the same condition — the
    /// target is down, dead or gone (<see cref="AttackTargetsUtility.ThreatDisabled"/>). It additionally
    /// stops the moment this pawn is no longer in the fight, which is what stops a recovered pawn carrying
    /// on punching.
    /// </summary>
    public sealed class JobDriver_SocialFight : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            Toil attack = Toils_Combat.AttackTarget(TargetIndex.A, () => MakeFistsVerb(pawn));
            attack.FailOn(() => !StillFighting(pawn, job.GetTarget(TargetIndex.A).Thing as Pawn));
            yield return attack;
        }

        private static bool StillFighting(Pawn pawn, Pawn? target) =>
            target != null
            && pawn.mindState.mentalStateHandler.CurState is MentalState_SocialFighting fight
            && ReferenceEquals(fight.otherPawn, target);

        private static Verb? MakeFistsVerb(Pawn pawn)
        {
            Tool? fists = AttackVerbUtility.NaturalWeaponFor(pawn);
            return fists == null ? null : MeleeVerbUtility.MakeVerb(pawn, fists);
        }
    }
}
