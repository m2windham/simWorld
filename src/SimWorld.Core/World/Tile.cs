using System.Collections.Generic;

namespace SimWorld.World
{
    /// <summary>One outgoing river edge from a tile, ending at <see cref="neighbor"/> (RimWorld: <c>RimWorld.Planet.RiverLink</c>).</summary>
    public struct RiverLink
    {
        public int neighbor;
        public RiverDef river;

        public RiverLink(int neighbor, RiverDef river)
        {
            this.neighbor = neighbor;
            this.river = river;
        }
    }

    /// <summary>One outgoing road edge from a tile, ending at <see cref="neighbor"/> (RimWorld: <c>RimWorld.Planet.RoadLink</c>).</summary>
    public struct RoadLink
    {
        public int neighbor;
        public RoadDef road;

        public RoadLink(int neighbor, RoadDef road)
        {
            this.neighbor = neighbor;
            this.road = road;
        }
    }

    /// <summary>
    /// One world-map tile (RimWorld: <c>RimWorld.Planet.Tile</c>). Produced empty by <see cref="WorldGrid"/>
    /// and filled in by the <c>Gen.WorldGenStep_*</c> pipeline. <c>feature</c> (RimWorld's named-region
    /// overlay, e.g. "the Great Sea") is out of scope for this port.
    /// </summary>
    public class Tile
    {
        public BiomeDef? biome;
        public float elevation;
        public Hilliness hilliness;

        /// <summary>Average annual temperature, °C.</summary>
        public float temperature;

        /// <summary>Average annual rainfall, mm.</summary>
        public float rainfall;

        /// <summary>0..1; how boggy the ground is, gated to low elevation and high rainfall.</summary>
        public float swampiness;

        /// <summary>
        /// Set by <c>WorldGenStep_Biomes</c>'s flood fill before biome scoring: true when this water tile
        /// belongs to a small enclosed body of water, so <c>BiomeWorker_Lake</c> (rather than
        /// <c>BiomeWorker_Ocean</c>) wins the tile.
        /// </summary>
        public bool lakeCandidate;

        public List<RiverLink> potentialRivers = new List<RiverLink>();
        public List<RoadLink> potentialRoads = new List<RoadLink>();

        /// <summary>
        /// Resource/landmark deposits derived from this tile's own terrain by <c>Gen.WorldGenStep_Deposits</c>
        /// (spec §5b.2). Only non-zero magnitudes are stored; always empty for a water tile.
        /// </summary>
        public List<TileDeposit> deposits = new List<TileDeposit>();

        /// <summary>RimWorld's rule: sea level is elevation 0; at or below it, the tile is water.</summary>
        public bool WaterCovered => elevation <= 0f;

        public IReadOnlyList<RiverLink> Rivers => potentialRivers;

        public IReadOnlyList<RoadLink> Roads => potentialRoads;

        public IReadOnlyList<TileDeposit> Deposits => deposits;

        /// <summary>This tile's magnitude for <paramref name="def"/>, in [0,1]; 0 when the deposit is absent.</summary>
        public float DepositMagnitude(DepositDef def)
        {
            for (int i = 0; i < deposits.Count; i++)
            {
                if (deposits[i].def == def) return deposits[i].magnitude;
            }
            return 0f;
        }
    }
}
