using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
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
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// A settlement on a real generated map, opened, with a bench beside its band: the shared setup of the
    /// ingredient-delivery tests here and the armoury's end-to-end test.
    /// </summary>
    internal sealed class CraftingTown
    {
        public Game Game = null!;
        public Settlement Settlement = null!;
        public CoreMap Map = null!;
        public Pawn[] Citizens = Array.Empty<Pawn>();
        public IntVec3 Band;

        public static CraftingTown Open(string seed, int band = 20)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: band);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            Pawn[] citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToArray();
            foreach (Pawn citizen in citizens) Find.TickManager.RegisterAllTickabilityFor(citizen);
            return new CraftingTown
            {
                Game = game,
                Settlement = settlement,
                Map = map,
                Citizens = citizens,
                Band = SettlementReach.BandCell(map)!.Value,
            };
        }

        public static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        public static void Know(params string[] projects)
        {
            foreach (string p in projects) Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(p));
        }

        /// <summary>Every stack of these defs off the map and out of the ledger, so the test decides where they are.</summary>
        public void Clear(params string[] defs)
        {
            foreach (string name in defs)
            {
                ThingDef def = Def(name);
                foreach (Thing t in Map.listerThings.ThingsOfDef(def).ToList()) t.Destroy();
                Settlement.SetStoreCount(def, 0);
            }
        }

        private bool Free(IntVec3 c) =>
            GenGrid.InBounds(c, Map) && GenGrid.Standable(c, Map)
            && !Map.thingGrid.ThingsAt(c).Any(t => t.def.category is ThingCategory.Building or ThingCategory.Item or ThingCategory.Pawn);

        /// <summary>A bench on the nearest cell to the band that it and the ring around it leave free.</summary>
        public Thing Bench(string def)
        {
            // At least eight cells from the band, so a pocket walled in beside the bench never encloses it.
            IntVec3 site = GenRadial.RadialPattern.Where(o => o.LengthHorizontal >= 8f).Select(o => Band + o)
                .First(c => Free(c) && GenAdj.AdjacentCells.All(d => Free(c + d)) && Map.reachability.CanReachCells(Band, c));
            return GenSpawn.Spawn(ThingMaker.MakeThing(Def(def), Def("WoodLog")), site, Map);
        }

        /// <summary>The first free cell, in row order, between <paramref name="min"/> and <paramref name="min"/>+20
        /// cells from <paramref name="from"/> that the band can walk to and that passes <paramref name="also"/>.</summary>
        public IntVec3 FarCell(IntVec3 from, int min, Func<IntVec3, bool>? also = null)
        {
            for (int z = 0; z < Map.Size.z; z++)
            {
                for (int x = 0; x < Map.Size.x; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    float d = (c - from).LengthHorizontal;
                    if (d < min || d > min + 20) continue;
                    if (!Free(c) || !Map.reachability.CanReachCells(Band, c)) continue;
                    if (also != null && !also(c)) continue;
                    return c;
                }
            }
            throw new InvalidOperationException("no free reachable cell " + min + " from " + from);
        }

        public Thing Stack(string def, int count, IntVec3 cell)
        {
            Thing t = ThingMaker.MakeThing(Def(def));
            t.stackCount = count;
            return GenSpawn.Spawn(t, cell, Map);
        }

        /// <summary>A crafter who does only <paramref name="work"/>, at a skill that does not make the test slow.</summary>
        public Pawn Crafter(WorkTypeDef work)
        {
            Pawn crafter = Citizens[0];
            crafter.workSettings.DisableAll();
            crafter.workSettings.SetPriority(work, 1);
            crafter.skills!.GetSkill(SkillDefOf.Crafting)!.Level = 12;
            return crafter;
        }

        /// <summary>Things of <paramref name="def"/> lying on the map, worn or held by anybody on it.</summary>
        public int Made(string def) =>
            Map.listerThings.ThingsOfDef(Def(def)).Count(t => t.Spawned)
            + Map.mapPawns.AllPawnsSpawned.Count(p => p.equipment?.Primary?.def.defName == def
                || (p.apparel?.WornApparel.Any(a => a.def.defName == def) ?? false));

        /// <summary>
        /// A ring of granite of radius 3 around <paramref name="centre"/>, everything inside it cleared and any pawn
        /// moved out to the band: the inside is standable ground nobody can walk into. Returns the inside cells.
        /// </summary>
        public List<IntVec3> SealedPocket(IntVec3 centre)
        {
            ThingDef granite = Def("Granite");
            var inside = new List<IntVec3>();
            for (int x = -3; x <= 3; x++)
            {
                for (int z = -3; z <= 3; z++)
                {
                    var cell = new IntVec3(centre.x + x, 0, centre.z + z);
                    Assert.True(GenGrid.InBounds(cell, Map));
                    foreach (Thing t in Map.thingGrid.ThingsAt(cell).ToList())
                    {
                        if (t is Pawn p)
                        {
                            p.DeSpawn();
                            GenSpawn.Spawn(p, Band, Map);
                        }
                        else
                        {
                            t.Destroy();
                        }
                    }
                    if (Math.Max(Math.Abs(x), Math.Abs(z)) == 3) GenSpawn.Spawn(ThingMaker.MakeThing(granite), cell, Map);
                    else inside.Add(cell);
                }
            }
            Assert.All(inside, c => Assert.False(Map.reachability.CanReachCells(Band, c), c + " should be sealed"));
            return inside;
        }

        /// <summary>Ticks the whole game until <paramref name="done"/> holds or <paramref name="maxTicks"/> pass,
        /// calling <paramref name="each"/> every tick. Returns the ticks run.</summary>
        public int RunUntil(Func<bool> done, int maxTicks, Action? each = null)
        {
            for (int i = 1; i <= maxTicks; i++)
            {
                Game.TickManager.DoSingleTick();
                each?.Invoke();
                if (i % 250 == 0 && done()) return i;
            }
            return maxTicks;
        }
    }

    /// <summary>
    /// A bill's ingredients are found anywhere the crafter can walk within the bill's own search radius, carried
    /// to the bench and crafted; the settlement's ledger supplies a bench whose bill the map cannot; nothing is
    /// fetched from, or issued into, a sealed pocket (era-arc §12.3: on seed 40/201 nothing was ever crafted,
    /// because no ingredient lay within 12 cells of a bench).
    /// </summary>
    public class IngredientDeliveryTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public IngredientDeliveryTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static CoreRecipeDef Recipe(string name) => DefDatabase<CoreRecipeDef>.GetNamed(name);

        private static WorkGiver_DoBill Giver(string def) => (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed(def).Worker;

        private static Bill_Production BillOf(Thing bench, string recipe) =>
            WorkGiver_DoBill.BillGiverFor(bench)!.BillStack.Bills.OfType<Bill_Production>().Single(b => b.recipe.defName == recipe);

        /// <summary>Everything on the bench but <paramref name="recipe"/> suspended, re-applied as the
        /// settlement's planners add bills, so only the bill under test is worked.</summary>
        private static void OnlyBill(Thing bench, string recipe)
        {
            foreach (Bill bill in WorkGiver_DoBill.BillGiverFor(bench)!.BillStack.Bills)
            {
                bill.suspended = bill.recipe.defName != recipe;
            }
        }

        [Fact]
        public void A_crossbow_is_made_from_wood_and_steel_carried_from_forty_cells_away()
        {
            CraftingTown town = CraftingTown.Open("craft-delivery-far");
            CraftingTown.Know("Bows", "Crossbows");
            town.Clear("WoodLog", "Steel", "Iron", "Crossbow");
            Thing smithy = town.Bench("Smithy");
            IntVec3 woodCell = town.FarCell(smithy.Position, 40);
            IntVec3 steelCell = town.FarCell(smithy.Position, 40, c => (c - woodCell).LengthHorizontal >= 15);
            Thing wood = town.Stack("WoodLog", 60, woodCell);
            Thing steel = town.Stack("Steel", 40, steelCell);
            output.WriteLine("smithy " + smithy.Position + ", wood " + woodCell + ", steel " + steelCell);

            // A shooter with empty hands, so the armoury keeps a crossbow bill on the smithy.
            Pawn recruit = town.Citizens[1];
            if (recruit.equipment.Primary is ThingWithComps held) recruit.equipment.RemoveEquipment(held);
            recruit.skills!.GetSkill(SkillDefOf.Shooting)!.Level = 10;
            recruit.skills.GetSkill(SkillDefOf.Melee)!.Level = 0;
            town.Crafter(WorkTypeDefOf.Smithing);
            OutfittingInitiative.Run(town.Map);
            OnlyBill(smithy, "Make_Crossbow");
            Assert.True(BillOf(smithy, "Make_Crossbow").ShouldDoNow());

            CellRect beside = smithy.OccupiedRect().ExpandedBy(JobDriver_DoBill.PlaceReach);
            bool steelBeside = false;
            int ticks = town.RunUntil(() =>
            {
                OnlyBill(smithy, "Make_Crossbow");
                return town.Made("Crossbow") > 0;
            }, 30000, () => steelBeside |= town.Map.listerThings.ThingsOfDef(CraftingTown.Def("Steel")).Any(t => t.Spawned && beside.Contains(t.Position)));
            output.WriteLine("crossbows made " + town.Made("Crossbow") + " after " + ticks + " ticks; steel left far " + (steel.Destroyed ? 0 : steel.stackCount));

            Assert.True(town.Made("Crossbow") > 0, "no crossbow after " + ticks + " ticks");
            Assert.True(steelBeside, "the steel was never put down beside the smithy: it was not carried");
            Assert.True(steel.Destroyed || steel.stackCount < 40, "the far steel pile was never taken from");
            Assert.True(wood.Destroyed || wood.stackCount < 60 || town.Map.listerThings.ThingsOfDef(CraftingTown.Def("WoodLog")).Any(t => t != wood),
                "no wood was taken");
        }

        /// <summary>A first bill nobody can supply does not hold up the bill behind it (RimWorld's
        /// <c>StartOrResumeBillJob</c> goes on to the next bill).</summary>
        [Fact]
        public void A_bill_with_no_ingredients_anywhere_does_not_hold_up_the_next_one()
        {
            CraftingTown town = CraftingTown.Open("craft-delivery-order");
            CraftingTown.Know("Tanning");
            town.Clear("Leather_Plain", "Cloth");
            Thing tailor = town.Bench("TableTailor");
            CompBillGiver comp = WorkGiver_DoBill.BillGiverFor(tailor)!;
            foreach (Bill old in comp.BillStack.Bills.ToList()) comp.BillStack.Delete(old);
            comp.BillStack.AddBill(new Bill_Production(Recipe("Make_Apparel_Shirt")) { repeatMode = BillRepeatMode.Forever });
            comp.BillStack.AddBill(new Bill_Production(Recipe("Make_Apparel_HideArmor")) { repeatMode = BillRepeatMode.Forever });
            Thing leather = town.Stack("Leather_Plain", 75, town.FarCell(tailor.Position, 20));

            Job? job = Giver("DoBillsTailor").JobOnThing(town.Citizens[0], tailor);

            Assert.NotNull(job);
            Assert.Same(leather, Assert.Single(job!.GetTargetQueue(TargetIndex.B)).Thing);
            Assert.Equal(40, Assert.Single(job.countQueue!));
        }

        [Fact]
        public void Leather_held_only_in_the_ledger_is_issued_beside_the_tailor_and_made_into_a_hide_coat()
        {
            CraftingTown town = CraftingTown.Open("craft-delivery-ledger");
            CraftingTown.Know("Tanning");
            town.Clear("Leather_Plain", "Apparel_HideArmor");
            Thing tailor = town.Bench("TableTailor");
            town.Settlement.SetStoreCount(CraftingTown.Def("Leather_Plain"), 200);
            town.Crafter(WorkTypeDefOf.Tailoring);

            // The armoury counts the ledger as in reach and queues the coat.
            OutfittingInitiative.Run(town.Map);
            Assert.True(BillOf(tailor, "Make_Apparel_HideArmor").targetCount > 0);
            OnlyBill(tailor, "Make_Apparel_HideArmor");

            int issued = SettlementBillStock.IssueBillIngredientsFromStores(town.Settlement, town.Map);
            List<Thing> leather = town.Map.listerThings.ThingsOfDef(CraftingTown.Def("Leather_Plain")).ToList();
            output.WriteLine("issued " + issued + " in " + leather.Count + " stacks at " + string.Join(", ", leather.Select(t => t.Position)));
            Assert.Equal(40, issued); // one coat's worth, not the ledger
            Assert.Equal(160, town.Settlement.StoreCountOf(CraftingTown.Def("Leather_Plain")));
            Assert.Equal(40, leather.Sum(t => t.stackCount));
            Assert.All(leather, t => Assert.True((t.Position - tailor.Position).LengthHorizontal <= 3f, t.Position + " is not beside the tailor at " + tailor.Position));
            Assert.All(leather, t => Assert.True(town.Map.reachability.CanReachCells(town.Band, t.Position)));

            // The map can pay for the bill now, so the next pass issues nothing more.
            Assert.Equal(0, SettlementBillStock.IssueBillIngredientsFromStores(town.Settlement, town.Map));

            int ticks = town.RunUntil(() =>
            {
                OnlyBill(tailor, "Make_Apparel_HideArmor");
                return town.Made("Apparel_HideArmor") > 0;
            }, 20000);
            output.WriteLine("hide coats " + town.Made("Apparel_HideArmor") + " after " + ticks + " ticks; ledger " + town.Settlement.StoreCountOf(CraftingTown.Def("Leather_Plain")));
            Assert.True(town.Made("Apparel_HideArmor") > 0, "no hide coat after " + ticks + " ticks");
        }

        [Fact]
        public void Leather_in_a_sealed_pocket_is_not_fetched_and_none_is_issued_into_one()
        {
            CraftingTown town = CraftingTown.Open("craft-delivery-pocket");
            CraftingTown.Know("Tanning");
            town.Clear("Leather_Plain");
            Thing tailor = town.Bench("TableTailor");
            var bill = new Bill_Production(Recipe("Make_Apparel_HideArmor")) { repeatMode = BillRepeatMode.RepeatCount, repeatCount = 1 };
            WorkGiver_DoBill.BillGiverFor(tailor)!.BillStack.AddBill(bill);
            WorkGiver_DoBill giver = Giver("DoBillsTailor");

            // Far off, sealed: never a job.
            IntVec3 far = town.FarCell(tailor.Position, 45, c =>
                GenRadial.RadialPattern.Take(49).All(o => GenGrid.InBounds(c + o, town.Map))
                && (c - town.Band).LengthHorizontal > 10);
            List<IntVec3> farInside = town.SealedPocket(far);
            town.Stack("Leather_Plain", 75, far);
            Assert.All(town.Citizens, p => Assert.False(giver.HasJobOnThing(p, tailor), p + " was offered sealed leather"));

            // The same pile where it can be walked to is a job (the guard is not simply refusing).
            Thing open = town.Stack("Leather_Plain", 75, town.FarCell(tailor.Position, 45));
            Assert.True(giver.HasJobOnThing(town.Citizens[0], tailor));
            open.Destroy();

            // A pocket beside the tailor, and every other cell near it taken, so the nearest free ground to the
            // bench is sealed. The ledger's leather must land outside it.
            int dx = tailor.Position.x >= town.Band.x ? 1 : -1;
            List<IntVec3> nearInside = town.SealedPocket(new IntVec3(tailor.Position.x + 4 * dx, 0, tailor.Position.z));
            foreach (IntVec3 o in GenRadial.RadialPattern.Take(GenRadial.NumCellsInRadius(3.9f)))
            {
                IntVec3 c = tailor.Position + o;
                if (!GenGrid.InBounds(c, town.Map) || !GenGrid.Standable(c, town.Map) || nearInside.Contains(c)) continue;
                if (town.Map.thingGrid.ThingsAt(c).Any(t => t.def.category is ThingCategory.Item or ThingCategory.Building or ThingCategory.Pawn)) continue;
                town.Stack("Steel", 1, c);
            }
            Assert.Contains(nearInside, c => (c - tailor.Position).LengthHorizontal < 3f);
            town.Settlement.SetStoreCount(CraftingTown.Def("Leather_Plain"), 200);

            int issued = SettlementBillStock.IssueBillIngredientsFromStores(town.Settlement, town.Map);
            List<Thing> placed = town.Map.listerThings.ThingsOfDef(CraftingTown.Def("Leather_Plain")).Where(t => !farInside.Contains(t.Position)).ToList();
            output.WriteLine("issued " + issued + " at " + string.Join(", ", placed.Select(t => t.Position)) + "; tailor " + tailor.Position);
            Assert.Equal(40, issued);
            Assert.All(placed, t => Assert.True(town.Map.reachability.CanReachCells(town.Band, t.Position), t.Position + " is sealed"));
        }
    }
}
