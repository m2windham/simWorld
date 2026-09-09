using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Tests.Content;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Combat
{
    /// <summary>CoverUtility's geometry — what actually stands between shooter and target — wired to a real map (system 12: Combat — cover).</summary>
    public class CoverTests : ContentTestBase
    {
        public CoverTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnWall(CoreMap map, IntVec3 cell)
        {
            Thing wall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, cell, map);
            return wall;
        }

        // ---- pure geometry ----

        [Fact]
        public void A_wall_squarely_between_shooter_and_target_gives_strong_cover()
        {
            CoreMap map = NewMap(10, 10);
            var target = new IntVec3(5, 0, 5);
            var shooter = new IntVec3(0, 0, 5);
            SpawnWall(map, new IntVec3(4, 0, 5)); // cardinally adjacent to target, directly toward the shooter

            List<CoverInfo> cover = CoverUtility.CalculateCoverGiverSet(target, shooter, map);
            CoverInfo info = Assert.Single(cover);
            Assert.True(info.blockChance > 0.5f, $"expected strong cover, got {info.blockChance}");
        }

        [Fact]
        public void No_cover_giving_thing_nearby_yields_an_empty_set()
        {
            CoreMap map = NewMap(10, 10);
            List<CoverInfo> cover = CoverUtility.CalculateCoverGiverSet(new IntVec3(5, 0, 5), new IntVec3(0, 0, 5), map);
            Assert.Empty(cover);
        }

        [Fact]
        public void Cover_badly_misaligned_with_the_shooters_line_counts_for_nothing()
        {
            CoreMap map = NewMap(10, 10);
            var target = new IntVec3(5, 0, 5);
            var shooter = new IntVec3(0, 0, 5); // due west of target
            SpawnWall(map, new IntVec3(5, 0, 6)); // due north of target: ~90 degrees off the shooter's line

            List<CoverInfo> cover = CoverUtility.CalculateCoverGiverSet(target, shooter, map);
            Assert.Empty(cover);
        }

        [Fact]
        public void The_target_itself_is_never_counted_as_its_own_cover()
        {
            CoreMap map = NewMap(10, 10);
            var targetCell = new IntVec3(5, 0, 5);
            var shooter = new IntVec3(0, 0, 5);
            Pawn target = SpawnHuman(map, targetCell);
            // The target's own cell can't be one of the 8 adjacent cells, but a second Thing sharing the
            // exact cell (a downed target under something) still must not count itself as cover for itself.
            List<CoverInfo> cover = CoverUtility.CalculateCoverGiverSet(targetCell, shooter, map, target);
            Assert.DoesNotContain(cover, c => ReferenceEquals(c.thing, target));
        }

        [Fact]
        public void Cover_right_next_to_the_shooter_is_weaker_than_the_same_cover_farther_off()
        {
            float BlockChanceAt(IntVec3 shooter)
            {
                CoreMap map = NewMap(20, 20);
                var target = new IntVec3(10, 0, 10);
                SpawnWall(map, new IntVec3(9, 0, 10));
                List<CoverInfo> cover = CoverUtility.CalculateCoverGiverSet(target, shooter, map);
                return Assert.Single(cover).blockChance;
            }

            float closeUp = BlockChanceAt(new IntVec3(9, 0, 9)); // one cell off — inside the point-blank band
            float farAway = BlockChanceAt(new IntVec3(0, 0, 10));
            Assert.True(closeUp < farAway, $"point-blank cover ({closeUp}) should block less than the same cover from range ({farAway})");
        }

        // ---- wired into an actual shot ----

        [Fact]
        public void Cover_lowers_the_reported_hit_chance_for_a_real_spawned_shot()
        {
            (ShotReport report, Pawn shooter, Pawn target) Setup(bool withWall)
            {
                CoreMap map = NewMap(10, 10);
                Pawn shooter = SpawnHuman(map, new IntVec3(0, 0, 5), "Shooter");
                Pawn target = SpawnHuman(map, new IntVec3(5, 0, 5), "Target");
                if (withWall) SpawnWall(map, new IntVec3(4, 0, 5));

                ThingDef revolver = Def("Gun_Revolver");
                VerbProperties props = revolver.verbs![0];
                var verb = new Verb_LaunchProjectile(shooter, props);
                float distance = (target.Position - shooter.Position).LengthHorizontal;
                List<CoverInfo> cover = CoverUtility.CalculateCoverGiverSet(target.Position, shooter.Position, map, target);
                return (ShotReport.HitReportFor(shooter, verb, target, distance, cover), shooter, target);
            }

            ShotReport openGround = Setup(withWall: false).report;
            ShotReport behindWall = Setup(withWall: true).report;

            Assert.Equal(1f, openGround.PassCoverChance, 4);
            Assert.True(behindWall.PassCoverChance < 1f);
            Assert.True(behindWall.TotalEstimatedHitChance < openGround.TotalEstimatedHitChance,
                $"a pawn behind a wall ({behindWall.TotalEstimatedHitChance}) should be harder to hit than one in the open ({openGround.TotalEstimatedHitChance})");
        }

        [Fact]
        public void A_pawn_behind_a_wall_is_actually_hit_less_often_over_many_shots()
        {
            int HitsOver(int trials, bool withWall)
            {
                CoreMap map = NewMap(10, 10);
                Pawn shooter = SpawnHuman(map, new IntVec3(0, 0, 5), "Shooter");
                Pawn target = SpawnHuman(map, new IntVec3(5, 0, 5), "Target");
                if (withWall) SpawnWall(map, new IntVec3(4, 0, 5));

                ThingDef revolver = Def("Gun_Revolver");
                VerbProperties props = revolver.verbs![0];
                float distance = (target.Position - shooter.Position).LengthHorizontal;

                int hits = 0;
                for (int i = 0; i < trials; i++)
                {
                    // A fresh verb per shot: cover (via the wall, or its absence) is resolved automatically
                    // from caster/target positions on the shared map — no CoverLookup override needed, the
                    // same path any real spawned shot now takes.
                    var verb = new Verb_LaunchProjectile(shooter, props);
                    Assert.True(verb.TryStartCastOn(target, distance));
                    while (!verb.Available()) verb.VerbTick();
                    if (verb.LastShot!.Hit) hits++;
                }
                return hits;
            }

            const int trials = 800;
            int openHits = HitsOver(trials, withWall: false);
            int coveredHits = HitsOver(trials, withWall: true);
            Assert.True(coveredHits < openHits, $"covered hits ({coveredHits}) should be fewer than open-ground hits ({openHits}) over {trials} shots");
        }
    }
}
