using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

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
        /// Population of one tier, such that the three tiers always sum to <see cref="TotalPopulation"/>.
        ///
        /// <para/>Every tier scans <see cref="Citizens"/>, which by design holds only live <c>Pawn</c> objects
        /// and stays small relative to a large settlement's total (the attended/significant slice, per §11.3) —
        /// never the whole population, so this scan never costs what counting 40,000 people would. Statistical
        /// adds the bare <see cref="StatisticalPopulation"/> cohort on top, and that cohort is still never
        /// enumerated: it is a count, added as a count.
        ///
        /// <para/><b>Statistical has two kinds of member and both must be counted.</b> A citizen can reach
        /// that tier as a bare cohort seat (<see cref="AddStatisticalPeople"/>) or as a live <c>Pawn</c> whose
        /// tracker settled there — <see cref="God.AttentionManager"/> does exactly that to every citizen of an
        /// unattended settlement, so the second kind is the normal end state rather than a curiosity. Answering
        /// from the bare count alone silently lost them and broke the sum above.
        /// </summary>
        public int PopulationOf(PawnTier tier)
        {
            int live = citizens.Count(p => p.tier.Tier == tier);
            return tier == PawnTier.Statistical ? live + statisticalPopulation : live;
        }

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

        /// <summary>
        /// Takes <paramref name="count"/> people out of the Statistical cohort, floored at zero, and returns how
        /// many actually left. The mirror of <see cref="AddStatisticalPeople"/>, and the thing a settlement needs
        /// in order to be somewhere people leave: emigration, famine, plague and a settlement simply failing are
        /// all population going down, and a cohort that can only grow can express none of them.
        /// <para/>
        /// Separate from <see cref="AddStatisticalPeople"/> rather than a signed count on it, because the two
        /// directions are not symmetrical at the tier boundary: adding people to a cohort is free, while removing
        /// them has a floor and a caller usually wants to know how many it actually got.
        /// </summary>
        public int RemoveStatisticalPeople(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            int removed = Math.Min(count, statisticalPopulation);
            statisticalPopulation -= removed;
            return removed;
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
            if (Find.TickManager.TicksGame % SettlementTuning.CitizenMapSyncIntervalTicks == 0) SyncCitizenSpawns();
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

        // ---- interior map (spec §11.2's seam) ----

        /// <summary>Never triggers generation — null until <see cref="EnterMap"/> has actually been called once. See that method's own doc for why a settlement that has never been opened stays this way.</summary>
        private Map.Map? interiorMap;

        public Map.Map? InteriorMap => interiorMap;

        /// <summary>
        /// This settlement's interior, generated the first time the player opens it and cached from then on —
        /// a second call returns the exact same <see cref="Map.Map"/> instance rather than paying to
        /// regenerate it. Sized by <see cref="TotalPopulation"/> via <see cref="MapGenTuning.MapSizeForPopulation"/>
        /// rather than the generator's own flat default (spec: "size the map by the settlement, not by a
        /// constant" — <c>TotalPopulation</c> is read once, at first entry, so a settlement that grows after
        /// being entered keeps the interior it was first given rather than resizing under the player).
        /// <para/>
        /// <b>Left null until entered on purpose.</b> A generated <see cref="Map.Map"/> is the single most
        /// expensive thing this save file can hold — a full terrain/roof grid plus every scattered Thing —
        /// and in a civilization of many settlements, most are never opened at all. Generating (and later
        /// saving) one for every settlement regardless of whether the player ever looks inside would be
        /// real, avoidable cost for nothing; see the module's report for the concrete numbers.
        /// </summary>
        public Map.Map EnterMap(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (interiorMap == null)
            {
                IntVec2 size = MapGenTuning.MapSizeForPopulation(TotalPopulation);
                interiorMap = MapGenerator.GenerateMapFor(this, world, size);
            }

            // Unconditional and immediate, unlike Tick's own rare-bucket gate on the same sync (spec §11.2's
            // seam is what this method exists for): a settlement's founders (or anyone else already
            // Full-tier) should be standing on its interior the instant it is opened, not up to
            // SettlementTuning.CitizenMapSyncIntervalTicks ticks later. Idempotent on a settlement that was
            // already entered — every already-spawned citizen is skipped, not re-spawned.
            SyncCitizenSpawns();
            return interiorMap;
        }

        // ---- citizen <-> map presence (spec §11.2/§11.3's seam) ----

        /// <summary>
        /// Reconciles <see cref="Citizens"/> against <see cref="InteriorMap"/>: a citizen who died is dropped
        /// from the roster (and taken off the map first, if they were still on it) so <see cref="TotalPopulation"/>
        /// never keeps counting somebody no longer alive; a Full-tier citizen not yet standing on a generated
        /// interior is placed there; a spawned citizen who is no longer Full-tier (demoted — see
        /// <see cref="Pawns.Pawn_TierTracker"/>) is taken back off it. Called unconditionally by
        /// <see cref="EnterMap"/> and on a rare gated cadence by <see cref="Tick"/> — see
        /// <see cref="SettlementTuning.CitizenMapSyncIntervalTicks"/> for why a periodic sweep is needed at
        /// all rather than only reacting to <see cref="AddCitizen"/>: <c>Pawns.FamilyManager.DemographyTick</c>
        /// (run from <see cref="GrowthTick"/> against this exact <see cref="Citizens"/> list) adds newborns
        /// and kills the aged directly, never through <see cref="AddCitizen"/>, so nothing short of a sweep
        /// over the roster itself would ever notice either.
        /// <para/>
        /// <b>Only Full-tier citizens are ever spawned — Interval never is.</b> Both tiers keep a real
        /// <c>Pawn</c> object (<see cref="Citizens"/>'s own doc), but Interval's is deliberately inert: spec
        /// §11.3 is explicit that Interval "advances only on the Long tick bucket... No jobs, no mind state,
        /// no skills". A pawn with no job driver and no mind state placed on a map would simply stand on one
        /// cell forever, contributing nothing while still costing map bookkeeping (grid registration, path
        /// cost, a reservation slot nobody frees) — occupancy with no behaviour behind it. Full is also
        /// exactly the tier spec §11.3 ties to attention ("dropping into ticked time <i>is</i> promoting the
        /// attended settlement to Full" — the two are one mechanism, not two): physical presence on the one
        /// settlement being watched at full depth is the honest embodiment of that tier, and nothing else
        /// warrants it.
        /// <para/>
        /// <b>What "release" means here.</b> Nothing in this codebase ever discards an already-generated
        /// <see cref="InteriorMap"/> — <see cref="EnterMap"/>'s own doc commits to caching it forever once
        /// built — so there is no "closing" event this method reacts to. The despawn half above is the real,
        /// reachable release this design actually has: a citizen leaves the map the moment they stop
        /// qualifying (death, or falling out of Full), not because some larger "exit settlement scope" verb
        /// fired. A future attention system wiring <c>Pawn_TierTracker.Notify_AttentionChanged</c> would make
        /// that demotion — and therefore this despawn — happen for the very reason spec §11.2 describes; nothing
        /// further would need to change here.
        /// </summary>
        public void SyncCitizenSpawns()
        {
            PruneDeadCitizens();
            if (interiorMap == null) return;
            DespawnNonFullCitizens(interiorMap);
            SpawnUnspawnedFullCitizens(interiorMap);
        }

        /// <summary>Drops every dead citizen from the roster — taking them off the map first if they were
        /// still spawned there — so population counts (<see cref="TotalPopulation"/>, <see cref="PopulationOf"/>)
        /// never keep counting someone <see cref="Pawns.Pawn.Dead"/>. This codebase has no Corpse Thing yet
        /// (Health's own territory, not this module's), so a dead citizen simply leaves both the map and the
        /// roster rather than leaving a body behind — the honest option available at this seam, not a
        /// pretence that a corpse system already exists.</summary>
        private void PruneDeadCitizens()
        {
            for (int i = citizens.Count - 1; i >= 0; i--)
            {
                Pawn pawn = citizens[i];
                if (!pawn.Dead) continue;
                if (pawn.Spawned) pawn.DeSpawn();
                citizens.RemoveAt(i);
            }
        }

        private void DespawnNonFullCitizens(Map.Map map)
        {
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn pawn = citizens[i];
                if (pawn.Spawned && pawn.Map == map && pawn.tier.Tier != PawnTier.Full) pawn.DeSpawn();
            }
        }

        private void SpawnUnspawnedFullCitizens(Map.Map map)
        {
            // Seeded with cells every already-spawned citizen occupies so a newcomer this same pass never
            // targets a cell another newcomer was just given — GenGrid.Standable alone would not catch that,
            // since a cell holding one Standable pawn is still Standable for the next one (RimWorld's own
            // pawns can share a cell too; this only avoids piling every new arrival onto the exact same spot).
            HashSet<IntVec3>? taken = null;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn pawn = citizens[i];
                if (pawn.tier.Tier != PawnTier.Full || pawn.Spawned) continue;
                taken ??= OccupiedCitizenCells(map);
                if (!TryFindCitizenPlacementCell(map, taken, out IntVec3 cell)) continue; // tried again next sync
                GenSpawn.Spawn(pawn, cell, map);
                taken.Add(cell);
            }
        }

        private HashSet<IntVec3> OccupiedCitizenCells(Map.Map map)
        {
            var cells = new HashSet<IntVec3>();
            for (int i = 0; i < citizens.Count; i++)
            {
                if (citizens[i].Spawned && citizens[i].Map == map) cells.Add(citizens[i].Position);
            }
            return cells;
        }

        /// <summary>
        /// Nearest standable cell to whatever the settlement has already built — the centroid of its
        /// <see cref="ThingRequestGroup.BuildingArtificial"/> things — or the map's own centre before anything
        /// has been built at all; never a map corner or an edge (spec brief: "somewhere sane and walkable,
        /// ideally near whatever the settlement already has built"). Searches <see cref="GenRadial"/>'s
        /// nearest-first pattern out to its own <see cref="GenRadial.MaxRadius"/> and only then falls back to
        /// a full-map scan, so the common case (a buildable map) never pays for one.
        /// </summary>
        private static bool TryFindCitizenPlacementCell(Map.Map map, HashSet<IntVec3> taken, out IntVec3 cell)
        {
            IntVec3 anchor = SettlementAnchor(map);
            foreach (IntVec3 offset in GenRadial.RadialPattern)
            {
                IntVec3 candidate = anchor + offset;
                if (TryUseCell(map, taken, candidate, out cell)) return true;
            }

            foreach (IntVec3 candidate in map.AllCells)
            {
                if (TryUseCell(map, taken, candidate, out cell)) return true;
            }

            cell = default;
            return false;
        }

        private static bool TryUseCell(Map.Map map, HashSet<IntVec3> taken, IntVec3 candidate, out IntVec3 cell)
        {
            if (!taken.Contains(candidate) && GenGrid.InBounds(candidate, map) && GenGrid.Standable(candidate, map))
            {
                cell = candidate;
                return true;
            }
            cell = default;
            return false;
        }

        private static IntVec3 SettlementAnchor(Map.Map map)
        {
            IReadOnlyList<Thing> built = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            if (built.Count == 0) return new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

            int sumX = 0, sumZ = 0;
            for (int i = 0; i < built.Count; i++)
            {
                sumX += built[i].Position.x;
                sumZ += built[i].Position.z;
            }
            return new IntVec3(sumX / built.Count, 0, sumZ / built.Count);
        }

        // ---- Scribe ----

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref foundingTick, "foundingTick", 0);
            Scribe_Values.Look(ref statisticalPopulation, "statisticalPopulation", 0);

            // Citizens are split for save purposes by spawn state rather than written as one list. An
            // unspawned citizen has nothing else that owns it, so it is deep-saved here exactly as before. A
            // citizen currently spawned on this settlement's own interiorMap is owned by that map's own Thing
            // list instead (Map.ExposeData deep-saves everything in listerThings, pawns included) — deep-
            // saving it a second time here would write two independent copies of the same citizen and
            // reconstruct two separate Pawn objects sharing one ThingID on load, which is exactly the
            // duplication a spawned citizen must never suffer. A spawned citizen is therefore written here
            // only as a reference to the one real copy interiorMap already owns, and is merged back into
            // Citizens once that reference resolves (ResolvingCrossRefs) — by which point LoadingVars has
            // already run for the *entire* save (interiorMap's own deep list included, wherever in the
            // document it sits), so the one real Pawn instance both sides point at already exists.
            // "Owned by something else" is the real test, and being spawned is only the common case of it: a
            // citizen who has just died is neither spawned nor gone from the roster yet (PruneDeadCitizens
            // runs on the rare sync, not at the moment of death), but their body is already inside a
            // Things.Corpse that the map's own Thing list deep-saves — so deep-saving them here as well
            // would reconstruct the same citizen twice on load, the exact duplication the comment above
            // exists to prevent. They are simply left out: the next sync would drop them from the roster
            // anyway, and the one real copy of them lives on in the corpse.
            List<Pawn>? unspawned = Scribe.mode == LoadSaveMode.Saving
                ? citizens.Where(p => !p.Spawned && p.corpse == null).ToList()
                : citizens;
            Scribe_Collections.Look(ref unspawned, "citizens", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars) citizens = unspawned ?? new List<Pawn>();

            List<Pawn>? spawnedCitizens = Scribe.mode == LoadSaveMode.Saving ? citizens.Where(p => p.Spawned).ToList() : null;
            Scribe_Collections.Look(ref spawnedCitizens, "spawnedCitizens", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs && spawnedCitizens != null)
            {
                citizens.AddRange(spawnedCitizens.Where(p => p != null));
            }

            Dictionary<ThingDef, int>? storeDict = new Dictionary<ThingDef, int>(stores);
            Scribe_Collections.Look(ref storeDict, "stores", LookMode.Def, LookMode.Value);
            stores.Clear();
            if (storeDict != null)
            {
                foreach (KeyValuePair<ThingDef, int> kv in storeDict) stores[kv.Key] = kv.Value;
            }

            // Null when this settlement has never been entered (EnterMap's own doc) — Scribe_Deep.Look
            // writes/reads a null element in that case rather than paying to save or load a map at all.
            Map.Map? m = interiorMap;
            Scribe_Deep.Look(ref m, "interiorMap");
            interiorMap = m;
        }
    }
}
