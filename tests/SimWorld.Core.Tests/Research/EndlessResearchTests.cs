using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// Endless tech (<c>research.endless</c>): what a civilization that has finished all 245 authored projects
    /// keeps its researchers on.
    ///
    /// <para/>What is pinned here is the shape of the tail — that it is composed rather than enumerated, that
    /// it is a pure function of (seed, track, age, slot) so a save carries two integers, that an age has
    /// optional work in it and not just a queue, and that none of it can move the authored era ladder by a
    /// hair. How far it stays coherent is <see cref="EndlessDepthTests"/>'s job.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EndlessResearchTests : ContentTestBase
    {
        public EndlessResearchTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
        }

        private readonly ResearchManager manager;

        internal static IReadOnlyList<EndlessResearchDef> Tracks =>
            DefDatabase<EndlessResearchDef>.AllDefsListForReading;

        internal static EndlessAgeDef Ages => DefDatabase<EndlessAgeDef>.AllDefsListForReading[0];

        internal static IEnumerable<ResearchProjectDef> Authored =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => !EndlessResearch.IsGenerated(p));

        internal static IEnumerable<ResearchProjectDef> GeneratedFor(int seed) =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => EndlessResearch.BelongsTo(p, seed));

        // ---- content ----

        [Fact]
        public void Every_track_continues_a_tag_the_authored_tree_actually_uses()
        {
            Assert.NotEmpty(Tracks);

            var authoredTags = new HashSet<string>();
            foreach (ResearchProjectDef project in Authored)
            {
                if (project.tags == null) continue;
                foreach (string tag in project.tags) authoredTags.Add(tag);
            }

            // A track that continues a tag nothing is tagged with would be a tail growing out of nothing.
            Assert.All(Tracks, t => Assert.Contains(t.trackTag, authoredTags));
            Assert.All(Tracks, t => Assert.Empty(t.ConfigErrors()));
            Assert.Empty(Ages.ConfigErrors());
        }

        [Fact]
        public void A_tracks_nouns_are_its_own_and_its_two_form_lists_are_disjoint()
        {
            // Half the proof that no two generated labels can collide (the other half is that an age's theme
            // is unique to it), and the reason a matter project never borrows a mind noun.
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (EndlessResearchDef track in Tracks)
            {
                foreach (string substrate in track.substrates)
                {
                    Assert.False(owner.TryGetValue(substrate, out string? already),
                        $"'{substrate}' is a substrate of both {already} and {track.defName}; substrates must be disjoint between tracks.");
                    owner[substrate] = track.defName;
                }

                Assert.Empty(track.foundationForms.Intersect(track.appliedForms, StringComparer.Ordinal));
            }
        }

        [Fact]
        public void An_age_is_narrow_enough_that_a_tracks_leaves_cannot_repeat_each_other()
        {
            // An age's applied work permutes the substrates and the forms independently, so no two of an age's
            // leaves share either word — but only while each list is at least as long as the age is wide.
            // Content, not code, has to keep that true.
            int leaves = Ages.projectsPerTrack - 1;
            foreach (EndlessResearchDef track in Tracks)
            {
                Assert.True(track.substrates.Count >= leaves,
                    $"{track.defName} has {track.substrates.Count} substrates but an age asks for {leaves} distinct ones.");
                Assert.True(track.appliedForms.Count >= leaves,
                    $"{track.defName} has {track.appliedForms.Count} applied forms but an age asks for {leaves} distinct ones.");
            }
        }

        [Fact]
        public void The_tail_opens_no_cheaper_than_the_authored_tree_closes()
        {
            // Sourced from the shipped tree rather than from a literal: the first thing past the end of
            // everything anyone wrote down should cost at least as much as the dearest thing in it, and not
            // so much more that the tail opens with a wall.
            float dearestAuthored = Authored.Max(p => p.baseCost);
            foreach (EndlessResearchDef track in Tracks)
            {
                float first = EndlessResearch.CostFor(Ages, track, 0, 0);
                Assert.True(first >= dearestAuthored,
                    $"{track.defName} opens at {first}, under the dearest authored project ({dearestAuthored}).");
                Assert.True(first <= dearestAuthored * 3f,
                    $"{track.defName} opens at {first}, more than three times the dearest authored project ({dearestAuthored}).");
            }
        }

        [Fact]
        public void Cost_always_rises_with_depth_and_the_marginal_step_always_shrinks()
        {
            // The trend, not the exponent (CLAUDE.md: assert behaviour over literals). Rising is what makes
            // the tail something research can always be spent on; a shrinking marginal step is what stops it
            // becoming the wall the geometric curve this replaced turned into around tier 20.
            EndlessResearchDef track = Tracks[0];
            var costs = new List<float>();
            for (int age = 0; age < 200; age++)
            {
                for (int slot = 0; slot < Ages.projectsPerTrack; slot++)
                {
                    costs.Add(EndlessResearch.CostFor(Ages, track, age, slot));
                }
            }

            for (int i = 1; i < costs.Count; i++)
            {
                Assert.True(costs[i] > costs[i - 1], $"cost did not rise from depth {i} to {i + 1}.");
            }
            for (int i = 2; i < costs.Count; i++)
            {
                Assert.True(costs[i] / costs[i - 1] <= costs[i - 1] / costs[i - 2] + 1e-4f,
                    $"the step from depth {i} to {i + 1} grew rather than shrank.");
            }

            // And the far tail is walkable: a geometric curve at 1.35 would make the last of these 10^24
            // times the first, which is another way of saying nobody ever sees it.
            Assert.True(costs[costs.Count - 1] / costs[0] < 10000f,
                "the deep tail costs more than four orders of magnitude over its first project; that is a wall, not a tail.");
        }

        // ---- shape ----

        [Fact]
        public void A_civilization_with_the_tree_unfinished_has_no_endless_tech_at_all()
        {
            Assert.Equal(0, manager.EndlessAges);
            Assert.Equal(0, manager.EndlessDepth);
            Assert.Null(manager.CurrentEndlessAgeLabel);
            Assert.False(manager.NothingLeftToResearch);
            Assert.False(manager.EndlessFrontierReached);
        }

        [Fact]
        public void Opening_an_age_mints_a_foundation_and_its_applied_work_for_every_track()
        {
            manager.EndlessSeed = 910_001;
            manager.ExtendEndlessTree();

            Assert.Equal(1, manager.EndlessAges);
            Assert.Equal(Tracks.Count * Ages.projectsPerTrack, GeneratedFor(manager.EndlessSeed).Count());
            Assert.Equal(manager.EndlessDepth, Ages.projectsPerTrack);

            foreach (EndlessResearchDef track in Tracks)
            {
                ResearchProjectDef foundation = Named(Ages, track, 0, 0, manager.EndlessSeed);
                Assert.True(foundation.CanStartNow, foundation.defName + " was minted but cannot be started");
                Assert.Contains(track.trackTag, foundation.tags!);
                Assert.Contains(EndlessResearch.EndlessTag, foundation.tags!);

                for (int slot = 1; slot < Ages.projectsPerTrack; slot++)
                {
                    ResearchProjectDef applied = Named(Ages, track, 0, slot, manager.EndlessSeed);
                    Assert.Same(foundation, Assert.Single(applied.prerequisites!));
                    Assert.False(applied.CanStartNow, applied.defName + " was startable before its own foundation");
                }
            }
        }

        [Fact]
        public void An_ages_applied_work_is_optional_and_its_foundation_is_not()
        {
            // The same minority-spine shape the authored tree was restructured into, and for the same reason:
            // an age with nothing skippable in it is a queue, and two civilizations walk a queue identically.
            manager.EndlessSeed = 910_002;
            for (int i = 0; i < 3; i++) manager.ExtendEndlessTree();

            var dependedUpon = new HashSet<ResearchProjectDef>();
            foreach (ResearchProjectDef project in GeneratedFor(manager.EndlessSeed))
            {
                if (project.prerequisites == null) continue;
                foreach (ResearchProjectDef prerequisite in project.prerequisites) dependedUpon.Add(prerequisite);
            }

            foreach (EndlessResearchDef track in Tracks)
            {
                for (int age = 0; age < 2; age++)
                {
                    Assert.Contains(Named(Ages, track, age, 0, manager.EndlessSeed), dependedUpon);
                    for (int slot = 1; slot < Ages.projectsPerTrack; slot++)
                    {
                        Assert.DoesNotContain(Named(Ages, track, age, slot, manager.EndlessSeed), dependedUpon);
                    }
                }
            }

            double spineFraction = dependedUpon.Count(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed))
                / (double)GeneratedFor(manager.EndlessSeed).Count();
            Assert.InRange(spineFraction, 0.1, 0.5);
        }

        [Fact]
        public void An_age_opens_for_every_track_at_once_or_for_none()
        {
            // A foundation follows its own track's previous foundation and one other track's, so no single
            // line of enquiry runs away from the rest — the tail is a civilization, not parallel counters.
            manager.EndlessSeed = 910_003;
            manager.ExtendEndlessTree();
            manager.ExtendEndlessTree();

            var firstAgeFoundations = new HashSet<ResearchProjectDef>(
                Tracks.Select(t => Named(Ages, t, 0, 0, manager.EndlessSeed)));

            foreach (EndlessResearchDef track in Tracks)
            {
                ResearchProjectDef second = Named(Ages, track, 1, 0, manager.EndlessSeed);
                Assert.True(second.prerequisites!.Count >= 2, second.defName + " follows only its own track.");
                Assert.Contains(Named(Ages, track, 0, 0, manager.EndlessSeed), second.prerequisites);

                // Every one of them is a foundation of the age below — never one of its optional projects,
                // which would make optional work mandatory for anyone who wants the next age.
                Assert.All(second.prerequisites, p => Assert.Contains(p, firstAgeFoundations));
            }
        }

        // ---- the authored ladder is untouched ----

        [Fact]
        public void Endless_tech_belongs_to_no_era_and_names_no_authored_project()
        {
            manager.EndlessSeed = 910_004;
            for (int i = 0; i < 5; i++) manager.ExtendEndlessTree();

            var authored = new HashSet<ResearchProjectDef>(Authored);
            foreach (ResearchProjectDef project in GeneratedFor(manager.EndlessSeed))
            {
                Assert.Null(project.era);
                foreach (ResearchProjectDef prerequisite in project.prerequisites ?? new List<ResearchProjectDef>())
                {
                    Assert.DoesNotContain(prerequisite, authored);
                }
                Assert.Null(project.hiddenPrerequisites);
            }

            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading)
            {
                Assert.DoesNotContain(era.Projects, EndlessResearch.IsGenerated);
                Assert.DoesNotContain(era.SpineProjects, EndlessResearch.IsGenerated);
            }
        }

        [Fact]
        public void Opening_ages_changes_no_authored_eras_spine_or_completion()
        {
            // The hazard this rules out is specific: a generated project naming an authored one would put
            // that project into its era's SpineProjects, and so raise what completing an authored era
            // requires — a run-time edit to the ladder, made by content that did not exist when it was
            // authored. Measured before and after rather than argued.
            manager.DebugSetAllProjectsFinished();
            IReadOnlyList<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading;
            var projectsBefore = eras.ToDictionary(e => e, e => e.Projects.Count);
            var spineBefore = eras.ToDictionary(e => e, e => new HashSet<ResearchProjectDef>(e.SpineProjects));
            EraDef? eraBefore = manager.CurrentEra;

            manager.EndlessSeed = 910_005;
            for (int i = 0; i < 8; i++) manager.ExtendEndlessTree();
            foreach (EraDef era in eras) era.ClearCachedData();

            foreach (EraDef era in eras)
            {
                Assert.Equal(projectsBefore[era], era.Projects.Count);
                Assert.True(spineBefore[era].SetEquals(era.SpineProjects), era.defName + "'s spine changed.");
                Assert.True(era.IsComplete, era.defName + " stopped being complete.");
            }
            Assert.Same(eraBefore, manager.CurrentEra);
            Assert.Null(manager.NextEra);
        }

        // ---- extension ----

        [Fact]
        public void Finishing_the_last_thing_there_is_to_research_produces_something_new_to_research()
        {
            manager.EndlessSeed = 910_006;
            manager.DebugSetAllProjectsFinished();

            // The authored tree is done, so the civilization has run out — which is the whole condition.
            Assert.True(manager.NothingLeftToResearch);

            manager.EnsureSomethingToResearch();
            Assert.False(manager.NothingLeftToResearch);
            Assert.True(manager.EndlessAges >= 1);
            Assert.NotEmpty(GeneratedFor(manager.EndlessSeed));
            Assert.NotNull(manager.CurrentEndlessAgeLabel);
        }

        [Fact]
        public void Reaching_the_frontier_opens_the_next_age_without_buying_everything_in_this_one()
        {
            manager.EndlessSeed = 910_007;
            manager.DebugSetAllProjectsFinished();
            manager.EnsureSomethingToResearch();
            Assert.Equal(1, manager.EndlessAges);

            // Race the foundations only, leaving every optional project of the age unbought.
            foreach (EndlessResearchDef track in Tracks)
            {
                manager.FinishProject(Named(Ages, track, 0, 0, manager.EndlessSeed));
            }

            Assert.Equal(2, manager.EndlessAges);
            Assert.Contains(GeneratedFor(manager.EndlessSeed), p => !manager.IsFinished(p) && p.prerequisites?.Count == 1);
        }

        [Fact]
        public void And_it_keeps_producing_one_however_many_times_the_tail_is_finished()
        {
            manager.EndlessSeed = 910_008;
            manager.DebugSetAllProjectsFinished();
            manager.EnsureSomethingToResearch();

            for (int round = 0; round < 20; round++)
            {
                ResearchProjectDef next = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .First(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed) && p.CanStartNow);
                manager.FinishProject(next);
                Assert.False(manager.NothingLeftToResearch, "the tail ran out after round " + round);
            }
        }

        [Fact]
        public void One_civilizations_tail_is_not_another_civilizations_remaining_work()
        {
            // The DefDatabase outlives a game. Before generated defNames carried the seed they were derived
            // from, a second civilization in the same process saw the first one's unfinished tail as its own
            // remaining work and so never extended past the authored tree at all.
            var first = new ResearchManager { EndlessSeed = 910_009 };
            Find.ResearchManager = first;
            first.DebugSetAllProjectsFinished();
            first.EnsureSomethingToResearch();
            Assert.True(first.EndlessAges >= 1);

            var second = new ResearchManager { EndlessSeed = 910_010 };
            Find.ResearchManager = second;
            second.DebugSetAllProjectsFinished();
            Assert.True(second.NothingLeftToResearch, "the second civilization inherited the first one's unfinished tail");
            second.EnsureSomethingToResearch();
            Assert.True(second.EndlessAges >= 1);
            Assert.Empty(GeneratedFor(910_009).Intersect(GeneratedFor(910_010)));
        }

        // ---- determinism ----

        [Fact]
        public void Two_civilizations_on_one_seed_agree_and_two_on_different_seeds_do_not()
        {
            List<string> onSeed(int seed)
            {
                var labels = new List<string>();
                for (int age = 0; age < 12; age++)
                {
                    foreach (EndlessResearchDef track in Tracks)
                    {
                        for (int slot = 0; slot < Ages.projectsPerTrack; slot++)
                        {
                            labels.Add(EndlessResearch.LabelFor(Ages, track, age, slot, seed));
                        }
                    }
                }
                return labels;
            }

            Assert.Equal(onSeed(4242), onSeed(4242));
            List<string> other = onSeed(9191);
            Assert.NotEqual(onSeed(4242), other);

            // Not merely reordered: two civilizations should be working on different things, not the same
            // things in a different order.
            Assert.True(onSeed(4242).Intersect(other).Count() < other.Count / 2,
                "two seeds share more than half their generated tech");
        }

        [Fact]
        public void Nothing_the_shared_random_stream_does_in_between_changes_the_tail()
        {
            // The whole reason this is derived rather than rolled: the tail must not depend on how many
            // pawns were generated, or raids rolled, between one project and the next.
            EndlessResearchDef track = Tracks[0];
            var before = new List<string>();
            for (int age = 0; age < 6; age++) before.Add(EndlessResearch.LabelFor(Ages, track, age, 0, 77));

            for (int i = 0; i < 500; i++) _ = Rand.Value;

            var after = new List<string>();
            for (int age = 0; age < 6; age++) after.Add(EndlessResearch.LabelFor(Ages, track, age, 0, 77));
            Assert.Equal(before, after);
        }

        [Fact]
        public void The_nth_generated_project_is_nameable_without_generating_the_first_n_minus_one()
        {
            // An index, not a sequence. Asking for the label of a deep project must not require minting
            // anything at all — and when it is eventually minted, it must be what was promised.
            const int seed = 910_011;
            EndlessResearchDef track = Tracks[2];
            const int deepAge = 137;
            string promised = EndlessResearch.LabelFor(Ages, track, deepAge, 2, seed);
            Assert.DoesNotContain(DefDatabase<ResearchProjectDef>.AllDefsListForReading, p => EndlessResearch.BelongsTo(p, seed));

            ResearchProjectDef minted = EndlessResearch.MintOrGet(
                Ages, Tracks, 2, deepAge, 2, seed, DefDatabase.Global);
            Assert.Equal(promised, minted.label);
        }

        // ---- save ----

        [Fact]
        public void A_save_in_the_middle_of_a_generated_run_reloads_the_same_tree()
        {
            manager.EndlessSeed = 910_012;
            manager.DebugSetAllProjectsFinished();
            manager.EnsureSomethingToResearch();
            for (int i = 0; i < 3; i++) manager.ExtendEndlessTree();

            EndlessResearchDef track = Tracks[0];
            ResearchProjectDef firstFoundation = Named(Ages, track, 0, 0, manager.EndlessSeed);
            ResearchProjectDef midAge = Named(Ages, track, 2, 1, manager.EndlessSeed);
            manager.FinishProject(firstFoundation);
            manager.CurrentProj = Named(Ages, track, 0, 1, manager.EndlessSeed);
            manager.ResearchPerformed(500f, null);

            string xml = Scribe.SaveToString(manager, "research");
            Assert.Contains("endlessAges", xml);
            Assert.Contains("endlessSeed", xml);
            // The tail itself is not in the save: two integers stand for every generated project.
            Assert.DoesNotContain(midAge.label!, xml);

            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", Content.Database);
            Assert.Equal(manager.EndlessAges, loaded.EndlessAges);
            Assert.Equal(manager.EndlessSeed, loaded.EndlessSeed);
            Assert.Equal(manager.CurrentEndlessAgeLabel, loaded.CurrentEndlessAgeLabel);

            ResearchProjectDef reloaded = Named(Ages, track, 2, 1, manager.EndlessSeed);
            Assert.Same(midAge, reloaded);
            Assert.Equal(EndlessResearch.CostFor(Ages, track, 2, 1), reloaded.baseCost, 3);
            Assert.True(loaded.IsFinished(firstFoundation));
            Assert.Equal(manager.GetProgress(manager.CurrentProj!), loaded.GetProgress(loaded.CurrentProj!));
        }

        [Fact]
        public void Opening_an_age_is_announced_but_loading_a_save_never_re_announces_one()
        {
            manager.EndlessSeed = 910_013;
            manager.DebugSetAllProjectsFinished();

            var opened = new List<string>();
            manager.EndlessAgeOpened += (index, label) => opened.Add(label);
            int chronicleBefore = Find.Storyteller.Chronicle.Count;
            int lettersBefore = Find.LetterStack.LettersListForReading.Count;

            manager.EnsureSomethingToResearch();

            Assert.NotEmpty(opened);
            Assert.Equal(manager.CurrentEndlessAgeLabel, opened[opened.Count - 1]);
            Assert.True(Find.Storyteller.Chronicle.Count > chronicleBefore);
            Assert.True(Find.LetterStack.LettersListForReading.Count > lettersBefore);
            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.StartsWith("Age opened:", StringComparison.Ordinal));

            string xml = Scribe.SaveToString(manager, "research");
            int chronicleAfterSave = Find.Storyteller.Chronicle.Count;
            int lettersAfterSave = Find.LetterStack.LettersListForReading.Count;
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", Content.Database);

            Assert.Equal(manager.EndlessAges, loaded.EndlessAges);
            Assert.Equal(chronicleAfterSave, Find.Storyteller.Chronicle.Count);
            Assert.Equal(lettersAfterSave, Find.LetterStack.LettersListForReading.Count);
        }

        internal static ResearchProjectDef Named(EndlessAgeDef ages, EndlessResearchDef track, int age, int slot, int seed) =>
            DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(ages, track, age, slot, seed));
    }
}
