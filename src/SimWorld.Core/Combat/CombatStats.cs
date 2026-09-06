using SimWorld.Pawns;

namespace SimWorld.Combat
{
    /// <summary>
    /// Reads a pawn's combat-relevant skill levels (RimWorld: <c>Verse.Pawn_SkillsTracker</c>). The Skills
    /// module isn't ported yet; tests and content read through <see cref="CombatStats.Skills"/> so it can be
    /// swapped for a real one later without touching this module.
    /// </summary>
    public interface ICombatSkills
    {
        int ShootingLevel(Pawn pawn);
        int MeleeLevel(Pawn pawn);
    }

    /// <summary>Level 4 shooting and melee for every pawn: RimWorld's untrained-colonist baseline, used until the Skills module lands.</summary>
    public sealed class DefaultCombatSkills : ICombatSkills
    {
        public int ShootingLevel(Pawn pawn) => 4;
        public int MeleeLevel(Pawn pawn) => 4;
    }

    /// <summary>
    /// Skill-driven combat curves computed directly from skill level (RimWorld: the <c>ShootingAccuracyPawn</c>,
    /// <c>MeleeHitChance</c> and <c>MeleeDodgeChance</c> StatDefs' skill-need curves), since the general Stats
    /// resolution pipeline hasn't landed yet. The matching StatDefs exist in content as data/documentation
    /// only; these curves are what code actually reads.
    /// </summary>
    public static class CombatStats
    {
        public static ICombatSkills Skills = new DefaultCombatSkills();

        private static readonly SimpleCurve ShootingAccuracyCurve = Curve((0f, 0.89f), (4f, 0.94f), (8f, 0.96f), (12f, 0.975f), (16f, 0.985f), (20f, 0.99f));
        private static readonly SimpleCurve MeleeHitChanceCurve = Curve((0f, 0.5f), (5f, 0.62f), (10f, 0.78f), (15f, 0.9f), (20f, 0.98f));
        private static readonly SimpleCurve MeleeDodgeChanceCurve = Curve((0f, 0f), (5f, 0.05f), (10f, 0.1f), (15f, 0.2f), (20f, 0.3f));

        public static float ShootingAccuracyPawn(int shootingLevel) => ShootingAccuracyCurve.Evaluate(shootingLevel);
        public static float MeleeHitChance(int meleeLevel) => MeleeHitChanceCurve.Evaluate(meleeLevel);
        public static float MeleeDodgeChance(int meleeLevel) => MeleeDodgeChanceCurve.Evaluate(meleeLevel);

        public static float ShootingAccuracyFor(Pawn pawn) => ShootingAccuracyPawn(Skills.ShootingLevel(pawn));
        public static float MeleeHitChanceFor(Pawn pawn) => MeleeHitChance(Skills.MeleeLevel(pawn));
        public static float MeleeDodgeChanceFor(Pawn pawn) => MeleeDodgeChance(Skills.MeleeLevel(pawn));

        private static SimpleCurve Curve(params (float x, float y)[] points)
        {
            var curve = new SimpleCurve();
            foreach ((float x, float y) in points) curve.Add(x, y);
            return curve;
        }
    }
}
