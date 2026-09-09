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
    /// Endless tech (<c>research.endless</c>): what a civilization that has finished all 232 authored
    /// projects keeps its researchers on. What is pinned here is that the tail exists, that it is derived
    /// rather than rolled (so a save stores one integer and reloads byte-identical tech), that it costs
    /// nothing until it is reached, and that it cannot disturb the authored era ladder it comes after.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EndlessResearchTests : ContentTestBase
    {
        public EndlessResearchTests(CoreContentFixture content) : base(content)
        {
        }

        private static IReadOnlyList<EndlessResearchDef> Tracks =>
            DefDatabase<EndlessResearchDef>.AllDefsListForReading;

        private static IEnumerable<ResearchProjectDef> Generated =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(EndlessResearch.IsGenerated);

        [Fact]
        public void Content_ships_endless_tracks_and_each_continues_a_tag_the_authored_tree_actually_uses()
        {
            Assert.NotEmpty(Tracks);

            var authoredTags = new HashSet<string>();
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (EndlessResearch.IsGenerated(project) || project.tags == null) continue;
                foreach (string tag in project.tags) authoredTags.Add(tag);
            }

            // A track that continues a tag nothing is tagged with would be a tail growing out of nothing.
            Assert.All(Tracks, t => Assert.Contains(t.trackTag, authoredTags));
            Assert.All(Tracks, t => Assert.Empty(t.ConfigErrors()));
        }

        [Fact]
        public void A_civilization_with_the_tree_unfinished_has_no_endless_tech_at_all()
        {
            Assert.Equal(0, Find.ResearchManager.EndlessTier);
            Assert.False(Find.ResearchManager.NothingLeftToResearch);
        }

        [Fact]
        public void Extending_mints_one_project_per_track_and_they_are_startable()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.ExtendEndlessTree();

            Assert.Equal(1, manager.EndlessTier);
            foreach (EndlessResearchDef track in Tracks)
            {
                ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 1));
                Assert.True(EndlessResearch.IsGenerated(project));
                Assert.True(project.CanStartNow, project.defName + " was minted but cannot be started");
                Assert.Contains(track.trackTag, project.tags!);
            }
        }

        [Fact]
        public void Each_tier_costs_more_than_the_one_before_and_follows_from_it()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.ExtendEndlessTree();
            manager.ExtendEndlessTree();
            manager.ExtendEndlessTree();

            EndlessResearchDef track = Tracks[0];
            ResearchProjectDef first = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 1));
            ResearchProjectDef second = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 2));
            ResearchProjectDef third = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 3));

            Assert.True(second.baseCost > first.baseCost);
            Assert.True(third.baseCost > second.baseCost);

            // A line, not a fan: tier n needs tier n-1, so how far a civilization got is one number.
            Assert.Null(first.prerequisites);
            Assert.Same(first, Assert.Single(second.prerequisites!));
            Assert.Same(second, Assert.Single(third.prerequisites!));
            Assert.False(second.CanStartNow, "tier 2 was startable without tier 1 finished");
        }

        [Fact]
        public void The_labels_do_not_read_as_one_word_with_a_number_after_it()
        {
            ResearchManager manager = Find.ResearchManager;
            for (int i = 0; i < 4; i++) manager.ExtendEndlessTree();

            EndlessResearchDef track = Tracks[0];
            var labels = new List<string>();
            for (int tier = 1; tier <= 4; tier++)
            {
                labels.Add(DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, tier)).label!);
            }

            Assert.Equal(labels.Count, labels.Distinct().Count());
            Assert.True(labels.Take(track.titles.Count).All(l => track.titles.Contains(l)),
                "the first pass through a track's titles should be the titles themselves, unnumbered");
        }

        [Fact]
        public void Minting_is_derived_not_rolled_so_the_same_tier_is_always_the_same_project()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.ExtendEndlessTree();
            EndlessResearchDef track = Tracks[0];
            ResearchProjectDef once = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 1));

            // Minting the same tier again returns what is already there rather than a second copy.
            ResearchProjectDef again = EndlessResearch.MintOrGet(track, 1, DefDatabase.Global);
            Assert.Same(once, again);
            Assert.Equal(EndlessResearch.CostFor(track, 1), once.baseCost, 3);
        }

        [Fact]
        public void Finishing_the_last_thing_there_is_to_research_produces_something_new_to_research()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.DebugSetAllProjectsFinished();

            // The authored tree is done, so the civilization has run out — which is the whole condition.
            Assert.True(manager.NothingLeftToResearch);

            manager.EnsureSomethingToResearch();
            Assert.False(manager.NothingLeftToResearch);
            Assert.True(manager.EndlessTier >= 1);
            Assert.NotEmpty(Generated);
        }

        [Fact]
        public void And_it_keeps_producing_one_however_many_times_the_tail_is_finished()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.DebugSetAllProjectsFinished();
            manager.EnsureSomethingToResearch();

            for (int round = 0; round < 5; round++)
            {
                ResearchProjectDef next = DefDatabase<ResearchProjectDef>.AllDefsListForReading.First(p => p.CanStartNow);
                manager.FinishProject(next);
                Assert.False(manager.NothingLeftToResearch, "the tail ran out after round " + round);
            }
        }

        [Fact]
        public void Endless_tech_belongs_to_no_era_and_cannot_hold_the_ladder_open()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.DebugSetAllProjectsFinished();
            EraDef? eraBefore = manager.CurrentEra;

            manager.ExtendEndlessTree();
            manager.ExtendEndlessTree();

            Assert.All(Generated, p => Assert.Null(p.era));
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading)
            {
                Assert.DoesNotContain(era.Projects, EndlessResearch.IsGenerated);
            }
            Assert.Same(eraBefore, manager.CurrentEra);
        }

        [Fact]
        public void A_save_carries_the_tail_as_one_number_and_reloads_the_same_tech()
        {
            ResearchManager manager = Find.ResearchManager;
            manager.DebugSetAllProjectsFinished();
            for (int i = 0; i < 3; i++) manager.ExtendEndlessTree();

            EndlessResearchDef track = Tracks[0];
            ResearchProjectDef tier2 = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 2));
            manager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 1)));
            manager.ResearchPerformed(0f, null);

            string xml = Scribe.SaveToString(manager, "research");
            Assert.Contains("endlessTier", xml);

            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", Content.Database);
            Assert.Equal(manager.EndlessTier, loaded.EndlessTier);

            // The projects the save referred to are back, by name, with the same costs — nothing about them
            // was written into the save beyond the tier count.
            ResearchProjectDef reloadedTier2 = DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 2));
            Assert.Same(tier2, reloadedTier2);
            Assert.Equal(EndlessResearch.CostFor(track, 2), reloadedTier2.baseCost, 3);
            Assert.True(loaded.IsFinished(DefDatabase<ResearchProjectDef>.GetNamed(EndlessResearch.DefNameFor(track, 1))));
        }
    }
}
