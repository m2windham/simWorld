using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Households and their houses (<see cref="Dwelling"/>, <see cref="DwellingUtility"/>,
    /// <see cref="DwellingPlanner"/>, and the village plots of <see cref="SettlementCompound"/>): a house of real
    /// walls, a door and beds planned when a household lacks one, each household sleeping in its own, laid out
    /// along a street out of the compound's door, never on the ring, and put back up when it is knocked down.
    /// Assertions read the map: what stands where, who sleeps in which bed. The rung plans themselves, their
    /// rooms and their air, are <see cref="ShelterRoomTests"/>.
    /// </summary>
    [Collection("GlobalDefs")]
    public class DwellingTests : ContentTestBase
    {
        public DwellingTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int x = 120, int z = 120) => new CoreMap(x, z, TerrainDefOf.Soil);

        /// <summary>The first rung is learned, not given: a band builds no house before it knows windbreaks.</summary>
        private static void KnowWindbreaks() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("WindbreakShelters"));

        /// <summary>A band of <paramref name="couples"/> households of two, as SettlementFounder pairs one.</summary>
        private static Settlement Band(int couples, int singles = 0)
        {
            KnowWindbreaks();
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Village", 0);
            for (int i = 0; i < couples; i++)
            {
                Pawn a = NewHuman("A" + i), b = NewHuman("B" + i);
                settlement.AddCitizen(a);
                settlement.AddCitizen(b);
                Find.FamilyManager.FoundHousehold(a, b, 0);
            }
            for (int i = 0; i < singles; i++)
            {
                Pawn p = NewHuman("S" + i);
                settlement.AddCitizen(p);
                Find.FamilyManager.FoundHousehold(p, null, 0);
            }
            return settlement;
        }

        private static void Pass(Settlement settlement, CoreMap map, int pass)
        {
            Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
            SettlementConstructionInitiative.TickSettlement(settlement, map);
        }

        private static void BuildEverythingPlanned(CoreMap map)
        {
            Pawn builder = NewHuman("Builder");
            foreach (Blueprint bp in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList())
            {
                bp.ReplaceWithFrame().CompleteConstruction(builder);
            }
        }

        private static int DwellingBlueprints(CoreMap map) => DwellingUtility.CountUnderway(map);

        private static bool InAHouse(CoreMap map, Thing t) => DwellingUtility.DwellingAt(map, t.Position) != null;

        private static int DormitoryBeds(CoreMap map) =>
            map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count(b => !InAHouse(map, b));

        /// <summary>Passes, building whatever each one planned, until a pass plans nothing. Returns the most
        /// dwellings ever planned at once.</summary>
        private static int GrowUntilSettled(Settlement settlement, CoreMap map, int from = 0)
        {
            int peakUnderway = 0;
            for (int pass = from; pass < from + 400; pass++)
            {
                int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                Pass(settlement, map, pass);
                peakUnderway = Math.Max(peakUnderway, DwellingBlueprints(map));
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == before) return peakUnderway;
                BuildEverythingPlanned(map);
            }
            Assert.Fail("still planning after 400 passes");
            return peakUnderway;
        }

        private static Thing TheDoor(CoreMap map) => Assert.Single(map.listerThings.ThingsOfDef(CompoundThingDefOf.Door));

        // ---- the ladder ----

        [Theory]
        [InlineData(null, null)]
        [InlineData("WindbreakShelters", "House_Paleo")]
        [InlineData("PitDwellings", "House_Meso")]
        [InlineData("Shelter", "House_Neo")]
        public void The_newest_house_the_civilization_knows_is_the_one_it_builds_and_none_before_the_first(string? project, string? expected)
        {
            if (project != null) Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(project));
            Assert.Equal(expected, DwellingUtility.PreferredDwellingDef()?.defName);
        }

        [Fact]
        public void A_band_that_has_not_learned_windbreaks_sleeps_in_its_dormitory()
        {
            CoreMap map = NewMap();
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Camp", 0);
            for (int i = 0; i < 3; i++)
            {
                Pawn a = NewHuman("A" + i), b = NewHuman("B" + i);
                settlement.AddCitizen(a);
                settlement.AddCitizen(b);
                Find.FamilyManager.FoundHousehold(a, b, 0);
            }
            GrowUntilSettled(settlement, map);
            Assert.Empty(DwellingUtility.DwellingsOn(map));
            Assert.Equal(6, DormitoryBeds(map));
        }

        // ---- the need ----

        [Fact]
        public void Every_household_gets_a_dwelling_once_everyone_has_a_bed_planned_and_only_a_few_go_up_at_once()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 10, singles: 1);

            // Everything else planned and nothing built yet: a bed planned for everyone is enough for the
            // first homes to be planned too. Waiting for every dormitory bed to stand starved a wood-poor
            // map of homes (storyteller seed 2024); RimWorld builds whatever is designated, in no fixed order.
            for (int pass = 0; pass < 40; pass++) Pass(settlement, map, pass);
            Assert.Equal(settlement.Citizens.Count, map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Count(bp => ReferenceEquals(bp.EntityToBuild, ConstructionThingDefOf.Bed) && !InAHouse(map, bp)));
            Assert.Empty(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed));
            Assert.InRange(DwellingBlueprints(map), 1, DwellingTuning.MaxUnderway);

            BuildEverythingPlanned(map);
            int peak = GrowUntilSettled(settlement, map, from: 40);

            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            Assert.Equal(11, dwellings.Count);
            Assert.All(dwellings, d => Assert.True(d.IsComplete(map)));
            Assert.InRange(peak, 1, DwellingTuning.MaxUnderway);
            var households = settlement.Citizens.Select(p => p.relations.familyId).Distinct().OrderBy(i => i).ToList();
            Assert.Equal(households, dwellings.Select(d => d.householdId).OrderBy(i => i).ToList());
            Assert.Equal(settlement.Citizens.Count, DwellingUtility.HousedCitizens(settlement, map));

            // The dormitory was not added to once the homes went up: one bed each, planned before the houses.
            Assert.Equal(settlement.Citizens.Count, DormitoryBeds(map));
        }

        [Fact]
        public void Citizens_with_no_household_want_no_dwelling()
        {
            CoreMap map = NewMap();
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Camp", 0);
            for (int i = 0; i < 20; i++) settlement.AddCitizen(NewHuman("Loner" + i));
            GrowUntilSettled(settlement, map);
            Assert.Empty(DwellingUtility.DwellingsOn(map));
            Assert.Equal(20, DormitoryBeds(map));
        }

        [Fact]
        public void A_newly_married_couple_moves_out_and_the_settlement_builds_them_a_home()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 10);
            GrowUntilSettled(settlement, map);
            Assert.Equal(10, DwellingUtility.DwellingsOn(map).Count);

            // Two of the band remarry each other: a new household, and the two they left keep their homes.
            Pawn a = settlement.Citizens[0], b = settlement.Citizens[3];
            Find.FamilyManager.FoundHousehold(a, b, 0);
            GrowUntilSettled(settlement, map, from: 500);

            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            Assert.Equal(11, dwellings.Count);
            Assert.Contains(dwellings, d => d.householdId == a.relations.familyId);
        }

        [Fact]
        public void A_house_whose_household_is_gone_goes_to_the_next_household_without_one()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 2);
            GrowUntilSettled(settlement, map);
            Dwelling first = DwellingUtility.DwellingsOn(map).First();
            int gone = first.householdId;

            // The household dies out; a newcomer household arrives.
            foreach (Pawn p in settlement.Citizens.Where(p => p.relations.familyId == gone).ToList()) p.health.Kill(null, null);
            Pawn newcomer = NewHuman("Newcomer");
            settlement.AddCitizen(newcomer);
            Find.FamilyManager.FoundHousehold(newcomer, null, 0);

            DwellingUtility.AssignHomes(settlement, map);

            Assert.Equal(newcomer.relations.familyId, first.householdId);
            Assert.Equal(2, DwellingUtility.DwellingsOn(map).Count);
        }

        // ---- the layout ----

        [Fact]
        public void Houses_line_a_street_out_of_the_door_and_their_doors_open_onto_it()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 20);
            GrowUntilSettled(settlement, map);

            Thing door = TheDoor(map);
            int streetZ = door.Position.z;
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            Assert.Equal(20, dwellings.Count);

            var taken = new HashSet<IntVec3>();
            foreach (Dwelling d in dwellings)
            {
                CellRect rect = d.footprint;
                Assert.All(rect.Cells, c => Assert.True(taken.Add(c), "two houses share " + c));

                // North of the street a house is turned North, south of it South, and either way its front row,
                // with its door in it, is the one next to the street.
                bool north = rect.minZ > streetZ;
                Assert.Equal(north ? Rot4.North : Rot4.South, d.rot);
                int frontRow = north ? rect.minZ - 1 : rect.maxZ + 1;
                Assert.True(Math.Abs(frontRow - streetZ) <= 1, d.def.defName + " at " + rect + " does not open onto the street at z " + streetZ);
                IntVec3 houseDoor = d.Parts.Single(p => p.Kind == DwellingPartKind.Door).Cell;
                Assert.Equal(north ? rect.minZ : rect.maxZ, houseDoor.z);
                Assert.IsType<Door>(map.edificeGrid[houseDoor]);

                // The street in front of it is open ground.
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    var c = new IntVec3(x, 0, frontRow);
                    Assert.Null(map.edificeGrid[c]);
                    Assert.True(GenGrid.Standable(c, map));
                }
            }

            // Houses on both sides of the street, and both sides of the compound.
            Assert.Contains(dwellings, d => d.rot == Rot4.North);
            Assert.Contains(dwellings, d => d.rot == Rot4.South);
            Assert.Contains(dwellings, d => d.footprint.minX > door.Position.x);
            Assert.Contains(dwellings, d => d.footprint.maxX < door.Position.x - 10);
        }

        [Fact]
        public void Nothing_ever_lands_on_a_wall_slot_or_the_door_threshold()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 20);
            settlement.AddStore(Def("WoodLog"), 60 * StorageHutNeed.StacksPerStorageHut * Def("WoodLog").stackLimit);
            GrowUntilSettled(settlement, map);
            for (int pass = 0; pass < 30; pass++)
            {
                SettlementWorksInitiative.Run(settlement, map);
                BuildEverythingPlanned(map);
            }

            SettlementCompound compound = map.settlementCompound!;
            foreach (IntVec3 c in compound.WallCells)
            {
                Thing? here = map.edificeGrid[c];
                Assert.True(here != null && StoneWallMaterials.IsWall(here.def), "wall slot " + c + " holds " + (here?.def.defName ?? "nothing"));
            }
            Thing door = TheDoor(map);
            Assert.Same(door, map.edificeGrid[door.Position]);
            for (int dz = -1; dz <= 1; dz++)
            {
                IntVec3 threshold = door.Position + new IntVec3(1, 0, dz);
                Assert.Null(map.edificeGrid[threshold]);
            }
            Assert.NotEmpty(map.listerThings.ThingsOfDef(ResearchWorkDefOf.ResearchBench));
        }

        [Fact]
        public void A_resident_walks_in_off_the_street_and_back_out_again()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 10);
            GrowUntilSettled(settlement, map);
            Thing door = TheDoor(map);

            var edge = new IntVec3(0, 0, 0);
            foreach (Dwelling d in DwellingUtility.DwellingsOn(map))
            {
                Pawn resident = settlement.Citizens.First(p => p.relations.familyId == d.householdId);
                GenSpawn.Spawn(resident, door.Position + new IntVec3(-2, 0, 0), map); // inside the ring
                IntVec3 slot = d.Beds(map).First().GetSleepingSlotPos(0);
                Assert.True(Reachability.CanReach(resident, new LocalTargetInfo(slot), PathEndMode.OnCell), "cannot get home to " + slot);
                resident.Position = slot;
                Assert.True(Reachability.CanReach(resident, new LocalTargetInfo(edge), PathEndMode.OnCell), "cannot leave " + slot);
                resident.DeSpawn();
            }
        }

        // ---- sleeping ----

        [Fact]
        public void Each_household_sleeps_in_its_own_house_and_whoever_it_has_no_bed_for_in_the_dormitory()
        {
            CoreMap map = NewMap(60, 60);
            Settlement settlement = Band(couples: 3, singles: 0);
            // A household of five in a windbreak hut of two beds: three of them sleep in the dormitory.
            Pawn a = settlement.Citizens[0];
            for (int i = 0; i < 3; i++)
            {
                Pawn kid = NewHuman("Kid" + i);
                kid.relations.familyId = a.relations.familyId;
                settlement.AddCitizen(kid);
            }

            DwellingDef hut = ShelterKit.Rung("House_Paleo");
            for (int i = 0; i < 3; i++) ShelterKit.Raise(map, hut, new IntVec3(20 + 6 * i, 0, 30));
            var dormitory = new List<Thing>();
            for (int i = 0; i < 4; i++) dormitory.Add(GenSpawn.Spawn(ThingMaker.MakeThing(ConstructionThingDefOf.Bed), new IntVec3(10 + 2 * i, 0, 10), map));
            DwellingUtility.AssignHomes(settlement, map);
            Assert.All(DwellingUtility.DwellingsOn(map), d => Assert.NotEqual(Pawn_RelationsTracker.None, d.householdId));

            List<Pawn> sleepers = settlement.Citizens.ToList();
            for (int i = 0; i < sleepers.Count; i++)
            {
                GenSpawn.Spawn(sleepers[i], new IntVec3(5 + i, 0, 20), map);
                sleepers[i].needs.rest!.CurLevel = 0.05f;
            }
            RunTicks(600, sleepers.ToArray());

            var beds = new HashSet<Thing>();
            foreach (Pawn p in sleepers)
            {
                Assert.True(p.Asleep, p + " is not asleep");
                Thing? bed = p.jobs.curJob?.GetTarget(TargetIndex.A).Thing;
                Assert.NotNull(bed);
                Assert.Same(bed, p.CurrentBed());
                Assert.True(beds.Add(bed!), "two sleepers share " + bed);
                Dwelling? home = DwellingUtility.DwellingOfBed(bed!);
                if (home != null) Assert.Equal(p.relations.familyId, home.householdId);
                Assert.True(p.needs.rest!.lastRestEffectiveness > 1f);
            }
            Pawn[] big = sleepers.Where(p => p.relations.familyId == a.relations.familyId).ToArray();
            Assert.Equal(2, big.Count(p => DwellingUtility.IsOwnHomeBed(p, p.CurrentBed()!)));
            Assert.Equal(3, big.Count(p => dormitory.Contains(p.CurrentBed()!)));
        }

        // ---- standing ----

        [Fact]
        public void A_house_that_loses_a_wall_is_planned_back_and_its_household_keeps_it()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 2);
            GrowUntilSettled(settlement, map);
            Dwelling d = DwellingUtility.DwellingsOn(map).First();
            int household = d.householdId;
            IntVec3 wallCell = d.Parts.First(p => p.Kind == DwellingPartKind.Wall).Cell;

            map.edificeGrid[wallCell]!.Destroy(DestroyMode.KillFinalize);
            Assert.False(d.IsComplete(map));
            Pass(settlement, map, 1000);

            Assert.Equal(household, d.householdId);
            Assert.Contains(map.thingGrid.ThingsListAt(wallCell), t => t is Blueprint);
            BuildEverythingPlanned(map);
            Assert.True(d.IsComplete(map));
        }

        // ---- determinism and saving ----

        [Fact]
        public void The_same_seed_lays_out_the_same_village_and_another_seed_another()
        {
            List<string> Run(int seed)
            {
                CoreMap.ResetMapIdCounter();
                Pawn.ResetThingIdCounter();
                Find.Reset();
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                CoreMap map = NewMap();
                GrowUntilSettled(Band(couples: 10), map);
                return map.listerThings.AllThings.Where(t => t.def.category == ThingCategory.Building)
                    .Select(t => t.def.defName + "@" + t.Position + "/" + t.Rotation)
                    .Concat(DwellingUtility.DwellingsOn(map).Select(d => d.def.defName + "@" + d.footprint + "/" + d.rot + "#" + d.householdId))
                    .OrderBy(s => s, StringComparer.Ordinal).ToList();
            }

            List<string> first = Run(777);
            Assert.Contains(first, s => s.StartsWith("House_Paleo", StringComparison.Ordinal));
            Assert.Equal(first, Run(777));
            Assert.NotEqual(first, Run(778));
        }

        [Fact]
        public void Who_lives_where_round_trips_through_Scribe_and_the_village_carries_on_the_same()
        {
            CoreMap original = NewMap();
            Settlement settlement = Band(couples: 10);
            for (int pass = 0; pass < 40; pass++)
            {
                Pass(settlement, original, pass);
                BuildEverythingPlanned(original);
            }
            Assert.NotEmpty(DwellingUtility.DwellingsOn(original));

            string xml = Scribe.SaveToString(original, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            static List<string> Homes(CoreMap map) => DwellingUtility.DwellingsOn(map)
                .Select(d => d.def.defName + "@" + d.footprint + "/" + d.rot + "#" + d.householdId).ToList();
            Assert.Equal(Homes(original), Homes(loaded));
            Assert.All(DwellingUtility.DwellingsOn(loaded), d => Assert.NotEqual(Pawn_RelationsTracker.None, d.householdId));

            static List<string> Planned(CoreMap map) => map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Select(bp => bp.EntityToBuild.defName + "@" + bp.Position + "/" + bp.Rotation).OrderBy(s => s, StringComparer.Ordinal).ToList();
            for (int pass = 40; pass < 60; pass++)
            {
                Pass(settlement, original, pass);
                Pass(settlement, loaded, pass);
            }
            Assert.Equal(Planned(original), Planned(loaded));
        }
    }
}
