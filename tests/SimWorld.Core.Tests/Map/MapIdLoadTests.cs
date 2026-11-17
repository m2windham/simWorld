using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// A map made after a load never takes a loaded map's id. RimWorld resumes its map-id counter from the
    /// save (<c>UniqueIDsManager</c>); this port advances it past each map it reads (<see cref="CoreMap.ExposeData"/>).
    /// </summary>
    public class MapIdLoadTests : ContentTestBase
    {
        public MapIdLoadTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_map_made_after_loading_a_save_does_not_reuse_a_loaded_maps_id()
        {
            var saved = new List<CoreMap>();
            for (int i = 0; i < 3; i++) saved.Add(new CoreMap(8, 8, TerrainDefOf.Soil));
            var xml = new List<string>();
            foreach (CoreMap map in saved) xml.Add(Scribe.SaveToString(map, "map"));

            // A fresh process: the counter starts from zero, as it does after Game.NewGame's hand-over.
            CoreMap.ResetMapIdCounter();
            var loadedIds = new HashSet<int>();
            foreach (string doc in xml)
            {
                CoreMap loaded = Scribe.Load<CoreMap>(doc, "map", out IReadOnlyList<string> errors);
                Assert.Empty(errors);
                loadedIds.Add(loaded.uniqueID);
            }
            Assert.Equal(3, loadedIds.Count);

            for (int i = 0; i < 3; i++)
            {
                var made = new CoreMap(8, 8, TerrainDefOf.Soil);
                Assert.True(loadedIds.Add(made.uniqueID), $"a map made after the load took id {made.uniqueID}, already a loaded map's");
            }
        }
    }
}
