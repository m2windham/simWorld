using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>The temperature buildings the code names. Its own <c>[DefOf]</c> class so no shared one is edited.</summary>
    [DefOf]
    public static class CoolingThingDefOf
    {
        /// <summary>RimWorld 1.0's passive cooler (<c>Buildings_Temperature.xml</c>).</summary>
        public static ThingDef PassiveCooler = null!;
    }

    /// <summary>
    /// When a settlement wants a passive cooler in its dormitory (translation: settlement temperature control).
    /// RimWorld leaves cooling to the player, who builds a passive cooler in a room once the summer or a heat
    /// wave makes it too hot. A god game has no player placing furniture, so the settlement does it: once it
    /// knows how (<c>PassiveCooler</c> research) and has a <see cref="SettlementCompound"/> to put it in, it
    /// wants one in the dormitory while a heat wave is in force or the outdoor air is above what its citizens
    /// find comfortable. It wants <see cref="CoolersWanted"/>, at <see cref="SettlementCompound.CoolerCells"/>.
    /// Once built they stay, and <see cref="WorkGiver_Refuel"/> keeps them fed with wood all year (20 logs a
    /// day for the pair), as RimWorld's would be unless the player forbade them.
    /// </summary>
    public static class SettlementCooling
    {
        /// <summary>How many passive coolers a dormitory gets: see <see cref="SettlementCompound.CoolerCells"/>
        /// for why two.</summary>
        public const int CoolersWanted = 2;

        public static bool WantsCooler(Settlement settlement, Map.Map map)
        {
            if (map.settlementCompound == null) return false;
            if (!CoolingThingDefOf.PassiveCooler.IsResearchFinished) return false;
            return IsHot(settlement, map);
        }

        /// <summary>A heat wave, or outdoor air above any citizen's comfortable maximum.</summary>
        public static bool IsHot(Settlement settlement, Map.Map map)
        {
            if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.HeatWave)) return true;
            float outdoor = map.outdoorTemperature;
            foreach (Pawn citizen in settlement.Citizens)
            {
                if (citizen.Dead) continue;
                if (outdoor > TemperatureInjuryUtility.ComfortableTemperatureRange(citizen).max) return true;
            }
            return false;
        }
    }
}
