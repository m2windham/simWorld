using SimWorld.Defs;

namespace SimWorld.Conditions
{
    /// <summary>
    /// The drought's own <c>[DefOf]</c> binding — its own class, in its own file, rather than an addition to
    /// the shared <see cref="GameConditionDefOf"/> that lives in <c>GameConditionDef.cs</c>.
    /// <see cref="DefOfHelper"/> binds by scanning every <c>[DefOf]</c> type, so a new class here cannot
    /// collide with a lane mid-edit on that file (CLAUDE.md).
    /// </summary>
    [DefOf]
    public static class DroughtGameConditionDefOf
    {
        public static GameConditionDef Drought = null!;
    }
}
