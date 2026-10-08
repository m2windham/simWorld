using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for the settlement's herb garden (<see cref="FarmingInitiative.RunHerbGarden"/>). RimWorld has
    /// nothing to port -- its player picks every growing zone's plant -- so each figure is this port's own and
    /// is pinned by behaviour (a bigger settlement wants a bigger garden; a better medicine crop wants less
    /// ground) rather than trusted as a literal, per CLAUDE.md.
    /// </summary>
    public static class FarmingMedicineTuning
    {
        /// <summary>
        /// Days the garden is sized to take to bring the settlement's wanted medicine stock back from empty,
        /// at the crop's nominal rate of growth. <b>A design number, not RimWorld's.</b> A month is long
        /// enough that the garden stays a modest patch next to the food field (its cells are what one citizen
        /// can sow and tend between meals) and short enough that a settlement which has just spent its stock
        /// is not defenceless for a season. The size it implies comes out of the content: how many cells a
        /// stock of <see cref="SettlementMedicineTuning.MedicineWantedPerCitizen"/> per head takes to grow
        /// back in this many days, given the crop's own yield and grow time, times the same
        /// <see cref="FarmingTuning.FieldSizeMargin"/> the food field is given, for the same reason.
        /// </summary>
        public const float RestockDays = 30f;
    }

    /// <summary>
    /// Translation: <b>a settlement that is running out of medicine grows some</b>. In RimWorld the player
    /// drags out a growing zone and picks healroot for it; here there is no player at the wheel, and
    /// <see cref="FarmingInitiative"/> only ever chose a food crop, so the one plant that makes herbal medicine
    /// was never in the ground and a founding band's medicine only ever went down.
    ///
    /// <para/><b>The rule, and nothing smaller would do.</b> A settlement paints a second, separate zone --
    /// the <see cref="HerbGardenLabel"/> -- and sows the medicine crop in it when <i>all</i> of these hold:
    /// <list type="number">
    /// <item><b>Food is provided for.</b> The food field has reached the size the mouths need (or has nowhere
    /// left to grow), and the settlement holds at least the larder it wants in hand
    /// (<c>HuntingInitiative.NutritionWanted</c> against <c>NutritionAvailable</c>, both books). Food always
    /// wins: while food is short the garden neither grows nor is sown (<see cref="Zone_Growing.allowSow"/> is
    /// switched off, so sowers' hands are on the food field), and a settlement that is starving never gives
    /// ground to a herb.</item>
    /// <item><b>It is short of medicine</b> -- medicine in hand, map and ledger together, is under
    /// <see cref="SettlementMedicineTuning.MedicineWantedPerCitizen"/> per citizen
    /// (<see cref="SettlementMedicine.IsShort"/>). A settlement with a full medicine chest plants none.</item>
    /// <item><b>Somebody can sow it</b> -- at least one living, standing citizen with the Growing work type
    /// on and the Plants skill the crop asks for (<see cref="PlantProperties.sowMinSkill"/>; RimWorld's own
    /// <c>Command_SetPlantToGrow.WarnAsAppropriate</c> asks the player the same question). Painting a garden
    /// nobody is allowed to sow would put cells in the ground that are just labelled.</item>
    /// </list>
    ///
    /// <para/><b>How big.</b> A modest patch, sized the way the food field is: derived from content. The
    /// medicine the settlement wants in hand (<see cref="SettlementMedicine.Wanted"/>), over what one cell of
    /// the crop returns per day (<c>harvestYield / growDays</c>) across
    /// <see cref="FarmingMedicineTuning.RestockDays"/>, times <see cref="FarmingTuning.FieldSizeMargin"/>. It
    /// only ever grows while the settlement is short, and it is never shrunk or cleared: once planted it is
    /// harvested and re-sown like any field, and what it makes beyond the settlement's own wants is banked
    /// like any other surplus. So the garden's size follows the settlement's need up and stays put.
    ///
    /// <para/><b>Which crop.</b> The sowable plant whose harvest is medicine, found the way
    /// <see cref="FarmingInitiative.CropFor"/> finds the food crop: read off content
    /// (<see cref="FarmingInitiative.MedicineCropFor"/>), so a second medicine plant would be found with no
    /// code change. It cannot be chosen as a <i>food</i> crop -- medicine has no nutrition -- which is why it
    /// needs its own rule at all.
    ///
    /// <para/><b>The player and this zone.</b> The garden is the settlement's own, found again by label, and
    /// <c>MapCommands.SetZonePlant</c> relabels any settlement-owned zone it changes the plant of, handing it
    /// to the player: the settlement then lays out its own again if it still needs one. So a player's choice
    /// is never quietly put back on the next pass. The same adoption applies to the food field.
    ///
    /// <para/><b>No state of its own.</b> Everything is re-derived from the zone, the map and the ledger each
    /// gated pass, so there is nothing here to Scribe beyond what <see cref="Zone_Growing"/> already saves.
    /// </summary>
    public static partial class FarmingInitiative
    {
        /// <summary>The label the settlement's herb garden carries, so a later pass finds the one it painted
        /// rather than starting another. A distinct zone from <see cref="FieldLabel"/>'s: the food field
        /// keeps meaning what it meant.</summary>
        public const string HerbGardenLabel = "Herb garden";

        /// <summary>This settlement's herb garden, or null when it has not painted one.</summary>
        public static Zone_Growing? HerbGardenOf(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Growing growing && growing.label == HerbGardenLabel) return growing;
            }
            return null;
        }

        /// <summary>
        /// The sowable plant this map should grow for medicine: the plant whose harvest is medicine
        /// (<c>ThingDef.IsMedicine</c>) with the best yield per cell per day whose
        /// <see cref="PlantProperties.sowMinFertility"/> some cell on the map can meet. Ties break by defName,
        /// for the reason <see cref="CropFor"/> does. Null when content ships none or the ground is too poor.
        /// </summary>
        public static ThingDef? MedicineCropFor(Map.Map map)
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

                float yield = MedicinePerCellPerDay(def);
                if (yield <= 0f) continue;
                if (yield > bestYield || (yield == bestYield && best != null && string.CompareOrdinal(def.defName, best.defName) < 0))
                {
                    best = def;
                    bestYield = yield;
                }
            }
            return best;
        }

        /// <summary>Units of medicine one sown cell of <paramref name="crop"/> returns per day once it is
        /// cycling -- yield over grow days, read off content. 0 for a plant whose harvest is not
        /// medicine.</summary>
        public static float MedicinePerCellPerDay(ThingDef crop)
        {
            if (crop == null) throw new ArgumentNullException(nameof(crop));
            PlantProperties? props = crop.plant;
            ThingDef? harvested = props?.harvestedThingDef;
            if (props == null || harvested == null || props.harvestYield <= 0) return 0f;
            if (!harvested.IsMedicine) return 0f;

            float growDays = props.growDays > 0f ? props.growDays : 0.01f;
            return props.harvestYield / growDays;
        }

        /// <summary>
        /// Cells of herb garden this settlement wants: its wanted stock of medicine, over what a cell of
        /// <paramref name="crop"/> yields in <see cref="FarmingMedicineTuning.RestockDays"/>, times
        /// <see cref="FarmingTuning.FieldSizeMargin"/>. Zero with nobody to doctor or a crop that makes none.
        /// </summary>
        public static int HerbGardenCellsWantedFor(Settlement? settlement, Map.Map map, ThingDef crop)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (crop == null) throw new ArgumentNullException(nameof(crop));

            float perCellPerDay = MedicinePerCellPerDay(crop);
            if (perCellPerDay <= 0f) return 0;

            int wantedStock = SettlementMedicine.Wanted(settlement, map);
            if (wantedStock <= 0) return 0;

            int breakEven = (int)Math.Ceiling(wantedStock / (FarmingMedicineTuning.RestockDays * perCellPerDay));
            return (int)Math.Ceiling(breakEven * FarmingTuning.FieldSizeMargin);
        }

        /// <summary>
        /// Whether the settlement is short of food in the sense that outranks medicine: it holds, map and
        /// ledger together, less than the larder it wants in hand. The honest total
        /// (<c>HuntingInitiative.NutritionAvailable</c>), not what lies within reach -- the reachable figure
        /// flickers across its line every time a meal is eaten and issued back, and a garden that switched on
        /// and off with it would never be sown.
        /// </summary>
        public static bool FoodIsShort(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return HuntingInitiative.NutritionAvailable(settlement, map) < HuntingInitiative.NutritionWanted(settlement, map);
        }

        /// <summary>
        /// Whether anybody in the settlement could sow <paramref name="crop"/> at all: a living citizen who is
        /// not downed, has the Growing work type switched on, and has the Plants skill the crop asks for. On a
        /// map no settlement owns, the humanlike pawns standing on it.
        /// </summary>
        public static bool AnyoneCanSow(Settlement? settlement, Map.Map map, ThingDef crop)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (crop?.plant == null) return false;

            if (settlement != null)
            {
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int i = 0; i < citizens.Count; i++)
                {
                    if (CanSow(citizens[i], crop)) return true;
                }
                return false;
            }

            IReadOnlyList<Pawn> onMap = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < onMap.Count; i++)
            {
                if (onMap[i].RaceProps.Humanlike && CanSow(onMap[i], crop)) return true;
            }
            return false;
        }

        private static bool CanSow(Pawn pawn, ThingDef crop)
        {
            if (pawn.Dead || pawn.Downed) return false;
            Pawn_WorkSettings? work = pawn.workSettings;
            if (work == null || !work.EverWork || !work.WorkIsActive(WorkTypeDefOf.Growing)) return false;
            return crop.plant!.PawnMeetsSowMinSkill(pawn);
        }

        /// <summary>
        /// One gated pass of the herb garden, after the food field has been seen to
        /// (<see cref="Run"/>): keeps an existing garden's sowing in step with the food situation, and decides
        /// whether to start or grow one. See the class doc for the rule. Public so a test can drive it without
        /// arranging for a whole field first.
        /// </summary>
        /// <param name="field">The settlement's food field -- where a new garden's cells are searched outward
        /// from, so the garden sits against the field rather than in a corner of the map.</param>
        public static void RunHerbGarden(Map.Map map, Settlement? settlement, Zone_Growing field)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (field == null) throw new ArgumentNullException(nameof(field));

            bool foodShort = FoodIsShort(settlement, map);

            // Food wins at the sower's hands as well as in the ground: with food short the garden stops being
            // sown, so every citizen who sows is working the food field. Harvesting is not touched -- a ripe
            // healroot is still picked.
            Zone_Growing? garden = HerbGardenOf(map);
            if (garden != null) garden.allowSow = !foodShort;

            if (foodShort) return;
            if (!SettlementMedicine.IsShort(settlement, map)) return;

            ThingDef? crop = MedicineCropFor(map);
            if (crop == null) return;
            if (!AnyoneCanSow(settlement, map, crop)) return;

            int wanted = HerbGardenCellsWantedFor(settlement, map, crop);
            if (wanted <= 0) return;

            if (garden == null)
            {
                garden = new Zone_Growing { label = HerbGardenLabel };
                map.zoneManager.RegisterZone(garden);
            }
            garden.plantDefToGrow = crop;
            garden.allowSow = true;

            if (garden.CellCount >= wanted) return;
            IntVec3 seed = garden.CellCount > 0 ? garden.Cells[0]
                : field.CellCount > 0 ? field.Cells[0]
                : FieldSeedCell(map);
            GrowZone(map, garden, crop, Math.Min(wanted - garden.CellCount, FarmingTuning.MaxCellsPerPass), seed);
        }

        /// <summary>Adds up to <paramref name="budget"/> cells fit for <paramref name="crop"/> to
        /// <paramref name="zone"/>, spiralling outward from <paramref name="seed"/> -- the shape
        /// <c>GrowField</c> gives the food field, with the seed chosen by the caller.</summary>
        private static void GrowZone(Map.Map map, Zone_Growing zone, ThingDef crop, int budget, IntVec3 seed)
        {
            if (budget <= 0) return;

            int added = 0;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count && added < budget; i++)
            {
                IntVec3 candidate = seed + pattern[i];
                if (!CanSowIn(map, candidate, crop)) continue;
                if (map.zoneManager.AddCell(zone, candidate)) added++;
            }
        }
    }
}
