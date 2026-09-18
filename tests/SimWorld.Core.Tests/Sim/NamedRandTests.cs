using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// The rule an ablation study lives or dies by: a system's dice are its own.
    ///
    /// <para/><b>What goes wrong without it.</b> Phase two of this project injects defects deliberately and
    /// measures what each one costs, by running the same seeded world with the defect off and on and
    /// subtracting. If the defect rolls against the ambient <see cref="Rand.Current"/>, its very first draw
    /// shifts every subsequent draw in the game by one — the weather, the births, the raids, the diseases all
    /// land on different values — and the measured difference is mostly that reshuffle. The number would be
    /// large, reproducible, and about nothing.
    ///
    /// <para/>These are cheap tests for an expensive mistake. The mistake does not announce itself: an
    /// ablation contaminated this way still produces a tidy table.
    /// </summary>
    [Collection("GlobalDefs")]
    public class NamedRandTests : ContentTestBase
    {
        public NamedRandTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>
        /// The one that matters. Deriving a named stream and drawing from it leaves the ambient stream exactly
        /// where it was, so a defect that rolls cannot move anybody else's dice.
        /// </summary>
        [Fact]
        public void Drawing_from_a_named_stream_does_not_move_the_ambient_one()
        {
            uint before = Rand.Current.Iterations;

            RandomStream mine = NamedRand.For("seed", "Blight");
            for (int i = 0; i < 100; i++) _ = mine.Value;

            Assert.Equal(before, Rand.Current.Iterations);
        }

        /// <summary>
        /// The other half: the ambient stream moving does not move a named one. Together these say the two are
        /// genuinely independent rather than merely starting apart.
        /// </summary>
        [Fact]
        public void The_ambient_stream_moving_does_not_move_a_named_one()
        {
            float[] Draw()
            {
                RandomStream s = NamedRand.For("seed", "Blight");
                return new[] { s.Value, s.Value, s.Value };
            }

            float[] quiet = Draw();
            for (int i = 0; i < 500; i++) _ = Rand.Current.Value;
            float[] afterNoise = Draw();

            Assert.Equal(quiet, afterNoise);
        }

        [Fact]
        public void The_same_name_and_seed_replay_the_same_sequence()
        {
            RandomStream a = NamedRand.For("seed", "Blight");
            RandomStream b = NamedRand.For("seed", "Blight");

            for (int i = 0; i < 20; i++) Assert.Equal(a.Value, b.Value);
        }

        [Fact]
        public void Different_names_are_different_streams()
        {
            RandomStream blight = NamedRand.For("seed", "Blight");
            RandomStream raids = NamedRand.For("seed", "Raids");

            int same = 0;
            for (int i = 0; i < 20; i++)
            {
                if (blight.Value == raids.Value) same++;
            }

            // Asserted as "not the same stream" rather than against any particular values: two streams that
            // agreed on every draw would be one stream wearing two names, which is the failure worth catching.
            Assert.True(same < 20, "two differently-named streams produced identical sequences");
        }

        [Fact]
        public void Different_seeds_are_different_streams_for_the_same_name()
        {
            RandomStream here = NamedRand.For("world-a", "Blight");
            RandomStream there = NamedRand.For("world-b", "Blight");

            int same = 0;
            for (int i = 0; i < 20; i++)
            {
                if (here.Value == there.Value) same++;
            }

            Assert.True(same < 20, "the same defect rolled identically in two different worlds");
        }

        /// <summary>
        /// Names cannot be run together into one stream. "Fire" + "Storm" and "FireStorm" would hash to the
        /// same seed under naive concatenation, and two systems quietly sharing dice is the exact failure this
        /// class exists to prevent — silent, and invisible in any result table.
        /// </summary>
        [Fact]
        public void Names_that_would_concatenate_into_each_other_do_not_collide()
        {
            RandomStream a = NamedRand.For("Fire", "Storm");
            RandomStream b = NamedRand.For("", "FireStorm");

            int same = 0;
            for (int i = 0; i < 20; i++)
            {
                if (a.Value == b.Value) same++;
            }

            Assert.True(same < 20, "seed/name boundary is not separated; two streams collided");
        }
    }
}
