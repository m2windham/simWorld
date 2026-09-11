using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>The JobDef <see cref="WorkGiver_FightFires"/> issues (RimWorld: <c>JobDefOf.BeatFire</c>),
    /// bound by defName in its own <c>[DefOf]</c> class in its own file rather than appended to the shared
    /// <see cref="JobDefOf"/> — the same reason <see cref="RepairJobDefOf"/> already is (CLAUDE.md).</summary>
    [DefOf]
    public static class FireJobDefOf
    {
        /// <summary>Beats one fire down until it goes out (system: fire — <see cref="WorkGiver_FightFires"/>/
        /// <see cref="JobDriver_BeatFire"/>).</summary>
        public static JobDef BeatFire = null!;
    }
}
