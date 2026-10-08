using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// Tuning for <see cref="SettlementMedicine"/>. RimWorld has nothing to port: its colony's medicine is the
    /// medicine lying on its one map and its player decides how much is enough, so the figure is this port's
    /// own, pinned by behaviour (a bigger settlement wants a bigger stock) rather than trusted as a literal.
    /// </summary>
    public static class SettlementMedicineTuning
    {
        /// <summary>
        /// Units of medicine a settlement wants within reach of its tenders, per citizen. <b>A design
        /// number, not RimWorld's.</b> Every tend spends exactly one unit
        /// (<c>AI.JobDriver_TendPatient.MedicinePerTend</c>), and a citizen is wounded or sick more than once
        /// in a stretch of play, so a stock of a couple per head is roughly one bad week for everybody rather
        /// than a single tend each. It does two jobs and they are the same number on purpose:
        /// <list type="bullet">
        /// <item>it is the line below which <c>Building.FarmingInitiative</c> decides the settlement needs a
        /// medicine crop in the ground, and</item>
        /// <item>it is the reserve <see cref="SettlementMedicine"/> keeps on the map when the rest of a
        /// settlement's goods are banked, so a settlement that decides it is short and one that decides it is
        /// stocked are always looking at the same stock.</item>
        /// </list>
        /// A founding band of twenty-five on twenty herbal medicine starts under it, which is the point: the
        /// settlement should start its herb garden while the starting stock is still being spent, not after.
        /// </summary>
        public const float MedicineWantedPerCitizen = 2f;
    }

    /// <summary>
    /// <b>Medicine is the one good a settlement banks that its own people have to be able to reach.</b>
    /// The map-to-ledger seam (<see cref="SettlementStockInitiative"/>) moves every non-food unit resting in a
    /// stockpile off the map and into <see cref="Settlement.Stores"/>, and the ledger-to-map half issues
    /// <i>food only</i>. Food is protected by a reserve — bank only what is above the larder the settlement
    /// wants in hand — and medicine had no such protection, with two consequences, both found while giving
    /// herbal medicine a source:
    /// <list type="number">
    /// <item><b>Medicine never reached a tender.</b> <c>Health.MedicineUtility.FindBestMedicine</c> looks for
    /// spawned items on the map, and a founding band's starting medicine is credited to the ledger by
    /// <c>Sim.Game.NewGame</c> (there is no map yet) and nothing ever put it on one. A watched settlement
    /// tended without medicine from its first day, whatever its scenario said it started with.</item>
    /// <item><b>A harvest would have vanished.</b> Healroot's medicine is hauled to the granary like anything
    /// else, and banking destroys the stack and credits the ledger. Growing medicine would have changed
    /// nothing a player could see.</item>
    /// </list>
    ///
    /// <para/><b>The translation, which is the food rule applied to a second good.</b> A settlement keeps
    /// <see cref="Wanted"/> units of medicine on its map, banks only what is above that, and issues the
    /// shortfall back from the ledger. The invariant is the stock initiative's own and stays true: a unit is
    /// a Thing on a map or a count in the ledger, never both; every move here credits one side and debits the
    /// other in the same step.
    ///
    /// <para/><b>Any medicine counts</b> (<c>ThingDef.IsMedicine</c>, the test <c>FindBestMedicine</c> uses),
    /// not only herbal: the reserve is "what the tenders can reach", and they take the most potent they find.
    ///
    /// <para/><b>No state of its own</b>, like the initiative it plugs into: everything is re-derived from the
    /// map and the ledger on each pass, so there is nothing here to Scribe.
    /// </summary>
    public static class SettlementMedicine
    {
        /// <summary>
        /// Units of medicine this settlement wants within reach: <see cref="SettlementMedicineTuning.MedicineWantedPerCitizen"/> per living citizen (rounded up, so a settlement of one wants two).
        /// Citizens only -- never <see cref="Settlement.StatisticalPopulation"/>, for the reason
        /// <c>AI.HuntingInitiative</c> gives. On a map no settlement owns, the humanlike pawns standing on it.
        /// </summary>
        public static int Wanted(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return (int)Math.Ceiling(MouthCount(settlement, map) * SettlementMedicineTuning.MedicineWantedPerCitizen);
        }

        /// <summary>Units of any medicine lying spawned on <paramref name="map"/> -- what a tender could walk
        /// to. Not the ledger: see the class doc.</summary>
        public static int OnMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            int total = 0;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (t.Spawned && t.def.IsMedicine) total += t.stackCount;
            }
            return total;
        }

        /// <summary>Units of any medicine in <paramref name="settlement"/>'s ledger (0 for no settlement).</summary>
        public static int InStores(Settlement? settlement)
        {
            if (settlement == null) return 0;

            int total = 0;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
            {
                if (kv.Key.IsMedicine) total += kv.Value;
            }
            return total;
        }

        /// <summary>The honest total of both books, the medicine counterpart of
        /// <c>AI.HuntingInitiative.NutritionAvailable</c>: used to ask whether the settlement <i>has</i>
        /// medicine, never whether a tender can reach it.</summary>
        public static int Stock(Settlement? settlement, Map.Map map) => OnMap(map) + InStores(settlement);

        /// <summary>
        /// Whether the settlement is short of medicine: less in hand, map and ledger together, than
        /// <see cref="Wanted"/>. Both books, because stock sitting in the ledger is stock this class issues
        /// onto the map within a pass (<see cref="IssueFromStores"/>); counting the map alone would read the
        /// instant before that as a shortage.
        /// </summary>
        public static bool IsShort(Settlement? settlement, Map.Map map) =>
            Stock(settlement, map) < Wanted(settlement, map);

        /// <summary>
        /// How much of a medicine <paramref name="stack"/> resting in a stockpile may be banked:
        /// nothing while the map holds no more than <see cref="Wanted"/>, and only the excess beyond it
        /// otherwise. <paramref name="surplus"/> is the map's current excess and is drawn down as it is spent,
        /// so one pass cannot bank the same surplus twice across several stacks -- the shape
        /// <c>SettlementStockInitiative.BankableUnits</c> gives food.
        /// </summary>
        public static int BankableUnits(Thing stack, ref int surplus)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));
            if (surplus <= 0) return 0;

            int take = Math.Min(stack.stackCount, surplus);
            surplus -= take;
            return take;
        }

        /// <summary>The medicine banking may take from the map this pass: what the map holds above
        /// <see cref="Wanted"/>. Computed once per pass, then drawn down by <see cref="BankableUnits"/>.</summary>
        public static int SurplusOnMap(Settlement settlement, Map.Map map) =>
            Math.Max(0, OnMap(map) - Wanted(settlement, map));

        /// <summary>
        /// The crossing the food half already has and medicine was missing: <b>ledger to map</b>. Moves
        /// medicine out of <paramref name="settlement"/>'s ledger and onto <paramref name="map"/> until the
        /// map holds <see cref="Wanted"/>. Returns the units issued.
        ///
        /// <para/>Bounded by the shortfall, not by the ledger, so opening a settlement a century in cannot
        /// dump a warehouse onto its map. The most potent kind goes first -- the order tenders prefer in
        /// <c>MedicineUtility.FindBestMedicine</c>. Debits in the same step that spawns, through the stock
        /// initiative's own placement, so it lands where issued food lands.
        /// </summary>
        public static int IssueFromStores(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            int deficit = Wanted(settlement, map) - OnMap(map);
            if (deficit <= 0) return 0;

            int issued = 0;
            while (deficit > 0)
            {
                ThingDef? def = BestStoredMedicine(settlement);
                if (def == null) break;

                int take = Math.Min(deficit, settlement.StoreCountOf(def));
                if (take <= 0) break;

                int placed = SettlementStockInitiative.PlaceIssued(def, take, map);
                if (placed <= 0) break;

                settlement.AddStore(def, -placed);
                deficit -= placed;
                issued += placed;

                // Short of what was asked for means the map ran out of room, not that another def would fare
                // better; trying the next one would spin.
                if (placed < take) break;
            }
            return issued;
        }

        /// <summary>The ledger's most potent medicine, ties broken by defName so two runs of the same state
        /// issue the same thing -- no draw on the shared random stream, and the probe depends on that.</summary>
        public static ThingDef? BestStoredMedicine(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            ThingDef? best = null;
            float bestPotency = -1f;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
            {
                ThingDef def = kv.Key;
                if (kv.Value <= 0 || !def.IsMedicine) continue;

                float potency = def.GetStatValue(HealthStatDefOf.MedicalPotency);
                if (potency > bestPotency
                    || (potency == bestPotency && best != null && string.CompareOrdinal(def.defName, best.defName) < 0))
                {
                    best = def;
                    bestPotency = potency;
                }
            }
            return best;
        }

        /// <summary>The settlement's living citizens, or -- with no settlement -- the humanlike pawns standing
        /// on the map. The same population <c>AI.HuntingInitiative</c> feeds.</summary>
        public static int MouthCount(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            int count = 0;
            if (settlement != null)
            {
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int i = 0; i < citizens.Count; i++)
                {
                    if (!citizens[i].Dead) count++;
                }
                return count;
            }

            IReadOnlyList<Pawn> onMap = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < onMap.Count; i++)
            {
                if (onMap[i].RaceProps.Humanlike && !onMap[i].Dead) count++;
            }
            return count;
        }
    }
}
