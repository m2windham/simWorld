using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <c>--suite metal</c> (crafting.materialladder): a played game read for its material ladder, the long run a
    /// 60-day bench cannot be. The storyteller suite's world — TribalStart, a populated world, the founding band,
    /// threats off unless <c>--threats true</c> — ticked for <c>--days</c>, and every <see cref="SampleEveryDays"/>
    /// days one row: the era, which ladder research is done, what ore and metal lie on the map, which furnaces
    /// stand, which smelting bills are queued, and what the citizens carry, by weapon and stuff. Then every
    /// change of era and the first day each metal appeared. A reading, not a score.
    /// </summary>
    internal static class MetalLadderSuite
    {
        private const int SampleEveryDays = 15;

        private static readonly string[] LadderResearch = { "StoneTools", "Copper", "Bronze", "BronzeWeapons", "Iron", "IronWeapons", "Steel", "SteelWeapons", "Electricity" };
        private static readonly string[] Ores = { "Flint", "CopperOre", "Tin", "IronOre" };
        private static readonly string[] Metals = { "Copper", "Bronze", "Iron", "Steel" };
        private static readonly string[] Benches = { "Smithy", "SmeltingFurnace", "Bloomery", "ElectricSmelter" };

        public static void Run(BenchOptions opt)
        {
            Bootstrap.ResetSim(opt.Seed);
            int days = opt.Days;
            var sw = Stopwatch.StartNew();

            int founded = Math.Min(opt.Band, SimWorld.World.SettlementTuning.FoundingBandRange.max);
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: opt.Solo,
                bandSize: founded,
                difficultyDef: opt.Threats ? null : new DifficultyDef
                {
                    defName = "MediumWithoutThreats",
                    threatScale = DifficultyDefOf.Medium.threatScale,
                    allowBigThreats = false,
                    allowIntroThreats = false,
                });
            SimWorld.World.Settlement home = game.CivilizationTarget.Seat
                ?? throw new InvalidOperationException("metal: the new game founded no settlement");
            GodCommands.OpenSettlement(home.tile);

            Console.WriteLine("## Material ladder — " + days + " days, seed " + opt.Seed + ", band " + opt.Band
                + ", threats " + (opt.Threats ? "on" : "off"));
            Console.WriteLine();
            Console.WriteLine("| day | era | ladder research | ore on map | metal on map | benches | smelting bills | carried (melee) | metal doors/walls |");
            Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");

            var firsts = new List<string>();
            var seen = new HashSet<string>();
            string era = Era();
            firsts.Add("day 0: era " + era);
            Row(0, home);
            for (int day = 1; day <= days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                Map.Map? map = home.InteriorMap;
                string now = Era();
                if (now != era)
                {
                    firsts.Add("day " + day + ": era " + now);
                    era = now;
                }
                if (map != null)
                {
                    foreach (string metal in Metals.Concat(Benches))
                    {
                        if (!seen.Contains(metal) && map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed(metal)).Count > 0)
                        {
                            seen.Add(metal);
                            firsts.Add("day " + day + ": first " + metal);
                        }
                    }
                    foreach (Pawn p in OutfittingInitiative.CitizensOn(map))
                    {
                        Thing? w = p.equipment?.Primary;
                        string key = w == null ? "" : "carried " + (w.Stuff?.defName ?? "-") + " " + w.def.defName;
                        if (w != null && !seen.Contains(key))
                        {
                            seen.Add(key);
                            firsts.Add("day " + day + ": first " + key);
                        }
                    }
                }
                if (day % SampleEveryDays == 0 || day == days) Row(day, home);
            }
            sw.Stop();

            Console.WriteLine();
            Console.WriteLine("### turns and firsts");
            Console.WriteLine();
            foreach (string f in firsts) Console.WriteLine("- " + f);
            Console.WriteLine();
            if (home.InteriorMap is Map.Map end) Diagnose(end);
            Console.WriteLine("ledger: deaths " + game.Storyteller.deaths + "; citizens " + home.Citizens.Count);
            Console.WriteLine(DwellingReading.Line(home));
            Console.WriteLine("### throughput");
            Console.WriteLine();
            Console.WriteLine("| days | seconds |");
            Console.WriteLine("| --- | --- |");
            Console.WriteLine("| " + days + " | " + sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " |");
        }

        private static string Era() => Find.ResearchManager.CurrentEra?.defName ?? "-";

        private static void Row(int day, SimWorld.World.Settlement home)
        {
            Map.Map? map = home.InteriorMap;
            string research = string.Join(" ", LadderResearch.Where(r => Find.ResearchManager.IsFinished(DefDatabase<ResearchProjectDef>.GetNamed(r))));
            if (map == null)
            {
                Console.WriteLine("| " + day + " | " + Era() + " | " + research + " | (no map) | | | | | |");
                return;
            }
            string ore = Counts(map, Ores);
            string metal = Counts(map, Metals);
            var benches = new List<string>();
            foreach (string b in Benches)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(b);
                int built = map.listerThings.ThingsOfDef(def).Count;
                int planned = SettlementWorksInitiative.CountBuiltOrPlanned(map, def) - built;
                if (built + planned > 0) benches.Add(b + " " + built + (planned > 0 ? "+" + planned : ""));
            }
            var bills = new List<string>();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Building))
            {
                if (t is not ThingWithComps twc || twc.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                foreach (Bill bill in comp.BillStack.Bills)
                {
                    if (bill is Bill_Production bp && (MaterialLadder.IsMetallurgy(bill.recipe) || MaterialLadder.IsLadderWeaponRecipe(bill.recipe)))
                    {
                        bills.Add(bill.recipe.defName + "→" + bp.targetCount
                            + (MaterialLadder.IsLadderWeaponRecipe(bill.recipe) ? "[" + string.Join("/", bill.ingredientFilter.AllowedThingDefs.Select(d => d.defName).OrderBy(s => s, StringComparer.Ordinal)) + "]" : ""));
                    }
                }
            }
            var carried = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Pawn p in OutfittingInitiative.CitizensOn(map))
            {
                Thing? w = p.equipment?.Primary;
                if (w == null || !MaterialLadder.IsMeleeWeapon(w.def)) continue;
                string key = (w.Stuff?.defName ?? "-") + " " + w.def.defName.Replace("MeleeWeapon_", "");
                carried[key] = (carried.TryGetValue(key, out int n) ? n : 0) + 1;
            }
            var built2 = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Building))
            {
                if ((t.def.defName == "Door" || t.def.defName == "Wall") && t.Stuff?.stuffProps?.categories?.Contains(MaterialLadderDefOf.Metallic) == true)
                {
                    string key = t.Stuff.defName + " " + t.def.defName;
                    built2[key] = (built2.TryGetValue(key, out int n) ? n : 0) + 1;
                }
            }
            Console.WriteLine("| " + day + " | " + Era() + " | " + research + " | " + ore + " | " + metal + " | "
                + string.Join(", ", benches) + " | " + string.Join(", ", bills) + " | "
                + string.Join(", ", carried.Select(kv => kv.Value + " " + kv.Key)) + " | "
                + string.Join(", ", built2.Select(kv => kv.Value + " " + kv.Key)) + " |");
        }

        /// <summary>Why a queued smelting bill is or is not being worked: the bench, each ore stack and its
        /// distance, and how many citizens could take the bill now.</summary>
        private static void Diagnose(Map.Map map)
        {
            Console.WriteLine("### smelting diagnostic");
            Console.WriteLine();
            List<Pawn> citizens = OutfittingInitiative.CitizensOn(map);
            int smiths = citizens.Count(c => c.workSettings != null && c.workSettings.WorkIsActive(WorkTypeDefOf.Smithing));
            Console.WriteLine("- citizens " + citizens.Count + ", with smithing active " + smiths);
            var veins = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            foreach (Thing rock in map.listerThings.ThingsInGroup(ThingRequestGroup.Building))
            {
                if (!rock.def.mineable || rock.def.mineableThing == null || !rock.Spawned) continue;
                string key = rock.def.mineableThing.defName;
                if (!veins.TryGetValue(key, out int[]? n)) veins[key] = n = new int[2];
                n[0]++;
                if (SimWorld.AI.WorkGiver_Miner.MayBeAccessible(rock.Position, map)) n[1]++;
            }
            Console.WriteLine("- vein cells (all/accessible): " + string.Join(", ", veins.Select(kv => kv.Key + " " + kv.Value[0] + "/" + kv.Value[1])));
            foreach (string b in Benches)
            {
                foreach (Thing bench in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed(b)))
                {
                    CompBillGiver? comp = (bench as ThingWithComps)?.GetComp<CompBillGiver>();
                    if (comp == null) continue;
                    foreach (Bill bill in comp.BillStack.Bills)
                    {
                        var bp = bill as Bill_Production;
                        int allowed = citizens.Count(c => bill.PawnAllowedToStartAnew(c));
                        Console.WriteLine("- " + b + " @" + bench.Position + " " + bill.recipe.defName + " doNow=" + (bp?.ShouldDoNow() ?? false)
                            + " suspended=" + bill.suspended + " allowedPawns=" + allowed);
                        foreach (IngredientCount ing in bill.recipe.ingredients ?? new List<IngredientCount>())
                        {
                            foreach (ThingDef d in ing.filter.AllowedThingDefs)
                            {
                                if (!bill.ingredientFilter.Allows(d)) continue;
                                foreach (Thing s in map.listerThings.ThingsOfDef(d))
                                {
                                    Zone? z = map.zoneManager.ZoneAt(s.Position);
                                    Console.WriteLine("    " + d.defName + " x" + s.stackCount + " @" + s.Position + " dist "
                                        + Math.Sqrt((s.Position - bench.Position).LengthHorizontalSquared).ToString("F1", CultureInfo.InvariantCulture)
                                        + " zone " + (z is Zone_Stockpile sp ? sp.label : z == null ? "-" : z.GetType().Name));
                                }
                            }
                        }
                    }
                }
            }
            foreach (Zone z in map.zoneManager.AllZones)
            {
                if (z is Zone_Stockpile sp && sp.label != null && sp.label.EndsWith(" stock", StringComparison.Ordinal))
                {
                    Console.WriteLine("- zone '" + sp.label + "' cells " + sp.Cells.Count + " first " + (sp.Cells.Count > 0 ? sp.Cells[0].ToString() : "-"));
                }
            }
            Console.WriteLine();
        }

        private static string Counts(Map.Map map, string[] defs) =>
            string.Join(" ", defs.Select(d => d + ":" + MaterialLadder.OnMap(map, DefDatabase<ThingDef>.GetNamed(d))));
    }
}
