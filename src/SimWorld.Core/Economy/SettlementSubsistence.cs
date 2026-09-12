using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// <b>The production half of the civilization-scale ledger</b> (spec §11.2's "civilization-wide sight,
    /// settlement-deep touch"). A settlement with no map works its own land and what it grows becomes
    /// something the civilization <i>has</i>.
    ///
    /// <para/><b>A recorded translation, not a port.</b> RimWorld has no counterpart to copy and the gap is
    /// structural rather than missing detail: RimWorld's world-tile settlements are inert props that produce
    /// nothing, and its colony's entire economy is a map — a plant grown in a cell, a meal cooked at a stove,
    /// a chunk carried to a stockpile. There is exactly one colony and the player is always looking at it. A
    /// civilization has hundreds of settlements and the player is looking at one, so the question RimWorld
    /// never has to answer — <i>what does a town do while nobody is watching it?</i> — is the whole of this
    /// class. The answer here is the smallest one that is honest: <b>a settlement nobody is watching feeds
    /// itself by subsistence</b>, growing food into its own ledger at a rate its land sets and stopping when
    /// it holds the larder the food economy already says a settlement wants.
    ///
    /// <para/><b>The defect this closes.</b> <see cref="SettlementLarder"/> closed consumption — a citizen
    /// with no map eats out of <see cref="Settlement.Stores"/> — and said in as many words what it left open:
    /// <i>"A settlement whose ledger is empty starves, and that is the correct outcome — it means that
    /// settlement's production is broken, which is a different problem from this one."</i> It was. Nothing at
    /// civilization scale wrote food into any ledger: the only adder was the founding band's ration crate,
    /// plus <see cref="SettlementStockInitiative"/>, which banks from a live map and therefore only ever
    /// fires for the one settlement somebody has opened. Measured over twenty in-game days on a settlement
    /// nobody opened — <c>Game.NewGame</c>, tribal start, twenty-five founders, nothing called by hand:
    ///
    /// <para/><code>
    /// day | alive | mean food | ledger nutrition | worst Malnutrition
    ///   0 |    25 |      0.80 |            440.0 | 0.00
    ///   5 |    24 |      0.31 |            242.4 | 0.00
    ///  11 |    22 |      0.33 |              0.0 | 0.00
    ///  15 |    22 |      0.00 |              0.0 | 0.43
    ///  20 |    18 |      0.00 |              0.0 | 1.00   (lethal at 1.00)
    /// </code>
    ///
    /// <para/>That is a ration crate draining at exactly the rate twenty-five people eat, and then a town
    /// dying of hunger — slowly instead of quickly, which was the previous lane's honest description of what
    /// it had achieved. In a world of many settlements that is every settlement but one.
    ///
    /// <para/><b>Who produces: the hands with no map, which are the same people as the mouths with no
    /// map.</b> The predicate is <c>!Spawned</c>, per citizen, and it is
    /// <see cref="SettlementLarder.WouldEatFromStores"/>'s predicate minus the hunger test — deliberately the
    /// same one, because the two sides of a book have to agree about who is in it. A citizen standing on a
    /// live map already forages, farms and cooks through the ported map path, and what rests in the granary
    /// is already carried across by <see cref="SettlementStockInitiative"/>; counting that citizen here as
    /// well would credit one person's labour twice. A citizen with no map does neither, and the ledger is
    /// both their field and their larder.
    ///
    /// <para/><b>The invariant this leans on is <see cref="SettlementStockInitiative"/>'s</b> — <i>a unit of
    /// goods is either a <c>Thing</c> on a map or a count in <see cref="Settlement.Stores"/>, never both</i>
    /// — and this class is the first writer that creates units rather than moving them, so it is the first
    /// one that could break it. It cannot, because the two paths are disjoint by the predicate above and not
    /// by a tier or a settlement-wide flag: a half-watched settlement (some citizens Full and spawned, the
    /// rest demoted and despawned by <c>God.AttentionBudget</c>) pays each citizen through exactly one path,
    /// and the sum is still one settlement's labour. <c>SettlementSubsistenceTests</c> pins that a settlement
    /// whose citizens are all on a map produces nothing here at all.
    ///
    /// <para/>The one case where both writers touch one ledger is a settlement with a <i>generated but
    /// unwatched</i> map (<see cref="SettlementStockInitiative"/>'s tiering state 2): its citizens have been
    /// demoted and despawned, so they produce here, while whatever is still resting in its granary keeps
    /// being banked. That is not a double payment — the banked goods are labour the map path already did and
    /// is now merely moving across — and it needs no special case, because the ceiling below counts the whole
    /// ledger: food arriving from the granary is food this class then does not grow.
    ///
    /// <para/><b>Why the Statistical cohort neither eats nor produces, stated as a decision.</b>
    /// <see cref="Settlement.StatisticalPopulation"/> is a bare count with no <c>Pawn</c> behind it (spec
    /// §11.3), and <see cref="SettlementLarder"/>'s own doc names the asymmetry that would follow from
    /// getting this wrong: <i>the cohort does not eat</i>. If production scaled with total population while
    /// consumption scales with the live roster, a settlement of ten thousand abstract people would flood its
    /// ledger feeding twenty-five — abstract food conjured for mouths whose hunger is not modelled. So the
    /// cohort is on neither side of this book, which is the only pairing that balances: the same set of
    /// people that <see cref="SettlementLarder"/> feeds is the set that produces here, and
    /// <c>AI.HuntingInitiative</c> already draws the same line for the demand figure. <b>The ledger models
    /// the live roster's subsistence and nothing else</b>, and a cohort's food is as unmodelled as its
    /// hunger. Making the cohort real on both sides at once is a coherent future change; making it real on
    /// one side is a bug.
    ///
    /// <para/><b>Every mouth is also a pair of hands, in proportion.</b> A citizen's production is scaled by
    /// their own <see cref="Pawn.HungerRate"/>, the same per-pawn factor
    /// <c>AI.HuntingInitiative.NutritionWanted</c> scales demand by — so a child eats less and works less by
    /// exactly the same factor, and a settlement's ledger is a household's book rather than a payroll. This
    /// port has no abstract labour model to draw a truer line with (no age-of-work, no occupation on the
    /// roster), and inventing one here would be a second demography beside the real one.
    ///
    /// <para/><b>How much: arithmetic over settlement state, with a ceiling.</b> Per day a settlement
    /// produces what its hands burn × <see cref="SettlementSubsistenceTuning.YieldRatio"/> ×
    /// <see cref="SettlementSubsistenceTuning.LandYieldFactor"/>, and it stops the moment its ledger holds
    /// <see cref="SettlementSubsistenceTuning.DaysOfFoodWanted"/> days of food — the same larder
    /// <c>AI.HuntingInitiative.WantsMeat</c> already sends hunters out to build and
    /// <c>Building.FarmingInitiative</c> already sizes a field against. <b>The ceiling is the load-bearing
    /// half</b>, not the rate: it is what stops this from being a food fountain. A settlement cannot bank
    /// more than a few days of food however good its land, so a long-lived civilization's ledger is a working
    /// larder rather than an ever-growing pile, and a bad year still empties it.
    ///
    /// <para/><b>The land can be too thin, and that is the model speaking.</b> On the best land content ships
    /// the factor is 1 and a settlement produces a real surplus; on an extreme desert it is 0.04 and the
    /// settlement produces a fraction of what it eats and will starve, and on an ice sheet (forageability 0)
    /// it produces nothing at all. That is a true statement about that ground — nothing here can feed a town
    /// the land cannot — and the honest thing this port lacks is the compensation a real civilization would
    /// have: caravans and trade between settlements, which exist as mechanism (<see cref="TradeRoute"/>,
    /// <see cref="SettlementTradeUtility"/>) with nothing driving them at civilization scale. Recorded rather
    /// than papered over with a floor that would make the biome decoration.
    ///
    /// <para/><b>No roll, ever.</b> This runs inside the tick loop, where a single draw would shift every
    /// subsequent roll in the game — the same statement <see cref="SettlementLarder"/>,
    /// <see cref="SettlementStockInitiative"/> and <c>Quests.QuestRewardSink</c> make about themselves. What
    /// is grown is a total order over content (best nutrition per cell per day, ties by <c>defName</c>); how
    /// much is arithmetic over the roster, the biome and the tick number. The fraction of a unit a pass earns
    /// is not rounded away and not drawn for either — see <see cref="Run"/>'s dither, which is a function of
    /// the pass index and nothing else. <c>SettlementSubsistenceTests</c> pins it on
    /// <c>Rand.Current.Iterations</c>.
    ///
    /// <para/><b>Cost: O(citizens above Statistical), never O(population)</b> — the guarantee
    /// <c>God.AttentionManager.Reconcile</c>, <c>God.GodRollup</c>, <c>Sim.CitizenTickRegistry</c> and
    /// <see cref="SettlementLarder"/> all make. One walk of the live roster and one of the ledger per
    /// settlement per <see cref="SettlementSubsistenceTuning.IntervalTicks"/> ticks; the cohort is never
    /// iterated because there is nobody there to hold a hoe. The two content walks a pass needs — which crop,
    /// and the best forageability in the world — are done once per world pass in <see cref="Tick"/> and
    /// handed down, not repeated per settlement.
    ///
    /// <para/><b>No state of its own.</b> Everything is re-derived from the roster, the tile and the ledger on
    /// each gated pass, so there is nothing here to Scribe: the ledger persists because
    /// <see cref="Settlement.ExposeData"/> already writes it, and a settlement that saves and loads mid-pass
    /// resumes producing at exactly the rate its state says it should.
    ///
    /// <para/><b>It also owns the founding rations, for every settlement rather than the player's.</b> See
    /// <see cref="ProvisionIfNewlyFounded"/>: that call used to sit in <c>Sim.Game.NewGame</c>, which founds
    /// exactly one settlement, so every rival civilization <c>World.EmergenceManager</c> raised during play
    /// walked in with an empty ledger.
    /// </summary>
    public static class SettlementSubsistence
    {
        // -----------------------------------------------------------------------------------------------
        // Entry points — the same shape every other settlement-scale initiative here uses.
        // -----------------------------------------------------------------------------------------------

        /// <summary>Civilization-wide entry point, called once per tick from <c>Sim.Game.WireTickHooks</c>
        /// and short-circuited by the gate below on every tick but the rare one it fires on. A silent no-op
        /// with no world running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            if (Find.TickManager.TicksGame % SettlementSubsistenceTuning.IntervalTicks != 0) return;

            // The two content-wide reads a pass needs, paid once for the whole world rather than once per
            // settlement: which crop this world's content grows best, and what its best land yields.
            ThingDef? crop = SubsistenceFoodDef();
            float bestForageability = SettlementSubsistenceTuning.BestForageabilityInContent;

            foreach (Settlement settlement in world.Settlements)
            {
                ProvisionIfNewlyFounded(settlement);
                Run(settlement, crop, LandYieldFactorFor(settlement, bestForageability));
            }
        }

        /// <summary>One settlement, self-gated on the same cadence — for a caller that has a settlement in
        /// hand rather than a world. Returns the units of food produced.</summary>
        public static int TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (Find.TickManager.TicksGame % SettlementSubsistenceTuning.IntervalTicks != 0) return 0;
            ProvisionIfNewlyFounded(settlement);
            return Run(settlement);
        }

        /// <summary>
        /// The ungated production pass: credits <paramref name="settlement"/>'s ledger with what its off-map
        /// hands grew this interval, and returns how many units that was. Public so a test (or a future
        /// caller) can drive one pass without arranging for the tick number to land on the interval.
        ///
        /// <para/><b>One call is one pass, and calling it twice inside one interval pays twice</b> — exactly
        /// as <see cref="SettlementLarder.Run"/> feeds twice if driven twice. The gate above is what makes a
        /// pass happen once; this is what a pass does.
        /// </summary>
        public static int Run(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            return Run(settlement, SubsistenceFoodDef(), LandYieldFactor(settlement));
        }

        private static int Run(Settlement settlement, ThingDef? crop, float landFactor)
        {
            // Settle the cheap questions first, cheapest first: a settlement with nobody off a map has no
            // abstract hands (every watched settlement, and every settlement of pure cohort), and land that
            // bears nothing edible grows nothing however many hands work it.
            if (landFactor <= 0f) return 0;
            float burnPerDay = BurnPerDay(settlement);
            if (burnPerDay <= 0f) return 0;

            // The ceiling. A settlement holding the larder the food economy says it wants produces nothing,
            // which is what keeps this from being a fountain.
            float gap = burnPerDay * SettlementSubsistenceTuning.DaysOfFoodWanted
                - SettlementLarder.StoredNutrition(settlement);
            if (gap <= 0f) return 0;

            IngestibleProperties? ingestible = crop?.ingestible;
            if (crop == null || ingestible == null || ingestible.nutrition <= 0f) return 0;

            int units = UnitsThisPass(burnPerDay, landFactor, ingestible.nutrition);
            if (units <= 0) return 0;

            int unitsThatFitTheGap = (int)Math.Ceiling(gap / ingestible.nutrition);
            if (units > unitsThatFitTheGap) units = unitsThatFitTheGap;

            settlement.AddStore(crop, units);
            return units;
        }

        // -----------------------------------------------------------------------------------------------
        // How much, and of what.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Whole units of <paramref name="unitNutrition"/>-sized food this pass delivers, with the fraction
        /// of a unit a pass earns neither rounded away nor drawn for.
        ///
        /// <para/><b>Why a dither rather than a truncation.</b> One unit of every raw foodstuff this port
        /// ships is 0.05 nutrition, and a pass is 1/30th of a day, so a settlement of twenty-five earns some
        /// sixty units a pass and truncation would cost it nothing — but a settlement of one earns two and a
        /// half, and a lone survivor on thin land earns a fraction of one. Truncating each pass
        /// independently would round that to nothing for ever and starve exactly the settlements least able
        /// to survive it. So the pass credits <c>floor(rate × n) − floor(rate × (n−1))</c> for pass index
        /// <c>n</c>: whole units always, the fraction carried in the arithmetic rather than in a field, and a
        /// pure function of state with no draw and nothing to Scribe.
        /// </summary>
        private static int UnitsThisPass(float burnPerDay, float landFactor, float unitNutrition)
        {
            double unitsPerPass = burnPerDay
                * SettlementSubsistenceTuning.YieldRatio
                * landFactor
                * SettlementSubsistenceTuning.IntervalTicks / GenDate.TicksPerDay
                / unitNutrition;
            if (unitsPerPass <= 0d) return 0;

            // Pass index, floored at one so an ungated call before the first interval has elapsed credits one
            // pass rather than measuring against a pass that never happened.
            long pass = Find.TickManager.TicksGame / SettlementSubsistenceTuning.IntervalTicks;
            if (pass < 1) pass = 1;

            return (int)(Math.Floor(unitsPerPass * pass) - Math.Floor(unitsPerPass * (pass - 1)));
        }

        /// <summary>
        /// Nutrition <paramref name="settlement"/>'s off-map roster burns per day — the mouths this ledger
        /// feeds, each at its own <see cref="Pawn.HungerRate"/>, exactly as
        /// <c>AI.HuntingInitiative.NutritionWanted</c> counts the mouths a hunt is for. It is both the demand
        /// figure and (scaled) the supply figure, because here they are the same people.
        /// </summary>
        public static float BurnPerDay(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            float perDay = 0f;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (!WouldWorkForStores(citizens[i])) continue;
                perDay += HuntingTuning.NutritionPerEaterPerDay * citizens[i].HungerRate;
            }
            return perDay;
        }

        /// <summary>Nutrition <paramref name="settlement"/> produces into its ledger per day at its current
        /// roster and on its own land — the rate <see cref="Run"/> delivers in whole units.</summary>
        public static float NutritionPerDay(Settlement settlement) =>
            BurnPerDay(settlement) * SettlementSubsistenceTuning.YieldRatio * LandYieldFactor(settlement);

        /// <summary>The ledger <paramref name="settlement"/> produces toward and stops at:
        /// <see cref="SettlementSubsistenceTuning.DaysOfFoodWanted"/> days of food for the roster this ledger
        /// feeds. The abstract twin of <c>AI.HuntingInitiative.NutritionWanted</c>, measured against the
        /// ledger half alone because there is no map half for a settlement this runs on.</summary>
        public static float NutritionWantedInStores(Settlement settlement) =>
            BurnPerDay(settlement) * SettlementSubsistenceTuning.DaysOfFoodWanted;

        /// <summary>
        /// Whether this citizen's labour lands in the ledger at all: alive, not suspended, and <b>not
        /// standing on a map</b>. <see cref="SettlementLarder.WouldEatFromStores"/> minus the hunger test —
        /// the same set, deliberately, so the two sides of the book agree about who is in it. See the class
        /// doc, and see <see cref="SettlementLarder"/>'s for why the map question is <c>Spawned</c> rather
        /// than a tier.
        /// </summary>
        public static bool WouldWorkForStores(Pawn citizen)
        {
            if (citizen == null) throw new ArgumentNullException(nameof(citizen));
            return !citizen.Dead && !citizen.Suspended && !citizen.Spawned;
        }

        /// <summary>
        /// What a settlement with no map grows: the harvested product of the best crop content ships, by the
        /// same nutrition-per-cell-per-day measure <c>Building.FarmingInitiative.CropFor</c> ranks a map's
        /// crops with, ties broken by <c>defName</c>. A total order read off content — no draw, and a new
        /// crop ships into this with no code change.
        ///
        /// <para/><b>Fertility is deliberately not consulted</b>, unlike the map path: fertility is a
        /// property of a terrain grid and a settlement this runs on has no map to have one. The land enters
        /// through <see cref="SettlementSubsistenceTuning.LandYieldFactor"/> instead, which is a property of
        /// the world tile and exists whether or not anybody has opened the settlement.
        /// </summary>
        public static ThingDef? SubsistenceFoodDef()
        {
            ThingDef? best = null;
            float bestYield = 0f;

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def.category != ThingCategory.Plant || def.plant == null) continue;

                float yield = FarmingInitiative.NutritionPerCellPerDay(def);
                if (yield <= 0f) continue;

                ThingDef harvested = def.plant.harvestedThingDef!;
                if (best == null || yield > bestYield
                    || (yield == bestYield && string.CompareOrdinal(harvested.defName, best.defName) < 0))
                {
                    best = harvested;
                    bestYield = yield;
                }
            }
            return best;
        }

        /// <summary>How much of its intent <paramref name="settlement"/>'s own ground returns — its tile's
        /// biome through <see cref="SettlementSubsistenceTuning.LandYieldFactor"/>, or 0 when there is no
        /// world, no tile or no biome to ask.</summary>
        public static float LandYieldFactor(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            return SettlementSubsistenceTuning.LandYieldFactor(BiomeOf(settlement));
        }

        /// <summary>The same answer as <see cref="LandYieldFactor(Settlement)"/> against a
        /// <paramref name="bestForageability"/> already read once for the whole world — see
        /// <see cref="Tick"/>.</summary>
        private static float LandYieldFactorFor(Settlement settlement, float bestForageability)
        {
            if (bestForageability <= 0f) return 0f;
            BiomeDef? biome = BiomeOf(settlement);
            if (biome == null || biome.forageability <= 0f) return 0f;
            float factor = biome.forageability / bestForageability;
            return factor > 1f ? 1f : factor;
        }

        /// <summary>The biome of the tile <paramref name="settlement"/> sits on, or null with no world
        /// running or a tile outside the grid — the same guard <c>Building.WildPlantSpawner</c> and
        /// <c>Weather.GenTemperature</c> already apply to the identical lookup.</summary>
        private static BiomeDef? BiomeOf(Settlement settlement)
        {
            SimWorld.World.World? world = Find.World;
            if (world == null || world.grid == null) return null;
            if (settlement.tile < 0 || settlement.tile >= world.grid.TilesCount) return null;
            return world.grid.Tiles[settlement.tile].biome;
        }

        // -----------------------------------------------------------------------------------------------
        // The founding rations — for every settlement, not just the player's.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Credits <paramref name="settlement"/> with the provisions its founding band walked in carrying
        /// (<see cref="SettlementLarder.ProvisionFoundingBand"/>) if this pass is the first one after it was
        /// founded, and returns how many units that was.
        ///
        /// <para/><b>The defect this closes, and why it lives here.</b> The provisioning call sat in
        /// <c>Sim.Game.NewGame</c>, which founds exactly one settlement — the player's. Every other caller of
        /// <c>World.SettlementFounder.Found</c> got nothing: <c>World.EmergenceManager.Emerge</c> raises a
        /// whole new civilization with a live founding band during play (spec §5b.4 — every rival in a solo
        /// start), and that band walked in with an empty ledger, no map to forage on and, before this class,
        /// nothing to produce with either. <b>A rival civilization founded with an empty larder is a town
        /// that dies before it can be a rival.</b>
        ///
        /// <para/><b>Why not at the founding site.</b> <see cref="SettlementLarder"/> already named the
        /// obstacle: the call belongs beside the founding, in <c>World.SettlementFounder.Found</c>, and
        /// cannot go there because <c>World/</c> would then reference <c>Economy/</c> and spec §2's
        /// "a module never references a layer above it" is the rule the whole architecture rests on. The
        /// answer is an owner at this layer rather than a reference the wrong way down, and this is it: the
        /// same Economy-side initiative that produces into a settlement's ledger also notices a settlement
        /// whose ledger has not been opened yet. One owner for the ledger's two writers, at the layer the
        /// ledger's consumer already lives on.
        ///
        /// <para/><b>Exactly once, with no state and no marker.</b> The window is "founded since the previous
        /// pass": <c>foundingTick &lt; now ≤ foundingTick + interval</c>. Passes fall on multiples of
        /// <see cref="SettlementSubsistenceTuning.IntervalTicks"/>, and exactly one multiple lies in a
        /// half-open window that long, so a settlement is provisioned on one pass and never again — no
        /// "provisioned" flag to save, nothing to migrate into an old save, and a settlement loaded from a
        /// save is long outside its own window and cannot be provisioned twice by loading. The one thing this
        /// does not survive is the tick loop skipping a whole interval (spec §11.1's abstract time step),
        /// which no caller does today; a settlement that missed its window is not left with nothing, it
        /// simply starts from an empty ledger and produces its way up.
        /// </summary>
        public static int ProvisionIfNewlyFounded(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            int sinceFounding = Find.TickManager.TicksGame - settlement.foundingTick;
            if (sinceFounding <= 0 || sinceFounding > SettlementSubsistenceTuning.IntervalTicks) return 0;
            return SettlementLarder.ProvisionFoundingBand(settlement);
        }
    }
}
