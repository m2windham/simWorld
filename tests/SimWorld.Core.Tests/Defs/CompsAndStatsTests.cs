using System.Linq;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class CompsAndStatsTests
    {
        private const string Stats = @"
  <StatDef><defName>MaxHitPoints</defName><defaultBaseValue>100</defaultBaseValue></StatDef>
  <StatDef><defName>Beauty</defName><defaultBaseValue>1</defaultBaseValue></StatDef>";

        [Fact]
        public void Comps_use_Class_for_polymorphic_entries()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <ThingDef>
    <defName>Panel</defName>
    <thingClass>TestCompMarker</thingClass>
    <category>Building</category>
    <comps>
      <li Class=""TestCompProperties""><power>5</power></li>
    </comps>
  </ThingDef>
</Defs>");

            Assert.Empty(result.Errors);
            var def = TestLoader.Single<ThingDef>(result);
            Assert.Equal(typeof(TestCompMarker), def.thingClass);
            Assert.Equal(ThingCategory.Building, def.category);
            Assert.Equal(5, def.GetCompProperties<TestCompProperties>()!.power);
            Assert.True(def.HasComp(typeof(TestCompMarker)));
            Assert.False(def.HasComp(typeof(string)));
        }

        [Fact]
        public void Comp_without_a_compClass_is_a_config_error()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <ThingDef><defName>P</defName><comps><li Class=""CompProperties"" /></comps></ThingDef>
</Defs>");

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("no compClass", error.Message);
            Assert.Equal("P", error.DefName);
        }

        [Fact]
        public void Unknown_or_incompatible_Class_is_an_error()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <ThingDef><defName>P</defName><comps><li Class=""NoSuchComp"" /><li Class=""TestDef"" /></comps></ThingDef>
</Defs>");

            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.Message.Contains("NoSuchComp"));
            Assert.Contains(result.Errors, e => e.Message.Contains("not a CompProperties"));
            Assert.Empty(TestLoader.Single<ThingDef>(result).comps!);
        }

        [Fact]
        public void StatBases_use_the_element_name_as_the_stat()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Stats + @"
  <ThingDef><defName>Wall</defName><statBases><MaxHitPoints>250</MaxHitPoints></statBases></ThingDef>
</Defs>");

            Assert.Empty(result.Errors);
            var wall = (ThingDef)result.Defs.Single(d => d.defName == "Wall");
            var maxHp = (StatDef)result.Defs.Single(d => d.defName == "MaxHitPoints");
            var beauty = (StatDef)result.Defs.Single(d => d.defName == "Beauty");
            Assert.Equal(250f, wall.GetStatValueAbstract(maxHp));
            Assert.True(wall.StatBaseDefined(maxHp));
            Assert.Equal(1f, wall.GetStatValueAbstract(beauty));
            Assert.False(wall.StatBaseDefined(beauty));
        }

        [Fact]
        public void Duplicate_statBase_is_a_config_error()
        {
            DefLoadResult result = TestLoader.Load("<Defs>" + Stats + @"
  <ThingDef><defName>Wall</defName><statBases><MaxHitPoints>1</MaxHitPoints><MaxHitPoints>2</MaxHitPoints></statBases></ThingDef>
</Defs>");

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("more than once", error.Message);
        }

        [Fact]
        public void Unknown_stat_in_statBases_is_an_unresolved_reference()
        {
            DefLoadResult result = TestLoader.Load(@"<Defs>
  <ThingDef><defName>Wall</defName><statBases><NotAStat>1</NotAStat></statBases></ThingDef>
</Defs>");

            Assert.Contains(result.Errors, e => e.Message.Contains("NotAStat"));
        }

        [Fact]
        public void Stat_min_greater_than_max_is_a_config_error()
        {
            DefLoadResult result = TestLoader.Load("<Defs><StatDef><defName>S</defName><minValue>5</minValue><maxValue>1</maxValue></StatDef></Defs>");
            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("minValue", error.Message);
        }
    }
}
