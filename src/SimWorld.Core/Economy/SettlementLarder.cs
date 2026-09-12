using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// <b>The seam between the two halves of the game, in the ledger-to-citizen direction</b> (spec §11.2's
    /// "civilization-wide sight, settlement-deep touch"). A citizen with no map to act in eats what the
    /// civilization <i>has</i>.
    ///
    /// <para/><b>The defect this closes.</b> Two things landed in the same batch and joined badly. Citizens
    /// off a map started ticking (<c>Sim.CitizenTickRegistry</c> — before it, tick-list membership was granted
    /// only by <c>Things.Thing.SpawnSetup</c>, so a settlement nobody had opened was a photograph). And the
    /// food economy was built: foraging, farming, cooking, and eating a sitting rather than a mouthful. But
    /// <b>every part of that food economy is map-based</b> — berries lie on a map, a growing zone is painted
    /// on one, a kitchen is built on one — and <c>Sim.Game.NewGame</c> focuses the settlement it founds, so
    /// its citizens tick at <see cref="PawnTier.Full"/> while nothing has opened the interior and there is no
    /// map. Measured over eight in-game days, a settlement nobody opened went from mean nutrition 0.80 to
    /// 0.00 within one day and stayed there, accruing <c>Malnutrition</c> at ~0.11 severity a day (lethal at
    /// 1) and losing four of twenty-five citizens. <b>An unwatched settlement starved where it used to be
    /// frozen</b>, and in a world of many settlements that is all of them but one.
    ///
    /// <para/><b>The predicate is <c>!Spawned</c>, and deliberately not a tier.</b> What an abstract eater
    /// lacks is not fidelity, it is <i>a world to act in</i>: <c>AI.JobGiver_GetFood</c> returns null the
    /// instant <c>pawn.Map</c> is null, so a citizen who is not spawned cannot reach food by any map-side
    /// route no matter what tier they hold. Reading the tier instead would be wrong in both directions — a
    /// Full-tier citizen of an unopened settlement has no map (the case this exists for), and an Interval
    /// citizen briefly still standing on a generated interior does. <c>Spawned</c> is exactly the question
    /// "is there a map under this pawn", which is exactly what decides whether the map path can serve them.
    ///
    /// <para/><b>Nobody is fed twice, and the stock invariant holds.</b> A citizen standing on a live map
    /// keeps walking to a meal and eating it; this never touches them. The invariant
    /// <see cref="SettlementStockInitiative"/> states — <i>a unit of goods is either a <c>Thing</c> on a map
    /// or a count in <see cref="Settlement.Stores"/>, never both</i> — is this class's own precondition and is
    /// untouched by it: nutrition leaves the ledger in the same step it enters a stomach, no <c>Thing</c> is
    /// ever created, and the two halves stay disjoint. That is also why the pairing is safe in the other
    /// direction: banking keeps a settlement's larder physically on the map for the people who eat from a
    /// map (<c>HuntingInitiative.NutritionWanted</c>'s reserve), and only the surplus above it crosses to the
    /// ledger the people with no map eat from.
    ///
    /// <para/><b>Nothing here invents nutrition.</b> Every unit eaten is a counted <see cref="ThingDef"/>
    /// that was already in the ledger, and it is spent at that def's own <c>ingestible.nutrition</c>, in the
    /// sitting size <c>Crafting.FoodUtility.WillIngestStackCountOf</c> gives the map path. <b>A settlement
    /// whose ledger is empty starves, and that is the correct outcome</b> — it means that settlement's
    /// production is broken, which is a different problem from this one. That problem is now
    /// <see cref="SettlementSubsistence"/>'s, and it is the other side of this exact book: the set of
    /// citizens that eats here is the set that produces there. See "what this does not close".
    ///
    /// <para/><b>Cost, and the Statistical cohort.</b> One walk of the settlement's <i>live roster</i> every
    /// <see cref="SettlementLarderTuning.IntervalTicks"/> ticks — 30 walks per settlement per in-game day,
    /// each citizen costing two field reads (<c>Dead</c>, <c>Spawned</c>) and, only for the hungry off-map
    /// ones, one walk of the ledger's own def list (a handful of entries) to pick the best food. A settlement
    /// whose ledger holds nothing edible pays only that ledger walk and returns before touching the roster at
    /// all. <b><see cref="Settlement.StatisticalPopulation"/> is never iterated</b> — it is a count with no
    /// <c>Pawn</c> behind each person, so there is nothing there to feed — which makes this O(citizens above
    /// Statistical) and never O(population), the same guarantee <c>God.AttentionManager.Reconcile</c>,
    /// <c>God.GodRollup</c> and <c>Sim.CitizenTickRegistry</c> already make. A settlement of ten thousand
    /// abstract citizens costs exactly what a settlement of ten does.
    ///
    /// <para/><b>Why the cohort does not eat, stated as a decision rather than an omission.</b> A cohort draw
    /// would be O(1) arithmetic and is deliberately not taken: a Statistical citizen's needs are
    /// <i>sampled</i>, never tracked (spec §11.3), so there is no hunger for the food to answer, and nothing
    /// anywhere credits a cohort's <i>production</i> into the ledger either. Modelling one side of a book
    /// whose other side does not exist would drain a settlement's stores to feed people whose being fed is
    /// not modelled, and starve the live roster to do it. <c>AI.HuntingInitiative</c> draws the same line for
    /// the same reason, so the demand figure and the consumption agree about who the mouths are.
    ///
    /// <para/><b>No roll, ever.</b> This runs inside the tick loop, where a single draw would shift every
    /// subsequent roll in the game (<c>Quests.QuestRewardSink</c> and <see cref="SettlementStockInitiative"/>
    /// both say the same thing about themselves). Which food is eaten is a total order over the ledger —
    /// preferability, then nutrition per unit, then <c>defName</c> — so it is decided, not drawn; how much is
    /// arithmetic the map path already does. <c>SettlementLarderTests</c> pins it on
    /// <c>Rand.Current.Iterations</c>.
    ///
    /// <para/><b>No state of its own.</b> Everything is re-derived from the roster and the ledger on each
    /// gated pass, so there is nothing here to Scribe; the ledger persists because
    /// <see cref="Settlement.ExposeData"/> already writes it.
    ///
    /// <para/><b>What this did not close, and what closed it.</b> Two things, both named here when this class
    /// landed so neither would be mistaken for working, and both since closed by
    /// <see cref="SettlementSubsistence"/> — the production half of the same ledger:
    /// <list type="number">
    /// <item><b>Nothing produced food at civilization scale.</b> An unwatched settlement has no map, and
    /// foraging, farming, cooking and hunting are all map-side; the one abstract producer this port had
    /// (<c>Crafting.Guild</c>) ships a single recipe and it cuts stone. So a settlement nobody opened ate its
    /// founding rations down over eleven in-game days and then starved to lethal <c>Malnutrition</c> by day
    /// twenty. <see cref="SettlementSubsistence"/> now grows food into the ledger for the citizens who have
    /// no map to grow it on — the same citizens this class feeds — at a rate their land sets, capped at the
    /// larder <c>AI.HuntingTuning.DaysOfFoodWanted</c> already names.</item>
    /// <item><b>Only the player's founding band was provisioned.</b>
    /// <see cref="ProvisionFoundingBand"/> was called from <c>Sim.Game.NewGame</c>, the orchestrator that
    /// founds the settlement a game starts on, so a band founded during play by
    /// <c>World.EmergenceManager</c> — every rival civilization in a solo start (spec §5b.4) — walked in with
    /// an empty ledger. The call could not go beside the founding in <c>World.SettlementFounder.Found</c>
    /// because that would make <c>World/</c> reference this layer (spec §2: "a module never references a
    /// layer above it"); the owner is at this layer instead, in
    /// <see cref="SettlementSubsistence.ProvisionIfNewlyFounded"/>, which reaches every settlement however it
    /// was founded and pays each exactly once.</item>
    /// </list>
    /// </summary>
    public static class SettlementLarder
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
            if (Find.TickManager.TicksGame % SettlementLarderTuning.IntervalTicks != 0) return;
            foreach (Settlement settlement in world.Settlements) Run(settlement);
        }

        /// <summary>One settlement, self-gated on the same cadence — for a caller that has a settlement in
        /// hand rather than a world.</summary>
        public static int TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (Find.TickManager.TicksGame % SettlementLarderTuning.IntervalTicks != 0) return 0;
            return Run(settlement);
        }

        /// <summary>
        /// The ungated pass: feeds every citizen of <paramref name="settlement"/> who is hungry and has no
        /// map to eat on, and returns how many ate. Public so a test (or a future caller) can drive one pass
        /// without arranging for the tick number to land on the interval.
        /// </summary>
        public static int Run(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            // Settle the cheap question first: a settlement holding nothing edible cannot feed anybody and
            // must not — it is starving, which is a true statement about it and not a case to paper over.
            // Costs one walk of the ledger's def list, and most settlements most of the time stop here.
            if (BestStoredFood(settlement) == null) return 0;

            int fed = 0;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (TryFeed(settlement, citizens[i])) fed++;
            }
            return fed;
        }

        // -----------------------------------------------------------------------------------------------
        // The seam itself.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Feeds <paramref name="citizen"/> one sitting out of <paramref name="settlement"/>'s ledger, and
        /// returns whether they ate. <b>Moves</b>, exactly as <see cref="SettlementStockInitiative"/> does in
        /// the other direction: the ledger is debited by the same units whose nutrition the stomach is
        /// credited with, in one step, so no unit is ever counted in two places.
        /// </summary>
        public static bool TryFeed(Settlement settlement, Pawn citizen)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (citizen == null) throw new ArgumentNullException(nameof(citizen));
            if (!WouldEatFromStores(citizen)) return false;

            ThingDef? food = BestStoredFood(settlement);
            if (food == null) return false;

            // The sitting the map path would have taken, capped by what the civilization actually holds:
            // one unit of every raw foodstuff this port ships is 0.05 nutrition against a 1.0 stomach, so
            // "a unit per pass" here would be the same starvation bug JobDriver_Ingest had (see
            // FoodUtility.WillIngestStackCountOf) wearing a different hat.
            int count = Math.Min(FoodUtility.WillIngestStackCountOf(citizen, food), settlement.StoreCountOf(food));
            if (count < 1) return false;

            settlement.AddStore(food, -count);
            citizen.needs.food!.Eat(food.ingestible!.nutrition * count);
            return true;
        }

        /// <summary>
        /// Whether this citizen eats from the ledger at all: alive, not suspended, <b>not standing on a
        /// map</b>, and hungry by exactly the test the map path gates on
        /// (<c>AI.ThinkNode_ConditionalHungry</c>: any category but <see cref="HungerCategory.Fed"/>).
        /// See the class doc for why the map question is <c>Spawned</c> rather than a tier.
        /// </summary>
        public static bool WouldEatFromStores(Pawn citizen)
        {
            if (citizen == null) throw new ArgumentNullException(nameof(citizen));
            if (citizen.Dead || citizen.Suspended || citizen.Spawned) return false;

            Need_Food? food = citizen.needs?.food;
            return food != null && food.CurCategory != HungerCategory.Fed;
        }

        /// <summary>
        /// The best thing in <paramref name="settlement"/>'s ledger to eat, or null when it holds nothing
        /// edible. "Best" is RimWorld's own answer — <see cref="FoodPreferability"/>, the enum whose own doc
        /// says it is "ordered so a higher value always beats a lower one" — then nutrition per unit, then
        /// <c>defName</c>. <b>A total order with no draw in it</b>: the last term cannot tie, so the choice
        /// never depends on ledger insertion order and never touches the shared <see cref="Rand"/> stream.
        /// </summary>
        public static ThingDef? BestStoredFood(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            ThingDef? best = null;
            foreach (KeyValuePair<ThingDef, int> stored in settlement.Stores)
            {
                if (stored.Value <= 0) continue;
                // The same predicate the map path eats by (AI.JobGiver_GetFood), so a def is food in the
                // ledger exactly when it would have been food lying on the ground.
                if (!stored.Key.IsNutritionGivingIngestible) continue;
                if (best == null || Beats(stored.Key, best)) best = stored.Key;
            }
            return best;
        }

        private static bool Beats(ThingDef candidate, ThingDef incumbent)
        {
            IngestibleProperties a = candidate.ingestible!;
            IngestibleProperties b = incumbent.ingestible!;

            if (a.preferability != b.preferability) return a.preferability > b.preferability;
            if (a.nutrition != b.nutrition) return a.nutrition > b.nutrition;
            return string.CompareOrdinal(candidate.defName, incumbent.defName) < 0;
        }

        /// <summary>Nutrition <paramref name="settlement"/> holds abstractly — the ledger half of
        /// <c>AI.HuntingInitiative.NutritionAvailable</c>, which is the figure this class spends and the
        /// only one it can.</summary>
        public static float StoredNutrition(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            float total = 0f;
            foreach (KeyValuePair<ThingDef, int> stored in settlement.Stores)
            {
                if (!stored.Key.IsNutritionGivingIngestible) continue;
                total += stored.Key.ingestible!.nutrition * stored.Value;
            }
            return total;
        }

        // -----------------------------------------------------------------------------------------------
        // The founding band's rations.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Credits <paramref name="settlement"/> with the provisions its founding band walked in carrying,
        /// and returns how many units that was.
        ///
        /// <para/><b>Why this is not optional.</b> Measured on shipped content, a newly founded settlement's
        /// ledger held <c>MeleeWeapon_Knife x2</c> and nothing else: the three scenarios ship a weapon each
        /// and no food at all, and nothing else writes the ledger at founding. So closing the consumption
        /// path above honestly still leaves a day-one settlement with an empty larder — the abstract eater
        /// would correctly find nothing and correctly starve. A band that has walked for days to get here
        /// arrives carrying food; this is that food, and it is the only thing in this file that <i>adds</i>
        /// to a ledger.
        ///
        /// <para/><b>What it is, and how much.</b> The densest ration the content ships that is not a cooked
        /// meal — travel food, chosen by rule rather than by name so a new ration ships into this with no
        /// code change (in the current content that is <c>Pemmican</c>, whose own description is "a dense,
        /// long-lasting ration ... keeps almost forever"). Meals are excluded because a cooked meal is a
        /// day's food rather than a season's, and this port has no spoilage model to say so. The amount is
        /// <see cref="SettlementLarderTuning.ProvisionNutritionPerMouth"/> per living citizen — a derivation
        /// off the shipped crops' own growDays and the food economy's own per-eater demand, never a literal.
        ///
        /// <para/><b>Idempotent by construction? No — and deliberately so.</b> This is a founding act and is
        /// called exactly once per settlement, by <see cref="SettlementSubsistence.ProvisionIfNewlyFounded"/>
        /// on the one gated pass that falls inside a settlement's founding window (it used to be called by
        /// <c>Sim.Game.NewGame</c>, which reached only the player's band — see this class's own "what this did
        /// not close"). Nothing else may call it on a cadence: food appearing in a ledger every interval would
        /// be exactly the "conjure nutrition from nothing" this module exists to avoid.
        /// </summary>
        public static int ProvisionFoundingBand(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            int mouths = 0;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (!citizens[i].Dead) mouths++;
            }
            if (mouths <= 0) return 0;

            ThingDef? ration = ProvisionDef();
            if (ration == null) return 0;

            float wanted = mouths * SettlementLarderTuning.ProvisionNutritionPerMouth;
            int units = (int)Math.Ceiling(wanted / ration.ingestible!.nutrition);
            if (units <= 0) return 0;

            settlement.AddStore(ration, units);
            return units;
        }

        /// <summary>
        /// What a founding band carries: the most nutrition-dense ingestible in content that is not a cooked
        /// meal, ties broken by <c>defName</c> — a total order, read off the content that already says so
        /// (the same rule <c>Building.FarmingInitiative.CropFor</c> and
        /// <c>Crafting.StonecutterInitiative.IsStonecutting</c> follow rather than naming a def). Falls back
        /// to the densest ingestible of any kind if content ships nothing but meals, and null if it ships no
        /// food at all.
        /// </summary>
        public static ThingDef? ProvisionDef()
        {
            ThingDef? bestRation = null;
            ThingDef? bestAny = null;

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (!def.IsNutritionGivingIngestible) continue;
                if (bestAny == null || Denser(def, bestAny)) bestAny = def;
                if (def.IsMeal) continue;
                if (bestRation == null || Denser(def, bestRation)) bestRation = def;
            }
            return bestRation ?? bestAny;
        }

        private static bool Denser(ThingDef candidate, ThingDef incumbent)
        {
            float a = candidate.ingestible!.nutrition;
            float b = incumbent.ingestible!.nutrition;
            if (a != b) return a > b;
            return string.CompareOrdinal(candidate.defName, incumbent.defName) < 0;
        }
    }
}
