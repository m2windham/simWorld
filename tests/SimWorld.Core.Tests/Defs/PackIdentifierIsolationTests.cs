using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SimWorld.Defs;

using Xunit;

namespace SimWorld.Tests.Defs
{
    /// <summary>
    /// One def load cannot see another's pack list.
    ///
    /// <para/><b>The failure this pins is a wrong answer, not a crash.</b>
    /// <see cref="global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers"/> was a plain static that
    /// <c>DefLoader</c> assigns when a load starts and resets to empty in a <c>finally</c>. Two loads on
    /// different threads shared that one slot, so one loader's cleanup could land in the middle of another's
    /// patching: the pack list read empty, and a <c>PatchOperationFindMod</c> that should have matched took
    /// its <c>nomatch</c> branch instead and patched in the wrong value. Content that quietly patches itself
    /// the wrong way is worse than content that fails to load, because nothing reports it.
    ///
    /// <para/>It surfaced exactly once, in a full-suite run, as
    /// <c>PatchOperationTests.FindMod_branches_on_which_packs_are_loaded</c> expecting "present" and reading
    /// "missing" — a test that touches none of the code that had just changed. A race that shows up in one run
    /// in many is the kind that gets called a flake and re-run; this asserts the isolation directly so it
    /// cannot be.
    /// </summary>
    public class PackIdentifierIsolationTests
    {
        [Fact]
        public void A_pack_list_set_on_one_thread_is_invisible_to_another()
        {
            global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers = new List<string> { "Mine.Only" };

            IReadOnlyCollection<string>? seenByOther = null;
            var other = new Thread(() => seenByOther = global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers);
            other.Start();
            other.Join();

            Assert.NotNull(seenByOther);
            Assert.Empty(seenByOther!);
            Assert.Contains("Mine.Only", global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers);

            global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers = Array.Empty<string>();
        }

        /// <summary>
        /// The shape of the original race, run head-on: many threads each set their own pack list, yield, and
        /// then check they still see it. Against the old plain static this fails almost immediately; the point
        /// of running it wide rather than once is that a single pass could get lucky.
        /// </summary>
        [Fact]
        public async Task Concurrent_loads_do_not_overwrite_each_others_pack_lists()
        {
            const int Threads = 16;
            var failures = new List<string>();
            var gate = new object();

            var tasks = new List<Task>();
            for (int i = 0; i < Threads; i++)
            {
                int n = i;
                tasks.Add(Task.Factory.StartNew(
                    () =>
                    {
                        string mine = "Pack." + n;
                        for (int round = 0; round < 50; round++)
                        {
                            global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers = new List<string> { mine };
                            Thread.Yield();

                            IReadOnlyCollection<string> seen = global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers;
                            if (seen.Count != 1 || !seen.Contains(mine))
                            {
                                lock (gate)
                                {
                                    failures.Add($"thread {n} round {round} saw [{string.Join(",", seen)}]");
                                }
                                return;
                            }

                            // What DefLoader's finally does, and the half that made the race destructive.
                            global::SimWorld.Defs.PatchOperationFindMod.LoadedPackIdentifiers = Array.Empty<string>();
                            Thread.Yield();
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default));
            }

            await Task.WhenAll(tasks);

            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }
    }
}
