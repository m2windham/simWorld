using SimWorld.Defs;

namespace SimWorld.World
{
    /// <summary>
    /// A resource or landmark a scouting party would notice on the land: fresh water, arable soil, clay,
    /// flint, stone, ore, salt, timber, game, a ford, defensible ground (spec §5b.2). RimWorld has no direct
    /// equivalent — deposits are wholly this port's own translation for the region/site founding design.
    /// Carries no tuning of its own: every threshold that decides where and how strongly a deposit appears
    /// lives in <see cref="DepositTuning"/>, read by <see cref="Gen.WorldGenStep_Deposits"/>.
    /// </summary>
    public class DepositDef : Def
    {
    }

    /// <summary>One deposit present on a tile, with a magnitude in [0,1] (RimWorld has no equivalent; see <see cref="DepositDef"/>).</summary>
    public struct TileDeposit
    {
        public DepositDef def;
        public float magnitude;

        public TileDeposit(DepositDef def, float magnitude)
        {
            this.def = def;
            this.magnitude = magnitude;
        }
    }
}
