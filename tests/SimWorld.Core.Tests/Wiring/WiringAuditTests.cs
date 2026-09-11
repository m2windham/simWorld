using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Wiring
{
    /// <summary>
    /// The wiring audit, run against the real core, plus the tests that keep the audit itself honest.
    ///
    /// <para/>Read <see cref="WiringAudit"/> first for what each check looks for and why. This file is the
    /// two things a check of this kind needs to be trusted: a comparison against the reviewed baseline, and
    /// proof that the checks still fire — because the failure mode of every "nothing is unused" test ever
    /// written is that it quietly stops looking and passes forever.
    /// </summary>
    [Collection("GlobalDefs")]
    public class WiringAuditTests : ContentTestBase
    {
        public WiringAuditTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>The shipped content only — see <see cref="CoreUnderAudit.ShippedDefs"/> for why that matters.</summary>
        private static IReadOnlyList<Def> AllDefs => CoreUnderAudit.ShippedDefs(DefDatabase.Global);

        // ---------------------------------------------------------------------------------------------
        // The audit itself.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The test this whole file exists for. Every seam the audit finds must be in the baseline, with the
        /// reason it is dormant beside it; anything else is a connection someone just broke, or forgot to
        /// make, and the failure message is the paste-ready line to add once it is genuinely deliberate.
        /// </summary>
        [Fact]
        public void No_seam_in_the_core_has_gone_dormant_unnoticed()
        {
            DormantSeamBaseline baseline = DormantSeamBaseline.Load(CoreUnderAudit.BaselinePath);
            IReadOnlyList<Seam> seams = WiringAudit.Run(CoreUnderAudit.Types, CoreUnderAudit.Source, AllDefs);

            List<Seam> unexpected = seams.Where(s => !baseline.Contains(s.Key)).ToList();
            Assert.True(unexpected.Count == 0, BuildNewSeamMessage(unexpected, baseline.Path));
        }

        /// <summary>
        /// The other half, and the half that makes the baseline shrink instead of rot: once a seam is wired
        /// up, its line has to go. Without this the file keeps entries that stopped being true, and a reader
        /// can no longer tell which of them still describe the code.
        /// </summary>
        [Fact]
        public void The_baseline_lists_nothing_that_is_wired_up_again()
        {
            DormantSeamBaseline baseline = DormantSeamBaseline.Load(CoreUnderAudit.BaselinePath);
            var found = new HashSet<string>(
                WiringAudit.Run(CoreUnderAudit.Types, CoreUnderAudit.Source, AllDefs).Select(s => s.Key),
                StringComparer.Ordinal);

            List<string> stale = baseline.Keys.Where(k => !found.Contains(k)).ToList();
            Assert.True(stale.Count == 0,
                "These baseline entries are no longer dormant — something now reaches them. Delete the lines "
                + "from " + baseline.Path + ", which is a good day:" + Environment.NewLine
                + string.Join(Environment.NewLine, stale.Select(k => "  " + k)));
        }

        /// <summary>
        /// A baseline of bare identifiers is a suppression list wearing a hat. Every line has to say why the
        /// seam is dormant — blocked on an unbuilt system, host-facing, content that has not caught up — so
        /// the next reader can tell an accepted gap from an accident nobody noticed.
        /// </summary>
        [Fact]
        public void Every_baseline_entry_says_why_it_is_dormant()
        {
            DormantSeamBaseline baseline = DormantSeamBaseline.Load(CoreUnderAudit.BaselinePath);

            Assert.True(baseline.Duplicates.Count == 0,
                "Listed twice in " + baseline.Path + ": " + string.Join(", ", baseline.Duplicates));
            Assert.True(baseline.Unexplained.Count == 0,
                "These baseline entries carry no reason (at least "
                + DormantSeamBaseline.MinimumReasonLength + " characters after the '|'):" + Environment.NewLine
                + string.Join(Environment.NewLine, baseline.Unexplained.Select(k => "  " + k)));
        }

        /// <summary>
        /// The vacuity guard. If the repository walk ever failed, or the assembly came back empty, every
        /// check would find nothing and this file would pass forever while looking exactly as green as it
        /// does today. So assert that the audit is holding the whole core in its hands before believing it.
        /// </summary>
        [Fact]
        public void The_audit_examined_the_whole_core()
        {
            Assert.True(CoreUnderAudit.Source.Files.Count > 400,
                "Only " + CoreUnderAudit.Source.Files.Count + " source files indexed from " + CoreUnderAudit.CoreSourceRoot);
            Assert.True(CoreUnderAudit.Source.TokenCount > 50000, "Source index looks empty: " + CoreUnderAudit.Source.TokenCount + " tokens");
            Assert.True(CoreUnderAudit.Types.Count > 500, "Only " + CoreUnderAudit.Types.Count + " types in the core assembly");
            Assert.True(CoreUnderAudit.Types.Count(t => t.IsInterface) > 10, "No interfaces found to check");
            Assert.True(AllDefs.Count > 500, "Only " + AllDefs.Count + " defs loaded");

            ContentSurvey survey = ContentSurvey.Of(AllDefs);
            Assert.True(survey.Fields.Count() > 400, "Content survey saw only " + survey.Fields.Count() + " fields");
            Assert.True(CoreUnderAudit.Types.Count(HasNotifyHook) > 20, "No Notify_ hooks found to check");
        }

        private static bool HasNotifyHook(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => m.Name.StartsWith("Notify_", StringComparison.Ordinal));

        private static string BuildNewSeamMessage(IReadOnlyList<Seam> unexpected, string baselinePath)
        {
            if (unexpected.Count == 0) return string.Empty;
            return unexpected.Count + " seam(s) in src/SimWorld.Core are reached by nothing and are not in the "
                + "reviewed baseline." + Environment.NewLine
                + "Either wire them up, or — if the gap is deliberate — add these lines to " + baselinePath
                + " with the real reason in place of the placeholder:" + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, unexpected.Select(DormantSeamBaseline.TemplateLineFor));
        }

        // ---------------------------------------------------------------------------------------------
        // Proving the audit is not a no-op.
        //
        // Every check below runs twice over a miniature core built for the purpose: once with the seam left
        // dangling, once with it wired. A check that has silently stopped looking fails the first half; a
        // check that reports everything fails the second.
        // ---------------------------------------------------------------------------------------------

        private static readonly IReadOnlyList<Type> FixtureTypes = new[]
        {
            typeof(IFixtureSampler), typeof(FixtureSampler), typeof(FixtureTracker),
            typeof(FixtureWorker), typeof(FixtureWorker_Placeholder), typeof(FixtureDef),
        };

        private static ContentSurvey SurveyOf(params Def[] defs) =>
            ContentSurvey.Of(defs, typeof(FixtureDef).Assembly);

        private static SourceIndex Source(string body) =>
            SourceIndex.FromTexts("Fixture/FixtureSystem.cs", body);

        [Fact]
        public void A_Notify_hook_nothing_raises_is_reported()
        {
            SourceIndex source = Source("class FixtureSystem { void Run(FixtureTracker t) { t.Tick(); } }");

            IReadOnlyList<Seam> seams = WiringAudit.NotifyHooksWithoutCaller(FixtureTypes, source);

            Assert.Contains(seams, s => s.Id.EndsWith(".Notify_FixtureThingHappened", StringComparison.Ordinal));
        }

        [Fact]
        public void A_Notify_hook_something_raises_is_not_reported()
        {
            SourceIndex source = Source("class FixtureSystem { void Run(FixtureTracker t) { t.Notify_FixtureThingHappened(); } }");

            IReadOnlyList<Seam> seams = WiringAudit.NotifyHooksWithoutCaller(FixtureTypes, source);

            Assert.DoesNotContain(seams, s => s.Id.EndsWith(".Notify_FixtureThingHappened", StringComparison.Ordinal));
        }

        /// <summary>
        /// The trap a plain grep falls into, and the reason this index parses instead of matching: in this
        /// codebase the seam that nothing calls is usually the one with a paragraph of documentation about
        /// what will call it one day.
        /// </summary>
        [Fact]
        public void A_hook_named_only_in_a_comment_or_a_string_still_counts_as_unraised()
        {
            SourceIndex source = Source(
                "class FixtureSystem {\n"
                + "    // TODO: call t.Notify_FixtureThingHappened() once the other module lands.\n"
                + "    /// <summary>See <see cref=\"FixtureTracker.Notify_FixtureThingHappened\"/>.</summary>\n"
                + "    void Run(FixtureTracker t) { Log(\"t.Notify_FixtureThingHappened()\"); }\n"
                + "}");

            IReadOnlyList<Seam> seams = WiringAudit.NotifyHooksWithoutCaller(FixtureTypes, source);

            Assert.Contains(seams, s => s.Id.EndsWith(".Notify_FixtureThingHappened", StringComparison.Ordinal));
        }

        [Fact]
        public void An_interface_whose_only_implementation_is_never_built_is_reported()
        {
            SourceIndex source = Source("class FixtureSystem { float Read(IFixtureSampler s) { return s.Sample(); } }");

            IReadOnlyList<Seam> seams =
                WiringAudit.InterfacesWithoutImplementation(FixtureTypes, source, SurveyOf());

            Assert.Contains(seams, s => s.Id == typeof(IFixtureSampler).FullName);
        }

        [Fact]
        public void An_interface_whose_implementation_is_built_is_not_reported()
        {
            SourceIndex source = Source("class FixtureSystem { IFixtureSampler Make() { return new FixtureSampler(); } }");

            IReadOnlyList<Seam> seams =
                WiringAudit.InterfacesWithoutImplementation(FixtureTypes, source, SurveyOf());

            Assert.DoesNotContain(seams, s => s.Id == typeof(IFixtureSampler).FullName);
        }

        [Fact]
        public void A_def_pointing_at_a_placeholder_worker_is_reported()
        {
            var def = new FixtureDef { defName = "Fixture", workerClass = typeof(FixtureWorker_Placeholder) };

            IReadOnlyList<Seam> seams = WiringAudit.PlaceholderWorkerClasses(new Def[] { def });

            Assert.Contains(seams, s => s.Id.Contains("FixtureWorker_Placeholder"));
        }

        [Fact]
        public void A_def_pointing_at_a_real_worker_is_not_reported()
        {
            var def = new FixtureDef { defName = "Fixture", workerClass = typeof(FixtureWorker) };

            IReadOnlyList<Seam> seams = WiringAudit.PlaceholderWorkerClasses(new Def[] { def });

            Assert.Empty(seams);
        }

        [Fact]
        public void A_field_the_code_reads_and_no_def_sets_is_reported()
        {
            SourceIndex source = Source("class FixtureSystem { bool Read(FixtureDef d) { return d.fixtureFlag; } }");
            ContentSurvey survey = SurveyOf(new FixtureDef { defName = "Fixture" });

            IReadOnlyList<Seam> seams = WiringAudit.ContentFieldsNoDefSets(survey, source, FixtureTypes);

            Assert.Contains(seams, s => s.Id.EndsWith(".fixtureFlag", StringComparison.Ordinal));
        }

        [Fact]
        public void A_field_some_def_sets_is_not_reported_as_content_silent()
        {
            SourceIndex source = Source("class FixtureSystem { bool Read(FixtureDef d) { return d.fixtureFlag; } }");
            ContentSurvey survey = SurveyOf(new FixtureDef { defName = "Fixture", fixtureFlag = true });

            IReadOnlyList<Seam> seams = WiringAudit.ContentFieldsNoDefSets(survey, source, FixtureTypes);

            Assert.DoesNotContain(seams, s => s.Id.EndsWith(".fixtureFlag", StringComparison.Ordinal));
        }

        [Fact]
        public void A_field_content_sets_and_no_code_reads_is_reported()
        {
            SourceIndex source = Source("class FixtureSystem { void Run(FixtureDef d) { } }");
            ContentSurvey survey = SurveyOf(new FixtureDef { defName = "Fixture", fixtureLabel = "set in content" });

            IReadOnlyList<Seam> seams = WiringAudit.ContentFieldsNoCodeReads(survey, source);

            Assert.Contains(seams, s => s.Id.EndsWith(".fixtureLabel", StringComparison.Ordinal));
        }

        [Fact]
        public void A_field_content_sets_and_code_reads_is_not_reported()
        {
            SourceIndex source = Source("class FixtureSystem { string? Run(FixtureDef d) { return d.fixtureLabel; } }");
            ContentSurvey survey = SurveyOf(new FixtureDef { defName = "Fixture", fixtureLabel = "set in content" });

            IReadOnlyList<Seam> seams = WiringAudit.ContentFieldsNoCodeReads(survey, source);

            Assert.DoesNotContain(seams, s => s.Id.EndsWith(".fixtureLabel", StringComparison.Ordinal));
        }

        [Fact]
        public void State_the_code_reads_and_never_writes_is_reported()
        {
            SourceIndex source = Source("class FixtureSystem { int Read(FixtureTracker t) { return t.fixtureTally; } }");

            IReadOnlyList<Seam> seams = WiringAudit.StateWithNoWriter(FixtureTypes, source, SurveyOf());

            Assert.Contains(seams, s => s.Id.EndsWith(".fixtureTally", StringComparison.Ordinal));
        }

        [Fact]
        public void State_something_writes_is_not_reported()
        {
            SourceIndex source = Source(
                "class FixtureSystem { int Read(FixtureTracker t) { t.fixtureTally = 1; return t.fixtureTally; } }");

            IReadOnlyList<Seam> seams = WiringAudit.StateWithNoWriter(FixtureTypes, source, SurveyOf());

            Assert.DoesNotContain(seams, s => s.Id.EndsWith(".fixtureTally", StringComparison.Ordinal));
        }

        /// <summary>
        /// Scribe writes a field by passing it as <c>ref</c>, never by assigning it, so a save-loaded field
        /// looks unwritten to anything that only looks for <c>=</c>. Getting this wrong would report most of
        /// the simulation's state as dormant, which is how a check like this loses its audience in one run.
        /// </summary>
        [Fact]
        public void State_written_only_through_a_ref_argument_is_not_reported()
        {
            SourceIndex source = Source(
                "class FixtureSystem { void Save(FixtureTracker t) { Scribe_Values.Look(ref t.fixtureTally, \"tally\"); }\n"
                + "    int Read(FixtureTracker t) { return t.fixtureTally; } }");

            IReadOnlyList<Seam> seams = WiringAudit.StateWithNoWriter(FixtureTypes, source, SurveyOf());

            Assert.DoesNotContain(seams, s => s.Id.EndsWith(".fixtureTally", StringComparison.Ordinal));
        }
    }

    // -------------------------------------------------------------------------------------------------
    // A miniature core for the audit to look at: one of each seam the checks know how to find.
    // -------------------------------------------------------------------------------------------------

    /// <summary>Stands in for <c>IEnvironmentSampler</c>: a seam a system reads through.</summary>
    public interface IFixtureSampler
    {
        float Sample();
    }

    /// <summary>The only implementation, exactly as <c>MapEnvironmentSampler</c> is.</summary>
    public sealed class FixtureSampler : IFixtureSampler
    {
        public float Sample() => 1f;
    }

    /// <summary>Stands in for <c>Pawn_TierTracker</c>: an event hook and a counter it keeps.</summary>
    public sealed class FixtureTracker
    {
        public int fixtureTally;

        public void Notify_FixtureThingHappened() => fixtureTally++;

        public void Tick()
        {
        }
    }

    public class FixtureWorker
    {
    }

    /// <summary>Stands in for <c>IncidentWorker_Placeholder</c>.</summary>
    public class FixtureWorker_Placeholder : FixtureWorker
    {
    }

    /// <summary>Stands in for any Def with a worker class and a couple of content fields.</summary>
    public class FixtureDef : Def
    {
        public Type workerClass = typeof(FixtureWorker);
        public string? fixtureLabel;
        public bool fixtureFlag;
    }
}
