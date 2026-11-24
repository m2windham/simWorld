using System.Collections.Generic;
using System.Linq;
using SimWorld.Director;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The chronicle remembers its worst day. <c>docs/research/era-arc.md</c> §5.2: a raid that killed 10 of 15
    /// left no moment, and the playtest's 24-death day went unrecorded, because the curator only knew firsts,
    /// era crossings and the longevity record.
    /// </summary>
    public class WorstDayMomentTests : ContentTestBase
    {
        public WorstDayMomentTests(CoreContentFixture content) : base(content)
        {
        }

        private static int Day(int day, int hour = 0) => day * GenDate.TicksPerDay + hour * GenDate.TicksPerHour;

        private static List<ChronicleEntry> WorstDays(IEnumerable<ChronicleEntry> entries) =>
            entries.Where(e => MomentCurator.CategoryForFreeform(e.incidentDefName) == MomentCurator.WorstDayCategory).ToList();

        [Fact]
        public void A_massacre_day_is_one_moment_naming_its_whole_toll()
        {
            var curator = new MomentCurator();
            var written = new List<ChronicleEntry>();
            for (int i = 0; i < 24; i++)
            {
                ChronicleEntry? e = curator.ConsiderDeathToll(Day(3, i % 20));
                if (e != null) written.Add(e);
            }

            ChronicleEntry only = Assert.Single(written);
            Assert.Same(only, Assert.Single(WorstDays(curator.Moments)));
            Assert.True(only.isMoment);
            Assert.Contains("24", only.incidentDefName);
            Assert.Equal(24, curator.WorstDayDeaths);
        }

        [Fact]
        public void A_lone_death_is_never_a_worst_day()
        {
            var curator = new MomentCurator();
            for (int day = 0; day < 10; day++) Assert.Null(curator.ConsiderDeathToll(Day(day, 5)));
            Assert.Empty(WorstDays(curator.Moments));
        }

        [Fact]
        public void Only_a_new_worst_fires_and_the_record_fires_less_often_over_time()
        {
            var curator = new MomentCurator();
            // Tolls by day: a bad day, a lighter one, an equal one, a worse one, then a long run of bad days
            // that never beat it.
            int[] tolls = { 3, 2, 3, 5, 4, 4, 2, 5, 3, 1 };
            var recordDays = new List<int>();
            for (int day = 0; day < tolls.Length; day++)
            {
                for (int d = 0; d < tolls[day]; d++)
                {
                    if (curator.ConsiderDeathToll(Day(day, d)) != null) recordDays.Add(day);
                }
            }

            Assert.Equal(new[] { 0, 3 }, recordDays); // a tie is not a record
            Assert.Equal(5, curator.WorstDayDeaths);
            Assert.Equal(2, WorstDays(curator.Moments).Count);
        }

        [Fact]
        public void Deaths_either_side_of_midnight_are_two_days()
        {
            var curator = new MomentCurator();
            Assert.Null(curator.ConsiderDeathToll(Day(4) - 1));
            Assert.Null(curator.ConsiderDeathToll(Day(4)));
            Assert.Empty(WorstDays(curator.Moments));
        }

        [Fact]
        public void The_storyteller_writes_a_worst_day_into_the_chronicle_from_citizens_deaths()
        {
            var storyteller = new Storyteller();
            Find.TickManager.DebugSetTicksGame(Day(2, 6));
            for (int i = 0; i < 4; i++) storyteller.RecordDeath(NewHuman("Victim" + i), DeathCause.Injury);

            ChronicleEntry worst = Assert.Single(WorstDays(storyteller.Chronicle));
            Assert.True(worst.isMoment);
            Assert.Contains("4", worst.incidentDefName);
            Assert.Contains(worst, storyteller.Moments);
        }

        [Fact]
        public void Scribe_round_trip_keeps_the_record_and_the_day_in_progress()
        {
            var curator = new MomentCurator();
            for (int i = 0; i < 3; i++) curator.ConsiderDeathToll(Day(5, i)); // record: 3 on day 5, still going

            string xml = Scribe.SaveToString(curator, "moments");
            MomentCurator loaded = Scribe.Load<MomentCurator>(xml, "moments", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(3, loaded.WorstDayDeaths);
            ChronicleEntry line = Assert.Single(WorstDays(loaded.Moments));

            // Day 5 continues after the load: the same line is rewritten, not a second one written.
            Assert.Null(loaded.ConsiderDeathToll(Day(5, 10)));
            Assert.Equal(4, loaded.WorstDayDeaths);
            Assert.Single(WorstDays(loaded.Moments));
            Assert.Contains("4", line.incidentDefName);

            // And the record is the loaded one: a later day of 4 is a tie, not a new worst.
            for (int i = 0; i < 4; i++) Assert.Null(loaded.ConsiderDeathToll(Day(9, i)));
            Assert.Single(WorstDays(loaded.Moments));
        }
    }
}
