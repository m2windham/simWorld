using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for <see cref="AbstractSettlementConstruction"/>. RimWorld has nothing to port here — its player
    /// places every blueprint by hand, and <see cref="SettlementConstructionInitiative"/> already named the
    /// only figures a citizen-driven need model needs (<see cref="ConstructionInitiativeTuning"/>, read here
    /// rather than restated) — so the one genuinely new number is <see cref="CitizensPerBuilder"/>, this port's
    /// own, pinned by <c>AbstractSettlementConstructionTests</c> as a trend (more citizens complete a shortfall
    /// no slower than fewer do), never as the literal.
    /// </summary>
    public static class AbstractConstructionTuning
    {
        /// <summary>Self-gate cadence, reused rather than reinvented: the same clock
        /// <see cref="SettlementConstructionInitiative"/> throttles its own blueprint placement on, so a
        /// settlement's needs are re-measured on one schedule whichever path is answering them.</summary>
        public static int IntervalTicks => ConstructionInitiativeTuning.IntervalTicks;

        /// <summary>
        /// Citizens per "builder" this pass credits toward whatever the settlement needs most. Unsourced —
        /// RimWorld has no autonomous off-map construction to read a labour ratio from — and deliberately
        /// small: a settlement's off-map hands are the same people <see cref="Economy.SettlementSubsistence"/>
        /// already counts as growing its food, so this is a second claim on the same limited labour, not an
        /// extra workforce invented for the purpose. See <see cref="AbstractSettlementConstruction.BuildCapacity"/>
        /// for how this and <see cref="ConstructionInitiativeTuning.MaxBlueprintsPerTick"/> (the same ceiling
        /// the real map path already throttles itself to, reused rather than restated) combine.
        /// </summary>
        public const int CitizensPerBuilder = 5;
    }

    /// <summary>
    /// What a settlement with no interior map builds, over time (translation: citizen-initiated construction
    /// under edicts, off the map — the abstract twin of <see cref="SettlementConstructionInitiative"/>, the
    /// same relationship <see cref="Economy.SettlementSubsistence"/> already has with the map-side food
    /// economy and <see cref="Health.AbstractDiseaseResolver"/> already has with the map-side tend job).
    ///
    /// <para/><b>The defect this closes.</b> <see cref="SettlementConstructionInitiative.TickSettlement(World.Settlement)"/>
    /// returns immediately whenever <see cref="World.Settlement.InteriorMap"/> is null — the honest boundary
    /// for a class whose whole mechanism is placing a <c>Blueprint</c> on a map that does not exist — but food
    /// (<see cref="Economy.SettlementSubsistence"/>) and medicine
    /// (<see cref="Health.AbstractDiseaseResolver.SettlementMedicalCapacity"/>) both already answer for a
    /// settlement nobody has ever opened. Construction was the one need left with no answer there at all: a
    /// settlement founded and never entered built nothing, forever, however many citizens it had or however
    /// long the game ran — which is directly the "infrastructure buys the right to not look" principle failing
    /// for the one system that principle is named after. This class is the answer, at the layer's own scale:
    /// arithmetic over the roster and the ledger, not a placed object.
    ///
    /// <para/><b>A no-op wherever the real class is not.</b> The instant <see cref="World.Settlement.InteriorMap"/>
    /// exists, <see cref="Run"/> returns 0 without touching anything — the map is that settlement's construction
    /// from then on, exactly as <see cref="Economy.SettlementSubsistence"/>'s own doc requires of its own
    /// production ("a unit of goods is either a Thing on a map or a count in Stores, never both"). The two
    /// classes are disjoint by the same test <see cref="World.Settlement.StructureCount"/> already uses for
    /// reading, so there is nothing here to keep in sync — see that method's own doc for the invariant this
    /// class writes into and never reads back out of directly.
    ///
    /// <para/><b>The need model is the real class's, not a second one invented here.</b> A bed per citizen, a
    /// handful of walls once there is anyone to shelter, storage sized to what <see cref="World.Settlement.Stores"/>
    /// holds — the exact targets <see cref="SettlementConstructionInitiative.ComputeNeeds"/> computes, read off
    /// the same <see cref="ConstructionInitiativeTuning"/> constants so the two paths can never disagree about
    /// what a settlement wants, only about how it gets there. The arithmetic itself is restated rather than
    /// shared (CLAUDE.md: add a file rather than edit a shared one — <see cref="SettlementConstructionInitiative"/>
    /// computes its targets against a live <c>Map.Map</c> it needs for material choice, so its private method
    /// cannot be called by a settlement that has none) — a handful of lines duplicated against constants that
    /// cannot drift, not a second model that could.
    ///
    /// <para/><b>Wall material: always the plain <see cref="ConstructionThingDefOf.Wall"/>.</b> The map path
    /// picks the toughest stone a settlement has a whole wall's worth of cut blocks for
    /// (<see cref="StoneWallMaterials.PreferredWallDef"/>), which needs a map to check loose stacks on. Nothing
    /// at civilization scale mints cut stone into <see cref="World.Settlement.Stores"/> today — only food is
    /// produced abstractly — so that check would always answer "none" here regardless; defaulting straight to
    /// the wooden wall is the honest simplification rather than a dead branch kept for symmetry. If an abstract
    /// stonework producer is ever added, this is the seam that would start choosing stone too.
    ///
    /// <para/><b>Rate: derived from citizens, bounded by the same cap the map path already throttles
    /// itself to.</b> See <see cref="BuildCapacity"/>. Not the Statistical cohort — <see cref="World.Settlement.Citizens"/>
    /// only, the same line <see cref="SettlementConstructionInitiative"/> already draws and for the same
    /// reason: a Statistical citizen has no individual <c>Pawn</c> to swing a hammer, by the tiering system's
    /// own design (spec §11.3). This lane follows that existing convention rather than diverging from it — a
    /// settlement whose population is mostly Statistical stays under-built relative to its total headcount
    /// exactly as it already does on the map path once entered, which is a consistent statement about the tier
    /// rather than a new asymmetry this class introduces.
    ///
    /// <para/><b>No roll, ever</b> — the same statement <see cref="Economy.SettlementSubsistence"/> makes about
    /// itself. What is wanted and how much of it is credited this pass are both arithmetic over settlement
    /// state; nothing here draws from any stream.
    /// </summary>
    public static class AbstractSettlementConstruction
    {
        // -----------------------------------------------------------------------------------------------
        // Entry points — the same shape SettlementSubsistence and SettlementConstructionInitiative use.
        // -----------------------------------------------------------------------------------------------

        /// <summary>Civilization-wide entry point, gated and silent with no world running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            if (Find.TickManager.TicksGame % AbstractConstructionTuning.IntervalTicks != 0) return;
            foreach (Settlement settlement in world.Settlements) Run(settlement);
        }

        /// <summary>One settlement, self-gated on the same cadence, for a caller with a settlement in hand
        /// rather than a world. Returns how many structures were credited.</summary>
        public static int TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (Find.TickManager.TicksGame % AbstractConstructionTuning.IntervalTicks != 0) return 0;
            return Run(settlement);
        }

        /// <summary>
        /// The ungated pass: credits <paramref name="settlement"/>'s <see cref="World.Settlement.Structures"/>
        /// ledger with what it built this interval and returns how many that was. Public so a test can drive
        /// one pass directly, the same shape <see cref="Economy.SettlementSubsistence.Run(World.Settlement)"/>
        /// exposes for the same reason.
        /// <para/>
        /// <b>A no-op the instant a map exists</b> — the real <see cref="SettlementConstructionInitiative"/>
        /// owns that settlement from there on, and this returns 0 without reading or writing anything.
        /// </summary>
        public static int Run(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (settlement.InteriorMap != null) return 0;

            List<(ThingDef Def, int Target)> needs = ComputeNeeds(settlement);
            if (needs.Count == 0) return 0;

            int capacity = BuildCapacity(settlement.Citizens.Count);
            if (capacity <= 0) return 0;

            int credited = 0;
            for (int i = 0; i < needs.Count && credited < capacity; i++)
            {
                ThingDef def = needs[i].Def;
                int shortfall = needs[i].Target - settlement.StructureCount(def);
                if (shortfall <= 0) continue;

                int give = Math.Min(shortfall, capacity - credited);
                settlement.AddStructure(def, give);
                credited += give;
            }
            return credited;
        }

        // -----------------------------------------------------------------------------------------------
        // How much: a rate derived from citizens, capped by the same throttle the map path already uses.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Complete structures this pass may credit, in total across every need. At least one wherever there
        /// is at least one citizen (a lone founder still eventually finishes a shelter, just slowly), scaling
        /// with headcount past that, and never more than <see cref="ConstructionInitiativeTuning.MaxBlueprintsPerTick"/>
        /// — the same per-tick ceiling <see cref="SettlementConstructionInitiative"/> already throttles blueprint
        /// placement to, reused rather than restated so the abstract path can never outbuild what the real one
        /// would ever allow in one gated interval.
        /// </summary>
        public static int BuildCapacity(int citizens)
        {
            if (citizens <= 0) return 0;
            int capacity = Math.Max(1, citizens / AbstractConstructionTuning.CitizensPerBuilder);
            return Math.Min(capacity, ConstructionInitiativeTuning.MaxBlueprintsPerTick);
        }

        // -----------------------------------------------------------------------------------------------
        // What is needed: SettlementConstructionInitiative.ComputeNeeds' own targets, restated against the
        // same tuning constants because that private method needs a Map.Map this settlement does not have.
        // -----------------------------------------------------------------------------------------------

        private static List<(ThingDef Def, int Target)> ComputeNeeds(Settlement settlement)
        {
            var needs = new List<(ThingDef, int)>(3);

            int citizens = settlement.Citizens.Count;
            if (citizens > 0)
            {
                needs.Add((ConstructionThingDefOf.Bed, citizens));

                int wallTarget = Math.Clamp(
                    (int)Math.Ceiling(citizens * ConstructionInitiativeTuning.WallsPerCitizen),
                    ConstructionInitiativeTuning.MinWallShelterCount,
                    ConstructionInitiativeTuning.MaxWallShelterCount);
                needs.Add((ConstructionThingDefOf.Wall, wallTarget));
            }

            int storageTarget = StorageTarget(settlement);
            if (storageTarget > 0) needs.Add((ConstructionThingDefOf.StorageHut, storageTarget));

            return needs;
        }

        /// <summary>One <c>StorageHut</c> per <see cref="ConstructionInitiativeTuning.GoodsPerStorageHut"/>
        /// units held in <see cref="World.Settlement.Stores"/>, at least one once anything is held at all —
        /// <see cref="SettlementConstructionInitiative"/>'s own arithmetic, restated verbatim against the same
        /// constant.</summary>
        private static int StorageTarget(Settlement settlement)
        {
            int totalStored = 0;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores) totalStored += kv.Value;
            if (totalStored <= 0) return 0;
            return Math.Max(1, (totalStored + ConstructionInitiativeTuning.GoodsPerStorageHut - 1) / ConstructionInitiativeTuning.GoodsPerStorageHut);
        }
    }
}
