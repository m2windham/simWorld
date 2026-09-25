using System.Collections.Generic;
using SimWorld.AI;
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
    /// player's Chop Wood designation. There is no designation layer here and no player placing blueprints
    /// either: <see cref="SettlementConstructionInitiative"/> is the settlement deciding what to build, and
    /// this is the same settlement deciding which trees to fell for it. The designation is replaced by the
    /// reason a player would have had for it, the one <see cref="WorkGiver_PlantsCut"/> already takes for the
    /// designations it translated: <b>the building sites on this map need more wood than is on its way to
    /// them.</b> See <see cref="WoodShortfall"/>. With no shortfall it fells nothing, so a settlement with
    /// nothing to build leaves its forest standing.
    /// <para/>
    /// <b>Why under Construction and not PlantCutting.</b> RimWorld's chop is <c>PlantCutting</c> work
    /// (natural priority 650), below <c>Mining</c> (700). This port's <see cref="AI.WorkGiver_Miner"/> has no
    /// designations either and so mines every reachable rock on the map; with the same default priorities a
    /// citizen who can mine never runs out of mining and never reaches a <c>PlantCutting</c> giver. The fell
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
        /// <c>HandleBlockingThingJob</c> makes before handing a builder a cut.</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || pawn.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting);

        /// <summary>
        /// Every tree on a building site, and every tree ready to fell whose wood the sites are short of. The
        /// demand is read here, once per scan, rather than per candidate in <see cref="HasJobOnThing"/>: it
        /// walks every site and every stack of the material, and the scan would otherwise pay that for every
        /// tree it considered.
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;

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
