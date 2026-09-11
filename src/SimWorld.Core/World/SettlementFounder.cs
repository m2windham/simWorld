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

            List<Pawn> founders = GenerateFoundingBand(bandSize, faction, rand);
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

        /// <summary>
        /// Founds a new <see cref="Settlement"/> as an already-established civilization's expansion rather
        /// than a from-nothing founding (spec §5b.4/§5b.5's emergence gap: "an existing civilization should
        /// be able to found a second settlement as it grows"). Unlike <see cref="Found"/>, this seats
        /// <paramref name="statisticalPopulation"/> people directly into the Statistical tier — no live
        /// founding band, no individually named founders — the same representation a settlement's own
        /// organic growth eventually produces once it no longer needs every citizen modelled individually
        /// (spec §11.3). Two callers use this for two different reasons, both documented on their own call
        /// sites: <c>Gen.WorldGenStep_Factions</c> for a faction's second-and-later settlement at world
        /// generation (population already established, never watched come into being), and
        /// <see cref="EmergenceManager"/> for a growing civilization spinning off a new settlement during
        /// play (a small, just-founded colony the player does watch happen).
        /// </summary>
        public static Settlement FoundColony(
            World world, int tile, Faction? faction, int statisticalPopulation, RandomStream rand,
            string? name = null, bool recordChronicle = true)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (statisticalPopulation < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(statisticalPopulation), statisticalPopulation, "Population cannot be negative.");
            }

            int foundingTick = Find.TickManager.TicksGame;
            string settlementName = name ?? RegionNameMaker.MakeRegionName(rand, ExistingSettlementNames(world));

            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, faction, settlementName, foundingTick);
            settlement.AddStatisticalPeople(statisticalPopulation);

            world.worldObjects.Add(settlement);

            if (recordChronicle)
            {
                Find.Storyteller.RecordChronicle(
                    "Expansion: " + settlementName + " founded as a new settlement of " + (faction?.name ?? "an unaffiliated people") + ".");
            }

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
        /// Generates <paramref name="bandSize"/> adult founders of <paramref name="faction"/>, alternating
        /// gender so consecutive pairs are always opposite-gender couples, with ages spread across
        /// <see cref="SettlementTuning.FounderAgeRange"/> rather than one identical age.
        ///
        /// <para/><b><paramref name="faction"/> goes into the request, not onto the pawn afterwards.</b> That
        /// is RimWorld's own shape (<c>PawnGenerationRequest.Faction</c>), and here it is load-bearing rather
        /// than stylistic: <see cref="PawnWeaponGenerator"/> and <see cref="PawnApparelGenerator"/> both read
        /// <c>request.Faction</c> to cap candidates at <c>ThingDef.techLevel &lt;= faction.def.techLevel</c>,
        /// so a founder enfactioned after generation would already be holding gear its civilization cannot
        /// make. A neolithic people founding a town get bows; an industrial one gets what it can build.
        ///
        /// <para/><b>This costs no extra <see cref="RandomStream"/> draws.</b> Both generators draw exactly
        /// once per candidate pick (<c>GenCollection.TryRandomElementByWeight</c>) whatever the candidate list
        /// holds, and the tech ceiling only ever shortens that list — it never adds or removes a draw. The
        /// ceiling can change <i>which</i> item is picked for a civilization below the top rung; it cannot
        /// move the stream. Pinned by
        /// <c>Tests.World.CitizenFactionTests.Founding_a_band_costs_the_same_random_draws_with_a_faction_as_without</c>.
        /// </summary>
        private static List<Pawn> GenerateFoundingBand(int bandSize, Faction? faction, RandomStream rand)
        {
            var founders = new List<Pawn>(bandSize);
            for (int i = 0; i < bandSize; i++)
            {
                Gender gender = i % 2 == 0 ? Gender.Male : Gender.Female;
                float age = rand.Range(SettlementTuning.FounderAgeRange);
                var request = new PawnGenerationRequest(
                    PawnKindDefOf.Tribesperson, fixedGender: gender, fixedBiologicalAge: age, faction: faction);
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
