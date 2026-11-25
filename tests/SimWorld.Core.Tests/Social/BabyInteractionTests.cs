using System.Collections.Generic;
using System.Linq;

using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Social;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Social
{
    /// <summary>
    /// A baby is not insulted. The fourth playtest's babies carried the <c>Insulted</c> memory at about -9 mood: the
    /// interaction sweep took every living humanlike citizen, babies included, as initiator and recipient. RimWorld
    /// keeps a baby out of random interactions both ways (recalled: the baby life stage cannot initiate one, and a
    /// downed pawn, which a baby always is, cannot receive one).
    /// </summary>
    [Collection("GlobalDefs")]
    public class BabyInteractionTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public BabyInteractionTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        [Fact]
        public void A_baby_in_a_real_band_is_never_insulted_and_never_talks_while_the_adults_still_do()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "baby-insult", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;

            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: settlement.faction);
            Pawn adult = settlement.Citizens.First(p => p.Spawned && !p.Downed);
            baby.relations.familyId = adult.relations.familyId;
            settlement.AddCitizen(baby);
            IntVec3 cell = map.AllCells.OrderBy(c => (c - adult.Position).LengthHorizontalSquared)
                .First(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0);
            GenSpawn.Spawn(baby, cell, map);
            Find.TickManager.DoSingleTick();
            Assert.Equal(DevelopmentalStage.Baby, baby.ageTracker.CurLifeStage!.developmentalStage);

            // The sweep the game runs every 2,500 ticks, run back to back: about 150 game days of interactions.
            var manager = new SocialInteractionManager();
            for (int round = 0; round < 3600; round++) manager.RunInterval(settlement.Citizens);

            List<Pawn> adults = settlement.Citizens.Where(p => !ReferenceEquals(p, baby) && !p.Dead).ToList();
            int adultInsults = adults.Sum(p => p.needs.mood!.thoughts.memories.Memories.Count(m => ReferenceEquals(m.def, SocialThoughtDefOf.Insulted)));
            IReadOnlyList<Thought_Memory> babyMemories = baby.needs.mood?.thoughts.memories.Memories ?? new List<Thought_Memory>();
            output.WriteLine("adult insult memories " + adultInsults + "; baby memories " + string.Join(", ", babyMemories.Select(m => m.def.defName)));

            Assert.True(adultInsults > 0, "the sweep ran but no adult was ever insulted, so the test proves nothing");
            Assert.DoesNotContain(babyMemories, m => ReferenceEquals(m.def, SocialThoughtDefOf.Insulted));
            Assert.DoesNotContain(babyMemories, m => m.otherPawn != null); // nobody talked to it at all
            Assert.DoesNotContain(adults.SelectMany(p => p.needs.mood!.thoughts.memories.Memories), m => ReferenceEquals(m.otherPawn, baby));
        }
    }
}
