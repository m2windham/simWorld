using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The first real pressure in this port, and the first thing measured by ablating it.
    ///
    /// <para/><b>What it replaces.</b> <c>IncidentWorker_ThreatEvent</c>, whose whole body was
    /// <c>=&gt; parms.points &gt; 0f</c>. <c>ManhunterPack</c> was shipped content pointing at it: the
    /// incident fired, told the storyteller it had succeeded, spent its refire timer and counted against the
    /// threat budget, and nothing happened to anybody. Every system downstream believed a threat had
    /// occurred. That is worse than having no threat at all, and it is the failure these tests exist to stop
    /// recurring — several of them assert that something actually happened rather than that a method returned
    /// true.
    ///
    /// <para/><b>Where it lands is the player's decision.</b> A watched settlement fights the pack on its own
    /// map; an unwatched one is told about it afterwards. A god watches one settlement and threats do not
    /// queue up at that one, so "where am I looking" costs something — which is the whole reason attention
    /// can be a currency rather than a camera.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ManhunterPackTests : ContentTestBase
    {
        public ManhunterPackTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
        }

        private static IncidentDef Manhunter => DefDatabase<IncidentDef>.GetNamed("ManhunterPack");

        private static CoreMap NewMap(int size = 30) => new CoreMap(size, size, TerrainDefOf.Soil);

        /// <summary>A settlement with people standing on a map — the state that makes a manhunter pack mean
        /// anything at all.</summary>
        private Settlement PeopledSettlement(CoreMap? map, int citizens = 6)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Packhome", 0);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = NewHuman("Villager" + i);
                settlement.AddCitizen(p);
                if (map != null) GenSpawn.Spawn(p, new IntVec3(10 + (i % 4), 0, 10 + (i / 4)), map);
            }
            return settlement;
        }

        /// <summary>A watched settlement, posed the way <c>RaidApproachTests</c> poses one: the map hook
        /// directly, so a test need not generate a 200x200 interior to assert what lands on it.</summary>
        private static IncidentParms OntoMap(CoreMap map, float points = 400f) =>
            new IncidentParms { target = new CivilizationTarget { Map = map }, points = points };

        /// <summary>A real settlement with no interior — the unwatched case, which is the whole point of the
        /// other branch.</summary>
        private static IncidentParms OntoSettlement(Settlement settlement, float points = 400f)
        {
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            return new IncidentParms { target = target, points = points };
        }

        private static int AnimalsOn(CoreMap map) =>
            map.mapPawns.AllPawnsSpawned.Count(p => !p.RaceProps.Humanlike);

        // ---- the thing it exists to stop being ----

        [Fact]
        public void A_watched_settlement_gets_animals_it_can_actually_see()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Assert.Equal(0, AnimalsOn(map));

            bool fired = Manhunter.Worker.TryExecute(OntoMap(map));

            Assert.True(fired);

            // The assertion the old placeholder would have passed by returning true: something is on the map.
            Assert.True(AnimalsOn(map) > 0, "a manhunter pack that spawns nothing is the bug this replaces");
        }

        [Fact]
        public void The_pack_is_hostile_to_the_people_it_turned_on()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Manhunter.Worker.TryExecute(OntoMap(map));

            Pawn animal = map.mapPawns.AllPawnsSpawned.First(p => !p.RaceProps.Humanlike);
            Pawn villager = map.mapPawns.AllPawnsSpawned.First(p => p.RaceProps.Humanlike);

            // Both directions. A pack the settlement cannot fight back against is not a fight.
            Assert.True(AttackTargetsUtility.HostileTo(animal, villager));
            Assert.True(AttackTargetsUtility.HostileTo(villager, animal));
        }

        /// <summary>
        /// Hostility has to reach the scan index, not just the predicate. <c>AttackTargetsCache</c> indexes
        /// hostility sources so a searcher need not ask about every pawn alive — and a factionless animal sits
        /// in no faction bucket, so if manhunting did not file it in the out-of-faction list the cached scan
        /// would find nothing where the uncached walk found a fight. The pack would be hostile and invisible.
        /// </summary>
        [Fact]
        public void The_pack_is_findable_by_a_citizen_looking_for_a_fight()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Manhunter.Worker.TryExecute(OntoMap(map));

            Pawn villager = map.mapPawns.AllPawnsSpawned.First(p => p.RaceProps.Humanlike);
            Assert.Contains(
                map.mapPawns.AttackTargets.GrudgeHolders,
                p => !p.RaceProps.Humanlike);

            Pawn? found = AttackTargetFinder.BestAttackTarget(villager, 9999f);
            Assert.NotNull(found);
            Assert.False(found!.RaceProps.Humanlike);
        }

        /// <summary>
        /// The pack turns among the settlement rather than arriving at a map edge, because
        /// <c>ThinkTrees_Animal.xml</c> has no <c>ThinkNode_Duty</c>: an animal handed
        /// <c>AssaultSettlement</c> ignores it, and an edge spawn would leave the pack outside every acquire
        /// radius with nothing to walk it in. Asserted as proximity rather than as an exact cell, because the
        /// claim is "close enough to be a threat", not any particular geometry.
        /// </summary>
        [Fact]
        public void The_pack_turns_within_reach_of_somebody()
        {
            CoreMap map = NewMap(40);
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Manhunter.Worker.TryExecute(OntoMap(map));

            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(p => !p.RaceProps.Humanlike).ToList();
            List<Pawn> people = map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Humanlike).ToList();

            foreach (Pawn animal in animals)
            {
                int nearest = people.Min(v => (v.Position - animal.Position).LengthManhattan);
                Assert.True(nearest <= 20, $"an animal turned {nearest} cells from the nearest person");
            }
        }

        // ---- the unwatched half ----

        [Fact]
        public void An_unwatched_settlement_is_still_attacked_and_still_told()
        {
            Settlement settlement = PeopledSettlement(null, citizens: 6);
            Find.God.Attention.ClearFocus();

            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            bool fired = Manhunter.Worker.TryExecute(OntoSettlement(settlement));

            Assert.True(fired);
            Assert.True(
                Find.LetterStack.LettersListForReading.Count > lettersBefore,
                "a mauling nobody is told about is indistinguishable from nothing happening");
        }

        [Fact]
        public void Both_paths_send_the_player_a_letter()
        {
            CoreMap map = NewMap();
            Settlement watched = PeopledSettlement(map);
            Find.God.Attention.Focus(watched);

            Manhunter.Worker.TryExecute(OntoMap(map));

            Assert.Contains(
                Find.LetterStack.LettersListForReading,
                l => l.label.Contains("Manhunter", System.StringComparison.Ordinal));
        }

        // ---- the ablation discipline ----

        /// <summary>
        /// The incident must not draw from the ambient stream. If it did, switching it on would shift every
        /// later draw in the game — weather, births, raids, diseases — and a measured difference between a run
        /// with the pack and a run without it would be mostly that reshuffle. See <see cref="NamedRand"/>.
        /// </summary>
        [Fact]
        public void Firing_the_incident_does_not_disturb_the_ambient_random_stream()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            uint before = Rand.Current.Iterations;
            Manhunter.Worker.TryExecute(OntoMap(map));

            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void The_same_tick_and_seed_compose_the_same_pack()
        {
            int PackSizeOnce()
            {
                CoreMap map = NewMap();
                Settlement settlement = PeopledSettlement(map);
                Find.God.Attention.Focus(settlement);
                Manhunter.Worker.TryExecute(OntoMap(map));
                return AnimalsOn(map);
            }

            Find.TickManager.DebugSetTicksGame(5000);
            int a = PackSizeOnce();
            Find.TickManager.DebugSetTicksGame(5000);
            int b = PackSizeOnce();

            Assert.Equal(a, b);
        }

        // ---- ablation ----

        /// <summary>
        /// Switched off, the incident still fires and still does nothing — which is the whole design of
        /// <see cref="Ablation"/>. An ablated incident that declined to fire would change what the
        /// storyteller's weighted roll lands on, shifting every later draw, and the measured difference would
        /// be that reshuffle rather than the thing under test.
        ///
        /// <para/>Note what the disabled arm reproduces exactly: the placeholder this worker replaced. That
        /// is not a coincidence worth smiling at and moving past — an accidental permanent ablation looks
        /// identical to a working feature unless somebody measures.
        /// </summary>
        [Fact]
        public void Ablated_it_still_fires_and_still_does_nothing()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Ablation.Disable("ManhunterPack");
            try
            {
                bool fired = Manhunter.Worker.TryExecute(OntoMap(map));

                Assert.True(fired, "an ablated incident must still report success, or selection itself changes");
                Assert.Equal(0, AnimalsOn(map));
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void Ablated_it_leaves_the_ambient_stream_exactly_where_the_live_one_does()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Ablation.Disable("ManhunterPack");
            try
            {
                uint before = Rand.Current.Iterations;
                Manhunter.Worker.TryExecute(OntoMap(map));
                Assert.Equal(before, Rand.Current.Iterations);
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void Switching_it_back_on_restores_the_threat()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(map);
            Find.God.Attention.Focus(settlement);

            Ablation.Disable("ManhunterPack");
            Ablation.Clear();

            Manhunter.Worker.TryExecute(OntoMap(map));

            // The harness leaving an ablation set would quietly report a disabled world as the baseline,
            // which is the one failure that makes every number downstream wrong and none of them look it.
            Assert.True(AnimalsOn(map) > 0);
        }

        [Fact]
        public void A_bigger_threat_budget_buys_a_bigger_pack()
        {
            int PackAt(float points)
            {
                CoreMap map = NewMap();
                Settlement settlement = PeopledSettlement(map);
                Find.God.Attention.Focus(settlement);
                Manhunter.Worker.TryExecute(OntoMap(map, points));
                return AnimalsOn(map);
            }

            // A band, not a literal: the claim is that points buy severity at all, which is what
            // pointsScaleable promises and what the placeholder never delivered.
            Assert.True(PackAt(1000f) > PackAt(60f));
        }
    }
}
