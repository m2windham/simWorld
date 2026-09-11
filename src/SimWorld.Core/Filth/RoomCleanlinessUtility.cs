using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Filth
{
    /// <summary>
    /// How dirty a room is, and what that costs (RimWorld: <c>RoomStatWorker_Cleanliness</c> and the
    /// <c>RoomStatDefOf.SurgerySuccessChanceFactor</c> it feeds). This is where filth stops being scenery: a
    /// pile of it in a room is a number somebody else reads.
    /// <para/>
    /// <b>Computed on demand, not cached on <see cref="Room"/>.</b> RimWorld caches room stats on the Room
    /// itself and invalidates them when the room changes. <see cref="Room"/> here has no stat storage and
    /// adding some means editing <c>Building/Room.cs</c> and <c>Building/RoomTracker.cs</c>, which this lane
    /// does not own. The two callers — a surgery, and a situational thought recomputed at most every 10 ticks
    /// — are both rare enough that walking a room's cells at the point of use is cheaper than the cache would
    /// have been to keep correct. If a third, hot caller appears, the cache is the fix and it belongs on
    /// <see cref="RoomTracker"/>.
    /// <para/>
    /// <b>Trim — terrain contributes nothing.</b> RimWorld sums each cell's terrain cleanliness alongside the
    /// Things in it, which is how a sterile tile makes an operating theatre better than a bare floor. This
    /// port's <see cref="TerrainDef"/> carries no <c>statBases</c> at all, so there is nothing to read and no
    /// sterile floor in content to read it from; only Things count here. Noted rather than faked.
    /// </summary>
    public static class RoomCleanlinessUtility
    {
        /// <summary>
        /// Multiplier on a surgery's success chance, by the cleanliness of the room it happens in (RimWorld:
        /// <c>RoomStatDefOf.SurgerySuccessChanceFactor</c>, whose own score stages run from a filthy room
        /// through to a sterile one). <b>SimWorld's own points</b> — RimWorld's are not sourced here — so
        /// <c>SurgeryCleanlinessTests</c> pins the trend (operating in filth fails more often than operating
        /// in a clean room, and a clean room is never a penalty) rather than any point on this curve.
        /// </summary>
        public static readonly SimpleCurve SurgerySuccessFactorFromCleanliness = new SimpleCurve
        {
            new CurvePoint(-5f, 0.7f),
            new CurvePoint(-2f, 0.85f),
            new CurvePoint(0f, 1f),
            new CurvePoint(2f, 1.05f),
        };

        /// <summary>
        /// Average cleanliness across a room's cells: every Thing standing in them contributes its
        /// <c>Cleanliness</c> stat, which is zero for everything that has not said otherwise and negative for
        /// filth (RimWorld: <c>RoomStatWorker_Cleanliness.GetScore</c>). Higher is cleaner; a spotless room
        /// is exactly 0.
        /// <para/>
        /// <b>Thickness is deliberately not multiplied in</b>, matching RimWorld's own room stat worker,
        /// which counts each pile once however deep it is. Thickness is paid for in cleaning work instead
        /// (<see cref="JobDriver_CleanFilth"/>), which is where it is felt.
        /// </summary>
        public static float CleanlinessOf(Room? room, Map.Map? map)
        {
            if (room == null || map == null || room.Cells.Count == 0) return 0f;

            float total = 0f;
            IReadOnlyList<IntVec3> cells = room.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    total += things[j].GetStatValue(FilthStatDefOf.Cleanliness);
                }
            }
            return total / cells.Count;
        }

        /// <summary>Cleanliness of the room containing <paramref name="cell"/>; 0 (spotless) when there is no
        /// room there — rooms are lazily flooded, see <see cref="CleaningBounds"/>'s remarks.</summary>
        public static float CleanlinessAt(Map.Map? map, IntVec3 cell)
        {
            if (map == null || !GenGrid.InBounds(cell, map)) return 0f;
            return CleanlinessOf(map.roomTracker.RoomAt(cell), map);
        }

        /// <summary>Cleanliness of the room <paramref name="pawn"/> is standing in.</summary>
        public static float CleanlinessAround(Pawn? pawn) =>
            pawn == null || !pawn.Spawned ? 0f : CleanlinessAt(pawn.Map, pawn.Position);

        /// <summary>
        /// What operating in this room does to a surgeon's odds (RimWorld: <c>Recipe_Surgery.CheckSurgeryFail</c>
        /// multiplying the surgeon's chance by the room's <c>SurgerySuccessChanceFactor</c>). Called with the
        /// patient, since that is who the operation is happening on top of and the surgeon is adjacent to
        /// them by construction. A patient on no map — a caravan surgery — gets a flat 1: there is no room to
        /// be dirty.
        /// </summary>
        public static float SurgerySuccessFactorFor(Pawn? patient)
        {
            if (patient == null || !patient.Spawned) return 1f;
            return SurgerySuccessFactorFromCleanliness.Evaluate(CleanlinessAround(patient));
        }
    }
}
