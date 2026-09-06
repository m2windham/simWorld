using System;
using System.Collections.Generic;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class DefOfTests
    {
        private const string Xml = "<Defs><TestDef><defName>Alpha</defName></TestDef><TestDef><defName>Beta</defName></TestDef></Defs>";

        [Fact]
        public void Binds_static_fields_by_name_and_alias()
        {
            DefLoadResult result = TestLoader.Create(configure: o =>
            {
                o.BindDefOfs = true;
                o.DefOfTypes = new List<Type> { typeof(TestDefOf) };
            }).AddXml(Xml).Load();

            Assert.Empty(result.Errors);
            Assert.Equal("Alpha", TestDefOf.Alpha.defName);
            Assert.Equal("Beta", TestDefOf.Second.defName);
        }

        [Fact]
        public void Missing_def_is_an_error()
        {
            DefLoadResult result = TestLoader.Create(configure: o =>
            {
                o.BindDefOfs = true;
                o.DefOfTypes = new List<Type> { typeof(MissingDefOf) };
            }).AddXml(Xml).Load();

            DefLoadError error = Assert.Single(result.Errors);
            Assert.Contains("DoesNotExist", error.Message);
        }

        [Fact]
        public void Scanning_binds_every_attributed_class_in_registered_assemblies()
        {
            DefLoadResult result = TestLoader.Create(configure: o => o.BindDefOfs = true).AddXml(Xml).Load();

            Assert.Equal("Alpha", TestDefOf.Alpha.defName);
            Assert.Contains(result.Errors, e => e.Message.Contains("MissingDefOf"));
        }

        [Fact]
        public void Direct_binding_rejects_non_def_fields()
        {
            var errors = new List<DefLoadError>();
            DefOfHelper.BindDefsFor(typeof(NotADefOf), new DefDatabase(), errors);
            DefLoadError error = Assert.Single(errors);
            Assert.Contains("not a Def type", error.Message);
        }

        private static class NotADefOf
        {
            public static string Oops = "";
        }
    }
}
