using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The dumping stockpile preset (<see cref="StorageSettingsPreset.DumpingStockpile"/>): the second of the two
    /// presets RimWorld ships, and the one that was left out until something gave the dead a destination. The
    /// first (<c>DefaultStockpile</c>) has never taken a body; this is the stockpile that takes bodies and
    /// nothing else.
    /// </summary>
    public class DumpingStockpileTests : ContentTestBase
    {
        public DumpingStockpileTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef HumanBody => CorpseDefGenerator.CorpseDefFor(Human);

        private static ThingDef AnimalBody => CorpseDefGenerator.CorpseDefFor(Husky);

        [Fact]
        public void The_dumping_preset_takes_every_body_and_nothing_else()
        {
            var zone = new Zone_Stockpile();
            zone.filter.SetFromPreset(StorageSettingsPreset.DumpingStockpile);

            Assert.True(zone.filter.Allows(HumanBody));
            Assert.True(zone.filter.Allows(AnimalBody));
            foreach (ThingDef def in zone.filter.AllowedThingDefs)
            {
                Assert.True(CorpseDefGenerator.IsCorpseDef(def), def.defName + " is not a body, and the dump takes bodies only");
            }
            Assert.False(zone.filter.Allows(Def("WoodLog")));
            Assert.False(zone.filter.Allows(Def("Meat_Generic")));
        }

        [Fact]
        public void The_default_preset_still_takes_everything_but_bodies()
        {
            var zone = new Zone_Stockpile();
            zone.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);

            Assert.False(zone.filter.Allows(HumanBody));
            Assert.False(zone.filter.Allows(AnimalBody));
            Assert.True(zone.filter.Allows(Def("WoodLog")));
        }

        [Fact]
        public void Applying_a_preset_replaces_what_was_there()
        {
            var zone = new Zone_Stockpile();
            zone.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            zone.filter.SetFromPreset(StorageSettingsPreset.DumpingStockpile);
            Assert.True(zone.filter.Allows(HumanBody));
            Assert.False(zone.filter.Allows(Def("WoodLog")));

            zone.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            Assert.False(zone.filter.Allows(HumanBody));
            Assert.True(zone.filter.Allows(Def("WoodLog")));
        }

        [Fact]
        public void A_body_lying_on_a_dumping_stockpile_is_in_valid_storage_and_one_on_the_default_is_not()
        {
            CoreMap map = new CoreMap(20, 20, SimWorld.Map.TerrainDefOf.Soil);
            Pawn dead = NewHuman("Dead");
            GenSpawn.Spawn(dead, new IntVec3(5, 0, 5), map);
            dead.health.Kill(null, null);
            CorpseThing body = dead.corpse!;

            var plain = new Zone_Stockpile();
            map.zoneManager.RegisterZone(plain);
            map.zoneManager.AddCell(plain, new IntVec3(5, 0, 5));
            plain.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            Assert.False(HaulAIUtility.IsInValidStorage(body));

            map.zoneManager.RemoveCell(plain, new IntVec3(5, 0, 5));
            var dump = new Zone_Stockpile();
            map.zoneManager.RegisterZone(dump);
            map.zoneManager.AddCell(dump, new IntVec3(5, 0, 5));
            dump.filter.SetFromPreset(StorageSettingsPreset.DumpingStockpile);
            Assert.True(HaulAIUtility.IsInValidStorage(body));
        }
    }
}
