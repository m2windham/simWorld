using System;
using SimWorld.Defs;
using Xunit;

namespace SimWorld.Tests.Defs
{
    /// <summary>
    /// <see cref="Def.index"/> is a <c>ushort</c>, so one concrete registry holds at most
    /// <see cref="DefDatabase.MaxDefsPerType"/> defs (research.endless.indexceiling). The one past that is
    /// refused with an error before it is registered anywhere, rather than half-added and then thrown out of
    /// a checked narrowing.
    /// </summary>
    public class DefIndexCeilingTests
    {
        [Fact]
        public void A_full_registry_refuses_the_next_def_cleanly()
        {
            var db = new DefDatabase();
            for (int i = 0; i < DefDatabase.MaxDefsPerType; i++)
            {
                db.Add(new TestDef { defName = "D" + i });
            }
            Assert.Equal(ushort.MaxValue, db.GetNamed<TestDef>("D" + (DefDatabase.MaxDefsPerType - 1)).index);

            var overflow = new TestDef { defName = "OneTooMany" };
            bool added = db.Add(overflow, DuplicateDefPolicy.LastWins, out string? error);

            Assert.False(added);
            Assert.Contains("registry is full", error);
            Assert.Null(db.GetNamedSilentFail<TestDef>("OneTooMany"));
            Assert.Null(db.GetNamed(typeof(Def), "OneTooMany"));
            Assert.Equal(DefDatabase.MaxDefsPerType, db.For<TestDef>().Count);
            Assert.Throws<InvalidOperationException>(() => db.Add(new TestDef { defName = "AlsoTooMany" }));
        }

        [Fact]
        public void A_full_registry_still_replaces_a_duplicate_in_place()
        {
            var db = new DefDatabase();
            for (int i = 0; i < DefDatabase.MaxDefsPerType; i++)
            {
                db.Add(new TestDef { defName = "D" + i });
            }

            var replacement = new TestDef { defName = "D7" };
            Assert.True(db.Add(replacement, DuplicateDefPolicy.LastWins, out string? error));
            Assert.Null(error);
            Assert.Same(replacement, db.GetNamed<TestDef>("D7"));
            Assert.Equal(DefDatabase.MaxDefsPerType, db.For<TestDef>().Count);
        }
    }
}
