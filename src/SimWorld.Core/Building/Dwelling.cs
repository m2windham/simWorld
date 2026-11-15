using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>The dwelling ladder, oldest first, bound in its own <c>[DefOf]</c> class (CLAUDE.md: a new
    /// binding goes in its own class). The defNames are the host's art families (<c>House_Paleo_a</c> ..
    /// <c>House_Chalco_d</c>), which the host resolves by stripping the variant suffix, so these four names
    /// are the whole contract with it.</summary>
    [DefOf]
    public static class DwellingThingDefOf
    {
        public static ThingDef House_Paleo = null!;
        public static ThingDef House_Meso = null!;
        public static ThingDef House_Neo = null!;
        public static ThingDef House_Chalco = null!;
    }

    /// <summary>Tuning for dwellings. RimWorld has no house to port, so both figures are this port's own, and
    /// <c>DwellingTests</c> pins the behaviour they produce rather than the literals.</summary>
    public static class DwellingTuning
    {
        /// <summary>
        /// How many dwellings a settlement has under way at once, blueprints and frames together. A dwelling
        /// costs several beds' worth of wood; planning one per household in a single pass would spread the
        /// band's first timber across a dozen half-delivered frames and finish none of them. Two keeps a
        /// house going up on each side of the street while the rest wait their turn.
        /// </summary>
        public const int MaxUnderway = 2;
    }

    /// <summary>
    /// A household's home: one building, a <see cref="Things.ThingDef.size"/> footprint the household sleeps
    /// in (translation: citizen-initiated construction, <c>docs/design/the-loop.md</c> §5 item 2; the host's
    /// REQ-001 ask 2). RimWorld has no house — a home there is a room the player walls in round beds — so
    /// this is not a port. The nearest RimWorld shape, and the one it borrows, is a bed: a dwelling <i>is</i>
    /// the bed source for its household, with one sleeping slot per cell of its width
    /// (<see cref="BedUtility.GetSleepingSlotsCount"/>, RimWorld's own rule), owned the way RimWorld's
    /// <c>CompAssignableToPawn</c> owns a bed, except that the owner is a household rather than a pawn.
    ///
    /// <para/><b>Why the dwelling is the bed and does not hold beds.</b> Beds inside a house would need the
    /// house to be something other than an edifice (two edifices cannot share a cell), and then walls, a
    /// roof and a door round them: a RimWorld room, which is the design the owner chose against. One
    /// building that sleeps its household needs one new field (<see cref="householdId"/>) and nothing else.
    /// It is <c>Standable</c>, like a bed, so its people walk in and lie down; its <c>pathCost</c> keeps
    /// anyone else walking round rather than through.
    /// </summary>
    public sealed class Dwelling : Building
    {
        /// <summary>The <see cref="Family"/> that lives here, or <see cref="Pawn_RelationsTracker.None"/>
        /// while nobody has moved in. Set only by <see cref="DwellingUtility.AssignHomes"/>.</summary>
        public int householdId = Pawn_RelationsTracker.None;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref householdId, "householdId", Pawn_RelationsTracker.None);
        }
    }

    /// <summary>
    /// Who lives where, and how many homes a settlement still wants. Everything here is a read of the map and
    /// the settlement except <see cref="AssignHomes"/>, which is the one writer of
    /// <see cref="Dwelling.householdId"/>, and nothing draws from <see cref="Rand"/>: households are taken in
    /// <see cref="Family.id"/> order and dwellings in <see cref="Thing.thingIDNumber"/> order.
    /// </summary>
    public static class DwellingUtility
    {
        /// <summary>The ladder, oldest first.</summary>
        public static IReadOnlyList<ThingDef> Ladder => new[]
        {
            DwellingThingDefOf.House_Paleo,
            DwellingThingDefOf.House_Meso,
            DwellingThingDefOf.House_Neo,
            DwellingThingDefOf.House_Chalco,
        };

        public static bool IsDwelling(ThingDef? def)
        {
            if (def == null) return false;
            IReadOnlyList<ThingDef> ladder = Ladder;
            for (int i = 0; i < ladder.Count; i++)
            {
                if (ReferenceEquals(ladder[i], def)) return true;
            }
            return false;
        }

        /// <summary>The newest rung the civilization knows how to build (<see cref="Things.ThingDef.IsResearchFinished"/>,
        /// read off each rung's own <c>researchPrerequisites</c>). The first rung has none, so a founding band
        /// always has a house it can build. A new rung changes what is built next; nothing standing is
        /// rebuilt.</summary>
        public static ThingDef PreferredDwellingDef()
        {
            IReadOnlyList<ThingDef> ladder = Ladder;
            for (int i = ladder.Count - 1; i > 0; i--)
            {
                if (ladder[i].IsResearchFinished) return ladder[i];
            }
            return ladder[0];
        }

        /// <summary>Every spawned dwelling on <paramref name="map"/>, oldest thing first.</summary>
        public static List<Dwelling> DwellingsOn(Map.Map map)
        {
            var found = new List<Dwelling>();
            IReadOnlyList<ThingDef> ladder = Ladder;
            for (int i = 0; i < ladder.Count; i++)
            {
                IReadOnlyList<Thing> standing = map.listerThings.ThingsOfDef(ladder[i]);
                for (int s = 0; s < standing.Count; s++)
                {
                    if (standing[s] is Dwelling d && d.Spawned) found.Add(d);
                }
            }
            found.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            return found;
        }

        /// <summary>The households of <paramref name="settlement"/>'s living citizens, by id. A citizen with no
        /// household (none in a founded band, which <c>SettlementFounder</c> pairs into households) is not
        /// one.</summary>
        public static SortedDictionary<int, int> Households(Settlement settlement)
        {
            var members = new SortedDictionary<int, int>();
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead) continue;
                int family = p.relations.familyId;
                if (family == Pawn_RelationsTracker.None) continue;
                members[family] = members.TryGetValue(family, out int n) ? n + 1 : 1;
            }
            return members;
        }

        /// <summary>
        /// Moves households in and out. A dwelling whose household has no living citizen left is vacated
        /// (the household died out, or married away: <see cref="FamilyManager.FoundHousehold"/> gives a
        /// couple a new household); then every vacant dwelling, oldest first, goes to the lowest-id household
        /// that has none. Called on the construction initiative's gated pass, so a finished house is lived in
        /// within one pass and a house left empty is offered again within one.
        /// </summary>
        public static void AssignHomes(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            SortedDictionary<int, int> households = Households(settlement);
            List<Dwelling> dwellings = DwellingsOn(map);
            var housed = new HashSet<int>();
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId == Pawn_RelationsTracker.None) continue;
                if (!households.ContainsKey(d.householdId) || !housed.Add(d.householdId)) d.householdId = Pawn_RelationsTracker.None;
            }

            var waiting = new List<int>();
            foreach (int household in households.Keys)
            {
                if (!housed.Contains(household)) waiting.Add(household);
            }

            int next = 0;
            for (int i = 0; i < dwellings.Count && next < waiting.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId == Pawn_RelationsTracker.None) d.householdId = waiting[next++];
            }
        }

        /// <summary>How many of <paramref name="settlement"/>'s citizens have a sleeping slot in their own
        /// household's dwelling: per household that has one, its members up to the dwelling's slots. The rest
        /// sleep in the dormitory, and the bed need counts only them.</summary>
        public static int HousedCitizens(Settlement settlement, Map.Map map)
        {
            SortedDictionary<int, int> households = Households(settlement);
            int housed = 0;
            List<Dwelling> dwellings = DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (!households.TryGetValue(d.householdId, out int members)) continue;
                housed += Math.Min(members, BedUtility.GetSleepingSlotsCount(d.def.size));
            }
            return housed;
        }

        /// <summary>Dwellings standing, plus every blueprint and frame for one, across the whole ladder: a
        /// household housed in a <c>House_Paleo</c> does not want a <c>House_Neo</c> as well.</summary>
        public static int CountBuiltOrPlanned(Map.Map map) => DwellingsOn(map).Count + CountUnderway(map);

        /// <summary>Dwelling blueprints and frames on <paramref name="map"/>.</summary>
        public static int CountUnderway(Map.Map map)
        {
            int count = 0;
            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && IsDwelling(bp.EntityToBuild)) count++;
            }
            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && IsDwelling(f.EntityToBuild)) count++;
            }
            return count;
        }

        // ---------------------------------------------------------------------------------------------
        // Sleeping.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// <paramref name="pawn"/>'s own household's dwelling, when it has a free sleeping slot for them and
        /// they can walk to it; else null, and the pawn looks for a dormitory bed as before
        /// (<see cref="RestUtility.FindBedFor"/>). RimWorld's <c>RestUtility.FindBedFor</c> tries the pawn's
        /// owned bed first in the same way. Read-only: who owns what is <see cref="AssignHomes"/>'s call.
        /// </summary>
        public static Dwelling? FindDwellingFor(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || pawn.relations.familyId == Pawn_RelationsTracker.None) return null;

            List<Dwelling> dwellings = DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId != pawn.relations.familyId) continue;
                int slot = SleepingSlotIndexFor(pawn, d);
                if (slot < 0) return null;
                int slots = BedUtility.GetSleepingSlotsCount(d.def.size);
                if (!map.reservationManager.CanReserve(pawn, d, slots, 0)) return null;
                if (!Reachability.CanReach(pawn, new LocalTargetInfo(d.GetSleepingSlotPos(slot)), PathEndMode.OnCell)) return null;
                return d;
            }
            return null;
        }

        /// <summary>
        /// Which of <paramref name="bed"/>'s sleeping slots <paramref name="pawn"/> lies in (RimWorld:
        /// <c>RestUtility.GetBedSleepingSlotPosFor</c>, which gives an owner its own slot). In a dwelling it is
        /// the pawn's place among the household's members on the map, by thing id, so it never changes while
        /// the household does not and two members never share one; -1 when the household has more members
        /// than the dwelling has slots and this pawn is past the last. Any other bed: slot 0.
        /// </summary>
        public static int SleepingSlotIndexFor(Pawn pawn, Thing bed)
        {
            if (!(bed is Dwelling) || bed.Map == null) return 0;
            int family = pawn.relations.familyId;
            int rank = 0;
            IReadOnlyList<Pawn> pawns = bed.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (ReferenceEquals(other, pawn) || other.Dead || other.relations.familyId != family) continue;
                if (other.thingIDNumber < pawn.thingIDNumber) rank++;
            }
            return rank < BedUtility.GetSleepingSlotsCount(bed.def.size) ? rank : -1;
        }

        /// <summary>Whether <paramref name="cell"/> is one of <paramref name="thing"/>'s sleeping slots, when
        /// it is a dwelling: a pawn lying there is in bed (<see cref="RestUtility.CurrentBed"/>), and one
        /// merely walking across the rest of the house is not.</summary>
        public static bool IsSleepingSlotOf(Thing thing, IntVec3 cell)
        {
            if (!(thing is Dwelling)) return false;
            int slots = BedUtility.GetSleepingSlotsCount(thing.def.size);
            for (int i = 0; i < slots; i++)
            {
                if (thing.GetSleepingSlotPos(i) == cell) return true;
            }
            return false;
        }
    }
}
