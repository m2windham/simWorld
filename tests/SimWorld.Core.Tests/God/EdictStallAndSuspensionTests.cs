using System.Collections.Generic;
using System.Linq;

using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The two edict reasons that can both be true at once compose: an edict suspended for a vacant office
    /// says that, and only that; the "nobody is allowed to do X" stall is a fact about an edict in force, and
    /// comes back with it when the office is filled again.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EdictStallAndSuspensionTests : ContentTestBase
    {
        public EdictStallAndSuspensionTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static EdictDef StewardEdict() => new EdictDef
        {
            defName = "TestStewardStallEdict",
            label = "test steward stall edict",
            requiredOffice = OfficeDefOf.Steward,
            prioritizedWork = new List<WorkTypeDef> { WorkTypeDefOf.Construction },
        };

        /// <summary>The settled world <c>EdictOfficeTests</c> uses: a founded town whose offices are seated.</summary>
        private static Settlement SettledWorld(string seed)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 3, soloStart: true);
            Find.World = world;
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(t => !taken.Contains(t) && !world.grid.Tiles[t].WaterCovered);
            Settlement town = SettlementFounder.Found(world, tile, world.factions[0], 20, new RandomStream(7), "Stallhold");
            OfficeManager.ReconcileWorld(world);
            return town;
        }

        [Fact]
        public void A_suspended_edict_says_it_is_suspended_and_the_stall_returns_only_once_it_is_in_force()
        {
            Settlement town = SettledWorld("edict-stall-suspension");
            CoreWorld world = Find.World!;
            EdictDef edict = StewardEdict();
            Assert.True(Find.God.Activate(edict));

            foreach (Pawn p in town.Citizens.Where(p => !p.Dead))
            {
                GodCommands.SetWorkPriority(p.thingIDNumber, "Construction", 0);
            }

            string stall = "nobody is allowed to do " + (WorkTypeDefOf.Construction.gerundLabel ?? WorkTypeDefOf.Construction.label);
            EdictOption inForce = EdictOption.For(edict, Find.God);
            Assert.Equal(EdictAvailability.Active, inForce.Availability);
            Assert.Contains(stall, inForce.Reason);

            OfficeManager.HolderIn(OfficeDefOf.Steward, town)!.health.Kill(null, null);
            OfficeManager.ReconcileWorld(world);
            Find.God.ReconcileSuspensions();

            EdictOption suspended = EdictOption.For(edict, Find.God);
            Assert.Equal(EdictAvailability.Suspended, suspended.Availability);
            Assert.StartsWith("Suspended.", suspended.Reason);
            Assert.Contains("Needs a seated Steward.", suspended.Reason);
            Assert.DoesNotContain("nobody is allowed", suspended.Reason);

            OfficeManager.ReconcileWorld(world); // a successor takes the seat
            Find.God.ReconcileSuspensions();

            EdictOption resumed = EdictOption.For(edict, Find.God);
            Assert.Equal(EdictAvailability.Active, resumed.Availability);
            Assert.Contains(stall, resumed.Reason);
        }

        [Fact]
        public void An_edict_in_force_that_someone_may_carry_out_reads_plainly_in_force()
        {
            SettledWorld("edict-stall-clear");
            EdictDef edict = StewardEdict();
            Assert.True(Find.God.Activate(edict));

            Assert.Equal("In force.", EdictOption.For(edict, Find.God).Reason);
        }
    }
}
