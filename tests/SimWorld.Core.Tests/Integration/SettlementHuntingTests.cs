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
    /// <para/><b>It used to stop there, and that was the finding.</b> This class's doc used to say in as many
    /// words that it deliberately did not assert a hunt was ever <i>taken</i>, because on a settlement founded
    /// the ordinary way it was not: twelve in-game days, zero hunt jobs, sixty-six taming jobs on the same
    /// animals. Two links past that lane's scope held it shut and both are closed now — see
    /// <see cref="A_settlement_that_needs_meat_hunts_and_the_meat_reaches_it"/>, which is the assertion that
    /// used to be impossible:
    /// <list type="number">
    /// <item><b>The food gate never opened.</b> <see cref="HuntingInitiative.WantsMeat"/> counted the
    /// abstract <c>Settlement.Stores</c> ledger, which a founding band is credited hundreds of nutrition
    /// into and which <b>nothing draws down while the settlement has a map</b> —
    /// <c>Economy.SettlementLarder</c> feeds only citizens who are not spawned. Measured false on 12,000 of
    /// 12,000 samples. The gate reads <see cref="HuntingInitiative.NutritionReachable"/> now — food a pawn
    /// standing on the map could walk to — and that method's own doc carries the argument and names the
    /// unbanking gap that would let it count both again.</item>
    /// <item><b>Taming took every animal first.</b> <c>Handling</c> is naturalPriority 950 against
    /// <c>Hunting</c>'s 850, and <see cref="WorkGiver_TameAnimals"/> was gated on nothing at all, so every
    /// wild animal was unconditional higher-priority work and the hunt giver was never consulted.
    /// <see cref="TamingInitiative"/> is the missing predicate — while a settlement is short of food the
    /// animals on its land are meat rather than livestock — and no priority anywhere was touched.</item>
    /// </list>
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

        /// <summary>
        /// <b>The assertion this file could not make before.</b> A settlement founded the ordinary way, ticked
        /// a week with nothing called by hand, has to actually hunt: a hunt job taken by a real citizen, and
        /// meat on the ground where there was none.
        ///
        /// <para/><b>The condition is genuine, not arranged.</b> A freshly generated interior carries no
        /// ingestible <c>Thing</c> at all — the wild food on it is standing <c>Plant_Berry</c>, which is a
        /// Plant and not an Item — so a founded band really is short of food on day one, and the test asserts
        /// that before it ticks anything. Nothing here forces a gate, spawns a weapon, or places prey.
        ///
        /// <para/><b>What each failure would mean</b>, so this goes red for a reason a hunting test can
        /// explain: no prey means <c>MapGen.GenStep_Animals</c>/<c>WildAnimalSpawner</c>; no armed citizen
        /// means <c>PawnKindDef.weaponTags</c> or <c>PawnWeaponGenerator</c>; no hunt taken with both present
        /// means the gate (<see cref="HuntingInitiative.WantsMeat"/>) or the ordering against Handling
        /// (<see cref="TamingInitiative"/>); a hunt taken and no meat means
        /// <see cref="JobDriver_Hunt"/>'s kill or its butchery toil.
        ///
        /// <para/><b>Bands and orderings, never counts.</b> How many hunts a week produces depends on the
        /// biome's animal density, how many tribespeople the weapon generator armed and what else they had to
        /// do, so the assertions are "at least one", "more than none" and "never while", which is what the
        /// behaviour actually claims.
        /// </summary>
        [Fact]
        public void A_settlement_that_needs_meat_hunts_and_the_meat_reaches_it()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "settlement-hunt-closes",
                subdivisionOverride: 3, soloStart: true, bandSize: 25);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;

            // The two preconditions a hunt needs, asserted separately so a failure names which one is gone.
            Assert.True(WildAnimalsOn(map).Count > 0, "no prey on a freshly generated interior");
            Assert.True(settlement.Citizens.Any(c => !c.Dead && HuntUtility.HasHuntingWeapon(c)
                    && !c.WorkTagIsDisabled(WorkTags.Violent)),
                "no citizen of a founded band can hunt: nobody is carrying a ranged weapon");

            // And the condition the whole thing is for: this settlement genuinely needs meat, before a tick.
            Assert.True(HuntingInitiative.WantsMeat(settlement, map),
                "a founded band standing on an interior with nothing edible on it must read as short of food");

            var lastJob = new Dictionary<Pawn, Job?>();
            int huntsTaken = 0;
            int tamesTaken = 0;
            int tamesTakenWhileShortOfFood = 0;
            int peakMeat = 0;

            for (int tick = 0; tick < Days * GenDate.TicksPerDay; tick++)
            {
                game.TickManager.DoSingleTick();

                IReadOnlyList<Pawn> onMap = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < onMap.Count; i++)
                {
                    Pawn pawn = onMap[i];
                    if (!pawn.RaceProps.Humanlike) continue;
                    Job? cur = pawn.jobs?.curJob;
                    lastJob.TryGetValue(pawn, out Job? previous);
                    if (ReferenceEquals(cur, previous)) continue;
                    lastJob[pawn] = cur;
                    if (cur == null) continue;
                    if (cur.def == HuntingDefOf.Hunt)
                    {
                        huntsTaken++;
                    }
                    else if (cur.def == JobDefOf.Tame)
                    {
                        tamesTaken++;
                        if (HuntingInitiative.WantsMeat(settlement, map)) tamesTakenWhileShortOfFood++;
                    }
                }

                // Sampled across the run rather than read at the end: meat is food, and a town that needed it
                // enough to go hunting eats it, hauls it and cooks it. What is being asserted is that it
                // existed, not that it was left lying there.
                int meat = MeatOn(map);
                if (meat > peakMeat) peakMeat = meat;
            }

            Assert.True(huntsTaken > 0,
                "a settlement with prey on its land, an armed citizen and an empty larder took no hunting job "
                + "in a whole week — the chain is open at the work giver, not at the wildlife ("
                + tamesTaken + " taming jobs were taken over the same week)");

            Assert.True(peakMeat > 0,
                huntsTaken + " hunting jobs were taken and no meat ever reached the map: the kill or the "
                + "butchery toil is what is broken, not the decision to hunt");

            // The ordering, stated exactly: a settlement short of food does not spend its handlers on
            // livestock. This is the assertion that replaces the naturalPriority argument, and it is measured
            // at the moment each taming job started rather than averaged over the week.
            Assert.Equal(0, tamesTakenWhileShortOfFood);
        }

        private static int MeatOn(SimWorld.Map.Map map)
        {
            int units = 0;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(SimWorld.Map.ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].def.IsMeat) units += items[i].stackCount;
            }
            return units;
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
