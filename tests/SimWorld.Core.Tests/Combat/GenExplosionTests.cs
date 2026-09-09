using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.Tests.Content;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Combat
{
    /// <summary>Radial explosion damage (system 12: Combat — structures): which cells a blast reaches, and how hard, respecting walls.</summary>
    public class GenExplosionTests : ContentTestBase
    {
        public GenExplosionTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Thing SpawnWall(CoreMap map, IntVec3 cell)
        {
            Thing wall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, cell, map);
            return wall;
        }

        [Fact]
        public void Cells_to_hit_cover_the_full_radius_on_open_ground()
        {
            CoreMap map = NewMap(20, 20);
            var center = new IntVec3(10, 0, 10);
            List<IntVec3> cells = GenExplosion.ExplosionCellsToHit(center, map, 3f);

            Assert.Contains(center, cells);
            Assert.Contains(new IntVec3(13, 0, 10), cells); // exactly at the radius, cardinal
            Assert.DoesNotContain(new IntVec3(14, 0, 10), cells); // one cell past it
        }

        [Fact]
        public void A_wall_shields_the_cells_directly_behind_it_but_is_itself_hit()
        {
            CoreMap map = NewMap(20, 20);
            var center = new IntVec3(10, 0, 10);
            SpawnWall(map, new IntVec3(12, 0, 10)); // between center and the cells further east

            List<IntVec3> cells = GenExplosion.ExplosionCellsToHit(center, map, 5f);

            Assert.Contains(new IntVec3(12, 0, 10), cells); // the wall itself takes the hit that stops the blast
            Assert.DoesNotContain(new IntVec3(13, 0, 10), cells); // shielded
            Assert.DoesNotContain(new IntVec3(14, 0, 10), cells); // shielded
            Assert.Contains(new IntVec3(10, 0, 15), cells); // unobstructed direction still reaches its own radius
        }

        [Fact]
        public void Damage_falls_off_with_distance_when_requested()
        {
            // Non-pawn Things (plain hit points, via Thing.TakeDamage) rather than pawns: a 50-point Blunt
            // hit to any one human body part almost always kills it or blows the part off outright — neither
            // shows up as a comparable "how much damage landed" number the way a wall's HitPoints delta does.
            CoreMap map = NewMap(20, 20);
            var center = new IntVec3(10, 0, 10);
            Thing near = SpawnWall(map, center); // at the epicenter
            Thing far = SpawnWall(map, new IntVec3(10, 0, 15)); // at the very edge of a radius-5 blast

            GenExplosion.DoExplosion(center, map, 5f, DamageDefOf.Blunt, null, 50f, damageFalloff: true);

            int nearDamage = near.MaxHitPoints - near.HitPoints;
            int farDamage = far.MaxHitPoints - far.HitPoints;
            Assert.True(nearDamage > 0);
            Assert.True(farDamage > 0);
            Assert.True(farDamage < nearDamage, $"edge damage ({farDamage}) should be less than center damage ({nearDamage})");
        }

        [Fact]
        public void No_falloff_means_the_full_amount_everywhere_in_radius()
        {
            CoreMap map = NewMap(20, 20);
            var center = new IntVec3(10, 0, 10);
            Thing near = SpawnWall(map, center);
            Thing far = SpawnWall(map, new IntVec3(10, 0, 15));

            GenExplosion.DoExplosion(center, map, 5f, DamageDefOf.Blunt, null, 50f, damageFalloff: false);

            int nearDamage = near.MaxHitPoints - near.HitPoints;
            int farDamage = far.MaxHitPoints - far.HitPoints;
            Assert.Equal(nearDamage, farDamage);
        }

        [Fact]
        public void A_non_pawn_thing_in_the_blast_takes_generic_hit_point_damage()
        {
            CoreMap map = NewMap(20, 20);
            var center = new IntVec3(10, 0, 10);
            Thing wall = SpawnWall(map, new IntVec3(10, 0, 11));
            int before = wall.HitPoints;

            GenExplosion.DoExplosion(center, map, 5f, DamageDefOf.Blunt, null, 50f);

            Assert.True(wall.HitPoints < before);
        }
    }
}
