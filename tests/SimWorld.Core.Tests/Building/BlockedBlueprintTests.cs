using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A frame never goes up around a pawn (<see cref="GenConstruct.FirstBlockingPawn"/>). Until the map had
    /// wood, no storage hut was ever built and this could not happen; once it had, seed 777 of the storyteller
    /// bench walled a citizen into a hut on its first day, and his empty job search every tick slowed the whole
    /// simulation from under a millisecond a tick to 46. An impassable building waits for the pawn to move; one
    /// that can be walked through does not.
    /// </summary>
    public class BlockedBlueprintTests : ContentTestBase
    {
        public BlockedBlueprintTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap() => new CoreMap(14, 14, SimWorld.Map.TerrainDefOf.Soil);

        private static Blueprint SpawnBlueprint(CoreMap map, IntVec3 cell, string defName)
        {
            var bp = (Blueprint)ThingMaker.MakeThing(Def("Blueprint_" + defName));
            GenSpawn.Spawn(bp, cell, map);
            return bp;
        }

        private static Pawn SpawnBuilderWithWood(CoreMap map)
        {
            Pawn builder = NewHuman("Builder");
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;
            GenSpawn.Spawn(builder, new IntVec3(1, 0, 1), map);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 60;
            GenSpawn.Spawn(wood, new IntVec3(2, 0, 1), map);
            return builder;
        }

        private static Pawn SpawnDowned(CoreMap map, IntVec3 cell)
        {
            Pawn lying = NewHuman("Lying");
            GenSpawn.Spawn(lying, cell, map);
            lying.health.ForceDowned = true;
            return lying;
        }

        [Fact]
        public void A_pawn_blocks_an_impassable_blueprint_and_not_one_that_can_be_walked_through()
        {
            CoreMap map = NewMap();
            Blueprint hut = SpawnBlueprint(map, new IntVec3(6, 0, 6), "StorageHut");
            Blueprint bed = SpawnBlueprint(map, new IntVec3(9, 0, 9), "Bed");
            Assert.Null(GenConstruct.FirstBlockingPawn(hut, null));

            Pawn onHut = SpawnDowned(map, hut.Position);
            SpawnDowned(map, bed.Position);

            Assert.Same(onHut, GenConstruct.FirstBlockingPawn(hut, null));
            Assert.Null(GenConstruct.FirstBlockingPawn(hut, onHut)); // the one delivering is not in its own way
            Assert.Null(GenConstruct.FirstBlockingPawn(bed, null));
        }

        [Fact]
        public void A_storage_hut_waits_for_the_pawn_lying_on_its_blueprint_and_is_built_once_they_are_gone()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            SpawnBlueprint(map, cell, "StorageHut");
            Pawn lying = SpawnDowned(map, cell);
            Pawn builder = SpawnBuilderWithWood(map);

            RunTicks(3000, builder);

            Assert.True(GenGrid.Walkable(lying.Position, map), "No frame may go up around a pawn: it would wall them in.");
            Assert.Empty(map.listerThings.ThingsOfDef(ConstructionThingDefOf.StorageHut));
            Assert.Contains(map.thingGrid.ThingsListAt(cell), t => t is Blueprint);

            // Carried off (a rescue, say): now the hut goes up.
            lying.DeSpawn();
            RunTicks(4000, builder);

            Assert.Single(map.listerThings.ThingsOfDef(ConstructionThingDefOf.StorageHut));
        }

        /// <summary>The delivering pawn is not counted as in its own way (RimWorld's <c>pawnToIgnore</c>), so a
        /// hauler that starts its job on the site must not end it inside the frame it raised: whether its path
        /// takes it off the site first or it delivers from where it stands, it finishes on open ground. Both ways a
        /// hauler can be on the site when its job begins: walking to logs that lie there, and already holding them
        /// (a save taken mid-carry).</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_hauler_that_starts_on_the_site_is_not_walled_in_by_its_own_frame(bool alreadyCarrying)
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            Blueprint wall = SpawnBlueprint(map, cell, "Wall");
            Pawn builder = NewHuman("Builder");
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;
            GenSpawn.Spawn(builder, cell, map);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = Def("Wall").CostListCountFor(Def("WoodLog"));
            GenSpawn.Spawn(wood, cell, map);
            if (alreadyCarrying)
            {
                builder.carryTracker.TryStartCarry(wood, wood.stackCount);
                builder.jobs.StartJob(new Job(JobDefOf.HaulToBuildingSite, builder.carryTracker.CarriedThing!, wall));
            }

            RunTicks(3000, builder);

            Assert.Single(map.listerThings.ThingsOfDef(Def("Wall")));
            Assert.True(GenGrid.Walkable(builder.Position, map), "The builder must not end up inside the wall it built.");
        }

        /// <summary>The step aside itself, driven directly: the in-play case that needed it (a hauler that picked its
        /// logs up from a storage hut's own blueprint and delivered from that cell) depends on a path the scenes
        /// above do not reproduce.</summary>
        [Fact]
        public void A_pawn_left_inside_a_frame_it_raised_steps_to_open_ground_and_one_beside_it_stays_put()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            Blueprint hut = SpawnBlueprint(map, cell, "StorageHut");
            Pawn inside = NewHuman("Inside");
            GenSpawn.Spawn(inside, cell, map);
            Pawn beside = NewHuman("Beside");
            GenSpawn.Spawn(beside, new IntVec3(7, 0, 6), map);

            hut.ReplaceWithFrame();
            Assert.False(GenGrid.Walkable(inside.Position, map));

            Assert.True(GenConstruct.StepOffUnwalkableCell(inside));
            Assert.True(GenGrid.Standable(inside.Position, map));
            Assert.True((inside.Position - cell).LengthHorizontalSquared <= 2, "The nearest open cell is next to the frame.");

            Assert.False(GenConstruct.StepOffUnwalkableCell(beside));
            Assert.Equal(new IntVec3(7, 0, 6), beside.Position);
        }

        [Fact]
        public void A_bed_is_built_even_with_a_pawn_lying_on_its_blueprint()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            SpawnBlueprint(map, cell, "Bed");
            Pawn lying = SpawnDowned(map, cell);
            Pawn builder = SpawnBuilderWithWood(map);

            RunTicks(3000, builder);

            Assert.Single(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed));
            Assert.True(GenGrid.Walkable(lying.Position, map));
        }
    }
}
