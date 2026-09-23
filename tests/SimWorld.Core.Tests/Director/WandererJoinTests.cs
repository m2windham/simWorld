using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <b>A wanderer who actually joins.</b>
    ///
    /// <para/><b>The defect these pin.</b> <c>IncidentWorker_WandererJoin</c>'s entire body was
    /// <c>=&gt; true</c>. The <c>WandererJoin</c> <c>IncidentDef</c> ships, the storyteller selects it, and on
    /// every firing the incident spent its twelve-day refire timer, was written to the chronicle and reported
    /// success — while the population did not move. That is the same shape as <c>ManhunterPack</c>'s and
    /// <c>Disease</c>'s own predecessors and the one this project cares most about: the instrumentation was
    /// being told a person had arrived who did not exist, so the probe's <c>pop</c> column was lying about the
    /// pressure the player was under.
    ///
    /// <para/><b>Why it is the loop and not one incident.</b> <c>docs/design/the-loop.md</c> makes pressure
    /// scale with the player's own growth. Pressure with no counterweight is a slope; in RimWorld the
    /// counterweight to losing colonists is the stream of incidents that give you people, and this is the only
    /// one of those this port ships.
    ///
    /// <para/><b>What these assert.</b> Not that a method returned true — every one of these would have passed
    /// against the stub if it did. That a real, named, living citizen exists afterwards; that the settlement's
    /// own population count and <c>CivilizationTarget.PlayerPawnsForStoryteller</c> both see them; that they
    /// belong to the civilization they joined, found a household in it, and carry gear it could have made;
    /// that a watched settlement gets them standing on its map and an unwatched one gets them just the same;
    /// that the same seed produces the same person; that the worker refuses — returning false and telling the
    /// chronicle nothing — when there is nowhere to join; and that they survive a save.
    ///
    /// <para/><b>Each of them was made to fail.</b> Against the stub, twelve of the fifteen below fail; the
    /// three that pass are the three that assert the incident does <i>not</i> do something (leaves the ambient
    /// stream alone, and ablated leaves everything alone), which a worker that does nothing satisfies for free.
    /// Every remaining line of the worker was then removed one at a time — the two refusals, the ablation gate,
    /// the <c>Rand.PushState</c> scope, each of the three calls the arrival is made of, the map sync, the
    /// letter, the chronicle line, the request's faction, and the world-seeded stream — and each removal was
    /// checked to fail a named test here. Two did not, the first time round, and the two tests that now catch
    /// them (<see cref="The_wanderer_founds_a_household_the_way_every_other_arrival_does"/> and
    /// <see cref="A_wanderer_arrives_with_gear_their_new_civilization_could_have_made"/>) exist because of it.
    /// </summary>
    public class WandererJoinTests : ContentTestBase
    {
        public WandererJoinTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();

            // Names are unique per *process*, not per game: PawnBioAndNameGenerator retries until
            // NameUseChecker has not seen the full name before, and that registry is static. Without this a
            // test's wanderer is named around whoever every earlier test in the run happened to name — the
            // same reason SettlementTests clears it in its own constructor.
            NameUseChecker.Clear();
        }

        // -------------------------------------------------------------------------------------------
        // Fixtures.
        // -------------------------------------------------------------------------------------------

        private static IncidentDef Wanderer => DefDatabase<IncidentDef>.GetNamed("WandererJoin");

        /// <summary>The live worker instance the def caches, so a test can read what the last firing produced
        /// without re-deriving it — the same hook <c>IncidentWorker_TraderCaravanArrival</c>'s tests use.</summary>
        private static IncidentWorker_WandererJoin Worker =>
            (IncidentWorker_WandererJoin)Wanderer.Worker;

        /// <summary>
        /// A world this thread is running, with one settlement on it and <paramref name="citizens"/> people
        /// living there. Small on purpose (subdivision 2): nothing below reads terrain. The world is what
        /// makes the wanderer's <c>NamedRand</c> stream seed-bound, which is what the determinism tests are
        /// about — <c>NamedRand.For</c> derives from <c>Find.World.info.seedString</c>.
        /// </summary>
        private (CoreWorld world, Settlement settlement) PosedWorld(string seed, int citizens = 4)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "WandererWorld", 2, soloStart: true);
            Find.World = world;

            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => !world.grid.Tiles[i].WaterCovered && !taken.Contains(i));

            var home = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Home", "F_Home");
            world.factions.Add(home);

            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, home, "Hearth", 0);
            world.worldObjects.Add(settlement);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Resident" + i));
            return (world, settlement);
        }

        private static CivilizationTarget Civ(Settlement settlement)
        {
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            return target;
        }

        private static IncidentParms Onto(Settlement settlement) =>
            new IncidentParms { target = Civ(settlement) };

        /// <summary>Everything about one arrival that could differ between two rolls, in one comparable string.
        /// Name alone could collide between seeds; name plus a biological age drawn from a continuous band
        /// effectively cannot, which is what keeps the "a different seed produces a different wanderer" test
        /// from being a coin flip.</summary>
        private static string Signature(Pawn pawn) =>
            (pawn.Name?.ToStringFull ?? pawn.Label)
            + "|" + pawn.ageTracker.AgeBiologicalYearsFloat.ToString("F4", CultureInfo.InvariantCulture)
            + "|" + pawn.gender;

        // -------------------------------------------------------------------------------------------
        // The thing it exists to stop being: somebody is actually there afterwards.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The whole point, on the watched path. Every assertion below the first would have failed against the
        /// stub, which returned true and produced nobody.
        /// </summary>
        [Fact]
        public void A_watched_settlement_gains_a_real_named_living_citizen()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("wanderer-watched");
            CoreMap map = settlement.EnterMap(world);
            Find.God.Attention.Focus(settlement);

            int before = settlement.TotalPopulation;
            CivilizationTarget target = Civ(settlement);

            bool fired = Wanderer.Worker.TryExecute(new IncidentParms { target = target });

            Assert.True(fired);

            Pawn? wanderer = Worker.LastWanderer;
            Assert.NotNull(wanderer);

            // A real person: alive, humanlike, and carrying a name of their own rather than a species label.
            Assert.False(wanderer!.Dead);
            Assert.True(wanderer.RaceProps.Humanlike);
            Assert.False(string.IsNullOrWhiteSpace(wanderer.Label));
            Assert.NotEqual(Human.label, wanderer.Label);

            // The worker says where it put them, so a bench or a host does not have to search the civilization
            // for the new face.
            Assert.Same(settlement, Worker.LastSettlement);

            // Countable by everything that counts citizens.
            Assert.Equal(before + 1, settlement.TotalPopulation);
            Assert.Contains(wanderer, settlement.Citizens);
            Assert.Contains(wanderer, target.PlayerPawnsForStoryteller);

            // And standing on the map the player has open — the settlement's own citizen/map seam placed
            // them, not this worker reaching for GenSpawn itself.
            Assert.True(wanderer.Spawned);
            Assert.Same(map, wanderer.Map);
        }

        /// <summary>
        /// Joining a town is joining its civilization. <c>Settlement.AddCitizen</c>'s own doc records why this
        /// matters rather than being tidiness: hostility rests on both sides carrying a faction, so a citizen
        /// with none makes a raid on their home a pantomime.
        /// </summary>
        [Fact]
        public void The_wanderer_belongs_to_the_civilization_they_joined()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-faction");

            Wanderer.Worker.TryExecute(Onto(settlement));

            Assert.NotNull(Worker.LastWanderer);
            Assert.Same(settlement.faction, Worker.LastWanderer!.faction);
        }

        /// <summary>
        /// A wanderer arrives with a household of their own, not as a familyless pawn on a roster. This is the
        /// third of the three calls the arrival is made of and the one with no visible effect on the day — so
        /// it is the one that would be quietly dropped. It is not decoration: <c>FamilyManager</c> is where the
        /// population comes from, and every path into a settlement that is not birth founds a household on
        /// arrival (<c>World.SettlementFounder.PairIntoHouseholds</c> for the founding band,
        /// <c>MigrationManager.MigrationTick</c> for a migrant). A citizen whose <c>familyId</c> is
        /// <c>None</c> is a citizen no <c>Family</c> counts among its living, which is the demographic
        /// equivalent of not being there.
        /// </summary>
        [Fact]
        public void The_wanderer_founds_a_household_the_way_every_other_arrival_does()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-household");

            Assert.True(Wanderer.Worker.TryExecute(Onto(settlement)));
            Pawn wanderer = Worker.LastWanderer!;

            Assert.NotEqual(Pawn_RelationsTracker.None, wanderer.relations.familyId);

            Family? household = Find.FamilyManager.GetFamily(wanderer.relations.familyId);
            Assert.NotNull(household);
            Assert.Equal(wanderer.thingIDNumber, household!.founderIdA);
            Assert.Equal(Pawn_RelationsTracker.None, household.founderIdB);
            Assert.Equal(1, household.livingCount);
            Assert.False(string.IsNullOrEmpty(household.surname));
        }

        /// <summary>
        /// A wanderer arrives carrying what the civilization they joined could have made — a neolithic town
        /// does not acquire a revolver by someone walking in with one.
        ///
        /// <para/><b>Why this needs its own test rather than resting on <c>AddCitizen</c>.</b> The settlement's
        /// faction reaches the pawn twice over: once through <c>PawnGenerationRequest.faction</c> and once
        /// through <c>Settlement.AddCitizen</c> afterwards. Only the first has this consequence —
        /// <c>PawnWeaponGenerator</c> and <c>PawnApparelGenerator</c> read <c>request.Faction</c>, not
        /// <c>pawn.faction</c>, to cap candidates at <c>ThingDef.techLevel &lt;= faction.def.techLevel</c>, and
        /// with no faction on the request there is no ceiling at all. So dropping the request's faction leaves
        /// every faction assertion in this file passing and changes only what the arrival is holding, which is
        /// exactly the kind of regression nothing notices. <c>SettlementFounder</c> records the same reasoning
        /// for the founding band; this pins it for the one other door into a settlement.
        ///
        /// <para/>Fired repeatedly, at a different tick each time so each firing draws a different stream:
        /// shipped content puts <c>Bow_Short</c> (neolithic) and <c>Gun_Revolver</c> (industrial) both inside
        /// <c>Tribesperson</c>'s weapon budget, so one uncapped arrival in two is armed above its era and a
        /// single firing would be a coin toss rather than a test.
        /// </summary>
        [Fact]
        public void A_wanderer_arrives_with_gear_their_new_civilization_could_have_made()
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                "wanderer-techlevel", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "WandererWorld", 2, soloStart: true);
            Find.World = world;

            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => !world.grid.Tiles[i].WaterCovered && !taken.Contains(i));

            FactionDef tribalDef = DefDatabase<FactionDef>.GetNamed("TribalCivilization");
            var tribe = new Faction(tribalDef, "Tribe", "F_Tribe");
            world.factions.Add(tribe);

            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, tribe, "Longhouse", 0);
            world.worldObjects.Add(settlement);

            var arrivals = new List<Pawn>();
            for (int i = 0; i < 12; i++)
            {
                Find.TickManager.DebugSetTicksGame(1000 * (i + 1));
                Assert.True(Wanderer.Worker.TryExecute(Onto(settlement)));
                arrivals.Add(Worker.LastWanderer!);
            }

            foreach (Pawn arrival in arrivals)
            {
                foreach (ThingWithComps gear in arrival.equipment.AllEquipmentListForReading)
                {
                    Assert.True(
                        gear.def.techLevel <= tribalDef.techLevel,
                        $"{arrival.Label} walked into a {tribalDef.techLevel} town carrying "
                        + $"{gear.def.defName}, which is {gear.def.techLevel}");
                }
            }

            // The ceiling has to be doing work rather than there being nothing to cap: with it lifted these
            // same twelve draws reach a weapon this town could not make.
            Assert.Contains(arrivals, p => p.equipment.Primary != null);
        }

        /// <summary>
        /// The incident fires whether or not anyone is looking, so the arrival has to work whether or not
        /// anyone is looking. There is no second mechanism for this: <c>AddCitizen</c> is uniform and the
        /// watched/unwatched split lives in <c>Settlement.SyncCitizenSpawns</c>, which has no map to place
        /// anybody on here and correctly does not try.
        /// </summary>
        [Fact]
        public void An_unwatched_settlement_gains_one_too()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-unwatched");
            Find.God.Attention.ClearFocus();
            Assert.Null(settlement.InteriorMap);

            int before = settlement.TotalPopulation;
            CivilizationTarget target = Civ(settlement);

            bool fired = Wanderer.Worker.TryExecute(new IncidentParms { target = target });

            Assert.True(fired);
            Pawn? wanderer = Worker.LastWanderer;
            Assert.NotNull(wanderer);
            Assert.False(wanderer!.Dead);

            Assert.Equal(before + 1, settlement.TotalPopulation);
            Assert.Contains(wanderer, settlement.Citizens);
            Assert.Contains(wanderer, target.PlayerPawnsForStoryteller);

            // Real, and not individually staged: nobody generated a map to stand them on.
            Assert.False(wanderer.Spawned);
            Assert.Null(settlement.InteriorMap);
        }

        /// <summary>
        /// The arrival reaches the storyteller's own view of the civilization, which is the count every threat
        /// this game throws is scaled against. A wanderer the narrator cannot see is a wanderer who does not
        /// raise the pressure they are meant to be relief from.
        /// </summary>
        [Fact]
        public void The_storytellers_own_roster_grows_by_exactly_one()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-roster", citizens: 6);
            CivilizationTarget target = Civ(settlement);

            int before = target.PlayerPawnsForStoryteller.Count();
            Wanderer.Worker.TryExecute(new IncidentParms { target = target });

            Assert.Equal(before + 1, target.PlayerPawnsForStoryteller.Count());
        }

        /// <summary>A person arriving that the player is never told about is a non-event — and this is the
        /// half of the loop that gives something back, so it is the half they most need to notice.</summary>
        [Fact]
        public void The_player_is_told_a_person_arrived_and_told_their_name()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-letter");

            int before = Find.LetterStack.LettersListForReading.Count;
            Wanderer.Worker.TryExecute(Onto(settlement));

            Assert.True(Find.LetterStack.LettersListForReading.Count > before);

            string name = Worker.LastWanderer!.Label;
            Letter letter = Find.LetterStack.LettersListForReading.Last();
            Assert.Contains(name, letter.label + " " + letter.text, System.StringComparison.Ordinal);
            Assert.Same(LetterDefOf.PositiveEvent, letter.def);
        }

        /// <summary>The chronicle knows who, not merely that. A history that records "a wanderer joined" and
        /// cannot say which one has lost the only part of it a player would retell.</summary>
        [Fact]
        public void The_chronicle_names_them()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-chronicle");

            Wanderer.Worker.TryExecute(Onto(settlement));
            string name = Worker.LastWanderer!.Label;

            Assert.Contains(
                Find.Storyteller.Chronicle,
                e => e.incidentDefName != null && e.incidentDefName.Contains(name, System.StringComparison.Ordinal));
        }

        // -------------------------------------------------------------------------------------------
        // Determinism.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The same seed gives the same wanderer, and a different one gives a different wanderer. Both halves
        /// matter: the first is the project's determinism rule, and the second is what proves the first is not
        /// passing because the generator is ignoring its seed entirely.
        ///
        /// <para/><b>Each run starts from a cleared <c>NameUseChecker</c>, and that is the honest setup rather
        /// than a convenience.</b> Two people in one run never share a name — the name generator retries
        /// against a static registry until it finds an unused one — so replaying a seed inside the same
        /// process without clearing it asks for a name the generator has already promised not to hand out
        /// twice. It came back "Alden" then "Nolan" off one seed, with the age and gender identical, which is
        /// the registry doing its job rather than the stream failing to do its own. What determinism can mean
        /// here is: the same seed, against the same "who has been named already" state, produces the same
        /// person.
        /// </summary>
        [Fact]
        public void The_same_seed_produces_the_same_wanderer_and_a_different_seed_does_not()
        {
            string ArrivalOn(string worldSeed)
            {
                NameUseChecker.Clear();
                (CoreWorld _, Settlement settlement) = PosedWorld(worldSeed);
                Find.TickManager.DebugSetTicksGame(5000);
                Assert.True(Wanderer.Worker.TryExecute(Onto(settlement)));
                return Signature(Worker.LastWanderer!);
            }

            string a1 = ArrivalOn("wanderer-seed-a");
            string a2 = ArrivalOn("wanderer-seed-a");
            string b = ArrivalOn("wanderer-seed-b");

            Assert.Equal(a1, a2);
            Assert.NotEqual(a1, b);
        }

        /// <summary>
        /// The incident must not draw from the ambient stream. If it did, switching it on would shift every
        /// later draw in the game and a measured difference between a run with wanderers and a run without
        /// would be mostly that reshuffle. <c>PawnGenerator</c> draws ambiently by the dozen, so this is the
        /// assertion that keeps the <c>Rand.PushState</c> around it from being quietly deleted — see
        /// <c>NamedRand</c>'s own doc and the 33 draws <c>ManhunterPack</c> leaked before a test caught it.
        /// </summary>
        [Fact]
        public void Firing_the_incident_does_not_disturb_the_ambient_random_stream()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-ambient");

            uint before = Rand.Current.Iterations;
            Assert.True(Wanderer.Worker.TryExecute(Onto(settlement)));

            Assert.Equal(before, Rand.Current.Iterations);
        }

        // -------------------------------------------------------------------------------------------
        // The refusal.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// No settlement, no wanderer — and the worker says so rather than reporting success. This is the
        /// exact defect being fixed, so the test that it is not reintroduced in a new place is the one this
        /// file would be incomplete without: returning true here would spend the refire timer and write a
        /// firing to the story state for an arrival that never happened.
        /// </summary>
        [Fact]
        public void It_refuses_and_changes_nothing_when_there_is_nowhere_to_join()
        {
            var target = new CivilizationTarget();
            for (int i = 0; i < 3; i++) target.pawns.Add(NewHuman("Pawn" + i));

            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            int chronicleBefore = Find.Storyteller.Chronicle.Count;

            bool fired = Wanderer.Worker.TryExecute(new IncidentParms { target = target });

            Assert.False(fired);
            Assert.Equal(3, target.PlayerPawnsForStoryteller.Count());
            Assert.Equal(lettersBefore, Find.LetterStack.LettersListForReading.Count);
            Assert.Equal(chronicleBefore, Find.Storyteller.Chronicle.Count);

            // The consequence of returning false that actually matters: nothing downstream believes an
            // incident happened, so the refire timer is not spent on a non-event.
            Assert.False(target.StoryState.HasFired(Wanderer));
        }

        /// <summary>
        /// A target that is not a civilization is refused the same way, and refused <i>quietly</i> — no
        /// exception, no letter, no chronicle line, no firing recorded. <c>CivilizationTarget</c> is the only
        /// <c>IIncidentTarget</c> this port ships, so this branch is unreachable from a running game today and
        /// this fake is the only thing that can walk it; the guard exists because
        /// <c>IIncidentTarget</c>'s own doc says a caravan or a single map is what the interface is shaped for
        /// next, and the day one is added is the day a worker that assumed otherwise starts throwing inside the
        /// storyteller's tick. Pinned rather than trusted: an unreached branch is a branch nobody has checked.
        /// </summary>
        [Fact]
        public void It_refuses_a_target_that_is_not_a_civilization()
        {
            var target = new NotACivilization();

            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            int chronicleBefore = Find.Storyteller.Chronicle.Count;

            bool fired = Wanderer.Worker.TryExecute(new IncidentParms { target = target });

            Assert.False(fired);
            Assert.Equal(lettersBefore, Find.LetterStack.LettersListForReading.Count);
            Assert.Equal(chronicleBefore, Find.Storyteller.Chronicle.Count);
            Assert.False(target.StoryState.HasFired(Wanderer));
        }

        /// <summary>The smallest thing that satisfies <c>IIncidentTarget</c> without being a civilization —
        /// see <see cref="It_refuses_a_target_that_is_not_a_civilization"/> for why one has to exist here.</summary>
        private sealed class NotACivilization : IIncidentTarget
        {
            public StoryState StoryState { get; } = new StoryState();

            public int Tile => 0;

            public float PlayerWealthForStoryteller => 0f;

            public IEnumerable<Pawn> PlayerPawnsForStoryteller => System.Array.Empty<Pawn>();

            public int ConstantRandSeed => 1;

            public IEnumerable<IncidentTargetTagDef> IncidentTargetTags() =>
                System.Array.Empty<IncidentTargetTagDef>();

            public string GetUniqueLoadID() => "NotACivilization";
        }

        // -------------------------------------------------------------------------------------------
        // Ablation.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Switched off, the incident still fires and nobody arrives — which is exactly the stub this worker
        /// replaces, and <c>Ablation</c>'s own doc is worth re-reading on why that coincidence is the point:
        /// an accidental permanent ablation is indistinguishable from a working feature unless somebody is
        /// measuring. An ablated incident that declined to fire would change what the storyteller's weighted
        /// roll lands on and the measured difference would be that reshuffle.
        /// </summary>
        [Fact]
        public void Ablated_it_still_fires_and_nobody_arrives()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-ablated");
            int before = settlement.TotalPopulation;

            Ablation.Disable("WandererJoin");
            try
            {
                bool fired = Wanderer.Worker.TryExecute(Onto(settlement));

                Assert.True(fired, "an ablated incident must still report success, or selection itself changes");
                Assert.Equal(before, settlement.TotalPopulation);
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void Ablated_it_leaves_the_ambient_stream_exactly_where_the_live_one_does()
        {
            (CoreWorld _, Settlement settlement) = PosedWorld("wanderer-ablated-stream");

            Ablation.Disable("WandererJoin");
            try
            {
                uint before = Rand.Current.Iterations;
                Wanderer.Worker.TryExecute(Onto(settlement));
                Assert.Equal(before, Rand.Current.Iterations);
            }
            finally
            {
                Ablation.Clear();
            }
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// A wanderer who joins and is gone after a save is a wanderer who never joined. Saved through the
        /// world, because that is what owns them now — the arrival is a citizen on a settlement's roster, not
        /// state this worker holds, and the round-trip being the settlement's own is the evidence that no
        /// parallel bookkeeping was introduced to carry them.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_keeps_the_wanderer_on_the_roster()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("wanderer-scribe");

            Assert.True(Wanderer.Worker.TryExecute(Onto(settlement)));
            string name = Worker.LastWanderer!.Label;
            int population = settlement.TotalPopulation;

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().Single(s => s.name == "Hearth");
            Assert.Equal(population, reloaded.TotalPopulation);
            Assert.Contains(reloaded.Citizens, c => c.Label == name);
        }
    }
}
