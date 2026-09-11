using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Things
{
    /// <summary>The Thing fire itself is (RimWorld: <c>ThingDefOf.Fire</c>). Bound in its own <c>[DefOf]</c>
    /// class in its own file rather than appended to a shared one — <see cref="DefOfHelper"/> binds by
    /// scanning every <c>[DefOf]</c> type, so a new class cannot collide with another lane mid-edit
    /// (CLAUDE.md: "add a file rather than edit a shared one").</summary>
    [DefOf]
    public static class FireThingDefOf
    {
        /// <summary>A burning cell or a burning Thing (system: fire — <see cref="SimWorld.Things.Fire"/>).</summary>
        public static ThingDef Fire = null!;
    }

    /// <summary>How readily a Thing catches and sustains fire (RimWorld: <c>StatDefOf.Flammability</c>).</summary>
    [DefOf]
    public static class FireStatDefOf
    {
        /// <summary>0 never burns; 1 is ordinary wood/cloth/plant matter. Read through the full stat
        /// pipeline so a Thing's <see cref="Thing.Stuff"/> can zero it (a stone wall) — see
        /// <see cref="ThingDef.BaseFlammability"/> and <see cref="Stats.StatPart_Flammability"/>.</summary>
        public static StatDef Flammability = null!;
    }

    /// <summary>Damage fire deals (RimWorld: <c>DamageDefOf.Flame</c>).</summary>
    [DefOf]
    public static class FireDamageDefOf
    {
        /// <summary>Burns, and sets what it hits alight — see <see cref="DamageWorker_Flame"/>. Distinct
        /// from the pre-existing <c>Burn</c> DamageDef, which is plain heat damage with no ignition, exactly
        /// as RimWorld ships both.</summary>
        public static DamageDef Flame = null!;
    }
}
