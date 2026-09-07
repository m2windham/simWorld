using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// Something sitting on one world tile — a settlement, later a caravan or site (RimWorld:
    /// <c>RimWorld.Planet.WorldObject</c>). Saved as part of <see cref="World.worldObjects"/>; unlike the
    /// tile grid itself, these are not regenerated from the seed (see <see cref="World.RegenerateGrid"/>).
    /// </summary>
    public class WorldObject : IExposable
    {
        public WorldObjectDef def = null!;
        public int tile;
        public Faction? faction;

        /// <summary>For Scribe's deep-load construction.</summary>
        public WorldObject()
        {
        }

        public WorldObject(WorldObjectDef def, int tile, Faction? faction)
        {
            this.def = def;
            this.tile = tile;
            this.faction = faction;
        }

        public void ExposeData()
        {
            WorldObjectDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref tile, "tile", 0);
            Scribe_References.Look(ref faction, "faction");
        }

        public override string ToString() => def?.defName + "@" + tile;
    }
}
