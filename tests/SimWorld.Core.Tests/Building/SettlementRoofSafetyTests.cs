using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The fourth playtest's two crush deaths, rebuilt on a real generated settlement. Its save holds the
    /// evidence: the two died at 2h on the same tick in a hall mined out of the mountain east of the compound,
    /// collapsed rock lies around the cells where they fell, and in the middle of the hall's thick rock roof are
    /// two house-sized holes with no roof at all, which nothing but <see cref="DwellingPlanner.Demolish"/> makes
    /// (it lifted every roof over an old house's footprint, rock included). The town's renewal was pulling down
    /// old houses that stood in the hall; their walls held the mountain up, and the last of them came down.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SettlementRoofSafetyTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public SettlementRoofSafetyTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static (Settlement settlement, CoreMap map, List<Pawn> citizens) Opened(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            return (settlement, map, settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList());
        }

        /// <summary>
        /// A <paramref name="size"/>-square block near the band, wiped to bare soil with no roof and nothing on
        /// it, and with no pawn standing in it: the hall the tests then roof with the mountain's rock.
        /// </summary>
        private static CellRect ClearedBlock(CoreMap map, List<Pawn> citizens, int size)
        {
            var centre = new IntVec3((int)citizens.Average(p => p.Position.x), 0, (int)citizens.Average(p => p.Position.z));
            for (int r = 12; r < 100; r += 3)
            {
                foreach (IntVec3 corner in new[] { new IntVec3(centre.x + r, 0, centre.z + r), new IntVec3(centre.x - r - size, 0, centre.z + r), new IntVec3(centre.x + r, 0, centre.z - r - size), new IntVec3(centre.x - r - size, 0, centre.z - r - size) })
                {
                    CellRect block = CellRect.FromLimits(corner.x, corner.z, corner.x + size - 1, corner.z + size - 1);
                    if (!GenGrid.InBounds(new IntVec3(block.minX - 1, 0, block.minZ - 1), map) || !GenGrid.InBounds(new IntVec3(block.maxX + 1, 0, block.maxZ + 1), map)) continue;
                    if (block.ExpandedBy(1).Cells.Any(c => map.thingGrid.ThingsListAt(c).Any(t => t is Pawn))) continue;
                    if (block.ExpandedBy(1).Cells.Any(c => map.roofGrid.Roofed(c))) continue;
                    // The roof's reach runs along the roof, so only the ring just outside could hold the block's roof
                    // up; nothing there may, so what holds it is only what a test builds.
                    if (block.ExpandedBy(1).Cells.Any(c => !block.Contains(c) && map.edificeGrid[c] is Thing e && e.def.Fillage == FillCategory.Full)) continue;
                    foreach (IntVec3 c in block.Cells)
                    {
                        foreach (Thing t in map.thingGrid.ThingsListAt(c).ToList()) t.Destroy(DestroyMode.Vanish);
                        map.terrainGrid.SetTerrain(c, SimWorld.Map.TerrainDefOf.Soil);
                    }
                    return block;
                }
            }
            throw new Xunit.Sdk.XunitException("no clear block near the band");
        }

        private static IntVec3 Center(CellRect r) => new IntVec3((r.minX + r.maxX) / 2, 0, (r.minZ + r.maxZ) / 2);

        private static void RoofWithRock(CoreMap map, CellRect block)
        {
            foreach (IntVec3 c in block.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick);
        }

        /// <summary>A ring of wooden walls around <paramref name="room"/>, owned by the band.</summary>
        private static List<Thing> Ring(CoreMap map, CellRect room, Faction faction)
        {
            var walls = new List<Thing>();
            foreach (IntVec3 c in room.EdgeCells)
            {
                Thing wall = ThingMaker.MakeThing(Def("Wall"), Def("WoodLog"));
                wall.SetFaction(faction);
                walls.Add(GenSpawn.Spawn(wall, c, map));
            }
            return walls;
        }

        private static void MoveTo(Pawn pawn, IntVec3 cell, CoreMap map)
        {
            pawn.DeSpawn();
            GenSpawn.Spawn(pawn, cell, map);
        }

        [Fact]
        public void A_crush_death_letter_says_what_brought_the_roof_down()
        {
            (Settlement _, CoreMap map, List<Pawn> citizens) = Opened("roof-cause-letter");
            CellRect hall = ClearedBlock(map, citizens, 9);
            RoofWithRock(map, hall);
            Thing rock = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sandstone")), Center(hall), map);
            Pawn victim = citizens[0];
            MoveTo(victim, Center(hall) + new IntVec3(2, 0, 0), map);

            rock.Destroy(DestroyMode.KillFinalize);

            Assert.True(victim.Dead, "a thick rock roof falling on someone kills them");
            Letter letter = Find.LetterStack.LettersListForReading.Last(l => l.label == "Death: " + victim.Label);
            output.WriteLine(letter.text);
            Assert.Equal(victim.Label + " was crushed to death when the rock overhead fell in when the sandstone holding it up was mined out.", letter.text);
        }

        [Fact]
        public void A_crush_under_an_old_house_being_pulled_down_names_the_house()
        {
            (Settlement settlement, CoreMap map, List<Pawn> citizens) = Opened("roof-cause-house");
            CellRect hall = ClearedBlock(map, citizens, 9);
            CellRect footprint = hall.ContractedBy(2);
            // Rock over the house alone: its walls are all that hold it, so it stands until the last goes and then
            // falls whole. Over a wider hall the first corners to fall leave rock that holds the middle up.
            RoofWithRock(map, footprint);
            List<Thing> walls = Ring(map, footprint, citizens[0].Faction!);
            SettlementConstructionInitiative.EnsureCompound(settlement, map);
            SettlementCompound compound = map.settlementCompound!;
            var old = new Dwelling(DefDatabase<DwellingDef>.AllDefsListForReading[0], footprint, Rot4.North) { id = 9001, beingDemolished = true };
            compound.dwellings.Add(old);
            Pawn victim = citizens[0];
            MoveTo(victim, Center(footprint), map);

            foreach (Thing wall in walls) wall.Destroy(DestroyMode.Deconstruct);

            Assert.True(victim.Dead);
            Letter letter = Find.LetterStack.LettersListForReading.Last(l => l.label == "Death: " + victim.Label);
            output.WriteLine(letter.text);
            Assert.Equal(victim.Label + " was crushed to death when the rock over the old house fell in as it came down.", letter.text);
        }

        [Fact]
        public void Pulling_down_an_old_house_in_a_mined_hall_keeps_the_mountain_up()
        {
            (Settlement settlement, CoreMap map, List<Pawn> citizens) = Opened("roof-demolish-hall");
            CellRect hall = ClearedBlock(map, citizens, 11);
            RoofWithRock(map, hall);
            CellRect footprint = hall.ContractedBy(2);
            List<Thing> walls = Ring(map, footprint, citizens[0].Faction!);
            SettlementConstructionInitiative.EnsureCompound(settlement, map);
            SettlementCompound compound = map.settlementCompound!;
            var old = new Dwelling(DefDatabase<DwellingDef>.AllDefsListForReading[0], footprint, Rot4.North) { id = 9002, beingDemolished = true };
            compound.dwellings.Add(old);

            DwellingPlanner.Demolish(map, compound);
            Assert.All(footprint.Cells, c => Assert.Same(RoofDefOf.RoofRockThick, map.roofGrid.RoofAt(c)));

            // The settlement's own hands, as the guarded work givers take walls: only what holds nothing up.
            bool took;
            int taken = 0;
            do
            {
                took = false;
                foreach (Thing wall in walls.Where(w => w.Spawned).ToList())
                {
                    Designation? des = map.designationManager.DesignationOn(wall, PlayerDesignationDefOf.Deconstruct);
                    if (des == null || des.source != DesignationSource.Settlement || RoofCollapseUtility.WouldCollapseRoofIfRemoved(wall)) continue;
                    JobDriver_Deconstruct.Deconstruct(wall);
                    taken++;
                    took = true;
                }
                DwellingPlanner.Demolish(map, compound);
            }
            while (took);

            List<Thing> left = walls.Where(w => w.Spawned).ToList();
            output.WriteLine($"{taken} walls taken, {left.Count} left holding the rock");
            Assert.True(taken > 0, "the old house came down");
            Assert.NotEmpty(left);
            Assert.All(left, w => Assert.Null(map.designationManager.DesignationOn(w, PlayerDesignationDefOf.Deconstruct)));
            Assert.DoesNotContain(old, compound.dwellings);
            Assert.All(hall.Cells, c => Assert.True(map.roofGrid.Roofed(c)));
            Assert.DoesNotContain(map.listerThings.ThingsOfDef(RoofCollapseDefOf.CollapsedRocks), t => hall.Contains(t.Position));
        }

        [Fact]
        public void The_settlement_never_takes_the_last_wall_holding_up_a_mountain()
        {
            (Settlement settlement, CoreMap map, List<Pawn> citizens) = Opened("roof-band-hall");
            CellRect hall = ClearedBlock(map, citizens, 11);
            RoofWithRock(map, hall);
            List<Thing> walls = Ring(map, hall.ContractedBy(2), citizens[0].Faction!);
            foreach (Thing wall in walls)
            {
                map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(wall), PlayerDesignationDefOf.Deconstruct, DesignationSource.Settlement));
            }
            // Down to the walls the mountain rests on, the way the guarded hands would get there.
            bool took;
            do
            {
                took = false;
                foreach (Thing wall in walls.Where(w => w.Spawned).ToList())
                {
                    if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(wall)) continue;
                    JobDriver_Deconstruct.Deconstruct(wall);
                    took = true;
                }
            }
            while (took);
            List<Thing> holding = walls.Where(w => w.Spawned).ToList();
            Assert.NotEmpty(holding);

            Pawn worker = citizens.First(p => !p.Downed);
            MoveTo(worker, new IntVec3(hall.minX - 1, 0, hall.minZ - 1), map);
            var giver = (WorkGiver_DeconstructRenewal)DefDatabase<SimWorld.Work.WorkGiverDef>.GetNamed("DeconstructRenewal").Worker;
            Assert.All(holding, w => Assert.False(giver.HasJobOnThing(worker, w), "the settlement is handed a wall the mountain rests on"));

            // And a job that reaches one anyway (handed out before a neighbour went) stops short of it.
            Thing target = holding[0];
            var job = new Job(DesignationJobDefOf.Deconstruct, target);
            worker.jobs.StartJob(job);
            for (int i = 0; i < GenDate.TicksPerHour * 2 && worker.jobs.curJob == job; i++) Find.TickManager.DoSingleTick();

            int rocks = map.listerThings.ThingsOfDef(RoofCollapseDefOf.CollapsedRocks).Count(t => hall.Contains(t.Position));
            output.WriteLine($"walls {walls.Count}, holding {holding.Count}, target standing {target.Spawned}, collapsed rock {rocks}");
            Assert.True(target.Spawned, "the last wall holding the mountain came down");
            Assert.Equal(0, rocks);
            Assert.All(hall.Cells, c => Assert.True(map.roofGrid.Roofed(c)));
        }
    }
}
