using System;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>A raid tells the player it happened, watched or not (RimWorld: the raid's ThreatBig letter).</summary>
    public class RaidLetterTests : ContentTestBase
    {
        public RaidLetterTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
        }

        private static Faction Raiders() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough", "F_Rough");

        private static IncidentWorker_RaidEnemy NewWorker() =>
            (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;

        private static Settlement Town()
        {
            var town = new Settlement(WorldObjectDefOf.Settlement, 7, null, "Ashford", 0);
            for (int i = 0; i < 8; i++) town.AddCitizen(NewHuman("Ashford" + i));
            return town;
        }

        [Fact]
        public void An_unwatched_raid_sends_one_threat_letter_saying_how_it_ended()
        {
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { Town() });
            IncidentWorker_RaidEnemy worker = NewWorker();

            Assert.True(worker.TryExecute(new IncidentParms { target = target, points = 500f, faction = Raiders() }));

            Letter letter = Assert.Single(Find.LetterStack.LettersListForReading, l => l.label == "Raid: Ashford");
            Assert.Same(LetterDefOf.ThreatBig, letter.def);
            Assert.Contains("Rough", letter.text, StringComparison.Ordinal);
            SettlementRaidOutcome outcome = worker.LastRaidOutcome!.Value;
            Assert.Contains(outcome.Repelled ? "held" : "broke in", letter.text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_raid_arriving_on_a_map_sends_one_threat_letter_and_claims_no_ending()
        {
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            IncidentWorker_RaidEnemy worker = NewWorker();

            Assert.True(worker.TryExecute(new IncidentParms
            {
                target = new CivilizationTarget { Map = map }, points = 500f, faction = Raiders(),
            }));

            Assert.Contains(worker.LastRaidPawns!, p => p.Spawned);
            Letter letter = Assert.Single(Find.LetterStack.LettersListForReading, l => l.def == LetterDefOf.ThreatBig);
            Assert.StartsWith("Raid: ", letter.label, StringComparison.Ordinal);
            Assert.DoesNotContain("Dead:", letter.text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_raid_with_nowhere_to_land_tells_nobody()
        {
            Assert.True(NewWorker().TryExecute(new IncidentParms
            {
                target = new CivilizationTarget(), points = 500f, faction = Raiders(),
            }));

            Assert.Empty(Find.LetterStack.LettersListForReading);
        }
    }
}
