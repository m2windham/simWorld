using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>Placement validity for a would-be Blueprint (RimWorld: <c>Verse.GenConstruct</c>, trimmed to
    /// the checks this port's content actually needs). Not ported: RimWorld's interaction-spot rules
    /// (<c>InteractionSpotBlocked</c>/<c>WouldBlockInteractionSpot</c>), because this port has no interaction
    /// cells to protect. From memory of RimWorld's source rather than a copy of it: the second rule refuses
    /// only an impassable building, or one of the same def, over a neighbour's interaction cell, and a bed is
    /// neither.</summary>
    public static class GenConstruct
    {
        /// <summary>
        /// True when <paramref name="entityToBuild"/> could be placed at <paramref name="cell"/>: in bounds,
        /// the terrain offers the affordance it needs, and no Blueprint/Frame/impassable edifice already
        /// occupies the cell. <paramref name="failReason"/> names the first check that failed.
        /// </summary>
        /// <summary>
        /// Whether <paramref name="entityToBuild"/> could be planned at <paramref name="cell"/> facing
        /// <paramref name="rot"/>, checking <b>every cell of its footprint</b> rather than only the one it is
        /// centred on.
        ///
        /// <para/><paramref name="rot"/> defaults to North so the eight existing callers are unchanged; every
        /// one of them places an unrotated building, and a caller that starts rotating has to say so.
        ///
        /// <para/>Until this validated the whole rect, a footprint was a field some systems honoured and
        /// others ignored — <c>ThingGrid</c> and <c>EdificeGrid</c> registered all of it while this accepted a
        /// placement based on one cell — which is why shipped content set no size at all until <c>Bed</c>
        /// became 1x2, and <c>Map.ThingSizeTests</c> carried a tripwire saying so.
        /// </summary>
        public static bool CanPlaceBlueprintAt(ThingDef entityToBuild, IntVec3 cell, Map.Map map, out string? failReason, Rot4 rot = default)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, rot, entityToBuild.size).Cells)
            {
                if (!CanPlaceOneCell(entityToBuild, c, map, out failReason)) return false;
            }

            failReason = null;
            return true;
        }

        private static bool CanPlaceOneCell(ThingDef entityToBuild, IntVec3 cell, Map.Map map, out string? failReason)
        {
            if (!GenGrid.InBounds(cell, map))
            {
                failReason = "out of bounds";
                return false;
            }

            TerrainAffordanceDef? needed = entityToBuild.terrainAffordanceNeeded;
            if (needed != null)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain.affordances == null || !terrain.affordances.Contains(needed))
                {
                    failReason = "terrain lacks the " + needed.defName + " affordance";
                    return false;
                }
            }

            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                Thing t = here[i];
                if (t.def.category == ThingCategory.Blueprint || t.def.category == ThingCategory.Frame)
                {
                    failReason = "something is already planned here";
                    return false;
                }
            }

            Thing? edifice = map.edificeGrid[cell];
            if (edifice != null)
            {
                failReason = "a building already occupies this cell";
                return false;
            }

            failReason = null;
            return true;
        }

        /// <summary>
        /// A pawn other than <paramref name="pawnToIgnore"/> standing where <paramref name="blueprint"/>'s
        /// frame would go up, when what is being built is impassable; null when nobody is in the way or the
        /// building can be walked through (RimWorld: the pawn half of
        /// <c>GenConstruct.FirstBlockingThing(constructible, pawnToIgnore)</c>/<c>BlocksConstruction</c>,
        /// which <c>Blueprint.TryReplaceWithSolidThing</c> checks, ignoring the delivering pawn, before it
        /// spawns the frame).
        /// <para/>
        /// <b>Why it is needed.</b> A frame of an impassable building is itself impassable, so turning a
        /// blueprint into one around a pawn walls that pawn in. Measured once construction could run at all
        /// (seed 777 of the storyteller bench, after this lane gave the map trees): a storage hut went up
        /// around a citizen standing on its blueprint; from that cell nothing was reachable, so every tick his
        /// job search came back empty and started again, scanning every rock on the map, and the whole
        /// simulation slowed from under a millisecond a tick to 46.
        /// <para/>
        /// The delivering pawn is ignored, as in RimWorld, and stepped aside once its frame is up
        /// (<see cref="StepOffUnwalkableCell"/>): a Touch path to the site is satisfied by standing on it, so
        /// a hauler who picked its load up from the site's own cell delivers from there.
        /// </summary>
        public static Pawn? FirstBlockingPawn(Blueprint blueprint, Pawn? pawnToIgnore)
        {
            Map.Map? map = blueprint.Map;
            if (map == null || blueprint.EntityToBuild.passability != Traversability.Impassable) return null;

            foreach (IntVec3 c in GenAdj.OccupiedRect(blueprint.Position, blueprint.Rotation, blueprint.EntityToBuild.size).Cells)
            {
                IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                for (int i = 0; i < here.Count; i++)
                {
                    if (here[i] is Pawn pawn && !ReferenceEquals(pawn, pawnToIgnore)) return pawn;
                }
            }
            return null;
        }

        /// <summary>
        /// Moves <paramref name="pawn"/> to the nearest cell it can stand on when the cell it is on can no
        /// longer be walked; does nothing otherwise, and returns whether it moved. Standing in for RimWorld's
        /// <c>Pawn_PathFollower.TryRecoverFromUnwalkablePosition</c>, which this port does not have, at the one
        /// place construction creates the situation: a hauler that delivered from the site's own cell and
        /// has just raised an impassable frame around itself. Measured before this existed: in the settlement
        /// <c>MapCommandsStandingRulesTests</c> opens, a hauler that had picked its logs up from a storage
        /// hut's blueprint delivered from that cell at tick 4739 and stayed inside the frame, jobless, for the
        /// rest of the run.
        /// </summary>
        internal static bool StepOffUnwalkableCell(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || map.pathGrid.Walkable(pawn.Position)) return false;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 1; i < pattern.Count; i++)
            {
                IntVec3 c = pawn.Position + pattern[i];
                if (!GenGrid.InBounds(c, map) || !GenGrid.Standable(c, map)) continue;
                pawn.Position = c;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The hand-authored <c>Blueprint_&lt;X&gt;</c>/<c>Frame_&lt;X&gt;</c> ThingDef for a buildable Def —
        /// see <see cref="Defs.ThingDef.entityToBuild"/>'s remarks for why these are content rather than
        /// generated. A linear scan over the (small, single-digit) set of Blueprint/Frame Defs this pass
        /// ships; not worth caching against a DefDatabase that a test's <c>ContentTestBase</c> swaps out
        /// from under a static cache every test.
        /// </summary>
        public static ThingDef? BlueprintDefFor(ThingDef entityToBuild) => FindBy(ThingCategory.Blueprint, entityToBuild);

        public static ThingDef? FrameDefFor(ThingDef entityToBuild) => FindBy(ThingCategory.Frame, entityToBuild);

        private static ThingDef? FindBy(ThingCategory category, ThingDef entityToBuild)
        {
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef d = all[i];
                if (d.category == category && ReferenceEquals(d.entityToBuild, entityToBuild)) return d;
            }
            return null;
        }
    }
}
