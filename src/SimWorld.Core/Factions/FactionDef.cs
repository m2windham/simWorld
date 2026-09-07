using SimWorld.Defs;

namespace SimWorld.Factions
{
    /// <summary>
    /// A kind of civilization (RimWorld: <c>RimWorld.FactionDef</c>). This is the minimal slice world
    /// generation needs to seed rival civilizations and place their settlements; diplomacy, goodwill and
    /// raid behaviour land with the full Factions system.
    /// </summary>
    public class FactionDef : Def
    {
        public TechLevel techLevel = TechLevel.Neolithic;

        /// <summary>Relative weight when <c>Gen.WorldGenStep_Factions</c> distributes settlements among factions.</summary>
        public float settlementGenerationWeight = 1f;

        /// <summary>At least this many factions of this def must exist after world generation.</summary>
        public int requiredCountAtGameStart;

        /// <summary>Never more than this many factions of this def, regardless of the population setting.</summary>
        public int maxCountAtGameStart = 1;

        /// <summary>Excluded from ordinary faction listings/selection (used for scenario-only or placeholder defs).</summary>
        public bool hidden;

        /// <summary>Never at peace with anyone; always hostile.</summary>
        public bool permanentEnemy;

        /// <summary>Eligible to be created by the random faction-count roll, as opposed to only ever created to satisfy <see cref="requiredCountAtGameStart"/>.</summary>
        public bool canMakeRandomly = true;

        /// <summary>The (singular) player civilization def.</summary>
        public bool isPlayer;

        /// <summary>Lower bound on settlements for one instance of this faction once created; null = no extra floor beyond 1.</summary>
        public int? minSettlements;
    }
}
