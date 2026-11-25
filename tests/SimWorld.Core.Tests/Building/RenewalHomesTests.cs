using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
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
        // ---- step 6: what the town keeps ----

        /// <summary>
        /// Renews every house the initiative will take, one pass after another, with the building done at once: each
        /// new house's parts finished where they were planned, the move, and the old house's parts deconstructed as
        /// marked. The map, the band, the planner and the initiative are the real ones; only the builders' time is
        /// skipped. Returns how many houses were renewed.
        /// </summary>
        private static int RenewAllInstantly(Village v)
        {
            CoreMap map = v.Map;
            Pawn builder = v.Settlement.Citizens.First(p => p.Spawned && !p.Dead);
            int renewed = 0;
            for (int pass = 0; pass < 40; pass++)
            {
                Dwelling? old = RenewalInitiative.TryRenewOne(v.Settlement, map);
                if (old == null) return renewed;
                renewed++;
                Dwelling replacement = v.Compound.DwellingById(old.replacedById)!;
                foreach (IntVec3 c in replacement.footprint.Cells)
                {
                    foreach (Blueprint bp in map.thingGrid.ThingsListAt(c).OfType<Blueprint>().ToList()) bp.ReplaceWithFrame().CompleteConstruction(builder);
                }
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) GenConstruct.StepOffUnwalkableCell(p);
                DwellingUtility.AssignHomes(v.Settlement, map);
                Assert.True(old.beingDemolished, "the household did not move into its finished house");
                DwellingPlanner.Plan(v.Settlement, map, dormitoryCovered: false);
                foreach (global::SimWorld.Designations.Designation des in map.designationManager.SpawnedDesignationsOfDef(global::SimWorld.Designations.PlayerDesignationDefOf.Deconstruct).ToList())
                {
                    if (des.target.Thing is Thing t && t.Spawned) JobDriver_Deconstruct.Deconstruct(t);
                }
                DwellingPlanner.Plan(v.Settlement, map, dormitoryCovered: false);
                Assert.DoesNotContain(old, v.Compound.dwellings);
            }
            Assert.Fail("still renewing after 40 passes");
            return renewed;
        }

        [Fact]
        public void The_founding_house_survives_a_full_renewal_of_its_rung()
        {
            Village v = Founded("renewal-founding", 4, "House_Paleo");
            Dwelling founding = v.Compound.DwellingById(v.Compound.foundingDwellingId)!;
            Assert.Same(v.Houses[0], founding);
            int household = founding.householdId;
            List<int> renewedHouseholds = v.Houses.Skip(1).Select(d => d.householdId).ToList();

            Assert.Equal(3, RenewAllInstantly(v));

            Assert.Contains(founding, v.Compound.dwellings);
            Assert.True(founding.isHeritage);
            Assert.True(founding.IsComplete(v.Map));
            Assert.Equal(household, founding.householdId);
            Assert.Equal("House_Paleo", founding.def.defName);
            Assert.Single(v.Compound.dwellings, d => d.def.defName == "House_Paleo");
            Assert.All(renewedHouseholds, h => Assert.Single(v.Compound.dwellings, d => d.def.defName == "House_Neo" && d.householdId == h));
        }

        [Fact]
        public void One_house_of_each_obsolete_rung_is_kept_and_it_is_the_oldest()
        {
            Village v = Founded("renewal-exemplar", 4, "House_Paleo", "House_Meso", "House_Meso", "House_Meso");
            int chronicleBefore = Find.Storyteller.Chronicle.Count;

            Assert.Equal(2, RenewAllInstantly(v));

            Dwelling kept = Assert.Single(v.Compound.dwellings, d => d.def.defName == "House_Meso");
            // Renewal takes the oldest first, so the first pit house went; when the last-but-one was due, the oldest
            // still standing was kept instead (section 7.6).
            Assert.Same(v.Houses[2], kept);
            Assert.True(kept.isHeritage);
            Assert.True(kept.IsComplete(v.Map));
            Assert.Single(v.Compound.dwellings, d => d.def.defName == "House_Paleo" && d.isHeritage); // the founding house
            Assert.Single(Find.Storyteller.Chronicle.Skip(chronicleBefore), e => e.incidentDefName.Contains("last pit house"));
            Assert.Single(Find.Storyteller.Chronicle.Skip(chronicleBefore), e => e.incidentDefName.StartsWith("The town begins to rebuild"));
        }

        [Fact]
        public void A_god_marked_house_is_repaired_in_its_own_stuff_and_never_renewed()
        {
            Village v = Founded("renewal-god-mark", 3, "House_Paleo");
            CoreMap map = v.Map;
            Dwelling marked = v.Houses[1], plain = v.Houses[2];
            IntVec3 markedWall = marked.Parts.First(p => p.Kind == DwellingPartKind.Wall).Cell;
            IntVec3 plainWall = plain.Parts.First(p => p.Kind == DwellingPartKind.Wall).Cell;
            Assert.Equal(Def("Wall"), map.edificeGrid[markedWall]!.def);
            Assert.Equal(Def("WoodLog"), map.edificeGrid[markedWall]!.Stuff);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetHeritage(map.edificeGrid[markedWall]!.thingIDNumber, true).Outcome);
            Assert.True(marked.isHeritage);
            Assert.Equal(GodCommandOutcome.NoChange, GodCommands.SetHeritage(map.edificeGrid[markedWall]!.thingIDNumber, true).Outcome);

            // The town now builds in granite: a plain house's lost wall goes back in granite, the kept house's in wood.
            foreach (ResearchProjectDef p in Def("WallGranite").researchPrerequisites ?? new List<ResearchProjectDef>()) Find.ResearchManager.SetProjectFinishedForSetup(p);
            Thing blocks = ThingMaker.MakeThing(Def("BlocksGranite"));
            blocks.stackCount = 75;
            GenSpawn.Spawn(blocks, map.AllCells.First(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0 && map.areaManager.Home[c]), map);
            Assert.Equal(Def("WallGranite"), StoneWallMaterials.PreferredWallDef(map));

            map.edificeGrid[markedWall]!.Destroy();
            map.edificeGrid[plainWall]!.Destroy();
            DwellingPlanner.Repair(map);
            Blueprint markedBp = map.thingGrid.ThingsListAt(markedWall).OfType<Blueprint>().Single();
            Blueprint plainBp = map.thingGrid.ThingsListAt(plainWall).OfType<Blueprint>().Single();
            Assert.Equal(Def("Wall"), markedBp.EntityToBuild);
            Assert.Equal(Def("WoodLog"), markedBp.stuffToUse);
            Assert.Equal(Def("WallGranite"), plainBp.EntityToBuild);

            // Put back up, the kept house is never the one renewed.
            Pawn builder = v.Settlement.Citizens.First(p => p.Spawned && !p.Dead);
            markedBp.ReplaceWithFrame().CompleteConstruction(builder);
            plainBp.ReplaceWithFrame().CompleteConstruction(builder);
            Assert.Equal(1, RenewAllInstantly(v)); // the plain house, and nothing after it
            Assert.Contains(marked, v.Compound.dwellings);
            Assert.Equal("House_Paleo", marked.def.defName);
            Assert.Equal(0, marked.replacedById);
            Assert.DoesNotContain(plain, v.Compound.dwellings);
        }

        // ---- step 3: standing walls renewed in the era's stone, within the budget ----

        /// <summary>A row of <paramref name="count"/> finished wooden walls of the settlement's own, outside any house,
        /// on open home ground; and the town able to build in granite.</summary>
        private static List<Thing> WoodWallRow(Village v, int count)
        {
            CoreMap map = v.Map;
            foreach (ResearchProjectDef p in Def("WallGranite").researchPrerequisites ?? new List<ResearchProjectDef>()) Find.ResearchManager.SetProjectFinishedForSetup(p);
            IntVec3 start = map.AllCells.First(c => Enumerable.Range(-1, count + 2).All(dx =>
                Enumerable.Range(-1, 3).All(dz =>
                {
                    IntVec3 n = new IntVec3(c.x + dx, 0, c.z + dz);
                    bool row = dz == 0 && dx >= 0 && dx < count;
                    return GenGrid.InBounds(n, map) && GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0
                        && (!row || GenConstruct.CanPlaceBlueprintAt(Def("WallGranite"), n, map, out _)) // ground that bears stone
                        && DwellingUtility.DwellingAt(map, n) == null && v.Compound.DwellingPlots(int.MaxValue, map.Size).All(pl => !pl.Plot.Contains(n));
                })));
            var walls = new List<Thing>();
            for (int i = 0; i < count; i++)
            {
                Thing wall = ThingMaker.MakeThing(Def("Wall"), Def("WoodLog"));
                wall.SetFaction(v.Faction);
                GenSpawn.Spawn(wall, new IntVec3(start.x + i, 0, start.z), map);
                walls.Add(wall);
            }
            return walls;
        }

        private static void SpawnBlocks(Village v, int count)
        {
            CoreMap map = v.Map;
            foreach (Thing old in map.listerThings.ThingsOfDef(Def("BlocksGranite")).ToList()) old.Destroy();
            while (count > 0)
            {
                Thing blocks = ThingMaker.MakeThing(Def("BlocksGranite"));
                blocks.stackCount = System.Math.Min(count, blocks.def.stackLimit);
                count -= blocks.stackCount;
                GenSpawn.Spawn(blocks, map.AllCells.First(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0 && c.x > 3 && c.z > 3), map);
            }
        }

        private static int Replacements(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count(b => GenConstruct.ThingToReplace(b) != null);

        [Fact]
        public void With_spare_stone_wood_walls_are_planned_for_replacement_within_the_budget()
        {
            Village v = Founded("renewal-walls", 2, "House_Paleo");
            List<Thing> walls = WoodWallRow(v, 12);
            SpawnBlocks(v, 300);
            Assert.Equal(Def("WallGranite"), StoneWallMaterials.PreferredWallDef(v.Map));

            int budget = RenewalTuning.MaxUnderway(v.Settlement.Citizens.Count(p => !p.Dead)) * MaterialLadder.WallCellsPerStructure;
            Assert.True(budget < walls.Count);
            Assert.Equal(budget, MaterialLadder.UpgradeStanding(v.Map, RenewalTuning.MaxUnderway(v.Settlement.Citizens.Count(p => !p.Dead))));
            Assert.Equal(budget, Replacements(v.Map));
            Assert.All(v.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Where(b => GenConstruct.ThingToReplace(b) != null),
                b => Assert.Contains(GenConstruct.ThingToReplace(b), walls)); // the free-standing walls, not a house's
            Assert.All(walls, w => Assert.True(w.Spawned)); // every wood wall still stands
            Assert.All(v.Houses, h => Assert.True(h.IsComplete(v.Map))); // and no house wall was touched
            Assert.Equal(0, MaterialLadder.UpgradeStanding(v.Map, 1)); // the budget is full

            // With budget and stone for every wall on the map, only the free-standing row is taken.
            SpawnBlocks(v, 3000);
            MaterialLadder.UpgradeStanding(v.Map, 100);
            Assert.Equal(walls.Count, Replacements(v.Map));
            Assert.All(v.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Where(b => GenConstruct.ThingToReplace(b) != null),
                b => Assert.Contains(GenConstruct.ThingToReplace(b), walls));
        }

        [Fact]
        public void A_wall_renewal_never_takes_the_stone_below_the_reserve_and_none_without_spare_stone()
        {
            Village v = Founded("renewal-walls-reserve", 2, "House_Paleo");
            WoodWallRow(v, 12);
            int perWall = Def("WallGranite").CostListAdjusted(null).Single().count;

            SpawnBlocks(v, perWall * 3);
            Assert.Equal(Def("WallGranite"), StoneWallMaterials.PreferredWallDef(v.Map));
            int planned = MaterialLadder.UpgradeStanding(v.Map, 1);
            // Each one planned needed its cost half again in spare stone before it was planned.
            int spareAfter = perWall * 3 - planned * perWall;
            Assert.True(planned > 0);
            Assert.True(spareAfter + perWall >= (int)System.Math.Ceiling(perWall * RenewalTuning.MaterialReserve), "a renewal took the stone below the reserve");
            Assert.True(spareAfter < (int)System.Math.Ceiling(perWall * RenewalTuning.MaterialReserve), "one more could have been planned");

            foreach (Thing bp in v.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).ToList()) bp.Destroy(DestroyMode.Cancel);
            SpawnBlocks(v, 0);
            // With no stone the wall it would raise is a metal one (BestBuildingMetal); take the metal away too.
            for (ThingDef? metal = MaterialLadder.BestBuildingMetal(v.Map, Def("Wall")); metal != null; metal = MaterialLadder.BestBuildingMetal(v.Map, Def("Wall")))
            {
                foreach (Thing t in v.Map.listerThings.ThingsOfDef(metal).ToList()) t.Destroy();
            }
            Assert.Equal(0, MaterialLadder.UpgradeStanding(v.Map, 1));
            Assert.Equal(0, Replacements(v.Map));
        }

        // ---- step 9: what the host is shown ----

        [Fact]
        public void The_house_views_carry_renewal_and_the_era_outlook_counts_the_town()
        {
            Village v = Founded("renewal-view", 3, "House_Paleo");
            CoreMap map = v.Map;
            Dwelling founding = v.Houses[0];
            Dwelling old = RenewalInitiative.TryRenewOne(v.Settlement, map)!;
            Dwelling replacement = v.Compound.DwellingById(old.replacedById)!;

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);
            DwellingView oldView = Assert.Single(snapshot.Dwellings, d => d.DwellingId == old.id);
            DwellingView newView = Assert.Single(snapshot.Dwellings, d => d.DwellingId == replacement.id);
            DwellingView foundingView = Assert.Single(snapshot.Dwellings, d => d.DwellingId == founding.id);

            Assert.Equal("SticksAndStones", oldView.EraDefName);
            Assert.True(oldView.IsObsolete);
            Assert.False(oldView.IsHeritage);
            Assert.Equal(old.completedTick, oldView.BuiltTick);
            Assert.True(oldView.AgeYears >= 0f);
            Assert.Equal(1f, oldView.Condition);
            Assert.Equal("Planned", oldView.Renewal);
            Assert.Equal(replacement.id, oldView.ReplacedByDwellingId);
            Assert.Null(oldView.ReplacesDwellingId);

            Assert.Equal("Agrarian", newView.EraDefName);
            Assert.False(newView.IsObsolete);
            Assert.Equal(-1, newView.BuiltTick);
            Assert.Equal("None", newView.Renewal);
            Assert.Equal(old.id, newView.ReplacesDwellingId);

            Assert.True(foundingView.IsHeritage);
            Assert.Equal("None", foundingView.Renewal);

            // A wall of the new house up: the old one's renewal is under way; its walls knocked about: its condition falls.
            Pawn builder = v.Settlement.Citizens.First(p => p.Spawned && !p.Dead);
            Blueprint first = replacement.footprint.Cells.SelectMany(c => map.thingGrid.ThingsListAt(c)).OfType<Blueprint>().First(b => GenConstruct.ReplaceTag(b.EntityToBuild) == "Wall" || b.EntityToBuild.earthBanked);
            first.ReplaceWithFrame().CompleteConstruction(builder);
            Thing oldWall = map.edificeGrid[old.Parts.First(p => p.Kind == DwellingPartKind.Wall).Cell]!;
            oldWall.HitPoints = oldWall.MaxHitPoints / 4;
            DwellingView underway = Assert.Single(MapViewSnapshot.Capture(map).Dwellings, d => d.DwellingId == old.id);
            Assert.Equal("Underway", underway.Renewal);
            Assert.True(underway.Condition < 1f);

            // The town's summary counts what stands on the map.
            RenewalSummary summary = GodViewSnapshot.Capture().Era.Renewal;
            RenewalCategoryLine homes = summary.Categories.Single(c => c.Category == "Homes");
            List<Dwelling> standing = v.Compound.dwellings.Where(d => !d.beingDemolished && d.completedTick >= 0).ToList();
            Assert.Equal(standing.Count, homes.Count);
            Assert.Equal(standing.Count(d => d.def.era!.defName == "Agrarian"), homes.InCurrentEra);
            Assert.Equal(standing.Count, homes.InCurrentOrPreviousEra); // Sticks and stones is the previous era
            Assert.Equal(1, homes.Underway);
            Assert.Equal(standing.Count(d => d.isHeritage), summary.HeritageCount);
            Assert.Null(summary.WaitingFor);
            Assert.Equal(new[] { "Homes", "Storage", "Workshops", "Walls" }, summary.Categories.Select(c => c.Category));
            int walls = map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .Count(b => b.Faction != null && GenConstruct.ReplaceTag(b.def) == "Wall" && DwellingUtility.DwellingAt(map, b.Position) == null);
            Assert.Equal(walls, summary.Categories.Single(c => c.Category == "Walls").Count);
            Assert.InRange(summary.Share, 0f, 1f);
        }
    }
}
