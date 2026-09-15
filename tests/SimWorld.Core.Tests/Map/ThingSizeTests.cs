using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// <c>ThingDef.size</c> — the footprint a Thing occupies (RimWorld: <c>ThingDef.size</c>, an
    /// <c>IntVec2</c>).
    ///
    /// <para/><b>What was actually missing.</b> The field, <see cref="GenAdj.OccupiedRect"/>, and the
    /// occupancy that reads it (<see cref="ThingGrid"/>, <see cref="EdificeGrid"/>, <c>Thing.Position</c>'s
    /// multi-cell path, <c>Thing.SpawnSetup</c>/<c>DeSpawn</c>'s path-cost sweeps) were all already here and
    /// all already correct. What was missing was the <i>parse</i>: no <c>IntVec2</c> parser was registered, so
    /// <c>&lt;size&gt;(1,2)&lt;/size&gt;</c> in content did not fail — it fell through
    /// <c>XmlObjectMapper</c>'s build-an-object path, found no child elements, and silently produced a
    /// footprint of (0, 0) cells. A def could not set a footprint at all, which is why nothing in content
    /// does.
    ///
    /// <para/><b>What still assumes 1x1</b>, and is deliberately not changed here — see the report in
    /// <c>docs/perf/map-view.md</c>: <c>Building.GenConstruct.CanPlaceBlueprintAt</c> validates one cell, the
    /// hand-authored <c>Blueprint_X</c>/<c>Frame_X</c> defs carry no size of their own, and the settlement
    /// construction initiative places into a single free cell. Setting a footprint on a buildable def before
    /// those three agree would be a field some systems honour and others ignore, which is worse than one
    /// nothing honours yet. So shipped content sets no size, and
    /// <see cref="Shipped_content_sets_no_footprint_yet"/> below is the tripwire for the day it does.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ThingSizeTests : ContentTestBase
    {
        public ThingSizeTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>A throwaway building def of the given footprint, built in code rather than loaded, so the
        /// occupancy tests below exercise the geometry without needing a def in the shipped database.</summary>
        internal static ThingDef DefWithSize(string defName, int x, int z) => new ThingDef
        {
            defName = defName,
            label = defName,
            thingClass = typeof(Thing),
            category = ThingCategory.Building,
            size = new IntVec2(x, z),
            passability = Traversability.Standable,
        };

        private static CoreMap NewMap(int sizeX = 20, int sizeZ = 20) =>
            new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static DefLoadResult Load(string xml) =>
            new DefLoader(new DefDatabase(), new DefTypeResolver(), new DefLoadOptions { BindDefOfs = false })
                .AddXml(xml)
                .Load();

        // ---- the parse, which is the part that was missing ----

        [Fact]
        public void A_def_can_finally_state_a_footprint()
        {
            DefLoadResult result = Load(@"<Defs>
  <ThingDef>
    <defName>SizeProbe</defName>
    <category>Building</category>
    <size>(1,2)</size>
  </ThingDef>
</Defs>");

            Assert.Empty(result.Errors);
            ThingDef def = result.Defs.OfType<ThingDef>().Single();

            // The regression this whole change exists for: before the IntVec2 parser was registered this was
            // (0, 0) — a footprint of no cells — with no error anywhere to say so.
            Assert.Equal(new IntVec2(1, 2), def.size);
            Assert.NotEqual(IntVec2.Zero, def.size);
        }

        [Fact]
        public void A_def_that_states_no_footprint_is_one_by_one()
        {
            DefLoadResult result = Load(@"<Defs>
  <ThingDef>
    <defName>NoSizeProbe</defName>
    <category>Building</category>
  </ThingDef>
</Defs>");

            Assert.Empty(result.Errors);
            Assert.Equal(IntVec2.One, result.Defs.OfType<ThingDef>().Single().size);
        }

        [Fact]
        public void A_footprint_that_is_not_a_pair_of_numbers_is_reported_rather_than_silently_defaulted()
        {
            DefLoadResult result = Load(@"<Defs>
  <ThingDef>
    <defName>BadSizeProbe</defName>
    <category>Building</category>
    <size>two by one</size>
  </ThingDef>
</Defs>");

            // Loudly wrong beats quietly wrong: the failure being fixed here was a bad footprint that loaded
            // clean, and a parser that swallowed its own errors would reintroduce it in a new place.
            Assert.NotEmpty(result.Errors);
        }

        [Theory]
        [InlineData("(1,2)")]
        [InlineData("1,2")]
        [InlineData("  ( 1 , 2 ) ")]
        public void A_footprint_parses_with_or_without_parentheses_and_spacing(string text)
        {
            // RimWorld's own Verse.IntVec2.FromString trims the parentheses and splits on the comma, and
            // content in the wild is written both ways.
            Assert.Equal(new IntVec2(1, 2), IntVec2.FromString(text));
        }

        [Fact]
        public void A_three_axis_vector_parses_the_same_way()
        {
            Assert.Equal(new IntVec3(1, 0, 2), IntVec3.FromString("(1,0,2)"));
            Assert.Throws<FormatException>(() => IntVec3.FromString("(1,2)"));
        }

        // ---- the geometry ----

        [Fact]
        public void A_footprint_rotates_with_the_thing()
        {
            var at = new IntVec3(10, 0, 10);
            var size = new IntVec2(1, 2);

            CellRect north = GenAdj.OccupiedRect(at, Rot4.North, size);
            CellRect east = GenAdj.OccupiedRect(at, Rot4.East, size);

            Assert.Equal(1, north.width);
            Assert.Equal(2, north.height);
            Assert.Equal(2, east.width);
            Assert.Equal(1, east.height);

            // Whatever the facing, the thing's own cell is inside its footprint and the area is the same —
            // the two properties anything reading a footprint depends on.
            Assert.True(north.Contains(at));
            Assert.True(east.Contains(at));
            Assert.Equal(north.Area, east.Area);
        }

        // ---- the occupancy that already honoured it ----

        [Fact]
        public void A_two_cell_building_occupies_both_cells_in_both_grids()
        {
            CoreMap map = NewMap();
            Thing bed = ThingMaker.MakeThing(DefWithSize("TwoCellProbe", 1, 2));
            GenSpawn.Spawn(bed, new IntVec3(10, 0, 10), map);

            var cells = bed.OccupiedRect().Cells.ToList();
            Assert.Equal(2, cells.Count);
            foreach (IntVec3 c in cells)
            {
                Assert.Contains(bed, map.thingGrid.ThingsListAt(c));
                Assert.Same(bed, map.edificeGrid[c]);
            }
        }

        [Fact]
        public void Moving_a_two_cell_building_leaves_no_cell_still_claiming_it()
        {
            CoreMap map = NewMap();
            Thing bed = ThingMaker.MakeThing(DefWithSize("MoveProbe", 1, 2));
            GenSpawn.Spawn(bed, new IntVec3(10, 0, 10), map);
            var vacated = bed.OccupiedRect().Cells.ToList();

            bed.Position = new IntVec3(4, 0, 4);

            foreach (IntVec3 c in vacated)
            {
                Assert.DoesNotContain(bed, map.thingGrid.ThingsListAt(c));
                Assert.NotSame(bed, map.edificeGrid[c]);
            }
            foreach (IntVec3 c in bed.OccupiedRect().Cells)
            {
                Assert.Contains(bed, map.thingGrid.ThingsListAt(c));
                Assert.Same(bed, map.edificeGrid[c]);
            }
        }

        [Fact]
        public void A_footprint_that_would_hang_off_the_map_is_refused_rather_than_half_registered()
        {
            CoreMap map = NewMap(20, 20);
            Thing bed = ThingMaker.MakeThing(DefWithSize("EdgeProbe", 1, 2));

            // The centre cell is in bounds; the second cell of the footprint is not. Both grids silently skip
            // out-of-bounds cells, so without this check the Thing would register in one cell while its own
            // OccupiedRect kept claiming two — a Thing the grids half-remember and never fully release.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => GenSpawn.Spawn(bed, new IntVec3(10, 0, 19), map));
            Assert.False(bed.Spawned);
        }

        [Fact]
        public void A_single_cell_thing_at_the_map_edge_still_spawns()
        {
            // The footprint check must not have tightened the ordinary case: everything in shipped content is
            // 1x1 and a rock on the last row is normal.
            CoreMap map = NewMap(20, 20);
            Thing rock = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Sandstone"));

            GenSpawn.Spawn(rock, new IntVec3(19, 0, 19), map);

            Assert.True(rock.Spawned);
        }

        // ---- the tripwire ----

        /// <summary>
        /// Every shipped ThingDef is 1x1 today, and this records that rather than celebrating it.
        ///
        /// <para/>Whoever first sets a real footprint in content will land here, and that is the intent: this
        /// test is the note telling them what to check before they do. A buildable def with a footprint also
        /// needs its <c>Blueprint_X</c> and <c>Frame_X</c> to carry the same size, and
        /// <c>GenConstruct.CanPlaceBlueprintAt</c> to validate the whole rect rather than one cell — none of
        /// which is done. Deleting this test is the right move once those agree; weakening it quietly is not.
        /// </summary>
        [Fact]
        public void Shipped_content_sets_no_footprint_yet()
        {
            var sized = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.size != IntVec2.One)
                .Select(d => d.defName + " " + d.size)
                .ToList();

            Assert.True(sized.Count == 0,
                "content now sets a footprint (" + string.Join(", ", sized) + ") — GenConstruct.CanPlaceBlueprintAt, "
                + "the Blueprint_/Frame_ defs and the construction initiative still assume 1x1; see ThingSizeTests' own remarks");
        }
    }
}
