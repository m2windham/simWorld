using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.World.Gen;
using SimWorld.World.Siting;

namespace SimWorld.World
{
    /// <summary>
    /// Rival civilizations emerging from the running simulation instead of being placed at world generation
    /// (spec §5b.4/§5b.5, tracker items <c>settlements.emergence</c> and <c>worldgen.civscale</c>). Ticked
    /// from <see cref="World.WorldTick"/> — "something a caller ticks", per the module's own brief, not a
    /// second game loop: this class owns no clock of its own, reads <see cref="Find.TickManager"/> exactly
    /// like every other per-interval system in this codebase (<see cref="Settlement.GrowthTick"/>,
    /// <c>Director.Storyteller.StorytellerTick</c>), and does nothing on ticks that are not a whole
    /// <see cref="EmergenceTuning.CheckIntervalTicks"/> apart.
    /// <para/>
    /// Two distinct events, both routed through <see cref="SettlementFounder"/> so a settlement this class
    /// creates is a real <see cref="Settlement"/> exactly like every other one (spec §5b.5):
    /// <list type="bullet">
    /// <item><b>A new civilization emerges</b> (<see cref="TryEmergeNewCivilization"/>): a fresh
    /// <see cref="Faction"/> is created and given one settlement via <see cref="SettlementFounder.Found"/> —
    /// a real, live, Full-tier founding band, chronicled, exactly the founding moment the player's own start
    /// gets (spec §5b.3). Gated by the world's current era (<see cref="EligibleFactionDefs"/>): nothing
    /// emerges more advanced than the most advanced civilization already known to exist.</item>
    /// <item><b>An existing civilization grows into a second settlement</b>
    /// (<see cref="TryExpandExistingCivilizations"/>): once a settlement is large enough to spare people
    /// (<see cref="EmergenceTuning.ExpansionPopulationThreshold"/>), it may found a colony via
    /// <see cref="SettlementFounder.FoundColony"/> — a small, freshly-seeded Statistical population, not a
    /// live band (routine growth, not an origin story), sited within the parent's own <see cref="WorldRegion"/>
    /// and chronicled as an expansion rather than a founding.</item>
    /// </list>
    /// Both use this manager's own <see cref="RandomStream"/>, seeded once from the world at construction and
    /// never touched by anything else — the same seed always produces the same emergence history, regardless
    /// of what else in the game has consumed <see cref="Rand.Current"/> by the time a check runs.
    /// </summary>
    public sealed class EmergenceManager : IExposable
    {
        private RandomStream rand = null!;

        /// <summary>For Scribe's deep-load construction.</summary>
        public EmergenceManager()
        {
        }

        /// <summary>Seeds this manager's stream from the world's own seed, salted so it never draws the same
        /// sequence as world generation's own steps (each of which salts by its own defName — see
        /// <see cref="WorldGenStep.SeededStream"/>).</summary>
        public EmergenceManager(int worldSeed)
        {
            rand = new RandomStream(GenText.StableStringHash(worldSeed + "_Emergence"));
        }

        /// <summary>Call once per game tick (see <see cref="World.WorldTick"/>). Only acts every
        /// <see cref="EmergenceTuning.CheckIntervalTicks"/> ticks.</summary>
        public void Tick(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (Find.TickManager.TicksGame % EmergenceTuning.CheckIntervalTicks != 0) return;

            TryEmergeNewCivilization(world);
            TryExpandExistingCivilizations(world);
        }

        // ---- a new civilization emerges ----

        private void TryEmergeNewCivilization(World world)
        {
            float popMultiplier = WorldGenStep_Factions.PopulationMultiplier[world.info.overallPopulation];
            float mtb = EmergenceTuning.NewCivilizationMTBYears / Math.Max(popMultiplier, 0.05f);
            if (!rand.MTBEventOccurs(mtb, GenDate.TicksPerYear, EmergenceTuning.CheckIntervalTicks)) return;

            IReadOnlyList<FactionDef> eligible = EligibleFactionDefs(world);
            if (eligible.Count == 0) return; // nothing era-appropriate left room to emerge.

            FactionDef chosen = GenCollection.RandomElementByWeight(eligible, d => Math.Max(0.01f, d.settlementGenerationWeight), rand);

            SiteWeightDef? weights = CurrentSiteWeights();
            if (weights == null) return; // no site-weight content loaded; nothing to score against.

            var existing = new List<WorldObject>(world.worldObjects);
            IEnumerable<int> candidates = LandCandidateTiles(world.grid);
            if (!TryPickBestSite(world.grid, candidates, existing, weights, out int tile)) return;

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Faction f in world.factions) usedNames.Add(f.name);
            string factionName = FactionNameMaker.MakeFactionName(chosen, rand, usedNames);
            var faction = new Faction(chosen, factionName, world.NextLoadId("Faction"));
            world.factions.Add(faction);
            Find.FactionManager.Add(faction);
            foreach (Faction other in world.factions)
            {
                if (!ReferenceEquals(other, faction)) faction.TryMakeInitialRelationsWith(other, rand);
            }

            int bandSize = rand.Range(SettlementTuning.FoundingBandRange);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, bandSize, rand);

