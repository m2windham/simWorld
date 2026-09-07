using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>
    /// What the ground itself is (RimWorld: <c>Verse.TerrainDef</c>). Every cell has exactly one; the
    /// <see cref="TerrainGrid"/> holds a top layer (this) and an under layer revealed when it is removed.
    /// </summary>
    public class TerrainDef : Def
    {
        /// <summary>Added to a pawn's path cost for entering a cell of this terrain.</summary>
        public int pathCost;

        /// <summary>Plant growth multiplier; 0 supports no growth.</summary>
        public float fertility;

        public Traversability passability = Traversability.Standable;

        /// <summary>Affordances this terrain offers; a ThingDef needing one can only be placed where it is present.</summary>
        public List<TerrainAffordanceDef>? affordances;

        /// <summary>Blood and dirt marks show and accumulate on this terrain.</summary>
        public bool takeFootprints;

        /// <summary>Water terrain: no footprints, and later systems treat it as unbuildable/swimmable.</summary>
        public bool water;

        /// <summary>Removing the top layer (e.g. mining a floor) reveals the terrain beneath instead of bare ground.</summary>
        public TerrainDef? underTerrain;

        /// <summary>Can serve as another terrain's <see cref="underTerrain"/> (natural ground can; most floors cannot).</summary>
        public bool layerable;

        public bool IsWater => water;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (fertility < 0f)
            {
                yield return "fertility must not be negative.";
            }
        }
    }
}
