using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The read half of the standing-rule and order levers (REQ 005): <see cref="CaptureLevers()"/> and its
    /// overloads. Its own file so the per-frame capture in <c>MapViewSnapshot.cs</c> is untouched; see
    /// <see cref="SettlementLevers"/> for why this is a separate, slower-clock capture.
    /// </summary>
    public sealed partial class MapViewSnapshot
    {
        /// <summary>The levers of whichever settlement the god has open — the same focus resolution
        /// <see cref="Capture()"/> and every <see cref="MapCommands"/> method use, so the levers read are the
        /// levers a command would set.</summary>
        public static SettlementLevers CaptureLevers()
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return AbsentLevers(reason!);
            return CaptureLevers(tile.Value);
        }

        /// <summary>The levers of the settlement on <paramref name="tile"/>.</summary>
        public static SettlementLevers CaptureLevers(int tile)
        {
            SimWorld.Map.Map? map = InteriorOf(tile, out string? reason, out _);
            if (map == null) return AbsentLevers(reason!);
            return CaptureLevers(map);
        }

        /// <summary>The levers of a map held directly — for the core's own tests and tools, as
        /// <see cref="Capture(SimWorld.Map.Map)"/> is.</summary>
        public static SettlementLevers CaptureLevers(SimWorld.Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            return new SettlementLevers(
                hasMap: true,
                absenceReason: null,
                Find.TickManager.TicksGame,
                BuildBenches(map),
                BuildZones(map),
                BuildArea(map, map.areaManager.Home),
                BuildArea(map, map.areaManager.SnowClear),
                map.medicalCare,
                BuildCitizenOrders(map));
        }

        private static SettlementLevers AbsentLevers(string reason)
        {
            var noCells = new AreaMask("", 0, 0, Array.Empty<bool>(), 0);
            return new SettlementLevers(
                hasMap: false,
                reason,
                Find.TickManager.TicksGame,
                Array.Empty<BenchView>(),
                Array.Empty<ZoneView>(),
                noCells,
                noCells,
                MedicalCareCategory.Best,
                Array.Empty<CitizenOrdersView>());
        }

        private static List<BenchView> BuildBenches(SimWorld.Map.Map map)
        {
            var benches = new List<BenchView>();
            IReadOnlyList<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is ThingWithComps twc) || !twc.Spawned) continue;
                CompBillGiver? comp = twc.GetComp<CompBillGiver>();
                if (comp == null) continue;

                IReadOnlyList<Bill> stack = comp.BillStack.Bills;
                var bills = new List<BillView>(stack.Count);
                for (int b = 0; b < stack.Count; b++) bills.Add(ViewOf(stack[b], b));

                IReadOnlyList<RecipeDef> recipeDefs = twc.def.AllRecipes;
                var recipes = new List<RecipeOption>(recipeDefs.Count);
                for (int r = 0; r < recipeDefs.Count; r++)
                {
                    RecipeDef recipe = recipeDefs[r];
                    recipes.Add(new RecipeOption(recipe.defName, recipe.LabelCap, recipe.AvailableNow));
                }

                benches.Add(new BenchView(twc.thingIDNumber, twc.def.defName, twc.def.LabelCap, twc.Position, bills, recipes));
            }
            return benches;
        }

        private static BillView ViewOf(Bill bill, int index)
        {
            var production = bill as Bill_Production;
            BillRepeatMode? mode = production?.repeatMode;
            // Not bill.ShouldDoNow(): on a TargetCount bill it rewrites `paused`, and a read must not change play.
            bool paused = production != null && production.paused;
            return new BillView(
                index,
                bill.recipe.defName,
                bill.recipe.LabelCap,
                bill.suspended,
                mode,
                mode == BillRepeatMode.RepeatCount ? production!.repeatCount : (int?)null,
                mode == BillRepeatMode.TargetCount ? production!.targetCount : (int?)null,
                paused);
        }

        private static List<ZoneView> BuildZones(SimWorld.Map.Map map)
        {
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            var views = new List<ZoneView>(zones.Count);
            for (int i = 0; i < zones.Count; i++)
            {
                Zone zone = zones[i];
                var cells = new List<IntVec3>(zone.Cells);
                switch (zone)
                {
                    case Zone_Stockpile stockpile:
                        views.Add(new ZoneView(i, ZoneKind.Stockpile, zone.label, cells, AllowedStorables(stockpile.filter), null, false));
                        break;
                    case Zone_Growing growing:
                        views.Add(new ZoneView(i, ZoneKind.Growing, zone.label, cells, Array.Empty<DefOption>(), growing.plantDefToGrow?.defName, growing.allowSow));
                        break;
                }
            }
            return views;
        }

        private static List<DefOption> AllowedStorables(ThingFilter filter)
        {
            var defs = new List<ThingDef>();
            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                // A stone-age granary does not list bronze: what the era cannot have yet is left out of the view
                // (the filter itself still allows it, so goods that do turn up are stored).
                if (def.EverHaulable && Research.EraAvailability.IsAvailableInCurrentEra(def)) defs.Add(def);
            }
            // The filter is a HashSet: sort so two captures of the same filter read identically.
            defs.Sort(static (a, b) => string.CompareOrdinal(a.defName, b.defName));
            var options = new List<DefOption>(defs.Count);
            for (int i = 0; i < defs.Count; i++) options.Add(DefLabels.Option(defs[i]));
            return options;
        }

        private static AreaMask BuildArea(SimWorld.Map.Map map, Area area)
        {
            int n = map.cellIndices.NumGridCells;
            var cells = new bool[n];
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (area[map.cellIndices.IndexToCell(i)])
                {
                    cells[i] = true;
                    count++;
                }
            }
            return new AreaMask(area.label, map.Size.x, map.Size.z, cells, count);
        }

        private static List<CitizenOrdersView> BuildCitizenOrders(SimWorld.Map.Map map)
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            var views = new List<CitizenOrdersView>();
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p.jobs == null || !p.RaceProps.Humanlike) continue;
                views.Add(new CitizenOrdersView(
                    p.thingIDNumber,
                    p.Label,
                    p.jobs.curJob?.def.defName,
                    p.jobs.curJob?.playerForced == true,
                    p.jobs.DirectedOrderCount));
            }
            return views;
        }
    }
}
