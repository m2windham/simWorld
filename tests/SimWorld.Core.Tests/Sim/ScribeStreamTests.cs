using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using SimWorld.Sim;
using Xunit;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// The streaming save path (<see cref="Scribe.SaveToStream{T}"/> / <see cref="Scribe.SaveToFile{T}"/>),
    /// which writes through an <see cref="System.Xml.XmlWriter"/> instead of building an
    /// <see cref="XDocument"/> first. What is worth pinning here is not the XML — the in-memory path's own
    /// tests already cover every value, collection and reference shape — but the three things streaming
    /// changes: that both paths describe the same document, that bytes actually leave during the save rather
    /// than at the end, and that a save which throws does not leave its file locked open.
    /// </summary>
    public class ScribeStreamTests
    {
        /// <summary>A stream that only counts, so a test can ask what had reached the sink at a given moment
        /// without holding the save itself.</summary>
        private sealed class CountingStream : Stream
        {
            public long BytesWritten { get; private set; }
            public bool Disposed { get; private set; }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }

        /// <summary>Big enough that a buffered writer must flush partway through, and carrying a subclass so
        /// the polymorphic <c>Class</c> attribute is exercised on the streaming path too.</summary>
        private sealed class Crowd : IExposable
        {
            public const int Size = 20_000;

            public List<Item>? members;
            public Func<long>? probe;

            /// <summary>What the sink had received by the time the last member was written — captured from
            /// inside the save, which is the only place the question means anything.</summary>
            public long BytesAtLastMember { get; private set; }

            public static Crowd Build()
            {
                var members = new List<Item>(Size);
                for (int i = 0; i < Size; i++)
                {
                    members.Add(i % 100 == 0
                        ? new SpecialItem("s" + i, i, "shiny")
                        : new Item("i" + i, i));
                }
                return new Crowd { members = members };
            }

            /// <summary>A crowd small enough to read, holding the members its own cross-references point at —
            /// a reference only resolves against something the same save wrote deeply.</summary>
            public static Crowd Small()
            {
                var a = new Item("a", 1);
                var b = new Item("b", 2);
                var c = new SpecialItem("c", 3, "shiny");
                a.next = c;
                c.next = a;
                return new Crowd { members = new List<Item> { a, b, c } };
            }

            public void ExposeData()
            {
                Scribe_Collections.Look(ref members, "members", LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.Saving && probe != null) BytesAtLastMember = probe();
            }
        }

        [Fact]
        public void A_streamed_save_describes_the_same_document_as_an_in_memory_one()
        {
            Crowd crowd = Crowd.Small();
            XDocument inMemory = Scribe.SaveToXDocument(crowd, "crowd");

            using var stream = new MemoryStream();
            Scribe.SaveToStream(crowd, "crowd", stream);
            XDocument streamed = XDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));

            Assert.True(XNode.DeepEquals(inMemory.Root, streamed.Root),
                "streamed:\n" + streamed + "\n\nin memory:\n" + inMemory);
        }

        [Fact]
        public void A_streamed_save_round_trips_including_a_polymorphic_subclass()
        {
            using var stream = new MemoryStream();
            Scribe.SaveToStream(Crowd.Small(), "crowd", stream);

            Crowd loaded = Scribe.Load<Crowd>(Encoding.UTF8.GetString(stream.ToArray()), "crowd");

            List<Item> members = loaded.members!;
            Assert.Equal(new[] { "a", "b", "c" }, members.Select(m => m.id));
            SpecialItem special = Assert.IsType<SpecialItem>(members[2]);
            Assert.Equal("shiny", special.extra);
            Assert.Same(members[0], special.next);
            Assert.Same(special, members[0].next);
        }

        [Fact]
        public void Content_reaches_the_sink_during_the_save_rather_than_at_the_end()
        {
            Crowd crowd = Crowd.Build();
            var stream = new CountingStream();
            crowd.probe = () => stream.BytesWritten;

            Scribe.SaveToStream(crowd, "crowd", stream);

            // The point of the whole path: by the time the last member had been written, the sink had already
            // taken most of the document. An in-memory saver could only ever report 0 here, because nothing
            // has been serialised at all until FinalizeSaving hands the document over.
            Assert.True(crowd.BytesAtLastMember > 0, "nothing had reached the sink while the save was still running");
            Assert.True(stream.BytesWritten > crowd.BytesAtLastMember, "the tail of the document never arrived");
        }

        [Fact]
        public void A_stream_the_caller_owns_is_left_open_and_one_it_hands_over_is_not()
        {
            var kept = new CountingStream();
            Scribe.SaveToStream(Crowd.Small(), "crowd", kept);
            Assert.False(kept.Disposed);

            var handedOver = new CountingStream();
            Scribe.SaveToStream(Crowd.Small(), "crowd", handedOver, leaveOpen: false);
            Assert.True(handedOver.Disposed);
        }

        [Fact]
        public void Saving_to_a_file_writes_a_save_that_loads_back_and_leaves_no_handle_open()
        {
            string path = Path.Combine(Path.GetTempPath(), "simworld-scribe-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Scribe.SaveToFile(Crowd.Small(), "crowd", path);

                Crowd loaded = Scribe.Load<Crowd>(File.ReadAllText(path), "crowd");
                List<Item> members = loaded.members!;
                Assert.Equal(3, members.Count);
                Assert.IsType<SpecialItem>(members[2]);
            }
            finally
            {
                // Would throw if the save still held the file — which is the other half of what is asserted.
                File.Delete(path);
            }
        }

        private sealed class Thrower : IExposable
        {
            public void ExposeData()
            {
                int one = 1;
                Scribe_Values.Look(ref one, "one");
                throw new InvalidOperationException("boom");
            }
        }

        [Fact]
        public void A_save_that_throws_partway_releases_its_file_and_leaves_no_session_behind()
        {
            string path = Path.Combine(Path.GetTempPath(), "simworld-scribe-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Assert.Throws<InvalidOperationException>(() => Scribe.SaveToFile(new Thrower(), "thrower", path));
                Assert.Equal(LoadSaveMode.Inactive, Scribe.mode);
                File.Delete(path);

                // And the thread is usable again rather than stuck in a session that never closed.
                using var stream = new MemoryStream();
                Scribe.SaveToStream(Crowd.Small(), "crowd", stream);
                Assert.NotEqual(0, stream.Length);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void An_attribute_written_after_child_content_is_rejected_rather_than_silently_dropped()
        {
            using var stream = new MemoryStream();
            Scribe.saver.InitSaving(stream, "savegame");
            try
            {
                Scribe.saver.EnterNode("node");
                Scribe.saver.WriteAttribute("Class", "Fine.Before.Content");
                Scribe.saver.WriteElement("child", "1");
                Assert.Throws<ScribeException>(() => Scribe.saver.WriteAttribute("Class", "Too.Late"));
            }
            finally
            {
                Scribe.ForceStop();
            }
        }

        [Fact]
        public void The_two_finalizers_refuse_each_other_s_sessions()
        {
            Scribe.saver.InitSaving("savegame");
            try
            {
                Assert.Throws<ScribeException>(() => Scribe.saver.FinalizeSavingStreamed());
            }
            finally
            {
                Scribe.ForceStop();
            }

            using var stream = new MemoryStream();
            Scribe.saver.InitSaving(stream, "savegame");
            try
            {
                Assert.Throws<ScribeException>(() => Scribe.saver.FinalizeSaving());
            }
            finally
            {
                Scribe.ForceStop();
            }
        }
    }
}
