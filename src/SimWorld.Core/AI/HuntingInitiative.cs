using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.AI
{
    /// <summary>
    /// Translation: <b>a settlement decides for itself when to hunt</b>, because there is no player to
    /// designate prey.
    /// <para/>
    /// RimWorld's <c>WorkGiver_HunterHunt</c> scans for animals carrying <c>Designation.Hunt</c> — a mark the
    /// player paints on individual animals. <b>There was no Designation system in this codebase when this was
    /// written</b> (there is one now, carrying only <c>Mine</c>: see <see cref="MiningInitiative"/>), so
    /// that predicate could not be ported; something has to answer "which animals should be shot?" in its place.
    /// Two answers already exist here and neither fits on its own:
    /// <list type="bullet">
    /// <item><see cref="WorkGiver_Miner"/> then translated the same problem away by dropping the designation
    /// entirely and mining <i>any</i> reachable mineable edifice. That turned out not to work for rock either
    /// (the whole band mined whenever nothing outranked Mining, so it now mines designated rock only), and it
    /// would be catastrophic here: hunters would kill every
    /// animal on the map forever, including the ones a settlement is trying to tame.</item>
    /// <item><see cref="Building.SettlementConstructionInitiative"/> is the codebase's established shape for
    /// exactly this gap — "no player designation exists, so the settlement decides for itself" — reading real
    /// settlement state (citizen count, the <see cref="Settlement.Stores"/> ledger) to derive what it needs.
    /// This class is that shape applied to food.</item>
    /// </list>
    /// So: <b>a hunter hunts only while its settlement is short of food</b>, and stops when it is not. The
    /// gate is the designation's replacement — self-limiting by construction, because a kill butchered at the
    /// site raises the very number the gate reads (see <see cref="JobDriver_Hunt"/>).
    /// <para/>
    /// <b>Read live, never stored.</b> Unlike construction — which places a durable <see cref="Building.Blueprint"/>
    /// Thing and therefore needed a tick hook in <c>Sim/Game.cs</c> — this initiative holds no state at all:
    /// <see cref="WorkGiver_Hunt.ShouldSkip"/> asks it fresh on every job search, the same way
    /// <see cref="JobGiver_Edicts"/> re-reads active edicts rather than caching them. Nothing to tick, nothing
    /// to Scribe, and no edit to <c>Sim/Game.cs</c> (a file other lanes share) to wire it.
    /// <para/>
    /// <b>What food stock a settlement can actually see — and the trap in it.</b> There are two separate food
    /// ledgers in this codebase and they are not synchronised:
    /// <list type="number">
    /// <item><see cref="Settlement.Stores"/>, a bare <c>ThingDef → int</c> ledger. Its only writers are
    /// scenario starting items (<c>Game.cs</c>), trade (<c>SettlementTradeUtility</c>) and guild production
    /// (<c>Crafting.Guild</c>). <b>Nothing ever credits a spawned Thing into it.</b></item>
    /// <item>Ingestible Things actually lying on the interior map — which is what
    /// <see cref="JobGiver_GetFood"/> sends a hungry pawn to eat, and what butchered meat becomes.</item>
    /// </list>
    /// A gate reading only <see cref="Settlement.Stores"/> (the obvious copy of construction's
    /// <c>StorageTarget</c>) would therefore <i>never close</i>: hunting would raise the map ledger while the
    /// store ledger it was watching stayed flat, and the settlement would hunt forever.
    /// <para/>
    /// <b>This class's first answer was to count both; its second read the map alone; it counts both
    /// again.</b> Counting both was wrong while the ledger was food its own people could not eat, and right
    /// once <c>Economy.SettlementStockInitiative</c> began issuing ledger food back to the map.
    /// <see cref="WantsMeat"/>'s own doc carries both measurements.
    /// <para/>
    /// <b>Who counts as an eater.</b> <see cref="Settlement.Citizens"/> only — never
    /// <see cref="Settlement.StatisticalPopulation"/> — the same line
    /// <see cref="Building.SettlementConstructionInitiative"/> draws and for the same reason: a Statistical
    /// citizen has no <c>Pawn</c> object by the tiering system's design (spec §11.3), so there is nobody there
    /// to eat what a hunter brings back. On a map no settlement owns (a bare test map, or one this port has
    /// no world behind yet) the humanlike pawns standing on it are used instead: the same population reached
    /// by the only other route available, so the giver still works without a generated world.
    /// </summary>
    public static class HuntingInitiative
    {
        /// <summary>The settlement whose <see cref="Settlement.InteriorMap"/> is <paramref name="map"/>, or
        /// null — no world running, or a map nothing owns. Never triggers map generation: it only compares
        /// against maps already built (see <see cref="Settlement.InteriorMap"/>'s own doc).</summary>
        public static Settlement? SettlementFor(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            SimWorld.World.World? world = Find.World;
            if (world == null) return null;
            foreach (Settlement settlement in world.Settlements)
            {
                if (ReferenceEquals(settlement.InteriorMap, map)) return settlement;
            }
            return null;
        }

        /// <summary>Whether anyone on <paramref name="map"/> should be out hunting right now, resolving the
        /// owning settlement (if any) itself.</summary>
        public static bool WantsMeat(Map.Map map) => WantsMeat(SettlementFor(map), map);

        /// <summary>
        /// Whether <paramref name="settlement"/> (null: the map's own population) is short enough of food to
        /// want a hunt: <see cref="ShortOfFood(Settlement?, Map.Map)"/>, the one reading of the food economy's
        /// shortage every famine response shares. Strictly "less held than wanted", no hysteresis band, so the
        /// gate reopens the moment food falls back below the target.
        ///
        /// <para/><b>It reads the map and the ledger, and for two years it read the map alone.</b> The map-only
        /// reading was right when it was written: a founding band's rations sat in
        /// <see cref="Settlement.Stores"/>, nothing issued them, and a gate that counted them was shut from
        /// tick zero while the band went hungry beside them (measured: false on every one of 12,000 samples).
        /// This method's doc said then that the day ledger food could be issued to the map, the gate should
        /// read <see cref="NutritionAvailable"/> again. <c>Economy.SettlementStockInitiative.IssueFromStores</c>
        /// is that day: it keeps the map topped up to <see cref="NutritionWanted"/> out of the ledger on
        /// every pass. Which made the map-only gate wrong the other way. The map is held <i>at</i> the
        /// target, so every mouthful eaten between two passes dropped it just under, and the settlement hunted
        /// while a year's food stood in its granary. Measured on the era arc (band 20, seed 99, threats off,
        /// ten years): hunting carried the store above the year the fields stop at, and the storage huts that
        /// house it reached 404 for 25 people (<c>docs/research/era-arc.md</c> §9.6).
        /// </summary>
        public static bool WantsMeat(Settlement? settlement, Map.Map map) => ShortOfFood(settlement, map);

        /// <summary>Whether the settlement that owns <paramref name="map"/> (or, with none, the people on it)
        /// is short of food, resolving the owner itself.</summary>
        public static bool ShortOfFood(Map.Map map) => ShortOfFood(SettlementFor(map), map);

        /// <summary>
        /// Whether everything <paramref name="settlement"/> holds to eat, on its map and in its ledger
        /// (<see cref="NutritionAvailable"/>), is less than <see cref="HuntingTuning.DaysOfFoodWanted"/> days
        /// of what its people eat (<see cref="NutritionWanted"/>). Read by the hunting gate and by the
        /// growers' early harvest (<c>Building.WorkGiver_GrowerHarvest</c>): both are what a RimWorld player
        /// does when the larder runs low (designate a hunt, designate an unripe crop for harvest), and with no
        /// player the settlement does them on this one figure, so they start and stop together.
        /// </summary>
        public static bool ShortOfFood(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return NutritionAvailable(settlement, map) < NutritionWanted(settlement, map);
        }

        /// <summary>
        /// Nutrition a pawn standing on <paramref name="map"/> could actually walk to and eat: everything
        /// edible lying on it, and nothing else. The figure <c>Economy.SettlementStockInitiative</c> measures
        /// its banking reserve against and <see cref="TamingInitiative"/> its surplus. Not the hunting gate's
        /// any more: see <see cref="WantsMeat"/>.
        /// </summary>
        public static float NutritionReachable(Map.Map map) => NutritionAvailable(null, map);

        /// <summary>
        /// Nutrition the settlement holds at civilization scale: everything edible lying on
        /// <paramref name="map"/> plus everything edible in <paramref name="settlement"/>'s abstract
        /// <see cref="Settlement.Stores"/> ledger. The honest total of both books, and the figure
        /// <see cref="ShortOfFood(Settlement?, Map.Map)"/> reads.
        /// </summary>
        public static float NutritionAvailable(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            float total = 0f;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.def.IsNutritionGivingIngestible) continue;
                total += t.def.ingestible!.nutrition * t.stackCount;
            }

            if (settlement != null)
            {
                foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
                {
                    if (!kv.Key.IsNutritionGivingIngestible) continue;
                    total += kv.Key.ingestible!.nutrition * kv.Value;
                }
            }
            return total;
        }

        /// <summary>
        /// Nutrition wanted in hand: <see cref="HuntingTuning.DaysOfFoodWanted"/> days' worth for every eater,
        /// each at its own <see cref="Pawn.HungerRate"/> rather than a flat per-head figure — a settlement of
        /// children and a settlement of adults do not want the same larder, and the rate is already on the
        /// pawn.
        /// </summary>
        public static float NutritionWanted(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return NutritionEatenPerDay(settlement, map) * HuntingTuning.DaysOfFoodWanted;
        }

        /// <summary>
        /// Nutrition the settlement's people eat in a day at their current hunger rates: the
        /// <see cref="NutritionWanted"/> figure before it is multiplied up to a larder. With no
        /// <paramref name="settlement"/>, the people on <paramref name="map"/>. Statistical population is not among
        /// the eaters, as it never is (see <c>Economy.SettlementLarder</c>).
        /// </summary>
        public static float NutritionEatenPerDay(Settlement? settlement, Map.Map? map)
        {
            float perDay = 0f;
            foreach (Pawn eater in Eaters(settlement, map))
            {
                perDay += HuntingTuning.NutritionPerEaterPerDay * eater.HungerRate;
            }
            return perDay;
        }

        /// <summary>The mouths this hunt is for — see this class's own doc on why Statistical population is
        /// never among them.</summary>
        private static IEnumerable<Pawn> Eaters(Settlement? settlement, Map.Map? map)
        {
            if (settlement != null)
            {
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int i = 0; i < citizens.Count; i++)
                {
                    if (!citizens[i].Dead) yield return citizens[i];
                }
                yield break;
            }

            if (map == null) yield break;
            IReadOnlyList<Pawn> onMap = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < onMap.Count; i++)
            {
                if (onMap[i].RaceProps.Humanlike && !onMap[i].Dead) yield return onMap[i];
            }
        }
    }
}
