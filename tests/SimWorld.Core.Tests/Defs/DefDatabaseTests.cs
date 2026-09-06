using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    public class DefDatabaseTests
    {
        private static TestDef Make(string name) => new TestDef { defName = name };

        [Fact]
        public void Add_registers_under_concrete_and_base_types_and_assigns_index()
        {
            var db = new DefDatabase();
            TestDef a = Make("A");
            var c = new ChildTestDef { defName = "C" };
            db.Add(a);
            db.Add(c);

            Assert.Equal(2, db.For<TestDef>().Count);
            Assert.Single(db.For<ChildTestDef>().AllDefs);
            Assert.Equal(2, db.DefCount);
            Assert.Equal(new Def[] { a, c }, db.AllDefs);
            Assert.Equal(0, a.index);
            Assert.Equal(0, c.index);
            Assert.Same(c, db.GetNamed<TestDef>("C"));
            Assert.Same(c, db.GetNamed(typeof(Def), "C"));
        }

        [Fact]
        public void Duplicate_policy_Error_keeps_first_and_reports()
        {
            var db = new DefDatabase();
            TestDef first = Make("A");
            db.Add(first);

            bool added = db.Add(Make("A"), DuplicateDefPolicy.Error, out string? error);

            Assert.False(added);
            Assert.NotNull(error);
            Assert.Same(first, db.GetNamed<TestDef>("A"));
            Assert.Equal(1, db.DefCount);
        }

        [Fact]
        public void Duplicate_policy_FirstWins_ignores_silently()
        {
            var db = new DefDatabase();
            TestDef first = Make("A");
            db.Add(first);

            bool added = db.Add(Make("A"), DuplicateDefPolicy.FirstWins, out string? error);

            Assert.False(added);
            Assert.Null(error);
            Assert.Same(first, db.GetNamed<TestDef>("A"));
        }

        [Fact]
        public void Duplicate_policy_LastWins_replaces_everywhere()
        {
            var db = new DefDatabase();
            db.Add(Make("A"));
            TestDef b = Make("B");
            db.Add(b);
            TestDef replacement = Make("A");

            bool added = db.Add(replacement, DuplicateDefPolicy.LastWins, out string? error);

            Assert.True(added);
            Assert.Null(error);
            Assert.Same(replacement, db.GetNamed<TestDef>("A"));
            Assert.Same(replacement, db.GetNamed(typeof(Def), "A"));
            Assert.Equal(new Def[] { b, replacement }, db.AllDefs);
            Assert.Equal(0, b.index);
            Assert.Equal(1, replacement.index);
        }

        [Fact]
        public void GetNamed_throws_and_SilentFail_returns_null()
        {
            var db = new DefDatabase();
            Assert.Throws<KeyNotFoundException>(() => db.GetNamed<TestDef>("Missing"));
            Assert.Null(db.GetNamedSilentFail<TestDef>("Missing"));
            Assert.Null(db.GetNamed(typeof(TestDef), "Missing"));
        }

        [Fact]
        public void Untyped_lookup_rejects_non_def_types()
        {
            var db = new DefDatabase();
            Assert.Throws<ArgumentException>(() => db.GetNamed(typeof(string), "x"));
        }

        [Fact]
        public void Remove_and_RemoveAll_reindex_and_clear_registries()
        {
            var db = new DefDatabase();
            TestDef a = Make("A");
            TestDef b = Make("B");
            var c = new ChildTestDef { defName = "C" };
            db.Add(a);
            db.Add(b);
            db.Add(c);

            Assert.True(db.Remove(a));
            Assert.False(db.Remove(a));
            Assert.Equal(0, b.index);
            Assert.Null(db.GetNamedSilentFail<TestDef>("A"));

            Assert.Equal(1, db.RemoveAll(typeof(ChildTestDef)));
            Assert.Empty(db.For<ChildTestDef>().AllDefs);
            Assert.Single(db.For<TestDef>().AllDefs);

            db.Clear();
            Assert.Equal(0, db.DefCount);
        }

        [Fact]
        public void Registry_order_is_insertion_order()
        {
            var db = new DefDatabase();
            string[] names = { "Zed", "Alpha", "Mid" };
            foreach (string name in names)
            {
                db.Add(Make(name));
            }
            Assert.Equal(names, db.For<TestDef>().AllDefs.Select(d => d.defName));
        }
    }

    /// <summary>Swaps <see cref="DefDatabase.Global"/>, so it shares the collection that serialises Global users.</summary>
    [Collection("GlobalDefs")]
    public class DefDatabaseFacadeTests
    {
        public DefDatabaseFacadeTests()
        {
            DefDatabase.Global = new DefDatabase();
        }

        [Fact]
        public void Static_facade_reads_the_global_database()
        {
            DefDatabase<TestDef>.Add(new TestDef { defName = "A" });
            DefDatabase<TestDef>.Add(new[] { new ChildTestDef { defName = "C" } });

            Assert.Equal(2, DefDatabase<TestDef>.DefCount);
            Assert.Equal("A", DefDatabase<TestDef>.GetNamed("A").defName);
            Assert.Null(DefDatabase<TestDef>.GetNamedSilentFail("Nope"));
            Assert.Single(DefDatabase<ChildTestDef>.AllDefsListForReading);

            DefDatabase<TestDef>.Clear();
            Assert.Equal(0, DefDatabase<TestDef>.DefCount);
            Assert.Equal(0, DefDatabase<ChildTestDef>.DefCount);
        }

        [Fact]
        public void Loader_defaults_to_the_global_database()
        {
            var loader = new DefLoader(types: new DefTypeResolver().AddAssembly(typeof(TestDef).Assembly), options: new DefLoadOptions { BindDefOfs = false });
            loader.AddXml("<Defs><TestDef><defName>G</defName></TestDef></Defs>").Load();
            Assert.Equal("G", DefDatabase<TestDef>.GetNamed("G").defName);
        }
    }
}
