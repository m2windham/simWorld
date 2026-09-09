using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Combat
{
    /// <summary>What a melee swing did (RimWorld: the outcome of <c>Verb_MeleeAttack.TryCastShot</c>).</summary>
    public enum MeleeAttackOutcome
    {
        Hit,
        Dodged,
        Missed,
    }

    /// <summary>Result of one melee swing.</summary>
    public sealed class MeleeAttackResult
    {
        public MeleeAttackOutcome Outcome { get; }
        public BodyPartRecord? HitPart { get; }
        public float DamageAmount { get; }
        public DamageResult? DamageResult { get; }

        public MeleeAttackResult(MeleeAttackOutcome outcome, BodyPartRecord? hitPart = null, float damageAmount = 0f, DamageResult? damageResult = null)
        {
            Outcome = outcome;
            HitPart = hitPart;
            DamageAmount = damageAmount;
            DamageResult = damageResult;
        }
    }

    /// <summary>
    /// An unarmed strike or melee weapon swing (RimWorld: <c>Verse.Verb_MeleeAttack</c>). First the attacker's
    /// <see cref="CombatStats.MeleeHitChanceFor"/> is rolled; on a hit, the target dodges by
    /// <see cref="CombatStats.MeleeDodgeChanceFor"/> unless it is down, asleep or dead; otherwise the tool's
    /// damage lands on a random part chosen by coverage, same as any other hit.
    /// </summary>
    public class Verb_MeleeAttack : Verb
    {
        public readonly Tool? tool;

        public MeleeAttackResult? LastResult { get; private set; }

        public Verb_MeleeAttack(Pawn caster, VerbProperties verbProps, Tool? tool = null) : base(caster, verbProps)
        {
            this.tool = tool;
        }

        /// <summary>This module's own <see cref="MeleeVerbUtility.MakeVerb"/> is the only way to construct a <see cref="Verb_MeleeAttack"/>, and it always passes a <see cref="Pawn"/> — nothing melees but pawns.</summary>
        private Pawn CasterPawn => (Pawn)caster;

        public float GetHitChance() => CombatStats.MeleeHitChanceFor(CasterPawn);

        protected override void TryCastShot(Pawn target, float distance)
        {
            if (!Rand.Chance(GetHitChance()))
            {
                LastResult = new MeleeAttackResult(MeleeAttackOutcome.Missed);
                return;
            }

            bool canDodge = !target.Downed && !target.Asleep && !target.Dead;
            if (canDodge && Rand.Chance(CombatStats.MeleeDodgeChanceFor(target)))
            {
                LastResult = new MeleeAttackResult(MeleeAttackOutcome.Dodged);
                return;
            }

            DamageDef damageDef = verbProps.meleeDamageDef ?? DamageDefOf.Blunt;
            BodyPartRecord? part = target.health.hediffSet.GetRandomNotMissingPart(damageDef, BodyPartHeight.Undefined, BodyPartDepth.Undefined, Rand.Current);
            float amount = verbProps.AdjustedMeleeDamageAmount();
            float armorPenetration = verbProps.AdjustedArmorPenetration;
            DamageResult damageResult = damageDef.Worker.Apply(new DamageInfo(damageDef, amount, armorPenetration, caster, part), target);
            LastResult = new MeleeAttackResult(MeleeAttackOutcome.Hit, part, amount, damageResult);
        }
    }
}
