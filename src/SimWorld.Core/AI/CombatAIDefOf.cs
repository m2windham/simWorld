using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.God;

namespace SimWorld.AI
{
    /// <summary>
    /// Defs the think tree's combat tier binds by name (system 9: AI — combat). Its own class rather than
    /// extra fields on <see cref="JobDefOf"/> or <c>Combat.ToolCapacityDefOf</c>: <c>DefOfHelper</c> binds by
    /// scanning every <c>[DefOf]</c> type, so a module's bindings never need an edit to a file another lane
    /// is also editing (CLAUDE.md — "add a file rather than edit a shared one"). <see cref="HuntingDefOf"/>
    /// and <c>Building.BuildingJobDefOf</c> already do exactly this.
    /// </summary>
    [DefOf]
    public static class CombatAIDefOf
    {
        /// <summary>Close to melee reach and swing until the target stops being a threat (RimWorld:
        /// <c>JobDefOf.AttackMelee</c>; see <see cref="JobDriver_AttackMelee"/>).</summary>
        public static JobDef AttackMelee = null!;

        /// <summary>Close to weapon range and shoot until the target stops being a threat (RimWorld:
        /// <c>JobDefOf.AttackStatic</c>; see <see cref="JobDriver_AttackStatic"/> for why this port's version
        /// closes at all, where RimWorld's stands still).</summary>
        public static JobDef AttackStatic = null!;

        /// <summary>The capacity an animal's synthesised natural weapon carries, so it resolves through the
        /// shipped <c>Bite</c> <see cref="ManeuverDef"/> like any weapon tool would (see
        /// <see cref="AttackVerbUtility.NaturalWeaponFor"/>). <c>Combat.ToolCapacityDefOf</c> binds only the
        /// three weapon capacities and is not this lane's file to extend.</summary>
        public static ToolCapacityDef Bite = null!;

        /// <summary>
        /// The standing civilization-scale order that puts unarmed citizens into the fight — this port's
        /// replacement for RimWorld's draft, since a god issues edicts rather than drafting individuals. See
        /// <see cref="CombatPostureUtility"/> for exactly what it changes and what it does not.
        /// </summary>
        public static EdictDef TakeUpArms = null!;
    }
}
