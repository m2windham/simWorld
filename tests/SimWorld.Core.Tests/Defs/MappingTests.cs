using System.Globalization;
using System.Linq;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class MappingTests
    {
        [Fact]
        public void Loads_primitives_strings_and_enums()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef>
    <defName>A</defName>
    <label>alpha thing</label>
    <count>3</count>
    <weight>1.5</weight>
    <flag>True</flag>
    <text>  hello  </text>
    <kind>Beta</kind>
    <flags>A, B</flags>
    <optional>7</optional>
  </TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal("A", def.defName);
            Assert.Equal("Alpha thing", def.LabelCap);
            Assert.Equal(3, def.count);
            Assert.Equal(1.5f, def.weight);
            Assert.True(def.flag);
            Assert.Equal("hello", def.text);
            Assert.Equal(TestKind.Beta, def.kind);
            Assert.Equal(TestFlags.A | TestFlags.B, def.flags);
            Assert.Equal(7, def.optional);
        }

        [Fact]
        public void Lists_load_from_li_children()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef>
    <defName>A</defName>
    <tags><li>x</li><li>y</li></tags>
    <nums><li>1</li><li>2</li><li>3</li></nums>
  </TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal(new[] { "x", "y" }, def.tags);
            Assert.Equal(new[] { 1, 2, 3 }, def.nums);
        }

        [Fact]
        public void Nested_objects_and_lists_of_objects()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef>
    <defName>A</defName>
    <nested><a>1</a><b>one</b><list><li>p</li></list><inner><a>2</a></inner></nested>
    <nestedList>
      <li><a>10</a></li>
      <li><a>20</a><b>twenty</b></li>
    </nestedList>
  </TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.NotNull(def.nested);
            Assert.Equal(1, def.nested!.a);
            Assert.Equal("one", def.nested.b);
            Assert.Equal(new[] { "p" }, def.nested.list);
            Assert.Equal(2, def.nested.inner!.a);
            Assert.Equal(2, def.nestedList!.Count);
            Assert.Equal(20, def.nestedList[1].a);
            Assert.Equal("twenty", def.nestedList[1].b);
        }

        [Fact]
        public void Dictionaries_load_from_key_value_entries()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef>
    <defName>A</defName>
    <dict>
      <li><key>one</key><value>1</value></li>
      <li><key>two</key><value>2</value></li>
    </dict>
  </TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal(2, def.dict!.Count);
            Assert.Equal(1, def.dict["one"]);
            Assert.Equal(2, def.dict["two"]);
        }

        [Fact]
        public void Ranges_and_curves_parse_from_rimworld_notation()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef>
    <defName>A</defName>
    <range>1~3</range>
    <frange>0.5~2</frange>
    <curve>
      <points>
        <li>(10, 1)</li>
        <li>(0, 0)</li>
      </points>
    </curve>
  </TestDef>
</Defs>");

            Assert.Empty(result.Errors);
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal(new IntRange(1, 3), def.range);
            Assert.Equal(new FloatRange(0.5f, 2f), def.frange);
            Assert.Equal(0.5f, def.curve!.Evaluate(5f));
            Assert.Equal(0f, def.curve.Evaluate(-1f));
            Assert.Equal(1f, def.curve.Evaluate(50f));
        }

        [Fact]
        public void Single_number_makes_a_degenerate_range()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName><range>4</range></TestDef></Defs>");
            Assert.Empty(result.Errors);
            Assert.Equal(new IntRange(4, 4), TestLoader.Single<TestDef>(result).range);
        }

        [Fact]
        public void Type_fields_resolve_by_short_or_full_name()
        {
            DefLoadResult result = TestLoader.Create()
                .AddXml("<Defs><TestDef><defName>A</defName><klass>TestCompMarker</klass></TestDef></Defs>")
                .AddXml("<Defs><TestDef><defName>B</defName><klass>SimWorld.Tests.Defs.TestCompMarker</klass></TestDef></Defs>")
                .Load();

            Assert.Empty(result.Errors);
            Assert.All(result.Defs, d => Assert.Equal(typeof(TestCompMarker), ((TestDef)d).klass));
        }

        [Fact]
        public void Unknown_element_is_reported_and_the_rest_still_loads()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDef><defName>A</defName><bogus>1</bogus><count>2</count></TestDef>
</Defs>");

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("bogus", error.Message);
            Assert.Equal("A", error.DefName);
            Assert.Equal(2, TestLoader.Single<TestDef>(result).count);
        }

        [Fact]
        public void Unknown_def_type_is_an_error()
        {
            DefLoadResult result = TestLoader.Load("<Defs><NoSuchDef><defName>A</defName></NoSuchDef></Defs>");
            Assert.Empty(result.Defs);
            Assert.Contains(result.Errors, e => e.Message.Contains("NoSuchDef"));
        }

        [Fact]
        public void Bad_value_is_an_error_and_keeps_the_default()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName><count>abc</count></TestDef></Defs>");
            Assert.Contains(result.Errors, e => e.Message.Contains("abc"));
            Assert.Equal(0, TestLoader.Single<TestDef>(result).count);
        }

        [Fact]
        public void Floats_parse_invariantly_under_a_comma_decimal_culture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName><weight>1.5</weight><frange>0.25~0.75</frange></TestDef></Defs>");
                Assert.Empty(result.Errors);
                TestDef def = TestLoader.Single<TestDef>(result);
                Assert.Equal(1.5f, def.weight);
                Assert.Equal(new FloatRange(0.25f, 0.75f), def.frange);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void LoadAlias_accepts_the_old_element_name()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName><oldCount>5</oldCount></TestDef></Defs>");
            Assert.Empty(result.Errors);
            Assert.Equal(5, TestLoader.Single<TestDef>(result).renamed);
        }

        [Fact]
        public void Unsaved_fields_cannot_be_loaded_from_xml()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName><runtimeOnly>1</runtimeOnly></TestDef></Defs>");
            Assert.Contains(result.Errors, e => e.Message.Contains("runtimeOnly"));
            Assert.Equal(0, TestLoader.Single<TestDef>(result).runtimeOnly);
        }

        [Fact]
        public void Custom_load_hook_replaces_field_mapping()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <TestDefWithCustom>
    <defName>A</defName>
    <custom>xyz</custom>
    <customs><li>one</li><li>two</li></customs>
  </TestDefWithCustom>
</Defs>");

            Assert.Empty(result.Errors);
            var def = TestLoader.Single<TestDefWithCustom>(result);
            Assert.Equal("custom:xyz", def.custom!.raw);
            Assert.Equal(new[] { "custom:one", "custom:two" }, def.customs!.Select(c => c.raw));
        }

        [Fact]
        public void Records_source_file_and_pack()
        {
            DefLoadResult result = TestLoader.Load("<Defs><TestDef><defName>A</defName></TestDef></Defs>", "Things/Test.xml", "MyMod");
            TestDef def = TestLoader.Single<TestDef>(result);
            Assert.Equal("Things/Test.xml", def.fileName);
            Assert.Equal("MyMod", def.packName);
        }

        [Fact]
        public void Malformed_xml_and_wrong_root_are_errors_not_exceptions()
        {
            DefLoadResult result = TestLoader.Create()
                .AddXml("<Defs><TestDef>", "broken.xml")
                .AddXml("<Things><TestDef><defName>A</defName></TestDef></Things>", "wrongroot.xml")
                .Load();

            Assert.Empty(result.Defs);
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.File == "broken.xml" && e.Message.Contains("parse"));
            Assert.Contains(result.Errors, e => e.File == "wrongroot.xml" && e.Message.Contains("<Defs>"));
        }
    }
}
