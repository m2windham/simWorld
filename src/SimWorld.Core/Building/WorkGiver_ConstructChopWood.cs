using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Fells a tree for wood the settlement's own building sites are waiting on, and clears a tree standing on
    /// one (translation of RimWorld's Chop Wood designation; see below).
    /// <para/>
    /// <b>The defect this closes.</b> <see cref="SettlementConstructionInitiative"/> places a bed blueprint
    /// for every citizen, and every one of them waited forever: a bed costs wood, and nothing a settlement did
    /// could ever produce any. Measured on the storyteller bench's populated world, seed 777 had no wood on its
    /// map, 25 bed blueprints on day 1 and 25 on day 30, none ever framed; seed 12345 had 52 wood of ruin loot,
    /// which built one bed and part-framed two storage huts and one other building on day 1 (three failed bed
    /// frames lost another twelve to half refunds), after which nothing more was framed for the rest of the
    /// run. Placement was never the failing half.
    /// <para/>
    /// <b>What RimWorld does, and what this translates.</b> Wood comes from trees
    /// (<c>MapGen.GenStep_Trees</c> now places them), felled by a <c>PlantCutting</c> worker through the
    /// player's Chop Wood designation. There is no Chop Wood designation here and no player placing blueprints
    /// either: <see cref="SettlementConstructionInitiative"/> is the settlement deciding what to build, and
    /// this is the same settlement deciding which trees to fell for it. The designation is replaced by the
    /// reason a player would have had for it, the one <see cref="WorkGiver_PlantsCut"/> already takes for the
    /// designations it translated: <b>the building sites on this map need more wood than is on its way to
    /// them.</b> See <see cref="WoodShortfall"/>. With no shortfall it fells nothing, so a settlement with
    /// nothing to build leaves its forest standing.
    /// <para/>
    /// <b>Why under Construction and not PlantCutting.</b> RimWorld's chop is <c>PlantCutting</c> work
    /// (natural priority 650), below <c>Mining</c> (700). When this was written this port's
    /// <see cref="AI.WorkGiver_Miner"/> had no designations and so mined every reachable rock on the map; with
    /// the same default priorities a citizen who could mine never ran out of mining and never reached a
    /// <c>PlantCutting</c> giver. (It reads <c>Mine</c> marks now and runs out of work all the time; the
    /// reasoning that follows stands regardless.) The fell
    /// is therefore the builder's, just below delivering materials, which is also where RimWorld itself cuts
    /// a plant for construction: <c>GenConstruct.HandleBlockingThingJob</c> hands a builder a <c>CutPlant</c>
    /// job for a plant blocking a site, and refuses a pawn who cannot do plant cutting, as
    /// <see cref="ShouldSkip"/> does here. That same rule is this giver's second reason: <b>a tree standing on
    /// a blueprint or frame is felled</b> whether or not wood is short, since a tree is pass-through-only and a
    /// bed finished over one could not be slept in.
    /// <para/>
    /// <b>Bounded, not a clear-cut.</b> <see cref="WoodShortfall"/> counts a tree already being felled at its
    /// expected yield, so the number of citizens felling at once is what the sites need, not everybody.
    /// </summary>
    public sealed class WorkGiver_ConstructChopWood : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>A pawn who cannot cut plants never fells a tree, the check RimWorld's
        /// <c>HandleBlockingThingJob</c> makes before handing a builder a cut. Nor is there any scan to run
        /// while the map has no blueprint or frame (see the body).</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null || pawn.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting)) return true;

            // No blueprint and no frame means no tree stands on a site and no wood is short
            // (WoodShortfall is zero when nothing is needed), so the walk over every plant on the map would
            // yield nothing. A settlement with nothing to build is exactly the idle colony that asks this
            // dozens of times a day per citizen.
            return map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == 0
                && map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count == 0;
        }

        /// <summary>
        /// Every tree on a building site, and every tree ready to fell whose wood the sites are short of. The
        /// demand is read here, once per scan, rather than per candidate in <see cref="HasJobOnThing"/>: it
        /// walks every site and every stack of the material, and the scan would otherwise pay that for every
        /// tree it considered.
        /// <para/>
        /// While no material the sites need is short, which is every settlement that has the wood it is
        /// building with, the only trees on offer are the ones standing on a site, and those are found where
        /// they must be, in the cells of the sites (see <see cref="PlantScanUtility"/>), not by walking every
        /// plant on the map. Once a material is short every tree that yields it is a candidate, and the walk is
        /// the right tool. Either way the trees arrive in the order the map lists its plants, so a scan picks
        /// the same tree.
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IEnumerable<Thing> trees = AnySiteMaterialShort(map) ? EveryCandidateTree(map) : TreesOnSites(map);
            foreach (Thing tree in trees) yield return tree;
        }

        /// <summary>Whether <see cref="WoodShortfall"/> is above zero for any material a blueprint or a frame
        /// on the map costs. A tree is felled for a shortage only of a material it yields, and a material no
        /// site costs is never short (<see cref="WoodShortfall"/> is zero when nothing needs it), so when this
        /// is false no tree is wanted for its wood. Asked of the sites rather than of the tree defs, which
        /// keeps the answer independent of which defs happen to be registered.</summary>
        private static bool AnySiteMaterialShort(Map.Map map)
        {
            List<ThingDef>? asked = null;
            foreach (ThingRequestGroup group in SiteGroups)
            {
                IReadOnlyList<Thing> sites = map.listerThings.ThingsInGroup(group);
                for (int i = 0; i < sites.Count; i++)
                {
                    List<ThingDefCountClass>? cost = sites[i].def.entityToBuild?.costList;
                    if (cost == null) continue;
                    for (int c = 0; c < cost.Count; c++)
                    {
                        ThingDef material = cost[c].thingDef;
                        if (asked != null && asked.Contains(material)) continue;
                        (asked ??= new List<ThingDef>()).Add(material);
                        if (WoodShortfall(map, material) > 0) return true;
                    }
                }
            }
            return false;
        }

        private static readonly ThingRequestGroup[] SiteGroups = { ThingRequestGroup.Blueprint, ThingRequestGroup.BuildingFrame };

        /// <summary>The trees whose cell holds a blueprint or a frame, in the map's own plant order.</summary>
        private static IEnumerable<Thing> TreesOnSites(Map.Map map)
        {
            List<Plant>? found = null;
            PlantScanUtility.PlantTest test = IsSpawnedTreeOnASite; // once, not per cell
            foreach (IntVec3 cell in PlantScanUtility.SiteCells(map))
            {
                PlantScanUtility.CollectPlantsAt(map, cell, ref found, test);
            }
            if (found == null) return Array.Empty<Thing>();
            return PlantScanUtility.InMapOrder(map, found);
        }

        private static bool IsSpawnedTreeOnASite(Plant plant) =>
            plant.Spawned && plant.def.plant != null && plant.def.plant.IsTree && StandsOnASite(plant, plant.Map!);

        /// <summary>Every tree standing on a site, plus every tree ready to fell whose wood is short, by
        /// walking the map's plants.</summary>
        private static IEnumerable<Thing> EveryCandidateTree(Map.Map map)
        {
            Dictionary<ThingDef, int>? shortfall = null;
            IReadOnlyList<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            for (int i = 0; i < plants.Count; i++)
            {
                if (!(plants[i] is Plant plant) || !plant.Spawned) continue;
                PlantProperties? props = plant.def.plant;
                if (props == null || !props.IsTree) continue;

                if (StandsOnASite(plant, map))
                {
                    yield return plant;
                    continue;
                }
                if (!plant.HarvestableNow || props.harvestedThingDef == null) continue;

                shortfall ??= new Dictionary<ThingDef, int>();
                if (!shortfall.TryGetValue(props.harvestedThingDef, out int missing))
                {
                    missing = WoodShortfall(map, props.harvestedThingDef);
                    shortfall[props.harvestedThingDef] = missing;
                }
                if (missing > 0) yield return plant;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Plant plant) || !plant.Spawned || plant.def.plant == null || !plant.def.plant.IsTree) return false;
            if (!plant.HarvestableNow && !StandsOnASite(plant, plant.Map!)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, plant)) return false;
            return Reachability.CanReach(pawn, plant, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(PlantCuttingJobDefOf.CutPlant, thing);

        /// <summary>
        /// How much more <paramref name="material"/> this map's building sites need than is already on its way
        /// to them: what every <see cref="Blueprint"/> costs and every <see cref="Frame"/> still lacks, less
        /// every loose stack on the map, every stack a pawn is carrying, and the expected yield of every plant
        /// being felled or harvested for it right now. Zero or less means felling another tree would only
        /// leave wood lying about.
        /// <para/>
        /// Loose stacks count wherever they lie, reachable or not and whoever has reserved them: a stack a
        /// hauler has reserved is on its way to a site already, and a stack nobody can reach is rare enough on
        /// a settlement's own map that pretending it is not there costs a tree at worst.
        /// </summary>
        public static int WoodShortfall(Map.Map map, ThingDef material)
        {
            int needed = 0;
            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp) needed += bp.EntityToBuild.CostListCountFor(material);
            }
            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f) needed += f.MaterialStillNeeded(material);
            }
            if (needed <= 0) return 0;

            int onItsWay = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(material);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) onItsWay += stacks[i].stackCount;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                Thing? carried = p.carryTracker?.CarriedThing;
                if (carried != null && carried.def == material) onItsWay += carried.stackCount;

                Job? job = p.jobs?.curJob;
                if (job == null || (job.def != PlantCuttingJobDefOf.CutPlant && job.def != BuildingJobDefOf.Harvest)) continue;
                if (job.targetA.Thing is Plant felled && felled.Spawned && felled.def.plant?.harvestedThingDef == material)
                {
                    onItsWay += ExpectedYield(felled);
                }
            }
            return needed - onItsWay;
        }

        /// <summary>What <see cref="Plant.Harvest"/> will drop for <paramref name="plant"/> at its current
        /// growth, before that method's random rounding.</summary>
        private static int ExpectedYield(Plant plant)
        {
            PlantProperties props = plant.def.plant!;
            return (int)(props.harvestYield * plant.Growth);
        }

        /// <summary>The tree shares its cell with a <see cref="Blueprint"/> or a <see cref="Frame"/> — the
        /// same test <see cref="WorkGiver_PlantsCut"/> uses for a plant blocking construction.</summary>
        private static bool StandsOnASite(Plant plant, Map.Map map) =>
            map.thingGrid.CellContains(plant.Position, ThingCategory.Blueprint) ||
            map.thingGrid.CellContains(plant.Position, ThingCategory.Frame);
    }
}
