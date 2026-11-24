using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The content-side lists the settlement levers choose from: which recipes a bench def runs, which things a
    /// stockpile filter can name, and which jobs an order may name (REQ 005 items 2, 4 and 8). Content-only and
    /// static, in the style of <c>God.View.StorytellerCatalogue</c> — none of it depends on a running game, so
    /// a host can build a picker before a bench is even placed and stop carrying its own copy of content.
    ///
    /// <para/>It lists; it does not select. Each choice is made through <see cref="MapCommands"/>, and every
    /// list comes back empty rather than throwing when no content is loaded.
    /// </summary>
    public static class LeverCatalogue
    {
        /// <summary>
        /// Every ThingDef that carries a bill-giver comp, with its <c>ThingDef.AllRecipes</c> — the recipes
        /// <see cref="MapCommands.QueueBill"/> accepts on it. Benches ordered by defName; recipes in
        /// <c>AllRecipes</c> order. Research state is per game and lives on <see cref="BenchView.Recipes"/>.
        /// </summary>
        public static IReadOnlyList<BenchRecipes> BillGivers()
        {
            var benches = new List<ThingDef>();
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsBillGiver(all[i])) benches.Add(all[i]);
            }
            benches.Sort(static (a, b) => string.CompareOrdinal(a.defName, b.defName));

            var result = new List<BenchRecipes>(benches.Count);
            for (int i = 0; i < benches.Count; i++)
            {
                result.Add(new BenchRecipes(benches[i].defName, benches[i].LabelCap, RecipeOptions(benches[i])));
            }
            return result;
        }

        /// <summary>The recipes the bench def <paramref name="benchDefName"/> runs, or empty when it is not a
        /// loaded bill-giving def.</summary>
        public static IReadOnlyList<DefOption> RecipesOf(string benchDefName)
        {
            ThingDef? def = string.IsNullOrEmpty(benchDefName)
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(benchDefName);
            if (def == null || !IsBillGiver(def)) return System.Array.Empty<DefOption>();
            return RecipeOptions(def);
        }

        /// <summary>
        /// Every thing a stockpile can hold — the defNames worth offering to
        /// <see cref="MapCommands.SetStockpileFilter"/> — ordered by defName. "Storable" is this port's own test,
        /// <c>ThingDef.EverHaulable</c> (an Item-category def), the same one hauling uses to decide what goes to
        /// a stockpile at all. Corpses are items and are included; the default stockpile filter leaves them
        /// out, and <see cref="ZoneView.Allowed"/> shows that.
        /// </summary>
        public static IReadOnlyList<DefOption> StorableThings()
        {
            var defs = new List<ThingDef>();
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].EverHaulable) defs.Add(all[i]);
            }
            defs.Sort(static (a, b) => string.CompareOrdinal(a.defName, b.defName));

            var result = new List<DefOption>(defs.Count);
            for (int i = 0; i < defs.Count; i++) result.Add(DefLabels.Option(defs[i]));
            return result;
        }

        /// <summary>
        /// The jobs meant to be ordered directly through <see cref="MapCommands.OrderJob(int, string, IntVec3)"/>
        /// / <see cref="MapCommands.OrderJob(int, string, int)"/>, each with the kind of target it takes.
        ///
        /// <para/><b>Why a list here and not a field on <see cref="JobDef"/>.</b> Content has no such field, and
        /// adding one is an edit to the shared <c>AI/JobDef.cs</c> and every JobDefs file. What makes a job
        /// orderable is a fact about its driver — it needs one target, in slot A, and nothing the order cannot
        /// supply (a bill, a bed in slot B, a carried count) — and that fact lives in the driver's code, so the
        /// list is kept beside the command that queues it. Every entry was checked against its driver:
        /// <c>Goto</c> and <c>ClearSnow</c> walk to <c>TargetIndex.A</c> as a cell; the rest act on the Thing in
        /// <c>TargetIndex.A</c>, <c>TendPatient</c> fetching medicine for itself when slot B is empty. A JobDef
        /// missing from content is left out rather than listed.
        ///
        /// <para/>This is advice for a picker, not a gate: <see cref="MapCommands.OrderJob(int, string, int)"/>
        /// still takes any JobDef, as its own doc requires.
        /// </summary>
        public static IReadOnlyList<OrderableJob> OrderableJobs()
        {
            var result = new List<OrderableJob>(Orderable.Length);
            for (int i = 0; i < Orderable.Length; i++)
            {
                (string defName, OrderTargetKind target, string hint, _) = Orderable[i];
                JobDef? def = DefDatabase<JobDef>.GetNamedSilentFail(defName);
                if (def == null) continue;
                result.Add(new OrderableJob(def.defName, def.LabelCap, target, hint));
            }
            return result;
        }

        /// <summary>
        /// <see cref="OrderableJobs()"/> less what this citizen could never do, for the order list shown beside
        /// one person. RimWorld's right-click menu offers a colonist only what they are capable of: a work type
        /// their age, backstory or traits disable is not offered (<see cref="Pawn.WorkTypeIsDisabled"/>), and a
        /// baby, or anyone who cannot move at all, is offered nothing. The second playtest offered a baby Mine,
        /// Hunt and Tend patient. Empty when no such person is on the open settlement's map.
        /// </summary>
        public static IReadOnlyList<OrderableJob> OrderableJobsFor(int citizenThingId)
        {
            var result = new List<OrderableJob>();
            Pawn? pawn = MapCommands.CitizenForOrders(citizenThingId);
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike) return result;
            if (pawn.ageTracker?.CurLifeStage?.developmentalStage == DevelopmentalStage.Baby) return result;
            if (!pawn.health.capacities.CapableOf(Health.PawnCapacityDefOf.Moving)) return result;

            for (int i = 0; i < Orderable.Length; i++)
            {
                (string defName, OrderTargetKind target, string hint, string? workTypeName) = Orderable[i];
                JobDef? def = DefDatabase<JobDef>.GetNamedSilentFail(defName);
                if (def == null) continue;
                if (workTypeName != null)
                {
                    Work.WorkTypeDef? workType = DefDatabase<Work.WorkTypeDef>.GetNamedSilentFail(workTypeName);
                    if (workType != null && pawn.WorkTypeIsDisabled(workType)) continue;
                }
                result.Add(new OrderableJob(def.defName, def.LabelCap, target, hint));
            }
            return result;
        }

        /// <summary>Each orderable job with the work type its own work giver files it under, so a person who
        /// may not do that work is not offered it; null for plain walking.</summary>
        private static readonly (string DefName, OrderTargetKind Target, string Hint, string? WorkType)[] Orderable =
        {
            ("Goto", OrderTargetKind.Cell, "a cell to walk to", null),
            ("ClearSnow", OrderTargetKind.Cell, "a snowed-in cell", "Cleaning"),
            ("Mine", OrderTargetKind.Thing, "a mineable rock", "Mining"),
            ("CutPlant", OrderTargetKind.Thing, "a plant to cut", "PlantCutting"),
            ("Harvest", OrderTargetKind.Thing, "a crop ready to harvest", "Growing"),
            ("Hunt", OrderTargetKind.Thing, "an animal to hunt", "Hunting"),
            ("TendPatient", OrderTargetKind.Thing, "a person who needs tending", "Doctor"),
            ("BeatFire", OrderTargetKind.Thing, "a fire", "Firefighter"),
            ("Clean", OrderTargetKind.Thing, "filth to clean", "Cleaning"),
            ("Repair", OrderTargetKind.Thing, "a damaged building", "Construction"),
        };

        internal static bool IsBillGiver(ThingDef def)
        {
            if (def.comps == null) return false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i] is CompProperties_BillGiver) return true;
            }
            return false;
        }

        private static IReadOnlyList<DefOption> RecipeOptions(ThingDef bench)
        {
            IReadOnlyList<RecipeDef> recipes = bench.AllRecipes;
            var result = new List<DefOption>(recipes.Count);
            for (int i = 0; i < recipes.Count; i++) result.Add(DefLabels.Option(recipes[i]));
            return result;
        }
    }
}
