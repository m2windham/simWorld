using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimWorld.Content;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    /// <summary>
    /// The two halves of the modding layer: XPath patch operations that edit content a pack does not own,
    /// and content packs ordered by their declared dependencies.
    /// </summary>
    public class PatchOperationTests
    {
        private const string BaseDefs = @"<Defs>
  <TestDef>
    <defName>Alpha</defName>
    <count>1</count>
    <text>original</text>
    <tags><li>one</li><li>two</li></tags>
  </TestDef>
  <TestDef>
    <defName>Beta</defName>
    <count>2</count>
  </TestDef>
</Defs>";

        private static DefLoadResult LoadPatched(string patchXml, string defs = BaseDefs, params string[] packIdentifiers)
        {
            DefLoader loader = TestLoader.Create().AddXml(defs, "defs.xml", "Core");
            loader.PackIdentifiers.AddRange(packIdentifiers);
            return loader.AddPatchXml(patchXml, "patch.xml", "TestMod").Load();
        }

        private static TestDef Get(DefLoadResult result, string defName) =>
            (TestDef)result.Defs.First(d => d.defName == defName);

        // ---- the operations ----

        [Fact]
        public void Replace_swaps_a_value_in_content_the_patch_does_not_own()
        {
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationReplace"">
    <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
    <value><text>patched</text></value>
  </Operation>
</Patch>");

            Assert.Empty(result.Errors);
            Assert.Equal("patched", Get(result, "Alpha").text);
            Assert.Equal(1, Get(result, "Alpha").count);
        }

        [Fact]
        public void Add_appends_or_prepends_inside_the_matched_node()
        {
            DefLoadResult appended = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationAdd"">
    <xpath>/Defs/TestDef[defName=""Alpha""]/tags</xpath>
    <value><li>three</li></value>
  </Operation>
</Patch>");
            Assert.Equal(new[] { "one", "two", "three" }, Get(appended, "Alpha").tags);

            DefLoadResult prepended = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationAdd"">
    <xpath>/Defs/TestDef[defName=""Alpha""]/tags</xpath>
    <value><li>zero</li></value>
    <order>Prepend</order>
  </Operation>
</Patch>");
            Assert.Equal(new[] { "zero", "one", "two" }, Get(prepended, "Alpha").tags);
        }

        [Fact]
        public void Insert_places_content_beside_the_match_not_inside_it()
        {
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationInsert"">
    <xpath>/Defs/TestDef[defName=""Alpha""]/tags/li[1]</xpath>
    <value><li>before-one</li></value>
  </Operation>
</Patch>");

            Assert.Empty(result.Errors);
            Assert.Equal(new[] { "before-one", "one", "two" }, Get(result, "Alpha").tags);
        }

        [Fact]
        public void Remove_deletes_the_matched_node()
        {
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationRemove"">
    <xpath>/Defs/TestDef[defName=""Beta""]</xpath>
  </Operation>
</Patch>");

            Assert.Empty(result.Errors);
            Assert.Single(result.Defs);
            Assert.Equal("Alpha", result.Defs[0].defName);
        }

        [Fact]
        public void Attribute_operations_add_set_and_remove()
        {
            // Abstract="True" makes the node a template, so adding that attribute must take Beta out of the
            // loaded set entirely — an attribute patch that "worked" but changed nothing would still pass a
            // weaker assertion than this one.
            DefLoadResult added = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationAttributeAdd"">
    <xpath>/Defs/TestDef[defName=""Beta""]</xpath>
    <attribute>Abstract</attribute>
    <value>True</value>
  </Operation>
</Patch>");
            Assert.Empty(added.Errors);
            Assert.DoesNotContain(added.Defs, d => d.defName == "Beta");

            DefLoadResult set = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationSequence"">
    <operations>
      <li Class=""PatchOperationAttributeAdd"">
        <xpath>/Defs/TestDef[defName=""Beta""]</xpath>
        <attribute>Abstract</attribute>
        <value>False</value>
      </li>
      <li Class=""PatchOperationAttributeSet"">
        <xpath>/Defs/TestDef[defName=""Beta""]</xpath>
        <attribute>Abstract</attribute>
        <value>True</value>
      </li>
    </operations>
  </Operation>
</Patch>");
            Assert.Empty(set.Errors);
            Assert.DoesNotContain(set.Defs, d => d.defName == "Beta");

            DefLoadResult removed = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationSequence"">
    <operations>
      <li Class=""PatchOperationAttributeAdd"">
        <xpath>/Defs/TestDef[defName=""Beta""]</xpath>
        <attribute>Abstract</attribute>
        <value>True</value>
      </li>
      <li Class=""PatchOperationAttributeRemove"">
        <xpath>/Defs/TestDef[defName=""Beta""]</xpath>
        <attribute>Abstract</attribute>
      </li>
    </operations>
  </Operation>
</Patch>");
            Assert.Empty(removed.Errors);
            Assert.Contains(removed.Defs, d => d.defName == "Beta");
        }

        [Fact]
        public void SetName_renames_the_matched_node()
        {
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationSetName"">
    <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
    <name>label</name>
  </Operation>
</Patch>");

            Assert.Empty(result.Errors);
            Assert.Null(Get(result, "Alpha").text);
            Assert.Equal("original", Get(result, "Alpha").label);
        }

        // ---- composition and control flow ----

        [Fact]
        public void A_sequence_stops_at_its_first_failure()
        {
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationSequence"">
    <operations>
      <li Class=""PatchOperationReplace"">
        <xpath>/Defs/TestDef[defName=""Missing""]/text</xpath>
        <value><text>never</text></value>
      </li>
      <li Class=""PatchOperationReplace"">
        <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
        <value><text>should-not-happen</text></value>
      </li>
    </operations>
  </Operation>
</Patch>");

            // The sequence reports failure (one error), and the second operation never ran.
            Assert.Single(result.Errors);
            Assert.Equal("original", Get(result, "Alpha").text);
        }

        [Fact]
        public void A_conditional_runs_the_branch_its_test_selects()
        {
            DefLoadResult matched = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationConditional"">
    <xpath>/Defs/TestDef[defName=""Alpha""]</xpath>
    <match Class=""PatchOperationReplace"">
      <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
      <value><text>found</text></value>
    </match>
    <nomatch Class=""PatchOperationReplace"">
      <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
      <value><text>absent</text></value>
    </nomatch>
  </Operation>
</Patch>");
            Assert.Empty(matched.Errors);
            Assert.Equal("found", Get(matched, "Alpha").text);

            DefLoadResult unmatched = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationConditional"">
    <xpath>/Defs/TestDef[defName=""Nobody""]</xpath>
    <match Class=""PatchOperationReplace"">
      <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
      <value><text>found</text></value>
    </match>
    <nomatch Class=""PatchOperationReplace"">
      <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
      <value><text>absent</text></value>
    </nomatch>
  </Operation>
</Patch>");
            Assert.Empty(unmatched.Errors);
            Assert.Equal("absent", Get(unmatched, "Alpha").text);
        }

        [Fact]
        public void FindMod_branches_on_which_packs_are_loaded()
        {
            const string patch = @"<Patch>
  <Operation Class=""PatchOperationFindMod"">
    <mods><li>Some.Other.Pack</li></mods>
    <match Class=""PatchOperationReplace"">
      <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
      <value><text>present</text></value>
    </match>
    <nomatch Class=""PatchOperationReplace"">
      <xpath>/Defs/TestDef[defName=""Alpha""]/text</xpath>
      <value><text>missing</text></value>
    </nomatch>
  </Operation>
</Patch>";

            Assert.Equal("present", Get(LoadPatched(patch, BaseDefs, "Some.Other.Pack"), "Alpha").text);
            Assert.Equal("missing", Get(LoadPatched(patch, BaseDefs), "Alpha").text);
        }

        [Fact]
        public void A_patch_that_matches_nothing_is_an_error_unless_it_says_otherwise()
        {
            DefLoadResult reported = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationRemove"">
    <xpath>/Defs/TestDef[defName=""Nobody""]</xpath>
  </Operation>
</Patch>");
            Assert.Single(reported.Errors);

            DefLoadResult silent = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationRemove"">
    <xpath>/Defs/TestDef[defName=""Nobody""]</xpath>
    <success>Always</success>
  </Operation>
</Patch>");
            Assert.Empty(silent.Errors);
        }

        // ---- the two things that make patching worth having ----

        [Fact]
        public void Patches_run_before_inheritance_so_patching_a_parent_reaches_its_children()
        {
            // This is the ordering RimWorld uses and the reason for it: a patch edits the authored XML, not
            // the flattened result, so one edit to an abstract base changes everything derived from it.
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationReplace"">
    <xpath>/Defs/TestDef[@Name=""BaseThing""]/count</xpath>
    <value><count>99</count></value>
  </Operation>
</Patch>", @"<Defs>
  <TestDef Name=""BaseThing"" Abstract=""True"">
    <count>1</count>
  </TestDef>
  <TestDef ParentName=""BaseThing"">
    <defName>Child</defName>
  </TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            Assert.Equal(99, Get(result, "Child").count);
        }

        [Fact]
        public void A_def_a_patch_adds_is_loaded_and_credited_to_the_patch()
        {
            DefLoadResult result = LoadPatched(@"<Patch>
  <Operation Class=""PatchOperationAdd"">
    <xpath>/Defs</xpath>
    <value>
      <TestDef>
        <defName>FromPatch</defName>
        <count>7</count>
      </TestDef>
    </value>
  </Operation>
</Patch>");

            Assert.Empty(result.Errors);
            TestDef added = Get(result, "FromPatch");
            Assert.Equal(7, added.count);

            // Provenance: the new Def belongs to the pack whose patch wrote it, not to the pack whose file it
            // landed next to. Errors and "later pack wins" both read this.
            Assert.Equal("TestMod", added.packName);
            Assert.Equal("Core", Get(result, "Alpha").packName);
        }

        [Fact]
        public void Loading_without_any_patch_is_untouched()
        {
            DefLoadResult result = TestLoader.Load(BaseDefs);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Defs.Count);
            Assert.Equal("original", Get(result, "Alpha").text);
        }
    }

    /// <summary>Content packs: reading a pack from a folder, and ordering packs by what they declare.</summary>
    public class ModContentPackTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "simworld-packs-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            GC.SuppressFinalize(this);
        }

        private string MakePack(string folder, string? about = null, string? defs = null, string? patches = null)
        {
            string dir = Path.Combine(root, folder);
            Directory.CreateDirectory(dir);
            if (about != null)
            {
                Directory.CreateDirectory(Path.Combine(dir, ModMetaData.DirectoryName));
                File.WriteAllText(Path.Combine(dir, ModMetaData.DirectoryName, ModMetaData.FileName), about);
            }
            if (defs != null)
            {
                Directory.CreateDirectory(Path.Combine(dir, ModContentPack.DefsFolder));
                File.WriteAllText(Path.Combine(dir, ModContentPack.DefsFolder, "defs.xml"), defs);
            }
            if (patches != null)
            {
                Directory.CreateDirectory(Path.Combine(dir, ModContentPack.PatchesFolder));
                File.WriteAllText(Path.Combine(dir, ModContentPack.PatchesFolder, "patch.xml"), patches);
            }
            return dir;
        }

        private static ModContentPack Read(string directory, List<DefLoadError> errors) =>
            ModContentPack.FromDirectory(directory, new DefTypeResolver(), errors)!;

        [Fact]
        public void A_pack_reads_its_own_metadata()
        {
            var errors = new List<DefLoadError>();
            string dir = MakePack("SomeMod", about: @"<ModMetaData>
  <packageId>author.somemod</packageId>
  <name>Some Mod</name>
  <author>Somebody</author>
  <modDependencies><li>simworld.core</li></modDependencies>
  <loadAfter><li>other.pack</li></loadAfter>
</ModMetaData>");

            ModContentPack pack = Read(dir, errors);

            Assert.Empty(errors);
            Assert.Equal("author.somemod", pack.PackageId);
            Assert.Equal("Some Mod", pack.Name);
            Assert.Equal(new[] { "simworld.core" }, pack.MetaData.modDependencies);
            Assert.Equal(new[] { "other.pack" }, pack.MetaData.loadAfter);
        }

        [Fact]
        public void A_pack_with_no_metadata_is_still_a_pack()
        {
            var errors = new List<DefLoadError>();
            string dir = MakePack("BareMod", defs: "<Defs></Defs>");

            ModContentPack pack = Read(dir, errors);

            Assert.Empty(errors);
            Assert.Equal("BareMod", pack.Name);
            Assert.Equal("BareMod", pack.PackageId);
            Assert.True(pack.HasDefs);
            Assert.False(pack.HasPatches);
        }

        [Fact]
        public void Core_loads_first_whatever_order_the_folders_are_in()
        {
            var errors = new List<DefLoadError>();
            var packs = new List<ModContentPack>
            {
                Read(MakePack("ZMod", about: "<ModMetaData><packageId>z.mod</packageId><name>ZMod</name></ModMetaData>"), errors),
                Read(MakePack("Core", about: "<ModMetaData><packageId>simworld.core</packageId><name>Core</name></ModMetaData>"), errors),
            };

            List<ModContentPack> ordered = ModLoadOrder.Resolve(packs, errors);

            Assert.Empty(errors);
            Assert.Equal("Core", ordered[0].Name);
        }

        [Fact]
        public void loadAfter_and_loadBefore_both_order_a_pair()
        {
            var errors = new List<DefLoadError>();
            ModContentPack a = Read(MakePack("A", about: "<ModMetaData><packageId>a</packageId><name>A</name><loadAfter><li>b</li></loadAfter></ModMetaData>"), errors);
            ModContentPack b = Read(MakePack("B", about: "<ModMetaData><packageId>b</packageId><name>B</name></ModMetaData>"), errors);

            List<ModContentPack> byLoadAfter = ModLoadOrder.Resolve(new List<ModContentPack> { a, b }, errors);
            Assert.Empty(errors);
            Assert.Equal(new[] { "B", "A" }, byLoadAfter.Select(p => p.Name));

            ModContentPack c = Read(MakePack("C", about: "<ModMetaData><packageId>c</packageId><name>C</name><loadBefore><li>d</li></loadBefore></ModMetaData>"), errors);
            ModContentPack d = Read(MakePack("D", about: "<ModMetaData><packageId>d</packageId><name>D</name></ModMetaData>"), errors);

            List<ModContentPack> byLoadBefore = ModLoadOrder.Resolve(new List<ModContentPack> { d, c }, errors);
            Assert.Empty(errors);
            Assert.Equal(new[] { "C", "D" }, byLoadBefore.Select(p => p.Name));
        }

        [Fact]
        public void Unconstrained_packs_keep_their_declared_order()
        {
            // Determinism is a feature: the same folder set must load the same way every run, so packs with
            // nothing to say about each other must not be re-sorted by anything incidental.
            var errors = new List<DefLoadError>();
            var packs = new List<ModContentPack>
            {
                Read(MakePack("First", about: "<ModMetaData><packageId>first</packageId><name>First</name></ModMetaData>"), errors),
                Read(MakePack("Second", about: "<ModMetaData><packageId>second</packageId><name>Second</name></ModMetaData>"), errors),
                Read(MakePack("Third", about: "<ModMetaData><packageId>third</packageId><name>Third</name></ModMetaData>"), errors),
            };

            List<ModContentPack> ordered = ModLoadOrder.Resolve(packs, errors);

            Assert.Empty(errors);
            Assert.Equal(new[] { "First", "Second", "Third" }, ordered.Select(p => p.Name));
        }

        [Fact]
        public void A_missing_dependency_is_reported_but_does_not_stop_the_load()
        {
            var errors = new List<DefLoadError>();
            ModContentPack needy = Read(MakePack("Needy", about: "<ModMetaData><packageId>needy</packageId><name>Needy</name><modDependencies><li>nowhere.pack</li></modDependencies></ModMetaData>"), errors);

            List<ModContentPack> ordered = ModLoadOrder.Resolve(new List<ModContentPack> { needy }, errors);

            Assert.Single(errors);
            Assert.Contains("nowhere.pack", errors[0].Message);
            Assert.Single(ordered);
        }

        [Fact]
        public void A_cycle_is_reported_and_every_pack_still_loads()
        {
            var errors = new List<DefLoadError>();
            ModContentPack a = Read(MakePack("CycleA", about: "<ModMetaData><packageId>cyclea</packageId><name>CycleA</name><loadAfter><li>cycleb</li></loadAfter></ModMetaData>"), errors);
            ModContentPack b = Read(MakePack("CycleB", about: "<ModMetaData><packageId>cycleb</packageId><name>CycleB</name><loadAfter><li>cyclea</li></loadAfter></ModMetaData>"), errors);

            List<ModContentPack> ordered = ModLoadOrder.Resolve(new List<ModContentPack> { a, b }, errors);

            Assert.Single(errors);
            Assert.Contains("cycle", errors[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, ordered.Count);
        }

        [Fact]
        public void A_second_pack_can_patch_the_first_ones_content()
        {
            // End to end through the real discovery/order/patch path: two folders on disk, one editing the
            // other, with nothing but their About.xml files to say which comes first.
            MakePack("Core",
                about: "<ModMetaData><packageId>simworld.core</packageId><name>Core</name></ModMetaData>",
                defs: @"<Defs><TestDef><defName>Alpha</defName><count>1</count></TestDef></Defs>");
            MakePack("Tweak",
                about: "<ModMetaData><packageId>somebody.tweak</packageId><name>Tweak</name></ModMetaData>",
                patches: @"<Patch>
  <Operation Class=""PatchOperationReplace"">
    <xpath>/Defs/TestDef[defName=""Alpha""]/count</xpath>
    <value><count>42</count></value>
  </Operation>
</Patch>");

            var database = new DefDatabase();
            var types = new DefTypeResolver().AddAssembly(typeof(ModContentPackTests).Assembly);
            DefLoadResult result = CoreContent.LoadAllPacks(
                database, types, new DefLoadOptions { BindDefOfs = false }, root);

            Assert.Empty(result.Errors);
            var alpha = (TestDef)result.Defs.Single(d => d.defName == "Alpha");
            Assert.Equal(42, alpha.count);
        }
    }

    /// <summary>The shipped Core content, loaded the way a modded game would load it.</summary>
    [Collection("GlobalDefs")]
    public class CorePackTests
    {
        [Fact]
        public void Core_is_a_content_pack_like_any_other()
        {
            var errors = new List<DefLoadError>();
            List<ModContentPack> packs = CoreContent.DiscoverPacks(null, new DefTypeResolver(), errors);

            Assert.Empty(errors);
            ModContentPack core = Assert.Single(packs, p => p.IsCoreMod);
            Assert.Equal("simworld.core", core.PackageId);
            Assert.True(core.HasDefs);
        }

        [Fact]
        public void The_shipped_content_loads_through_the_pack_path_exactly_as_it_does_directly()
        {
            var direct = new DefDatabase();
            DefLoadResult straight = CoreContent.Load(direct, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = false });

            var viaPacks = new DefDatabase();
            DefLoadResult packed = CoreContent.LoadAllPacks(viaPacks, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = false });

            Assert.Empty(straight.Errors);
            Assert.Empty(packed.Errors);
            Assert.Equal(straight.Defs.Count, packed.Defs.Count);
        }
    }
}
