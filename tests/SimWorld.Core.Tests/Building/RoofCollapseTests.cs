using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Roof support flood-fill and collapse (system 16: Building — roof collapse).</summary>
    public class RoofCollapseTests : ContentTestBase
    {
        public RoofCollapseTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static global::SimWorld.Building.Building SpawnWall(CoreMap map, IntVec3 cell)
        {
            var wall = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, cell, map);
            return wall;
        }

        private static Thing SpawnRock(CoreMap map, IntVec3 cell)
        {
            Thing rock = ThingMaker.MakeThing(Def("Sandstone"));
            GenSpawn.Spawn(rock, cell, map);
            return rock;
        }

        [Fact]
        public void A_roofed_cell_far_from_any_remaining_support_collapses_when_its_only_support_is_removed()
        {
            CoreMap map = NewMap(20, 3);
            SpawnWall(map, new IntVec3(0, 0, 1)); // P1: permanent support
            SpawnWall(map, new IntVec3(3, 0, 1)); // P3: permanent support, covers a nearby band
            global::SimWorld.Building.Building p2 = SpawnWall(map, new IntVec3(10, 0, 1)); // removed below

            for (int x = 1; x <= 13; x++)
            {
                if (x == 3) continue; // P3's own cell
                map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            }

            var far = new IntVec3(13, 0, 1);
            Thing sitTest = ThingMaker.MakeThing(Def("WoodLog"));
            sitTest.HitPoints = 1000;
            GenSpawn.Spawn(sitTest, far, map);

            Assert.True(map.roofGrid.Roofed(far));

            p2.Destroy();

            Assert.False(map.roofGrid.Roofed(far), "A roofed cell 3 tiles from the removed pillar and 10 from any remaining wall should have collapsed.");
            Assert.True(sitTest.HitPoints < 1000, "Whatever was under the collapsing cell should have taken damage.");
        }

        [Fact]
        public void A_roofed_cell_still_close_to_a_remaining_wall_keeps_its_roof()
        {
            CoreMap map = NewMap(20, 3);
            SpawnWall(map, new IntVec3(0, 0, 1)); // P1
            SpawnWall(map, new IntVec3(3, 0, 1)); // P3: stays — supports the near band
            global::SimWorld.Building.Building p2 = SpawnWall(map, new IntVec3(10, 0, 1)); // removed below

            for (int x = 1; x <= 13; x++)
            {
                if (x == 3) continue;
                map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            }

            var near = new IntVec3(6, 0, 1); // 3 from P3 (stays), 4 from the despawning P2 — a real candidate
            Assert.True(map.roofGrid.Roofed(near));

            p2.Destroy();

            Assert.True(map.roofGrid.Roofed(near), "A cell still within radius of a wall that was never touched should not collapse.");
        }

        [Fact]
        public void Removing_a_wall_with_no_roof_anywhere_does_nothing()
        {
            CoreMap map = NewMap(5, 5);
            global::SimWorld.Building.Building wall = SpawnWall(map, new IntVec3(2, 0, 2));
            wall.Destroy(); // must not throw even though nothing is roofed
            Assert.False(map.roofGrid.Roofed(new IntVec3(2, 0, 2)));
        }

        /// <summary>
        /// RimWorld's support reach is a flood fill <i>through roofed cells</i>, not a straight radius: a wall
        /// on the far side of open sky holds up nothing. This port used a plain radius query, so two unrelated
        /// roofed areas propped each other up across a gap that had no ceiling in it at all.
        /// </summary>
        [Fact]
        public void Support_does_not_reach_across_a_gap_with_no_roof_over_it()
        {
            CoreMap map = NewMap(20, 3);
            SpawnWall(map, new IntVec3(2, 0, 1));
            map.roofGrid.SetRoof(new IntVec3(3, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);

            // Four cells of open sky, then a lone roofed cell well inside a straight-line radius of the wall.
            var isolated = new IntVec3(8, 0, 1);
            map.roofGrid.SetRoof(isolated, SimWorld.Map.RoofDefOf.RoofConstructed);

            Assert.True(RoofCollapseUtility.WithinRangeOfRoofHolder(new IntVec3(3, 0, 1), map),
                "a roofed cell next to the wall is held up by it");
            Assert.False(RoofCollapseUtility.WithinRangeOfRoofHolder(isolated, map),
                "a roofed cell six tiles from the wall with no roof in between is not held up by it — the load path runs along the ceiling");
        }

        /// <summary>
        /// The distance itself, pinned as a band around RimWorld's sourced 6.9 rather than as that literal:
        /// six cells of roof away from a wall is held up, eight is not.
        /// </summary>
        [Fact]
        public void Support_reaches_along_the_roof_for_several_cells_and_then_stops()
        {
            CoreMap map = NewMap(30, 3);
            SpawnWall(map, new IntVec3(0, 0, 1));
            for (int x = 1; x <= 20; x++) map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);

            Assert.True(RoofCollapseUtility.WithinRangeOfRoofHolder(new IntVec3(6, 0, 1), map));
            Assert.False(RoofCollapseUtility.WithinRangeOfRoofHolder(new IntVec3(8, 0, 1), map));
        }

        /// <summary>
        /// A roofed area cut off from every holder falls as one, not one ring of it at a time (RimWorld:
        /// <c>RoofCollapseCellsFinder.CheckCollapseFlyingRoofs</c>). This port had no answer to that question,
        /// so roof left hanging beyond the radial check simply stayed up for ever.
        /// </summary>
        [Fact]
        public void A_roof_severed_from_every_holder_falls_all_at_once()
        {
            CoreMap map = NewMap(40, 3);
            global::SimWorld.Building.Building onlySupport = SpawnWall(map, new IntVec3(0, 0, 1));
            for (int x = 1; x <= 30; x++) map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);

            onlySupport.Destroy();

            for (int x = 1; x <= 30; x++)
            {
                Assert.False(map.roofGrid.Roofed(new IntVec3(x, 0, 1)),
                    "cell " + x + " was still roofed: a ceiling with nothing holding it up anywhere along its length has to come down all of it");
            }
        }

        [Fact]
        public void A_thick_roof_collapse_leaves_rock_and_keeps_its_roof_while_an_ordinary_one_vanishes()
        {
            CoreMap map = NewMap(30, 30);
            var thinCell = new IntVec3(3, 0, 3);
            var thickCell = new IntVec3(20, 0, 20);
            map.roofGrid.SetRoof(thinCell, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.roofGrid.SetRoof(thickCell, SimWorld.Map.RoofDefOf.RoofRockThick);

            Thing underThin = ThingMaker.MakeThing(Def("WoodLog"));
            underThin.HitPoints = 1000;
            GenSpawn.Spawn(underThin, thinCell, map);
            Thing underThick = ThingMaker.MakeThing(Def("WoodLog"));
            underThick.HitPoints = 1000;
            GenSpawn.Spawn(underThick, thickCell, map);

            // Neither cell has any support at all (no walls anywhere on this map), so calling the collapse
            // check directly with each as its own "vacated footprint" collapses it deterministically — the
            // point here is what each kind of roof does, not the despawn-triggered wiring (covered above).
            RoofCollapseUtility.Notify_RoofHolderDespawned(CellRect.SingleCell(thinCell), map);
            RoofCollapseUtility.Notify_RoofHolderDespawned(CellRect.SingleCell(thickCell), map);

            Assert.False(map.roofGrid.Roofed(thinCell), "an ordinary roof vanishes when it falls");
            Assert.True(underThin.HitPoints < 1000 && !underThin.Destroyed,
                "an ordinary roof bruises what is under it rather than obliterating it");

            Assert.True(map.roofGrid.Roofed(thickCell),
                "overhead mountain does not go away, however many times it comes down (RoofDef.VanishOnCollapse is !isThickRoof)");
            Assert.True(underThick.Destroyed, "a mountain destroys what is under it");
            Thing? leftBehind = map.edificeGrid[thickCell];
            Assert.NotNull(leftBehind);
            Assert.Equal("CollapsedRocks", leftBehind!.def.defName);
            Assert.True(RoofCollapseUtility.HoldsRoof(thickCell, map),
                "and what it leaves holds the roof it left behind up again, which is what stops a collapse walking across a mountain");
        }

        /// <summary>
        /// The signature <c>docs/WORK-REGISTER.md</c> §10 reported and could not explain: "a destroyed heart,
        /// brain or liver on a corpse whose total injury severity is near zero". A roof hit with no body region
        /// set draws a part by coverage over the whole body, inside parts included, and one 50-point hit on a
        /// heart is a dead colonist. RimWorld's ordinary roof hits <see cref="BodyPartHeight.Top"/> /
        /// <see cref="BodyPartDepth.Outside"/> and cannot reach an organ at all.
        /// </summary>
        [Fact]
        public void An_ordinary_roof_falling_on_a_pawn_cannot_destroy_an_internal_organ()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(5, 0, 5);

            int survived = 0;
            const int trials = 40;
            for (int i = 0; i < trials; i++)
            {
                Pawn pawn = NewHuman("Crushed" + i);
                GenSpawn.Spawn(pawn, cell, map);
                map.roofGrid.SetRoof(cell, SimWorld.Map.RoofDefOf.RoofConstructed);

                RoofCollapserImmediate.DropRoofInCells(cell, map);

                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff.Part == null) continue;
                    Assert.True(hediff.Part.depth != BodyPartDepth.Inside,
                        "an ordinary roof reached " + hediff.Part.Label + ", which is inside the body");
                }
                if (!pawn.Dead) survived++;
                if (!pawn.Destroyed) pawn.Destroy();
            }

            Assert.True(survived * 2 > trials,
                "only " + survived + " of " + trials + " healthy colonists walked away from a constructed roof falling on them — RimWorld's is 15–30 damage to the top of the body, not a death sentence");
        }

        /// <summary>
        /// And the other half of RimWorld's branch: overhead mountain is not a damage roll, it is a death.
        /// The death has to be attributable to <i>nothing</i> — no instigator — and to read as a crushing.
        /// </summary>
        [Fact]
        public void A_mountain_falling_on_a_pawn_kills_it_and_names_no_killer()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(5, 0, 5);
            Pawn pawn = NewHuman("Miner");
            GenSpawn.Spawn(pawn, cell, map);
            map.roofGrid.SetRoof(cell, SimWorld.Map.RoofDefOf.RoofRockThick);

            RoofCollapserImmediate.DropRoofInCells(cell, map);

            Assert.True(pawn.Dead, "nothing walks out from under overhead mountain");
            Assert.False(pawn.health.KilledByAnotherPawn, "a ceiling is not a killer");
            Assert.NotNull(pawn.health.DeathCauseDamage);
            Assert.Equal(RoofCollapseDefOf.Crush, pawn.health.DeathCauseDamage);
        }

        /// <summary>
        /// The whole of §9a's misreading, pinned in one line. The letter a crushed citizen produces used to say
        /// "beaten to death" because the collapse dealt <c>Blunt</c>, and two batches of work went looking for
        /// a murderer on the strength of it.
        /// </summary>
        [Fact]
        public void The_damage_a_collapse_deals_does_not_report_itself_as_a_beating()
        {
            Assert.Equal("Crush", RoofCollapseDefOf.Crush.defName);
            Assert.Contains("crushed", RoofCollapseDefOf.Crush.deathMessage!);
            Assert.DoesNotContain("beaten", RoofCollapseDefOf.Crush.deathMessage!);
        }

        /// <summary>
        /// The translation this lane added: with no player to designate mining and no overlay to read, the
        /// miner itself has to refuse the cells holding a ceiling up. Rock in the middle of rock is fine;
        /// the last rock under a stretch of roof is not.
        /// </summary>
        [Fact]
        public void A_rock_that_is_holding_a_roof_up_is_not_offered_as_mining_work()
        {
            CoreMap map = NewMap(30, 30);

            // A block of rock, all of it roofed thick the way map generation roofs rock.
            var rocks = new List<Thing>();
            for (int x = 10; x <= 16; x++)
            {
                for (int z = 10; z <= 16; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    rocks.Add(SpawnRock(map, c));
                    map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofRockThick);
                }
            }

            Thing inTheMiddle = rocks.First(r => r.Position == new IntVec3(13, 0, 13));
            Assert.False(RoofCollapseUtility.WouldCollapseRoofIfRemoved(inTheMiddle),
                "rock with rock all around it holds up nothing that its neighbours do not also hold up");

            // A single rock alone under its own roof is the only thing holding that roof up.
            var lone = new IntVec3(3, 0, 3);
            Thing loneRock = SpawnRock(map, lone);
            map.roofGrid.SetRoof(lone, SimWorld.Map.RoofDefOf.RoofRockThick);
            Assert.True(RoofCollapseUtility.WouldCollapseRoofIfRemoved(loneRock),
                "the last rock under a stretch of ceiling is exactly the cell a player would not have designated");
        }

        /// <summary>
        /// The module's Scribe round trip. Support and collapse hold no state of their own — there is no
        /// buffer to persist, because the cells condemned by one despawn fall inside that same call — so what
        /// has to survive a save is what a collapse <i>left</i>: the rock in the cell, the thick roof still
        /// over it, and the support that rock now provides.
        /// </summary>
        [Fact]
        public void What_a_collapse_leaves_behind_survives_a_save_and_still_holds_the_roof_up()
        {
            CoreMap map = NewMap(12, 12);
            var cell = new IntVec3(6, 0, 6);
            map.roofGrid.SetRoof(cell, SimWorld.Map.RoofDefOf.RoofRockThick);
            RoofCollapserImmediate.DropRoofInCells(cell, map);
            Assert.Equal("CollapsedRocks", map.edificeGrid[cell]!.def.defName);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out System.Collections.Generic.IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Same(SimWorld.Map.RoofDefOf.RoofRockThick, loaded.roofGrid.RoofAt(cell));
            Thing? rocks = loaded.edificeGrid[cell];
            Assert.NotNull(rocks);
            Assert.Equal("CollapsedRocks", rocks!.def.defName);
            Assert.True(RoofCollapseUtility.WithinRangeOfRoofHolder(cell, loaded),
                "a loaded save has to remember that the cell is shored up, or the first despawn nearby drops the roof again");
        }

        [Fact]
        public void A_frame_completing_construction_does_not_falsely_collapse_a_roof_it_alone_supports()
        {
            // Regression test for the WillReplace fix: Frame.CompleteConstruction destroys the Frame and
            // spawns the real Building at the same cell in the same call. Between those two lines the cell
            // genuinely holds no edifice for a moment — without DestroyMode.WillReplace telling the roof
            // check to ignore that moment, a roof only this Frame supported would be destroyed for good, even
            // though the finished wall reappears microseconds later.
            CoreMap map = NewMap(10, 10);
            var frameCell = new IntVec3(5, 0, 5);
            var nearbyCell = new IntVec3(6, 0, 5); // adjacent: the frame is its only possible support

            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Wall"));
            frame.AddMaterial(Def("WoodLog"), 5);
            GenSpawn.Spawn(frame, frameCell, map);
            map.roofGrid.SetRoof(nearbyCell, SimWorld.Map.RoofDefOf.RoofConstructed);
            Assert.True(map.roofGrid.Roofed(nearbyCell));

            Thing built = frame.CompleteConstruction(NewHuman());

            Assert.IsType<global::SimWorld.Building.Building>(built);
            Assert.True(map.roofGrid.Roofed(nearbyCell), "Completing a Frame must not trigger a false collapse via its own momentary despawn.");
        }
    }
}
