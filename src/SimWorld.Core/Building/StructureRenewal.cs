using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Research;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Renewal of the town's stores and workshops (<c>docs/design/era-progression.md</c> §7, the steps after homes;
    /// renewal lane 2). Once a day, under the same waits as the homes, a store standing in an older era's form is
    /// replaced in place by the era's own (<see cref="StorageForms"/>), and a workbench is rebuilt in place in a
    /// better material once the town holds one to spare: wood, then cut stone, then iron and steel.
    /// <para/>
    /// <b>What RimWorld gives.</b> Replace in place (recalled, added around 1.3): a blueprint of a different stuff
    /// placed over a building, raised while the old one stands and swapped in when finished
    /// (<see cref="GenConstruct.PlaceReplacement"/>). A RimWorld player rebuilds a wooden bench in steel by hand; the
    /// settlement is the player here. The bench keeps its bills across the swap (<see cref="Frame.CompleteConstruction"/>),
    /// as a player's bills would have to be set again otherwise.
    /// <para/>
    /// <b>The form's era.</b> A store's era is its form's (<see cref="EraAvailability.EraOf"/>); a bench's or wall's is
    /// the later of its def's and its material's (<see cref="FormEraOrder"/>), since the material is what changes when
    /// a bench is renewed. Steel is the latest material any chain here makes, so past the Medieval age a bench or wall
    /// can renew no further: that is a production gap in the content, not something this pass can close.
    /// <para/>
    /// <b>Budget.</b> At most <c>maxUnderway</c> (<see cref="RenewalTuning.MaxUnderway"/>) stores and benches being
    /// replaced at once, separate from the homes' and the walls' budgets. A replacement is planned only when its
    /// material is held <see cref="RenewalTuning.MaterialReserve"/> times over, spare over other sites' claims, except
    /// wood, which a builder fells on demand. Stores first, then benches (§7.5's tie order), the furthest behind
    /// first, then map order. Nothing here draws from <see cref="Rand"/>.
    /// </summary>
    public static class StructureRenewal
    {
        /// <summary>Plans what is due within the budget; returns how many replacements it planned.</summary>
        public static int Renew(Map.Map map, int maxUnderway)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int underway = 0;
            var sites = new List<Thing>(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            sites.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            for (int i = 0; i < sites.Count; i++)
            {
                Building? replacing = GenConstruct.ThingToReplace(sites[i]);
                if (replacing != null && (StorageForms.IsStorage(replacing.def) || IsBench(replacing.def))) underway++;
            }
            int budget = maxUnderway - underway;
            if (budget <= 0) return 0;

            ThingDef storeForm = StorageForms.Newest();
            int storeEra = EraOrder(EraAvailability.EraOf(storeForm));
            var due = new List<(Building Old, ThingDef Def, ThingDef? Stuff, int Rank, int Behind)>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is Building b) || b.Faction == null || b.isHeritage || !b.Spawned) continue;
                if (DwellingUtility.DwellingAt(map, b.Position) != null) continue;
                if (StorageForms.IsStorage(b.def))
                {
                    int behind = storeEra - FormEraOrder(b);
                    if (behind <= 0) continue;
                    due.Add((b, storeForm, BestStuff(map, storeForm), 0, behind));
                }
                else if (IsBench(b.def))
                {
                    ThingDef? stuff = BestStuff(map, b.def);
                    if (stuff == null) continue;
                    int behind = MaterialEraOrder(stuff) - (b.Stuff == null ? 0 : MaterialEraOrder(b.Stuff));
                    if (behind <= 0) continue;
                    due.Add((b, b.def, stuff, 1, behind));
                }
            }
            due.Sort((x, y) =>
            {
                if (x.Rank != y.Rank) return x.Rank.CompareTo(y.Rank);
                if (x.Behind != y.Behind) return y.Behind.CompareTo(x.Behind);
                IntVec3 a = x.Old.Position, c = y.Old.Position;
                return a.x != c.x ? a.x.CompareTo(c.x) : a.z.CompareTo(c.z);
            });

            int planned = 0;
            for (int i = 0; i < due.Count && planned < budget; i++)
            {
                (Building old, ThingDef def, ThingDef? stuff, int _, int _) = due[i];
                if (!ReserveHolds(map, def.CostListAdjusted(stuff))) continue;
                if (GenConstruct.PlaceReplacement(def, stuff, old.Position, map) != null) planned++;
            }
            return planned;
        }

        /// <summary>A workbench renewal rebuilds in a better material: one cell, made from stuff, with bills.</summary>
        public static bool IsBench(ThingDef def) =>
            def.category == ThingCategory.Building && def.MadeFromStuff && def.size.x == 1 && def.size.z == 1
            && def.GetCompProperties<CompProperties_BillGiver>() != null;

        /// <summary>The era a standing structure's form belongs to, as an era order (the first era is 0): the later
        /// of its def's research era (<see cref="EraAvailability.EraOf"/>) and its material's
        /// (<see cref="MaterialEraOrder"/>).</summary>
        public static int FormEraOrder(Thing thing)
        {
            int own = EraOrder(EraAvailability.EraOf(thing.def));
            int made = thing.Stuff == null ? 0 : MaterialEraOrder(thing.Stuff);
            return Math.Max(own, made);
        }

        /// <summary>
        /// The era a building material belongs to, as an era order: that of the earliest recipe that makes it,
        /// by the latest era among the recipe's research; 0 when a recipe anyone can run makes it, and the
        /// material's own <see cref="EraAvailability.EraOf"/> when no recipe does (wood, dug stone: 0).
        /// <para/>
        /// Not <see cref="EraAvailability.EraOf"/> itself, which gives anything that can also be dug up no era:
        /// RimWorld maps carry compacted steel, so steel read as a first-era material and a wooden bench was
        /// never behind a steel one. For the arc steel is the smelter's, a Medieval material.
        /// </summary>
        public static int MaterialEraOrder(ThingDef stuff)
        {
            bool made = false;
            int earliest = int.MaxValue;
            IReadOnlyList<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < recipes.Count; i++)
            {
                List<ThingDefCountClass>? products = recipes[i].products;
                if (products == null || !products.Exists(p => ReferenceEquals(p.thingDef, stuff))) continue;
                int era = 0;
                List<ResearchProjectDef>? research = recipes[i].researchPrerequisites;
                for (int r = 0; research != null && r < research.Count; r++) era = Math.Max(era, EraOrder(research[r].era));
                earliest = Math.Min(earliest, era);
                made = true;
            }
            return made ? earliest : EraOrder(EraAvailability.EraOf(stuff));
        }

        /// <summary>
        /// The material to build <paramref name="def"/> of now: of the bulk materials it can be made from (a bulk
        /// metal the smiths and smelters do not want, <see cref="MaterialLadder.BestBuildingMetal"/>; cut stone held
        /// to spare; wood), the one of the latest era, the toughest on a tie. Wood always qualifies: it is felled on
        /// demand. Null when <paramref name="def"/> takes none of them.
        /// </summary>
        public static ThingDef? BestStuff(Map.Map map, ThingDef def)
        {
            if (!def.MadeFromStuff) return null;
            var options = new List<ThingDef>();
            ThingDef? metal = MaterialLadder.BestBuildingMetal(map, def);
            if (metal != null) options.Add(metal);
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef stuff = all[i];
                if (stuff.stuffProps?.categories == null || stuff.stuffProps.commonality < 1f || !GenStuff.CanMake(stuff, def)) continue;
                if (ReferenceEquals(stuff, StuffDefOf.WoodLog))
                {
                    options.Add(stuff);
                    continue;
                }
                if (stuff.stuffProps.categories.Contains(MaterialLadderDefOf.Metallic)) continue; // BestBuildingMetal's
                if (MaterialLadder.Spare(map, stuff) >= def.CostListCountFor(stuff, stuff)) options.Add(stuff);
            }
            ThingDef? best = null;
            for (int i = 0; i < options.Count; i++)
            {
                ThingDef s = options[i];
                if (best == null) { best = s; continue; }
                int es = MaterialEraOrder(s), eb = MaterialEraOrder(best);
                if (es > eb || (es == eb && (MaterialLadder.Toughness(s) > MaterialLadder.Toughness(best)
                    || (MaterialLadder.Toughness(s) == MaterialLadder.Toughness(best) && string.CompareOrdinal(s.defName, best.defName) < 0))))
                {
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Whether every material but wood is held <see cref="RenewalTuning.MaterialReserve"/> times over,
        /// spare over what other sites claim.</summary>
        internal static bool ReserveHolds(Map.Map map, List<ThingDefCountClass> cost)
        {
            for (int i = 0; i < cost.Count; i++)
            {
                if (ReferenceEquals(cost[i].thingDef, StuffDefOf.WoodLog)) continue;
                if (MaterialLadder.Spare(map, cost[i].thingDef) < (int)Math.Ceiling(cost[i].count * RenewalTuning.MaterialReserve)) return false;
            }
            return true;
        }

        private static int EraOrder(EraDef? era) => era?.order ?? 0;
    }
}
