using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A grave puts one body away (<see cref="Building_Grave"/>, <see cref="Building_Casket"/>,
    /// <see cref="ThingOwner"/>). The defect it answers, from the player's side: a person dies and their body
    /// lies where they fell for the rest of the game. A grave is the other place a body can be — inside
    /// something, off the map, out of sight — and the rules for which body a grave will take are RimWorld's:
    /// humanlike, one at a time, and by default one of the settlement's own and not a stranger.
    /// <para/>
    /// What is pinned is behaviour. The grave's work and cost are this port's own figures (unsourced; see
    /// <c>Buildings_Grave.xml</c>), so nothing here asserts them: only that the settlement can build one.
    /// </summary>
    public class GraveTests : ContentTestBase
    {
        public GraveTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap() => new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);

        private static Faction OurFaction() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");

        private static Faction TheirFaction() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");

        private static Building_Grave NewGrave(CoreMap map, IntVec3 cell)
        {
            var grave = (Building_Grave)ThingMaker.MakeThing(GraveDefOf.Grave);
            GenSpawn.Spawn(grave, cell, map);
            return grave;
        }

        /// <summary>Somebody of <paramref name="faction"/> who has died where they stood, and the body.</summary>
        private static CorpseThing Body(CoreMap map, IntVec3 cell, Faction? faction, string name = "Dead")
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            p.health.Kill(null, null);
            Assert.NotNull(p.corpse);
            return p.corpse!;
        }

        // ---- the def ----

        [Fact]
        public void The_grave_def_says_what_a_grave_is()
        {
            ThingDef grave = GraveDefOf.Grave;
            Assert.Equal(typeof(Building_Grave), grave.thingClass);
            Assert.Equal(1, grave.size.x);
            Assert.Equal(2, grave.size.z);

            // Fixed: corpses, all of them, and only them.
            StorageSettings fixedSettings = grave.building!.fixedStorageSettings!;
            Assert.True(fixedSettings.filter.Allows(CorpseDefGenerator.CorpseDefFor(Human)));
            Assert.True(fixedSettings.filter.Allows(CorpseDefGenerator.CorpseDefFor(Husky)));
            Assert.False(fixedSettings.filter.Allows(Def("WoodLog")));

            // Default: humanlike bodies, strangers refused, and wanted more than a dumping stockpile wants them.
            StorageSettings defaults = grave.building.defaultStorageSettings!;
            Assert.Equal(StoragePriority.Important, defaults.Priority);
            Assert.True(defaults.Priority > StoreUtility.StockpilePriority);
            Assert.True(defaults.filter.Allows(CorpseDefGenerator.CorpseDefFor(Human)));
            Assert.False(defaults.filter.Allows(CorpseDefGenerator.CorpseDefFor(Husky)));
            Assert.Contains(SpecialThingFilter.CorpsesStranger, defaults.disallowedSpecialFilters);
            Assert.DoesNotContain(SpecialThingFilter.CorpsesColonist, defaults.disallowedSpecialFilters);
        }

        [Fact]
        public void A_new_graves_settings_are_its_own_copy_of_the_defaults()
        {
            var grave = (Building_Grave)ThingMaker.MakeThing(GraveDefOf.Grave);
            StorageSettings defaults = GraveDefOf.Grave.building!.defaultStorageSettings!;

            Assert.Equal(defaults.Priority, grave.GetStoreSettings().Priority);
            Assert.NotSame(defaults, grave.GetStoreSettings());

            grave.GetStoreSettings().Priority = StoragePriority.Low;
            grave.GetStoreSettings().disallowedSpecialFilters.Clear();

            Assert.Equal(StoragePriority.Important, defaults.Priority);
            Assert.Contains(SpecialThingFilter.CorpsesStranger, defaults.disallowedSpecialFilters);
            Assert.Same(GraveDefOf.Grave.building.fixedStorageSettings, grave.GetParentStoreSettings());
        }

        // ---- what a grave will take ----

        [Fact]
        public void A_grave_takes_one_of_its_own_and_refuses_a_stranger_an_animal_and_an_item()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            Faction ours = OurFaction();

            CorpseThing citizen = Body(map, new IntVec3(10, 0, 10), ours, "Citizen");
            CorpseThing stranger = Body(map, new IntVec3(12, 0, 10), TheirFaction(), "Stranger");
            CorpseThing drifter = Body(map, new IntVec3(14, 0, 10), null, "Drifter");
            var dog = new Pawn(Husky, "Rex");
            GenSpawn.Spawn(dog, new IntVec3(16, 0, 10), map);
            dog.health.Kill(null, null);
            Thing logs = GenSpawn.Spawn(ThingMaker.MakeThing(Def("WoodLog")), new IntVec3(18, 0, 10), map);

            Assert.True(grave.Accepts(citizen));
            Assert.False(grave.Accepts(stranger), "a stranger is not one of ours");
            Assert.False(grave.Accepts(drifter), "nobody's faction is not ours either");
            Assert.False(grave.Accepts(dog.corpse!), "the default is humanlike bodies");
            Assert.False(grave.Accepts(logs), "a grave takes bodies and only bodies");
        }

        [Fact]
        public void Setting_a_grave_to_take_strangers_makes_it_take_them()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing stranger = Body(map, new IntVec3(12, 0, 10), TheirFaction(), "Stranger");
            Assert.False(grave.Accepts(stranger));

            grave.GetStoreSettings().disallowedSpecialFilters.Remove(SpecialThingFilter.CorpsesStranger);

            Assert.True(grave.Accepts(stranger));
        }

        // ---- filling it, exactly once ----

        [Fact]
        public void A_body_goes_inside_the_grave_and_leaves_the_map()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(10, 0, 10), OurFaction(), "Bob");
            Pawn bob = body.InnerPawn!;

            Assert.True(grave.TryAcceptThing(body));

            Assert.True(grave.HasCorpse);
            Assert.Same(body, grave.Corpse);
            Assert.False(body.Spawned, "a body in the ground is not on the map");
            Assert.DoesNotContain(body, map.listerThings.AllThings);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));
            Assert.Same(grave, body.ParentHolder);
            Assert.Same(grave, body.StoringThing());
            Assert.Same(bob, grave.Corpse!.InnerPawn);
            Assert.Equal("grave of Bob", grave.Label);
        }

        [Fact]
        public void A_grave_is_filled_exactly_once()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            Faction ours = OurFaction();
            CorpseThing first = Body(map, new IntVec3(10, 0, 10), ours, "First");
            CorpseThing second = Body(map, new IntVec3(12, 0, 10), ours, "Second");

            Assert.True(grave.TryAcceptThing(first));
            Assert.False(grave.Accepts(second));
            Assert.False(grave.TryAcceptThing(second));

            Assert.Same(first, grave.Corpse);
            Assert.Equal(1, grave.GetDirectlyHeldThings().Count);
            Assert.True(second.Spawned, "the refused body is still lying where it was");
            Assert.False(grave.StorageTabVisible, "a full grave has no settings left to choose");
        }

        [Fact]
        public void An_empty_grave_is_labelled_a_grave_and_a_full_one_names_who_lies_there()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            Assert.Equal("grave", grave.Label);
            Assert.Contains("nothing", grave.GetInspectString());

            grave.TryAcceptThing(Body(map, new IntVec3(10, 0, 10), OurFaction(), "Alice"));

            Assert.Equal("grave of Alice", grave.Label);
            Assert.DoesNotContain("nothing", grave.GetInspectString());
            Assert.Contains(grave.Corpse!.Label, grave.GetInspectString());
        }

        // ---- seeing it: a buried body is put away ----

        [Fact]
        public void A_body_in_a_grave_gives_no_observed_thought_and_one_on_the_ground_does()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(10, 0, 10), OurFaction(), "Bob");

            Assert.NotNull(body.GiveObservedThought());

            grave.TryAcceptThing(body);

            Assert.Null(body.GiveObservedThought());
        }

        [Fact]
        public void A_pawn_beside_a_filled_grave_is_not_disturbed()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(10, 0, 10));
            Pawn watcher = NewHuman("Watcher");
            GenSpawn.Spawn(watcher, new IntVec3(12, 0, 10), map);
            grave.TryAcceptThing(Body(map, new IntVec3(20, 0, 20), OurFaction(), "Bob"));

            watcher.needs.mood!.observer.ObserveSurroundingThings();

            Assert.Empty(watcher.needs.mood.thoughts.memories.Memories);
        }

        [Fact]
        public void A_body_lying_in_a_stockpile_is_still_seen()
        {
            // RimWorld's storing Thing is the building that holds it, and a zone is not a building: a dumping
            // stockpile does not hide its dead. The settlement keeps its dump away from its people for this.
            CoreMap map = NewMap();
            CorpseThing body = Body(map, new IntVec3(10, 0, 10), TheirFaction(), "Raider");
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, body.Position);
            zone.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            zone.filter.SetAllow(DefDatabase<ThingCategoryDef>.GetNamed("Corpses"), true);

            Assert.Null(body.StoringThing());
            Assert.NotNull(body.GiveObservedThought());
        }

        // ---- taking it apart ----

        [Fact]
        public void Destroying_a_grave_by_damage_gives_the_body_back_and_vanishing_it_does_not()
        {
            CoreMap map = NewMap();
            Building_Grave shot = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(10, 0, 10), OurFaction(), "Shot");
            shot.TryAcceptThing(body);

            shot.Destroy(DestroyMode.KillFinalize);

            Assert.True(body.Spawned);
            Assert.Equal(new IntVec3(5, 0, 5), body.Position);
            Assert.Null(body.ParentHolder);
            Assert.False(body.InnerPawn!.Destroyed);

            Building_Grave gone = NewGrave(map, new IntVec3(8, 0, 5));
            CorpseThing other = Body(map, new IntVec3(10, 0, 12), OurFaction(), "Gone");
            gone.TryAcceptThing(other);

            gone.Destroy(DestroyMode.Vanish);

            Assert.True(other.Destroyed);
            Assert.True(other.InnerPawn!.Destroyed, "destroying a corpse takes the body with it");
        }

        // ---- save and load ----

        [Fact]
        public void A_filled_grave_round_trips_with_its_body_and_its_settings()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            grave.GetStoreSettings().Priority = StoragePriority.Preferred;
            // The reverse of the default: strangers welcome, the settlement's own refused. A save that dropped
            // the set would come back with neither, or with the default.
            grave.GetStoreSettings().disallowedSpecialFilters.Remove(SpecialThingFilter.CorpsesStranger);
            grave.GetStoreSettings().disallowedSpecialFilters.Add(SpecialThingFilter.CorpsesColonist);
            // A factionless body: a pawn's faction is a reference to something saved elsewhere, and this save
            // is of one map, so a body that points at a faction would load with a dangling reference.
            CorpseThing body = Body(map, new IntVec3(10, 0, 10), null, "Bob");
            Pawn bob = body.InnerPawn!;
            grave.TryAcceptThing(body);
            Building_Grave empty = NewGrave(map, new IntVec3(8, 0, 5));
            empty.GetStoreSettings().Priority = StoragePriority.Low;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            List<Building_Grave> graves = loaded.listerThings.ThingsOfDef(GraveDefOf.Grave).Cast<Building_Grave>().ToList();
            Assert.Equal(2, graves.Count);
            Building_Grave full = graves.Single(g => g.HasCorpse);
            Assert.Equal("grave of Bob", full.Label);
            Assert.Equal(StoragePriority.Preferred, full.GetStoreSettings().Priority);
            Assert.DoesNotContain(SpecialThingFilter.CorpsesStranger, full.GetStoreSettings().disallowedSpecialFilters);
            Assert.Contains(SpecialThingFilter.CorpsesColonist, full.GetStoreSettings().disallowedSpecialFilters);
            Assert.Same(full, full.GetStoreSettings().Owner);

            CorpseThing buried = full.Corpse!;
            Assert.False(buried.Spawned);
            Assert.Same(full, buried.ParentHolder);
            Assert.Same(full, buried.StoringThing());
            Assert.Equal(bob.thingIDNumber, buried.InnerPawn!.thingIDNumber);
            Assert.True(buried.InnerPawn.Dead);
            Assert.Same(buried, buried.InnerPawn.corpse);
            Assert.Empty(loaded.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));

            Building_Grave other = graves.Single(g => !g.HasCorpse);
            Assert.Equal(StoragePriority.Low, other.GetStoreSettings().Priority);
            Assert.True(other.StorageTabVisible);
            Assert.Null(buried.GiveObservedThought());
        }

        [Fact]
        public void A_loaded_empty_grave_still_takes_a_body_by_its_defs_fixed_rules()
        {
            CoreMap map = NewMap();
            NewGrave(map, new IntVec3(5, 0, 5));
            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            var grave = (Building_Grave)loaded.listerThings.ThingsOfDef(GraveDefOf.Grave).Single();
            CorpseThing body = Body(loaded, new IntVec3(10, 0, 10), OurFaction(), "Bob");
            Thing logs = GenSpawn.Spawn(ThingMaker.MakeThing(Def("WoodLog")), new IntVec3(12, 0, 10), loaded);

            Assert.True(grave.Accepts(body));
            Assert.False(grave.Accepts(logs));
            Assert.NotNull(grave.GetParentStoreSettings());
        }

        // ---- the groups the hauler and the gravedigger ask ----

        [Fact]
        public void The_map_indexes_bodies_and_haul_destinations_by_group()
        {
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(10, 0, 10), OurFaction(), "Bob");
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("WoodLog")), new IntVec3(12, 0, 10), map);

            Assert.Equal(new Thing[] { body }, map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));
            Assert.Equal(new Thing[] { grave }, map.listerThings.ThingsInGroup(ThingRequestGroup.HaulDestination));
        }
    }
}
