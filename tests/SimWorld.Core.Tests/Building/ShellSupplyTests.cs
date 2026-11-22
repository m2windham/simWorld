using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Scarce material closes a house before it furnishes one or goes into storage
    /// (<see cref="WorkGiver_ConstructDeliverResourcesToShells"/>). Two dug pit houses stood open through a heat
    /// wave for want of four logs each, while the only tree that grew went into spare beds and a storage hut nearer
    /// the builder.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ShellSupplyTests : ContentTestBase
    {
        private static readonly IntVec3 HouseCorner = new IntVec3(30, 0, 10);
        private static readonly IntVec3 HutCell = new IntVec3(6, 0, 6);
        private static readonly IntVec3 LogCell = new IntVec3(5, 0, 3);

        public ShellSupplyTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>A pit house dug but for its door and beds, all three still to be supplied, a storage hut begun
        /// beside the builder, and <paramref name="logs"/> logs on the ground.</summary>
        private static (CoreMap, Dwelling, Pawn) Site(int logs, bool doorIsFrame, string doorStuff = "WoodLog")
        {
            var map = new CoreMap(44, 30, TerrainDefOf.Soil);
            DwellingDef def = ShelterKit.Rung("House_Meso");
            var dwelling = new Dwelling(def, new CellRect(HouseCorner.x, HouseCorner.z, def.Size.x, def.Size.z), Rot4.North);
            ShelterKit.Compound(map).dwellings.Add(dwelling);
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 rot) in dwelling.Parts)
            {
                if (kind == DwellingPartKind.Wall)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.WallEarth), cell, map, rot);
                    continue;
                }
                ThingDef part = kind == DwellingPartKind.Door ? CompoundThingDefOf.Door : ConstructionThingDefOf.Bed;
                Blueprint bp = Plan(map, part, cell, rot, kind == DwellingPartKind.Door ? doorStuff : "WoodLog");
                if (kind == DwellingPartKind.Door && doorIsFrame) bp.ReplaceWithFrame();
            }
            Plan(map, ConstructionThingDefOf.StorageHut, HutCell, Rot4.North, null).ReplaceWithFrame();

            if (logs > 0)
            {
                Thing wood = ThingMaker.MakeThing(Wood);
                wood.stackCount = logs;
                GenSpawn.Spawn(wood, LogCell, map);
            }
            Pawn builder = NewHuman("Builder");
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;
            GenSpawn.Spawn(builder, new IntVec3(3, 0, 3), map);
            return (map, dwelling, builder);
        }

        private static Blueprint Plan(CoreMap map, ThingDef def, IntVec3 cell, Rot4 rot, string? stuff)
        {
            var bp = (Blueprint)ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(def)!);
            if (stuff != null) bp.stuffToUse = DefDatabase<ThingDef>.GetNamed(stuff);
            GenSpawn.Spawn(bp, cell, map, rot);
            return bp;
        }

        private static ThingDef Wood => DefDatabase<ThingDef>.GetNamed("WoodLog");

        private static int DoorCost => CompoundThingDefOf.Door.costStuffCount;

        private static bool DoorStands(CoreMap map, Dwelling d) =>
            d.Parts.Where(p => p.Kind == DwellingPartKind.Door).All(p => Dwelling.IsBuilt(map, p.Kind, p.Cell));

        private static int LogsIn(CoreMap map, ThingDef entity)
        {
            int n = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                if (t is Frame f && ReferenceEquals(f.EntityToBuild, entity))
                {
                    n += f.TotalMaterialCost().Where(c => ReferenceEquals(c.thingDef, Wood)).Sum(c => c.count - f.MaterialStillNeeded(c.thingDef));
                }
            }
            return n;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_last_logs_hang_the_door_before_they_go_into_a_bed_or_a_nearer_hut(bool doorIsFrame)
        {
            (CoreMap map, Dwelling house, Pawn builder) = Site(DoorCost, doorIsFrame);

            RunTicks(6000, builder);

            Assert.True(DoorStands(map, house));
            Assert.Equal(0, LogsIn(map, ConstructionThingDefOf.StorageHut));
            Assert.Equal(0, LogsIn(map, ConstructionThingDefOf.Bed));
        }

        /// <summary>An order, not a reserve: the logs a door has no use for are not kept back.</summary>
        [Fact]
        public void What_the_shell_does_not_need_goes_on_to_the_other_sites()
        {
            (CoreMap map, Dwelling house, Pawn builder) = Site(DoorCost + 10, doorIsFrame: false);

            RunTicks(6000, builder);

            Assert.True(DoorStands(map, house));
            Assert.Equal(10, LogsIn(map, ConstructionThingDefOf.StorageHut) + LogsIn(map, ConstructionThingDefOf.Bed)
                + house.Beds(map).Count * ConstructionThingDefOf.Bed.costStuffCount);
        }

        /// <summary>A shell nobody can supply holds nothing up: here its door wants steel and there is none.</summary>
        [Fact]
        public void A_shell_that_cannot_be_supplied_does_not_hold_the_wood()
        {
            (CoreMap map, Dwelling house, Pawn builder) = Site(10, doorIsFrame: false, doorStuff: "Steel");

            RunTicks(6000, builder);

            Assert.False(DoorStands(map, house));
            Assert.Equal(10, LogsIn(map, ConstructionThingDefOf.StorageHut));
        }
    }
}
