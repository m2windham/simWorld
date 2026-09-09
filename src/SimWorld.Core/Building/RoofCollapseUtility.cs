using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Roof support and collapse (RimWorld: <c>Verse.RoofCollapseUtility</c> / <c>Verse.RoofCollapseCellsFinder</c>).
    ///
    /// <para/><b>What counts as support:</b> any spawned edifice with <c>Fillage.Full</c> — a wall, a door, a
    /// Frame mid-construction of one (its fillage is copied from what it will become, per <see cref="Frame"/>'s
    /// own remarks), or unmined natural rock. This reuses <see cref="RoomTracker.IsFullEdifice"/>'s own notion
    /// of "wall-like" rather than adding a separate <c>ThingDef.holdsRoof</c> flag RimWorld carries — nothing
    /// in this port's content needs the two concepts to differ yet.
    ///
    /// <para/><b>What the check is:</b> a roofed, edifice-free cell is supported if some Fillage.Full edifice
    /// sits within <see cref="RoofSupportMaxRadius"/> cells of it, straight-line — a direct radius query via
    /// <see cref="GenRadial"/>, not a flood fill through open cells and not a graph search.
    ///
    /// <para/><b>Why not build this on the region graph or on Room/RoomGroup</b> (this module's brief asked
    /// for that judgement call explicitly): both already answer a different question than "how far is the
    /// nearest wall". The region graph (<see cref="RegionGrid"/>/<see cref="RegionAndRoomUpdater"/>) partitions
    /// *reachability* — its cells stop at a portal (a doorway) the way a support radius should not, and it
    /// only ever covers *walkable* cells, while a roofed-but-impassable cell (solid, not-yet-mined rock, which
    /// <c>GenStep_Roofs</c> roofs too) still needs to be excluded from collapse — correctly, but not for a
    /// reachability reason. Room/RoomGroup (<see cref="RoomTracker"/>) partitions *thermal enclosure* — a
    /// Room can be one contiguous thermal unit while still having a corner too far from any wall to hold its
    /// own roof up, so "which Room a cell is in" answers nothing about whether that cell is supported. A
    /// direct spatial radius query is the actual shape of the question, and <see cref="GenRadial"/> already
    /// exists to answer exactly that shape cheaply — so this reuses neither graph, matching the reasoning
    /// <see cref="RoomTracker"/>'s own remarks already gave for not reusing the *other* graph in its place.
    ///
    /// <para/><b>When this runs:</b> event-driven, not a per-tick scan — <see cref="Map.EdificeGrid.DeRegister"/>
    /// calls <see cref="Notify_RoofHolderDespawned"/> whenever a Fillage.Full edifice leaves the map (destroyed,
    /// mined out, deconstructed), which is the only way support is ever lost. Nothing re-checks a cell that
    /// was already supported and had nothing change nearby, so a 300×300 map with no destruction pays nothing
    /// for this every tick — see this module's report for why that mattered here.
    /// </summary>
    public static class RoofCollapseUtility
    {
        /// <summary>
        /// How far a roofed, edifice-free cell may sit from the nearest support and still count as held up.
        /// RimWorld's real support-search radius and algorithm are not sourced or verified against decompiled
        /// source in this sandbox (see this module's report) — this port's own straight-radius check is a
        /// simplified stand-in for RimWorld's (unknown, here) exact shape. Pinned by trend tests — a roofed
        /// area far enough from every remaining wall collapses, one still close to a wall does not — never by
        /// this literal.
        /// </summary>
        public const float RoofSupportMaxRadius = 5f;

        /// <summary>Blunt damage dealt to each Thing under an ordinary collapsing roof cell. Unsourced —
        /// RimWorld's real collapse damage roll is not known in this sandbox; pinned by a test that only
        /// asserts damage occurs and that a thick roof hits harder, never by the literal.</summary>
        public const float CollapseDamageAmount = 50f;

        /// <summary>A thick natural rock roof collapses more violently — <see cref="RoofDef.isThickRoof"/>'s
        /// own doc comment already said so before this pass built anything that reads it.</summary>
        public const float ThickRoofCollapseDamageMultiplier = 2f;

        /// <summary>
        /// An edifice that may have been holding up roof just left <paramref name="vacatedFootprint"/>.
        /// Re-checks every roofed, edifice-free cell within <see cref="RoofSupportMaxRadius"/> of it and
        /// collapses whichever no longer has support, together as one event (RimWorld: roof collapse always
        /// falls in one contiguous batch, not cell by cell).
        /// </summary>
        public static void Notify_RoofHolderDespawned(CellRect vacatedFootprint, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var candidates = new HashSet<IntVec3>();
            foreach (IntVec3 d in vacatedFootprint.Cells)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(d, RoofSupportMaxRadius, useCenter: true))
                {
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (!map.roofGrid.Roofed(c)) continue;
                    if (IsSelfSupporting(c, map)) continue;
                    candidates.Add(c);
                }
            }
            if (candidates.Count == 0) return;

            List<IntVec3>? toCollapse = null;
            foreach (IntVec3 c in candidates)
            {
                if (IsSupported(c, map)) continue;
                (toCollapse ??= new List<IntVec3>()).Add(c);
            }
            if (toCollapse != null) Collapse(toCollapse, map);
        }

        /// <summary>True if a roofed cell has some Fillage.Full edifice within <see cref="RoofSupportMaxRadius"/>,
        /// including itself. An unroofed cell trivially counts as supported — there is nothing there to fall.</summary>
        public static bool IsSupported(IntVec3 cell, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!map.roofGrid.Roofed(cell)) return true;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, RoofSupportMaxRadius, useCenter: true))
            {
                if (!GenGrid.InBounds(c, map)) continue;
                if (IsSelfSupporting(c, map)) return true;
            }
            return false;
        }

        private static bool IsSelfSupporting(IntVec3 c, Map.Map map)
        {
            Thing? edifice = map.edificeGrid[c];
            return edifice != null && edifice.def.Fillage == FillCategory.Full;
        }

        /// <summary>Removes the roof from every cell in <paramref name="cells"/> and damages whatever is
        /// under each one (RimWorld: <c>RoofCollapseUtility.DoCollapse</c>).</summary>
        private static void Collapse(List<IntVec3> cells, Map.Map map)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                RoofDef? roof = map.roofGrid.RoofAt(c);
                map.roofGrid.SetRoof(c, null);

                float damage = CollapseDamageAmount * ((roof?.isThickRoof ?? false) ? ThickRoofCollapseDamageMultiplier : 1f);
                // Copy first: TakeDamage/DamageWorker.Apply can destroy a Thing, which deregisters it from
                // the very list ThingsListAt returns — mutating what this loop is iterating.
                var here = new List<Thing>(map.thingGrid.ThingsListAt(c));
                for (int t = 0; t < here.Count; t++)
                {
                    DealCollapseDamage(here[t], damage);
                }
            }

            // Room's cached anyCellUnroofed/temperature tracking is only invalidated by an edifice spawning
            // or despawning (see RoomTracker's own remarks) — a roof-only change like this one needs the
            // same nudge, or a room that just lost its roof would keep equalising as if it still had one.
            map.roomTracker.Notify_Dirty();
        }

        private static void DealCollapseDamage(Thing thing, float amount)
        {
            var dinfo = new DamageInfo(DamageDefOf.Blunt, amount);
            if (thing is Pawn pawn)
            {
                // Pawns take damage through the Health module's own DamageWorker pipeline, not Thing.TakeDamage
                // (which only ever runs the generic hit-points path) — matching how Combat.Verb_MeleeAttack
                // already applies damage to a pawn target.
                DamageDefOf.Blunt.Worker.Apply(dinfo, pawn);
            }
            else
            {
                thing.TakeDamage(dinfo);
            }
        }
    }
}
