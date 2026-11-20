using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// A work giver that works the player's own designations and nothing else — RimWorld's designation-driven
    /// givers (<c>WorkGiver_Miner</c>, <c>WorkGiver_PlantsCut</c>, <c>WorkGiver_HunterHunt</c>,
    /// <c>WorkGiver_Tame</c>, <c>WorkGiver_Slaughter</c>, <c>WorkGiver_Deconstruct</c>,
    /// <c>WorkGiver_HaulUrgently</c>), which read <c>designationManager.SpawnedDesignationsOfDef</c>.
    ///
    /// <para/><b>Player first, initiative as the fallback.</b> Each sits in its work type above the
    /// initiative-driven giver it shadows (<c>WorkGivers_PlayerDesignated.xml</c>, a higher
    /// <c>priorityInType</c>), and <see cref="Pawn_WorkSettings"/> orders givers by work priority, then the work
    /// type's natural priority, then <c>priorityInType</c>. So a pawn doing that work type tries the player's
    /// marks first; with none it can reach, the scan moves on to the next giver in the same type — the
    /// settlement's own (<see cref="WorkGiver_Miner"/>, <see cref="WorkGiver_PlantsCut"/>,
    /// <see cref="WorkGiver_Hunt"/>, <see cref="WorkGiver_TameAnimals"/>) — unchanged. With no player marks
    /// on the map <see cref="ShouldSkip"/> says so before anything is scanned, which is what keeps play with
    /// zero player designations identical to play before these givers existed.
    ///
    /// <para/>Work the player marked skips the settlement's own judgement about it — whether it wants meat,
    /// whether it can afford livestock, whether the prey is big — because that judgement is what the player
    /// replaced. Physical limits stay: reachability, reservations, a ranged weapon to hunt with.
    /// </summary>
    public abstract class WorkGiver_PlayerDesignated : WorkGiver_Scanner
    {
        /// <summary>The designation defs this giver works.</summary>
        protected abstract IReadOnlyList<DesignationDef> Defs { get; }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            IReadOnlyList<DesignationDef> defs = Defs;
            for (int i = 0; i < defs.Count; i++)
            {
                if (map.designationManager.AnyPlayerDesignationOfDef(defs[i])) return false;
            }
            return true;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<DesignationDef> defs = Defs;
            for (int d = 0; d < defs.Count; d++)
            {
                IReadOnlyList<Designation> list = map.designationManager.SpawnedDesignationsOfDef(defs[d]);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].source != DesignationSource.Player) continue;
                    Thing? t = TargetThing(list[i], map);
                    if (t != null) yield return t;
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.Destroyed || !thing.Spawned) return false;
            DesignationDef? def = PlayerDesignationOn(pawn.Map!, thing);
            if (def == null) return false;
            if (!CanWork(pawn, thing, def)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            return Reachability.CanReach(pawn, thing, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            DesignationDef? def = PlayerDesignationOn(pawn.Map!, thing);
            return def == null ? null : MakeJob(pawn, thing, def);
        }

        /// <summary>The thing a designation asks work on; a cell designation overrides this.</summary>
        protected virtual Thing? TargetThing(Designation des, Map.Map map) =>
            des.target.Thing is Thing t && t.Spawned && t.Map == map ? t : null;

        /// <summary>The player's mark on <paramref name="thing"/>, among <see cref="Defs"/>, if any.</summary>
        protected virtual DesignationDef? PlayerDesignationOn(Map.Map map, Thing thing)
        {
            IReadOnlyList<DesignationDef> defs = Defs;
            for (int i = 0; i < defs.Count; i++)
            {
                Designation? des = map.designationManager.DesignationOn(thing, defs[i]);
                if (des != null && des.source == DesignationSource.Player) return defs[i];
            }
            return null;
        }

        /// <summary>The giver's own physical gate beyond reservation and reachability.</summary>
        protected virtual bool CanWork(Pawn pawn, Thing thing, DesignationDef def) => true;

        protected abstract Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def);
    }

    /// <summary>Mines player-designated rock (RimWorld: <c>WorkGiver_Miner</c>, read over the player's Mine
    /// marks only; <see cref="WorkGiver_Miner"/> keeps the settlement's). No roof gate: RimWorld lets the player
    /// dig out a roof's support and bring it down, and so does this — the settlement's own giver keeps its gate
    /// because there nobody chose the risk.</summary>
    public sealed class WorkGiver_MinerDesignated : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        protected override IReadOnlyList<DesignationDef> Defs => defs ??= new[] { DesignationDefOf.Mine };

        protected override Thing? TargetThing(Designation des, Map.Map map)
        {
            IntVec3 cell = des.target.Cell;
            return WorkGiver_Miner.MayBeAccessible(cell, map) ? WorkGiver_Miner.FirstMineable(cell, map) : null;
        }

        protected override DesignationDef? PlayerDesignationOn(Map.Map map, Thing thing)
        {
            if (!thing.def.mineable) return null;
            Designation? des = map.designationManager.DesignationAt(thing.Position, DesignationDefOf.Mine);
            return des != null && des.source == DesignationSource.Player ? DesignationDefOf.Mine : null;
        }

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def) => new Job(JobDefOf.Mine, thing);
    }

    /// <summary>Cuts or harvests player-designated plants (RimWorld: <c>WorkGiver_PlantsCut</c>, which reads
    /// both CutPlant and HarvestPlant). A harvest mark on a plant that is not ready waits for it.</summary>
    public sealed class WorkGiver_PlantsCutDesignated : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        protected override IReadOnlyList<DesignationDef> Defs =>
            defs ??= new[] { PlayerDesignationDefOf.CutPlant, PlayerDesignationDefOf.HarvestPlant };

        protected override bool CanWork(Pawn pawn, Thing thing, DesignationDef def) =>
            thing is Plant plant && (def != PlayerDesignationDefOf.HarvestPlant || plant.HarvestableNow);

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def) =>
            new Job(def == PlayerDesignationDefOf.HarvestPlant ? BuildingJobDefOf.Harvest : PlantCuttingJobDefOf.CutPlant, thing);
    }

    /// <summary>Hunts player-designated animals (RimWorld: <c>WorkGiver_HunterHunt</c>): a ranged weapon and
    /// violence are still required, as in RimWorld. The settlement's size limit on prey
    /// (<see cref="HuntUtility.IsSafeToHunt"/>) is not applied — RimWorld leaves that to the player, and here the
    /// player decided — but an animal already angry at somebody is still passed over, for the provoke/abort loop
    /// <see cref="HuntUtility.IsSafeToHunt"/>'s doc describes.</summary>
    public sealed class WorkGiver_HunterHunt : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        protected override IReadOnlyList<DesignationDef> Defs => defs ??= new[] { PlayerDesignationDefOf.Hunt };

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            base.ShouldSkip(pawn, forced) || !HuntUtility.HasHuntingWeapon(pawn) || pawn.WorkTagIsDisabled(WorkTags.Violent);

        protected override bool CanWork(Pawn pawn, Thing thing, DesignationDef def) =>
            thing is Pawn animal && !ReferenceEquals(animal, pawn) && HuntUtility.IsHuntableAnimal(animal) && !HuntUtility.IsAngry(animal);

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def) => new Job(HuntingDefOf.Hunt, thing);
    }

    /// <summary>Tames player-designated animals (RimWorld: <c>WorkGiver_Tame</c>), with
    /// <see cref="WorkGiver_TameAnimals"/>'s own refusal of an animal that has turned on somebody. Ignores
    /// <see cref="TamingInitiative.WantsLivestock"/>: the player chose to keep this one.</summary>
    public sealed class WorkGiver_Tamer : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        protected override IReadOnlyList<DesignationDef> Defs => defs ??= new[] { PlayerDesignationDefOf.Tame };

        protected override bool CanWork(Pawn pawn, Thing thing, DesignationDef def) =>
            thing is Pawn animal && TamingInitiative.IsTameable(animal)
            && !HuntUtility.IsAngry(animal) && !AttackTargetsUtility.IsManhunting(animal);

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def) => new Job(JobDefOf.Tame, thing);
    }

    /// <summary>Slaughters player-designated livestock (RimWorld: <c>WorkGiver_Slaughter</c>, Handling): not by a
    /// pawn who will not do violence, and not an animal in a rage.</summary>
    public sealed class WorkGiver_Slaughter : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        protected override IReadOnlyList<DesignationDef> Defs => defs ??= new[] { PlayerDesignationDefOf.Slaughter };

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            base.ShouldSkip(pawn, forced) || pawn.WorkTagIsDisabled(WorkTags.Violent);

        protected override bool CanWork(Pawn pawn, Thing thing, DesignationDef def) =>
            thing is Pawn animal && !ReferenceEquals(animal, pawn) && !animal.Dead
            && !HuntUtility.IsAngry(animal) && !AttackTargetsUtility.IsManhunting(animal);

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def) =>
            new Job(DesignationJobDefOf.Slaughter, thing);
    }

    /// <summary>Deconstructs player-designated buildings (RimWorld: <c>WorkGiver_Deconstruct</c>, Construction).</summary>
    public sealed class WorkGiver_Deconstruct : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        protected override IReadOnlyList<DesignationDef> Defs => defs ??= new[] { PlayerDesignationDefOf.Deconstruct };

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def) =>
            new Job(DesignationJobDefOf.Deconstruct, thing);
    }

    /// <summary>Hauls player-designated items to storage ahead of ordinary hauling (RimWorld:
    /// <c>WorkGiver_HaulUrgently</c>) — the same job and the same storage search <see cref="WorkGiver_Haul"/>
    /// makes. Picking the item up despawns it, which takes the mark with it, as in RimWorld.</summary>
    public sealed class WorkGiver_HaulUrgently : WorkGiver_PlayerDesignated
    {
        private static IReadOnlyList<DesignationDef>? defs;

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        protected override IReadOnlyList<DesignationDef> Defs => defs ??= new[] { PlayerDesignationDefOf.HaulUrgently };

        protected override bool CanWork(Pawn pawn, Thing thing, DesignationDef def) =>
            thing.stackCount > 0 && !HaulAIUtility.IsInValidStorage(thing) && HaulAIUtility.TryFindBestStockpileCell(pawn, thing, out _);

        protected override Job? MakeJob(Pawn pawn, Thing thing, DesignationDef def)
        {
            if (!HaulAIUtility.TryFindBestStockpileCell(pawn, thing, out IntVec3 cell)) return null;
            return new Job(JobDefOf.HaulToCell, thing, cell) { haulMode = HaulMode.ToCellStorage };
        }
    }
}
