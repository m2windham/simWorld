using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// A child born in the settlement is dressed as warmly as the founders (exposure4 lane), on a real generated map.
    /// The tailor made only shirts, so in the 600-day arc on 40/201 every child grew up in one shirt (comfortable from
    /// 12 C) while the founders kept the robes and pants they were generated in; a quake opened the houses at -11 C and
    /// eleven children died and no adult. RimWorld's tailor makes tribalwear and pants, and Biotech's children wear
    /// child-sized versions of them (rimworldwiki.com Children page, read).
    /// </summary>
    [Collection("GlobalDefs")]
    public class WarmClothesTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public WarmClothesTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private static void Run(Game game, int ticks)
        {
            for (int i = 0; i < ticks; i++) game.TickManager.DoSingleTick();
        }

        [Fact]
        public void The_tailor_makes_a_robe_pants_and_a_parka_for_a_child_in_a_shirt_and_the_child_wears_them()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "warm-clothes", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;
            Run(game, 2 * ConstructionInitiativeTuning.IntervalTicks);
            Find.ResearchManager.SetProjectFinishedForSetup(DefDatabase<ResearchProjectDef>.GetNamed("Tanning"));

            Pawn tailor = settlement.Citizens.First(p => p.Spawned && !p.Downed && !p.WorkTypeIsDisabled(WorkTypeDefOf.Tailoring));
            IntVec3 near = tailor.Position;
            IntVec3[] open = map.AllCells.OrderBy(c => (c - near).LengthHorizontalSquared)
                .Where(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0 && DwellingUtility.DwellingAt(map, c) == null)
                .Take(40).ToArray();
            ThingDef benchDef = DefDatabase<ThingDef>.GetNamed("TableTailor");
            IntVec3 benchCell = open.First(c => GenConstruct.CanPlaceBlueprintAt(benchDef, c, map, out _));
            Thing bench = ThingMaker.MakeThing(benchDef, GenStuff.DefaultStuffFor(benchDef));
            bench.SetFaction(settlement.faction);
            GenSpawn.Spawn(bench, benchCell, map, Rot4.North);
            foreach ((string def, IntVec3 cell) in new[] { ("Leather_Plain", 0), ("Cloth", 1), ("Leather_Plain", 2) }.Select(x => (x.Item1, open.Where(c => !bench.OccupiedRect().Contains(c)).ElementAt(10 + x.Item2))))
            {
                Thing stack = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def));
                stack.stackCount = 75;
                GenSpawn.Spawn(stack, cell, map);
            }

            Pawn child = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: settlement.faction);
            child.ageTracker.DebugSetAge(5f);
            child.relations.familyId = tailor.relations.familyId;
            child.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Shirt"), DefDatabase<ThingDef>.GetNamed("Cloth")));
            settlement.AddCitizen(child);
            GenSpawn.Spawn(child, open[5], map);
            float shirtOnly = TemperatureInjuryUtility.ComfortableTemperatureRange(child).min;
            float founder = TemperatureInjuryUtility.ComfortableTemperatureRange(tailor).min;

            foreach (Pawn p in settlement.Citizens.Where(p => p.Spawned && p.workSettings != null && p != child)) p.workSettings.DisableAll();
            tailor.workSettings.SetPriority(WorkTypeDefOf.Tailoring, 1);
            OutfittingInitiative.Run(map);
            CompBillGiver bills = ((ThingWithComps)bench).GetComp<CompBillGiver>()!;
            Assert.Contains(bills.BillStack.Bills, b => b.recipe.defName == "Make_Apparel_TribalRobe");
            Assert.Contains(bills.BillStack.Bills, b => b.recipe.defName == "Make_Apparel_Pants");
            Assert.Contains(bills.BillStack.Bills, b => b.recipe.defName == "Make_Apparel_Parka");

            // A child takes one garment per look, and looks again after RimWorld's 6000-9000 ticks: give it two days.
            static bool Dressed(Pawn c) => new[] { "Apparel_TribalRobe", "Apparel_Pants", "Apparel_Parka" }
                .All(d => c.apparel.WornApparel.Any(a => a.def.defName == d));
            int ticks = 0;
            for (; ticks < 2 * GenDate.TicksPerDay && !Dressed(child); ticks += GenDate.TicksPerHour) Run(game, GenDate.TicksPerHour);
            output.WriteLine("dressed after " + (ticks / (float)GenDate.TicksPerHour).ToString("0") + " h; parkas made "
                + map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Apparel_Parka")).Count + "; leather left "
                + map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Leather_Plain")).Sum(t => t.stackCount) + "; bills "
                + string.Join(", ", bills.BillStack.Bills.Select(b => b.recipe.defName + (b is Bill_Production bp ? " " + bp.repeatMode + " " + bp.targetCount + " made " + bills.CountProducts(bp) : "") + (b.suspended ? " suspended" : ""))));

            string worn = string.Join(",", child.apparel.WornApparel.Select(a => a.def.defName));
            float dressed = TemperatureInjuryUtility.ComfortableTemperatureRange(child).min;
            output.WriteLine("child wears " + worn + "; comfortable from " + dressed.ToString("0.0") + " (shirt only "
                + shirtOnly.ToString("0.0") + ", the founder " + founder.ToString("0.0") + " in "
                + string.Join(",", tailor.apparel.WornApparel.Select(a => a.def.defName)) + ")");
            Assert.Contains(child.apparel.WornApparel, a => a.def.defName == "Apparel_TribalRobe");
            Assert.Contains(child.apparel.WornApparel, a => a.def.defName == "Apparel_Pants");
            Assert.Contains(child.apparel.WornApparel, a => a.def.defName == "Apparel_Parka");
            Assert.True(dressed <= founder, "the dressed child still feels the cold before the founder does");
        }
    }
}
