using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// A settlement as a real entity: population, stores, founding tick, name and growth (spec §5b.5's own
    /// gap table: "today it is a def, a tile and a faction"). Subclasses <see cref="WorldObject"/> rather than
    /// composing one — <see cref="WorldObject"/> is already saved (<see cref="World.worldObjects"/>, deep,
    /// polymorphically: <see cref="Scribe_Values.Look{T}"/> already writes a <c>Class</c> attribute whenever an
    /// item's runtime type differs from the declared one) and already ticked (<see cref="World.WorldTick"/>
    /// calls <see cref="WorldObject.Tick"/> virtually), the exact same shape <see cref="Caravans.Caravan"/>
    /// already uses for its own richer <c>WorldObject</c>. Composing would mean reinventing both.
    /// <para/>
    /// <b>Population is tier-aware, not a flat list (spec §11.3).</b> <see cref="Citizens"/> holds real
    /// <c>Pawn</c> objects only for citizens above Statistical — Full and Interval, the tiers
    /// <c>Pawn_TierTracker</c> keeps a live, identity-bearing object for. A Statistical citizen is, by that
    /// tier's own design, a member of a cohort rather than an individually-simulated record (needs sampled,
    /// health a coarse readout, no per-tick cost) — so this settlement follows the same design one level up
    /// and keeps that slice as <see cref="StatisticalPopulation"/>, a bare running count with no <c>Pawn</c>
    /// behind each person. A settlement of 40,000 mostly-Statistical citizens therefore costs one <c>int</c>,
    /// not 40,000 live objects — <see cref="TotalPopulation"/> and <see cref="PopulationOf"/> answer from that
    /// count directly rather than by enumerating anyone.
    /// <para/>
    /// <b>Growth wires into the real demography mechanism, with one honest seam.</b> <see cref="GrowthTick"/>
    /// runs <c>FamilyManager.DemographyTick</c> against <see cref="Citizens"/> exactly as any other population
    /// would (marriages, births, deaths from age — see <c>FamilyManager</c>), so a settlement whose people are
    /// still real objects grows for real, not decoratively. But that mechanism fundamentally needs a real
    /// <c>Pawn</c> per person to pair into marriages and roll births against — it has nothing to run against
    /// <see cref="StatisticalPopulation"/>'s bare count, and giving it 40,000 real objects to run against would
    /// undo the entire point of the tier. <see cref="StatisticalPopulation"/> instead grows by
    /// <see cref="SettlementTuning.StatisticalNetGrowthPerYear"/> each interval — the measured aggregate
    /// outcome of that same mechanism (§11.5), not a second, independently-tuned curve; see the constant's own
    /// doc. This is the one place this module knowingly diverges from "wire it to the real mechanism" for a
    /// slice of the population that mechanism cannot, by the tier's own design, run against — see the module's
    /// report for the full reasoning.
    /// </summary>
    public class Settlement : WorldObject
    {
        public string name = "";

        /// <summary>Game tick this settlement was founded (<see cref="TickManager.TicksGame"/> at the time).</summary>
        public int foundingTick;

        /// <summary>
        /// Real <c>Pawn</c> objects for every citizen above Statistical tier (Full and Interval — see the
        /// class doc). Never holds a Statistical citizen. Nothing in this module moves a citizen out of this
        /// list and into <see cref="StatisticalPopulation"/> on its own — §11.5 leaves the Interval-to-
        /// Statistical demotion *policy* (who, when) to the director, and this module only builds the shape
        /// that policy will need once it exists.
        /// </summary>
        private List<Pawn> citizens = new List<Pawn>();

        /// <summary>Citizens counted, not individually modelled: identity beyond the running total is not
        /// tracked at the Statistical tier already (<c>Pawn_TierTracker</c>'s own design), so this is a
        /// faithful representation of that slice, not merely a cheaper one.</summary>
        private int statisticalPopulation;

        /// <summary>Simple def→count ledger (spec: "keep it a simple def→count ledger; do not build
        /// ThingOwner"). Deliberately not a <c>ThingOwner</c> of real Things with hit points, quality or
        /// stacking — that gap is separate and out of this module's scope.</summary>
        private readonly Dictionary<ThingDef, int> stores = new Dictionary<ThingDef, int>();

        /// <summary>For Scribe's deep-load construction.</summary>
        public Settlement()
        {
        }

        public Settlement(WorldObjectDef def, int tile, Faction? faction, string name, int foundingTick)
            : base(def, tile, faction)
        {
            this.name = name ?? throw new ArgumentNullException(nameof(name));
            this.foundingTick = foundingTick;
        }

        // ---- population ----

        /// <summary>Real <c>Pawn</c> objects for Full/Interval citizens. Never the whole population — see the
        /// class doc; read <see cref="StatisticalPopulation"/> or <see cref="TotalPopulation"/> for the rest.</summary>
        public IReadOnlyList<Pawn> Citizens => citizens;

        /// <summary>Citizens held as a bare count with no individual <c>Pawn</c> object (see the class doc).</summary>
        public int StatisticalPopulation => statisticalPopulation;

        /// <summary>Total population across every tier, answered from <see cref="Citizens"/>' count plus
        /// <see cref="StatisticalPopulation"/> directly — never by materialising or enumerating a Statistical
        /// citizen, which by design does not exist as an object to enumerate.</summary>
        public int TotalPopulation => citizens.Count + statisticalPopulation;

        /// <summary>
        /// Population of one tier. Statistical answers directly from <see cref="StatisticalPopulation"/> (no
        /// enumeration); Full/Interval scan <see cref="Citizens"/>, which by design holds only those two
        /// tiers and is expected to stay small relative to a large settlement's total (the attended/significant
        /// slice, per §11.3) — never the whole population, so this scan never costs what counting 40,000
        /// people would.
        /// </summary>
        public int PopulationOf(PawnTier tier) =>
            tier == PawnTier.Statistical ? statisticalPopulation : citizens.Count(p => p.tier.Tier == tier);

        public void AddCitizen(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            citizens.Add(pawn);
        }

        /// <summary>
        /// Seats <paramref name="count"/> more people directly into the Statistical cohort with no live
        /// <c>Pawn</c> object each — the shape a settlement of tens of thousands needs (spec §11.3). Used by
        /// growth beyond what a live roster should carry, and directly by tests proving the
        /// query-without-instantiation constraint the module was built to satisfy.
        /// </summary>
        public void AddStatisticalPeople(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            statisticalPopulation += count;
        }

        // ---- stores ----

        public IReadOnlyDictionary<ThingDef, int> Stores => stores;

        public int StoreCountOf(ThingDef def) => def != null && stores.TryGetValue(def, out int c) ? c : 0;

        /// <summary>Sets a store to an exact count; a non-positive count clears the entry rather than keeping
        /// a zero around, so <see cref="Stores"/> only ever lists what the settlement actually holds.</summary>
        public void SetStoreCount(ThingDef def, int count)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (count <= 0) stores.Remove(def);
            else stores[def] = count;
        }

        public void AddStore(ThingDef def, int delta) => SetStoreCount(def, StoreCountOf(def) + delta);

        // ---- growth ----

        public override void Tick(World world)
        {
            base.Tick(world);
            GrowthTick();
        }

        /// <summary>
        /// The settlement's own demographic sweep: gated to the same
        /// <see cref="DemographyTuning.DemographyIntervalTicks"/> cadence <c>FamilyManager.DemographyTick</c>
        /// already gates itself to (so calling this more often than once a year costs nothing extra), then runs
        /// that real mechanism against <see cref="Citizens"/> and grows <see cref="StatisticalPopulation"/> by
        /// its own closed-form rate — see the class doc for why those are two different mechanisms for two
        /// populations that are represented two different ways on purpose.
        /// </summary>
        public void GrowthTick()
        {
            if (Find.TickManager.TicksGame % DemographyTuning.DemographyIntervalTicks != 0) return;

            Find.FamilyManager.DemographyTick(citizens);
            GrowStatisticalCohort();
        }

        private void GrowStatisticalCohort()
        {
            if (statisticalPopulation <= 0) return;
            double grown = statisticalPopulation * (1.0 + SettlementTuning.StatisticalNetGrowthPerYear);
            statisticalPopulation = Math.Max(0, (int)Math.Round(grown));
        }

        // ---- Scribe ----

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref foundingTick, "foundingTick", 0);
            Scribe_Values.Look(ref statisticalPopulation, "statisticalPopulation", 0);

            List<Pawn>? c = citizens;
            Scribe_Collections.Look(ref c, "citizens", LookMode.Deep);
            citizens = c ?? new List<Pawn>();

            Dictionary<ThingDef, int>? storeDict = new Dictionary<ThingDef, int>(stores);
            Scribe_Collections.Look(ref storeDict, "stores", LookMode.Def, LookMode.Value);
            stores.Clear();
            if (storeDict != null)
            {
                foreach (KeyValuePair<ThingDef, int> kv in storeDict) stores[kv.Key] = kv.Value;
            }
        }
    }
}
