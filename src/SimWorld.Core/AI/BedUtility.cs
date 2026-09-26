using System;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Where on a bed a sleeper lies (RimWorld: <c>RimWorld.BedUtility</c>, the two methods this port needs).
    ///
    /// <para/><b>A bed is bigger than the cell a pawn lies in.</b> RimWorld's single bed is 1x2 and its double
    /// bed 2x2; a pawn in one lies on a <i>sleeping slot</i>, one per cell of the bed's width, each at the
    /// bed's head end. For a single bed that is the bed's own <c>Position</c> at every facing, because
    /// <see cref="GenAdj.OccupiedRect"/> turns the footprint about that cell. Everything here that puts a pawn
    /// on a bed — <see cref="JobDriver_LayDown"/> and <see cref="JobDriver_TakeToBed"/>, through
    /// <see cref="Toils_Bed.GotoBed"/> — asks this rather than reading <c>Position</c> directly, so the rule
    /// lives in one place, as RimWorld's does.
    /// </summary>
    public static class BedUtility
    {
        /// <summary>How many pawns a bed of <paramref name="bedSize"/> sleeps: one per cell of its width
        /// (RimWorld: <c>BedUtility.GetSleepingSlotsCount</c>).</summary>
        public static int GetSleepingSlotsCount(IntVec2 bedSize) => bedSize.x;

        /// <summary>
        /// The cell sleeping slot <paramref name="index"/> of a bed at <paramref name="bedCenter"/> facing
        /// <paramref name="bedRot"/> occupies (RimWorld: <c>BedUtility.GetSleepingSlotPos</c>): the row of the
        /// footprint at the end the bed is placed from, counted across its width.
        /// </summary>
        public static IntVec3 GetSleepingSlotPos(int index, IntVec3 bedCenter, Rot4 bedRot, IntVec2 bedSize)
        {
            int slots = GetSleepingSlotsCount(bedSize);
            if (index < 0 || index >= slots)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    "Sleeping slot " + index + " asked of a bed with " + slots + ".");
            }

            CellRect rect = GenAdj.OccupiedRect(bedCenter, bedRot, bedSize);
            if (bedRot == Rot4.North) return new IntVec3(rect.minX + index, bedCenter.y, rect.minZ);
            if (bedRot == Rot4.East) return new IntVec3(rect.minX, bedCenter.y, rect.maxZ - index);
            if (bedRot == Rot4.South) return new IntVec3(rect.minX + index, bedCenter.y, rect.maxZ);
            return new IntVec3(rect.maxX, bedCenter.y, rect.maxZ - index);
        }

        /// <summary>The sleeping slot <paramref name="index"/> of a spawned (or once-spawned)
        /// <paramref name="bed"/>, at its own position, facing and footprint (RimWorld:
        /// <c>Building_Bed.GetSleepingSlotPos</c>).</summary>
        public static IntVec3 GetSleepingSlotPos(this Thing bed, int index = 0)
        {
            if (bed == null) throw new ArgumentNullException(nameof(bed));
            return GetSleepingSlotPos(index, bed.Position, bed.Rotation, bed.Size);
        }
    }
}
