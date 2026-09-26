namespace SimWorld.Defs
{
    /// <summary>
    /// What makes a ThingDef a growable plant (RimWorld: <c>RimWorld.PlantProperties</c>), trimmed to what
    /// <see cref="Building.Plant"/>'s growth tick and harvest actually use — no growth stages, no wind
    /// exposure/visual size, no <c>sowTags</c> (a <see cref="Building.Zone_Growing"/> names a plant def
    /// directly rather than filtering by tag).
    /// </summary>
    public class PlantProperties
    {
        /// <summary>Real days (<see cref="Sim.GenDate.TicksPerDay"/> each) to grow from freshly sown to fully
        /// mature at a growth-rate factor of exactly 1 (perfect fertility × light × temperature). RimWorld's
        /// own per-species values are not sourced here — content picks and documents a number per species.</summary>
        public float growDays = 10f;

        /// <summary>What harvesting a mature plant of this def spawns; null means never harvestable for yield
        /// (RimWorld: decorative/wild growth that can only ever be cut, not harvested).</summary>
        public ThingDef? harvestedThingDef;

        /// <summary>Units of <see cref="harvestedThingDef"/> a fully-grown (Growth == 1) plant yields — scaled
        /// down by the plant's actual <see cref="Building.Plant.Growth"/> at harvest time.</summary>
        public int harvestYield;

        /// <summary>
        /// Minimum <see cref="Building.Plant.Growth"/> before a harvest job will target this plant at all.
        /// <b>Deviation:</b> defaults to 1 (fully grown only) — this port has no separate "ripe" life stage
        /// the way RimWorld's real early-harvest-allowed species do, so early harvest is not modelled; a
        /// content author can still lower this per species once that distinction exists.
        /// </summary>
        public float harvestMinGrowth = 1f;

        /// <summary>Minimum terrain fertility (<see cref="Map.TerrainDef.fertility"/>) a cell needs before a
        /// growing zone will sow this def there at all.</summary>
        public float sowMinFertility;

        /// <summary>
        /// What kind of harvest this plant gives (RimWorld: <c>PlantProperties.harvestTag</c>). RimWorld's
        /// content uses <c>"Standard"</c> for crops and <c>"Wood"</c> for trees; only the second is read here,
        /// through <see cref="IsTree"/>. Unset means an ordinary plant.
        /// </summary>
        public string? harvestTag;

        /// <summary>A plant whose harvest is wood (RimWorld: <c>PlantProperties.IsTree</c>, the same test on
        /// the same tag). A tree is felled for its wood by
        /// <see cref="Building.WorkGiver_ConstructChopWood"/>, never foraged by
        /// <see cref="Building.WorkGiver_GrowerHarvest"/> outside a growing zone.</summary>
        public bool IsTree => harvestTag == "Wood";

        /// <summary>Whether a live plant of this def blocks a roof from being built over its cell (RimWorld:
        /// <c>PlantProperties.interferesWithRoof</c>, default <c>false</c> there too). Read by
        /// <see cref="Building.RoofUtility.FirstBlockingThing"/>. Trees set it, as RimWorld's <c>TreeBase</c>
        /// does (<c>Plants_Trees.xml</c>), so a builder fells a tree before roofing over it.</summary>
        public bool interferesWithRoof;
    }

    /// <summary>
    /// Plant-layer half of <see cref="ThingDef"/> (system 16: Building — plant growth). Kept in its own
    /// partial file, matching the split <c>ThingDef.Things.cs</c>/<c>ThingDef.Building.cs</c> already use.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>Present only on Defs whose <c>thingClass</c> derives from <see cref="Building.Plant"/>.</summary>
        public PlantProperties? plant;
    }
}
