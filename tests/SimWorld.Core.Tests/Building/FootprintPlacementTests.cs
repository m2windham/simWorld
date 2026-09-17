using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.Tests.Content;
using SimWorld.Tests.Map;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The three things <c>Map.ThingSizeTests</c> named as still assuming 1x1, now that they agree.
    ///
    /// <para/>Batch eleven found that <c>ThingDef.size</c> never worked — no <c>IntVec2</c> parser was
    /// registered, so <c>&lt;size&gt;(1,2)&lt;/size&gt;</c> silently produced a footprint of (0,0) — and fixed
    /// the parse. It deliberately stopped there, because the field would have been honoured by
    /// <c>ThingGrid</c> and <c>EdificeGrid</c> and ignored by placement, which is worse than a field nothing
    /// honours. Its tripwire test said so and named what to fix: this is that fix.
    ///
    /// <para/><b>Blueprints and frames derive their footprint rather than declaring one.</b> The alternative
    /// was authoring a matching <c>&lt;size&gt;</c> on every hand-written <c>Blueprint_X</c> and
    /// <c>Frame_X</c>, which is the same number written three times and free to drift. RimWorld generates
    /// those defs from the parent and so cannot disagree with it; deriving from <c>entityToBuild</c> gets the
    /// same guarantee without the generator.
    /// </summary>
    [Collection("GlobalDefs")]
    public class FootprintPlacementTests : ContentTestBase
    {
        public FootprintPlacementTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        /// <summary>A blueprint def standing in for <paramref name="built"/>, shaped like the hand-authored
        /// ones in content: it carries <c>entityToBuild</c> and no size of its own.</summary>
        private static ThingDef BlueprintFor(ThingDef built) => new ThingDef
        {
            defName = "Blueprint_" + built.defName,
            label = built.defName + " (blueprint)",
            thingClass = typeof(Blueprint),
            category = ThingCategory.Blueprint,
            passability = Traversability.Standable,
            entityToBuild = built,
        };

        // ---- the validator ----

        [Fact]
        public void A_footprint_blocked_anywhere_but_its_centre_is_refused()
        {
            CoreMap map = NewMap();
            ThingDef twoByTwo = ThingSizeTests.DefWithSize("FootprintBench", 2, 2);

            // Clear: all four cells are free.
            Assert.True(GenConstruct.CanPlaceBlueprintAt(twoByTwo, new IntVec3(5, 0, 5), map, out _));

            // Block a cell the footprint covers but is NOT centred on. Before this change the centre alone
            // was checked, so this placement was accepted and two buildings overlapped.
            ThingDef wall = ThingSizeTests.DefWithSize("FootprintWall", 1, 1);
            GenSpawn.Spawn(ThingMaker.MakeThing(wall), new IntVec3(6, 0, 6), map);

            Assert.False(GenConstruct.CanPlaceBlueprintAt(twoByTwo, new IntVec3(5, 0, 5), map, out string? reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void A_footprint_that_runs_off_the_map_is_refused_even_when_its_centre_is_inside()
        {
            CoreMap map = NewMap(10);
            ThingDef twoByTwo = ThingSizeTests.DefWithSize("EdgeBench", 2, 2);

            // Centre in bounds, the rest of the rect not.
            Assert.False(GenConstruct.CanPlaceBlueprintAt(twoByTwo, new IntVec3(9, 0, 9), map, out string? reason));
            Assert.Equal("out of bounds", reason);
        }

        [Fact]
        public void A_one_by_one_is_unaffected_which_is_every_caller_that_exists_today()
        {
            CoreMap map = NewMap();
            ThingDef single = ThingSizeTests.DefWithSize("SingleCell", 1, 1);

            Assert.True(GenConstruct.CanPlaceBlueprintAt(single, new IntVec3(5, 0, 5), map, out _));

            GenSpawn.Spawn(ThingMaker.MakeThing(single), new IntVec3(5, 0, 5), map);
            Assert.False(GenConstruct.CanPlaceBlueprintAt(single, new IntVec3(5, 0, 5), map, out _));
            Assert.True(GenConstruct.CanPlaceBlueprintAt(single, new IntVec3(6, 0, 5), map, out _));
        }

        // ---- the blueprint's own footprint ----

        [Fact]
        public void A_blueprint_occupies_what_it_will_become_not_the_one_cell_its_def_declares()
        {
            CoreMap map = NewMap();
            ThingDef twoByTwo = ThingSizeTests.DefWithSize("PlannedBench", 2, 2);
            ThingDef blueprintDef = BlueprintFor(twoByTwo);

            Assert.Equal(IntVec2.One, blueprintDef.size);   // the def really does declare nothing

            Thing blueprint = ThingMaker.MakeThing(blueprintDef);
            GenSpawn.Spawn(blueprint, new IntVec3(5, 0, 5), map);

            Assert.Equal(new IntVec2(2, 2), blueprint.Size);
            Assert.Equal(4, blueprint.OccupiedRect().Cells.Count());

            // Registered under every cell of the footprint, not just its position.
            foreach (IntVec3 c in blueprint.OccupiedRect().Cells)
            {
                Assert.Contains(blueprint, map.thingGrid.ThingsListAt(c));
            }
        }

        [Fact]
        public void A_second_blueprint_cannot_be_laid_across_the_first_ones_footprint()
        {
            CoreMap map = NewMap();
            ThingDef twoByTwo = ThingSizeTests.DefWithSize("CrowdedBench", 2, 2);
            ThingDef blueprintDef = BlueprintFor(twoByTwo);

            GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), new IntVec3(5, 0, 5), map);

            // (6,6) is inside the first blueprint's rect but is not its position — the case the single-cell
            // check accepted.
            Assert.False(GenConstruct.CanPlaceBlueprintAt(twoByTwo, new IntVec3(6, 0, 6), map, out string? reason));
            Assert.Equal("something is already planned here", reason);
        }

        [Fact]
        public void A_frame_keeps_the_footprint_its_blueprint_reserved()
        {
            CoreMap map = NewMap();
            ThingDef twoByTwo = ThingSizeTests.DefWithSize("FramedBench", 2, 2);

            var frameDef = new ThingDef
            {
                defName = "Frame_FramedBench",
                label = "framed bench (under construction)",
                thingClass = typeof(Frame),
                category = ThingCategory.Frame,
                passability = Traversability.Standable,
                entityToBuild = twoByTwo,
            };

            Thing frame = ThingMaker.MakeThing(frameDef);
            GenSpawn.Spawn(frame, new IntVec3(5, 0, 5), map);

            Assert.Equal(new IntVec2(2, 2), frame.Size);
            Assert.Equal(4, frame.OccupiedRect().Cells.Count());
        }

        // ---- rotation, which the validator now takes ----

        [Fact]
        public void Rotating_a_footprint_moves_which_cells_are_checked()
        {
            CoreMap map = NewMap();
            ThingDef oneByThree = ThingSizeTests.DefWithSize("LongBench", 1, 3);

            // Block a cell that the East-facing rect covers and the North-facing one does not. Which cell
            // that is follows from GenAdj.OccupiedRect, so find it rather than assert a hand-computed pair.
            var north = GenAdj.OccupiedRect(new IntVec3(9, 0, 9), Rot4.North, oneByThree.size).Cells.ToList();
            var east = GenAdj.OccupiedRect(new IntVec3(9, 0, 9), Rot4.East, oneByThree.size).Cells.ToList();
            IntVec3 onlyEast = east.First(c => !north.Contains(c));

            ThingDef wall = ThingSizeTests.DefWithSize("RotWall", 1, 1);
            GenSpawn.Spawn(ThingMaker.MakeThing(wall), onlyEast, map);

            Assert.True(GenConstruct.CanPlaceBlueprintAt(oneByThree, new IntVec3(9, 0, 9), map, out _, Rot4.North));
            Assert.False(GenConstruct.CanPlaceBlueprintAt(oneByThree, new IntVec3(9, 0, 9), map, out _, Rot4.East));
        }
    }
}
