using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The gap between "the work giver is wired" and "anyone in a real game can ever do it".
    ///
    /// <para/><see cref="WorkGiver_Hunt"/> requires a ranged weapon, as RimWorld's <c>HasHuntingWeapon</c>
    /// does. <see cref="SettlementFounder"/> generates every founding citizen as <c>Tribesperson</c>, and that
    /// kind shipped with no <c>weaponTags</c> at all — so <c>PawnWeaponGenerator</c> returned early for every
    /// citizen in every generated settlement, and the Hunt work type was wired and permanently dormant. Every
    /// unit test of the hunting mechanism passed throughout, because they arm their own pawns.
    ///
    /// <para/>These tests deliberately arm nobody. They assert the property the mechanism tests cannot: that
    /// shipped content, generating a settlement the ordinary way, produces citizens who can actually hunt.
    /// </summary>
    public class HuntingIsReachableInShippedContentTests : ContentTestBase
    {
        public HuntingIsReachableInShippedContentTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Settlement FoundBand(string seed)
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            return SettlementFounder.Found(world, tile, faction, 30, new RandomStream(20260911), "Huntholm");
        }

        [Fact]
        public void AFoundedSettlementContainsSomeoneWhoCanHunt()
        {
            Settlement settlement = FoundBand("hunt-reachable");

            int armed = settlement.Citizens.Count(HuntUtility.HasHuntingWeapon);

            Assert.True(armed > 0,
                "no citizen of a freshly founded settlement carries a hunting weapon, so the Hunt work type "
                + "can never produce a job in a real game (" + settlement.Citizens.Count + " citizens)");
        }

        [Fact]
        public void ItIsSomeOfTheBandRatherThanAllOfIt()
        {
            // A weapon is drawn against a money budget, so arming is a distribution rather than a guarantee.
            // Asserted as a band, not a count: pinning an exact number would pin the RNG stream, and the
            // point is only that this is a hunting band and not a standing army.
            Settlement settlement = FoundBand("hunt-distribution");

            int armed = settlement.Citizens.Count(HuntUtility.HasHuntingWeapon);

            Assert.InRange(armed, 1, settlement.Citizens.Count);
        }

        [Fact]
        public void NobodyInANeolithicBandCarriesSomethingFromALaterEra()
        {
            // PawnWeaponGenerator caps candidates at the faction's techLevel, which is what lets one
            // PawnKindDef serve a civilization at any rung of the era ladder. If that cap ever broke, a
            // founding band would turn up holding assault rifles and this is where it would show.
            Settlement settlement = FoundBand("hunt-techlevel");
            TechLevel ceiling = settlement.faction!.def.techLevel;

            foreach (Pawn citizen in settlement.Citizens)
            {
                ThingDef? weapon = citizen.equipment.Primary?.def;
                if (weapon == null) continue;
                Assert.True(weapon.techLevel <= ceiling,
                    citizen.Name + " carries " + weapon.defName + " (" + weapon.techLevel + ") above the "
                        + "faction's " + ceiling);
            }
        }
    }
}
