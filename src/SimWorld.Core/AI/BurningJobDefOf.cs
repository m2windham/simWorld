using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>The JobDefs the burning tier issues (<c>BurningResponse.cs</c>), content in
    /// <c>JobDefs_Burning.xml</c>. Its own <c>[DefOf]</c> class in its own file, not fields appended to the
    /// shared <see cref="JobDefOf"/> (CLAUDE.md: "add a file rather than edit a shared one").</summary>
    [DefOf]
    public static class BurningJobDefOf
    {
        /// <summary>Stand still and put out the fire riding you (RimWorld: <c>JobDefOf.ExtinguishSelf</c>).</summary>
        public static JobDef ExtinguishSelf = null!;

        /// <summary>The short pause between run legs (RimWorld: <c>JobDefOf.Wait_Wander</c>).</summary>
        public static JobDef Wait_Wander = null!;

        /// <summary>A run leg (RimWorld: <c>JobDefOf.GotoWander</c> at Sprint urgency; see
        /// <see cref="JobGiver_RunRandom"/> for why it is its own def here).</summary>
        public static JobDef GotoWander_Run = null!;

        /// <summary>Walk onto a cell and stop there (RimWorld: <c>JobDefOf.Goto</c>, which is on-cell; see
        /// <see cref="JobDriver_GotoOnCell"/> for why this port's own <c>Goto</c> is not it).</summary>
        public static JobDef Goto_OnCell = null!;
    }
}
