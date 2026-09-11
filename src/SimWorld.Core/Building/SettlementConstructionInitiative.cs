using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Translation: citizen-initiated construction under edicts (<c>docs/status.json</c>'s
    /// <c>building.initiative</c>, <c>docs/spec/simworld-spec.md</c> §10). RimWorld has nothing to port here
    /// — its player places every blueprint by hand. §10's own re-focus is that the player is a god directing
    /// a civilization, not a colony overseer placing every wall, so a settlement has to decide for itself what
    /// it needs and place the blueprint — the existing blueprint → frame → building pipeline
    /// (<see cref="GenConstruct"/>, <see cref="Blueprint"/>, <see cref="Frame"/>,
    /// <c>WorkGiver_ConstructDeliverResourcesTo{Blueprints,Frames}</c>, <c>WorkGiver_ConstructFinishFrame</c>)
    /// already hauls materials to a blueprint and finishes it; nothing before this class decided to place one.
    /// <para/>
    /// <b>What "needs something" means here.</b> Deliberately small, concrete and read straight off real
    /// settlement state rather than a speculative economy (the brief's own words): a bed per citizen who could
    /// physically use one (<see cref="World.Settlement.Citizens"/> — see the remark on that choice below), a
    /// handful of walls once there is anyone to shelter, and storage sized to what the settlement's own
    /// <see cref="World.Settlement.Stores"/> ledger actually holds. See <see cref="ComputeNeeds"/>.
    /// <para/>
    /// <b>Only <see cref="World.Settlement.Citizens"/> count toward a need, never
    /// <see cref="World.Settlement.StatisticalPopulation"/>.</b> A Statistical citizen has no individual
    /// <c>Pawn</c> object by the tiering system's own design (spec §11.3) — there is no one there to
    /// physically occupy a bed or shelter behind a wall, so counting them would build monuments to people this
    /// system cannot honestly say exist on this particular map. <see cref="God.GodRollup"/> draws exactly this
    /// same line for the god view's own aggregates (cohort-sampling a Statistical slice rather than pretending
    /// each member is individually real); this class draws it the other way, by simply not counting them,
    /// because "how many beds" has no sampled/aggregate answer the way "mean mood" does — a bed either exists
    /// on the map or it does not.
    /// <para/>
    /// <b>The edict seam.</b> An active <see cref="EdictDef.prioritizedConstruction"/> reorders which unmet
    /// need gets filled first — the same declarative, read-live shape <see cref="EdictDef.prioritizedWork"/>
    /// already uses for routine work (see <c>AI.JobGiver_Edicts</c>): no activation-time side effect, read
    /// fresh off <see cref="Find.God"/>'s active edicts every gated tick, so deactivating the edict leaves no
    /// trace — the very next tick simply stops finding it, and every need this class was already going to fill
    /// eventually is exactly what it still fills, just back in its unbiased order.
    /// <para/>
    /// <b>Self-gated, not a per-tick scan.</b> <see cref="TickSettlement(World.Settlement, Map.Map)"/> gates
    /// itself on <see cref="ConstructionInitiativeTuning.IntervalTicks"/>, the same "rare bucket" shape
    /// <c>GodManager.GodTick</c>/<c>Storyteller.StorytellerTick</c>/<c>Settlement.GrowthTick</c> already use —
    /// stateless (a tick-modulo check, matching <c>Settlement.GrowthTick</c>'s own idiom) rather than a stored
    /// "last ran" field, since this lane cannot add one to <see cref="World.Settlement"/> itself.
    /// <para/>
    /// <b>A settlement nobody has entered has no interior map.</b> <see cref="World.Settlement.InteriorMap"/>
    /// is null until <see cref="World.Settlement.EnterMap"/> has actually run once (that method's own doc:
    /// generation is never triggered speculatively). <see cref="TickSettlement(World.Settlement)"/> reads that
    /// literally: no map means nowhere to place anything, so it is a deliberate no-op there, not a pretend
    /// build against a map that does not exist.
    /// <para/>
    /// <b>Not wired to a tick loop by this lane.</b> <see cref="Tick"/> is the civilization-wide entry point a
    /// host loop calls once per tick (cheaply short-circuited by the gate above every time but the rare one it
    /// fires) — mirroring <c>_ =&gt; God.GodTick()</c> in <c>Sim/Game.cs</c>'s own <c>WireTickHooks</c>. This
    /// lane does not own <c>Sim/Game.cs</c>; wiring <c>tm.PostTickers.Add(_ =&gt; SettlementConstructionInitiative.Tick());</c>
    /// there is left to whoever does (see this module's report).
    /// </summary>
    public static class SettlementConstructionInitiative
    {
        private readonly struct Need
        {
            public readonly ThingDef EntityDef;
            public readonly int Target;

            public Need(ThingDef entityDef, int target)
            {
                EntityDef = entityDef;
                Target = target;
            }
        }

        /// <summary>Civilization-wide entry point: every settlement in <see cref="Find.World"/>, or a silent
        /// no-op when no world is running at all (a fresh <see cref="Sim.Game"/> mid-construction, a test with
        /// no world) — the same "nothing to do without a live world" boundary <c>Game.TickMaps</c> draws for
        /// its own <see cref="World.WorldObject"/> sweep.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements) TickSettlement(settlement);
        }

        /// <summary>One settlement, reading its own <see cref="World.Settlement.InteriorMap"/> — null (never
        /// entered) is a real, expected state, not an error; see this class's own doc for why that is a no-op
        /// rather than a triggered generation.</summary>
        public static void TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            TickSettlement(settlement, map);
        }

        /// <summary>
        /// The actual logic, against an explicit map — production code always reaches this through
        /// <see cref="TickSettlement(World.Settlement)"/>/<see cref="Tick"/>; this overload exists so a test
        /// (or a future caller entering a settlement scope) can drive it without paying for a fully generated
        /// world just to attach a map to a <see cref="Settlement"/>. Self-gates here, the one place every
        /// entry point above funnels through, so the interval check has a single home.
        /// </summary>
        public static void TickSettlement(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % ConstructionInitiativeTuning.IntervalTicks != 0) return;

            List<Need> needs = ComputeNeeds(settlement, map);
            if (needs.Count == 0) return;

            HashSet<ThingDef>? biased = CollectEdictBias();
            if (biased != null)
            {
                // Stable sort: biased needs move to the front, the unbiased rest keep their original
                // (survival-first) relative order — exactly the "reorder, don't replace" shape
                // JobGiver_Edicts already gives prioritizedWork.
                needs = needs.OrderByDescending(n => IsBiased(biased, n.EntityDef)).ToList();
            }

            int placed = 0;
            for (int i = 0; i < needs.Count && placed < ConstructionInitiativeTuning.MaxBlueprintsPerTick; i++)
            {
                Need need = needs[i];
                int shortfall = need.Target - CountBuiltOrPlanned(map, need.EntityDef);
                for (int u = 0; u < shortfall && placed < ConstructionInitiativeTuning.MaxBlueprintsPerTick; u++)
                {
                    if (!TryFindPlacementCell(map, need.EntityDef, out IntVec3 cell)) break;
                    PlaceBlueprint(map, need.EntityDef, cell);
                    placed++;
                }
            }
        }

        /// <summary>
        /// What this settlement currently lacks, each already trimmed to what is not already built or already
        /// planned (see <see cref="CountBuiltOrPlanned"/>) — callers see only genuine shortfalls. Order is the
        /// unbiased default priority: a bed for a person before a wall around them before a shed for their
        /// goods, survival before shelter before property.
        /// </summary>
        private static List<Need> ComputeNeeds(Settlement settlement, Map.Map map)
        {
            var needs = new List<Need>(3);

            int citizens = settlement.Citizens.Count;
            if (citizens > 0)
            {
                needs.Add(new Need(ConstructionThingDefOf.Bed, citizens));

                int wallTarget = Math.Clamp(
                    (int)Math.Ceiling(citizens * ConstructionInitiativeTuning.WallsPerCitizen),
                    ConstructionInitiativeTuning.MinWallShelterCount,
                    ConstructionInitiativeTuning.MaxWallShelterCount);
                // Which material, not how many: the wall need is computed once, here, and
                // StoneWallMaterials only answers what to cut it from — the toughest stone the settlement has
                // a whole wall's worth of blocks for, or wood when it has none. See that class for why a
                // second decider wanting its own stone walls would be the wrong shape, and why
                // CountBuiltOrPlanned below has to count every wall kind against this one target.
                needs.Add(new Need(StoneWallMaterials.PreferredWallDef(map), wallTarget));
            }

            int storageTarget = StorageTarget(settlement);
            if (storageTarget > 0) needs.Add(new Need(ConstructionThingDefOf.StorageHut, storageTarget));

            return needs;
        }

        /// <summary>One <c>StorageHut</c> per <see cref="ConstructionInitiativeTuning.GoodsPerStorageHut"/>
        /// units actually held in <see cref="World.Settlement.Stores"/>, at least one once anything is held at
        /// all, zero when the ledger is empty — a settlement with nothing to keep has nothing to build storage
        /// for.</summary>
        private static int StorageTarget(Settlement settlement)
        {
            int totalStored = 0;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores) totalStored += kv.Value;
            if (totalStored <= 0) return 0;
            return Math.Max(1, (totalStored + ConstructionInitiativeTuning.GoodsPerStorageHut - 1) / ConstructionInitiativeTuning.GoodsPerStorageHut);
        }

        /// <summary>Every buildable Def named by any active edict's <see cref="EdictDef.prioritizedConstruction"/>,
        /// or null when nothing is active/biased — read live off <see cref="Find.God"/> every call, never
        /// cached, so an edict's bias appears and disappears with its own activation exactly like
        /// <c>JobGiver_Edicts</c>' work-type bias does.</summary>
        private static HashSet<ThingDef>? CollectEdictBias()
        {
            IReadOnlyList<EdictDef> active = Find.God.ActiveEdicts;
            if (active.Count == 0) return null;

            HashSet<ThingDef>? biased = null;
            for (int i = 0; i < active.Count; i++)
            {
                List<ThingDef> prioritized = active[i].prioritizedConstruction;
                for (int j = 0; j < prioritized.Count; j++)
                {
                    biased ??= new HashSet<ThingDef>();
                    biased.Add(prioritized[j]);
                }
            }
            return biased;
        }

        /// <summary>Already-built instances of <paramref name="entityDef"/> plus any Blueprint or Frame
        /// already under way for it — a shortfall counts only what is genuinely still missing, so a settlement
        /// never queues a second blueprint for something it (or its citizens) already started.
        /// <para/>
        /// A wall is a wall whatever it is cut from: <see cref="StoneWallMaterials.EquivalentsOf"/> widens
        /// this to every Def that fills the same need (itself, for everything that is not a wall). Without
        /// that, a settlement whose masons had walled it in granite would still count itself forty wooden
        /// walls short — the need is one need, and only the material moved.</summary>
        private static int CountBuiltOrPlanned(Map.Map map, ThingDef entityDef)
        {
            IReadOnlyList<ThingDef> fills = StoneWallMaterials.EquivalentsOf(entityDef);

            int count = 0;
            for (int i = 0; i < fills.Count; i++) count += map.listerThings.ThingsOfDef(fills[i]).Count;

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && Fills(fills, bp.EntityToBuild)) count++;
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && Fills(fills, f.EntityToBuild)) count++;
            }

            return count;
        }

        /// <summary>Whether an active edict prioritises this need. Matched across
        /// <see cref="StoneWallMaterials.EquivalentsOf"/> for the same reason
        /// <see cref="CountBuiltOrPlanned"/> counts across it: <c>GreatWorksMandate</c> names <c>Wall</c> by
        /// defName, and a settlement that had cut enough stone to wall itself in granite would otherwise stop
        /// being biased by the edict the moment it did — the edict is about walls, not about wood.</summary>
        private static bool IsBiased(HashSet<ThingDef> biased, ThingDef entityDef)
        {
            IReadOnlyList<ThingDef> fills = StoneWallMaterials.EquivalentsOf(entityDef);
            for (int i = 0; i < fills.Count; i++)
            {
                if (biased.Contains(fills[i])) return true;
            }
            return false;
        }

        private static bool Fills(IReadOnlyList<ThingDef> fills, ThingDef? entityDef)
        {
            for (int i = 0; i < fills.Count; i++)
            {
                if (ReferenceEquals(fills[i], entityDef)) return true;
            }
            return false;
        }

        /// <summary>
        /// Picks a valid cell for <paramref name="entityDef"/> by sampling at random rather than scanning the
        /// map — deterministic given the ambient <see cref="Rand"/> stream (CLAUDE.md: "all randomness goes
        /// through a seeded RandomStream"), and cheap regardless of map size. Validity is entirely
        /// <see cref="GenConstruct.CanPlaceBlueprintAt"/>'s own call — this never overlaps or blocks a
        /// Blueprint, Frame or edifice already there, because that is exactly what that check already
        /// refuses.
        /// </summary>
        private static bool TryFindPlacementCell(Map.Map map, ThingDef entityDef, out IntVec3 cell)
        {
            for (int attempt = 0; attempt < ConstructionInitiativeTuning.MaxPlacementAttempts; attempt++)
            {
                var candidate = new IntVec3(Rand.Range(0, map.Size.x), 0, Rand.Range(0, map.Size.z));
                if (GenConstruct.CanPlaceBlueprintAt(entityDef, candidate, map, out _))
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = default;
            return false;
        }

        private static void PlaceBlueprint(Map.Map map, ThingDef entityDef, IntVec3 cell)
        {
            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(entityDef);
            if (blueprintDef == null) return; // No Blueprint content authored for this Def — nothing to place.
            Thing blueprint = ThingMaker.MakeThing(blueprintDef);
            GenSpawn.Spawn(blueprint, cell, map);
        }
    }
}
