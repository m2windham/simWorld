using SimWorld.Combat;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The think tree's combat tier (RimWorld: <c>RimWorld.JobGiver_AIFightEnemies</c>): finds the nearest
    /// hostile worth attacking and issues the job that goes and does it.
    /// <para/>
    /// <b>This is the tier that was missing.</b> The Combat module was complete and tested —
    /// <see cref="Verb"/>, <see cref="Verb_LaunchProjectile"/>, <see cref="Verb_MeleeAttack"/>, armour,
    /// cover, downing, death, capture — and raids generated real squads, but nothing in the AI layer ever
    /// cast a verb at an enemy, so both sides of a raid went on farming. One <c>&lt;li&gt;</c> in each think
    /// tree and this giver are what connect the two.
    /// <para/>
    /// <b>Ranged versus melee.</b> A pawn with a ranged weapon gets <c>AttackStatic</c>; anything else gets
    /// <c>AttackMelee</c>, which is also what an empty-handed pawn's fists resolve to. The decision is made
    /// here and not in the driver because the two jobs are genuinely different RimWorld jobs with different
    /// drivers, and a job that changed its mind about which one it was mid-run could not be saved coherently.
    /// <para/>
    /// <b>Who this fires for, and how far it looks,</b> is <see cref="CombatPostureUtility"/>'s whole
    /// subject — read that first; it is where the draft went.
    /// </summary>
    public class JobGiver_AIFightEnemies : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!CombatPostureUtility.CanFight(pawn)) return null;

            // Built before the target scan so an unarmed pacifist-by-content pawn — one that has nothing to
            // attack with at all, which is what NaturalWeaponFor returning null means — pays nothing for a
            // scan whose answer it could not act on.
            Verb? verb = AttackVerbUtility.TryGetAttackVerb(pawn);
            if (verb == null) return null;

            Pawn? target = FindTarget(pawn);
            if (target == null) return null;

            JobDef def = verb.verbProps.IsMeleeAttack ? CombatAIDefOf.AttackMelee : CombatAIDefOf.AttackStatic;
            return new Job(def, target) { expiryInterval = CombatAITuning.AttackJobExpiryTicks };
        }

        /// <summary>The enemy to go for; overridden by <see cref="JobGiver_Manhunter"/>, which has already
        /// decided.</summary>
        protected virtual Pawn? FindTarget(Pawn pawn) =>
            AttackTargetFinder.BestAttackTarget(pawn, CombatPostureUtility.TargetAcquireRadiusFor(pawn));
    }

    /// <summary>
    /// An animal that has turned on somebody and is coming for them (RimWorld:
    /// <c>RimWorld.JobGiver_Manhunter</c>, which subclasses <c>JobGiver_AIFightEnemies</c> there too).
    /// <para/>
    /// Sits under <see cref="ThinkNode_ConditionalAngryAtHandler"/> in <c>ThinkTrees_Animal.xml</c>, where
    /// <see cref="JobGiver_WanderAnywhere"/> used to stand alone. That was the honest placeholder the animals
    /// and hunting lanes left: both a failed tame (<see cref="TameUtility.TryTame"/>) and wounded prey
    /// (<see cref="HuntUtility.TryProvokeRevenge"/>) set
    /// <see cref="MindState.Pawn_MindState.angryAt"/>, and the tier that read it had nowhere to route the
    /// animal because no attack job existed — <see cref="HuntUtility.TryProvokeRevenge"/>'s own doc says so
    /// in as many words ("The animal does not fight back"). It does now.
    /// <para/>
    /// Targets the pawn it is angry at and nobody else, rather than re-scanning: the grudge already names its
    /// target (see <see cref="AttackTargetsUtility.HostileTo"/> on why this port's <c>angryAt</c> stays
    /// personal where RimWorld's manhunter is hostile to everyone). Contributes nothing once that target is
    /// gone or already down, so the tier falls through to the wander node still sitting beside it and the
    /// animal calms down on its own when <see cref="MindState.Pawn_MindState.angryUntilTick"/> passes.
    /// </summary>
    public sealed class JobGiver_Manhunter : JobGiver_AIFightEnemies
    {
        protected override Pawn? FindTarget(Pawn pawn)
        {
            Pawn? grudge = pawn.mindState?.angryAt;
            if (grudge == null || !AttackTargetsUtility.IsAngryAt(pawn, grudge)) return null;
            if (AttackTargetsUtility.ThreatDisabled(grudge)) return null;
            if (!ReferenceEquals(grudge.Map, pawn.Map)) return null;
            return Reachability.CanReach(pawn, grudge, PathEndMode.Touch) ? grudge : null;
        }
    }
}
