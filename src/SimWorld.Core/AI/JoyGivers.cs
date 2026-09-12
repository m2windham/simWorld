using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Cell picking shared by the recreation givers (RimWorld: <c>RCellFinder.TryFindSkygazeCell</c>,
    /// <c>CellFinder</c> and <c>WalkPathFinder</c>, which this port has no equivalents of). Bounded random
    /// probing in the same shape <see cref="JobGiver_WanderAnywhere"/> already uses, so an idle settlement
    /// never pays an O(radius²) scan every time somebody wants a break.
    /// </summary>
    internal static class JoyCellFinder
    {
        /// <summary>Tries up to <paramref name="tries"/> random cells in <paramref name="radius"/> of
        /// <paramref name="root"/> that the pawn can stand on and reach.</summary>
        public static bool TryFindCellNear(Pawn pawn, IntVec3 root, float radius, bool unroofedOnly, int tries, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            int count = GenRadial.NumCellsInRadius(radius);
            if (count <= 1) return false;

            for (int i = 0; i < tries; i++)
            {
                IntVec3 candidate = root + GenRadial.RadialPattern[Rand.Range(1, count)];
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Standable(candidate, map)) continue;
                if (unroofedOnly && GenGrid.Roofed(candidate, map)) continue;
                if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;
                result = candidate;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Stare at the sky (RimWorld: <c>RimWorld.JoyGiver_Skygaze</c>). Needs nothing whatsoever except open
    /// sky overhead and eyes to look with, which is why it is the recreation a band that has built nothing
    /// can still take. RimWorld additionally refuses it in rain and under a few game conditions
    /// (<c>JoyUtility.EnjoyableOutsideNow</c>); this port checks the weather's own rain rate, which is the
    /// half of that test its content supports.
    /// </summary>
    public sealed class JoyGiver_Skygaze : JoyGiver
    {
        public override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || !pawn.Spawned) return null;
            if (!JoyWeather.EnjoyableOutsideNow(map)) return null;
            if (!JoyCellFinder.TryFindCellNear(pawn, pawn.Position, SkygazeSearchRadius, def.unroofedOnly, SearchTries, out IntVec3 cell))
            {
                return null;
            }
            return new Job(def.jobDef, cell);
        }

        /// <summary>Not RimWorld-sourced (<c>RCellFinder.TryFindSkygazeCell</c> walks the region system this
        /// port has no equivalent of); wide enough to find open sky just outside a small shelter.</summary>
        private const float SkygazeSearchRadius = 12f;

        private const int SearchTries = 12;
    }

    /// <summary>
    /// Go for a walk (RimWorld: <c>RimWorld.JoyGiver_GoForWalk</c>). The other giver a band with no
    /// furniture can always take. RimWorld picks a multi-leg walk route through
    /// <c>WalkPathFinder.TryFindWalkPath</c>; this port has no such finder, so the walk is one leg to a
    /// distant reachable cell and the joy accrues along the way exactly as it does in RimWorld.
    /// </summary>
    public sealed class JoyGiver_GoForWalk : JoyGiver
    {
        public override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || !pawn.Spawned) return null;
            if (!JoyWeather.EnjoyableOutsideNow(map)) return null;
            if (!JoyCellFinder.TryFindCellNear(pawn, pawn.Position, WalkRadius, unroofedOnly: false, SearchTries, out IntVec3 cell))
            {
                return null;
            }
            return new Job(def.jobDef, cell);
        }

        /// <summary>Not RimWorld-sourced (RimWorld's walk route is built from regions, not a radius); far
        /// enough that the walk takes a real slice of the session rather than two steps.</summary>
        private const float WalkRadius = 20f;

        private const int SearchTries = 12;
    }

    /// <summary>
    /// Sit with somebody and talk (RimWorld: <c>RimWorld.JoyGiver_SocialRelax</c>), the Social kind and by
    /// far the most load-bearing recreation in a settlement — it is what stops a whole population building
    /// tolerance to one single kind and going bored.
    ///
    /// <para/><b>The one translation in this module, and it is deliberate.</b> RimWorld anchors social
    /// relaxation to a <c>CompGatherSpot</c> — a campfire, a table, a party spot — and returns no job at all
    /// when the map has none. This port ships no gather-spot building of any kind (no campfire, no table, no
    /// chair: see <c>Data/Core/Defs/ThingDefs_Buildings/</c>), so a 1:1 port of this giver would be dead
    /// content on every map, and the brief this module was written to is explicit that a tribal band with no
    /// furniture has to be able to do <i>something</i> or the recreation system only serves rich
    /// settlements. So the relationship is inverted the way CLAUDE.md suggests inverting a contested one:
    /// <b>the gathering is around people, not furniture.</b> The anchor is another citizen of the same
    /// settlement who is spawned, awake, not in a mental state and not already down; everything else — the
    /// job, the joy kind, the session length, the rate, the tolerance — is RimWorld's unchanged. The moment
    /// a gather-spot building exists in content this giver should prefer it, and RimWorld's hook for that is
    /// <c>JoyGiverDef.thingDefs</c> — see that field's note in <see cref="JoyGiverDef"/> for why it is not
    /// declared yet.
    /// </summary>
    public sealed class JoyGiver_SocialRelax : JoyGiver
    {
        /// <summary>How far away a companion may be and still be worth walking to. Not RimWorld-sourced
        /// (RimWorld's gather spot is found by a map-wide lister, not a radius).</summary>
        private const float CompanionSearchRadius = 25f;

        private const int SitSpotTries = 8;

        public override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || !pawn.Spawned) return null;

            Pawn? companion = FindCompanion(pawn, map);
            if (companion == null) return null;
            if (!JoyCellFinder.TryFindCellNear(pawn, companion.Position, SitSpotRadius, unroofedOnly: false, SitSpotTries, out IntVec3 spot))
            {
                return null;
            }
            return new Job(def.jobDef, companion, spot);
        }

        /// <summary>Sitting distance, in cells — RimWorld's own gather radius for this giver is 3.9.</summary>
        private const float SitSpotRadius = 3.9f;

        /// <summary>
        /// The nearest fellow citizen worth sitting with. Deterministic (nearest, then lowest id) rather than
        /// a random draw: this runs inside the think tree, and every giver that draws makes the whole game's
        /// roll sequence depend on how many pawns happened to look for a break this tick.
        /// </summary>
        private static Pawn? FindCompanion(Pawn pawn, Map.Map map)
        {
            Pawn? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Pawn> candidates = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn other = candidates[i];
                if (ReferenceEquals(other, pawn)) continue;
                if (!other.RaceProps.Humanlike || other.Dead || other.Downed) continue;
                if (!other.Awake() || other.InMentalState) continue;
                if (!ReferenceEquals(other.faction, pawn.faction)) continue;

                int distSq = (other.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq > CompanionSearchRadius * CompanionSearchRadius) continue;
                if (distSq > bestDistSq) continue;
                if (distSq == bestDistSq && best != null && other.thingIDNumber >= best.thingIDNumber) continue;
                if (!Reachability.CanReach(pawn, other, PathEndMode.Touch)) continue;
                best = other;
                bestDistSq = distSq;
            }
            return best;
        }
    }

    /// <summary>Whether it is pleasant enough outside to be out in it (RimWorld:
    /// <c>JoyUtility.EnjoyableOutsideNow</c>, whose rain-rate threshold of 0.25 this keeps; its temperature
    /// and game-condition clauses read data this port does not carry).</summary>
    internal static class JoyWeather
    {
        public const float MaxRainRate = 0.25f;

        public static bool EnjoyableOutsideNow(Map.Map map) =>
            map.weatherManager == null || map.weatherManager.RainRate < MaxRainRate;
    }
}
