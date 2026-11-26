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
    /// <b>Ported guards:</b> RimWorld's home-area check and its <c>t.Faction == pawn.Faction</c> check, so a
    /// ruin (factionless, from <c>MapGen.GenStep_Ruins</c>) is never anybody's chore, inside the home area or
    /// out. <b>Translation, what is left:</b> there is no <c>listerBuildingsRepairable</c>, so the scan is
    /// <see cref="ThingRequestGroup.BuildingArtificial"/> (category <c>Building</c>, not mineable) filtered by
    /// <see cref="IsRepairable"/>: an artificial building that uses hit points and is missing some.
    /// <para/>
    /// <b>What actually damages a building here</b> (nothing did for a while, so this is worth naming):
    /// <see cref="Combat.GenExplosion.DoExplosion"/>, reached from <see cref="Building.CompExplosive"/> — a
    /// <c>TrapIED</c> going off next to a wall; <see cref="Building.RoofCollapseUtility"/>'s blunt damage to
    /// everything under a roof that just fell in; and <c>MapGen.GenStep_Ruins</c>, which spawns every ruin
    /// wall already weathered to a fraction of its max. Ranged and melee combat do <b>not</b>: this port's
    /// <see cref="Combat.Verb.TryStartCastOn"/> takes a <see cref="Pawn"/> target and nothing else, so a
    /// raider cannot shoot a wall. <b>And now fire</b> (system: fire — <see cref="Things.Fire"/>), which is
    /// also why RimWorld's <c>t.IsBurning()</c> guard finally has a counterpart below: when this was written
    /// there was no <c>Fire</c> class in the codebase at all and nothing could burn.
    /// </summary>
    public sealed class WorkGiver_Repair : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            // Only what IsRepairable keeps, HasJobOnThing's first and side-effect-free test (RimWorld walks
            // ListerBuildingsRepairable, not every building): the survivors keep their order, so the scan takes the
            // same building without measuring and sorting every whole wall on the map first.
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (IsRepairable(buildings[i])) yield return buildings[i];
            }
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
            // RimWorld: WorkGiver_Repair's (!forced && pawn.Faction == Faction.OfPlayer &&
            // !pawn.Map.areaManager.Home[t.Position]) guard; every worker here is the settlement's own. It is
            // also what keeps builders off the weathered ruins GenStep_Ruins scatters across a map, which RimWorld
            // never repairs because a ruin has no faction: without it, half a band walked the map patching ruin
            // walls while the compound and its houses stood unfinished (the host's game at tick 46,000).
            if (!forced && !pawn.Map!.areaManager.Home[thing.Position]) return false;
            // RimWorld: t.Faction != pawn.Faction -> no job. A ruin has no faction, so nobody repairs it, home
            // area or not; a stockpile's home margin used to reach one and keep half a band on it.
            if (!ReferenceEquals(thing.Faction, pawn.Faction)) return false;
            // RimWorld: WorkGiver_Repair's own t.IsBurning() guard. Patching up a building that is still on
            // fire is work thrown away — put the fire out first, which is emergency work and will already be
            // this pawn's next job anyway (WorkGiver_FightFires).
            if (thing.IsBurning()) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(RepairJobDefOf.Repair, thing);
    }
}
