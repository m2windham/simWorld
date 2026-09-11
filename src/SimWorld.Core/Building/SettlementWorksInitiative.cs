using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// The public works a settlement raises for itself: the bench its scholars need, and the art its people
    /// look at (systems: research — a bench to research at; mining — something a mined mineral is made out
    /// of; needs.beauty — something positive for the sampler to find).
    ///
    /// <para/><b>The two holes this closes, and why they are one lane.</b>
    /// <see cref="AI.WorkGiver_Research"/> requires a <see cref="ResearchWorkDefOf.ResearchBench"/> and
    /// refuses to produce a job without one — RimWorld's own rule, kept deliberately. The bench had a
    /// Blueprint/Frame pair and a costList and nothing in <c>src/</c> ever placed one, so <i>every</i>
    /// civilization in this port was locked out of research, and with it the era ladder, the tech tree and
    /// the divergence work that stands on top of them. <c>Sculpture</c> was in exactly the same state one
    /// system over: a full Blueprint/Frame pair, a Beauty stat the sampler reads, and no line of code that
    /// so much as named it. Both are "a building nobody decides to build", both are answered by the same
    /// decision loop, and neither needs a mechanism — only somebody to want one.
    ///
    /// <para/><b>Why a third initiative rather than a need on
    /// <see cref="SettlementConstructionInitiative"/>.</b> That class computes bed, wall and store from
    /// <see cref="Settlement.Citizens"/> and <see cref="Settlement.Stores"/>, and its targets are all
    /// per-capita. Neither of these is: two benches is a civilization-wide figure the tech tree was balanced
    /// against (<see cref="WorksInitiativeTuning.ResearchBenchesWanted"/>), and a sculpture count is a
    /// property of a place rather than of a headcount (<see cref="SculptureMaterials.SculpturesWantedOf"/>).
    /// Adding them there would also mean editing a file other lanes are working in, for no gain — CLAUDE.md's
    /// own rule, and the reason the shipped initiatives are already three files rather than one.
    ///
    /// <para/><b>What generalised and what did not.</b> The placement half generalises completely, and
    /// <see cref="CountBuiltOrPlanned"/>/<see cref="TryPlaceBlueprint"/> below are that half, written once and
    /// used for both works here: count what is built or already planned, find a cell
    /// <see cref="GenConstruct.CanPlaceBlueprintAt"/> accepts, spawn the Blueprint the content authored. The
    /// <i>bill</i> half does not generalise, and this lane deliberately did not force it: what makes
    /// <c>Crafting.StonecutterInitiative</c> work is not its bench-placing but
    /// <c>StoneWallMaterials.BlocksWantedOf</c> — a demand figure derived from a want the settlement already
    /// had. Every bench asks a different question there ("how many walls' worth of stone", "how many days'
    /// nutrition", "how many bodies to clothe"), and for some shipped recipes the honest answer is still zero
    /// because nothing consumes the product. That lane already wrote the rule this one obeys: a bill whose
    /// target is zero must not be queued. See this lane's report for the three benches left unwired and what
    /// each of them needs first.
    ///
    /// <para/><b>The 12-cell trap, and why it does not bite here.</b> <c>Crafting.WorkGiver_DoBill</c> only
    /// offers a bill whose ingredients already lie within its search radius of the bench, which is why the
    /// stonecutter must be planted among its chunks. Neither work here goes through that giver:
    /// <see cref="AI.WorkGiver_Research"/> wants only reachability, and a sculpture is built by the
    /// construction pipeline, which hauls its costList to the frame from anywhere on the map
    /// (<see cref="WorkGiver_ConstructDeliverResources"/>). Art still clusters, but for a different reason
    /// entirely — see <see cref="ArtClusterRadius"/>.
    ///
    /// <para/><b>No state of its own.</b> Everything it decides is re-derived every gated tick from what is
    /// standing on the map, so there is nothing here to Scribe and a loaded save resumes with no catch-up
    /// step; <c>SettlementWorksTests</c> pins that by running it across a save/load round trip and asserting
    /// it neither duplicates what exists nor forgets what it wanted.
    /// </summary>
    public static class SettlementWorksInitiative
    {
        /// <summary>
        /// How far from the last piece a new one is planted, in cells — <b>derived, not chosen</b>. Art only
        /// reads if it is dense: <see cref="BeautyUtility.AverageBeautyPerceptible"/> averages over the cells
        /// a citizen can see, so sculptures scattered across a map move that average by almost nothing however
        /// many there are. <see cref="SculptureMaterials.SculpturesWantedOf"/> counts what it takes to lift
        /// one such sample, and this is the radius that sample is taken over
        /// (<see cref="BeautyUtility.SampleRadius"/>, truncated to whole cells because a
        /// <see cref="CellRect"/> is squared off): plant them inside it and the count means what it says.
        /// </summary>
        public static int ArtClusterRadius => (int)BeautyUtility.SampleRadius;

        // ---------------------------------------------------------------------------------------------
        // Entry points. Same shape as SettlementConstructionInitiative/StonecutterInitiative, deliberately:
        // a civilization-wide Tick a host loop calls every tick and that costs a modulo on all but the rare
        // one it fires, plus per-settlement and ungated overloads a test or a settlement scope can drive.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Civilization-wide entry point, called once per tick from <c>Sim.Game.WireTickHooks</c>.
        /// Picks the civilization's next research project before visiting any settlement — a settlement with a
        /// bench and nothing to study is still a settlement that cannot research, and until this lane exactly
        /// one line in <c>src/</c> ever chose a project (see <see cref="ResearchAgenda"/>). A silent no-op
        /// with no world running, like its two siblings.</summary>
        public static void Tick()
        {
            if (!DueNow) return;

            ResearchAgenda.EnsureProject();

            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements)
            {
                Map.Map? map = settlement.InteriorMap;
                if (map != null) Run(settlement, map);
            }
        }

        /// <summary>One settlement, reading its own <see cref="Settlement.InteriorMap"/>. A settlement nobody
        /// has ever entered has no map and therefore nowhere to put anything, which is a real, expected state
        /// rather than an error — never a triggered generation, exactly as its two siblings read it.</summary>
        public static void TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            TickSettlement(settlement, map);
        }

        /// <summary>The gated per-settlement pass against an explicit map, so a test (or a future caller
        /// entering a settlement scope) can drive it without paying for a generated world just to attach a
        /// map to a <see cref="Settlement"/>.</summary>
        public static void TickSettlement(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!DueNow) return;
            Run(settlement, map);
        }

        /// <summary>
        /// The ungated logic. Public so a test can drive one pass without arranging for the tick number to
        /// land on the interval. A settlement with nobody living in it wants neither: there is no one to
        /// research and no one to look at the art.
        /// </summary>
        public static void Run(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (settlement.Citizens.Count == 0) return;

            EnsureResearchBenches(map);
            EnsureArt(map);
        }

        private static bool DueNow => Find.TickManager.TicksGame % WorksInitiativeTuning.IntervalTicks == 0;

        // ---------------------------------------------------------------------------------------------
        // The two works.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Raises a research bench when the settlement is short of
        /// <see cref="WorksInitiativeTuning.ResearchBenchesWanted"/> — one blueprint per gated call, so a
        /// settlement grows into its works rather than stamping all of them out in a single pass (the same
        /// shape <c>SettlementConstructionInitiative</c>'s per-tick cap gives its own needs). Placed anywhere
        /// the map will take it: <see cref="AI.WorkGiver_Research"/> asks only that a scholar can reach it.
        /// </summary>
        private static void EnsureResearchBenches(Map.Map map)
        {
            ThingDef bench = ResearchWorkDefOf.ResearchBench;
            if (!bench.IsResearchFinished) return; // Ungated in shipped content; honoured anyway, not assumed.
            if (CountBuiltOrPlanned(map, bench) >= WorksInitiativeTuning.ResearchBenchesWanted) return;

            TryPlaceBlueprint(map, bench, anchor: null, radius: 0);
        }

        /// <summary>
        /// Carves the next sculpture, in the best material the settlement can currently afford
        /// (<see cref="SculptureMaterials.PreferredSculptureDef"/>), until it has as many standing together as
        /// one perceptible sample takes (<see cref="SculptureMaterials.SculpturesWantedOf"/>). Counted across
        /// every material at once — a settlement that carved its gold has not stopped having art — and
        /// anchored on what it has already made, so the pieces gather in one place where they can be seen
        /// rather than scattering to no effect.
        /// </summary>
        private static void EnsureArt(Map.Map map)
        {
            ThingDef wanted = SculptureMaterials.PreferredSculptureDef(map);
            int target = SculptureMaterials.SculpturesWantedOf(wanted);
            if (target <= 0) return;
            if (CountBuiltOrPlanned(map, wanted) >= target) return;

            TryPlaceBlueprint(map, wanted, ArtAnchor(map), ArtClusterRadius);
        }

        /// <summary>
        /// Where the next sculpture goes beside: whatever art the settlement already has, and failing that a
        /// bed — the one building this port places that says "people are here", and the room whose beauty a
        /// citizen spends most of their time inside. Null when the settlement has neither, which simply means
        /// the first piece is planted wherever the map allows and every later one gathers around it.
        /// </summary>
        private static IntVec3? ArtAnchor(Map.Map map)
        {
            IReadOnlyList<ThingDef> art = SculptureMaterials.AllSculptureDefs;
            for (int i = 0; i < art.Count; i++)
            {
                IReadOnlyList<Thing> standing = map.listerThings.ThingsOfDef(art[i]);
                for (int s = 0; s < standing.Count; s++)
                {
                    if (standing[s].Spawned) return standing[s].Position;
                }
            }

            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int b = 0; b < beds.Count; b++)
            {
                if (beds[b].Spawned) return beds[b].Position;
            }
            return null;
        }

        // ---------------------------------------------------------------------------------------------
        // The reusable half: counting what is already under way, and getting a blueprint onto the map.
        //
        // Public because both works above need it and so would a fourth. SettlementConstructionInitiative and
        // Crafting.StonecutterInitiative each carry their own private copy of the counting half, written
        // before there was a third caller; folding those two onto this one is a worthwhile follow-up and is
        // deliberately not done here, because it would edit two files other lanes are working in to change
        // nothing a player could see (CLAUDE.md).
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Instances of <paramref name="entityDef"/> already standing, plus every Blueprint and Frame already
        /// under way for one, so a settlement never plants a second of something it has already started.
        /// Counted across <see cref="SculptureMaterials.EquivalentsOf"/>, so a want two Defs can both fill
        /// sees both — without it a settlement whose citizens had carved five gold sculptures would go on
        /// wanting five wooden ones, the same trap <see cref="StoneWallMaterials.EquivalentsOf"/> exists to
        /// close for walls.
        /// </summary>
        public static int CountBuiltOrPlanned(Map.Map map, ThingDef entityDef)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (entityDef == null) throw new ArgumentNullException(nameof(entityDef));

            IReadOnlyList<ThingDef> fills = SculptureMaterials.EquivalentsOf(entityDef);

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

        /// <summary>
        /// Spawns the Blueprint content authored for <paramref name="entityDef"/> on the first cell that will
        /// take it, and reports whether it managed to. With an <paramref name="anchor"/> the search is a
        /// <see cref="CellRect"/> scanned in cell order around it — deterministic, and there is no reason to
        /// spend the shared <see cref="Rand"/> stream on a bounded rect that has a perfectly good answer,
        /// which is the same call <c>StonecutterInitiative</c> makes for its own bench. Without one it samples
        /// the map at random (<c>ConstructionInitiativeTuning.MaxPlacementAttempts</c> tries, that class's own
        /// reasoning: a handful of grid lookups beats scanning every cell of a large map, and a map with
        /// genuinely no room carries its shortfall to the next gated tick). Validity is entirely
        /// <see cref="GenConstruct.CanPlaceBlueprintAt"/>'s call.
        /// </summary>
        public static bool TryPlaceBlueprint(Map.Map map, ThingDef entityDef, IntVec3? anchor, int radius)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (entityDef == null) throw new ArgumentNullException(nameof(entityDef));

            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(entityDef);
            if (blueprintDef == null) return false; // No Blueprint content authored — nothing to place.

            if (!TryFindPlacementCell(map, entityDef, anchor, radius, out IntVec3 cell)) return false;

            GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), cell, map);
            return true;
        }

        private static bool TryFindPlacementCell(Map.Map map, ThingDef entityDef, IntVec3? anchor, int radius, out IntVec3 cell)
        {
            if (anchor.HasValue && radius > 0)
            {
                CellRect scan = CellRect.CenteredOn(anchor.Value, radius).ClipInsideMap(map);
                foreach (IntVec3 candidate in scan.Cells)
                {
                    if (GenConstruct.CanPlaceBlueprintAt(entityDef, candidate, map, out _))
                    {
                        cell = candidate;
                        return true;
                    }
                }
                // Nothing free beside the anchor: fall through to the whole map rather than stalling forever
                // on a full cluster. A piece out of sight is worth less than one in it, and no piece at all
                // is worth nothing.
            }

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

        private static bool Fills(IReadOnlyList<ThingDef> fills, ThingDef? entityDef)
        {
            for (int i = 0; i < fills.Count; i++)
            {
                if (ReferenceEquals(fills[i], entityDef)) return true;
            }
            return false;
        }
    }
}
