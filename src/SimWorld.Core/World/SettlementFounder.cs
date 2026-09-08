using System;
using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// Founds a new <see cref="Settlement"/>: creates the entity, seeds its founding population, records the
    /// founding in the chronicle, and registers it with the world (spec §5b.3, §5b.5). Mirrors
    /// <see cref="Caravans.CaravanMaker"/>'s shape for the same reason — build the entity, populate it,
    /// register it with <see cref="World.worldObjects"/> — for the other kind of thing that sits on a tile.
    /// </summary>
    public static class SettlementFounder
    {
        /// <summary>
        /// Founds a settlement at <paramref name="tile"/> with a band of <paramref name="bandSize"/> people
        /// (spec §5b.3: 20-40, in several households). Every founder is generated Full-tier — that is simply
        /// <c>Pawn_TierTracker</c>'s own default state for a freshly-made <c>Pawn</c>, not something this
        /// method has to force. Households are founded via <see cref="Find.FamilyManager"/>'s existing
        /// <c>FoundHousehold</c> rather than a second lineage path, per the module's brief.
        /// </summary>
        public static Settlement Found(World world, int tile, Faction? faction, int bandSize, RandomStream rand, string? name = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (bandSize < SettlementTuning.FoundingBandRange.min || bandSize > SettlementTuning.FoundingBandRange.max)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bandSize),
                    bandSize,
                    $"Founding band must be {SettlementTuning.FoundingBandRange.min}-{SettlementTuning.FoundingBandRange.max} people (spec §5b.3).");
            }

            int foundingTick = Find.TickManager.TicksGame;
            string settlementName = name ?? RegionNameMaker.MakeRegionName(rand, ExistingSettlementNames(world));

            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, faction, settlementName, foundingTick);

            List<Pawn> founders = GenerateFoundingBand(bandSize, rand);
            PairIntoHouseholds(founders, foundingTick);
            foreach (Pawn pawn in founders) settlement.AddCitizen(pawn);

            world.worldObjects.Add(settlement);

            // Find.Storyteller is Layer 5 (Director); reached only through the Find service locator, the same
            // sanctioned cross-layer path FamilyManager itself already uses for the identical call — this
            // file never references SimWorld.Director.
            Find.Storyteller.RecordChronicle(
                "Founding: " + settlementName + " founded by " + founders.Count + " settlers.");

            return settlement;
        }

        private static List<string> ExistingSettlementNames(World world)
        {
            var names = new List<string>();
            foreach (WorldObject obj in world.worldObjects)
            {
                if (obj is Settlement s) names.Add(s.name);
            }
            return names;
        }

        /// <summary>
        /// Generates <paramref name="bandSize"/> adult founders, alternating gender so consecutive pairs are
        /// always opposite-gender couples, with ages spread across <see cref="SettlementTuning.FounderAgeRange"/>
        /// rather than one identical age.
        /// </summary>
        private static List<Pawn> GenerateFoundingBand(int bandSize, RandomStream rand)
        {
            var founders = new List<Pawn>(bandSize);
            for (int i = 0; i < bandSize; i++)
            {
                Gender gender = i % 2 == 0 ? Gender.Male : Gender.Female;
                float age = rand.Range(SettlementTuning.FounderAgeRange);
                var request = new PawnGenerationRequest(PawnKindDefOf.Tribesperson, fixedGender: gender, fixedBiologicalAge: age);
                founders.Add(PawnGenerator.GeneratePawn(request));
            }
            return founders;
        }

        /// <summary>Pairs founders consecutively into "several households" (spec §5b.3); an odd one out founds
        /// a single-founder household, the same shape <c>FamilyManager.FoundHousehold</c> already supports for
        /// "symmetry with future immigration-style founding".</summary>
        private static void PairIntoHouseholds(List<Pawn> founders, int foundingTick)
        {
            int i = 0;
            for (; i + 1 < founders.Count; i += 2)
            {
                Find.FamilyManager.FoundHousehold(founders[i], founders[i + 1], foundingTick);
            }
            if (i < founders.Count)
            {
                Find.FamilyManager.FoundHousehold(founders[i], null, foundingTick);
            }
        }
    }
}
