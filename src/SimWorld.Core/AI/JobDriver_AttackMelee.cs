using System.Collections.Generic;

namespace SimWorld.AI
{
    /// <summary>
    /// Walk up to an enemy and hit it until it stops being a threat (RimWorld:
    /// <c>RimWorld.JobDriver_AttackMelee</c>).
    /// <para/>
    /// Asks for a melee verb specifically (<see cref="AttackVerbUtility.TryGetMeleeVerb"/>) rather than the
    /// pawn's best attack, matching RimWorld's own driver, which goes through
    /// <c>Pawn_MeleeVerbs.TryGetMeleeVerb</c>: a pawn sent into melee swings even while carrying a bow. For
    /// an empty-handed pawn that resolves to the fists <see cref="AttackVerbUtility.NaturalWeaponFor"/>
    /// builds — which is the cornered-farmer case
    /// <see cref="CombatPostureUtility"/> exists to describe.
    /// <para/>
    /// <b>Reserves nothing.</b> Every other driver in this module reserves its target so two pawns never
    /// converge on the same work; an enemy is the opposite case — a fight several citizens gang up on is the
    /// point, and a reservation would let the first attacker lock everyone else out of it.
    /// </summary>
    public sealed class JobDriver_AttackMelee : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Combat.AttackTarget(TargetIndex.A, () => AttackVerbUtility.TryGetMeleeVerb(pawn));
        }
    }
}
