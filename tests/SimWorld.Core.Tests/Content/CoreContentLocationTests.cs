using System;
using System.IO;

using SimWorld.Content;
using SimWorld.Defs;

using Xunit;

namespace SimWorld.Tests.Content
{
    /// <summary>
    /// How the core finds its own content, and the one arrangement where it used to fail silently.
    ///
    /// <para/>A Unity host consuming this as a local UPM package by relative path keeps the content in a
    /// sibling repository, outside the host project entirely. Unity compiles the package into the host's
    /// <c>Library/ScriptAssemblies/</c>, so every probe that starts at the assembly and walks up stays inside
    /// the host project and never reaches it. The load then succeeded with zero defs — which a host cannot
    /// tell apart from a civilization that has not started, and did not, until one reported "zero edicts"
    /// for content that ships five.
    ///
    /// <para/>These tests build that exact directory shape in a temp folder rather than describing it, and
    /// deliberately touch no global state: no environment variable, no <see cref="CoreContent.ResetCache"/>,
    /// so they are safe beside the shared content fixture.
    /// </summary>
    public sealed class CoreContentLocationTests : IDisposable
    {
        private readonly string root;

        public CoreContentLocationTests()
        {
            root = Path.Combine(Path.GetTempPath(), "simworld-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A temp folder that will not delete is not worth failing a test over.
            }
        }

        /// <summary>Builds the host's real layout: a package repository and a Unity project side by side under
        /// one parent, the project's manifest naming the package by a relative path.</summary>
        private string BuildSiblingLayout(string manifestPathValue, bool withContent = true)
        {
            string package = Path.Combine(root, "simWorld", "src", "SimWorld.Core");
            if (withContent)
            {
                string defs = Path.Combine(package, "Data", CoreContent.PackName, "Defs");
                Directory.CreateDirectory(defs);
                File.WriteAllText(Path.Combine(defs, "ThingDefs_Test.xml"), "<Defs />");
            }
            else
            {
                Directory.CreateDirectory(package);
            }

            string project = Path.Combine(root, "simWorld.Host");
            Directory.CreateDirectory(Path.Combine(project, "Packages"));
            File.WriteAllText(
                Path.Combine(project, "Packages", "manifest.json"),
                "{\n  \"dependencies\": {\n    \"com.unity.ide.rider\": \"3.0.28\",\n"
                + "    \"com.simworld.core\": \"" + manifestPathValue + "\"\n  }\n}\n");

            string scriptAssemblies = Path.Combine(project, "Library", "ScriptAssemblies");
            Directory.CreateDirectory(scriptAssemblies);
            return scriptAssemblies;
        }

        [Fact]
        public void FindsContentInASiblingRepositoryThroughTheUnityManifest()
        {
            string assemblyDir = BuildSiblingLayout("file:../../simWorld/src/SimWorld.Core");

            string? data = CoreContent.TryResolveFromUnityPackageManifest(assemblyDir, out string? manifest);

            Assert.NotNull(manifest);
            Assert.NotNull(data);
            // The point of the whole exercise: the resolved directory is outside the Unity project.
            Assert.False(data!.StartsWith(Path.Combine(root, "simWorld.Host"), StringComparison.Ordinal));
            Assert.True(Directory.Exists(Path.Combine(data, CoreContent.PackName, "Defs")));
        }

