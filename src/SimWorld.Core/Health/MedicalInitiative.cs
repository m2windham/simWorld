using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Health
{
    /// <summary>
    /// Translation: <b>a settlement provides for its sick with what its era knows</b> (system: health, item
    /// health.medicalresearch). RimWorld's player builds the hospital, the drug lab and the medicine bill; there
    /// is no player here, so the settlement does, the same translation <see cref="OutfittingInitiative"/> and
    /// <see cref="SettlementWorksInitiative"/> make for clothes and benches.
    ///
    /// <para/><b>The problem it solves.</b> The era-arc runs died of disease after researching every medical
    /// project, because none of those projects changed a tend (<c>docs/research/era-arc.md</c> §7.3 gap 4,
    /// §9.5). RimWorld answers infection with tend quality (doctor × medicine potency + the bed's offset,
    /// <see cref="TendUtility.CalculateBaseTendQuality"/>) and immunity gain (the bed's
    /// <see cref="HealthStatDefOf.BedRestEffectiveness"/>). The era decides which of those levers a settlement has:
    /// <list type="bullet">
    /// <item><b>SticksAndStones to Bronze:</b> herbal medicine from healroot (potency 0.6, ceiling 0.7), tended
    /// in an ordinary bed. Unchanged: the founding band knows healroot.</item>
    /// <item><b>Classical</b> (<see cref="MedicalEraDefOf.Medicine"/>): a sick-house, one hospital bed per
    /// <see cref="MedicalBeds.CitizensPerBedSickHouse"/> citizens: +0.10 tend quality and faster immunity.</item>
    /// <item><b>Medieval</b> (<see cref="MedicalEraDefOf.Hospitals"/>): a hospital, one bed per
    /// <see cref="MedicalBeds.CitizensPerBedHospital"/>. (<see cref="MedicalEraDefOf.Pharmacology"/>): the
    /// apothecary's bench, distilling herbal medicine into medicine (potency 1.0, ceiling 1.0), one dose in hand
    /// wanted per citizen.</item>
    /// <item><b>Industrial onward:</b> the same beds and medicine, more of them as the settlement grows.</item>
    /// </list>
    ///
    /// <para/><b>Stateless.</b> Every pass re-derives what is wanted from the map, the bills live on the bench
    /// that saves them, and placement scans cells in order, so it draws nothing from the shared random stream
    /// beyond what <see cref="SettlementWorksInitiative.TryPlaceBlueprint"/> itself spends.
    /// </summary>
    public static class MedicalInitiative
    {
        /// <summary>Self-gate cadence: the rare bucket every map initiative uses.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>How far from the ward's anchor a hospital bed may go. Unsourced: a room's width.</summary>
        public const int WardRadius = 6;

        /// <summary>Cells in the stockpile kept beside the apothecary's bench for its herbal medicine, the way
        /// <see cref="SettlementWorkshops"/> keeps one beside a smithy. Unsourced.</summary>
        public const int BenchStockCells = 4;

        /// <summary>Doses of medicine wanted in hand per citizen: the same figure the healroot field is sized by
        /// (<see cref="Farming.CropDemandWorker_Medicine.MedicinePerCitizenWanted"/>).</summary>
        public static float MedicinePerCitizenWanted => Farming.CropDemandWorker_Medicine.MedicinePerCitizenWanted;

        /// <summary>One map, self-gating on <see cref="IntervalTicks"/>. Called from <see cref="Map.Map.MapTick"/>.</summary>
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated logic, public so a test can drive one pass. A no-op on a map no settlement owns.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Settlement? settlement = HuntingInitiative.SettlementFor(map);
            if (settlement == null) return;
            int living = LivingCitizens(settlement);
            if (living == 0) return;

            EnsureHospitalBeds(map, settlement, living);
            EnsureDrugLab(map);
            UpdateMedicineBills(map, living);
        }

        /// <summary>
        /// Hospital beds against <see cref="MedicalBeds.HospitalBedsWanted"/>, one blueprint a pass, in the
        /// compound's dormitory: under its roof and inside its walls, where a patient is out of the weather. A
        /// free bed slot first (<see cref="SettlementCompound.BedCells"/>); with none free, a dormitory cot the
        /// settlement has more of than it has people sleeping in the dormitory is taken up and the hospital bed
        /// goes where it stood (<see cref="TryConvertSurplusCot"/>). With no compound the ward goes wherever the
        /// map takes it, beside its anchor.
        /// </summary>
        private static void EnsureHospitalBeds(Map.Map map, Settlement settlement, int living)
        {
            ThingDef bed = MedicalEraDefOf.HospitalBed;
            int wanted = MedicalBeds.HospitalBedsWanted(living);
            if (wanted <= 0) return;
            if (SettlementWorksInitiative.CountBuiltOrPlanned(map, bed) >= wanted) return;

            SettlementCompound? compound = map.settlementCompound;
            if (compound == null)
            {
                SettlementWorksInitiative.TryPlaceBlueprint(map, bed, WardAnchor(map), WardRadius);
                return;
            }
            foreach (IntVec3 cell in compound.BedCells)
            {
                if (!GenConstruct.CanPlaceBlueprintAt(bed, cell, map, out _)) continue;
                SpawnBlueprint(map, bed, cell);
                return;
            }
            TryConvertSurplusCot(map, settlement, living, compound);
        }

        /// <summary>
        /// Takes up one dormitory cot nobody is in or has claimed, when the dormitory has more cots than the
        /// citizens who sleep there (<paramref name="living"/> less those housed in their own dwelling), and
        /// lays a hospital bed blueprint on its slot. The cot is deconstructed on the spot with its material
        /// refunded (<see cref="DestroyMode.Deconstruct"/>): the labour of taking it apart is skipped, a
        /// shortcut recorded here rather than a deconstruct job no settlement initiative can yet raise (the
        /// deconstruct giver works the player's marks alone). Returns whether a cot was converted.
        /// </summary>
        public static bool TryConvertSurplusCot(Map.Map map, Settlement settlement, int living, SettlementCompound compound)
        {
            var cots = new List<Thing>();
            foreach (IntVec3 cell in compound.BedCells)
            {
                IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < here.Count; i++)
                {
                    if (ReferenceEquals(here[i].def, ConstructionThingDefOf.Bed) && here[i].Position == cell
                        && DwellingUtility.DwellingOfBed(here[i]) == null) cots.Add(here[i]);
                }
            }
            int sleepers = living - DwellingUtility.HousedCitizens(settlement, map);
            if (cots.Count <= Math.Max(0, sleepers)) return false;

            // The last cot in the layout's order first, so the ward fills from the back of the dormitory.
            for (int i = cots.Count - 1; i >= 0; i--)
            {
                Thing cot = cots[i];
                if (map.reservationManager.IsReserved(cot) || Occupied(map, cot)) continue;
                IntVec3 cell = cot.Position;
                cot.Destroy(DestroyMode.Deconstruct);
                SpawnBlueprint(map, MedicalEraDefOf.HospitalBed, cell);
                return true;
            }
            return false;
        }

        private static bool Occupied(Map.Map map, Thing bed)
        {
            foreach (IntVec3 c in bed.OccupiedRect().Cells)
            {
                IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                for (int i = 0; i < here.Count; i++)
                {
                    if (here[i] is Pawn) return true;
                }
            }
            return false;
        }

        private static void SpawnBlueprint(Map.Map map, ThingDef entityDef, IntVec3 cell)
        {
            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(entityDef);
            if (blueprintDef == null) return;
            var blueprint = (Blueprint)ThingMaker.MakeThing(blueprintDef);
            blueprint.stuffToUse = GenStuff.AffordableStuffFor(map, entityDef);
            GenSpawn.Spawn(blueprint, cell, map);
        }

        /// <summary>The apothecary's bench, once, beside the herbal medicine it works.</summary>
        private static void EnsureDrugLab(Map.Map map)
        {
            ThingDef lab = MedicalEraDefOf.DrugLab;
            if (!lab.IsResearchFinished) return;
            if (SettlementWorksInitiative.CountBuiltOrPlanned(map, lab) == 0)
            {
                IntVec3? anchor = NearestHerbal(map) ?? WardAnchor(map);
                SettlementWorksInitiative.TryPlaceBlueprint(map, lab, anchor, Crafting.WorkGiver_DoBill.IngredientSearchRadius / 2);
            }
            EnsureBenchStock(map, lab);
        }

        /// <summary>
        /// Medicine wanted in hand (<see cref="MedicinePerCitizenWanted"/> per citizen), as a target-count bill on
        /// each apothecary's bench. The bill counts unclaimed doses on the map (<see cref="CompBillGiver.CountProducts"/>),
        /// so a dose spent on a tend is made again. Never a zero target: with nobody living there is no bill.
        /// </summary>
        public static void UpdateMedicineBills(Map.Map map, int living)
        {
            RecipeDef recipe = MedicalEraDefOf.Make_MedicineIndustrial;
            int target = (int)Math.Ceiling(living * MedicinePerCitizenWanted);
            IReadOnlyList<Thing> labs = map.listerThings.ThingsOfDef(MedicalEraDefOf.DrugLab);
            for (int i = 0; i < labs.Count; i++)
            {
                if (!labs[i].Spawned || (labs[i] as ThingWithComps)?.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                Bill_Production? bill = BillFor(comp, recipe);
                if (!recipe.AvailableNow || target <= 0)
                {
                    if (bill != null) comp.BillStack.Delete(bill);
                    continue;
                }
                if (bill == null)
                {
                    bill = new Bill_Production(recipe) { repeatMode = BillRepeatMode.TargetCount };
                    comp.BillStack.AddBill(bill);
                }
                bill.targetCount = target;
                bill.unpauseWhenYouHave = target - 1;
            }
        }

        private static Bill_Production? BillFor(CompBillGiver comp, RecipeDef recipe)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i] is Bill_Production production && ReferenceEquals(production.recipe, recipe)) return production;
            }
            return null;
        }

        /// <summary>Where the ward gathers: an existing hospital bed, else the compound's door, else the first bed.</summary>
        private static IntVec3? WardAnchor(Map.Map map)
        {
            IReadOnlyList<Thing> ward = map.listerThings.ThingsOfDef(MedicalEraDefOf.HospitalBed);
            for (int i = 0; i < ward.Count; i++)
            {
                if (ward[i].Spawned) return ward[i].Position;
            }
            if (map.settlementCompound != null) return map.settlementCompound.DoorCell;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < beds.Count; i++)
            {
                if (beds[i].Spawned) return beds[i].Position;
            }
            return null;
        }

        private static IntVec3? NearestHerbal(Map.Map map)
        {
            IReadOnlyList<Thing> herbal = map.listerThings.ThingsOfDef(MedicineDefOf.MedicineHerbal);
            for (int i = 0; i < herbal.Count; i++)
            {
                if (herbal[i].Spawned) return herbal[i].Position;
            }
            return null;
        }

        /// <summary>A small stockpile beside a built apothecary's bench that takes herbal medicine, so the
        /// healroot harvest is hauled within the bench's reach (<see cref="Crafting.WorkGiver_DoBill.IngredientSearchRadius"/>).</summary>
        private static void EnsureBenchStock(Map.Map map, ThingDef lab)
        {
            IReadOnlyList<Thing> built = map.listerThings.ThingsOfDef(lab);
            if (built.Count == 0 || !built[0].Spawned) return;
            Thing bench = built[0];

            string label = lab.label + " stock";
            Zone_Stockpile? pile = null;
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int z = 0; z < zones.Count; z++)
            {
                if (zones[z] is Zone_Stockpile s && s.label == label) pile = s;
            }
            if (pile == null)
            {
                pile = new Zone_Stockpile { label = label };
                pile.filter.SetDisallowAll();
                pile.filter.SetAllow(MedicineDefOf.MedicineHerbal, true);
                map.zoneManager.RegisterZone(pile);
            }

            CellRect occupied = bench.OccupiedRect();
            foreach (IntVec3 c in CellRect.CenteredOn(bench.Position, 2).ClipInsideMap(map).Cells)
            {
                if (pile.CellCount >= BenchStockCells) return;
                if (occupied.Contains(c) || !GenGrid.Standable(c, map)) continue;
                if (map.zoneManager.ZoneAt(c) != null || GenGrid.GetEdifice(c, map) != null) continue;
                if (HasBlueprintAt(map, c)) continue;
                map.zoneManager.AddCell(pile, c);
            }
        }

        private static bool HasBlueprintAt(Map.Map map, IntVec3 c)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Blueprint || here[i] is Frame) return true;
            }
            return false;
        }

        private static int LivingCitizens(Settlement settlement)
        {
            int living = 0;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (!citizens[i].Dead) living++;
            }
            return living;
        }
    }
}
