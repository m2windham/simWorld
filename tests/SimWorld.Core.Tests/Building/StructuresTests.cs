using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Turrets, traps and explosives (system 12: Combat — structures): buildings with comps, following the module's existing shape.</summary>
    public class StructuresTests : ContentTestBase
    {
        public StructuresTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static global::SimWorld.Building.Building SpawnBuilding(CoreMap map, IntVec3 cell, string defName)
        {
            var thing = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static Faction NewFaction(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);

        // ---- content ----

        [Fact]
        public void Structures_content_loads_with_no_errors()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(Def("Turret_Mini").GetCompProperties<global::SimWorld.Building.CompProperties_TurretGun>());
            Assert.NotNull(Def("TrapSpike").GetCompProperties<global::SimWorld.Building.CompProperties_Trap>());
            Assert.NotNull(Def("TrapIED").GetCompProperties<global::SimWorld.Building.CompProperties_Explosive>());
        }

        // ---- turret ----

        [Fact]
        public void An_unpowered_turret_never_fires()
        {
            CoreMap map = NewMap(20, 20);
            global::SimWorld.Building.Building turret = SpawnBuilding(map, new IntVec3(5, 0, 5), "Turret_Mini");
            var comp = turret.GetComp<global::SimWorld.Building.CompTurretGun>()!;
            comp.Faction = NewFaction("Defenders");

            Pawn hostile = SpawnHuman(map, new IntVec3(5, 0, 10));
            hostile.faction = NewFaction("Raiders");
            comp.Faction.SetRelationDirect(hostile.faction, FactionRelationKind.Hostile, -100);

            // No generator anywhere on the map: PowerNetTick immediately browns the turret out.
            Find.TickManager.PostTickers.Add(_ => map.MapTick());
            RunTicks(1000, hostile);

            Assert.Empty(hostile.health.hediffSet.hediffs);
        }

        [Fact]
        public void A_powered_turret_fires_on_a_hostile_pawn_in_range_and_wounds_it()
        {
            CoreMap map = NewMap(20, 20);
            Find.TickManager.PostTickers.Add(_ => map.MapTick());

            global::SimWorld.Building.Building turret = SpawnBuilding(map, new IntVec3(5, 0, 5), "Turret_Mini");
            SpawnBuilding(map, new IntVec3(6, 0, 5), "WoodFiredGenerator"); // cardinally adjacent: powers it directly, no conduit needed
            var comp = turret.GetComp<global::SimWorld.Building.CompTurretGun>()!;
            Faction defenders = NewFaction("Defenders");
            Faction raiders = NewFaction("Raiders");
            defenders.SetRelationDirect(raiders, FactionRelationKind.Hostile, -100);
            comp.Faction = defenders;

            Pawn hostile = SpawnHuman(map, new IntVec3(5, 0, 15)); // 10 cells away, straight line, well within the 26.9 range
            hostile.faction = raiders;

            RunTicks(4000, hostile); // several full warmup/burst/cooldown cycles

            Assert.NotEmpty(hostile.health.hediffSet.hediffs);
        }

        [Fact]
        public void A_turret_never_fires_on_its_own_factions_pawns()
        {
            CoreMap map = NewMap(20, 20);
            Find.TickManager.PostTickers.Add(_ => map.MapTick());

            global::SimWorld.Building.Building turret = SpawnBuilding(map, new IntVec3(5, 0, 5), "Turret_Mini");
            SpawnBuilding(map, new IntVec3(6, 0, 5), "WoodFiredGenerator");
            var comp = turret.GetComp<global::SimWorld.Building.CompTurretGun>()!;
            Faction defenders = NewFaction("Defenders");
            comp.Faction = defenders;

            Pawn friendly = SpawnHuman(map, new IntVec3(5, 0, 15));
            friendly.faction = defenders;

            RunTicks(1000, friendly);

            Assert.Empty(friendly.health.hediffSet.hediffs);
        }

        // ---- trap ----

        [Fact]
        public void A_spike_trap_springs_once_on_the_first_pawn_to_enter_and_then_disarms()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(5, 0, 5);
            global::SimWorld.Building.Building trap = SpawnBuilding(map, cell, "TrapSpike");
            var comp = trap.GetComp<global::SimWorld.Building.CompTrap>()!;
            Assert.True(comp.armed);

            Pawn victim = SpawnHuman(map, cell);
            RunTicks(1, victim);

            Assert.False(comp.armed);
            Assert.NotEmpty(victim.health.hediffSet.hediffs);

            // Spent: a second pawn standing on the same cell is not harmed.
            int hediffsBefore = victim.health.hediffSet.hediffs.Count;
            Pawn second = SpawnHuman(map, cell, "Second");
            RunTicks(1, second, victim);
            Assert.Empty(second.health.hediffSet.hediffs);
            Assert.Equal(hediffsBefore, victim.health.hediffSet.hediffs.Count);
        }

        [Fact]
        public void A_trap_never_springs_on_its_own_factions_pawns()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(5, 0, 5);
            global::SimWorld.Building.Building trap = SpawnBuilding(map, cell, "TrapSpike");
            var comp = trap.GetComp<global::SimWorld.Building.CompTrap>()!;
            Faction owner = NewFaction("Owners");
            comp.Faction = owner;

            Pawn colonist = SpawnHuman(map, cell);
            colonist.faction = owner;
            RunTicks(1, colonist);

            Assert.True(comp.armed);
            Assert.Empty(colonist.health.hediffSet.hediffs);
        }

        // ---- explosive trap (trap + explosive comps together) ----

        [Fact]
        public void An_IED_trap_detonates_instead_of_dealing_its_own_hit_and_damages_a_wider_area()
        {
            CoreMap map = NewMap(20, 20);
            var cell = new IntVec3(10, 0, 10);
            global::SimWorld.Building.Building ied = SpawnBuilding(map, cell, "TrapIED");
            var trapComp = ied.GetComp<global::SimWorld.Building.CompTrap>()!;
            var explosiveComp = ied.GetComp<global::SimWorld.Building.CompExplosive>()!;

            // A bystander two cells away from the IED, well outside the trap's own single-cell trigger, but
            // inside its explosive's blast radius.
            Thing bystanderWall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(bystanderWall, new IntVec3(12, 0, 10), map);
            int wallHpBefore = bystanderWall.HitPoints;

            Pawn victim = SpawnHuman(map, cell);
            RunTicks(1, victim);

            Assert.False(trapComp.armed);
            Assert.True(explosiveComp.detonated);
            Assert.True(bystanderWall.HitPoints < wallHpBefore, "the explosion should reach beyond the trap's own single cell");
        }

        // ---- Scribe ----

        [Fact]
        public void Turret_trap_and_explosive_state_round_trip_through_scribe()
        {
            // Faction is deliberately left unset here: a Faction round-trips through the FactionManager/World
            // save, not through a lone Map's — see Pawn.faction's own Scribe_References field for the same
            // constraint. What this test pins is this module's own new comp state: an armed/spent trap and a
            // detonated/undetonated charge.
            CoreMap map = NewMap(10, 10);
            SpawnBuilding(map, new IntVec3(1, 0, 1), "Turret_Mini");

            global::SimWorld.Building.Building trap = SpawnBuilding(map, new IntVec3(3, 0, 3), "TrapSpike");
            Pawn victim = SpawnHuman(map, new IntVec3(3, 0, 3));
            RunTicks(1, victim); // springs it
            Assert.False(trap.GetComp<global::SimWorld.Building.CompTrap>()!.armed);

            global::SimWorld.Building.Building ied = SpawnBuilding(map, new IntVec3(6, 0, 6), "TrapIED");
            Assert.False(ied.GetComp<global::SimWorld.Building.CompExplosive>()!.detonated);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            global::SimWorld.Building.Building? loadedTurret = null;
            global::SimWorld.Building.Building? loadedTrap = null;
            global::SimWorld.Building.Building? loadedIed = null;
            foreach (Thing t in loaded.listerThings.AllThings)
            {
                if (t.def.defName == "Turret_Mini") loadedTurret = (global::SimWorld.Building.Building)t;
                if (t.def.defName == "TrapSpike") loadedTrap = (global::SimWorld.Building.Building)t;
                if (t.def.defName == "TrapIED") loadedIed = (global::SimWorld.Building.Building)t;
            }
            Assert.NotNull(loadedTurret);
            Assert.NotNull(loadedTrap);
            Assert.NotNull(loadedIed);

            Assert.NotNull(loadedTurret!.GetComp<global::SimWorld.Building.CompTurretGun>());
            Assert.False(loadedTrap!.GetComp<global::SimWorld.Building.CompTrap>()!.armed);
            Assert.False(loadedIed!.GetComp<global::SimWorld.Building.CompExplosive>()!.detonated);
        }
    }
}
