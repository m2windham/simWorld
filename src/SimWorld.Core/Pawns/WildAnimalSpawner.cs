using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Wildlife coming back after something kills it (RimWorld: <c>RimWorld.WildAnimalSpawner</c>, a per-map
    /// component that keeps a map's animal population near what its biome supports).
    ///
    /// <para/><b>The defect this closes, and why a one-shot scatter would not have.</b> Nothing anywhere in
    /// this codebase had ever spawned a wild animal on an interior map, so the whole hunting chain — work
    /// giver, driver, initiative gate, revenge roll, kill-site butchery, every part of it built and green —
    /// could never start: <c>AI.WorkGiver_Hunt.PotentialWorkThingsGlobal</c> scanned the map's pawns and
    /// found only citizens. <c>MapGen.GenStep_Animals</c> is half the answer and on its own would have been
    /// the wild-plant defect over again: a standing population placed once, hunted to extinction inside a
    /// week, and a map that is bare for the rest of the game. This is the other half — the land restocking
    /// itself at its own pace, so hunting is a renewable trickle rather than a windfall. The pair is exactly
    /// <c>MapGen.GenStep_Scatterers</c> + <c>Building.WildPlantSpawner</c>, one system over.
    ///
    /// <para/><b>The model.</b> Two numbers, the first taken from content rather than invented here:
    /// <list type="bullet">
    /// <item>How much wildlife this map should carry — <see cref="WildAnimalTuning.DesiredAnimalWeight"/>,
    /// off <see cref="BiomeDef.animalDensity"/>, the very formula generation stocked it with, so a map
    /// restocks toward the population it was born with rather than toward a second, separately-tuned
    /// one.</item>
    /// <item>How fast it comes back — <see cref="WildAnimalTuning.RepopulateDays"/>, which content does
    /// <i>not</i> author per biome the way it authors <see cref="BiomeDef.wildPlantRegrowDays"/> for plants,
    /// so it is this port's own single figure and is pinned by trend tests rather than by its literal.</item>
    /// </list>
    /// The two combine into an animals-per-tick rate of <c>desiredCount / (repopulateDays × 60000)</c>,
    /// rolled once per <see cref="WildAnimalTuning.IntervalTicks"/> while the map is below its budget.
    /// Linear, not asymptotic — a map missing half its animals and a map missing all of them restock at the
    /// same rate per tick, which is what makes "repopulate days" mean the plain thing it says. The same
    /// deliberate choice <c>Building.WildPlantSpawner</c> makes and for the same reason.
    ///
    /// <para/><b>Determinism.</b> The gap check is arithmetic; every roll goes through
    /// <see cref="RandomStream.ChanceSeeded"/> and <see cref="RandomStream.RangeSeeded"/>, seeded from the
    /// map's own id and the current tick. Pawn generation itself is the one part that cannot:
    /// <see cref="PawnGenerator.GeneratePawn"/> is ported code that reads the ambient
    /// <see cref="Rand.Current"/> throughout. So it is wrapped in
    /// <see cref="Rand.PushState(int)"/>/<see cref="Rand.PopState"/>, which reseeds the ambient stream for
    /// the duration and then restores its seed <i>and</i> its iteration count exactly — the generated animal
    /// is a pure function of the seed, and the ambient stream comes out of this at the position it went in
    /// at. That matters because this runs inside the tick loop, where a single stray draw would shift every
    /// subsequent roll in the game; <c>Economy.SettlementLarder</c>, <c>Economy.SettlementStockInitiative</c>
    /// and <c>Quests.QuestRewardSink</c> all say the same thing about themselves, and
    /// <c>WildAnimalSpawnerTests</c> pins it on <c>Rand.Current.Iterations</c>.
    ///
    /// <para/><b>Cost.</b> One walk of the map's spawned pawns per map per
    /// <see cref="WildAnimalTuning.IntervalTicks"/> ticks — 240 times an in-game day, over a list that holds
    /// the settlement's citizens and its dozen animals — and nothing at all on the other 249 ticks. A spawn
    /// itself (up to <see cref="MaxCellTriesPerSpawn"/> cell tries plus one
    /// <see cref="PawnGenerator.GeneratePawn"/>) happens well under once a day per map. Nothing here is on
    /// the hot path, and a map whose population is at budget pays only the walk.
    ///
    /// <para/><b>What actually consumes a map's wildlife today, measured — and it is not hunting.</b> On a
    /// founded settlement of twenty-five, the eight animals a temperate map is born with are gone inside a
    /// week and the four this spawner sends after them go the same way: by day eight, nine have been
    /// <i>tamed</i> into livestock and six lie dead, mostly of wounds taken in the manhunter fights a failed
    /// tame provokes (<c>Pawns.TameUtility</c>). Not one is hunted. <c>AI.WorkGiver_TameAnimals</c>
    /// sits on the <c>Handling</c> work type at naturalPriority 950 against <c>Hunting</c>'s 850 and is
    /// gated on nothing at all, so every wild animal is claimed as taming work before <c>AI.WorkGiver_Hunt</c>
    /// is ever consulted — and <c>AI.HuntingInitiative</c>'s food gate is shut anyway on a settlement
    /// carrying its founding rations. Both are recorded in this lane's report and neither is touched here.
    /// It matters to this class for one reason: the rate above is calibrated against that real removal rate,
    /// not against an imagined hunting one.
    ///
    /// <para/><b>No state of its own</b>, so there is nothing here to Scribe: the standing population is
    /// re-derived from the pawns actually on the map, and those are saved by <see cref="Map.Map.ExposeData"/>
    /// like any other Thing.
    ///
    /// <para/><b>A map with no world tile does nothing here</b>, exactly as <c>Weather.MapClimate</c> and
    /// <c>Building.WildPlantSpawner</c> treat the same state: most tests build a bare <see cref="Map.Map"/>
    /// that never sat on a planet, and no animal should walk onto one.
    /// </summary>
    public static class WildAnimalSpawner
    {
        /// <summary>Random cells tried per spawn before giving up until the next pass. A miss is cheap and
        /// common (rock, roofed, water, built on, standing in a wall); an exhaustive search for somewhere to
        /// put one more chicken is not worth paying for — the same trade
        /// <c>Building.WildPlantSpawner</c> and <c>MapGen.GenStep_Scatterers</c> already make.</summary>
        public const int MaxCellTriesPerSpawn = 10;

        /// <summary>Purpose offsets handed to <see cref="SeedFor"/>, so the chance roll, the kind roll and
        /// each cell try are independent draws rather than the same one reused.</summary>
        private const int ChancePurpose = 0;

        private const int KindPurpose = 1;

        private const int CellPurposeBase = 10;

        /// <summary>Called once per map per tick from <see cref="Map.Map.MapTick"/>; self-gates on
        /// <see cref="WildAnimalTuning.IntervalTicks"/> so the population walk below is paid rarely. The
        /// single place the interval check lives, the same shape <c>Building.FarmingInitiative.TickMap</c>
        /// uses.</summary>
        public static void WildAnimalSpawnerTick(Map.Map map)
        {
            if (map == null) return;
            if (Find.TickManager.TicksGame % WildAnimalTuning.IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated pass, resolving the map's world tile itself. Public so a test can drive one
        /// without arranging for the tick number to land on the interval.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Tile? tile = TryResolveTile(map);
            if (tile == null) return;
            Run(map, tile);
        }

        /// <summary>The same pass against an explicit tile — for a caller (or a test) that has one in hand
        /// and no world behind the map, which is how most of this suite's maps are built.</summary>
        public static void Run(Map.Map map, Tile tile)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            float desired = DesiredAnimalWeight(map, tile);
            if (desired <= 0f) return;
            if (CurrentAnimalWeight(map) >= desired) return;

            List<PawnKindDef> kinds = WildAnimalTuning.WildAnimalKinds();
            if (kinds.Count == 0) return;

            int tick = Find.TickManager.TicksGame;
            float chance = SpawnChancePerInterval(desired, MeanAnimalWeight(kinds));
            if (!RandomStream.ChanceSeeded(chance, SeedFor(map, tick, ChancePurpose))) return;

            PawnKindDef kind = kinds[RandomStream.RangeSeeded(0, kinds.Count, SeedFor(map, tick, KindPurpose))];
            TrySpawnOne(map, kind, SeedFor(map, tick, CellPurposeBase));
        }

        /// <summary>
        /// Stocks <paramref name="map"/> up to the weight its tile supports in one pass, and answers how many
        /// animals that took. This is the generation-time half — <c>MapGen.GenStep_Animals</c> is its only
        /// caller — sharing <see cref="DesiredAnimalWeight"/> and <see cref="TrySpawnOne"/> with the tick
        /// path above so the two can never disagree about what this land holds.
        /// <para/>
        /// <paramref name="seed"/> comes from the generating step's own seeded stream, so a world tile
        /// generated twice is born with the same animals standing in the same cells.
        /// </summary>
        public static int StockMap(Map.Map map, Tile tile, int seed)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            float desired = DesiredAnimalWeight(map, tile);
            if (desired <= 0f) return 0;

            List<PawnKindDef> kinds = WildAnimalTuning.WildAnimalKinds();
            if (kinds.Count == 0) return 0;

            int spawned = 0;
            float weight = CurrentAnimalWeight(map);
            // Bounded by the budget divided by the smallest animal that could fill it, so a run of failed
            // placements on a map with almost no open ground still terminates.
            for (int attempt = 0; weight < desired && attempt < MaxStockAttempts(desired, kinds); attempt++)
            {
                PawnKindDef kind = kinds[RandomStream.RangeSeeded(0, kinds.Count, SeedFor(seed, attempt, KindPurpose))];
                Pawn? animal = TrySpawnOne(map, kind, SeedFor(seed, attempt, CellPurposeBase));
                if (animal == null) continue;
                weight += WildAnimalTuning.AnimalWeightOf(animal);
                spawned++;
            }
            return spawned;
        }

        /// <summary>How much animal weight is standing on this map: live, spawned, unowned animals only.
        /// A tamed animal belongs to a faction and is somebody's livestock, so it neither counts toward what
        /// the wilderness holds nor stops the wilderness restocking — the same "wild is an animal with no
        /// faction" line <c>AI.HuntUtility.IsHuntableAnimal</c> and <c>AI.WorkGiver_TameAnimals</c> both
        /// draw.</summary>
        public static float CurrentAnimalWeight(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            float total = 0f;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!pawn.RaceProps.Animal || pawn.faction != null || pawn.Dead) continue;
                total += WildAnimalTuning.AnimalWeightOf(pawn);
            }
            return total;
        }

        /// <summary>The weight this map's tile supports, from the same formula generation stocked it with.</summary>
        public static float DesiredAnimalWeight(Map.Map map, Tile tile) =>
            map == null ? 0f : WildAnimalTuning.DesiredAnimalWeight(map.cellIndices.NumGridCells, tile);

        /// <summary>
        /// Chance that one animal walks back onto the map this pass: a whole map's worth spread evenly over
        /// <see cref="WildAnimalTuning.RepopulateDays"/>, converted from a weight budget to a head count by
        /// <paramref name="meanWeight"/>, and scaled to the <see cref="WildAnimalTuning.IntervalTicks"/> this
        /// pass covers. Capped at 1, a cap the shipped biomes never come near — the densest one on a
        /// default-sized map sits near 0.01.
        /// </summary>
        public static float SpawnChancePerInterval(float desiredWeight, float meanWeight)
        {
            if (desiredWeight <= 0f || meanWeight <= 0f) return 0f;
            float desiredCount = desiredWeight / meanWeight;
            float perInterval = desiredCount * WildAnimalTuning.IntervalTicks
                / (WildAnimalTuning.RepopulateDays * GenDate.TicksPerDay);
            return perInterval > 1f ? 1f : perInterval;
        }

        /// <summary>
        /// Generates one wild animal of <paramref name="kind"/> and puts it on an open cell, or answers null
        /// when <see cref="MaxCellTriesPerSpawn"/> tries found nowhere to stand. Wild means
        /// <see cref="Pawn.faction"/> null — nobody's livestock and nobody's pet — which is precisely what
        /// makes it lawful prey (<c>AI.HuntUtility.IsHuntableAnimal</c>) and a candidate for taming
        /// (<c>AI.WorkGiver_TameAnimals</c>).
        /// <para/>
        /// See this class's doc for why generation runs inside a pushed <see cref="Rand"/> state: it is the
        /// one step here that cannot take a seeded stream, and the push is what keeps it from moving the
        /// ambient one.
        /// </summary>
        public static Pawn? TrySpawnOne(Map.Map map, PawnKindDef kind, int seed)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (kind == null) throw new ArgumentNullException(nameof(kind));

            if (!TryFindSpawnCell(map, seed, out IntVec3 cell)) return null;

            Pawn animal;
            Rand.PushState(seed);
            try
            {
                animal = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind));
            }
            finally
            {
                Rand.PopState();
            }

            GenSpawn.Spawn(animal, cell, map);
            return animal;
        }

        /// <summary>Open, unroofed, standable ground: where an animal can actually be. Unroofed because
        /// wildlife belongs outdoors and because a cave or a settlement's own rooms are not where a herd
        /// wanders in — the same condition <c>MapGen.GenStep_Scatterers</c> places plants under.</summary>
        private static bool CanSpawnAt(Map.Map map, IntVec3 cell)
        {
            if (map.roofGrid.Roofed(cell)) return false;
            if (map.edificeGrid[cell] != null) return false;
            if (!GenGrid.Standable(cell, map)) return false;
            return !map.terrainGrid.TerrainAt(cell).IsWater;
        }

        private static bool TryFindSpawnCell(Map.Map map, int seed, out IntVec3 cell)
        {
            for (int attempt = 1; attempt <= MaxCellTriesPerSpawn; attempt++)
            {
                int x = RandomStream.RangeSeeded(0, map.Size.x, SeedFor(seed, attempt, CellPurposeBase));
                int z = RandomStream.RangeSeeded(0, map.Size.z, SeedFor(seed, attempt, CellPurposeBase + 1));
                var candidate = new IntVec3(x, 0, z);
                if (!CanSpawnAt(map, candidate)) continue;
                cell = candidate;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        /// <summary>Mean weight of the kinds this map may be stocked with — the divisor that turns a weight
        /// budget into "how many animals is that". Uniform over the kinds because nothing in content says
        /// one is commoner than another; see <see cref="WildAnimalTuning.WildAnimalKinds"/> for that
        /// gap.</summary>
        private static float MeanAnimalWeight(List<PawnKindDef> kinds)
        {
            if (kinds.Count == 0) return 0f;
            float total = 0f;
            for (int i = 0; i < kinds.Count; i++) total += WildAnimalTuning.AnimalWeightOf(kinds[i]);
            return total / kinds.Count;
        }

        /// <summary>Placement attempts <see cref="StockMap"/> may make: enough that the budget can be filled
        /// with the lightest animal available, plus slack for cells that turn out to be unusable.</summary>
        private static int MaxStockAttempts(float desired, List<PawnKindDef> kinds)
        {
            float lightest = float.MaxValue;
            for (int i = 0; i < kinds.Count; i++)
            {
                float weight = WildAnimalTuning.AnimalWeightOf(kinds[i]);
                if (weight < lightest) lightest = weight;
            }
            if (lightest <= 0f || float.IsInfinity(lightest)) return 0;
            return (int)(desired / lightest) + kinds.Count + 1;
        }

        /// <summary>The world tile this map sits on, or null when it has none (no world current, no tile
        /// assigned, or a tile outside this world's grid) — resolved live and guarded exactly as
        /// <c>Weather.MapClimate.TryResolve</c> and <c>Building.WildPlantSpawner</c> both do it.</summary>
        private static Tile? TryResolveTile(Map.Map map)
        {
            global::SimWorld.World.World? world = Find.World;
            if (world == null || world.grid == null) return null;
            if (map.tile < 0 || map.tile >= world.grid.TilesCount) return null;
            return world.grid.Tiles[map.tile];
        }

        private static int SeedFor(Map.Map map, int tick, int purpose) => SeedFor(map.uniqueID, tick, purpose);

        /// <summary>A seed unique to (base, step, purpose); <see cref="RandomStream"/>'s seeded statics hash
        /// it before use, so a plain mix is enough to keep two maps, two ticks and two cell tries
        /// independent.</summary>
        private static int SeedFor(int baseSeed, int step, int purpose)
        {
            unchecked
            {
                uint hash = (uint)baseSeed * 2654435761u;
                hash ^= (uint)step * 2246822519u;
                hash ^= (uint)purpose * 3266489917u;
                return (int)hash;
            }
        }
    }
}
