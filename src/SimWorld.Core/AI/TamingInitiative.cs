using System;
using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.AI
{
    /// <summary>
    /// Translation: <b>a settlement decides for itself when it wants livestock</b>, because there is no
    /// player to designate an animal for taming — the mirror of <see cref="HuntingInitiative"/>, and the
    /// piece whose absence kept hunting from ever happening.
    ///
    /// <para/><b>The defect this closes, measured.</b> Wild animals reached interior maps in the previous
    /// batch and hunting still never happened: twelve in-game days on a watched settlement produced
    /// <b>zero hunts and sixty-six tamings</b>. The population was not hunted flat, it was tamed flat. The
    /// cause is an asymmetry rather than a priority number. In RimWorld <i>both</i> verbs are gated on a
    /// player designation — <c>WorkGiver_HunterHunt</c> and <c>WorkGiver_InteractAnimal</c> both draw their
    /// work from <c>designationManager.SpawnedDesignationsOfDef(...)</c>. This port has no Designation system
    /// at all, and when that predicate was translated away hunting got a civilization-scale substitute
    /// (<see cref="HuntingInitiative"/>) and <b>taming got nothing</b>:
    /// <see cref="WorkGiver_TameAnimals.PotentialWorkThingsGlobal"/> yielded every wild animal on the map
    /// with no gate but reachability and reservation. <c>Handling</c> is naturalPriority 950 against
    /// <c>Hunting</c>'s 850, so every wild animal was unconditional higher-priority work and the hunt giver
    /// was never consulted. <see cref="WorkGiver_Hunt"/>'s own doc claimed that ordering "needed no
    /// special-casing here"; it is corrected there.
    ///
    /// <para/><b>Why an initiative and not a Designation layer, stated as a decision.</b> The obvious reading
    /// is that the missing thing is RimWorld's <c>DesignationManager</c>. It is not, and the difference
    /// matters because a designation layer on its own <i>changes nothing</i>: with no player, an empty
    /// designation store means both givers find no work and the game gets quieter rather than more correct.
    /// Something has to <i>issue</i> the designation, and that something can only be a civilization-scale
    /// reader of settlement state — which is exactly this class. So the real choice is between
    /// <c>initiative → designation → giver</c> and <c>initiative → giver</c>, and the middle hop buys
    /// nothing this port can spend today. Against it: five givers here have already translated the same
    /// predicate away and recorded it at the site (<see cref="WorkGiver_Miner"/>, <see cref="WorkGiver_PlantsCut"/>,
    /// <see cref="WorkGiver_Repair"/>, <see cref="WorkGiver_Hunt"/>, and <see cref="Building.WorkGiver_GrowerSow"/>
    /// through <see cref="Building.FarmingInitiative"/>), so adding the layer now would either leave the port
    /// half designation-driven and half initiative-driven, or mean retrofitting all five. <b>What is missing
    /// is a reason, not a record.</b>
    ///
    /// <para/><b>What a Designation layer would still buy, and why it can wait.</b> Three things, all real
    /// and none of them load-bearing for "does a settlement that needs meat hunt": a durable, save-visible
    /// record of intent the god view could show and a host could write into; one place for every work type to
    /// express "this thing, not that one", so mine/cut/hunt/tame stop each inventing their own predicate; and
    /// stability, since a designation persists while a live-read gate can flip mid-job (bounded here — a hunt
    /// carries <see cref="HuntingTuning.HuntJobExpiryTicks"/> and the driver is not re-gated once started).
    /// That is a consolidation across six working givers, worth doing on its own terms and not a prerequisite
    /// for this.
    ///
    /// <para/><b>The rule, and it is one sentence: a settlement keeps as much livestock as its surplus feeds,
    /// and while it is short of food the animals on its land are meat rather than livestock.</b> Both halves
    /// read the same number <see cref="HuntingInitiative.WantsMeat"/> reads, so the two verbs can never both
    /// be on:
    /// <list type="number">
    /// <item><b>Food first.</b> <see cref="HuntingInitiative.WantsMeat"/> true ⇒ no taming at all. This is
    /// the ordering fix, and note what it is <i>not</i>: nothing here touches <c>naturalPriority</c>.
    /// Handling still outranks Hunting exactly as RimWorld's content says, and that is now harmless, because
    /// the higher-priority giver correctly finds no work in the state where the lower one must run.</item>
    /// <item><b>Then only out of surplus.</b> Everything reachable above the larder the settlement wants in
    /// hand is divided by what one animal eats over that same horizon — see <see cref="HerdWanted"/>. A
    /// settlement living hand to mouth wants no herd; one with a full granary can afford some.</item>
    /// </list>
    ///
    /// <para/><b>Self-limiting, and that is the whole design.</b> A tame animal is an ordinary <c>Pawn</c>
    /// with <see cref="Needs.Need_Food"/> that walks to the settlement's food and eats it
    /// (<see cref="JobGiver_GetFood"/>), so every head taken on lowers the surplus that decided to take it.
    /// The herd therefore converges instead of running away, and if it overshoots — a harvest banked, then
    /// eaten — taming simply stops until the surplus returns. Nothing untames an animal and nothing here
    /// tries to.
    ///
    /// <para/><b>An honest gap this gate is shaped around.</b> <i>No tame animal in this port repays its
    /// keep yet.</i> The produce comps exist and the content sets them (<c>CompMilkable</c> and
    /// <c>CompShearable</c> on Muffalo, <c>CompEggLayer</c> on Chicken) but nothing anywhere calls
    /// <c>CompHasGatherableBodyResource.Gather</c>, so no milk, wool or egg is ever collected; training
    /// exists but nothing routes a trained animal to haul; and RimWorld's <c>Designation.Slaughter</c> — the
    /// route by which a meat herd becomes meat — has no counterpart here either. So the only honest herd
    /// figure available today is "what we can feed", not "what we need it to produce". <b>When a gatherer or
    /// a slaughter route lands, <see cref="HerdWanted"/> is the one method that changes</b>: the target
    /// becomes the herd that covers the settlement's demand for what the herd makes, and the feed cost below
    /// becomes the ceiling rather than the whole rule. Recorded here rather than papered over with an
    /// invented benefit.
    ///
    /// <para/><b>Read live, never stored.</b> Exactly <see cref="HuntingInitiative"/>'s shape: no state, no
    /// tick hook, nothing to Scribe, and no edit to <c>Sim/Game.cs</c>. Every answer is re-derived from the
    /// map, the roster and the ledger on each job search, so a settlement that saves and loads resumes with
    /// the same answer its state implies.
    /// </summary>
    public static class TamingInitiative
    {
        /// <summary>Whether anyone on <paramref name="map"/> should be taming right now, resolving the owning
        /// settlement (if any) itself — <see cref="HuntingInitiative.SettlementFor"/>, the same lookup the
        /// hunting gate makes, so the two verbs are always asked about one settlement.</summary>
        public static bool WantsLivestock(Map.Map map) =>
            WantsLivestock(HuntingInitiative.SettlementFor(map), map);

        /// <summary>
        /// Whether <paramref name="settlement"/> (null: the map's own population, the same fallback
        /// <see cref="HuntingInitiative"/> makes for a map no world owns) wants another head of livestock.
        /// Food first, then surplus — see this class's own doc.
        /// </summary>
        public static bool WantsLivestock(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            // Short of food (or holding exactly the larder it wants and nothing over): an animal standing on
            // this land is meat, not livestock. This is HuntingInitiative.WantsMeat expressed as the sign of
            // the same subtraction rather than asked again — the two gates are one number, and asking twice
            // would walk the map's items twice on every job search for no second answer.
            float surplus = Surplus(settlement, map);
            if (surplus <= 0f) return false;

            // One walk of the map's pawns answers both halves: how many head are already kept, and what the
            // next one would eat. ShouldSkip runs on every job search for every citizen, so this stays at one
            // walk of the items and one of the pawns, the same order WorkGiver_Hunt.ShouldSkip already pays.
            Census census = TakeCensus(map);
            return census.Tame < HeadsAffordable(surplus, census);
        }

        /// <summary>
        /// Reachable food above the larder the settlement wants in hand — <see cref="HuntingInitiative"/>'s
        /// two figures, subtracted. Negative is exactly <see cref="HuntingInitiative.WantsMeat"/>, which is
        /// why the two verbs can never both be on: one wants this below zero and the other above it.
        /// </summary>
        public static float Surplus(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return HuntingInitiative.NutritionReachable(map) - HuntingInitiative.NutritionWanted(settlement, map);
        }

        /// <summary>
        /// How many head this settlement's food surplus can keep: everything reachable above the larder it
        /// wants in hand (<see cref="HuntingInitiative.NutritionReachable"/> less
        /// <see cref="HuntingInitiative.NutritionWanted"/>), divided by what one animal eats over
        /// <see cref="TamingTuning.DaysOfFeedWanted"/> days.
        ///
        /// <para/><b>Every term is read off something the codebase already decided</b> — there is no literal
        /// in this method. The surplus is the same pair of figures the hunting gate compares and
        /// <c>Economy.SettlementStockInitiative</c> banks against, so a unit of food means one thing across
        /// the whole food economy. The cost per head is the food economy's own per-eater rate
        /// (<see cref="HuntingTuning.NutritionPerEaterPerDay"/>) scaled by the animal's own
        /// <see cref="Pawn.HungerRate"/>, exactly as <see cref="HuntingInitiative.NutritionWanted"/> scales a
        /// citizen's — so a chicken counts for a fraction of a muffalo rather than both counting as "one
        /// animal", and a content pass that adds a heavier species needs no change here.
        ///
        /// <para/>The rate used is the mean over the <i>candidate</i> animals actually standing on this map,
        /// because those are the ones a handler could walk to today; a map with no candidate left on it is
        /// answered with a cost of one full eater's feed rather than a divide by zero, which is the
        /// conservative direction (fewer heads wanted, not infinitely many).
        /// </summary>
        public static int HerdWanted(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            float surplus = Surplus(settlement, map);
            return surplus <= 0f ? 0 : HeadsAffordable(surplus, TakeCensus(map));
        }

        /// <summary>
        /// Nutrition one more head of livestock would eat over <see cref="TamingTuning.DaysOfFeedWanted"/>
        /// days, at the mean <see cref="Pawn.HungerRate"/> of the tameable animals on this map. Falls back to
        /// a full eater's rate (1.0) when there is nothing left to tame — see <see cref="HerdWanted"/>.
        /// </summary>
        public static float FeedCostPerHead(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return FeedCostPerHead(TakeCensus(map));
        }

        /// <summary>
        /// Livestock already standing on <paramref name="map"/>: a live, spawned animal that belongs to
        /// somebody. The complement of <see cref="IsTameable"/> over the same population, and the same
        /// "wild is an animal with no faction" line <see cref="HuntUtility.IsHuntableAnimal"/> and
        /// <c>Pawns.WildAnimalSpawner.CurrentAnimalWeight</c> both draw.
        ///
        /// <para/><b>Counted by head, not by weight</b>, deliberately: what <see cref="HerdWanted"/> answers
        /// is a head count derived from a mean feed cost, so counting the standing herd any other way would
        /// compare two different units.
        /// </summary>
        public static int TameAnimalCount(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return TakeCensus(map).Tame;
        }

        /// <summary>What one walk of a map's pawns answers: the herd already kept, and the appetite of what
        /// is left to take on.</summary>
        private readonly struct Census
        {
            public Census(int tame, int tameable, float tameableHungerRate)
            {
                Tame = tame;
                Tameable = tameable;
                TameableHungerRate = tameableHungerRate;
            }

            public int Tame { get; }

            public int Tameable { get; }

            public float TameableHungerRate { get; }
        }

        private static Census TakeCensus(Map.Map map)
        {
            int tame = 0;
            int tameable = 0;
            float hungerRate = 0f;

            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (!(pawnsOnMap[i] is Pawn animal) || !animal.RaceProps.Animal || animal.Dead) continue;
                if (animal.faction != null)
                {
                    tame++;
                }
                else if (IsTameable(animal))
                {
                    tameable++;
                    hungerRate += animal.HungerRate;
                }
            }
            return new Census(tame, tameable, hungerRate);
        }

        private static float FeedCostPerHead(Census census)
        {
            float meanRate = census.Tameable > 0 ? census.TameableHungerRate / census.Tameable : 1f;
            return TamingTuning.DaysOfFeedWanted * HuntingTuning.NutritionPerEaterPerDay * meanRate;
        }

        private static int HeadsAffordable(float surplus, Census census)
        {
            float feedPerHead = FeedCostPerHead(census);
            return feedPerHead <= 0f ? 0 : (int)(surplus / feedPerHead);
        }

        /// <summary>A live, spawned, unowned animal — <see cref="WorkGiver_TameAnimals"/>'s own predicate,
        /// shared so the gate and the giver can never disagree about what the candidates are.</summary>
        public static bool IsTameable(Pawn animal)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            return animal.RaceProps.Animal && animal.faction == null && animal.Spawned && !animal.Dead;
        }
    }
}
