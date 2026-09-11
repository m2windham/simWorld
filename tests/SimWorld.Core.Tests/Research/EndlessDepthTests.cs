using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// How far the endless tail stays worth having. <see cref="EndlessResearchTests"/> pins its shape; this
    /// runs a civilization a long way past the end of the authored tree and asks whether what it finds there
    /// is still research — distinct, coherent, and legibly belonging to the age it turned up in — rather than
    /// a string with a number on the end.
    ///
    /// <para/>The number to beat is the one this replaced: three tracks of three authored titles, so the tenth
    /// generated project a civilization ever saw was already "matter compilation IV".
    /// </summary>
    [Collection("GlobalDefs")]
    public class EndlessDepthTests : ContentTestBase
    {
        public EndlessDepthTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
        }

        private readonly ResearchManager manager;

        /// <summary>Ages to run past the authored ladder. Eight authored eras hold 245 projects; twenty-five
        /// generated ages hold 600, so this is a civilization that has spent longer past the end of its
        /// records than inside them.</summary>
        private const int DeepAges = 25;

        private static EndlessAgeDef Ages => EndlessResearchTests.Ages;

        private static IReadOnlyList<EndlessResearchDef> Tracks => EndlessResearchTests.Tracks;

        /// <summary>A label that is a word with a number after it — the failure mode this whole module is a
        /// response to. Digits, Roman numerals (case-sensitive: "civil" is spelled out of the same five
        /// letters), and the filler a generator reaches for once it has run out of ideas.</summary>
        private static readonly Regex Counter =
            new Regex(@"\s(?:[0-9]+|[IVXLC]{1,6}|(?i:mark|phase|stage|tier|level|gen)\s*[0-9]*)$");

        /// <summary>Finishes projects cheapest-first until the civilization has finished
        /// <paramref name="count"/> of them, extending the tail as it goes. Returns them in order.</summary>
        private List<ResearchProjectDef> RunCivilization(int count)
        {
            var finished = new List<ResearchProjectDef>(count);
            for (int i = 0; i < count; i++)
            {
                ResearchProjectDef? next = Startable()
                    .OrderBy(p => p.baseCost)
                    .ThenBy(p => p.defName, StringComparer.Ordinal)
                    .FirstOrDefault();
                Assert.True(next != null, $"the civilization ran out of research after {i} generated projects.");

                foreach (ResearchProjectDef prerequisite in next!.prerequisites ?? new List<ResearchProjectDef>())
                {
                    Assert.True(manager.IsFinished(prerequisite),
                        $"{next.defName} was startable with {prerequisite.defName} unfinished.");
                }

                manager.FinishProject(next);
                finished.Add(next);
            }
            return finished;
        }

        private IEnumerable<ResearchProjectDef> Startable() =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed) && p.CanStartNow);

        private void ReachTheTail(int seed) => ReachTheTail(manager, seed);

        /// <summary>
        /// Puts a civilization exactly where endless tech begins: the authored tree finished and nothing else
        /// touched. Deliberately not <see cref="ResearchManager.DebugSetAllProjectsFinished"/>, which finishes
        /// every project in the process-wide database — including a tail another civilization in this test run
        /// already generated, which is not history this one lived.
        /// </summary>
        private static void ReachTheTail(ResearchManager target, int seed)
        {
            target.EndlessSeed = seed;
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => !EndlessResearch.IsGenerated(p)).ToList())
            {
                target.SetProjectFinishedForSetup(project);
            }
            target.EnsureSomethingToResearch();
        }

        [Fact]
        public void A_civilization_far_past_the_authored_tree_never_runs_out_of_work()
        {
            ReachTheTail(730_001);
            List<ResearchProjectDef> finished = RunCivilization(DeepAges * Tracks.Count * Ages.projectsPerTrack);

            Assert.Equal(finished.Count, finished.Distinct().Count());
            Assert.True(manager.EndlessAges > DeepAges,
                $"{DeepAges * Tracks.Count * Ages.projectsPerTrack} projects in, only {manager.EndlessAges} ages had opened.");
            Assert.False(manager.NothingLeftToResearch);
            Assert.NotEmpty(Startable());
        }

        [Fact]
        public void Everything_it_finds_out_there_is_distinct_and_none_of_it_is_a_counter()
        {
            ReachTheTail(730_002);
            RunCivilization(DeepAges * Tracks.Count * Ages.projectsPerTrack);

            List<ResearchProjectDef> generated = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed)).ToList();
            Assert.True(generated.Count >= 600, $"only {generated.Count} projects were generated.");

            var labels = generated.Select(p => p.label!).ToList();
            Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(generated.Count, generated.Select(p => p.defName).Distinct(StringComparer.Ordinal).Count());

            foreach (string label in labels)
            {
                Assert.False(Counter.IsMatch(label), $"'{label}' is a name with a counter on the end.");
                Assert.True(label.Split(' ').Length >= 3, $"'{label}' is too thin to read as a discipline.");
                Assert.Equal(label, label.Trim());
            }
        }

        [Fact]
        public void Every_generated_project_reads_as_its_own_age_and_its_own_track()
        {
            // "Coherent, era-appropriate" made checkable: a label is exactly the age's shared register word,
            // then a noun this track and no other studies, then a form that says whether it is the age's
            // foundation or work hanging off it.
            ReachTheTail(730_003);
            RunCivilization(DeepAges * Tracks.Count * Ages.projectsPerTrack);

            var themes = new HashSet<string>(StringComparer.Ordinal);
            for (int age = 0; age < manager.EndlessAges; age++)
            {
                string theme = Ages.ThemeFor(age, manager.EndlessSeed);
                Assert.True(themes.Add(theme), $"age {age} reuses the theme '{theme}'.");

                foreach (EndlessResearchDef track in Tracks)
                {
                    var substratesUsed = new List<string>();
                    var formsUsed = new List<string>();
                    for (int slot = 0; slot < Ages.projectsPerTrack; slot++)
                    {
                        ResearchProjectDef project = EndlessResearchTests.Named(Ages, track, age, slot, manager.EndlessSeed);
                        string label = project.label!;

                        Assert.StartsWith(theme + " ", label, StringComparison.Ordinal);
                        Assert.Contains(theme, project.tags!);
                        Assert.Contains(track.trackTag, project.tags!);

                        string rest = label.Substring(theme.Length + 1);
                        string substrate = track.substrates.SingleOrDefault(s => rest.StartsWith(s + " ", StringComparison.Ordinal))
                            ?? throw new Xunit.Sdk.XunitException($"'{label}' names no substrate of {track.defName}.");

                        string form = rest.Substring(substrate.Length + 1);
                        List<string> expected = EndlessResearch.IsFoundationSlot(slot) ? track.foundationForms : track.appliedForms;
                        List<string> forbidden = EndlessResearch.IsFoundationSlot(slot) ? track.appliedForms : track.foundationForms;
                        Assert.Contains(form, expected);
                        Assert.DoesNotContain(form, forbidden);

                        if (EndlessResearch.IsFoundationSlot(slot)) continue;
                        substratesUsed.Add(substrate);
                        formsUsed.Add(form);
                    }

                    // An age's applied work varies in both words, not just the last one: three projects that
                    // read "precedent appeal, precedent convening, precedent auditing" are one idea with three
                    // endings, which is the same complaint as a numeral in a longer coat.
                    Assert.Equal(substratesUsed.Count, substratesUsed.Distinct(StringComparer.Ordinal).Count());
                    Assert.Equal(formsUsed.Count, formsUsed.Distinct(StringComparer.Ordinal).Count());
                }
            }
        }

        [Fact]
        public void Nothing_out_there_follows_from_the_authored_tree_or_from_anything_deeper_than_itself()
        {
            ReachTheTail(730_004);
            RunCivilization(DeepAges * Tracks.Count * Ages.projectsPerTrack);

            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed)))
            {
                Assert.Null(project.era);
                Assert.Empty(project.ConfigErrors());

                int depth = DepthOf(project);
                foreach (ResearchProjectDef prerequisite in project.prerequisites ?? new List<ResearchProjectDef>())
                {
                    Assert.True(EndlessResearch.BelongsTo(prerequisite, manager.EndlessSeed),
                        $"{project.defName} follows {prerequisite.defName}, which is not this civilization's own tail.");
                    Assert.True(DepthOf(prerequisite) < depth,
                        $"{project.defName} follows {prerequisite.defName}, which is at least as deep.");
                }
            }
        }

        [Fact]
        public void Cost_keeps_rising_the_whole_way_down_without_ever_becoming_a_wall()
        {
            ReachTheTail(730_005);
            RunCivilization(DeepAges * Tracks.Count * Ages.projectsPerTrack);

            foreach (EndlessResearchDef track in Tracks)
            {
                var byDepth = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .Where(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed) && p.tags!.Contains(track.trackTag))
                    .OrderBy(DepthOf)
                    .Select(p => p.baseCost)
                    .ToList();

                for (int i = 1; i < byDepth.Count; i++)
                {
                    Assert.True(byDepth[i] > byDepth[i - 1], $"{track.defName} stopped getting harder at depth {i + 1}.");
                }

                // Still finishable at the bottom: the deepest project of a 25-age run should be a large
                // multiple of the first and nowhere near the runaway a geometric curve produces (1.35^100 is
                // 10^13). The band, not a literal, per CLAUDE.md.
                float ratio = byDepth[byDepth.Count - 1] / byDepth[0];
                Assert.InRange(ratio, 10f, 2000f);
            }
        }

        [Fact]
        public void Two_civilizations_on_one_seed_do_not_have_to_spend_it_the_same_way()
        {
            // The tail has optional work in it, so what a civilization has done out there is a fact about the
            // civilization and not only about the seed. A frontier-racer and a completionist share a seed and
            // still end up somewhere visibly different.
            const int seed = 730_006;
            const int agesRaced = 6;
            int budget = agesRaced * Tracks.Count;

            ReachTheTail(seed);
            for (int age = 0; age < agesRaced; age++)
            {
                foreach (EndlessResearchDef track in Tracks)
                {
                    manager.FinishProject(EndlessResearchTests.Named(Ages, track, age, 0, seed));
                }
            }
            var racer = Finished(manager, seed);
            Assert.Equal(budget, racer.Count);

            var second = new ResearchManager();
            Find.ResearchManager = second;
            ReachTheTail(second, seed);
            for (int i = 0; i < budget; i++)
            {
                ResearchProjectDef next = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .Where(p => EndlessResearch.BelongsTo(p, seed) && p.CanStartNow)
                    .OrderBy(p => p.baseCost).ThenBy(p => p.defName, StringComparer.Ordinal).First();
                second.FinishProject(next);
            }
            var completionist = Finished(second, seed);

            Assert.Equal(racer.Count, completionist.Count);
            Assert.True(manager.EndlessAges > second.EndlessAges,
                "racing the frontier got no further than buying everything in order did.");
            double overlap = racer.Intersect(completionist).Count() / (double)racer.Union(completionist).Count();
            Assert.True(overlap < 0.6, $"two playstyles finished {overlap:P0} of the same projects out past the ladder.");
        }

        private static HashSet<ResearchProjectDef> Finished(ResearchManager target, int seed) =>
            new HashSet<ResearchProjectDef>(DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => EndlessResearch.BelongsTo(p, seed) && target.IsFinished(p)));

        private static int DepthOf(ResearchProjectDef project)
        {
            string name = project.defName;
            int lastUnderscore = name.LastIndexOf('_');
            return int.Parse(name.Substring(lastUnderscore + 1), CultureInfo.InvariantCulture);
        }
    }
}
