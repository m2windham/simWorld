using System.Collections.Generic;
using System.Linq;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The policy behind <see cref="Pawn_TierTracker.Notify_ChronicleNamed"/> for the living
    /// (<see cref="ChronicleFame"/>): outliving everyone the civilization has ever known, and nothing else.
    /// <para/>
    /// The load-bearing test here is the negative one,
    /// <see cref="A_century_of_ordinary_life_names_almost_nobody_and_never_more_than_one_at_a_time"/>. The
    /// flag is one-directional — a chronicle entry is never unwritten — so anyone this names holds a
    /// <see cref="PawnTier.Full"/> seat for the rest of their life, second of four in
    /// <see cref="AttentionBudget"/>'s ordering. A policy that fired often would do to that fixed 500-seat
    /// budget exactly what <c>MigrationManager</c>'s founder flag did before it was removed: fill it
    /// permanently with the unremarkable.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ChronicleFameTests : ContentTestBase
    {
        public ChronicleFameTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static CoreWorld NewWorld(string seed) =>
            WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 3, soloStart: true);

        /// <summary>A real founded band on the first free land tile — the same shape
        /// <c>AttentionBudgetTests.Found</c> uses, and the only one that puts live <c>Pawn</c>s on a
        /// roster.</summary>
        private static Settlement Found(CoreWorld world, int seed, string name, int bandSize = 24)
        {
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            for (int tile = 0; tile < world.grid.TilesCount; tile++)
            {
                if (taken.Contains(tile) || world.grid.Tiles[tile].WaterCovered) continue;
                return SettlementFounder.Found(world, tile, world.factions[0], bandSize, new RandomStream(seed), name);
            }

            Assert.Fail("no free land tile for a settlement");
            return null!;
        }

        private static List<Pawn> Aged(params float[] years)
        {
            var people = new List<Pawn>(years.Length);
            for (int i = 0; i < years.Length; i++)
            {
                Pawn p = NewHuman("Citizen" + i);
                p.ageTracker.DebugSetAge(years[i]);
                people.Add(p);
            }
            return people;
        }

        private static int NamedCount(IEnumerable<Pawn> people) => people.Count(p => p.tier.ChronicleNamed);

        private static int LongevityLines() =>
            Find.Storyteller.Chronicle.Count(e => e.incidentDefName.StartsWith("Longevity:"));

        // ---- the rule fires ----

        [Fact]
        public void The_oldest_citizen_is_named_when_nobody_has_ever_lived_longer()
        {
            List<Pawn> people = Aged(20f, 45f, 31f);
            Pawn eldest = people[1];

            Assert.Same(eldest, ChronicleFame.ConsiderDoyen(people));
            Assert.True(eldest.tier.ChronicleNamed);
            Assert.Equal(PawnTier.Full, eldest.tier.Tier);

            // Named by the chronicle means named *in* the chronicle: the line is written, and the curator
            // keeps it as a landmark rather than letting it roll off with the news.
            Assert.Equal(1, LongevityLines());
            Assert.Contains(Find.Storyteller.Moments, e => e.incidentDefName.StartsWith("Longevity:"));

            // And nobody else.
            Assert.Equal(1, NamedCount(people));
        }

        [Fact]
        public void Nobody_is_named_for_an_ordinary_life_below_the_record()
        {
            List<Pawn> people = Aged(20f, 45f, 31f);
            ChronicleFame.ConsiderDoyen(people); // the 45-year-old takes it

            // A whole cohort of perfectly respectable lives, none of them a record.
            List<Pawn> ordinary = Aged(30f, 38f, 44f, 12f, 5f);
            Assert.Null(ChronicleFame.ConsiderDoyen(ordinary));
            Assert.Equal(0, NamedCount(ordinary));
            Assert.Equal(1, LongevityLines());
        }

        [Fact]
        public void The_holder_is_not_announced_again_every_year_they_go_on_living()
        {
            List<Pawn> people = Aged(20f, 45f);
            Pawn eldest = people[1];
            Assert.Same(eldest, ChronicleFame.ConsiderDoyen(people));

            // Twenty more years of being the oldest person alive. Each one is, literally, a new longest life
            // — and the civilization remarks on it exactly once.
            for (int year = 0; year < 20; year++)
            {
                eldest.ageTracker.DebugSetAge(45f + year + 1);
                Assert.Null(ChronicleFame.ConsiderDoyen(people));
            }

            Assert.Equal(1, LongevityLines());
            Assert.Equal(1, Find.Storyteller.Moments.Count(e => e.incidentDefName.StartsWith("Longevity:")));
        }

        [Fact]
        public void A_successor_is_only_possible_once_the_holder_is_gone_and_then_must_still_beat_them()
        {
            List<Pawn> people = Aged(50f);
            Pawn first = people[0];
            Assert.Same(first, ChronicleFame.ConsiderDoyen(people));

            // While the holder lives, nobody younger can take it — which is the whole of why at most one
            // living citizen is ever named by this rule.
            Pawn rival = NewHuman("Rival");
            rival.ageTracker.DebugSetAge(49f);
            people.Add(rival);
            Assert.Null(ChronicleFame.ConsiderDoyen(people));

            first.health.Kill(null, null);
            Assert.Null(ChronicleFame.ConsiderDoyen(people)); // 49 still does not beat 50
            Assert.False(rival.tier.ChronicleNamed);

            rival.ageTracker.DebugSetAge(51f);
            Assert.Same(rival, ChronicleFame.ConsiderDoyen(people));
            Assert.Equal(2, LongevityLines());
        }

        [Fact]
        public void A_death_record_and_a_living_record_move_the_same_ratchet()
        {
            // Somebody dies at 80, which is a record; a living 70-year-old is therefore not remarkable, and
            // only passing 80 makes them so. One "longest life the civilization has known", two ways to reach
            // it — see MomentCurator's own doc.
            Pawn deceased = NewHuman("Deceased");
            deceased.ageTracker.DebugSetAge(80f);
            Find.Storyteller.RecordDeath(deceased, DeathCause.Age);

            List<Pawn> people = Aged(70f);
            Assert.Null(ChronicleFame.ConsiderDoyen(people));
            Assert.Equal(0, LongevityLines());

            people[0].ageTracker.DebugSetAge(81f);
            Assert.Same(people[0], ChronicleFame.ConsiderDoyen(people));
        }

        [Fact]
        public void The_dead_are_never_named_by_this_rule()
        {
            List<Pawn> people = Aged(30f, 90f);
            people[1].health.Kill(null, null);

            Assert.Same(people[0], ChronicleFame.ConsiderDoyen(people));
            Assert.False(people[1].tier.ChronicleNamed);
        }

        // ---- the measurement the policy has to survive ----

        /// <summary>
        /// A century of real demography on a founded settlement — the same harness
        /// <c>AttentionBudgetTests.A_century_of_demography_holds_the_Full_tier_flat_instead_of_doubling_it</c>
        /// uses, because the thing being protected here is that test's budget. The roster grows into the
        /// thousands; what must not grow with it is the set of citizens holding a permanent Full-tier seat
        /// for a reason that can never be revoked.
        /// <para/>
        /// Measured, on the two seeds this file runs a century on:
        /// <list type="bullet">
        /// <item><c>fame-century</c> (seed 911): roster 1,463 at year 100, <b>4</b> citizens ever named, peak
        /// living named <b>1</b>, 4 of the civilization's 11 moments.</item>
        /// <item><c>fame-budget</c> (seed 912): roster 1,404, <b>3</b> ever named, <b>none alive</b> at year
        /// 100 — the record stood at 93.3 years and nobody had reached it again — 3 of 9 moments.</item>
        /// </list>
        /// Three or four people per century out of some fifteen hundred, against a 500-seat budget. The
        /// assertions below are written as bands rather than those literals, since the exact count is a
        /// property of the seed's mortality and not of the policy.
        /// </summary>
        [Fact]
        public void A_century_of_ordinary_life_names_almost_nobody_and_never_more_than_one_at_a_time()
        {
            CoreWorld world = NewWorld("fame-century");
            Find.World = world;
            Settlement home = Found(world, 911, "Century");

            int peakNamedAlive = 0;
            var everNamed = new HashSet<int>();

            int tick = 0;
            for (int year = 1; year <= 100; year++)
            {
                foreach (Pawn p in home.Citizens.ToList())
                {
                    if (!p.Dead) p.ageTracker.AgeTickMothballed(DemographyTuning.DemographyIntervalTicks);
                }

                tick += DemographyTuning.DemographyIntervalTicks;
                Find.TickManager.DebugSetTicksGame(tick);
                home.GrowthTick();
                home.SyncCitizenSpawns();

                List<Pawn> namedAlive = home.Citizens.Where(p => !p.Dead && p.tier.ChronicleNamed).ToList();
                foreach (Pawn p in namedAlive) everNamed.Add(p.thingIDNumber);
                peakNamedAlive = System.Math.Max(peakNamedAlive, namedAlive.Count);
            }

            // The premise: a century of demography really does produce the population this has to survive.
            // If this fails, demography changed and the rest of the test proves nothing.
            Assert.True(home.Citizens.Count > 400,
                $"expected a century to grow the roster substantially; got {home.Citizens.Count}");

            // The conclusion, and the whole point of the rule's shape: at no sweep in the century does more
            // than one living citizen hold this distinction, whatever the roster did underneath.
            Assert.True(peakNamedAlive <= 1,
                $"more than one living citizen was chronicle-named at once (peak {peakNamedAlive})");

            // Over the whole century a handful of people ever hold it — a record broken a few times, not an
            // event stream. Asserted as a band rather than a literal: the exact count is a property of the
            // seed's mortality, and what matters is the order of magnitude against a 500-seat budget.
            Assert.InRange(everNamed.Count, 1, 10);

            // And the flag really is scarce relative to the roster it is drawn from, which is the claim the
            // budget depends on.
            Assert.True(everNamed.Count * 50 < home.Citizens.Count,
                $"{everNamed.Count} named out of a roster of {home.Citizens.Count} is not rare enough");

            // Nothing else about ordinary life produced a name: every citizen who never held the record is
            // unnamed, including the very oldest ones still alive behind whoever holds it.
            Assert.All(home.Citizens.Where(p => !everNamed.Contains(p.thingIDNumber)),
                p => Assert.False(p.tier.ChronicleNamed));
        }

        /// <summary>
        /// The same century again, asked the question the budget actually cares about: did this policy cost
        /// the Full tier anything. It cannot — a named citizen occupies one seat of
        /// <see cref="TieringTuning.FullTierBudget"/> and there is never more than one of them — but the
        /// failure mode being guarded against is silent, so it is measured rather than argued.
        /// </summary>
        [Fact]
        public void The_Full_tier_budget_is_unmoved_by_a_century_of_this_policy()
        {
            CoreWorld world = NewWorld("fame-budget");
            Find.World = world;
            Settlement home = Found(world, 912, "Budgeted");
            Find.God.Attention.Focus(home);

            int peakFull = home.PopulationOf(PawnTier.Full);
            int tick = 0;
            for (int year = 1; year <= 100; year++)
            {
                foreach (Pawn p in home.Citizens.ToList())
                {
                    if (!p.Dead) p.ageTracker.AgeTickMothballed(DemographyTuning.DemographyIntervalTicks);
                }

                tick += DemographyTuning.DemographyIntervalTicks;
                Find.TickManager.DebugSetTicksGame(tick);
                home.GrowthTick();
                home.SyncCitizenSpawns();
                Find.God.Attention.Reconcile();
                peakFull = System.Math.Max(peakFull, home.PopulationOf(PawnTier.Full));
            }

            Assert.True(peakFull <= TieringTuning.FullTierBudget,
                $"the Full tier overran the budget during the century (peak {peakFull})");
            Assert.True(home.Citizens.Count(p => !p.Dead && p.tier.ChronicleNamed) <= 1);
        }

        // ---- the landmark list this policy reads from ----

        [Fact]
        public void A_romance_is_news_rather_than_a_landmark()
        {
            // Regression: these lines carried no "Category:" prefix, so MomentCurator.CategoryForFreeform
            // took the whole unique sentence as the category and every single couple was a "first of its
            // kind" — appended for ever to Moments, which is never trimmed. Found while building the policy
            // that reads Moments. The first romance is still a landmark; the fiftieth is not.
            var storyteller = new Storyteller();
            Find.Storyteller = storyteller;

            for (int i = 0; i < 25; i++)
            {
                Pawn a = NewHuman("Lover" + i + "A");
                Pawn b = NewHuman("Lover" + i + "B");
                global::SimWorld.Social.RomanceUtility.BecomeLovers(a, b);
            }

            Assert.Equal(25, storyteller.Chronicle.Count(e => e.incidentDefName.StartsWith("Romance:")));
            Assert.Equal(1, storyteller.Moments.Count(e => e.incidentDefName.StartsWith("Romance:")));
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trip_of_the_record_this_policy_reads()
        {
            // The policy holds no state of its own on purpose: everything it needs is the one ratchet in
            // MomentCurator, plus a flag already Scribed on the pawn. What has to survive a save is that
            // ratchet — a loaded game whose record came back at zero would re-name the next citizen to reach
            // any age at all, and re-name one every year after that.
            List<Pawn> people = Aged(62f);
            Assert.NotNull(ChronicleFame.ConsiderDoyen(people));

            MomentCurator before = Find.Storyteller.moments;
            Assert.Equal(62f, before.LongestLifeYearsKnown, 3);

            string xml = Scribe.SaveToString(before, "moments");
            MomentCurator loaded = Scribe.Load<MomentCurator>(xml, "moments", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(62f, loaded.LongestLifeYearsKnown, 3);
            Assert.Contains(loaded.Moments, e => e.incidentDefName.StartsWith("Longevity:") && e.isMoment);

            // A loaded game does not start handing out the distinction again: the same 50-year-old who was
            // not remarkable before the save is not remarkable after it.
            var storyteller = new Storyteller();
            storyteller.moments = loaded;
            Find.Storyteller = storyteller;
            Assert.Null(ChronicleFame.ConsiderDoyen(Aged(50f)));
        }
    }
}
