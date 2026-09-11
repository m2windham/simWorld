using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Sends a citizen to beat out the nearest fire (RimWorld: <c>RimWorld.WorkGiver_FightFires</c>). The
    /// worker behind the <c>FightFires</c> WorkGiverDef, which has sat in content with no worker and nothing
    /// it could target since there was no <c>Fire</c> class at all.
    /// <para/>
    /// <b>Emergency ordering is not this class's doing and needs nothing from it.</b> <c>FightFires</c>
    /// already carries <c>emergency=true</c>, <c>Pawn_WorkSettings</c> already splits the giver list on that
    /// flag, and <c>JobGiver_Work</c> already runs the emergency list to exhaustion before the normal one —
    /// so a burning settlement drops hauling and cooking on its own. Firefighter also has the highest
    /// <c>naturalPriority</c> of any work type in content (1400), which orders it first <i>within</i> the
    /// emergency list, ahead of emergency doctoring.
    /// <para/>
    /// <b>Translation — no home-area gate.</b> RimWorld refuses fires outside <c>Map.areaManager.Home</c>,
    /// so colonists do not run across the map into a forest fire. This port has the Home area
    /// (<c>Building.AreaManager.Home</c>) but <i>nothing populates it</i> — it is empty on every map, by that
    /// module's own admission ("nothing yet reads it"). Porting the gate 1:1 would mean no fire is ever
    /// firefighting work, which is precisely the dormant-feature trap this codebase has been bitten by
    /// before. The gate is therefore left out until something fills the home area, and every fire on the map
    /// is work. The line to restore is one <c>if</c> in <see cref="HasJobOnThing"/>.
    /// <para/>
    /// <b>Kept:</b> RimWorld's rule that a fire riding a <i>hostile</i> pawn is not your problem.
    /// </summary>
    public sealed class WorkGiver_FightFires : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || FireUtility.AllFires(pawn.Map).Count == 0;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return System.Array.Empty<Thing>();
            return FireUtility.AllFires(map);
        }

        /// <summary>Whether <paramref name="pawn"/> should put <paramref name="thing"/> out. Public and
        /// static so the predicate is testable without driving a whole think tree, the shape
        /// <c>WorkGiver_Repair.IsRepairable</c> already uses.</summary>
        public static bool IsFireToFight(Pawn pawn, Thing thing)
        {
            if (!(thing is Fire fire)) return false;
            if (fire.Destroyed || !fire.Spawned) return false;
            if (fire.Map != pawn.Map) return false;

            // A fire on someone else's fighter is their problem. Own-faction pawns (and animals), and fires
            // on cells or buildings, are all fair game.
            if (fire.parent is Pawn burning
                && !ReferenceEquals(burning, pawn)
                && pawn.faction != null
                && burning.faction != null
                && pawn.faction.HostileTo(burning.faction))
            {
                return false;
            }
            return true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!IsFireToFight(pawn, thing)) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(FireJobDefOf.BeatFire, thing);
    }
}
