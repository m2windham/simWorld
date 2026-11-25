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
    /// there at the first reading, less what a citizen arrived already carrying: crafted gear, plus anything a
    /// raider dropped there, which a run before the Bronze raider tier (day 300) does not have. Printed once per
    /// <see cref="ArcSuite"/> sample.
    /// </summary>
    internal sealed class ArmouryReading
    {
        private readonly HashSet<int> atStart = new HashSet<int>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private readonly HashSet<int> knownCitizens = new HashSet<int>();
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
                // Gear a newcomer walked in with was not made here.
                if (started && knownCitizens.Add(p.thingIDNumber))
                {
                    foreach (ThingWithComps a in p.apparel.WornApparel) atStart.Add(a.thingIDNumber);
                    if (p.equipment.Primary != null) atStart.Add(p.equipment.Primary.thingIDNumber);
                }
                knownCitizens.Add(p.thingIDNumber);
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

        /// <summary>What the armoury's materials come from: each ore, metal and raw material on the map (+ in the
        /// ledger), whether the settlement can get more (a reachable vein or a smelting chain), the furnaces standing
        /// and their bills. Printed under <see cref="Line"/>.</summary>
        public static string MaterialsLine(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return "materials: (settlement has no map)";
            var parts = new List<string>();
            foreach (string name in new[] { "WoodLog", "Leather_Plain", "CopperOre", "Copper", "Tin", "Bronze", "IronOre", "Iron", "Steel", "Coal" })
            {
                ThingDef? def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def == null) continue;
                parts.Add(name + " " + MaterialLadder.OnMap(map, def) + "+" + settlement.StoreCountOf(def)
                    + (Laddered(def) && !MaterialLadder.CanGet(map, def) ? " (no source)" : ""));
            }
            var benches = new List<string>();
            foreach (Thing bench in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
            {
                CompBillGiver? comp = (bench as ThingWithComps)?.GetComp<CompBillGiver>();
                if (comp == null || !bench.def.AllRecipes.Any(MaterialLadder.IsMetallurgy)) continue;
                benches.Add(bench.def.defName + "{" + string.Join(",", comp.BillStack.Bills.OfType<Bill_Production>()
                    .Select(b => b.recipe.defName + " " + b.targetCount + (b.ShouldDoNow() ? "" : " paused"))) + "}");
            }
            // Ore veins on the map by yield: cells a miner can stand beside now / all cells.
            var veins = new SortedDictionary<string, int[]>(System.StringComparer.Ordinal);
            foreach (Thing rock in map.listerThings.ThingsInGroup(ThingRequestGroup.Building))
            {
                if (!rock.Spawned || !rock.def.mineable || rock.def.mineableThing == null) continue;
                string yield = rock.def.mineableThing.defName;
                if (yield.StartsWith("Chunk", System.StringComparison.Ordinal)) continue;
                if (!veins.TryGetValue(yield, out int[]? n)) veins[yield] = n = new int[2];
                n[1]++;
                if (SimWorld.AI.WorkGiver_Miner.MayBeAccessible(rock.Position, map)) n[0]++;
            }
            return "materials: " + string.Join(", ", parts) + "; veins [" + string.Join(", ", veins.Select(v => v.Key + " " + v.Value[0] + "/" + v.Value[1]))
                + "]; furnaces [" + string.Join(" ", benches) + "]";
        }

        private static string Tally(IEnumerable<string> names) =>
            string.Join(",", names.GroupBy(n => n).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count()));

        // CanGet answers for the material ladder only (a vein, or smelting); wood and leather come from elsewhere.
        private static bool Laddered(ThingDef def) =>
            def.stuffProps?.categories?.Contains(MaterialLadderDefOf.Metallic) == true
            || DefDatabase<ThingDef>.AllDefsListForReading.Any(t => t.mineableThing == def);
    }
}