            Find.Storyteller.RecordChronicle(
                "Civilization emerged: " + factionName + " (" + chosen.LabelCap + "), founding " + settlement.name + ".");
        }

        /// <summary>
        /// Every non-hidden, non-player <see cref="FactionDef"/> that could emerge right now: era-appropriate
        /// (<see cref="FactionDef.techLevel"/> no higher than the world's current era —
        /// <see cref="ResearchManager.CurrentEra"/>, the only civilization-wide era this simulation tracks, so
        /// it stands in for "the most advanced technology known to exist in the world" — the "era seeding"
        /// half of <c>worldgen.civscale</c>) and under its own <see cref="FactionDef.maxCountAtGameStart"/>
        /// (reused as a total-instance ceiling, not only a game-start one: the same authored cap that would
        /// have bounded this def's count at world generation bounds how many of it can ever exist). Public and
        /// pure so era-gating is directly testable without depending on a probabilistic MTB roll.
        /// </summary>
        public static IReadOnlyList<FactionDef> EligibleFactionDefs(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            TechLevel ceiling = Find.ResearchManager.CurrentEra?.techLevel ?? TechLevel.Neolithic;
            var result = new List<FactionDef>();
            foreach (FactionDef def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (def.hidden || def.isPlayer) continue;
                if (def.techLevel > ceiling) continue;
                int existingCount = world.factions.Count(f => f.def == def);
                if (existingCount >= def.maxCountAtGameStart) continue;
                result.Add(def);
            }
            return result;
        }

        // ---- an existing civilization grows into a second settlement ----

        private void TryExpandExistingCivilizations(World world)
        {
            float popMultiplier = WorldGenStep_Factions.PopulationMultiplier[world.info.overallPopulation];
            float mtb = EmergenceTuning.ExpansionMTBYears / Math.Max(popMultiplier, 0.05f);

            // Snapshot: only settlements that already existed when this check began are eligible to expand
            // this tick, so one expansion cannot chain into another within the same check.
            var settlements = world.worldObjects.OfType<Settlement>().ToList();
            foreach (Settlement settlement in settlements)
            {
                if (settlement.faction == null) continue;
                if (settlement.TotalPopulation < EmergenceTuning.ExpansionPopulationThreshold) continue;
                int settlementsOfFaction = settlements.Count(s => s.faction == settlement.faction);
                if (settlementsOfFaction >= EmergenceTuning.MaxSettlementsPerFaction) continue;
                if (!rand.MTBEventOccurs(mtb, GenDate.TicksPerYear, EmergenceTuning.CheckIntervalTicks)) continue;

                if (!TryFoundColonyFor(world, settlement)) continue; // no room in the parent's own region this time; try again next check.
            }
        }

        private bool TryFoundColonyFor(World world, Settlement parent)
        {
            SiteWeightDef? weights = CurrentSiteWeights();
            if (weights == null) return false;

            WorldRegion? region = world.regions.FirstOrDefault(r => r.tiles.Contains(parent.tile));
            IEnumerable<int> candidates = region != null
                ? region.tiles.Where(t => !world.grid.Tiles[t].WaterCovered && (world.grid.Tiles[t].biome?.canBuildBase ?? false))
                : LandCandidateTiles(world.grid);

            var existing = new List<WorldObject>(world.worldObjects);
            if (!TryPickBestSite(world.grid, candidates, existing, weights, out int tile)) return false;

            int population = rand.Range(SettlementTuning.FoundingBandRange);
            SettlementFounder.FoundColony(world, tile, parent.faction, population, rand);
            return true;
        }

        // ---- shared siting ----

        private static IEnumerable<int> LandCandidateTiles(WorldGrid grid)
        {
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                if (!tile.WaterCovered && (tile.biome?.canBuildBase ?? false)) yield return i;
            }
        }

        /// <summary>
        /// The best-scoring candidate (spec §5b.2: decisive, not advisory, for emergent foundings) at least
        /// <see cref="WorldGenStep_Factions.MinSettlementDistance"/> tiles from every existing settlement —
        /// the exact spacing rule world generation itself uses, so an emerged settlement never lands
        /// implausibly close to one placed at world start. False when nothing scored above zero (no
        /// necessity-satisfying tile available) or no candidate was far enough from an existing settlement.
        /// </summary>
        private static bool TryPickBestSite(WorldGrid grid, IEnumerable<int> candidates, IReadOnlyList<WorldObject> existing, SiteWeightDef weights, out int tile)
        {
            int minDistance = WorldGenStep_Factions.MinSettlementDistance(grid.TilesCount);
            int best = -1;
            float bestScore = 0f;
            foreach (int candidate in candidates)
            {
                bool tooClose = false;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (grid.ApproxDistanceInTiles(candidate, existing[i].tile) < minDistance) { tooClose = true; break; }
                }
                if (tooClose) continue;

                float score = SiteScorer.Score(grid, candidate, weights);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            tile = best;
            return best >= 0;
        }

        /// <summary>The <see cref="SiteWeightDef"/> for the world's current era (see
        /// <see cref="EligibleFactionDefs"/> for why <see cref="ResearchManager.CurrentEra"/> stands in for
        /// "the world's era" at all).</summary>
        private static SiteWeightDef? CurrentSiteWeights() => SiteWeightDef.ForEra(Find.ResearchManager.CurrentEra);

        public void ExposeData()
        {
            int seed = rand?.Seed ?? 0;
            int iterations = rand != null ? unchecked((int)rand.Iterations) : 0;
            Scribe_Values.Look(ref seed, "randSeed", 0);
            Scribe_Values.Look(ref iterations, "randIterations", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                rand = new RandomStream(seed);
                rand.Restore(seed, unchecked((uint)iterations));
            }
        }
    }
}
