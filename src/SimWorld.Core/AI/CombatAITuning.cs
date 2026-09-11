using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Tuning for the think tree's combat tier (system 9: AI — combat). RimWorld's own numbers live on
    /// <c>JobGiver_AIFightEnemy</c>'s fields and on each race's natural-weapon <c>tools</c>; neither could be
    /// read from here, so every constant below is <b>this port's own</b> and each says what it stands in for.
    /// Following CLAUDE.md, none of them is asserted literally by a test — each is pinned by a band, a trend
    /// or an ordering in <c>CombatAITests</c>. <see cref="HuntingTuning"/> is the sibling file for the hunt,
    /// and deliberately the source of the one constant the two share (<see cref="MeleeReachCells"/>).
    /// </summary>
    public static class CombatAITuning
    {
        /// <summary>
        /// How far a pawn looks for something to attack, in cells (RimWorld:
        /// <c>JobGiver_AIFightEnemy.targetAcquireRadius</c>, whose default could not be sourced from here).
        /// Comfortably beyond the longest ranged weapon this port ships (<c>Gun_AssaultRifle</c>, 27.5), so a
        /// pawn notices a shooter that is already able to hit it rather than only reacting once hit — which
        /// is the property the test pins, not this number.
        /// </summary>
        public const float TargetAcquireRadius = 40f;

        /// <summary>
        /// How close an attacker must be to swing, in cells — one cell including diagonals, RimWorld's own
        /// melee reach. Deliberately an alias of <see cref="HuntingTuning.MeleeReachCells"/> rather than a
        /// second literal: a melee <see cref="Combat.VerbProperties"/> built by
        /// <see cref="Combat.MeleeVerbUtility"/> leaves <see cref="Combat.VerbProperties.range"/> at its
        /// 90-cell ranged default, so every melee caller in this codebase has to substitute the same number,
        /// and two copies of it would be two chances to drift.
        /// </summary>
        public const float MeleeReachCells = HuntingTuning.MeleeReachCells;

        /// <summary>
        /// How long one attack job may run before it gives up, through <see cref="Job.expiryInterval"/> (the
        /// ceiling <see cref="HuntingTuning.HuntJobExpiryTicks"/> exists for, for the same reason: the target
        /// moves and the attacker re-paths after it, so without a ceiling a chase across open ground never
        /// ends). An hour rather than the hunt's half-day because a fight that has not resolved in an hour is
        /// a fight the attacker cannot win from where it stands, and re-deciding is cheap — the very next
        /// think-tree pass simply re-issues the same job if the enemy is still there.
        /// </summary>
        public const int AttackJobExpiryTicks = GenDate.TicksPerHour;

        /// <summary>
        /// Power of the natural weapon <see cref="AttackVerbUtility.NaturalWeaponFor"/> builds for an animal,
        /// per point of <see cref="Pawns.Pawn.BodySize"/>. RimWorld puts a per-species <c>tools</c> list on
        /// every animal ThingDef; <b>this port's races carry none at all</b> (see
        /// <see cref="AttackVerbUtility.NaturalWeaponFor"/> for why that is synthesised in code rather than
        /// added to the two shared race files), so one scaling rule stands in for three hand-written tool
        /// lists. Scaled by body size and not flat so the shipped animals order themselves the way they
        /// obviously should — a chicken (0.30) is a nuisance, a husky (0.86) hurts, a muffalo (1.5) is
        /// genuinely dangerous — which is the ordering the test pins.
        /// </summary>
        public const float NaturalWeaponPowerPerBodySize = 10f;
    }
}
