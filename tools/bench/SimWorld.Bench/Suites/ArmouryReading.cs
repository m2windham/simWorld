using System.Collections.Generic;
using System.Linq;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Whether the settlement arms itself (era-arc §12.3): citizens wearing armour and what, ranged weapons held,
    /// the standing armoury bills, and how many armour pieces and ranged weapons have appeared since the run began.
    /// "Made" counts distinct things seen worn or held by a citizen or lying on the settlement's map that were not
    /// there at the first reading: crafted gear, plus anything a raider dropped there, which a run before the
    /// Bronze raider tier (day 300) does not have. Printed once per <see cref="ArcSuite"/> sample.
    /// </summary>
    internal sealed class ArmouryReading
    {
        private readonly HashSet<int> atStart = new HashSet<int>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private readonly Dictionary<string, int> madeByDef = new Dictionary<string, int>();
        private bool started;

        private static bool IsGear(ThingDef def) => Armoury.IsArmour(def) || Armoury.IsRangedWeapon(def);

        public string Line(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return "armoury: (settlement has no map)";
            List<Pawn> citizens = settlement.Citizens.Where(p => !p.Dead && p.Spawned && p.Map == map).ToList();

            var gear = new List<Thing>();
            foreach (Pawn p in citizens)
            {
                gear.AddRange(p.apparel.WornApparel.Where(a => Armoury.IsArmour(a.def)));
                if (p.equipment.Primary != null && Armoury.IsRangedWeapon(p.equipment.Primary.def)) gear.Add(p.equipment.Primary);
            }
            gear.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.Spawned && IsGear(t.def)));
            foreach (Thing t in gear)
            {
                if (!started) { atStart.Add(t.thingIDNumber); continue; }
                if (atStart.Contains(t.thingIDNumber) || !seen.Add(t.thingIDNumber)) continue;
                madeByDef.TryGetValue(t.def.defName, out int n);
                madeByDef[t.def.defName] = n + 1;
            }
            started = true;

            int armoured = citizens.Count(p => p.apparel.WornApparel.Any(a => Armoury.IsArmour(a.def)));
            string worn = Tally(citizens.SelectMany(p => p.apparel.WornApparel.Where(a => Armoury.IsArmour(a.def)).Select(a => a.def.defName)));
            string held = Tally(citizens.Where(p => p.equipment.Primary != null).Select(p => p.equipment.Primary!.def.defName));
            var bills = new List<string>();
            foreach (Thing bench in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
            {
                CompBillGiver? comp = (bench as ThingWithComps)?.GetComp<CompBillGiver>();
                if (comp == null) continue;
                foreach (Bill_Production b in comp.BillStack.Bills.OfType<Bill_Production>())
                {
                    if (Armoury.IsArmourRecipe(b.recipe) || Armoury.IsRangedRecipe(b.recipe)) bills.Add(b.recipe.defName + " " + b.targetCount + (b.ShouldDoNow() ? "" : " (paused)"));
                }
            }
            return "armoury: " + armoured + "/" + citizens.Count + " citizens in armour [" + worn + "]; held [" + held + "]; made since start "
                + madeByDef.Values.Sum() + " [" + string.Join(",", madeByDef.OrderBy(k => k.Key).Select(k => k.Key + "x" + k.Value)) + "]; bills ["
                + string.Join(", ", bills) + "]";
        }

        private static string Tally(IEnumerable<string> names) =>
            string.Join(",", names.GroupBy(n => n).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count()));
    }
}
