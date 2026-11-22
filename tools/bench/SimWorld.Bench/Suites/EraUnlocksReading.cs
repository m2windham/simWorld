using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defence;
using SimWorld.Health;
using SimWorld.Things;

namespace SimWorld.Bench.Suites
{
    /// <summary>One line on what the era has given a settlement to keep its people alive, and what it has used:
    /// medicine on the map, hospital beds and apothecary's benches, tends by medicine, the watch against the
    /// threat, citizens armed, and the trap line. Printed by <see cref="ArcSuite"/> each sample and by
    /// <see cref="StorytellerSuite"/> at the end of a run (era-unlocks).</summary>
    internal static class EraUnlocksReading
    {
        public static string Line(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return "era unlocks: (settlement has no map)";
            float threat = SettlementDefence.ThreatPointsOf(map);
            var citizens = settlement.Citizens.Where(p => !p.Dead && p.Spawned && p.Map == map).ToList();
            int armed = citizens.Count(AttackVerbUtility.HasEquippedWeapon);
            int watch = citizens.Count(SettlementDefence.IsOnWatch);
            int trapsArmed = 0, trapsSpent = 0;
            foreach (SimWorld.Defs.ThingDef def in new[] { DefenceDefOf.TrapSpike, DefenceDefOf.TrapIED })
            {
                foreach (Thing t in map.listerThings.ThingsOfDef(def))
                {
                    if ((t as ThingWithComps)?.GetComp<CompTrap>()?.armed ?? true) trapsArmed++;
                    else trapsSpent++;
                }
            }
            return "era unlocks: medicine on map herbal " + Count(map, MedicineDefOf.MedicineHerbal)
                + ", industrial " + Count(map, MedicineDefOf.MedicineIndustrial)
                + "; hospital beds " + map.listerThings.ThingsOfDef(MedicalEraDefOf.HospitalBed).Count
                + ", apothecary " + map.listerThings.ThingsOfDef(MedicalEraDefOf.DrugLab).Count
                + "; tends so far: none " + TendTally.None + ", herbal " + TendTally.Herbal + ", industrial " + TendTally.Industrial
                + ", in hospital bed " + TendTally.InHospitalBed
                + "; threat " + threat.ToString("0") + " (raid of ~" + SettlementDefence.RaidersExpected(threat) + ")"
                + ", watch " + watch + ", armed " + armed + "/" + citizens.Count
                + "; traps armed " + trapsArmed + ", spent " + trapsSpent;
        }

        private static int Count(Map.Map map, SimWorld.Defs.ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);
    }
}
