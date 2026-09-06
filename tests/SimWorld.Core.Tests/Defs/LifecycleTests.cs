using System.Linq;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class LifecycleTests
    {
        [Fact]
        public void Hooks_run_in_order_and_references_are_resolved_before_ResolveReferences()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef><defName>A</defName><other>B</other></TestDef>
  <TestDef><defName>B</defName></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            var a = (TestDef)result.Defs.Single(d => d.defName == "A");
            Assert.Equal(new[] { "PostLoad", "ResolveReferences", "ConfigErrors" }, a.calls);
            Assert.True(a.otherWasResolvedInResolveReferences);
        }

        [Fact]
        public void Config_errors_are_reported_with_the_def_name()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>Bad</defName><count>-1</count></TestDef></Defs>");

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Equal("Bad", error.DefName);
            Assert.Contains("count is negative", error.Message);
        }

        [Fact]
        public void ignoreConfigErrors_suppresses_them()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>Bad</defName><count>-1</count><ignoreConfigErrors>true</ignoreConfigErrors></TestDef></Defs>");
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void Missing_defName_is_a_config_error()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><count>1</count></TestDef></Defs>");
            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("defName", error.Message);
        }

        [Fact]
        public void ThrowOnError_raises_with_every_error_attached()
        {
            DefLoader loader = TestLoader.Create(configure: o => o.ThrowOnError = true)
                .AddXml("<Defs><TestDef><defName>A</defName><bogus>1</bogus><other>Nope</other></TestDef></Defs>");

            DefLoadException exception = Assert.Throws<DefLoadException>(() => loader.Load());
            Assert.Equal(2, exception.Errors.Count);
            Assert.Contains("2 error(s)", exception.Message);
        }

        [Fact]
        public void PostLoad_exceptions_become_errors()
        {
            DefLoadResult result = TestLoader.Load("<Defs><ThrowingDef><defName>T</defName></ThrowingDef></Defs>");
            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("boom", error.Message);
            Assert.Single(result.Defs);
        }

        [Fact]
        public void Later_pack_overrides_earlier_by_defName_under_LastWins()
        {
            DefLoadResult result = TestLoader.Create()
                .AddXml("<Defs><TestDef><defName>A</defName><count>1</count></TestDef></Defs>", "core.xml", "Core")
                .AddXml("<Defs><TestDef><defName>A</defName><count>2</count></TestDef></Defs>", "mod.xml", "Mod")
                .Load();

            Assert.Empty(result.Errors);
            TestDef a = TestLoader.Single<TestDef>(result);
            Assert.Equal(2, a.count);
            Assert.Equal("Mod", a.packName);
        }

        [Fact]
        public void Duplicate_policy_Error_reports_and_keeps_the_first()
        {
            DefLoadResult result = TestLoader.Create(configure: o => o.DuplicatePolicy = DuplicateDefPolicy.Error)
                .AddXml("<Defs><TestDef><defName>A</defName><count>1</count></TestDef><TestDef><defName>A</defName><count>2</count></TestDef></Defs>")
                .Load();

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("Duplicate", error.Message);
            Assert.Equal(1, TestLoader.Single<TestDef>(result).count);
        }

        [Fact]
        public void Queued_nodes_are_consumed_by_Load()
        {
            DefLoader loader = TestLoader.Create().AddXml("<Defs><TestDef><defName>A</defName></TestDef></Defs>");
            Assert.Equal(1, loader.PendingCount);
            loader.Load();
            Assert.Equal(0, loader.PendingCount);
            Assert.Empty(loader.Load().Defs);
        }
    }
}
