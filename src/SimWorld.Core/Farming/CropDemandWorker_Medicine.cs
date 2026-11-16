using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Farming
{
    /// <summary>
    /// Grows a medicine crop (healroot, <c>CropDemands_Medicine.xml</c>) when the settlement runs low, because
    /// there is no player to drag out a healroot field. The food field picks its crop by nutrition and a
    /// medicine crop feeds nobody, so without this the founding band's starting medicine ran down and nothing
    /// made more (measured on threat-free bands, <c>docs/perf/storyteller-pressure</c>).
    ///
    /// <para/><b>How much.</b> This port's own figures, there being no RimWorld player to port: the settlement
    /// wants <see cref="MedicinePerCitizenWanted"/> doses in hand per living citizen, and the field is one cell
    /// per dose short (<see cref="CropDemandWorker.CellsFor"/>; healroot yields one dose a plant). At or above
    /// the figure the field wants nothing, so <see cref="CropDemandFields"/> stops sowing it.
    ///
    /// <para/><b>Who.</b> Nothing is wanted until a pawn on the map can sow the crop: healroot needs Plants
    /// <see cref="PlantProperties.sowMinSkill"/>, RimWorld's own gate, and a field nobody may sow is a field
    /// nobody works.
    /// </summary>
    public class CropDemandWorker_Medicine : CropDemandWorker
    {
        /// <summary>Doses of medicine wanted in hand per citizen. This port's own; a founding band starts with
        /// about one each (<c>Scenarios.xml</c>), so "fewer than we started with" is when it sows.</summary>
        public const float MedicinePerCitizenWanted = 1f;

        public override int CellsWanted(Map.Map map, Settlement? settlement)
        {
            if (settlement == null) return 0;
            int missing = MedicineWanted(settlement) - MedicineInHand(settlement, map);
            if (missing <= 0 || !AnyoneCanSow(map, def.crop)) return 0;
            return CellsFor(missing);
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
    }
}
