using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a marked rock, walks adjacent to it, swings a pick at it until its hit points run out, and
    /// mines it out (RimWorld: <c>RimWorld.JobDriver_Mine</c>).
    ///
    /// <para/><b>The dig is the rock's hit points, not a timer.</b> This used to wait a flat 300 ticks and
    /// then remove the rock, so a granite wall and a thin vein cost the same, a master miner no less than a
    /// beginner, and the 12,985 limestone cells of a seed-777 mountain went in six days. Now, as in RimWorld:
    /// every <c>round(100 ÷ MiningSpeed)</c> ticks the miner lands one pick hit
    /// (<see cref="BaseTicksBetweenPickHits"/>, the <c>MiningSpeed</c> stat being skill-driven), a hit deals
    /// 80 damage to natural rock and 40 to anything else, and the last hit — the one that finds no more
    /// than a hit's worth of hit points left — mines the rock out through <see cref="Mineable.DestroyMined"/>,
    /// which drops the chunk or the ore. A level-20 miner is about twice a level-0 miner's speed, and
    /// granite takes about three times a vein's hits to get through; both fall out of the formula rather than
    /// being written down anywhere.
    ///
    /// <para/><b>Experience is continuous.</b> RimWorld teaches mining one tick at a time, for as long as the
    /// pick is swinging (<see cref="LearnXpPerTick"/>), not in a lump when the rock gives way — so a miner
    /// who is interrupted still learned for the time they spent.
    ///
    /// <para/><b>The job needs a mark.</b> <see cref="FailOnMissingMineMark"/> ends it the moment the cell
    /// stops carrying a <see cref="DesignationDefOf.Mine"/> designation — someone cancelled it, or another
    /// miner finished — exactly as RimWorld's <c>FailOnCellMissingDesignation</c> does. The one exception is
    /// RimWorld's <c>Job.ignoreDesignations</c> made to fit this port: a <see cref="Job.playerForced"/> job
    /// (<c>MapCommands.OrderJob</c> — "you, dig that rock") carries the player's choice of rock in the order
    /// itself. RimWorld's own floating-menu order does not skip the mark, but RimWorld has no order that
    /// names a rock to a person without going through a designator first; this port does.
    ///
    /// <para/><b>The roof guard is asked at every hit.</b> <see cref="Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved"/>
    /// is this port's translation of the judgement a player makes while choosing where to dig (see that
    /// method), and it is asked when <c>WorkGiver_Miner</c> hands the job out and again here. It used to be
    /// asked once, at the end, because the dig was one instant; a dig is hundreds of ticks now, and a
    /// neighbour taken in the meantime is all it takes to leave this cell holding a ceiling up, so asking at
    /// the end would spend the whole dig and then refuse. Asked per hit — about once every hundred ticks per
    /// miner — a stale answer ends the job within a hit of going stale. Refusing simply ends the job with
    /// the rock still standing and still marked; the giver then declines it too, so the miner picks something
    /// else and there is no loop.
    ///
    /// <para/><b>Not ported</b> because nothing here carries them: the mining effecter and progress bar
    /// (the host draws from state), <c>mineStrikeManager.CheckStruckOre</c> (ore revealed by digging — this
    /// port has no fog), <c>records.CellsMined</c> and the <c>MinedValuable</c>/<c>CaravanRemoteMining</c>
    /// tales (no records or tale system), the forbid-everything-a-non-colonist-drops rule, and RimWorld's
    /// 0.6 floor on an NPC's mining speed (no NPC mines in this port). <c>ticksToPickHit</c> is also not
    /// saved: nothing in this port's drivers is (see <see cref="JobDriver"/>), so a miner loaded mid-swing
    /// simply starts the next swing over, a delay of at most <see cref="BaseTicksBetweenPickHits"/> ÷ speed
    /// ticks. The rock's own hit points, and the yield its miners have earned, are saved with the rock.
    /// </summary>
    public sealed class JobDriver_Mine : JobDriver
    {
        /// <summary>Ticks between pick hits at a <c>MiningSpeed</c> of 1 (RimWorld: <c>JobDriver_Mine.BaseTicksBetweenPickHits</c>).</summary>
        public const int BaseTicksBetweenPickHits = 100;

        /// <summary>Damage one pick hit does to natural rock (RimWorld: <c>BaseDamagePerPickHit_NaturalRock</c>).</summary>
        public const int BaseDamagePerPickHit_NaturalRock = 80;

        /// <summary>Damage one pick hit does to anything that is not natural rock — an ore vein (RimWorld:
        /// <c>BaseDamagePerPickHit_NotNaturalRock</c>).</summary>
        public const int BaseDamagePerPickHit_NotNaturalRock = 40;

        /// <summary>Mining experience a miner earns per tick of swinging, learned non-directly like all work
        /// (RimWorld: the literal <c>0.07f</c> in the mine toil's tick action).</summary>
        public const float LearnXpPerTick = 0.07f;

        private int ticksToPickHit = -1000;

        /// <summary>The rock being dug: the job's Thing target, or the first mineable in its cell when the
        /// job was handed a bare cell (an order that named a cell, not a rock).</summary>
        private Thing? MineTarget
        {
            get
            {
                LocalTargetInfo target = job.GetTarget(TargetIndex.A);
                if (target.HasThing) return target.Thing;
                Map.Map? map = pawn.Map;
                return map == null ? null : MineableUtility.GetFirstMineable(target.Cell, map);
            }
        }

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            Toil reserve = Toils_Reserve.Reserve(TargetIndex.A);
            yield return FailOnMissingMineMark(reserve);

            Toil gotoRock = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return FailOnMissingMineMark(gotoRock);

            Toil mine = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            mine.tickAction = () => MineTick();
            mine.FailOn(() =>
            {
                Thing? rock = MineTarget;
                return rock == null || rock.Destroyed || !rock.Spawned;
            });
            // RimWorld's FailOnCannotTouch: still standing next to it. Adjacent (eight-way) is exactly what
            // reaching a rock with PathEndMode.Touch means, and checking it directly is a pair of
            // subtractions where a reachability query would be a region search on every tick.
            mine.FailOn(() =>
            {
                Thing? rock = MineTarget;
                if (rock == null) return true;
                IntVec3 d = rock.Position - pawn.Position;
                return Math.Abs(d.x) > 1 || Math.Abs(d.z) > 1;
            });
            yield return FailOnMissingMineMark(mine);
        }

        /// <summary>
        /// RimWorld's <c>FailOnCellMissingDesignation(TargetIndex.A, DesignationDefOf.Mine)</c>: the job is
        /// incompletable once its cell carries no mine mark — unless it is a player's direct order for this
        /// rock (see the class doc).
        /// </summary>
        private Toil FailOnMissingMineMark(Toil toil) => toil.FailOn(() =>
        {
            if (job.playerForced) return false;
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            return map.designationManager.DesignationAt(job.GetTarget(TargetIndex.A).Cell, DesignationDefOf.Mine) == null;
        });

        /// <summary>One tick of swinging: learn, count down to the next hit, land it.</summary>
        private void MineTick()
        {
            Thing? rock = MineTarget;
            if (rock == null || rock.Destroyed) return;

            if (ticksToPickHit < -100) ResetTicksToPickHit();

            pawn.skills?.Learn(SkillDefOf.Mining, LearnXpPerTick, false);

            ticksToPickHit--;
            if (ticksToPickHit > 0) return;

            // Asked at every hit; see the class doc. Ending here leaves the rock exactly as damaged as it is.
            if (Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            int damage = rock.def.isNaturalRock ? BaseDamagePerPickHit_NaturalRock : BaseDamagePerPickHit_NotNaturalRock;
            Mineable? mineable = rock as Mineable;
            if (rock.HitPoints > damage)
            {
                rock.TakeDamage(new DamageInfo(MiningDamageDefOf.Mining, damage, instigator: pawn));
            }
            else if (mineable != null)
            {
                // The last hit: whatever is left is the damage it does, and the miner is credited for exactly
                // that much of the rock.
                mineable.Notify_TookMiningDamage(rock.HitPoints, pawn);
                mineable.HitPoints = 0;
                mineable.DestroyMined(pawn);
            }
            else
            {
                // A mineable Def whose thingClass is not Mineable: no yield to give, and ordinary damage
                // cannot destroy it (natural rock is destroyable=false). Content cannot reach this today — a
                // test asserts every mineable Def in content is a Mineable — and it stays rather than looping
                // forever, because a job driver is the wrong place to hang on a content bug.
                rock.Destroy(DestroyMode.KillFinalize);
            }

            if (rock.Destroyed)
            {
                ReadyForNextToil();
                return;
            }
            ResetTicksToPickHit();
        }

        /// <summary>The wait to the next hit: <c>round(100 ÷ MiningSpeed)</c> ticks (RimWorld:
        /// <c>JobDriver_Mine.ResetTicksToPickHit</c>). The stat is floored by its own <c>minValue</c>, so this
        /// cannot divide by zero.</summary>
        private void ResetTicksToPickHit() => ticksToPickHit = TicksBetweenPickHits(pawn);

        /// <summary>Ticks between this miner's pick hits right now — the quantity that falls as skill rises.</summary>
        public static int TicksBetweenPickHits(Pawn miner) =>
            (int)Math.Round(BaseTicksBetweenPickHits / miner.GetStatValue(StatDefOf.MiningSpeed));
    }
}
