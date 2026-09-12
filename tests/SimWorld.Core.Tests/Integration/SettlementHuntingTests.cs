using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>A test that watches the game rather than a module.</b> It founds a settlement the ordinary way —
    /// <see cref="Game.NewGame"/> on the tribal scenario, opened through the god seam — and asks the
    /// question a suite of hunting tests could not: <i>is there anything out there to hunt?</i>
    ///
    /// <para/><b>Why it exists.</b> For the whole history of this project the answer was no. Nothing
    /// anywhere in <c>src/</c> had ever spawned a wild animal on an interior map, so
    /// <see cref="WorkGiver_Hunt.PotentialWorkThingsGlobal"/> — which walks the map's pawns looking for an
    /// unowned animal — yielded nothing on every map ever generated, for ever, and so did
    /// <see cref="WorkGiver_TameAnimals"/> beside it. Hunting, taming, training and husbandry were all
    /// built, wired, tested and green, and every one of them was unreachable from a real game. It is the
    /// same defect shape as the wild food plants and the growing zone before it: content authored
    /// (<see cref="BiomeDef.animalDensity"/>, for all fourteen shipped biomes), mechanism built, and nothing
    /// in <c>src/</c> ever creating the thing. Every unit test of the hunting mechanism passed throughout,
    /// because they spawn their own animals.
    ///
    /// <para/><b>What this asserts.</b> That the land a settlement is founded on carries game, in a band its
    /// own biome sets; that every animal on it is lawful prey; that a real founded citizen — armed by the
    /// ordinary path, nobody's weapon placed by hand — is offered a real, reachable, reservable hunt job;
    /// and that the wilderness is renewable rather than a one-shot scatter, so a hunted-out map is not bare
    /// for the rest of the game.
    ///
    /// <para/><b>What it deliberately does not assert, and this is the finding.</b> It does not assert that
    /// a hunt is ever <i>taken</i>, because on a settlement founded the ordinary way it is not — measured
    /// over twelve in-game days, zero hunt jobs, while twenty-eight taming jobs were started on the same
    /// animals. Two links past this lane's hold it shut, both measured and neither touched here:
    /// <list type="number">
    /// <item><b>The food gate never opens.</b> <see cref="WorkGiver_Hunt.ShouldSkip"/> defers to
    /// <see cref="HuntingInitiative.WantsMeat"/> — hunt only while short of food — and a founded band walks
    /// in with <c>Economy.SettlementLarderTuning.ProvisionNutritionPerMouth</c> of rations, which is
    /// <c>(DaysToFirstHarvest + DaysOfFoodWanted)</c> days' worth against a gate set at
    /// <see cref="HuntingTuning.DaysOfFoodWanted"/>: 440 nutrition against a want of 160, by construction.
    /// Worse, that ledger is never drawn down while the settlement has a map — <c>Economy.SettlementLarder</c>
    /// feeds only citizens with no map to eat on — so the rations sit in
    /// <see cref="HuntingInitiative.NutritionAvailable"/> for ever and the gate is shut from tick 0 onward.
    /// Measured: <c>WantsMeat</c> false on 12,000 of 12,000 samples across twelve days.</item>
    /// <item><b>Taming takes every animal first.</b> <c>Handling</c> is naturalPriority 950 against
    /// <c>Hunting</c>'s 850, and <see cref="WorkGiver_TameAnimals"/> is gated on nothing at all — every wild
    /// animal is taming work, always — so the hunt giver is never even consulted. Measured with the food
    /// gate forced open by hand: still zero hunts in ten days.
    /// <see cref="WorkGiver_Hunt"/>'s own doc says this ordering "needed no special-casing here"; that is
    /// what this measurement contradicts.</item>
    /// </list>
    /// Asserting a hunt here would be asserting those two are healthy and would go red for reasons a
    /// wildlife test cannot explain — the same line <c>SettlementFoodTests</c> draws about the population.
    /// See this lane's report and <c>docs/WORK-REGISTER.md</c> §9a.
    /// </summary>
    public class SettlementHuntingTests : ContentTestBase
    {
        public SettlementHuntingTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Days = 7;

        [Fact]
        public void A_founded_settlement_has_game_on_its_land_and_is_offered_the_hunt()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "settlement-hunting",
                subdivisionOverride: 3, soloStart: true, bandSize: 25);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;
            Tile tile = game.World!.grid.Tiles[settlement.tile];

            // Before a single tick: the land a settlement is founded on has to carry game. This was zero on
            // every generated map in the project's history.
            List<Pawn> atGeneration = WildAnimalsOn(map);
            Assert.True(atGeneration.Count > 0,
                "a freshly generated interior on a " + tile.biome!.defName + " tile (animalDensity "
                + tile.biome.animalDensity + ") carried no wild animal at all, so the Hunt and Handling work "
                + "types can never produce a job in a real game");

            // And it carries about as much as the biome says, not an arbitrary handful. A band, not a count:
            // stocking fills a weight budget, so the last animal placed may overshoot by its own weight, and
            // a settlement's map — rock, ruins, roads — can come up short when its cell tries miss.
            float desired = WildAnimalSpawner.DesiredAnimalWeight(map, tile);
            float standing = WildAnimalSpawner.CurrentAnimalWeight(map);
            Assert.InRange(standing, desired * 0.5f, desired + HeaviestKindWeight());

            foreach (Pawn animal in atGeneration)
            {
                Assert.True(HuntUtility.IsHuntableAnimal(animal),
                    animal.kindDef!.defName + " stands on a generated map but is not lawful hunting work");
            }

            // The link this lane exists to close, asked of the work giver itself and of a citizen the
            // ordinary founding path produced — nobody's bow placed by hand.
            var giver = new WorkGiver_Hunt();
            int offered = 0;
            foreach (Pawn citizen in settlement.Citizens)
            {
                if (citizen.Dead || !HuntUtility.HasHuntingWeapon(citizen)) continue;
                if (citizen.WorkTagIsDisabled(WorkTags.Violent)) continue;
                List<Thing> prey = giver.PotentialWorkThingsGlobal(citizen).ToList();
                if (prey.Count == 0) continue;
                if (prey.Any(t => giver.HasJobOnThing(citizen, t) && giver.JobOnThing(citizen, t) != null)) offered++;
            }
            Assert.True(offered > 0,
                "no armed citizen of a founded settlement is offered a hunt: PotentialWorkThingsGlobal is "
                + "empty, or nothing it yields is reachable and reservable");

            // A week of the ordinary loop, nothing called by hand. Wild animals are Pawns with needs, minds
            // and a think tree of their own, and this is what proves they survive contact with the real tick
            // path rather than only with a test's.
            for (int day = 0; day < Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
            }
            Assert.Contains(settlement.Citizens, p => !p.Dead);

            // The renewal half, asked of the settlement's own map: take everything off the land and let it
            // answer. Without this the wildlife is a one-shot scatter — the wild-plant defect over again,
            // where a map cleared once stays bare for the rest of the game.
            foreach (Pawn animal in AnimalsOn(map)) animal.DeSpawn();
            Assert.Empty(WildAnimalsOn(map));

            RunSpawnerTicks(map, (int)(WildAnimalTuning.RepopulateDays * GenDate.TicksPerDay));

            Assert.True(WildAnimalsOn(map).Count > 0,
                "a map hunted out of game did not restock within its own repopulate window, so hunting is a "
                + "windfall rather than a renewable source");
        }

        private static List<Pawn> AnimalsOn(SimWorld.Map.Map map) =>
            map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal && !p.Dead).ToList();

        private static List<Pawn> WildAnimalsOn(SimWorld.Map.Map map) =>
            AnimalsOn(map).Where(p => p.faction == null).ToList();

        /// <summary>Advances the clock and runs only the spawner: the rest of <c>MapTick</c> is other
        /// modules' business, and a multi-day window of the whole game loop would cost minutes.</summary>
        private static void RunSpawnerTicks(SimWorld.Map.Map map, int ticks)
        {
            int start = Find.TickManager.TicksGame;
            for (int t = 1; t <= ticks; t++)
            {
                Find.TickManager.DebugSetTicksGame(start + t);
                WildAnimalSpawner.WildAnimalSpawnerTick(map);
            }
        }

        private static float HeaviestKindWeight() =>
            WildAnimalTuning.WildAnimalKinds().Max(WildAnimalTuning.AnimalWeightOf);
    }
}
