using System.Collections.Generic;

namespace SimWorld.AI
{
    /// <summary>
    /// Shoot an enemy until it stops being a threat (RimWorld: <c>RimWorld.JobDriver_AttackStatic</c>).
    /// <para/>
    /// <b>Translation — this one closes, where RimWorld's stands still.</b> RimWorld splits the work in two:
    /// <c>JobGiver_AIFightEnemy.TryFindShootingPosition</c> runs <c>CastPositionFinder</c> to pick a cell
    /// with line of sight, cover and the right range, the pawn walks there, and only then does
    /// <c>JobDriver_AttackStatic</c> take over and hold that spot. There is no
    /// <c>CastPositionFinder</c> in this port and building one is a Map-module job (it wants line of sight,
    /// cover scoring and a cell scan) — so the approach folds into the driver, exactly as
    /// <see cref="JobDriver_Hunt"/> already folds it in for the same reason: the target moves, so the
    /// position to shoot from cannot be chosen once up front. The pawn walks until its weapon reaches, then
    /// stands and fires, which is the behaviour RimWorld's two halves add up to for everything except cover
    /// use.
    /// <para/>
    /// Cover is not lost by doing it this way — <see cref="Combat.Verb_LaunchProjectile"/> already resolves
    /// real cover off the map's own grids for every shot. What is lost is *seeking* cover, which is
    /// <c>CastPositionFinder</c>'s job and is recorded here as absent.
    /// </summary>
    public sealed class JobDriver_AttackStatic : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Combat.AttackTarget(TargetIndex.A, () => AttackVerbUtility.TryGetAttackVerb(pawn));
        }
    }
}
