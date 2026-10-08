using System;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;

namespace SimWorld.Things
{
    /// <summary>
    /// Natural rock and ore veins: the things a miner digs out (RimWorld: <c>RimWorld.Mineable</c>). The
    /// <c>thingClass</c> every Def under <c>BaseNaturalRock</c> carries, so the yield lives on the thing
    /// being mined rather than in the job driver — the same shape <see cref="Building.Plant.Harvest"/> uses
    /// for crops, and for the same reason: one place spawns the yield, whoever asked for it.
    ///
    /// <para/><b>Yield is earned by damage, as in RimWorld.</b> Every pick hit that lands
    /// (<see cref="AI.JobDriver_Mine"/> deals it as a <c>Mining</c> <see cref="DamageInfo"/>,
    /// <see cref="TakeDamage"/> takes the receipt) adds the fraction of the rock it took off, scaled by that
    /// miner's <c>MiningYield</c> stat, to <see cref="YieldPct"/>. Mined all the way out by one expert, a
    /// wasteable vein pays its whole <c>mineableYield</c>; by a beginner, a bit over a third of it; by two
    /// people, each is credited with exactly the part they dug. It is saved with the rock, so a vein half-dug
    /// when the game is saved is half-credited when it is loaded.
    ///
    /// <para/><b>Scope.</b> RimWorld's <c>Mineable</c> also yields on <c>Destroy(KillFinalize)</c> (a bomb
    /// collapsing a vein pays out, a fifth of it). That path is deliberately not ported: every shipped
    /// mineable Def sets <c>destroyable=false</c> — natural rock is removed by mining and by nothing else, as
    /// <c>Buildings_Natural.xml</c>'s own comment says — so a <c>Kill</c> arm would be a branch no shipped
    /// content can reach. Map generation's own <c>Destroy()</c> calls (carving caves, replacing rock with a
    /// vein) pass <see cref="DestroyMode.Vanish"/> and so pay nothing, which is what you want: a map should
    /// not generate with the rubble of its own generation lying on it.
    /// </summary>
    public class Mineable : Thing
    {
        /// <summary>
        /// How much of the vein has been credited to its miners so far, 0 to about 1 (RimWorld:
        /// <c>Mineable.yieldPct</c>): the sum over every pick hit of <c>damage ÷ MaxHitPoints × the miner's
        /// MiningYield</c>. Read once, by <see cref="DestroyMined"/>.
        /// </summary>
        private float yieldPct;

        /// <summary>What this rock has credited to its miners so far. Read-only outside: only a pick hit adds to it.</summary>
        public float YieldPct => yieldPct;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref yieldPct, "yieldPct", 0f);
        }

        /// <summary>
        /// A pick hit from a pawn is credited to the yield before it is applied (RimWorld: the
        /// <c>PreApplyDamage</c> override, where a <c>Mining</c> hit from a <c>Pawn</c> instigator on a rock
        /// whose yield is wasteable calls <see cref="Notify_TookMiningDamage"/>). This port has no
        /// <c>PreApplyDamage</c> hook, so the credit is taken at the top of <see cref="TakeDamage"/> instead.
        /// </summary>
        public override DamageResult TakeDamage(DamageInfo dinfo)
        {
            if (dinfo == null) throw new ArgumentNullException(nameof(dinfo));
            if (!Destroyed
                && def.mineableThing != null
                && def.mineableYieldWasteable
                && dinfo.Def == MiningDamageDefOf.Mining
                && dinfo.Instigator is Pawn miner)
            {
                Notify_TookMiningDamage((int)Math.Round(dinfo.Amount, MidpointRounding.AwayFromZero), miner);
            }
            return base.TakeDamage(dinfo);
        }

        /// <summary>
        /// Credits <paramref name="miner"/> with <paramref name="amount"/> hit points of this rock (RimWorld:
        /// <c>Mineable.Notify_TookMiningDamage</c>): never more than the rock has left, as a fraction of its
        /// full toughness, scaled by how much of a vein that miner recovers.
        /// </summary>
        public void Notify_TookMiningDamage(int amount, Pawn miner)
        {
            if (miner == null) throw new ArgumentNullException(nameof(miner));
            int num = Math.Min(amount, HitPoints);
            float fraction = (float)num / MaxHitPoints;
            yieldPct += fraction * miner.GetStatValue(MiningStatDefOf.MiningYield);
        }

        /// <summary>
        /// Removes this as a finished act of mining and drops what it holds (RimWorld:
        /// <c>Mineable.DestroyMined</c>). Position and map are read before the destroy, because destroying
        /// de-spawns first and a de-spawned Thing no longer knows where it was. The miner's skill is not
        /// credited here: RimWorld teaches mining continuously while the job runs, one tick at a time
        /// (<see cref="AI.JobDriver_Mine.LearnXpPerTick"/>), not in a lump at the end. <paramref name="pawn"/>
        /// is RimWorld's parameter and is kept for the call shape; its only use there is to forbid the drop
        /// for a non-colonist, and this port has no forbidden flag.
        /// </summary>
        public void DestroyMined(Pawn? pawn)
        {
            Map.Map? map = Map;
            IntVec3 pos = Position;

            Destroy(DestroyMode.KillFinalize);
            TrySpawnYield(map, pos);
        }

        /// <summary>
        /// Takes the mine mark off a cell when the rock in it leaves the map, however it left (RimWorld:
        /// <c>Building.DeSpawn</c> ends in <c>designationManager.Notify_BuildingDespawned</c>). Mined out,
        /// carved away by map generation, or refilled by a collapse — the mark was drawn over rock, and
        /// there is none. The footprint is read before the base call for the reason
        /// <see cref="DestroyMined"/> reads the position early.
        /// </summary>
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map.Map? map = Map;
            if (map == null)
            {
                base.DeSpawn(mode);
                return;
            }

            CellRect footprint = OccupiedRect();
            base.DeSpawn(mode);
            map.designationManager.Notify_BuildingDespawned(footprint);
        }

        /// <summary>
        /// Spawns <c>def.mineableThing</c> in the freed cell, in the amount
        /// <see cref="MineableUtility.YieldFromCredit"/> decides (RimWorld: <c>Mineable.TrySpawnYield</c>). A
        /// def with no yield, or a drop-chance roll that came up empty, simply leaves the cell clear — mining
        /// a rock out is worth doing for the space either way.
        /// </summary>
        private void TrySpawnYield(Map.Map? map, IntVec3 pos)
        {
            if (map == null) return;

            int count = MineableUtility.YieldFromCredit(def, yieldPct, Rand.Current);
            if (count <= 0) return;

            Thing stack = ThingMaker.MakeThing(def.mineableThing!);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, pos, map);
        }
    }
}
