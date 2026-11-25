using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Homes renewing through the eras (<c>docs/design/era-progression.md</c> §7.9 steps 4 to 6), on a real
    /// generated settlement with its own band. The village is set up the same way each time: a tribal band whose
    /// first houses stand in a past era's rung, a town that has since entered the next era and learned its house, and
    /// stores and beds enough that nothing makes renewal wait unless a test says so.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RenewalHomesTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public RenewalHomesTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ResearchProjectDef Project(string name) => DefDatabase<ResearchProjectDef>.GetNamed(name);

        private sealed class Village
        {
            public Game Game = null!;
            public Settlement Settlement = null!;
            public CoreMap Map => Settlement.InteriorMap!;
            public Faction Faction = null!;
            public SettlementCompound Compound => Map.settlementCompound!;
            public List<Dwelling> Houses = new List<Dwelling>();
        }

        /// <summary>
        /// A tribal band on a generated map, its first <paramref name="houses"/> houses standing in
        /// <paramref name="rungs"/> (cycled), everybody else on a dormitory bed, a fortnight's food in the stores, and
        /// the town moved on into the Agrarian age with its longhouse (<c>House_Neo</c>) learned.
        /// </summary>
        private Village Founded(string seed, int houses, params string[] rungs)
        {
            CoreMap.ResetMapIdCounter();
            var v = new Village
            {
                Game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("TribalStart").scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20),
            };
            v.Settlement = v.Game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(v.Settlement.tile).Outcome);
            v.Faction = v.Settlement.Citizens.First(p => p.Spawned).Faction!;
            for (int i = 0; i < 2000 && v.Map.settlementCompound == null; i++) v.Game.TickManager.DoSingleTick();
            Assert.NotNull(v.Map.settlementCompound);

            ResearchManager research = Find.ResearchManager;
            research.SetProjectFinishedForSetup(Project("WindbreakShelters"));
            research.SetProjectFinishedForSetup(Project("PitDwellings"));
            List<int> households = DwellingUtility.Households(v.Settlement).Keys.ToList();
            for (int i = 0; i < houses; i++)
            {
                Dwelling? d = ShelterKit.RaiseOnPlot(v.Map, ShelterKit.Rung(rungs[i % rungs.Length]), households[i], v.Faction);
                Assert.NotNull(d);
                v.Houses.Add(d!);
            }

            foreach (ResearchProjectDef p in DefDatabase<EraDef>.GetNamed("SticksAndStones").Projects) research.SetProjectFinishedForSetup(p);
            research.SetProjectFinishedForSetup(Project("Shelter"));
            Assert.Equal("Agrarian", research.CurrentEra!.defName);
            Assert.Equal("House_Neo", DwellingUtility.PreferredDwellingDef()!.defName);

            BedEveryone(v);
            v.Settlement.AddStore(Def("MealSimple"), 600);
            DwellingUtility.AssignHomes(v.Settlement, v.Map);
            DwellingPlanner.Plan(v.Settlement, v.Map, dormitoryCovered: false); // ids, completion ticks, the founding house
            Assert.Null(RenewalInitiative.WaitReason(v.Settlement, v.Map));
            return v;
        }

        /// <summary>Dormitory beds, finished, in the corner of the map farthest from the village, until there is a bed
        /// for everybody.</summary>
        private static void BedEveryone(Village v)
        {
            CoreMap map = v.Map;
            int citizens = v.Settlement.Citizens.Count(p => !p.Dead);
            CellRect plot = v.Compound.DwellingPlots(1, map.Size).First().Plot;
            var centre = new IntVec3(plot.minX + plot.width / 2, 0, plot.minZ + plot.height / 2);
            IEnumerable<IntVec3> far = map.AllCells.OrderByDescending(c => (c - centre).LengthHorizontalSquared).ThenBy(c => c.x).ThenBy(c => c.z);
            foreach (IntVec3 c in far)
            {
                if (map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= citizens) return;
                if (c.x < 2 || c.z < 2 || c.x > map.Size.x - 3 || c.z > map.Size.z - 3) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, c, map, out _)) continue;
                if (!GenAdj.OccupiedRect(c, Rot4.North, ConstructionThingDefOf.Bed.size).ExpandedBy(1).Cells.All(n => GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0)) continue;
                Thing bed = ThingMaker.MakeThing(ConstructionThingDefOf.Bed, Def("WoodLog"));
                bed.SetFaction(v.Faction);
                GenSpawn.Spawn(bed, c, map);
            }
            Assert.Fail("no room for the dormitory beds");
        }

        private static Dwelling? HomeOf(CoreMap map, int household) =>
            DwellingUtility.DwellingsOn(map).FirstOrDefault(d => d.householdId == household);

        private static bool BuildingsStandIn(CoreMap map, CellRect footprint) =>
            footprint.Cells.Any(c => map.thingGrid.ThingsListAt(c).Any(t => t.def.category == ThingCategory.Building && !t.def.mineable));

        /// <summary>Runs the game until <paramref name="done"/> or <paramref name="days"/> pass, checking at every rare
        /// tick that <paramref name="household"/> has a finished house of its own and that there is a bed for everybody.</summary>
        private void RunHoused(Village v, int household, int days, System.Func<bool> done)
        {
            int citizens = v.Settlement.Citizens.Count(p => !p.Dead);
            for (int i = 0; i < days * GenDate.TicksPerDay && !done(); i++)
            {
                v.Game.TickManager.DoSingleTick();
                if (i % 250 != 0) continue;
                Dwelling? home = HomeOf(v.Map, household);
                Assert.True(home != null, "household " + household + " has no house on tick " + Find.TickManager.TicksGame);
                Assert.True(home!.IsComplete(v.Map),
                    "household " + household + " lives in a house that is not whole on tick " + Find.TickManager.TicksGame);
                Assert.True(v.Map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= citizens - v.Settlement.Citizens.Count(p => p.Dead),
                    "a citizen has no bed on tick " + Find.TickManager.TicksGame);
            }
        }

        // ---- step 4: a household gets the new house before it loses the old ----

        [Fact]
        public void A_past_era_home_is_rebuilt_as_the_current_eras_house_while_its_household_stays_housed()
        {
            Village v = Founded("renewal-homes", 3, "House_Paleo");
            Dwelling founding = v.Compound.DwellingById(v.Compound.foundingDwellingId)!;
            Assert.True(founding.isHeritage);

            // The next daily pass picks the oldest house that is not the founding one.
            Dwelling? old = null;
            for (int i = 0; i < GenDate.TicksPerDay + 1 && old == null; i++)
            {
                v.Game.TickManager.DoSingleTick();
                old = v.Houses.FirstOrDefault(d => d.replacedById != 0);
            }
            Assert.NotNull(old);
            Assert.NotSame(founding, old);
            int household = old!.householdId;
            CellRect oldFootprint = old.footprint;
            Dwelling replacement = v.Compound.DwellingById(old.replacedById)!;
            Assert.Equal("House_Neo", replacement.def.defName);
            Assert.Same(old, HomeOf(v.Map, household)); // still theirs while the new one goes up

            int movedOn = -1;
            RunHoused(v, household, 12, () =>
            {
                if (movedOn < 0 && replacement.householdId == household) movedOn = Find.TickManager.TicksGame;
                if (movedOn < 0) Assert.False(old.beingDemolished, "the old house came down before the household moved");
                return !v.Compound.dwellings.Contains(old);
            });
            output.WriteLine("moved on " + movedOn + ", old house gone by " + Find.TickManager.TicksGame);

            Assert.True(movedOn > 0, "the household never moved");
            Assert.DoesNotContain(old, v.Compound.dwellings);
            Assert.Same(replacement, HomeOf(v.Map, household));
            Assert.True(replacement.IsComplete(v.Map));
            Assert.False(BuildingsStandIn(v.Map, oldFootprint));
            Assert.Equal(0, replacement.replacesId);
            Assert.Contains(founding, v.Compound.dwellings); // the founding house is kept
        }

        [Fact]
        public void A_save_made_mid_rebuild_loads_and_the_rebuild_finishes()
        {
            Village v = Founded("renewal-save", 3, "House_Paleo");
            Dwelling? old = null;
            for (int i = 0; i < GenDate.TicksPerDay + 1 && old == null; i++)
            {
                v.Game.TickManager.DoSingleTick();
                old = v.Houses.FirstOrDefault(d => d.replacedById != 0);
            }
            Assert.NotNull(old);
            int household = old!.householdId, oldId = old.id, newId = old.replacedById;

            // Save once the new house is going up and the household has not yet moved.
            RunHoused(v, household, 6, () => RenewalInitiative.StageOf(v.Map, old) == RenewalStage.Underway);
            Assert.Equal(RenewalStage.Underway, RenewalInitiative.StageOf(v.Map, old));
            string xml = Scribe.SaveToString(v.Game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            Assert.Equal(xml, Scribe.SaveToString(loaded, "game"));

            var lv = new Village { Game = loaded, Settlement = loaded.World!.worldObjects.OfType<Settlement>().First() };
            Dwelling loadedOld = lv.Compound.DwellingById(oldId)!;
            Dwelling loadedNew = lv.Compound.DwellingById(newId)!;
            Assert.Equal(household, loadedOld.householdId);
            Assert.Equal(newId, loadedOld.replacedById);
            Assert.Equal(oldId, loadedNew.replacesId);
            Assert.Equal(RenewalStage.Underway, RenewalInitiative.StageOf(lv.Map, loadedOld));

            RunHoused(lv, household, 12, () => !lv.Compound.dwellings.Contains(loadedOld));
            Assert.DoesNotContain(loadedOld, lv.Compound.dwellings);
            Assert.Same(loadedNew, HomeOf(lv.Map, household));
            Assert.Equal("House_Neo", loadedNew.def.defName);
        }

        // ---- step 5: the initiative's waits and its order ----

        /// <summary>Moves the clock to the tick before the next daily pass and runs that one tick, so the pass reads
        /// the map exactly as the test left it.</summary>
        private static void PassNow(Village v)
        {
            int day = RenewalTuning.PassIntervalTicks;
            int next = (Find.TickManager.TicksGame / day + 1) * day;
            Find.TickManager.DebugSetTicksGame(next - 1);
            v.Game.TickManager.DoSingleTick();
            Assert.Equal(0, Find.TickManager.TicksGame % day);
        }

        private static bool AnyRenewal(Village v) => DwellingUtility.DwellingsOn(v.Map).Any(d => d.replacedById != 0 || d.replacesId != 0);

        /// <summary>Puts the village in <paramref name="crisis"/> and returns how to lift it.</summary>
        private System.Action Crisis(Village v, string crisis)
        {
            CoreMap map = v.Map;
            switch (crisis)
            {
                case "bed":
                {
                    Thing bed = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).First(b => !DwellingUtility.IsDwellingBed(b));
                    IntVec3 at = bed.Position;
                    bed.Destroy();
                    return () =>
                    {
                        Thing again = ThingMaker.MakeThing(ConstructionThingDefOf.Bed, Def("WoodLog"));
                        again.SetFaction(v.Faction);
                        GenSpawn.Spawn(again, at, map);
                    };
                }
                case "food":
                {
                    int stored = v.Settlement.StoreCountOf(Def("MealSimple"));
                    v.Settlement.SetStoreCount(Def("MealSimple"), 0);
                    foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.def.IsNutritionGivingIngestible).ToList()) t.Destroy();
                    return () => v.Settlement.AddStore(Def("MealSimple"), stored);
                }
                case "raid":
                {
                    var theirs = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Raiders", "F_Raiders");
                    Find.FactionManager.Add(theirs);
                    theirs.SetRelationDirect(v.Faction, FactionRelationKind.Hostile, -100);
                    v.Faction.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
                    Pawn raider = NewHuman("Raider");
                    raider.SetFaction(theirs);
                    IntVec3 edge = map.AllCells.First(c => c.x == 1 && GenGrid.Standable(c, map));
                    GenSpawn.Spawn(raider, edge, map);
                    return () => raider.Destroy();
                }
                default:
                    throw new System.ArgumentException(crisis);
            }
        }

        [Theory]
        [InlineData("bed", "not everyone has a bed")]
        [InlineData("food", "food is short")]
        [InlineData("raid", "enemies are on the map")]
        public void Nothing_is_renewed_in_a_crisis_and_renewal_resumes_once_it_is_over(string crisis, string reason)
        {
            Village v = Founded("renewal-wait-" + crisis, 3, "House_Paleo");
            System.Action lift = Crisis(v, crisis);
            Assert.Equal(reason, RenewalInitiative.WaitReason(v.Settlement, v.Map));

            PassNow(v);
            Assert.False(AnyRenewal(v), "a renewal was planned during the crisis (" + crisis + ")");
            Assert.Equal(reason, RenewalInitiative.WaitReason(v.Settlement, v.Map));

            // Over, renewal resumes on a later daily pass: once the houses the pass just planned for households
            // without one are up, since renewal waits for those too (DwellingTuning.MaxUnderway).
            lift();
            Assert.Null(RenewalInitiative.WaitReason(v.Settlement, v.Map));
            for (int i = 0; i < 3 * GenDate.TicksPerDay && !AnyRenewal(v); i++) v.Game.TickManager.DoSingleTick();
            Assert.True(AnyRenewal(v), "renewal did not resume after the crisis (" + crisis + ")");
        }

        [Fact]
        public void Of_two_obsolete_houses_the_more_crowded_is_renewed_first()
        {
            Village v = Founded("renewal-crowding", 3, "House_Paleo");
            Dwelling older = v.Houses[1], newer = v.Houses[2];
            Assert.False(older.isHeritage);
            Assert.False(newer.isHeritage);
            Assert.True(older.id < newer.id);

            // Crowd the newer house: three more people join its household, past its two beds. The older house's
            // household is cut to one, so nothing but crowding tells them apart besides age, which favours the older.
            int[] housed = v.Houses.Select(d => d.householdId).ToArray();
            List<Pawn> others = v.Settlement.Citizens.Where(p => !p.Dead && !housed.Contains(p.relations.familyId)).ToList();
            foreach (Pawn p in others.Take(3)) p.relations.familyId = newer.householdId;
            foreach (Pawn p in v.Settlement.Citizens.Where(p => p.relations.familyId == older.householdId).Skip(1).ToList())
            {
                p.relations.familyId = others.Skip(3).First().relations.familyId;
            }
            Assert.True(DwellingUtility.Households(v.Settlement)[newer.householdId] > newer.def.BedCount);

            Assert.Same(newer, RenewalInitiative.TryRenewOne(v.Settlement, v.Map));
        }

        [Fact]
        public void Of_two_equally_due_houses_the_older_is_renewed_first()
        {
            Village v = Founded("renewal-crowding", 3, "House_Paleo");
            int[] housed = v.Houses.Select(d => d.householdId).ToArray();
            int spare = v.Settlement.Citizens.First(p => !p.Dead && !housed.Contains(p.relations.familyId)).relations.familyId;
            foreach (Dwelling d in v.Houses.Skip(1))
            {
                foreach (Pawn p in v.Settlement.Citizens.Where(p => p.relations.familyId == d.householdId).Skip(1).ToList()) p.relations.familyId = spare;
            }
            Assert.Same(v.Houses[1], RenewalInitiative.TryRenewOne(v.Settlement, v.Map));
        }
    }
}
