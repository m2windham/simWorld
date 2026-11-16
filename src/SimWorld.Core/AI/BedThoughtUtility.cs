using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Thoughts;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>The four sleeping memories (RimWorld: <c>ThoughtDefOf.SleptInBedroom</c>, <c>SleptInBarracks</c>,
    /// <c>SleptOnGround</c>, <c>SleptOutside</c>).</summary>
    [DefOf]
    public static class SleepThoughtDefOf
    {
        public static ThoughtDef SleptInBedroom = null!;
        public static ThoughtDef SleptInBarracks = null!;
        public static ThoughtDef SleptOnGround = null!;
        public static ThoughtDef SleptOutside = null!;
    }

    /// <summary>
    /// What where somebody slept does to their mood (RimWorld: <c>Toils_LayDown.ApplyBedThoughts</c>). Every
    /// sleeping memory is cleared first, so a pawn carries only the one for their latest sleep, as RimWorld's
    /// does.
    ///
    /// <para/><b>Translated, not ported, in three places.</b>
    /// <list type="bullet">
    /// <item><b>Bedroom and barracks are read off the bed, not the room.</b> RimWorld asks the room's role,
    /// which comes from how many owned beds stand in it; this port has no room roles and no bed ownership.
    /// A household's own <see cref="Dwelling"/> is its bedroom, and any other bed, the compound's dormitory
    /// among them, is barracks. RimWorld gives neither memory for a bed the sleeper does not own; here every
    /// dormitory bed is open to anyone, so the dormitory sleeper gets the barracks memory instead of
    /// nothing.</item>
    /// <item><b>Outside only off a bed.</b> RimWorld adds <c>SleptOutside</c> whenever the room is
    /// psychologically outdoors, bed or not. This port's constructed rooms are never roofed
    /// (<see cref="Room.TouchesOutside"/> is true for every one of them), so that rule would call every
    /// dormitory sleeper outside. Here it is the ground sleeper's alone.</item>
    /// <item><b>Applied as the pawn falls asleep</b>, not when they get up: a job driver here keeps no saved
    /// state, so a sleep that started before a save would otherwise end without its memory.</item>
    /// </list>
    /// </summary>
    public static class BedThoughtUtility
    {
        public static void ApplyBedThoughts(Pawn pawn, Thing? bed)
        {
            MemoryThoughtHandler? memories = pawn.needs?.mood?.thoughts.memories;
            if (memories == null) return;

            memories.RemoveMemoriesOfDef(SleepThoughtDefOf.SleptInBedroom);
            memories.RemoveMemoriesOfDef(SleepThoughtDefOf.SleptInBarracks);
            memories.RemoveMemoriesOfDef(SleepThoughtDefOf.SleptOnGround);
            memories.RemoveMemoriesOfDef(SleepThoughtDefOf.SleptOutside);

            if (bed == null)
            {
                memories.TryGainMemory(SleepThoughtDefOf.SleptOnGround);
                Room? room = pawn.Map?.roomTracker.RoomAt(pawn.Position);
                if (room == null || room.TouchesOutside) memories.TryGainMemory(SleepThoughtDefOf.SleptOutside);
                return;
            }

            memories.TryGainMemory(IsOwnHome(pawn, bed) ? SleepThoughtDefOf.SleptInBedroom : SleepThoughtDefOf.SleptInBarracks);
        }

        private static bool IsOwnHome(Pawn pawn, Thing bed) =>
            bed is Dwelling dwelling
            && pawn.relations != null
            && pawn.relations.familyId != Pawn_RelationsTracker.None
            && dwelling.householdId == pawn.relations.familyId;
    }
}
