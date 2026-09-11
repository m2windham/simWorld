using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
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

        // ---- the condition the defect rests on ----

        [Fact]
        public void Every_citizen_of_a_founded_settlement_belongs_to_no_faction()
        {
            // Not an accusation, a fact to build on: the settlement is the player's and its people are
            // nobody's. If this ever stops being true the two tests below stop testing what they say, and
            // this is where that would show.
            FoundedTown town = Found("factionless-citizens");

            Assert.NotNull(town.Settlement.faction);
            Assert.NotEmpty(town.Settlement.Citizens);
            Assert.All(town.Settlement.Citizens, c => Assert.Null(c.faction));
            Assert.All(town.Settlement.Citizens, c => Assert.True(c.RaceProps.Humanlike));
        }

        // ---- the fix ----

        [Fact]
        public void A_faction_that_attacks_factionless_people_fights_a_settlements_citizens()
        {
            FoundedTown town = Found("rough-raid");
            Faction rough = Raiders(town, "RoughOutlanders", "Rough");
            Assert.True(rough.def.hostileToFactionlessHumanlikes, "the shipped content this test rests on changed");

            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Pawn raider = RaiderBeside(rough, citizen, town.Interior);

            Assert.True(AttackTargetsUtility.HostileTo(raider, citizen), "the raider does not see the citizen as an enemy");
            Assert.True(AttackTargetsUtility.HostileTo(citizen, raider), "the citizen does not see the raider as an enemy");

            raider.jobs.TryFindAndStartJob();
            citizen.jobs.TryFindAndStartJob();

            Assert.True(IsAttackJob(raider.jobs.curJob), "the raider walked into a settlement and went about its day");
            Assert.True(IsAttackJob(citizen.jobs.curJob), "a citizen with a raider at arm's length kept working");
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
            Pawn raider = RaiderBeside(rough, citizen, town.Interior);

            const float Radius = 40f;
            Assert.Same(
                AttackTargetFinder.BestAttackTargetUncached(raider, Radius),
                AttackTargetFinder.BestAttackTarget(raider, Radius));
            Assert.Same(
                AttackTargetFinder.BestAttackTargetUncached(citizen, Radius),
                AttackTargetFinder.BestAttackTarget(citizen, Radius));
            Assert.NotNull(AttackTargetFinder.BestAttackTarget(raider, Radius));
        }

        [Fact]
        public void A_faction_that_does_not_attack_factionless_people_still_does_not()
        {
            // The residue, asserted rather than left to be discovered: the flag is content's statement about
            // one faction, not a blanket rule. TribalCivilization and OutlanderCivilization declare it false,
            // so their raids on a settlement of factionless citizens still find nobody — the real fix for
            // those is a citizen faction, which belongs to the World module and not here. This test exists so
            // that gap is visible and pinned instead of silent.
            FoundedTown town = Found("tribal-raid");
            Faction tribal = Raiders(town, "TribalCivilization", "Tribal");
            Assert.False(tribal.def.hostileToFactionlessHumanlikes);

            Pawn citizen = town.Settlement.Citizens.First(c => c.Spawned);
            Pawn raider = RaiderBeside(tribal, citizen, town.Interior);

            Assert.False(AttackTargetsUtility.HostileTo(raider, citizen));
            Assert.False(AttackTargetsUtility.HostileTo(citizen, raider));
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
