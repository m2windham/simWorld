using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a damaged, reachable, unreserved artificial building and sends a pawn to patch it back up
    /// (RimWorld: <c>RimWorld.WorkGiver_Repair</c>). <c>Repair</c>'s <c>giverClass</c> in
    /// <c>WorkGivers.xml</c>; before this it fell back to <see cref="WorkGiver_Pending"/>, so the whole
    /// Construction work type could only ever build, never maintain.
    /// <para/>
    /// <b>Translation — "the player faction's damaged buildings" becomes "any damaged artificial building
    /// on this map".</b> RimWorld scans <c>Map.listerBuildingsRepairable.RepairableBuildings(pawn.Faction)</c>
    /// and then re-checks <c>t.Faction == pawn.Faction</c>, so a colonist never repairs a raider's turret.
    /// Neither half of that is expressible here. This port's <see cref="Thing"/> carries <b>no faction field
    /// at all</b> — only <see cref="Pawn.faction"/> does, a gap <see cref="Building.CompTurretGun.Faction"/>
    /// already had to work around by giving the comp its own — so a building simply has no owner to compare
    /// against, and there is no <c>listerBuildingsRepairable</c> to consult either. What the map does carry
    /// is <see cref="ThingRequestGroup.BuildingArtificial"/>: category <c>Building</c>, not
    /// <see cref="ThingDef.mineable"/>. That is this codebase's own standing line between natural rock and
    /// something somebody built, and it lands in the same place as RimWorld's <c>building.repairable</c>
    /// (false on mineable rock, true on constructed buildings) — so the predicate becomes
    /// <see cref="IsRepairable"/>: an artificial building that uses hit points and is missing some.
    /// The same shape <see cref="WorkGiver_Miner"/> used when it translated mining's <c>Designation.Mine</c>
    /// away and mined any reachable mineable edifice instead.
    /// <para/>
    /// <b>What actually damages a building here</b> (nothing did for a while, so this is worth naming):
    /// <see cref="Combat.GenExplosion.DoExplosion"/>, reached from <see cref="Building.CompExplosive"/> — a
    /// <c>TrapIED</c> going off next to a wall; <see cref="Building.RoofCollapseUtility"/>'s blunt damage to
    /// everything under a roof that just fell in; and <c>MapGen.GenStep_Ruins</c>, which spawns every ruin
    /// wall already weathered to a fraction of its max. Ranged and melee combat do <b>not</b>: this port's
    /// <see cref="Combat.Verb.TryStartCastOn"/> takes a <see cref="Pawn"/> target and nothing else, so a
    /// raider cannot shoot a wall. There is no fire in this codebase at all, which is why RimWorld's own
    /// <c>t.IsBurning()</c> guard has no counterpart below.
    /// </summary>
    public sealed class WorkGiver_Repair : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)) yield return t;
        }

        /// <summary>
        /// This port's stand-in for RimWorld's <c>ListerBuildingsRepairable</c> membership test — see the
        /// class doc for why faction plays no part. A destroyed or de-spawned thing is nothing to walk to; a
        /// <c>useHitPoints=false</c> thing can never be damaged in the first place, so it can never be
        /// short of whole; and <c>HitPoints &lt;= 0</c> would mean something already dead that
        /// <see cref="Thing.TakeDamage"/> only leaves behind when <c>destroyable</c> is false (natural rock,
        /// which <see cref="ThingRequestGroup.BuildingArtificial"/> already excludes) — not a repair job.
        /// </summary>
        public static bool IsRepairable(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned) return false;
            if (thing.def.category != ThingCategory.Building || thing.def.mineable) return false;
            if (!thing.def.useHitPoints) return false;
            return thing.HitPoints > 0 && thing.HitPoints < thing.MaxHitPoints;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!IsRepairable(thing)) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(RepairJobDefOf.Repair, thing);
    }
}
