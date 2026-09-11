using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for <see cref="FarmingInitiative"/>. RimWorld has nothing to port here — its player drags out
    /// every growing zone by hand and decides how big it needs to be — so every figure is this port's own,
    /// documented at its declaration and pinned by behaviour (a bigger settlement wants a bigger field; a
    /// field big enough that the harvest outruns the mouths) rather than trusted as a literal, per CLAUDE.md.
    /// </summary>
    public static class FarmingTuning
    {
        /// <summary>Self-gate cadence, the same rare bucket every other initiative in this codebase uses. A
        /// field is not urgent on any one tick; it is urgent that it exists before the wild food runs out.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// Cells added to the field per gated pass. A settlement grows its field over many ticks rather than
        /// painting the whole thing at once — the same shape <c>Economy.SettlementStockInitiative</c>'s
        /// granary and <see cref="SettlementConstructionInitiative"/>'s blueprints both use, and for the same
        /// reason: work has to keep up with the plan or the plan is a list of things nobody is doing.
        /// </summary>
        public const int MaxCellsPerPass = 60;

        /// <summary>
        /// Margin on the field size, over the arithmetic that says exactly how many cells feed the mouths.
        /// <see cref="Plant.TickLong"/> grows a crop at fertility × light × temperature, and every one of
        /// those is below 1 for most of a real day on real ground, so a field sized at exactly the
        /// break-even figure grows slower than that figure assumed and the settlement starves while standing
        /// in its own crop. Also covers what the harvest loses to
        /// <c>Director.DifficultyUtility.CropYieldFactor</c> and to a plant cut before it was ripe.
        /// </summary>
        public const float FieldSizeMargin = 3f;
    }

    /// <summary>
    /// Translation: <b>a settlement clears and sows its own fields</b>, because there is no player to drag a
    /// growing zone out on the map.
    ///
    /// <para/><b>The gap this closes, and it is the same one this register keeps finding.</b>
    /// <see cref="Zone_Growing"/>, <see cref="WorkGiver_GrowerSow"/>, <see cref="WorkGiver_GrowerHarvest"/>,
    /// <see cref="JobDriver_Sow"/>, <see cref="JobDriver_Harvest"/>, <see cref="Plant"/>'s whole
    /// fertility × light × temperature growth model and two crop ThingDefs were all built, all tested and all
    /// green. <b>Nothing in <c>src/</c> had ever created a <see cref="Zone_Growing"/></b> — every one in the
    /// repository was made by a test — so <c>WorkGiver_GrowerSow.PotentialWorkCellsGlobal</c> iterated an
    /// empty list in every game that has ever run and no citizen anywhere has ever sown a seed. That is
    /// exactly the shape <c>Economy.SettlementStockInitiative</c> found for the stockpile zone and
    /// <c>Crafting.StonecutterInitiative</c> found for bills: the mechanism was finished and nobody wanted
    /// anything.
    ///
    /// <para/><b>Why it matters more than it looks.</b> Wild food (<see cref="WildFoodTuning"/>) is what a
    /// band eats while it is founding a settlement; what regrows on its own is nowhere near what a town
    /// burns. Farming is the only source in this port whose output scales with how much work the settlement
    /// puts in rather than with how much wilderness happens to be nearby, so it is the thing that has to be
    /// carrying a settlement by the time the standing wild stock is gone.
    ///
    /// <para/><b>How big a field.</b> Derived, not chosen: the mouths on one side
    /// (<see cref="HuntingInitiative.NutritionWanted"/>'s own per-eater rate, so the food economy has exactly
    /// one demand figure and not three that can drift) and, on the other, what one cell of the chosen crop
    /// actually yields per day — <c>harvestYield × the harvested thing's nutrition ÷ growDays</c>, read off
    /// the content rather than restated here. A margin covers the fact that nothing on a real map grows at
    /// rate 1. See <see cref="FieldCellsWantedFor"/>.
    ///
    /// <para/><b>Which crop.</b> Whichever sowable plant content says feeds people best per cell per day and
    /// whose <see cref="PlantProperties.sowMinFertility"/> the ground can actually meet — the same "read it
    /// off the content that already says so" rule <c>StonecutterInitiative.IsStonecutting</c> follows. Ship a
    /// third crop and this finds it with no code change.
    ///
    /// <para/><b>No state of its own.</b> Everything is re-derived every gated pass from the zone standing on
    /// the map, so there is nothing here to Scribe and a loaded save resumes mid-field with no catch-up step.
    /// </summary>
    public static class FarmingInitiative
    {
        /// <summary>The label a settlement's own field carries, so a second pass finds the one it painted
        /// last time rather than starting another.</summary>
        public const string FieldLabel = "Fields";

        // -------------------------------------------------------------------------------------------
        // Entry points — the same shape every other initiative here uses.
        // -------------------------------------------------------------------------------------------

        /// <summary>Civilization-wide entry point. A silent no-op with no world running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements) TickSettlement(settlement);
        }

        /// <summary>One settlement, reading its own <see cref="Settlement.InteriorMap"/> — null (never
        /// entered) is a real, expected state and a no-op, never a triggered generation.</summary>
        public static void TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            TickMap(map);
        }

        /// <summary>One map, self-gating on <see cref="FarmingTuning.IntervalTicks"/> — the single place
        /// every entry point funnels through, so the interval check has one home. Called once per map per
        /// tick from <see cref="Map.Map.MapTick"/>.</summary>
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % FarmingTuning.IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated logic. Public so a test can drive one pass without arranging for the tick
        /// number to land on the interval.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Settlement? settlement = HuntingInitiative.SettlementFor(map);
            int wanted = FieldCellsWantedFor(settlement, map);
            if (wanted <= 0) return;

            ThingDef? crop = CropFor(map);
            if (crop == null) return;

            Zone_Growing field = FieldOf(map) ?? NewField(map);

            // Re-stated every pass rather than only at creation: a crop that becomes available later (a
            // research finishing, a better one shipped in content) should take over the field the settlement
            // already has, and a field that somehow lost its crop should get one back.
            field.plantDefToGrow = crop;
            field.allowSow = true;

            if (field.CellCount >= wanted) return;
            GrowField(map, field, crop, Math.Min(wanted - field.CellCount, FarmingTuning.MaxCellsPerPass));
        }

        // -------------------------------------------------------------------------------------------
        // How big, and of what.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Cells of field this settlement wants: enough that the crop's own yield per cell per day covers
        /// what its eaters burn per day, times <see cref="FarmingTuning.FieldSizeMargin"/>. Zero when there
        /// is nobody to feed or no crop worth sowing — a settlement with no mouths does not farm.
        /// </summary>
        public static int FieldCellsWantedFor(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            ThingDef? crop = CropFor(map);
            if (crop == null) return 0;

            float perCellPerDay = NutritionPerCellPerDay(crop);
            if (perCellPerDay <= 0f) return 0;

            // The one demand figure the whole food economy shares (hunting's gate reads the same per-eater
            // rate), divided back out of "days wanted in hand" into "burned per day".
            float wantedInHand = HuntingInitiative.NutritionWanted(settlement, map);
            if (wantedInHand <= 0f) return 0;
            float burnedPerDay = wantedInHand / HuntingTuning.DaysOfFoodWanted;

            int breakEven = (int)Math.Ceiling(burnedPerDay / perCellPerDay);
            return (int)Math.Ceiling(breakEven * FarmingTuning.FieldSizeMargin);
        }

        /// <summary>Nutrition one sown cell of <paramref name="crop"/> returns per day once it is cycling —
        /// yield × the harvested thing's nutrition, over the days it takes to grow. Read entirely off
        /// content.</summary>
        public static float NutritionPerCellPerDay(ThingDef crop)
        {
            if (crop == null) throw new ArgumentNullException(nameof(crop));
            PlantProperties? props = crop.plant;
            ThingDef? harvested = props?.harvestedThingDef;
            if (props == null || harvested == null || props.harvestYield <= 0) return 0f;
            if (!harvested.IsNutritionGivingIngestible) return 0f;

            float growDays = props.growDays > 0f ? props.growDays : 0.01f;
            return props.harvestYield * harvested.ingestible!.nutrition / growDays;
        }

        /// <summary>
        /// The crop this map should sow: the sowable, food-bearing plant with the best nutrition per cell per
        /// day whose <see cref="PlantProperties.sowMinFertility"/> at least one cell on the map can meet.
        /// Ties break by defName so two runs of the same state choose the same crop — determinism is a
        /// feature, and this is the one place here that could otherwise follow Def load order.
        /// </summary>
        public static ThingDef? CropFor(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            float bestFertility = BestFertilityOn(map);
            ThingDef? best = null;
            float bestYield = 0f;

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def.category != ThingCategory.Plant || def.plant == null) continue;
                if (def.plant.sowMinFertility > bestFertility) continue;

                float yield = NutritionPerCellPerDay(def);
                if (yield <= 0f) continue;
                if (yield > bestYield || (yield == bestYield && best != null && string.CompareOrdinal(def.defName, best.defName) < 0))
                {
                    best = def;
                    bestYield = yield;
                }
            }
            return best;
        }

        /// <summary>The most fertile ground this map has anywhere — what decides which crops are even
        /// candidates. A whole-map scan, but only over the terrain grid and only on a gated tick.</summary>
        private static float BestFertilityOn(Map.Map map)
        {
            float best = 0f;
            for (int x = 0; x < map.Size.x; x++)
            {
                for (int z = 0; z < map.Size.z; z++)
                {
                    float fertility = map.terrainGrid.TerrainAt(new IntVec3(x, 0, z)).fertility;
                    if (fertility > best) best = fertility;
                }
            }
            return best;
        }

        // -------------------------------------------------------------------------------------------
        // The field itself.
        // -------------------------------------------------------------------------------------------

        /// <summary>This settlement's field, or null when it has not painted one yet.</summary>
        public static Zone_Growing? FieldOf(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Growing growing && growing.label == FieldLabel) return growing;
            }
            return null;
        }

        private static Zone_Growing NewField(Map.Map map)
        {
            var field = new Zone_Growing { label = FieldLabel };
            map.zoneManager.RegisterZone(field);
            return field;
        }

        /// <summary>
        /// Adds up to <paramref name="budget"/> sowable cells to the field, spiralling outward from where
        /// the field already is (or, for a brand-new one, from where the citizens are) so a settlement's
        /// fields are one worked block rather than a scatter of single cells across a 200×200 map that
        /// nobody can walk between. <see cref="GenRadial.RadialPattern"/> in order rather than random
        /// sampling: there is no need to spend the shared <see cref="Rand"/> stream on a decision with a
        /// perfectly good deterministic answer.
        /// </summary>
        private static void GrowField(Map.Map map, Zone_Growing field, ThingDef crop, int budget)
        {
            if (budget <= 0) return;

            IntVec3 seed = field.CellCount > 0 ? field.Cells[0] : FieldSeedCell(map);
            int added = 0;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count && added < budget; i++)
            {
                IntVec3 candidate = seed + pattern[i];
                if (!CanSowIn(map, candidate, crop)) continue;
                if (map.zoneManager.AddCell(field, candidate)) added++;
            }
        }

        /// <summary>Where a settlement with no field yet starts one: the first citizen standing on the map,
        /// falling back to the map's middle. Not a random cell — a field the town has to cross the map to
        /// reach is a field nobody works.</summary>
        private static IntVec3 FieldSeedCell(Map.Map map)
        {
            IReadOnlyList<Pawns.Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].RaceProps.Humanlike) return pawns[i].Position;
            }
            return new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
        }

        /// <summary>
        /// Whether a cell may join the field: open, unroofed, unzoned ground fertile enough for the crop.
        /// Deliberately does <b>not</b> require the cell to be empty of plants — wild growth standing in an
        /// actively-sowing zone is precisely what <c>AI.WorkGiver_PlantsCut</c> exists to clear, so a field
        /// painted over scrub is a field the settlement clears itself rather than one it refuses to plant.
        /// </summary>
        public static bool CanSowIn(Map.Map map, IntVec3 cell, ThingDef crop)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (crop?.plant == null) return false;
            if (!GenGrid.InBounds(cell, map) || !GenGrid.Standable(cell, map)) return false;
            if (map.zoneManager.ZoneAt(cell) != null) return false;
            if (map.roofGrid.Roofed(cell)) return false;
            if (map.edificeGrid[cell] != null) return false;

            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain.IsWater) return false;
            return terrain.fertility >= crop.plant.sowMinFertility;
        }
    }
}
