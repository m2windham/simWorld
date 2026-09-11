using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// A settlement's citizens belong to their settlement's civilization. "World" is aliased to
    /// <c>global::SimWorld.World.World</c> throughout, exactly as <see cref="SettlementTests"/> does — this
    /// test namespace's own last segment is also "World", which would otherwise shadow it.
    ///
    /// <para/><b>What was wrong.</b> Hostility rests on both sides carrying a <see cref="Pawn.faction"/>
    /// (<see cref="AttackTargetsUtility.HostileTo"/>), and in this port only raiders had one:
    /// <c>SettlementFounder.GenerateFoundingBand</c> built its <c>PawnGenerationRequest</c> naming no faction,
    /// and <c>Factions.PawnGroupMaker</c> was the only caller in <c>src/</c> that ever passed one. A
    /// settlement had a faction and not one of its people did — so every system that asks who someone belongs
    /// to got "nobody" for the entire civilian population: hostility, custody, doctoring, wardens, taming and
    /// animal training among them.
    ///
    /// <para/><b>The measurement, on a settlement founded the ordinary way from shipped content with a real
    /// <c>RaidEnemy</c> firing onto its interior</b> (see
    /// <see cref="A_raid_on_a_founded_settlement_is_a_fight_on_both_sides"/>):
    /// <list type="bullet">
    /// <item>before — 0 of 15 raiders saw a citizen as hostile, 0 of 30 citizens saw a raider, 0 attack jobs
    /// on either side;</item>
    /// <item>after — 15 of 15 raiders, 30 of 30 citizens, 15 of 15 raiders and 24 of 30 citizens took an
    /// attack job. The six are the pawns whose backstories disable <see cref="WorkTags.Violent"/>; the test
    /// asserts that rather than the count.</item>
    /// </list>
    /// </summary>
    public class CitizenFactionTests : ContentTestBase
    {
        public CitizenFactionTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private sealed class FoundedTown
        {
            public global::SimWorld.World.World World = null!;
            public Settlement Settlement = null!;
            public global::SimWorld.Map.Map Interior = null!;
            public Faction Player = null!;
        }

        /// <summary>Founds a settlement the ordinary way: shipped content, a generated world, the player's own
        /// faction, and whoever <c>SettlementFounder</c> makes. Nothing here enfactions or arms anybody.</summary>
        private static FoundedTown Found(string seed, int bandSize = 30, bool enter = true)
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 4, soloStart: true);
            Faction player = world.factions.First();
            Find.FactionManager.Add(player);
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, player, bandSize, new RandomStream(20260911), "Holdfast");
            return new FoundedTown
            {
                World = world,
                Settlement = settlement,
                Interior = enter ? settlement.EnterMap(world) : null!,
                Player = player,
            };
        }

        /// <summary>A registered faction of <paramref name="defName"/>, at war with the town.</summary>
        private static Faction Hostile(FoundedTown town, string defName, string name)
        {
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(faction);
            town.Player.SetRelationDirect(faction, FactionRelationKind.Hostile, -100);
            return faction;
        }

        /// <summary>Fires a real <see cref="IncidentWorker_RaidEnemy"/> onto the town's interior and hands back
        /// the squad it actually generated and spawned.</summary>
        private static IReadOnlyList<Pawn> FireRaidOnto(FoundedTown town, Faction raiders, float points)
        {
            var target = new CivilizationTarget { Map = town.Interior };
            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestCitizenFactionRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = target, points = points, faction = raiders }));
            return worker.LastRaidPawns!;
        }

        private static IntVec3 FreeCellNear(IntVec3 root, global::SimWorld.Map.Map map)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 1; i < GenRadial.NumCellsInRadius(6); i++)
            {
                IntVec3 candidate = root + pattern[i];
                if (GenGrid.InBounds(candidate, map) && GenGrid.Standable(candidate, map)) return candidate;
            }
            return root;
        }

        private static bool IsAttackJob(Job? job) =>
            job != null
            && (ReferenceEquals(job.def, CombatAIDefOf.AttackMelee) || ReferenceEquals(job.def, CombatAIDefOf.AttackStatic));

        // ---- the measurement ----

        [Fact]
        public void A_raid_on_a_founded_settlement_is_a_fight_on_both_sides()
        {
            // TribalCivilization on purpose: it declares hostileToFactionlessHumanlikes false, so before this
            // lane its raids on a town of factionless citizens found nobody at all. That is the residue
            // AI.FactionlessRaidHostilityTests recorded as "a citizen-faction gap in the World module", and
            // this is that gap closed — the fix is a faction on the citizen, not the flag on the content.
            FoundedTown town = Found("citizen-faction-measurement");
            Faction raiders = Hostile(town, "TribalCivilization", "Tribal");
            Assert.False(raiders.def.hostileToFactionlessHumanlikes, "the shipped content this measurement rests on changed");

            IReadOnlyList<Pawn> squad = FireRaidOnto(town, raiders, points: 600f);
            List<Pawn> citizens = town.Settlement.Citizens.Where(c => c.Spawned).ToList();
            Assert.NotEmpty(squad);
            Assert.NotEmpty(citizens);

            int raidersSeeingACitizen = squad.Count(r => citizens.Any(c => AttackTargetsUtility.HostileTo(r, c)));
            int citizensSeeingARaider = citizens.Count(c => squad.Any(r => AttackTargetsUtility.HostileTo(c, r)));
            Assert.Equal(squad.Count, raidersSeeingACitizen);
            Assert.Equal(citizens.Count, citizensSeeingARaider);

            // Then the half the distance hides: the same real squad, brought into contact. This port has no
            // squad-approach AI (IncidentWorker_RaidEnemy's own doc says so), so a raid lands on a map edge far
            // outside CombatAITuning.TargetAcquireRadius and nobody on either side can act on what they see.
            // Moving them stands in for that missing walk; it does not stand in for the hostility above, which
            // is measured where the raid actually landed.
            for (int i = 0; i < squad.Count; i++)
            {
                Pawn raider = squad[i];
                raider.DeSpawn();
                GenSpawn.Spawn(raider, FreeCellNear(citizens[i % citizens.Count].Position, town.Interior), town.Interior);
            }

            foreach (Pawn p in squad) p.jobs.TryFindAndStartJob();
            foreach (Pawn c in citizens) c.jobs.TryFindAndStartJob();

            Assert.All(squad, r => Assert.True(IsAttackJob(r.jobs.curJob), "a raider stood next to a citizen and went about its day"));

            // Not every citizen fights, and that is RimWorld's rule rather than a shortfall: a pawn whose
            // backstory disables Violent work never takes an attack job. Asserted as "everyone who can, does"
            // instead of as the 24-of-30 this seed happens to produce.
            List<Pawn> ableCitizens = citizens.Where(c => !c.WorkTagIsDisabled(WorkTags.Violent)).ToList();
            Assert.NotEmpty(ableCitizens);
            Assert.All(ableCitizens, c => Assert.True(IsAttackJob(c.jobs.curJob), "a citizen able to fight, with a raider at arm's length, kept working"));
            Assert.All(
                citizens.Where(c => !IsAttackJob(c.jobs.curJob)),
                c => Assert.True(c.WorkTagIsDisabled(WorkTags.Violent), "a citizen who could fight did not"));
        }

        // ---- where the faction comes from: all four doors ----

        [Fact]
        public void Every_citizen_of_a_founding_band_carries_the_settlements_faction()
        {
            FoundedTown town = Found("founding-band-faction", enter: false);

            Assert.NotNull(town.Settlement.faction);
            Assert.NotEmpty(town.Settlement.Citizens);
            Assert.All(town.Settlement.Citizens, c => Assert.Same(town.Player, c.faction));
        }

        [Fact]
        public void A_spawned_citizen_is_filed_under_its_faction_and_not_among_the_factionless()
        {
            // AttackTargetsCache buckets by faction, and its factionlessHumanlikes list used to be "most of the
            // map" on a settlement interior (its own doc said so). The filing has to follow the fix, or the
            // index would offer a scan pawns the live predicate then finds hostile in a bucket nobody visits.
            FoundedTown town = Found("citizen-faction-filing");
            AttackTargetsCache cache = town.Interior.mapPawns.AttackTargets;
            List<Pawn> spawned = town.Settlement.Citizens.Where(c => c.Spawned).ToList();

            Assert.NotEmpty(spawned);
            Assert.Empty(cache.FactionlessHumanlikes);
            Assert.Contains(town.Player, cache.FactionsPresent);
            Assert.All(spawned, c => Assert.Contains(c, cache.PawnsInFaction(town.Player)));
        }

        [Fact]
        public void A_newborn_citizen_is_born_into_its_parents_faction()
        {
            // Births never pass through AddCitizen — FamilyManager.DemographyTick appends a newborn straight
            // onto the roster (Settlement.SyncCitizenSpawns' own doc says so). Without the birth site setting
            // it, a settlement's next generation would arrive factionless among enfactioned founders and the
            // whole defect would come back one generation in.
            FoundedTown town = Found("newborn-faction", bandSize: 20, enter: false);
            var population = town.Settlement.Citizens.ToList();
            foreach (Pawn p in population)
            {
                if (p.needs?.mood != null) p.needs.mood.CurLevel = 1f;
                if (p.needs?.food != null) p.needs.food.CurLevel = 1f;
            }

            int adultCount = population.Count;
            for (int seed = 0; seed < 200 && population.Count == adultCount; seed++)
            {
                Rand.Current = new RandomStream(seed);
                Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks * (seed + 1));
                Find.FamilyManager.DemographyTick(population);
            }

            List<Pawn> newborns = population.Skip(adultCount).ToList();
            Assert.NotEmpty(newborns);
            Assert.All(newborns, n => Assert.Same(town.Player, n.faction));
        }

        [Fact]
        public void A_migrant_who_joins_a_settlement_carries_its_faction()
        {
            FoundedTown town = Found("migrant-faction", bandSize: 20, enter: false);
            foreach (Pawn p in town.Settlement.Citizens)
            {
                if (p.needs?.mood != null) p.needs.mood.CurLevel = 1f;
                if (p.needs?.food != null) p.needs.food.CurLevel = 1f;
            }

            int before = town.Settlement.Citizens.Count;
            for (int seed = 0; seed < 200 && town.Settlement.Citizens.Count == before; seed++)
            {
                Rand.Current = new RandomStream(seed);
                Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks * (seed + 1));
                MigrationManager.MigrationTick(town.Settlement, PawnKindDefOf.Colonist);
            }

            Assert.True(town.Settlement.Citizens.Count > before, "no migrant ever arrived to test");
            Assert.All(town.Settlement.Citizens.Skip(before), m => Assert.Same(town.Player, m.faction));
        }

        [Fact]
        public void A_household_that_departs_keeps_the_civilization_it_lived_under()
        {
            // Settled deliberately rather than left to be discovered. Leaving a town is emigration, not
            // renouncing a civilization — and RimWorld's faction is allegiance, never map or roster
            // membership. The alternative is actively worse than a no-op: clearing the faction would turn
            // every departing household into a factionless humanlike, which is exactly the population
            // FactionDef.hostileToFactionlessHumanlikes exists to hunt. Walking out of town would make you prey.
            FoundedTown town = Found("departure-faction", bandSize: 20, enter: false);
            var population = town.Settlement.Citizens.ToList();
            foreach (Pawn p in population)
            {
                if (p.needs?.mood != null) p.needs.mood.CurLevel = 0f;
                if (p.needs?.food != null) p.needs.food.CurLevel = 0f;
            }

            var before = new List<Pawn>(population);
            for (int seed = 0; seed < 200 && population.Count == before.Count; seed++)
            {
                Rand.Current = new RandomStream(seed);
                MigrationManager.ProcessDepartures(population);
            }

            List<Pawn> departed = before.Where(p => !population.Contains(p)).ToList();
            Assert.NotEmpty(departed);
            Assert.All(departed, p => Assert.Same(town.Player, p.faction));
        }

        [Fact]
        public void The_roster_sweep_enfactions_a_citizen_seated_by_a_side_door()
        {
            // The backstop, for a citizen that reached the roster without passing AddCitizen: a save written
            // before this lane, or a hand-built pose. Asserted through the public sweep the settlement already
            // runs on its own cadence.
            FoundedTown town = Found("roster-sweep", bandSize: 20, enter: false);
            Pawn stray = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
            Assert.Null(stray.faction);

            town.Settlement.AddCitizen(stray);
            stray.faction = null; // as a pre-fix save would load it
            town.Settlement.SyncCitizenSpawns();

            Assert.Same(town.Player, stray.faction);
        }

        [Fact]
        public void A_citizen_recruited_into_another_faction_is_not_dragged_back_by_the_sweep()
        {
            // AdoptCitizen fills a null and never overwrites. A captured citizen stays on this roster while it
            // is held, and Pawn_GuestTracker writes the host faction onto it when it is recruited; a sweep that
            // re-stamped the settlement's own faction would silently undo that on the next sync.
            FoundedTown town = Found("recruited-citizen", bandSize: 20, enter: false);
            Faction captors = Hostile(town, "RoughOutlanders", "Rough");
            Pawn citizen = town.Settlement.Citizens.First();

            citizen.faction = captors;
            town.Settlement.SyncCitizenSpawns();

            Assert.Same(captors, citizen.faction);
        }

        // ---- readers of Pawn.faction whose answer stopped being "nobody" ----

        [Fact]
        public void A_citizen_can_doctor_another_citizen_of_the_same_town()
        {
            // AI.DoctorUtility.IsCaredForBy opens with "if (carer.faction == null) return false", and
            // WorkGiver_Tend, WorkGiver_RescueDowned and WorkGiver_FeedPatient all route through it. So before
            // this lane no citizen in any generated game could tend, rescue or feed any other — the whole
            // medicine work type was inert in play while every one of its unit tests passed, because each of
            // those hands its own doctor a faction by hand. Same shape as the work register's §5, found the
            // same way: by putting a real game in front of it.
            FoundedTown town = Found("citizen-doctoring");
            List<Pawn> spawned = town.Settlement.Citizens.Where(c => c.Spawned).ToList();
            Pawn doctor = spawned[0];
            Pawn patient = spawned[1];

            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            cut.Worker.Apply(new DamageInfo(cut, 6f, hitPart: patient.RaceProps.body!.GetPartByLabel("left arm")), patient);

            Assert.True(DoctorUtility.IsCaredForBy(doctor, patient));
            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            Assert.True(emergency.HasJobOnThing(doctor, patient), "a citizen cannot treat a bleeding neighbour");
        }

        [Fact]
        public void An_animal_a_citizen_tames_joins_the_citizens_civilization_and_becomes_trainable()
        {
            // TameUtility.TryTame writes "animal.faction = tamer.faction". With a factionless tamer that was a
            // no-op dressed as a success: the animal stayed factionless, so Pawn_TrainingTracker.CanBeTrained
            // refused it forever ("pawn.faction == null"), WorkGiver_Animals still saw it as wild, and
            // HuntUtility still offered it as game. A settlement could tame the same muffalo every day and
            // never own one.
            FoundedTown town = Found("citizen-taming");
            Pawn tamer = town.Settlement.Citizens.First(c => c.Spawned);
            var animal = new Pawn(DefDatabase<ThingDef>.GetNamed("Muffalo"), "Muffalo");
            GenSpawn.Spawn(animal, FreeCellNear(tamer.Position, town.Interior), town.Interior);

            bool tamed = false;
            for (int seed = 0; seed < 500 && !tamed; seed++)
            {
                Rand.Current = new RandomStream(seed);
                animal.mindState.angryAt = null;
                tamed = TameUtility.TryTame(animal, tamer);
            }

            Assert.True(tamed, "no taming attempt ever succeeded, so this test proved nothing");
            Assert.Same(town.Player, animal.faction);
            Assert.False(HuntUtility.IsHuntableAnimal(animal), "a tamed animal is still offered to hunters as game");
            Assert.True(
                animal.training.CanBeTrained(DefDatabase<TrainableDef>.GetNamed("Obedience")),
                "a tamed animal still cannot be trained");
        }

        // ---- determinism ----

        [Fact]
        public void Founding_a_band_costs_the_same_random_draws_with_a_faction_as_without()
        {
            // The faction goes into the PawnGenerationRequest, where PawnWeaponGenerator and
            // PawnApparelGenerator read it as a techLevel ceiling on their candidate lists. Both draw once per
            // pick whatever the list holds, so a shorter list cannot move the stream — but "cannot" is the kind
            // of claim this project keeps finding untrue two batches later, so it is measured. If this ever
            // fails, every fixed-seed test downstream of pawn generation has moved for a reason unrelated to
            // correctness, and the right response is to find out why rather than to re-pin them.
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "draw-parity", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 4, soloStart: true);
            Faction player = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);

            (uint Draws, string Gear) Found(Faction? faction)
            {
                Rand.Current = new RandomStream(4242);
                NameUseChecker.Clear();
                Settlement s = SettlementFounder.Found(
                    world, tile, faction, 24, new RandomStream(7), "Drawtown" + (faction == null ? "A" : "B"));
                string gear = string.Join(
                    "|",
                    s.Citizens.Select(c =>
                        (c.equipment.Primary?.def.defName ?? "-") + ":"
                        + string.Join(",", c.apparel.WornApparel.Select(a => a.def.defName))));
                return (Rand.Current.Iterations, gear);
            }

            (uint Draws, string Gear) without = Found(null);
            (uint Draws, string Gear) with = Found(player);

            Assert.Equal(without.Draws, with.Draws);

            // And for the shipped player civilization the outcome is identical too, not merely equally
            // expensive: PlayerCivilization is Industrial, which is at or above the techLevel of every weapon
            // and apparel def Tribesperson's tags can reach, so the ceiling removes no candidate at all. A
            // civilization further down the ladder is a different matter and deliberately so — an emergent
            // TribalCivilization (Neolithic) now gets bows where its founders used to be able to roll a
            // revolver, which is the rule RimWorld applies and the reason the faction belongs in the request.
            Assert.Equal(without.Gear, with.Gear);
        }

        // ---- the tiering does not pay for it (spec §11.3) ----

        [Fact]
        public void Enfactioning_citizens_never_materialises_a_statistical_citizen()
        {
            // A Statistical citizen is a seat in a count with no Pawn behind it. Nothing in this fix may force
            // one into existence to give it a faction. Measured through the Thing id counter: allocating an id
            // before and after, and finding them consecutive, means no Thing at all was constructed in between.
            var settlement = new Settlement(
                WorldObjectDefOf.Settlement, tile: 1,
                faction: new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Big", "F_Big"),
                name: "Myriad", foundingTick: 0);
            settlement.AddStatisticalPeople(40_000);

            int before = Thing.AllocateThingId();
            settlement.SyncCitizenSpawns();
            int after = Thing.AllocateThingId();

            Assert.Equal(before + 1, after);
            Assert.Equal(40_000, settlement.StatisticalPopulation);
            Assert.Empty(settlement.Citizens);
            Assert.Equal(40_000, settlement.TotalPopulation);
            Assert.Equal(40_000, settlement.PopulationOf(PawnTier.Statistical));
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trip_preserves_every_citizens_faction()
        {
            // A faction is a reference, not a value, and Pawn.faction already Scribes as one
            // (Scribe_References) — the same way Settlement's own WorldObject.faction does. This proves the
            // reference resolves for a citizen on both sides of the split Settlement.ExposeData makes: an
            // unspawned citizen, deep-saved on the roster, and a spawned one saved only as a reference into
            // the interior map's own Thing list.
            FoundedTown town = Found("citizen-faction-scribe", bandSize: 20);

            // Entering the map spawns every Full-tier citizen, so one is added afterwards and left un-synced
            // to cover the other side of the split too.
            Pawn newcomer = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
            town.Settlement.AddCitizen(newcomer);
            Assert.Contains(town.Settlement.Citizens, c => c.Spawned);
            Assert.Contains(town.Settlement.Citizens, c => !c.Spawned);

            string xml = Scribe.SaveToString(town.World, "world");
            global::SimWorld.World.World loaded = Scribe.Load<global::SimWorld.World.World>(
                xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement? reloaded = loaded.worldObjects.OfType<Settlement>().FirstOrDefault(s => s.name == "Holdfast");
            Assert.NotNull(reloaded);
            Assert.NotNull(reloaded!.faction);
            Assert.Equal(town.Settlement.Citizens.Count, reloaded.Citizens.Count);
            Assert.All(reloaded.Citizens, c => Assert.Same(reloaded.faction, c.faction));

            // Same faction object as the world's own, not a second copy reconstructed alongside it — that is
            // the whole point of saving it by reference.
            Assert.Contains(reloaded.faction!, loaded.factions);
        }
    }
}
