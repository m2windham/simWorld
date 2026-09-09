using System.Collections.Generic;
using System.Linq;

using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Social;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.World;
using Xunit;

using CoreScenario = SimWorld.Scenario.Scenario;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// Who the social sweep is allowed to pair up. It rolls chitchat, insults, courtship and falling-out
    /// between people, so its scope decides who can have a relationship at all — and once
    /// <c>World.EmergenceManager</c> started founding rival civilizations, a whole-world sweep meant most
    /// candidate pairs were two strangers on opposite sides of the planet who have never met.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SocialScopeTests : ContentTestBase
    {
        public SocialScopeTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void People_socialize_with_the_people_they_live_among_and_not_with_another_civilization()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "social-scope", subdivisionOverride: 3, soloStart: true);
            Settlement home = game.World!.worldObjects.OfType<Settlement>().First();

            // A second civilization, far away, with its own people.
            var abroad = new Settlement(WorldObjectDefOf.Settlement, home.tile + 137, null, "Abroad", 0);
            for (int i = 0; i < 6; i++) abroad.AddCitizen(PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist)));
            game.World.worldObjects.Add(abroad);

            var here = new HashSet<Pawn>(home.Citizens);
            var there = new HashSet<Pawn>(abroad.Citizens);
            Assert.NotEmpty(here);
            Assert.NotEmpty(there);

            // Long enough for many sweeps: the interval is 2,500 ticks.
            for (int i = 0; i < 30_000; i++) game.TickManager.DoSingleTick();

            foreach (Pawn pawn in here.Concat(there))
            {
                foreach (Thought_Memory memory in pawn.needs.mood!.thoughts.memories.Memories)
                {
                    if (memory.otherPawn == null) continue;
                    bool sameSettlement = (here.Contains(pawn) && here.Contains(memory.otherPawn))
                        || (there.Contains(pawn) && there.Contains(memory.otherPawn));
                    Assert.True(sameSettlement,
                        pawn.Label + " has a social memory about " + memory.otherPawn.Label + ", who lives in another civilization");
                }
            }

            // And the sweep really did run — a scope test that passes because nothing happened proves nothing.
            Assert.Contains(here, p => p.needs.mood!.thoughts.memories.Memories.Any(m => m.otherPawn != null));
        }
    }
}
