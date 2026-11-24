using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// When a baby needs carrying out of the cold or the heat, and where to (RimWorld: Biotech's
    /// <c>ChildcareUtility</c>, the temperature half of <c>BabyNeedsMovingByHarm</c> and <c>SafePlaceForBaby</c>,
    /// recalled from the 1.4 decompile rather than re-read, so the call shapes are RimWorld's and the details
    /// below are this port's).
    /// <para/>
    /// <b>The problem it solves.</b> A baby cannot walk (<see cref="LifeStageDef.alwaysDowned"/>), wears nothing,
    /// and is comfortable only from 16 C up: <see cref="JobGiver_SeekSafeTemperature"/> is no use to it. Without
    /// somebody to carry it, a baby lies wherever it was last put, and a house that falls to 4 C on a winter night
    /// gives it hypothermia that never recovers, because recovery needs air above its comfortable minimum
    /// (<see cref="TemperatureInjuryUtility"/>). On a 40-strong band that was most of the exposure deaths of a
    /// ten-year run: newborns, night after night, in unheated rooms, while their robed parents were fine.
    /// <para/>
    /// <b>Where to.</b> A bed in a room whose air is inside the baby's comfortable range: its own household's
    /// house first, as RimWorld puts a baby in its own crib first, then the nearest such bed that is not another
    /// household's (<see cref="RestUtility.FindBedFor"/>'s rule). No comfortable bed anywhere is no job, as in
    /// RimWorld: carrying a baby from one cold room to another saves nobody, and a settlement that never lights
    /// a hearth loses its babies. That is a cost the arc should keep.
    /// </summary>
    public static class ChildcareUtility
    {
        /// <summary>RimWorld: <c>ChildcareUtility.CanSuckle</c>'s cousin, the baby test — a pawn in the baby
        /// developmental stage.</summary>
        public static bool IsBaby(Pawn pawn) =>
            pawn.ageTracker?.CurLifeStage?.developmentalStage == DevelopmentalStage.Baby;

        /// <summary>Whether <paramref name="baby"/> lies in air outside its comfortable range (RimWorld: the
        /// temperature reasons of <c>ChildcareUtility.BabyNeedsMovingByHarm</c>).</summary>
        public static bool NeedsMovingForTemperature(Pawn baby)
        {
            if (baby.Dead || !baby.Spawned || !IsBaby(baby) || TemperatureDefOf.Hypothermia == null) return false;
            FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(baby);
            return !comfy.Includes(TemperatureInjuryUtility.AmbientTemperature(baby));
        }

        /// <summary>
        /// A bed <paramref name="hauler"/> can carry <paramref name="baby"/> to whose room is comfortable for it,
        /// or null (RimWorld: <c>ChildcareUtility.SafePlaceForBaby</c>). The baby's household's house first,
        /// then the nearest bed to the baby that is no other household's.
        /// </summary>
        public static Thing? SafeBedForBaby(Pawn baby, Pawn hauler)
        {
            Map.Map? map = baby.Map;
            if (map == null || hauler.Map != map) return null;
            FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(baby);
            int family = baby.relations?.familyId ?? Pawn_RelationsTracker.None;

            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                if (family == Pawn_RelationsTracker.None || dwellings[i].householdId != family) continue;
                List<Thing> beds = dwellings[i].Beds(map);
                for (int b = 0; b < beds.Count; b++)
                {
                    if (Usable(beds[b], baby, hauler, comfy)) return beds[b];
                }
            }

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> all = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < all.Count; i++)
            {
                Thing bed = all[i];
                int distSq = (bed.Position - baby.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                Dwelling? home = DwellingUtility.DwellingOfBed(bed);
                if (home != null && home.householdId != family) continue;
                if (!Usable(bed, baby, hauler, comfy)) continue;
                best = bed;
                bestDistSq = distSq;
            }
            return best;
        }

        private static bool Usable(Thing bed, Pawn baby, Pawn hauler, FloatRange comfy)
        {
            Map.Map map = bed.Map!;
            if (!bed.Spawned || bed.IsBurning()) return false;
            IntVec3 slot = bed.GetSleepingSlotPos();
            if (slot == baby.Position) return false;
            Room? room = map.roomTracker.RoomAt(slot);
            if (room == null || !comfy.Includes(room.Temperature)) return false;
            if (OccupiedByAnother(map, slot, baby)) return false;
            if (!map.reservationManager.CanReserve(hauler, bed)) return false;
            return Reachability.CanReach(hauler, new LocalTargetInfo(slot), PathEndMode.OnCell);
        }

        /// <summary>Somebody else already lies in this bed: a sleeper has it reserved, but a baby or a patient
        /// laid there holds no reservation, so it is read off the cell.</summary>
        private static bool OccupiedByAnother(Map.Map map, IntVec3 slot, Pawn baby)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(slot);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Pawn p && !ReferenceEquals(p, baby) && p.Lying) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Carries a baby out of cold or hot air to a comfortable bed (RimWorld: Biotech's
    /// <c>WorkGiver_BringBabyToSafety</c>, an emergency giver). RimWorld files it under its own Childcare work
    /// type; this port has no Childcare column (adding one changes every work table the host draws), so it is
    /// basic work — in a band every adult minds the babies, which is also the Stone Age reading. The carry
    /// itself is <see cref="JobDriver_TakeToBed"/>, the rescue's driver, under its own job def.
    /// </summary>
    public sealed class WorkGiver_BringBabyToSafety : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (ChildcareUtility.IsBaby(pawns[i])) yield return pawns[i];
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn baby) || ReferenceEquals(baby, pawn) || ChildcareUtility.IsBaby(pawn)) return false;
            if (pawn.faction == null || !ReferenceEquals(baby.faction, pawn.faction)) return false;
            if (!ChildcareUtility.NeedsMovingForTemperature(baby)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, baby)) return false;
            if (!Reachability.CanReach(pawn, baby, PathEndMode)) return false;
            return ChildcareUtility.SafeBedForBaby(baby, pawn) != null;
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn baby)) return null;
            Thing? bed = ChildcareUtility.SafeBedForBaby(baby, pawn);
            return bed != null ? new Job(ChildcareJobDefOf.BringBabyToSafety, baby, bed) : null;
        }
    }

    [DefOf]
    public static class ChildcareJobDefOf
    {
        public static JobDef BringBabyToSafety = null!;
    }
}
