using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Things
{
    /// <summary>
    /// Natural rock and ore veins: the things a miner digs out (RimWorld: <c>Verse.Mineable</c>). The
    /// <c>thingClass</c> every Def under <c>BaseNaturalRock</c> now carries, so the yield lives on the thing
    /// being mined rather than in the job driver — the same shape <see cref="Building.Plant.Harvest"/> uses
    /// for crops, and for the same reason: one place spawns the yield, whoever asked for it.
    ///
    /// <para/><b>Scope.</b> RimWorld's <c>Mineable</c> also yields on <c>Kill</c> (a bomb collapsing a vein
    /// pays out, minus the miner's share). That path is deliberately not ported: every shipped mineable Def
    /// sets <c>destroyable=false</c> — natural rock is removed by mining and by nothing else, as
    /// <c>Buildings_Natural.xml</c>'s own comment says — so a <c>Kill</c> arm would be a branch no shipped
    /// content can reach. Map generation's own <c>Destroy()</c> calls (carving caves, replacing rock with a
    /// vein) pass <see cref="DestroyMode.Vanish"/> and so pay nothing, which is what you want: a map should
    /// not generate with the rubble of its own generation lying on it.
    /// </summary>
    public class Mineable : Thing
    {
        /// <summary>
        /// Mining xp for digging one cell out. Unsourced — RimWorld grants mining xp continuously while the
        /// job runs rather than in a lump at the end, and its rate is not available to check against here —
        /// so this is the same shape and the same order of magnitude as
        /// <see cref="Building.Plant.HarvestXp"/>, which made the identical call for harvesting. Pinned by a
        /// test asserting a miner's Mining skill moves at all, not by this literal.
        /// </summary>
        public const float MineXp = 40f;

        /// <summary>
        /// Removes this as a finished act of mining and drops what it holds (RimWorld:
        /// <c>Mineable.DestroyMined</c>). Position and map are read before the destroy, because destroying
        /// de-spawns first and a de-spawned Thing no longer knows where it was.
        /// </summary>
        public void DestroyMined(Pawn? miner)
        {
            Map.Map? map = Map;
            IntVec3 pos = Position;

            Destroy(DestroyMode.KillFinalize);
            TrySpawnYield(map, pos, miner);

            miner?.skills?.GetSkill(SkillDefOf.Mining)?.Learn(MineXp);
        }

        /// <summary>
        /// Spawns <c>def.mineableThing</c> in the freed cell, in the amount
        /// <see cref="MineableUtility.YieldFor"/> decides (RimWorld: <c>Mineable.TrySpawnYield</c>). A def
        /// with no yield, or a drop-chance roll that came up empty, simply leaves the cell clear — mining a
        /// rock out is worth doing for the space either way.
        /// </summary>
        private void TrySpawnYield(Map.Map? map, IntVec3 pos, Pawn? miner)
        {
            if (map == null) return;

            int count = MineableUtility.YieldFor(def, miner, Rand.Current);
            if (count <= 0) return;

            Thing stack = ThingMaker.MakeThing(def.mineableThing!);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, pos, map);
        }
    }
}
