using System.Linq;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class CrossRefTests
    {
        [Fact]
        public void Field_references_resolve_across_files_and_forward()
        {
            DefLoadResult result = TestLoader.Create()
                .AddXml("<Defs><TestDef><defName>A</defName><other>B</other></TestDef></Defs>", "a.xml")
                .AddXml("<Defs><TestDef><defName>B</defName></TestDef></Defs>", "b.xml")
                .Load();

            Assert.Empty(result.Errors);
            var a = (TestDef)result.Defs.Single(d => d.defName == "A");
            var b = (TestDef)result.Defs.Single(d => d.defName == "B");
            Assert.Same(b, a.other);
        }

        [Fact]
        public void List_references_keep_their_order()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef><defName>A</defName><others><li>B</li><li>A</li></others></TestDef>
  <TestDef><defName>B</defName></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            var a = (TestDef)result.Defs.Single(d => d.defName == "A");
            Assert.Equal(new[] { "B", "A" }, a.others!.Select(d => d.defName));
            Assert.Same(a, a.others![1]);
        }

        [Fact]
        public void Dictionary_values_may_be_references()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef><defName>A</defName><defDict><li><key>k</key><value>B</value></li></defDict></TestDef>
  <TestDef><defName>B</defName></TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            var a = (TestDef)result.Defs.Single(d => d.defName == "A");
            Assert.Equal("B", a.defDict!["k"].defName);
        }

        [Fact]
        public void Unresolved_reference_is_an_error_and_leaves_null()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName><other>Nope</other></TestDef></Defs>");

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("Nope", error.Message);
            Assert.Contains("TestDef", error.Message);
            Assert.Null(TestLoader.Single<TestDef>(result).other);
        }

        [Fact]
        public void Subclass_defs_are_found_through_a_base_typed_field()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef><defName>A</defName><other>X</other></TestDef>
  <ChildTestDef><defName>X</defName><extra>e</extra></ChildTestDef>
</Defs>");

            Assert.Empty(result.Errors);
            var a = (TestDef)result.Defs.Single(d => d.defName == "A");
            Assert.IsType<ChildTestDef>(a.other);
        }

        [Fact]
        public void References_only_match_the_fields_def_type()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef><defName>A</defName><other>T</other></TestDef>
  <ThingDef><defName>T</defName></ThingDef>
</Defs>");

            Assert.Contains(result.Errors, e => e.Message.Contains("no TestDef named 'T'"));
        }
    }
}
