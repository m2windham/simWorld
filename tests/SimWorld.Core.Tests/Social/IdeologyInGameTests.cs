using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Social.Ideology;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Social
{
    /// <summary>
    /// <c>Ideo</c> instantiated, and what that actually changes about a running game.
    ///
    /// <para/><b>Why this file exists.</b> Every other test in <see cref="IdeologyTests"/> proves the ported
    /// mechanics are correct in isolation — a hand-built <see cref="Ideo"/>, assigned to
    /// <see cref="IdeoManager.Current"/> by the test itself, driving a hand-built <see cref="Pawn"/>. None of
    /// that ever exercises <c>new Ideo(</c> outside a test, because nothing in <c>src/</c> ever called it —
    /// <c>docs/design/player-first.md</c> §10's own audit names this precisely: "a step further back" than a
    /// mechanism with no caller. This suite drives a real <see cref="Game.NewGame"/>, through
    /// <see cref="Scenario.ScenPart_StartingIdeo"/>, and asks what a citizen's real, tick-driven simulation
    /// state looks like with a civilization's belief system actually wired in — never by calling a
    /// <see cref="Social.Ideology.PreceptWorker"/> directly.
    ///
    /// <para/><b>The honest boundary, pinned rather than asserted in prose.</b> Precept-driven mood is proven
    /// live below, through nothing but <see cref="Sim.TickManager.DoSingleTick"/>. Role-holding and ritual
    /// performance are not: nothing in <c>src/</c> ever calls <see cref="Ideo.TryAssignRole"/> or
    /// <see cref="Social.Ideology.RitualUtility.PerformRitual"/> on its own, so a citizen never actually comes
    /// to hold a role or a ritual never actually happens by simply letting a founded civilization run.
    /// <see cref="Nothing_assigns_a_role_or_performs_a_ritual_on_its_own_no_matter_how_long_the_game_runs"/>
    /// pins exactly that, so the gap stays visible instead of being quietly assumed away the next time
    /// somebody reads this suite and sees roles and rituals exercised a few tests above it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class IdeologyInGameTests : ContentTestBase, IDisposable
    {
        public IdeologyInGameTests(CoreContentFixture content) : base(content)
        {
            IdeoManager.Reset();
        }

        public void Dispose()
        {
            IdeoManager.Reset();
        }

        /// <summary>Long enough for the mood seeker (0.05-0.06/hour, a 150-tick interval) to fully close a
        /// three-point thought offset several times over — the arithmetic is in this suite's own report, not
        /// repeated as a magic number here — and short enough to stay a test. <see cref="SimDeterminismTests"/>
        /// uses the same order of magnitude for the same reason: long enough for the tick lists and the
        /// thought system to have genuinely turned over.</summary>
        private const int Ticks = 6000;

        private static IdeoDef Hearthway => DefDatabase<IdeoDef>.GetNamed("TheHearthway");
        private static IdeoDef ForgeCovenant => DefDatabase<IdeoDef>.GetNamed("TheForgeCovenant");
        private static IdeoRoleDef Elder => DefDatabase<IdeoRoleDef>.GetNamed("Elder");
        private static IdeoRoleDef Forgemaster => DefDatabase<IdeoRoleDef>.GetNamed("Forgemaster");
        private static RitualDef HearthGathering => DefDatabase<RitualDef>.GetNamed("HearthGathering");
        private static RitualDef ForgeRite => DefDatabase<RitualDef>.GetNamed("ForgeRite");

        private static Game NewSoloGame(string seed, int bandSize = 20) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: bandSize);

        // ---- 1. instantiation at game start ----

        [Fact]
        public void NewGame_through_the_shipped_TribalStart_scenario_ends_with_a_non_null_Ideo()
        {
            Game game = NewSoloGame("ideo-instantiation-seed");

            Assert.NotNull(game.Ideo);
            Assert.True(game.Ideo!.def == Hearthway || game.Ideo.def == ForgeCovenant);
            Assert.Same(game.Ideo, Find.Ideo); // Find.Ideo resolves through the current game, same as every other service.
        }

        [Fact]
        public void The_same_seed_draws_the_same_ideology_every_time()
        {
            IdeoDef a = NewSoloGame("same-ideo-seed").Ideo!.def;
            IdeoDef b = NewSoloGame("same-ideo-seed").Ideo!.def;
            IdeoDef c = NewSoloGame("same-ideo-seed").Ideo!.def;

            Assert.Same(a, b);
            Assert.Same(b, c);
        }

        /// <summary>The other half of the claim, exactly as <see cref="SimDeterminismTests.A_different_seed_runs_a_different_game"/>
        /// exists beside its own same-seed test: a draw that never varied would satisfy the test above by
        /// being broken, not by being seeded. Swept rather than asserted on one pair, because there are only
        /// two candidates — a single differing pair proves the draw is seed-sensitive; it does not need every
        /// pair to differ.</summary>
        [Fact]
        public void Different_seeds_can_draw_the_other_ideology()
        {
            var drawn = new HashSet<string>();
            for (int i = 0; i < 20 && drawn.Count < 2; i++)
            {
                drawn.Add(NewSoloGame("ideo-sweep-seed-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Ideo!.def.defName);
            }

            Assert.Equal(2, drawn.Count);
        }

        // ---- 2. proof it is alive: precepts move a real citizen's mood through the real tick loop ----

        /// <summary>
        /// The core loop this whole lane exists to light up: <see cref="Needs.Need_Mood.NeedInterval"/> runs
        /// every 150 ticks, off nothing but <see cref="Sim.TickManager.DoSingleTick"/>; it reads
        /// <see cref="Thoughts.SituationalThoughtHandler"/>, which recalculates every 10 ticks off the same
        /// clock and, for <c>HearthwayBelonging</c>/<c>ForgeCovenantBelonging</c>, asks nothing about the
        /// citizen at all beyond <see cref="Social.Ideology.PreceptWorker.AppliesTo"/>'s Humanlike default — so
        /// every founder gets it the instant an <see cref="Ideo"/> exists. No line below calls a
        /// <see cref="Social.Ideology.PreceptWorker"/>, a <see cref="Thoughts.ThoughtWorker"/>, or
        /// <c>Recalculate()</c> — only <c>DoSingleTick</c> and a plain read of <see cref="Needs.Need.CurLevel"/>.
        ///
        /// <para/><b>Same seed, ideology forcibly cleared for the control arm.</b> <see cref="Game.NewGame"/>
        /// reseeds <see cref="Rand"/> from the seed string at its very top, so two calls with the same seed
        /// draw an identical founding band, tile and starting ideology up to the instant
        /// <see cref="ScenPart_StartingIdeo.PostGameStart"/> returns (see
        /// <see cref="GameTests.Same_seed_founds_the_same_size_settlement_on_the_same_tile"/> for the same
        /// property pinned on world generation). Clearing <see cref="Find.Ideo"/> immediately afterward, before
        /// a single tick runs, isolates the one input this test is about: every other seeded draw the rest of
        /// the run makes is identical between the two arms at tick zero.
        /// </summary>
        [Fact]
        public void A_founders_mood_rises_measurably_more_under_an_active_Ideo_than_an_identical_founding_with_none()
        {
            Game withIdeo = NewSoloGame("ideo-mood-seed");
            Pawn founderWithIdeo = withIdeo.World!.Settlements.First().Citizens[0];

            Game withoutIdeo = NewSoloGame("ideo-mood-seed"); // same seed: identical founding band and tile.
            Find.Ideo = null; // the one input this test isolates — cleared before a single tick runs.
            Pawn founderWithoutIdeo = withoutIdeo.World!.Settlements.First().Citizens[0];

            // Same seed really did found the same band (Name has no value equality, so compare the rendered form).
            Assert.Equal(founderWithIdeo.Name?.ToStringFull, founderWithoutIdeo.Name?.ToStringFull);

            for (int i = 0; i < Ticks; i++)
            {
                withIdeo.TickManager.DoSingleTick();
                withoutIdeo.TickManager.DoSingleTick();
            }

            float moodWithIdeo = founderWithIdeo.needs.mood!.CurLevel;
            float moodWithoutIdeo = founderWithoutIdeo.needs.mood!.CurLevel;

            // The belonging precept is a flat +3 mood points under either shipped ideoligion and asks nothing
            // about the citizen, so this direction holds regardless of which one the seed drew.
            Assert.True(moodWithIdeo > moodWithoutIdeo,
                $"expected the Ideo arm's mood ({moodWithIdeo}) to have risen above the no-Ideo control's ({moodWithoutIdeo}) " +
                "from nothing but real ticks — the precept/thought/need chain never fired.");
        }

        /// <summary>
        /// The same proof from the read side a host would actually use: a snapshot taken after real ticking
        /// finds the belonging thought active, without this test ever calling
        /// <see cref="Thoughts.SituationalThoughtHandler.Recalculate"/> or reaching into a precept worker.
        /// <see cref="Thoughts.ThoughtHandler.GetAllMoodThoughts"/> is the same call
        /// <see cref="Needs.Need_Mood.CurInstantLevel"/> makes every interval; this only asks it to list what
        /// it found rather than sum it, so the specific thought driving the mood shift above can be named.
        /// </summary>
        [Fact]
        public void The_belonging_thought_is_actually_active_on_a_founder_after_real_ticking()
        {
            Game game = NewSoloGame("ideo-belonging-seed");
            bool hearthway = game.Ideo!.def == Hearthway;
            ThoughtDef expected = DefDatabase<ThoughtDef>.GetNamed(hearthway ? "HearthwayBelonging" : "ForgeCovenantBelonging");

            Pawn founder = game.World!.Settlements.First().Citizens[0];
            for (int i = 0; i < 100; i++) game.TickManager.DoSingleTick(); // well past both the 10-tick and 150-tick cadences.

            var thoughts = new List<Thought>();
            founder.needs.mood!.thoughts.GetAllMoodThoughts(thoughts);

            Assert.Contains(thoughts, t => t.def == expected);
        }

        // ---- 3. the honest boundary: roles and rituals are reachable, not automatic ----

        /// <summary>
        /// Exercises <see cref="Ideo.TryAssignRole"/> and <see cref="Social.Ideology.RitualUtility.PerformRitual"/>
        /// against a real game's real <see cref="Ideo"/> and real citizens — proving the machinery a role/ritual
        /// needs actually works end to end on live simulation objects, not a hand-built test fixture. This is a
        /// direct call to each, deliberately: nothing in <c>src/</c> calls either on its own (see
        /// <see cref="Nothing_assigns_a_role_or_performs_a_ritual_on_its_own_no_matter_how_long_the_game_runs"/>),
        /// so "a role that can be held" and "a ritual that can happen" are exactly what this proves — capability,
        /// not an automatic occurrence. The ritual's own memory thought is then read back through the real
        /// thought pipeline (<see cref="Thoughts.ThoughtHandler.GetAllMoodThoughts"/>) rather than assumed,
        /// exactly as <see cref="The_belonging_thought_is_actually_active_on_a_founder_after_real_ticking"/> does.
        /// </summary>
        [Fact]
        public void A_role_can_be_held_and_a_ritual_can_be_performed_on_a_real_games_own_citizens()
        {
            Game game = NewSoloGame("ideo-role-ritual-seed", bandSize: 20);
            IReadOnlyList<Pawn> citizens = game.World!.Settlements.First().Citizens;
            bool hearthway = game.Ideo!.def == Hearthway;
            IdeoRoleDef role = hearthway ? Elder : Forgemaster;
            RitualDef ritual = hearthway ? HearthGathering : ForgeRite;
            ThoughtDef attendeeMemory = DefDatabase<ThoughtDef>.GetNamed(hearthway ? "AttendedHearthGathering" : "AttendedForgeRite");

            Pawn officiant = citizens[0];
            Assert.True(game.Ideo.TryAssignRole(officiant, role));
            Assert.Equal(role, game.Ideo.Roles.RoleOf(officiant));

            RitualOutcome outcome = RitualUtility.PerformRitual(ritual, citizens, game.Ideo);
            Assert.InRange(outcome.Quality, 0f, 1f);

            var thoughts = new List<Thought>();
            officiant.needs.mood!.thoughts.GetAllMoodThoughts(thoughts);
            Assert.Contains(thoughts, t => t.def == attendeeMemory);
        }

        /// <summary>
        /// The pin for this lane's own honest finding: an ideology sitting on a civilization that nobody ever
        /// tells to hold a role or perform a ritual runs for a real, tick-driven while and does neither on its
        /// own. If this ever goes red, something in <c>src/</c> started calling
        /// <see cref="Ideo.TryAssignRole"/> or <see cref="Social.Ideology.RitualUtility.PerformRitual"/> — which
        /// is a good day, and this test (and this suite's own report) should be updated to say so rather than
        /// deleted quietly.
        /// </summary>
        [Fact]
        public void Nothing_assigns_a_role_or_performs_a_ritual_on_its_own_no_matter_how_long_the_game_runs()
        {
            Game game = NewSoloGame("ideo-nothing-automatic-seed", bandSize: 20);
            IReadOnlyList<Pawn> citizens = game.World!.Settlements.First().Citizens;

            for (int i = 0; i < Ticks; i++) game.TickManager.DoSingleTick();

            Assert.All(citizens, p => Assert.Null(game.Ideo!.Roles.RoleOf(p)));

            ThoughtDef hearthGatheringMemory = DefDatabase<ThoughtDef>.GetNamed("AttendedHearthGathering");
            ThoughtDef forgeRiteMemory = DefDatabase<ThoughtDef>.GetNamed("AttendedForgeRite");
            Assert.All(citizens, p =>
            {
                var thoughts = new List<Thought>();
                p.needs.mood!.thoughts.GetAllMoodThoughts(thoughts);
                Assert.DoesNotContain(thoughts, t => t.def == hearthGatheringMemory || t.def == forgeRiteMemory);
            });
        }

        // ---- 4. the read side: earned by section 2's proof, not built ahead of it ----

        [Fact]
        public void CurrentIdeology_reports_null_with_no_Ideo_and_the_right_name_memes_and_precepts_once_one_exists()
        {
            Assert.Null(GodViewSnapshot.CurrentIdeology()); // no game current at all yet.

            Game game = NewSoloGame("ideo-view-seed");
            bool hearthway = game.Ideo!.def == Hearthway;

            IdeologySummary? summary = GodViewSnapshot.CurrentIdeology();

            Assert.NotNull(summary);
            Assert.Equal(game.Ideo.name, summary!.Name);
            Assert.Equal(game.Ideo.def.memes.Count, summary.Memes.Count);
            Assert.Equal(game.Ideo.Precepts.Count, summary.Precepts.Count);
            string expectedPrecept = hearthway ? "elders lead" : "forgemasters lead";
            Assert.Contains(summary.Precepts, p => p.Equals(expectedPrecept, StringComparison.OrdinalIgnoreCase));
        }

        // ---- 5. Scribe: the whole Game, not just an Ideo in isolation ----

        /// <summary>
        /// <see cref="IdeologyTests.Ideo_and_its_role_assignments_round_trip_through_Scribe"/> already pins an
        /// <see cref="Ideo"/> round-tripping inside a purpose-built holder. This is the question that actually
        /// matters for a save file: does <see cref="Game.ExposeData"/> — which nobody had reason to check,
        /// because nothing ever fed <see cref="Game.Ideo"/> before this lane — actually carry it through a real
        /// game's save and load. It already does (<c>Scribe_Deep.Look(ref beliefs, "ideo")</c> has been in
        /// <see cref="Game.ExposeData"/> since <c>Game.Ideo</c> was added), which this pins rather than assumes.
        /// </summary>
        [Fact]
        public void A_whole_Games_Ideo_and_role_holders_survive_a_real_Scribe_round_trip()
        {
            Game game = NewSoloGame("ideo-scribe-seed", bandSize: 20);
            IdeoDef expectedDef = game.Ideo!.def;
            IdeoRoleDef role = expectedDef == Hearthway ? Elder : Forgemaster;

            Pawn officiant = game.World!.Settlements.First().Citizens[0];
            Assert.True(game.Ideo.TryAssignRole(officiant, role));

            for (int i = 0; i < 500; i++) game.TickManager.DoSingleTick();

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.NotNull(loaded.Ideo);
            Assert.Same(expectedDef, loaded.Ideo!.def);

            Pawn? loadedOfficiant = loaded.World!.Settlements.First().Citizens
                .FirstOrDefault(p => p.thingIDNumber == officiant.thingIDNumber);
            Assert.NotNull(loadedOfficiant);
            Assert.Equal(role, loaded.Ideo.Roles.RoleOf(loadedOfficiant!));
        }
    }
}