        [Fact]
        public void ResolvesRelativeToThePackagesFolderNotTheProjectRoot()
        {
            // Unity's own rule. Resolved against the project root instead, "../../simWorld" would land one
            // directory too high and find nothing — so a pass here is the rule, not a coincidence.
            string assemblyDir = BuildSiblingLayout("file:../../simWorld/src/SimWorld.Core");

            string? data = CoreContent.TryResolveFromUnityPackageManifest(assemblyDir, out _);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "simWorld", "src", "SimWorld.Core", "Data")),
                Path.GetFullPath(data!));
        }

        [Fact]
        public void AnAbsoluteManifestPathAlsoResolves()
        {
            string absolute = Path.Combine(root, "simWorld", "src", "SimWorld.Core").Replace('\\', '/');
            string assemblyDir = BuildSiblingLayout("file:" + absolute);

            Assert.NotNull(CoreContent.TryResolveFromUnityPackageManifest(assemblyDir, out _));
        }

        [Fact]
        public void AManifestThatNamesNoPackageCarryingContentIsDistinctFromNoManifestAtAll()
        {
            // The distinction the out parameter exists for: "this is not a Unity project" and "this project's
            // manifest does not point at the content" are different problems with different fixes, and a host
            // printing one when it means the other sends someone looking in the wrong place.
            string assemblyDir = BuildSiblingLayout("file:../../simWorld/src/SimWorld.Core", withContent: false);

            string? data = CoreContent.TryResolveFromUnityPackageManifest(assemblyDir, out string? manifest);

            Assert.Null(data);
            Assert.NotNull(manifest);
        }

        [Fact]
        public void NoUnityProjectAboveMeansNoManifestAndNoData()
        {
            string lonely = Path.Combine(root, "not-a-unity-project", "deep");
            Directory.CreateDirectory(lonely);

            Assert.Null(CoreContent.TryResolveFromUnityPackageManifest(lonely, out string? manifest));
            Assert.Null(manifest);
        }

        [Fact]
        public void AVersionedRegistryDependencyIsIgnoredRatherThanMisread()
        {
            // Every manifest is full of ordinary "com.unity.x": "1.2.3" entries. Only file: values are paths.
            string project = Path.Combine(root, "registry-only");
            Directory.CreateDirectory(Path.Combine(project, "Packages"));
            File.WriteAllText(
                Path.Combine(project, "Packages", "manifest.json"),
                "{\"dependencies\":{\"com.unity.burst\":\"1.8.4\",\"com.unity.ide.vscode\":\"1.2.5\"}}");

            Assert.Null(CoreContent.TryResolveFromUnityPackageManifest(project, out string? manifest));
            Assert.NotNull(manifest);
        }

        [Fact]
        public void AnExplicitDataDirectoryBypassesTheSearchEntirely()
        {
            string data = Path.Combine(root, "explicit", "Data");
            string defs = Path.Combine(data, CoreContent.PackName, "Defs");
            Directory.CreateDirectory(defs);
            File.WriteAllText(Path.Combine(defs, "Empty.xml"), "<Defs />");

            var database = new DefDatabase();
            // DefOf binding off: this pack deliberately ships nothing, and every [DefOf] field would report
            // itself missing. That the binder says so is correct — it is what the content test relies on.
            DefLoadResult result = CoreContent.Load(
                database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = false }, data);

            Assert.Empty(result.Errors);
            // The assertion that matters: it loaded from where it was told, not from the repository the test
            // process is running inside — which does ship content and would have produced hundreds of defs.
            Assert.Equal(0, database.DefCount);
        }

        [Fact]
        public void ADirectoryWithNoDefFilesIsAnErrorRatherThanAnEmptyLoad()
        {
            // The silent mode, made loud. A zero-def load reads exactly like a game that has not started.
            string data = Path.Combine(root, "hollow", "Data");
            Directory.CreateDirectory(Path.Combine(data, CoreContent.PackName, "Defs"));

            var loader = new DefLoader(new DefDatabase(), new DefTypeResolver(), new DefLoadOptions());
            Exception thrown = Assert.ThrowsAny<Exception>(() => CoreContent.AddCoreDefs(loader, data));

            Assert.Contains("no Def files", thrown.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TheSearchReportNamesEveryPlaceItLooked()
        {
            // This string is what a host prints when content is missing; a report that omitted a probe would
            // send someone looking in the wrong place.
            string report = CoreContent.DescribeSearch();

            Assert.Contains(CoreContent.DataEnvironmentVariable, report, StringComparison.Ordinal);
            Assert.Contains("AppContext.BaseDirectory", report, StringComparison.Ordinal);
            Assert.Contains("beside the assembly", report, StringComparison.Ordinal);
            Assert.Contains("manifest.json", report, StringComparison.Ordinal);
            Assert.Contains("src/SimWorld.Core/Data", report, StringComparison.Ordinal);
        }

        [Fact]
        public void TheShippedContentIsStillFoundWithoutAnyOfThis()
        {
            // The regression guard for the change itself: the repository walk that every test run depends on
            // must keep working, and must find content that actually has files in it.
            Assert.NotNull(CoreContent.DataDirectory);
            Assert.True(Directory.Exists(CoreContent.DefsDirectory!));
            Assert.NotEmpty(Directory.GetFiles(CoreContent.DefsDirectory!, "*.xml", SearchOption.AllDirectories));
        }
    }
}
