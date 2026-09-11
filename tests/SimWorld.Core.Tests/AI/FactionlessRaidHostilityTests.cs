using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The gap between "raids generate real squads and pawns fight" and "anyone in a real game ever throws a
    /// punch when a raid lands", exactly as <see cref="HuntingIsReachableInShippedContentTests"/> is that gap
    /// for hunting.
    ///
    /// <para/><b>What was wrong.</b> Hostility rested on both sides having a <see cref="Pawn.faction"/>, and
    /// in this port only raiders have one: <c>World.SettlementFounder</c> generates every citizen through a
    /// <c>PawnGenerationRequest</c> that names no faction, and <c>Factions.PawnGroupMaker</c> is the only
    /// caller in <c>src/</c> that passes one. A settlement has a faction; not one of its citizens does.
    /// <see cref="FactionDef.hostileToFactionlessHumanlikes"/> — set by exactly one shipped faction, the
    /// permanent-enemy <c>RoughOutlanders</c> — is the flag whose whole job is that pairing, and no line of
    /// <c>src/</c> read it.
    ///
    /// <para/><b>Measured before the fix</b>, on a settlement founded the ordinary way from shipped content
    /// with a real <c>RaidEnemy</c> firing onto its interior: 30 citizens, 14 raiders standing on the same
    /// map, <b>0 of 14 raiders saw a citizen as hostile, 0 of 30 citizens saw a raider as hostile</b>, and no
    /// pawn on either side took an attack job. Every existing combat test passed throughout, because each one
    /// hands its own defenders a faction.
    ///
    /// <para/><b>Since then, the real fix landed.</b> A settlement's citizens carry their settlement's faction
    /// (<c>World.CitizenFactionTests</c>, which re-runs the measurement above and records the after numbers),
    /// so a raid on a town is now a fight by faction relation and this flag is no longer what carries it.
    /// The flag is still wired, still read, and still reachable — the factionless-humanlike population it is
    /// aimed at is now released prisoners (<c>Factions.Pawn_GuestTracker.Release</c> clears the faction) and
    /// pawns generated outside any settlement, rather than every civilian in the game — so these tests were
    /// re-aimed at one of those rather than deleted.
    ///
    /// <para/>These tests arm and enfaction nobody by hand: the citizens are whoever
    /// <c>SettlementFounder</c> made, and the raiders are whoever <c>PawnGroupMakerUtility</c> bought.
    /// </summary>
    public class FactionlessRaidHostilityTests : ContentTestBase
    {
        public FactionlessRaidHostilityTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static FactionDef Def(string defName) => DefDatabase<FactionDef>.GetNamed(defName);

        private sealed class FoundedTown
        {
            public global::SimWorld.World.World World = null!;
            public Settlement Settlement = null!;
            public global::SimWorld.Map.Map Interior = null!;
            public Faction Player = null!;
        }

        private static FoundedTown Found(string seed)
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 4, soloStart: true);
            Faction player = world.factions.First();
            Find.FactionManager.Add(player);
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, player, 30, new RandomStream(20260911), "Holdfast");
            return new FoundedTown
            {
                World = world,
                Settlement = settlement,
                Interior = settlement.EnterMap(world),
                Player = player,
            };
        }

        /// <summary>A hostile faction of <paramref name="defName"/>, registered and at war with the town.</summary>
        private static Faction Raiders(FoundedTown town, string defName, string name)
        {
            var faction = new Faction(Def(defName), name, "F_" + name);
            Find.FactionManager.Add(faction);
            town.Player.SetRelationDirect(faction, FactionRelationKind.Hostile, -100);
            return faction;
        }

        /// <summary>One real raider of <paramref name="faction"/>, put down beside <paramref name="neighbour"/>.
        /// Generated through the same <c>PawnGroupMakerUtility</c> path a raid uses, so it is armed and
        /// kinded exactly as a raider is — nothing here equips anybody.</summary>
        private static Pawn RaiderBeside(Faction faction, Pawn neighbour, global::SimWorld.Map.Map map)
        {
            List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(
                new PawnGroupMakerParms { faction = faction, groupKind = PawnGroupKindDefOf.Combat, points = 100f });
            Pawn raider = squad[0];
            IntVec3 cell = FreeCellNear(neighbour.Position, map);
            GenSpawn.Spawn(raider, cell, map);
            return raider;
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

        /// <summary>A humanlike belonging to no faction at all, standing on the town's interior. Since
        /// <c>World.CitizenFactionTests</c> landed, a settlement's citizens are no longer that population —
        /// released prisoners (<c>Factions.Pawn_GuestTracker.Release</c> clears the faction outright) and
        /// pawns generated outside any settlement are, so the rule under test is exercised against one of
        /// those instead of against a civilian who now has a civilization.</summary>
        private static Pawn FactionlessHumanlikeBeside(Pawn neighbour, global::SimWorld.Map.Map map)
        {
            // mustBeCapableOfViolence so the test does not ride on a backstory roll: a pawn whose adulthood
            // disables Violent work never takes an attack job whatever it can see, which is RimWorld's rule
            // and not the thing under test here.
            Pawn drifter = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                DefDatabase<PawnKindDef>.GetNamed("Drifter"), fixedBiologicalAge: 30f, mustBeCapableOfViolence: true));
            Assert.Null(drifter.faction);
            Assert.True(drifter.RaceProps.Humanlike);
            GenSpawn.Spawn(drifter, FreeCellNear(neighbour.Position, map), map);
            return drifter;
        }

        // ---- the condition the defect rested on, and what replaced it ----

        [Fact]
        public void Every_citizen_of_a_founded_settlement_carries_its_settlements_faction()
        {
            // This used to assert the opposite, and the assertion was the defect: the settlement was the
            // player's and its people were nobody's, which is what made a raid on a watched town a pantomime.
            // The real fix landed in the World module — a citizen belongs to its civilization — and
            // World.CitizenFactionTests is its measurement. What survives here is the flag's own rule, now
            // aimed at the population that is still genuinely factionless.
            FoundedTown town = Found("factionless-citizens");

            Assert.NotNull(town.Settlement.faction);
            Assert.NotEmpty(town.Settlement.Citizens);
            Assert.All(town.Settlement.Citizens, c => Assert.Same(town.Settlement.faction, c.faction));
            Assert.All(town.Settlement.Citizens, c => Assert.True(c.RaceProps.Humanlike));
        }

        // ---- the fix ----

        [Fact]
        public void A_faction_that_attacks_factionless_people_fights_a_factionless_humanlike()
        {
            FoundedTown town = Found("rough-raid");
            Faction rough = Raiders(town, "RoughOutlanders", "Rough");
            Assert.True(rough.def.hostileToFactionlessHumanlikes, "the shipped content this test rests on changed");

            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Pawn drifter = FactionlessHumanlikeBeside(citizen, town.Interior);
            Pawn raider = RaiderBeside(rough, drifter, town.Interior);

            Assert.True(AttackTargetsUtility.HostileTo(raider, drifter), "the raider does not see the factionless humanlike as an enemy");
            Assert.True(AttackTargetsUtility.HostileTo(drifter, raider), "the factionless humanlike does not see the raider as an enemy");

            raider.jobs.TryFindAndStartJob();
            drifter.jobs.TryFindAndStartJob();

            Assert.True(IsAttackJob(raider.jobs.curJob), "the raider walked up to a stranger and went about its day");
            Assert.True(IsAttackJob(drifter.jobs.curJob), "a factionless humanlike with a raider at arm's length kept working");
        }

        [Fact]
        public void The_index_offers_the_pairing_the_full_scan_finds()
        {
            // AttackTargetsCache is allowed to be a superset and never a subset. Factionless pawns are in no
            // faction bucket, so this rule is the one place the index could have silently gone blind: the
            // cached scan and the walk-the-whole-map scan must agree for both sides of this fight.
            FoundedTown town = Found("rough-index");
            Faction rough = Raiders(town, "RoughOutlanders", "Rough");

            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Pawn drifter = FactionlessHumanlikeBeside(citizen, town.Interior);
            Pawn raider = RaiderBeside(rough, drifter, town.Interior);

            const float Radius = 40f;
            Assert.Same(
                AttackTargetFinder.BestAttackTargetUncached(raider, Radius),
                AttackTargetFinder.BestAttackTarget(raider, Radius));
            Assert.Same(
                AttackTargetFinder.BestAttackTargetUncached(drifter, Radius),
                AttackTargetFinder.BestAttackTarget(drifter, Radius));
            Assert.Same(
                AttackTargetFinder.BestAttackTargetUncached(citizen, Radius),
                AttackTargetFinder.BestAttackTarget(citizen, Radius));
            Assert.NotNull(AttackTargetFinder.BestAttackTarget(raider, Radius));

            // The factionless half of the index still has an occupant, and it is no longer "most of the map".
            Assert.Equal(new[] { drifter }, town.Interior.mapPawns.AttackTargets.FactionlessHumanlikes);
        }

        [Fact]
        public void A_faction_that_does_not_attack_factionless_people_still_does_not_but_fights_a_citizen()
        {
            // The flag is content's statement about one faction, not a blanket rule: TribalCivilization and
            // OutlanderCivilization declare it false and still walk past a stranger who belongs to nobody.
            //
            // The residue this test used to pin is gone. It read "their raids on a settlement of factionless
            // citizens still find nobody — the real fix for those is a citizen faction, which belongs to the
            // World module and not here". That fix landed; the second half below is it, asserted from this
            // side so the two halves cannot drift apart. A tribal raid on a town is a fight now, and it is a
            // fight by faction relation rather than by this flag.
            FoundedTown town = Found("tribal-raid");
            Faction tribal = Raiders(town, "TribalCivilization", "Tribal");
            Assert.False(tribal.def.hostileToFactionlessHumanlikes);

            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Pawn drifter = FactionlessHumanlikeBeside(citizen, town.Interior);
            Pawn raider = RaiderBeside(tribal, drifter, town.Interior);

            Assert.False(AttackTargetsUtility.HostileTo(raider, drifter));
            Assert.False(AttackTargetsUtility.HostileTo(drifter, raider));

            Assert.True(AttackTargetsUtility.HostileTo(raider, citizen));
            Assert.True(AttackTargetsUtility.HostileTo(citizen, raider));
        }

        [Fact]
        public void A_wild_animal_is_still_nobody_s_enemy()
        {
            // The rule is about humanlikes, and deliberately: every wild animal on a map is factionless too,
            // and a raid that opened fire on the local muffalo herd on arrival would be this fix overshooting.
            FoundedTown town = Found("rough-animals");
            Faction rough = Raiders(town, "RoughOutlanders", "Rough");

            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Pawn raider = RaiderBeside(rough, citizen, town.Interior);

            var muffalo = new Pawn(DefDatabase<ThingDef>.GetNamed("Muffalo"), "Muffalo");
            GenSpawn.Spawn(muffalo, FreeCellNear(raider.Position, town.Interior), town.Interior);

            Assert.Null(muffalo.faction);
            Assert.False(AttackTargetsUtility.HostileTo(raider, muffalo));
            Assert.False(AttackTargetsUtility.HostileTo(muffalo, raider));
        }

        [Fact]
        public void A_raid_still_arrives_at_the_map_edge_and_that_is_a_different_gap()
        {
            // Recorded, not fixed. With hostility working, a real RaidEnemy firing onto a real settlement
            // interior still changes nothing on the tick it lands: the squad arrives on a map edge and a
            // 200x200 interior puts it far outside CombatAITuning.TargetAcquireRadius of anybody. Closing
            // that distance needs squad approach AI, which this port does not have (IncidentWorker_RaidEnemy
            // says so: "Walking the squad from there to the colony is AI's, not this module's"). Asserted as
            // the ordering — arrival distance beyond acquire radius — rather than as the measured 120 cells.
            FoundedTown town = Found("arrival-distance");
            Faction rough = Raiders(town, "RoughOutlanders", "Rough");

            var target = new CivilizationTarget { Map = town.Interior };
            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestArrivalRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = target, points = 600f, faction = rough }));

            IReadOnlyList<Pawn> raiders = worker.LastRaidPawns!;
            Assert.NotEmpty(raiders);
            Assert.All(raiders, r => Assert.True(r.Spawned));

            // They see the citizens as enemies now — that is this lane's fix — and are still too far away to
            // do anything about it.
            Assert.All(raiders, r => Assert.Contains(town.Settlement.Citizens, c => AttackTargetsUtility.HostileTo(r, c)));

            float nearest = raiders
                .SelectMany(r => town.Settlement.Citizens.Where(c => c.Spawned).Select(c => (r.Position - c.Position).LengthHorizontal))
                .Min();
            Assert.True(nearest > CombatAITuning.TargetAcquireRadius,
                "a raid now arrives within acquire radius of the town; if that is deliberate this test should "
                + "become the opposite assertion (nearest " + nearest + ")");
        }
    }
}
