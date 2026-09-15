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
    /// player paints on individual animals. <b>There is no Designation system in this codebase at all</b>, so
    /// that predicate cannot be ported; something has to answer "which animals should be shot?" in its place.
    /// Two answers already exist here and neither fits on its own:
    /// <list type="bullet">
    /// <item><see cref="WorkGiver_Miner"/> translated the same problem away by dropping the designation
    /// entirely — it mines <i>any</i> reachable mineable edifice. That works for rock (a colony genuinely
    /// wants all of it, and rock does not bleed) and would be catastrophic here: hunters would kill every
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
    /// <b>This class's first answer was to count both, and that was the wrong half of the trap.</b> Counting
    /// both made the gate terminate in principle and never open in practice, because the ledger a watched
    /// settlement holds is food its own people cannot eat — measured, false on every sample across twelve
    /// in-game days on a founded settlement. The gate reads the map half alone now
    /// (<see cref="NutritionReachable"/>); <see cref="WantsMeat"/>'s own doc carries the measurement, the
    /// argument, and the one change that would make counting both correct again.
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
        /// want a hunt. Strictly "less in reach than wanted" — no hysteresis band, so the gate reopens the
        /// moment food falls back below the target rather than waiting for a second threshold.
        ///
        /// <para/><b>It reads <see cref="NutritionReachable"/>, not <see cref="NutritionAvailable"/>, and
        /// that correction is the reason hunting could never happen.</b> Measured over twelve in-game days on
        /// a settlement founded the ordinary way, this returned false on every sample: a founding band is
        /// credited <c>Economy.SettlementLarderTuning.ProvisionNutritionPerMouth</c> into
        /// <see cref="Settlement.Stores"/> — hundreds of nutrition against a want of a hundred and sixty —
        /// and <b>nothing draws that ledger down while the settlement has a map</b>, because
        /// <c>Economy.SettlementLarder</c> feeds only citizens who are <i>not</i> spawned. So the hunters
        /// were reading a granary their own people cannot eat out of, and concluding they were rich. Shut
        /// from tick zero, permanently, for every settlement anyone opens.
        ///
        /// <para/><b>The fix states the seam rather than moving a threshold.</b> A hunt is map work: it is
        /// done by people standing on a map, it feeds people standing on a map, and the meat it produces
        /// lands on that map. The food that answers it is therefore the food on that map. The abstract ledger
        /// is the other book — <c>Economy.SettlementLarder</c> spends it, <c>Economy.SettlementSubsistence</c>
        /// fills it, and both deal exclusively with citizens who have no map — and hunting is not in it.
        /// This also makes the gate agree exactly with the one other reader of these two figures:
        /// <c>Economy.SettlementStockInitiative</c> banks only what is <i>above</i>
        /// <see cref="NutritionWanted"/> measured against the map alone, so one number now has two signs —
        /// below the reserve the settlement hunts, above it the surplus is banked and
        /// <see cref="TamingInitiative"/> spends it on livestock. Before this they were two numbers that
        /// disagreed.
        ///
        /// <para/><b>What is genuinely still open, named rather than hidden by this.</b> There is no
        /// <i>unbanking</i> pass anywhere: nothing ever turns a count in <see cref="Settlement.Stores"/> back
        /// into a <c>Thing</c> on a map, so a watched settlement can hold a full granary and starve beside
        /// it. That is a real defect and it is not this one — it belongs to the module that owns the ledger's
        /// two writers. <b>The day it lands, this method should go back to reading
        /// <see cref="NutritionAvailable"/></b>, because the ledger will then be food the hunters' own people
        /// can actually eat, and counting it will be the truth instead of the trap it is today.
        /// </summary>
        public static bool WantsMeat(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return NutritionReachable(map) < NutritionWanted(settlement, map);
        }

        /// <summary>
        /// Nutrition a pawn standing on <paramref name="map"/> could actually walk to and eat: everything
        /// edible lying on it, and nothing else. The figure <see cref="WantsMeat"/> gates on — see that
        /// method for why the abstract ledger is deliberately not in it — and the same one
        /// <c>Economy.SettlementStockInitiative</c> measures its banking reserve against.
        /// </summary>
        public static float NutritionReachable(Map.Map map) => NutritionAvailable(null, map);

        /// <summary>
        /// Nutrition the settlement holds at civilization scale: everything edible lying on
        /// <paramref name="map"/> plus everything edible in <paramref name="settlement"/>'s abstract
        /// <see cref="Settlement.Stores"/> ledger. The honest total of both books — used for "how much food
        /// does this town have", never for "should somebody go hunting", which is
        /// <see cref="NutritionReachable"/>'s question and that method's own doc explains why.
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

            float perDay = 0f;
            foreach (Pawn eater in Eaters(settlement, map))
            {
                perDay += HuntingTuning.NutritionPerEaterPerDay * eater.HungerRate;
            }
            return perDay * HuntingTuning.DaysOfFoodWanted;
        }

        /// <summary>The mouths this hunt is for — see this class's own doc on why Statistical population is
        /// never among them.</summary>
        private static IEnumerable<Pawn> Eaters(Settlement? settlement, Map.Map map)
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

            IReadOnlyList<Pawn> onMap = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < onMap.Count; i++)
            {
                if (onMap[i].RaceProps.Humanlike && !onMap[i].Dead) yield return onMap[i];
            }
        }
    }
}
