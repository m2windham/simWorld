using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class InheritanceTests
    {
        private const string Base = @"<TestDef Name=""Base"" Abstract=""True"">
    <count>1</count>
    <weight>2</weight>
    <tags><li>a</li></tags>
    <nested><a>1</a><b>x</b></nested>
  </TestDef>";

        [Fact]
        public void Child_inherits_parent_values_and_overrides_scalars()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + @"
  <TestDef ParentName=""Base""><defName>C</defName><weight>5</weight></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal("C", def.defName);
            Assert.Equal(1, def.count);
            Assert.Equal(5f, def.weight);
            Assert.Equal(new[] { "a" }, def.tags);
        }

        [Fact]
        public void Abstract_nodes_are_templates_only()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + "</Defs>");
            Assert.Empty(result.Errors);
            Assert.Empty(result.Defs);
        }

        [Fact]
        public void List_items_append_to_the_parents()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + @"
  <TestDef ParentName=""Base""><defName>C</defName><tags><li>b</li></tags></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            Assert.Equal(new[] { "a", "b" }, TestLoader.Single<TestDef>(result).tags);
        }

        [Fact]
        public void Inherit_false_replaces_the_parents_list()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + @"
  <TestDef ParentName=""Base""><defName>C</defName><tags Inherit=""False""><li>b</li></tags></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            Assert.Equal(new[] { "b" }, TestLoader.Single<TestDef>(result).tags);
        }

        [Fact]
        public void Nested_objects_merge_field_by_field()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + @"
  <TestDef ParentName=""Base""><defName>C</defName><nested><b>y</b></nested></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal(1, def.nested!.a);
            Assert.Equal("y", def.nested.b);
        }

        [Fact]
        public void IsNull_clears_an_inherited_value()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + @"
  <TestDef ParentName=""Base""><defName>C</defName><nested IsNull=""True"" /></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            Assert.Null(TestLoader.Single<TestDef>(result).nested);
        }

        [Fact]
        public void Chains_resolve_through_intermediate_parents()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Base + @"
  <TestDef Name=""Mid"" ParentName=""Base"" Abstract=""True""><flag>true</flag><tags><li>m</li></tags></TestDef>
  <TestDef ParentName=""Mid""><defName>Leaf</defName><tags><li>l</li></tags></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal(1, def.count);
            Assert.True(def.flag);
            Assert.Equal(new[] { "a", "m", "l" }, def.tags);
        }

        [Fact]
        public void Parent_may_be_defined_in_a_later_file()
        {
            DefLoadResult result = TestLoader.Create()
                .AddXml(@"<Defs><TestDef ParentName=""Base""><defName>C</defName></TestDef></Defs>", "child.xml")
                .AddXml("<Defs>" + Base + "</Defs>", "base.xml")
                .Load();

            Assert.Empty(result.Errors);
            Assert.Equal(1, TestLoader.Single<TestDef>(result).count);
        }

        [Fact]
        public void Missing_parent_is_an_error_but_the_def_still_loads()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef ParentName=""Nope""><defName>C</defName><count>9</count></TestDef>
</Defs>");

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("Nope", error.Message);
            Assert.Equal(9, TestLoader.Single<TestDef>(result).count);
        }

        [Fact]
        public void Inheritance_cycles_are_reported()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef Name=""A"" ParentName=""B""><defName>A</defName></TestDef>
  <TestDef Name=""B"" ParentName=""A""><defName>B</defName></TestDef>
</Defs>");

            Assert.Contains(result.Errors, e => e.Message.Contains("cycle"));
            Assert.Equal(2, result.Defs.Count);
        }
    }
}
