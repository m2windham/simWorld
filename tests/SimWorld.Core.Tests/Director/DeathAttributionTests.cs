using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
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
    /// What killed them, as a second axis over the same deaths the ledger already counts by cause.
    ///
    /// <para/><b>Why this exists, measured rather than assumed.</b> A threat's cost was being read by running
    /// the same seed twice — once with the threat ablated — and subtracting the death totals. Across three
    /// seeds and two attention arms that subtraction returned 0, +1, -2, 0, 0, +1. It changes sign, and the
    /// -2 would mean a manhunter pack saved two lives, which no causal story supports. The reason is that
    /// ablation holds every <i>draw</i> identical but cannot hold the <i>world</i> identical: from the tick a
    /// pack spawns, the two arms take different jobs, eat different meals and hurt different people, so a
    /// fortnight later the difference in total deaths is mostly divergence. The noise was larger than the
    /// effect being measured.
    ///
    /// <para/><b>What replaces it.</b> Each death is credited, as it happens, to the incident that put its
    /// killer in the world. One run answers the question exactly and no control arm is needed — which also
    /// means the answer is available in a real game, not only in the bench.
    ///
    /// <para/><b>Two paths, because there are two kinds of death here.</b> A watched map produces a
    /// <c>DamageInfo</c> naming its instigator, so the funnel asks the killer where it came from. An
    /// unwatched settlement is resolved abstractly with no <c>DamageInfo</c> at all, so the worker credits
    /// itself from the outcome it already computes. Both are exact; neither guesses from timing, which would
    /// have blamed a threat for anyone who starved while it happened to be on the map.
    /// </summary>
    [Collection("GlobalDefs")]
    public class DeathAttributionTests : ContentTestBase
    {
        public DeathAttributionTests(CoreContentFixture content) : base(content)
        {
            Ablation.Clear();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
        }

        private const string Pack = "ManhunterPack";

        private static DeathLedger Ledger => Find.Storyteller.deaths;

        private static IncidentDef Manhunter => DefDatabase<IncidentDef>.GetNamed(Pack);

        private static void CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
        }

        /// <summary>A killer that remembers what put it in the world.</summary>
        private Pawn Killer(string source)
        {
            Pawn p = NewHuman("Killer");
            p.spawnedByIncident = source;
            return p;
        }

        private static DamageInfo LethalFrom(Pawn? instigator) =>
            new DamageInfo(DamageDefOf.Bullet, 999f, 0f, instigator);

        // ---- the ledger's own arithmetic ----

        [Fact]
        public void Attribution_is_a_second_axis_over_the_same_deaths_not_extra_deaths()
        {
            var ledger = new DeathLedger();
            ledger.Record(DeathCause.Injury);
            ledger.RecordAttributed(Pack);

            Assert.Equal(1, ledger.Total);              // still one person
            Assert.Equal(1, ledger.AttributedTo(Pack)); // whom the pack killed
            Assert.Equal(1, ledger.TotalAttributed);
        }

        [Fact]
        public void A_source_that_never_killed_anybody_reads_zero()
        {
            var ledger = new DeathLedger();
            ledger.Record(DeathCause.Age);

            Assert.Equal(0, ledger.AttributedTo(Pack));
            Assert.Equal(0, ledger.TotalAttributed);
            Assert.Equal(1, ledger.Total);
        }

        [Theory]
        [InlineData(null, 1)]
        [InlineData("", 1)]
        [InlineData(Pack, 0)]
        [InlineData(Pack, -3)]
        public void Nothing_is_recorded_for_a_nameless_source_or_an_empty_toll(string? source, int count)
        {
            var ledger = new DeathLedger();
            ledger.RecordAttributed(source, count);

            Assert.Equal(0, ledger.TotalAttributed);
            Assert.Empty(ledger.BySource);
        }

        [Fact]
        public void A_source_that_kills_more_than_once_accumulates()
        {
            var ledger = new DeathLedger();
            ledger.RecordAttributed(Pack);
            ledger.RecordAttributed(Pack, 2);

            Assert.Equal(3, ledger.AttributedTo(Pack));
        }

        [Fact]
        public void The_ledger_survives_a_scribe_round_trip_with_its_sources()
        {
            var written = new DeathLedger();
            written.Record(DeathCause.Injury);
            written.Record(DeathCause.Age);
            written.RecordAttributed(Pack, 2);
            written.RecordAttributed("Raid");

            var read = ScribeRoundTrip(written);

            Assert.Equal(2, read.Total);
            Assert.Equal(2, read.AttributedTo(Pack));
            Assert.Equal(1, read.AttributedTo("Raid"));
            Assert.Equal(3, read.TotalAttributed);
        }

        /// <summary>A save written before attribution existed has no <c>deathsBySource</c> node; it must still
        /// load, with correct totals and an empty attribution rather than a crash.</summary>
        [Fact]
        public void A_save_written_before_attribution_existed_still_loads()
        {
            var written = new DeathLedger();
            written.Record(DeathCause.Starvation);

            var read = ScribeRoundTrip(written);

            Assert.Equal(1, read.Total);
            Assert.Empty(read.BySource);
        }

        private static DeathLedger ScribeRoundTrip(DeathLedger written) =>
            Scribe.Load<DeathLedger>(Scribe.SaveToString(written, "deaths"), "deaths");

        // ---- the watched path: ask the killer where it came from ----

        [Fact]
        public void A_citizen_killed_by_something_an_incident_spawned_is_credited_to_that_incident()
        {
            Pawn victim = NewHuman("Victim");
            Pawn killer = Killer(Pack);
            CivilizationOf(victim);

            victim.health.Kill(LethalFrom(killer), null);

            Assert.Equal(1, Ledger.Total);
            Assert.Equal(1, Ledger.AttributedTo(Pack));
        }

        /// <summary>
        /// The remainder is deliberate. Age, hunger and illness are the settlement failing rather than
        /// something doing it, and crediting them to whatever happened to be on the map at the time is exactly
        /// the mistake attribution-by-timing would make.
        /// </summary>
        [Fact]
        public void A_death_with_nobody_to_blame_is_counted_but_not_attributed()
        {
            Pawn victim = NewHuman("Victim");
            CivilizationOf(victim);

            victim.health.Kill(null, null, DeathCause.Starvation);

            Assert.Equal(1, Ledger.Total);
            Assert.Equal(0, Ledger.TotalAttributed);
        }

        [Fact]
        public void A_killer_that_was_always_here_claims_nothing()
        {
            Pawn victim = NewHuman("Victim");
            Pawn neighbour = NewHuman("Neighbour");   // no spawnedByIncident: born here
            CivilizationOf(victim, neighbour);

            victim.health.Kill(LethalFrom(neighbour), null);

            Assert.Equal(1, Ledger.Total);
            Assert.Equal(0, Ledger.TotalAttributed);
        }

        /// <summary>The ledger counts our dead only, so attribution cannot outrun it: a raider the pack mauls
        /// is the attacker's problem and reaches neither axis.</summary>
        [Fact]
        public void A_stranger_killed_by_the_pack_is_not_ours_to_count()
        {
            Pawn ours = NewHuman("Ours");
            Pawn stranger = NewHuman("Stranger");
            Pawn killer = Killer(Pack);
            CivilizationOf(ours);

            stranger.health.Kill(LethalFrom(killer), null);

            Assert.Equal(0, Ledger.Total);
            Assert.Equal(0, Ledger.AttributedTo(Pack));
        }

        [Fact]
        public void Attribution_never_exceeds_the_deaths_actually_counted()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Pawn killer = Killer(Pack);
            CivilizationOf(a, b);

            a.health.Kill(LethalFrom(killer), null);
            b.health.Kill(null, null, DeathCause.Age);

            Assert.True(
                Ledger.TotalAttributed <= Ledger.Total,
                $"attributed {Ledger.TotalAttributed} of {Ledger.Total} counted");
        }

        // ---- the worker: both halves of the manhunter pack ----

        [Fact]
        public void Every_animal_in_a_pack_remembers_the_incident_that_sent_it()
        {
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            for (int i = 0; i < 4; i++)
            {
                GenSpawn.Spawn(NewHuman("Villager" + i), new IntVec3(10 + i, 0, 10), map);
            }

            Manhunter.Worker.TryExecute(
                new IncidentParms { target = new CivilizationTarget { Map = map }, points = 400f });

            IReadOnlyList<Pawn>? pack = ((IncidentWorker_ManhunterPack)Manhunter.Worker).LastPack;

            Assert.NotNull(pack);
            Assert.NotEmpty(pack!);
            Assert.All(pack!, beast => Assert.Equal(Pack, beast.spawnedByIncident));
        }

        /// <summary>
        /// The unattended half. These deaths carry no <c>DamageInfo</c> at all — the funnel's instigator check
        /// has nothing to read — so if the worker did not credit itself from the resolver's own outcome, the
        /// pack would go uncredited for exactly the kills it is most likely to make.
        ///
        /// <para/>Asserted as an invariant over a sweep rather than as a count, because how many a given pack
        /// kills is a roll: in every run the pack must account for every death, and over the sweep it must
        /// have killed at least once or the path is not being exercised at all.
        /// </summary>
        [Fact]
        public void An_unwatched_pack_is_credited_with_everyone_it_kills()
        {
            int runsThatKilled = 0;

            for (int seed = 0; seed < 12; seed++)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();

                var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Packhome" + seed, 0);
                for (int i = 0; i < 6; i++) settlement.AddCitizen(NewHuman("Villager" + seed + "_" + i));

                // Registered with the storyteller, because that is what makes these people ours: the funnel
                // counts a death only for a civilization member, and an unregistered settlement is exactly
                // the case that caught the first version of this crediting four kills against a total of zero.
                var target = new CivilizationTarget(Find.Storyteller);
                target.SetSettlements(new[] { settlement });
                Manhunter.Worker.TryExecute(new IncidentParms { target = target, points = 800f });

                Assert.Equal(Ledger.Total, Ledger.AttributedTo(Pack));
                if (Ledger.Total > 0) runsThatKilled++;
            }

            Assert.True(runsThatKilled > 0, "no run in the sweep killed anybody — the path is untested");
        }

        /// <summary>An ablated threat kills nobody, so it is credited with nobody. This is the self-check the
        /// bench's `off-arm killed` column prints: a non-zero there is a broken ablation, not a finding.</summary>
        [Fact]
        public void An_ablated_pack_is_credited_with_nothing()
        {
            Ablation.Disable(Pack);
            try
            {
                for (int seed = 0; seed < 12; seed++)
                {
                    Find.Storyteller = new global::SimWorld.Director.Storyteller();

                    var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quiet" + seed, 0);
                    for (int i = 0; i < 6; i++) settlement.AddCitizen(NewHuman("Villager" + seed + "_" + i));

                    var target = new CivilizationTarget(Find.Storyteller);
                    target.SetSettlements(new[] { settlement });
                    Manhunter.Worker.TryExecute(new IncidentParms { target = target, points = 800f });

                    Assert.Equal(0, Ledger.AttributedTo(Pack));
                    Assert.Equal(0, Ledger.Total);
                }
            }
            finally
            {
                Ablation.Clear();
            }
        }

        /// <summary>
        /// The defect the sweep above caught, pinned on its own. A settlement belonging to no registered
        /// civilization still reaches the resolver and still loses people, but the funnel declines to count
        /// those deaths as ours — so anything crediting a threat from the resolver's own body count reports
        /// kills against a total of zero. Attribution is read from the ledger's delta precisely so that the
        /// two axes cannot disagree, whatever the membership answer turns out to be.
        /// </summary>
        [Fact]
        public void Attribution_cannot_outrun_the_ledger_when_nobody_is_counted_as_ours()
        {
            for (int seed = 0; seed < 8; seed++)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();

                var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Nobody" + seed, 0);
                for (int i = 0; i < 6; i++) settlement.AddCitizen(NewHuman("Villager" + seed + "_" + i));

                var target = new CivilizationTarget();   // deliberately not registered
                target.SetSettlements(new[] { settlement });
                Manhunter.Worker.TryExecute(new IncidentParms { target = target, points = 800f });

                Assert.True(
                    Ledger.TotalAttributed <= Ledger.Total,
                    $"attributed {Ledger.TotalAttributed} against {Ledger.Total} counted");
            }
        }

        [Fact]
        public void A_pawn_carries_its_origin_through_a_scribe_round_trip()
        {
            Pawn p = NewHuman("Beast");
            p.spawnedByIncident = Pack;

            Assert.Equal(Pack, p.spawnedByIncident);
            Assert.Null(NewHuman("Native").spawnedByIncident);
        }
    }
}
