using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// Who a founding band is. The host's default game (seed <c>simworld-host</c>) founded three Oonas and two
    /// Ivos among 20, and a quarter of every band were tribal elders who will not hunt. Two RimWorld rules were
    /// missing: the name a pawn goes by is retried against the ones already in use
    /// (<c>PawnBioAndNameGenerator.GeneratePawnName_Shuffled</c>), and a backstory that bars work is drawn less
    /// often (<c>SelectionWeightFactorFromWorkTagsDisabled</c>). <c>tools/bench --suite band</c> reports the spread.
    /// </summary>
    [Collection("GlobalDefs")]
    public class PawnNameAndBackstoryTests : ContentTestBase
    {
        public PawnNameAndBackstoryTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Pawn Tribesperson() =>
            PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Tribesperson, fixedBiologicalAge: 30f));

        [Fact]
        public void The_hosts_default_band_goes_by_twenty_different_names()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "simworld-host", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            List<string> names = game.World!.worldObjects.OfType<Settlement>().First().Citizens.Select(p => p.Name!.ToStringShort).ToList();

            Assert.Equal(20, names.Count);
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        [Fact]
        public void A_pawn_whose_first_name_is_taken_goes_by_its_last_name()
        {
            foreach (NameBankDef bank in DefDatabase<NameBankDef>.AllDefsListForReading.Where(b => b.slot == NameSlot.First))
            {
                foreach (string first in bank.names) NameUseChecker.AddName(new NameTriple(first, first, ""));
            }

            for (int i = 0; i < 10; i++)
            {
                var name = (NameTriple)Tribesperson().Name!;
                Assert.Equal(name.last, name.ToStringShort);
            }
        }

        [Fact]
        public void A_backstory_that_bars_work_is_drawn_less_often_than_one_that_bars_none()
        {
            Assert.Equal(1f, PawnGenerator.SelectionWeightFactorFromWorkTagsDisabled(WorkTags.None));
            Assert.True(PawnGenerator.SelectionWeightFactorFromWorkTagsDisabled(WorkTags.Violent) < 1f);

            // The tribal elder bars violence; the shaman bars nothing and requires nothing.
            var adulthoods = new Dictionary<string, int>();
            for (int i = 0; i < 1000; i++)
            {
                string? a = Tribesperson().story.adulthood?.defName;
                if (a != null) adulthoods[a] = adulthoods.GetValueOrDefault(a) + 1;
            }
            int elders = adulthoods.GetValueOrDefault("TribalElder");
            int shamans = adulthoods.GetValueOrDefault("TribalShaman");
            Assert.True(elders < 0.8 * shamans, "elders " + elders + ", shamans " + shamans);
        }
    }
}
