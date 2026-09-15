using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The whole path a host actually walks: found a civilization, open a settlement through
    /// <c>God/View</c>, and ask <c>Map/View</c> what is in it. Every other test in this folder builds a map by
    /// hand; this one asserts that a <i>generated</i> settlement interior — the real thing, with its thousands
    /// of rocks and its wild growth — comes back as something a renderer could put on screen.
    ///
    /// <para/>The assertions are bands and invariants rather than counts. A generated map's exact contents are
    /// the map generator's business and change when it does; what must hold is that the host is never handed a
    /// map it cannot draw.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewIntegrationTests : ContentTestBase
    {
        public MapViewIntegrationTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
            MapViewTracker.ResetSessionIdCounter();
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        /// <summary>
        /// The one that would have caught the gap this seam was built to close: before <c>Map/View</c> the
        /// host could open a settlement and then had nothing to ask about it at all.
        /// </summary>
        [Fact]
        public void An_opened_settlement_describes_a_map_the_host_could_draw()
        {
            Game game = NewSoloGame("map-view-drawable");
            Settlement settlement = PlayerSettlement(game);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(settlement.tile);

            Assert.True(snapshot.ContentLoaded);
            Assert.True(snapshot.HasMap);
            Assert.Equal(settlement.tile, snapshot.Tile);
            Assert.Equal(settlement.name, snapshot.SettlementName);
            Assert.Equal(game.TickManager.TicksGame, snapshot.TicksGame);

            CoreMap map = settlement.InteriorMap!;
            Assert.Equal(map.Size.x, snapshot.SizeX);
            Assert.Equal(map.Size.z, snapshot.SizeZ);

            // 1. Terrain for every cell, and every one of them a terrain that exists in content — a host
            //    drawing a floor tile per cell can look up every single one.
            Assert.Equal(map.cellIndices.NumGridCells, snapshot.Terrain.Cells.Count);
            Assert.NotEmpty(snapshot.Terrain.Palette);
            foreach (string terrain in snapshot.Terrain.Palette)
            {
                Assert.NotNull(DefDatabase<TerrainDef>.GetNamedSilentFail(terrain));
            }
            Assert.All(snapshot.Terrain.Cells, i => Assert.InRange(i, 0, snapshot.Terrain.Palette.Count - 1));

            // 2. Things, with resolvable defNames and positions inside the map. A generated tribal interior is
            //    overwhelmingly rock, so "many" is a safe band without pinning a generator's output.
            var things = snapshot.AllThings().ToList();
            Assert.True(things.Count > 100, "a generated interior held only " + things.Count + " things");
            foreach (ThingView thing in things)
            {
                Assert.NotNull(DefDatabase<ThingDef>.GetNamedSilentFail(thing.DefName));
                Assert.True(GenGrid.InBounds(thing.Position, map), thing.DefName + " is at " + thing.Position + ", off the map");
                Assert.True(thing.OccupiedSize.x >= 1 && thing.OccupiedSize.z >= 1);
                Assert.InRange(thing.HealthFraction, 0f, 1f);
                Assert.NotEqual(ThingCategory.Pawn, thing.Category);
            }

            // Every Thing the map holds that is not a pawn is in the snapshot exactly once: the chunk walk
            // must neither drop a Thing straddling a chunk edge nor emit a multi-cell one twice.
            int nonPawnThings = map.listerThings.AllThings.Count(t => t.def.category != ThingCategory.Pawn);
            Assert.Equal(nonPawnThings, things.Count);
            Assert.Equal(things.Count, things.Select(t => t.ThingId).Distinct().Count());

            // 3. Pawns, positioned. An opened settlement is not an empty town (see OpenSettlementTests), so
            //    somebody is standing on it and the host has somebody to draw.
            Assert.NotEmpty(snapshot.Pawns);
            Assert.Equal(map.mapPawns.AllPawnsSpawned.Count, snapshot.Pawns.Count);
            foreach (PawnView pawn in snapshot.Pawns)
            {
                Assert.True(GenGrid.InBounds(pawn.Position, map), pawn.Label + " is at " + pawn.Position + ", off the map");
                Assert.NotNull(DefDatabase<ThingDef>.GetNamedSilentFail(pawn.DefName));
                Assert.False(string.IsNullOrWhiteSpace(pawn.Label));
                Assert.InRange(pawn.HealthFraction, 0f, 1f);
                foreach (string apparel in pawn.ApparelDefNames)
                {
                    Assert.NotNull(DefDatabase<ThingDef>.GetNamedSilentFail(apparel));
                }
            }
        }

        /// <summary>
        /// The seam's cost claim, asserted as a trend rather than a time: a settlement that has been ticking
        /// sends back a small fraction of its chunks, not all of them. This is what makes a per-frame read
        /// affordable at all, and it is the property that would quietly stop holding if something started
        /// dirtying chunks it should not — a pawn's step, most likely.
        /// </summary>
        [Fact]
        public void A_ticking_settlement_re_sends_a_small_fraction_of_its_chunks()
        {
            Game game = NewSoloGame("map-view-incremental");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);

            MapViewVersions held = MapViewSnapshot.Capture(settlement.tile).Versions;
            int totalChunks = settlement.InteriorMap!.mapView.ChunkCount;

            for (int i = 0; i < 500; i++) game.TickManager.DoSingleTick();

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(settlement.tile, held);

            Assert.False(delta.FullResync);
            Assert.True(delta.ChangedChunks.Count < totalChunks / 2,
                "500 ticks dirtied " + delta.ChangedChunks.Count + " of " + totalChunks
                + " chunks — something is treating a pawn's step, or a tick, as a map change");

            // The pawns are always there, which is the other half of the bargain: they are re-read whole
            // precisely so they never have to dirty a chunk.
            Assert.NotEmpty(delta.Pawns);
            Assert.Equal(settlement.InteriorMap.mapPawns.AllPawnsSpawned.Count, delta.Pawns.Count);
        }

        /// <summary>
        /// The seam is reachable from nothing but a world tile — the same handle <c>GodCommands</c> takes —
        /// and it explains itself when there is nothing there. A host that has to distinguish "not generated
        /// yet" from "generated and empty" has <c>SettlementSummary.HasInteriorMap</c> for the first and this
        /// for the second, and neither one throws.
        /// </summary>
        [Fact]
        public void A_settlement_with_no_interior_says_so_rather_than_throwing()
        {
            Game game = NewSoloGame("map-view-no-interior");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.ClearSettlementFocus();
            Assert.Null(settlement.InteriorMap);

            MapViewSnapshot before = MapViewSnapshot.Capture(settlement.tile);

            Assert.False(before.HasMap);
            Assert.Equal(settlement.name, before.SettlementName);
            Assert.Contains(settlement.name, before.AbsenceReason!);
            Assert.Empty(before.Chunks);

            // A tile with nothing on it is a different answer, because it calls for a different thing on
            // screen: clear the selection rather than offer to open a town.
            IReadOnlyList<int> occupied = game.World!.worldObjects.OfType<Settlement>().Select(s => s.tile).ToList();
            int emptyTile = Enumerable.Range(0, 10_000).First(t => !occupied.Contains(t));
            MapViewSnapshot nowhere = MapViewSnapshot.Capture(emptyTile);

            Assert.False(nowhere.HasMap);
            Assert.Null(nowhere.SettlementName);
            Assert.NotEqual(before.AbsenceReason, nowhere.AbsenceReason);

            // And once it is opened, the same tile answers.
            GodCommands.OpenSettlement(settlement.tile);
            Assert.True(MapViewSnapshot.Capture(settlement.tile).HasMap);
        }
    }
}
