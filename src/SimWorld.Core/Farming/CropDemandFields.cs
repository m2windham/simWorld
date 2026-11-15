using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.World;

namespace SimWorld.Farming
{
    /// <summary>
    /// Keeps one growing zone per <see cref="CropDemandDef"/>, sized by its worker (crafting.billcreators).
    /// Called from <c>Building.FarmingInitiative.Run</c> on the same cadence as the food field, so the
    /// non-food crops share the farmer's pass without that class knowing any of them.
    ///
    /// <para/>A field grows toward its worker's figure by at most
    /// <see cref="FarmingTuning.MaxCellsPerPass"/> a pass and never shrinks (the food field's rule); when the
    /// figure falls to zero the field stops being sown (<see cref="Zone_Growing.allowSow"/>), so what stands
    /// is harvested and nothing replaces it. Cells come from <see cref="FarmingInitiative.CanSowIn"/>, which
    /// refuses any cell already zoned, so two fields never overlap. No state of its own: the zones are
    /// the state, and the zone manager saves them.
    /// </summary>
    public static class CropDemandFields
    {
        public static void Run(Map.Map map, Settlement? settlement)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var defs = new List<CropDemandDef>(DefDatabase<CropDemandDef>.AllDefsListForReading);
            defs.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            for (int d = 0; d < defs.Count; d++)
            {
                CropDemandDef def = defs[d];
                if (def.crop?.plant == null || !def.crop.IsResearchFinished) continue;
                int wanted = def.Worker.CellsWanted(map, settlement);
                Zone_Growing? field = FieldOf(map, def.fieldLabel);
                if (wanted <= 0)
                {
                    if (field != null) field.allowSow = false;
                    continue;
                }

                if (field == null)
                {
                    field = new Zone_Growing { label = def.fieldLabel };
                    map.zoneManager.RegisterZone(field);
                }
                field.plantDefToGrow = def.crop;
                field.allowSow = true;
                if (field.CellCount < wanted)
                {
                    Grow(map, field, def.crop, Math.Min(wanted - field.CellCount, FarmingTuning.MaxCellsPerPass));
                }
            }
        }

        /// <summary>The growing zone labelled <paramref name="label"/> on <paramref name="map"/>, or null.</summary>
        public static Zone_Growing? FieldOf(Map.Map map, string label)
        {
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Growing growing && growing.label == label) return growing;
            }
            return null;
        }

        /// <summary>Adds up to <paramref name="budget"/> sowable cells, spiralling out from the field's own
        /// first cell, else the food field's, else the first humanlike on the map: crops are tended by the
        /// same farmers, so they sit together.</summary>
        private static void Grow(Map.Map map, Zone_Growing field, ThingDef crop, int budget)
        {
            IntVec3 seed = SeedCell(map, field);
            int added = 0;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count && added < budget; i++)
            {
                IntVec3 candidate = seed + pattern[i];
                if (!FarmingInitiative.CanSowIn(map, candidate, crop)) continue;
                if (map.zoneManager.AddCell(field, candidate)) added++;
            }
        }

        private static IntVec3 SeedCell(Map.Map map, Zone_Growing field)
        {
            if (field.CellCount > 0) return field.Cells[0];
            Zone_Growing? food = FarmingInitiative.FieldOf(map);
            if (food != null && food.CellCount > 0) return food.Cells[0];
            IReadOnlyList<Pawns.Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].RaceProps.Humanlike) return pawns[i].Position;
            }
            return new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
        }
    }
}
