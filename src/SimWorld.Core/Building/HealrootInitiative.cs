using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Translation: <b>a settlement sows its own medicine when it runs low</b>, because there is no player
    /// to drag out a healroot field. The medicine half of <see cref="FarmingInitiative"/>, and the same
    /// translation: RimWorld's player paints a growing zone and picks healroot for it; here the settlement
    /// does, on a need it can read.
    ///
    /// <para/><b>The gap this closes.</b> A founding band starts with herbal medicine (the scenario's stock)
    /// and nothing ever made more: <see cref="FarmingInitiative.CropFor"/> picks the crop that feeds people
    /// best, and a medicine crop feeds nobody, so it could never be chosen. Measured on a threat-free band
    /// (<c>docs/perf/storyteller-pressure</c>), the stock ran down to nothing and every infection after that
    /// was untended. RimWorld's remedy is healroot (<c>Plants_Healroot.xml</c>); this is what plants it.
    ///
    /// <para/><b>When, and how much.</b> This port's own figures, there being no RimWorld player to port:
    /// the settlement wants <see cref="MedicinePerCitizenWanted"/> doses in hand per citizen. Below that it
    /// keeps a healroot field of one cell per dose wanted (healroot yields one dose a plant) and lets it be
    /// sown; at or above it the field stays but nothing new is sown, so a stocked settlement is not tying up
    /// a grower. The field is only painted once somebody can sow it — healroot needs Plants
    /// <see cref="PlantProperties.sowMinSkill"/>, RimWorld's own gate — since a field nobody may sow is a field
    /// nobody works.
    ///
    /// <para/><b>No state of its own</b>, like <see cref="FarmingInitiative"/>: everything is re-derived from
    /// the zone on the map, so nothing is saved.
    /// </summary>
    public static class HealrootInitiative
    {
        /// <summary>The label the settlement's medicine field carries.</summary>
        public const string FieldLabel = "Healroot";

        /// <summary>Doses of medicine wanted in hand per citizen. This port's own; a founding band starts
        /// at about one each (<c>ScenarioDefs</c>), so "fewer than we started with" is when it sows.</summary>
        public const float MedicinePerCitizenWanted = 1f;

        /// <summary>Cells added per pass; the same pacing reason as <see cref="FarmingTuning.MaxCellsPerPass"/>.</summary>
        public const int MaxCellsPerPass = 20;

        /// <summary>Gated with <see cref="FarmingInitiative.TickMap"/>, which calls this once per rare
        /// interval.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Run(map, HuntingInitiative.SettlementFor(map));
        }

        /// <summary>The ungated logic for a known settlement; public so a test needs no world.</summary>
        public static void Run(Map.Map map, Settlement? settlement)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (settlement == null) return;

            ThingDef? crop = CropFor(map);
            if (crop == null) return;

            int wanted = MedicineWanted(settlement);
            if (wanted <= 0) return;
            bool low = MedicineInHand(settlement, map) < wanted;

            Zone_Growing? field = FieldOf(map);
            if (field == null)
            {
                if (!low || !AnyoneCanSow(map, crop)) return;
                field = new Zone_Growing { label = FieldLabel };
                map.zoneManager.RegisterZone(field);
            }

            field.plantDefToGrow = crop;
            field.allowSow = low;
            if (!low || field.CellCount >= wanted) return;
            GrowField(map, field, crop, Math.Min(wanted - field.CellCount, MaxCellsPerPass));
        }

        /// <summary>Doses this settlement wants in hand.</summary>
        public static int MedicineWanted(Settlement settlement)
        {
            int living = 0;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (!citizens[i].Dead) living++;
            }
            return (int)Math.Ceiling(living * MedicinePerCitizenWanted);
        }

        /// <summary>Medicine the settlement holds: what is on its map plus what is in its ledger.</summary>
        public static int MedicineInHand(Settlement settlement, Map.Map map)
        {
            int count = 0;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].def.IsMedicine) count += items[i].stackCount;
            }
            foreach (KeyValuePair<ThingDef, int> stored in settlement.Stores)
            {
                if (stored.Key.IsMedicine) count += stored.Value;
            }
            return count;
        }

        /// <summary>The sowable plant whose harvest is medicine and gives the most per cell per day, among
        /// those the map's best ground can carry. Read off content, as <see cref="FarmingInitiative.CropFor"/>
        /// reads food crops; ties break by defName.</summary>
        public static ThingDef? CropFor(Map.Map map)
        {
            float bestFertility = 0f;
            foreach (IntVec3 c in map.AllCells)
            {
                float f = map.terrainGrid.TerrainAt(c).fertility;
                if (f > bestFertility) bestFertility = f;
            }

            ThingDef? best = null;
            float bestRate = 0f;
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                PlantProperties? props = def.plant;
                if (def.category != ThingCategory.Plant || props?.harvestedThingDef == null) continue;
                if (!props.harvestedThingDef.IsMedicine || props.harvestYield <= 0) continue;
                if (props.sowMinFertility > bestFertility) continue;
                float rate = props.harvestYield / Math.Max(props.growDays, 0.01f);
                if (rate > bestRate || (rate == bestRate && best != null && string.CompareOrdinal(def.defName, best.defName) < 0))
                {
                    best = def;
                    bestRate = rate;
                }
            }
            return best;
        }

        /// <summary>This settlement's medicine field, or null.</summary>
        public static Zone_Growing? FieldOf(Map.Map map)
        {
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Growing growing && growing.label == FieldLabel) return growing;
            }
            return null;
        }

        private static bool AnyoneCanSow(Map.Map map, ThingDef crop)
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || !p.RaceProps.Humanlike) continue;
                if (WorkGiver_GrowerSow.CanSow(p, crop)) return true;
            }
            return false;
        }

        /// <summary>Spirals outward from the field (or, for a new one, from the first citizen standing on the
        /// map) adding sowable cells — the same shape <see cref="FarmingInitiative"/> grows its field in, so
        /// the healroot sits beside the town rather than across the map.</summary>
        private static void GrowField(Map.Map map, Zone_Growing field, ThingDef crop, int budget)
        {
            IntVec3 seed = field.CellCount > 0 ? field.Cells[0] : SeedCell(map);
            int added = 0;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count && added < budget; i++)
            {
                IntVec3 candidate = seed + pattern[i];
                if (!FarmingInitiative.CanSowIn(map, candidate, crop)) continue;
                if (map.zoneManager.AddCell(field, candidate)) added++;
            }
        }

        private static IntVec3 SeedCell(Map.Map map)
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].RaceProps.Humanlike) return pawns[i].Position;
            }
            return new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
        }
    }
}
