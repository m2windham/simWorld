using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// A raid letter's "jump to" goes where the raiders are now. The host asked for it: the letter carried only
    /// where they came in, and by the time anyone read it they were in the middle of the settlement. RimWorld's
    /// raid letter looks at the raiders themselves (its <c>LookTargets</c>).
    /// </summary>
    [Collection("GlobalDefs")]
    public class LetterLookTargetsNowTests : ContentTestBase
    {
        public LetterLookTargetsNowTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        [Fact]
        public void A_raid_letter_says_where_each_raider_stands_now()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "letter-look", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;

            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;
            var raiders = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough", "F_Rough");
            Assert.True(worker.TryExecute(new IncidentParms { target = new CivilizationTarget { Map = map }, points = 300f, faction = raiders }));
            var squad = worker.LastRaidPawns!.Where(p => p.Spawned).ToList();
            Assert.NotEmpty(squad);

            LetterView Raid() => GodViewSnapshot.Capture().Letters.Single(l => l.Label.StartsWith("Raid: "));
            void AssertWhereTheyStand(LetterView letter)
            {
                var spawned = squad.Where(p => p.Spawned).ToList();
                Assert.Equal(spawned.Count, letter.LookTargetsNow.Count);
                foreach (Pawn p in spawned)
                {
                    LookTargetNow at = Assert.Single(letter.LookTargetsNow, t => t.ThingId == p.thingIDNumber);
                    Assert.Equal(p.Position, at.Cell);
                    Assert.Equal(map.tile, at.Tile);
                }
            }

            AssertWhereTheyStand(Raid());
            // One raider walks into the settlement: the letter follows them there, not back to where they came in.
            Pawn walker = squad[0];
            IntVec3 arrivedAt = walker.Position;
            IntVec3 inside = settlement.Citizens.First(p => p.Spawned && p.Map == map).Position;
            Assert.NotEqual(arrivedAt, inside);
            walker.DeSpawn();
            GenSpawn.Spawn(walker, inside, map);

            LetterView later = Raid();
            AssertWhereTheyStand(later);
            Assert.Equal(inside, Assert.Single(later.LookTargetsNow, t => t.ThingId == walker.thingIDNumber).Cell);
        }
    }
}
