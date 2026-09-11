using System;
using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Who counts as an enemy (RimWorld: <c>Verse.GenHostility</c>). Deliberately built on the faction
    /// relations that already exist (<see cref="Faction.HostileTo"/>, derived from goodwill by
    /// <see cref="Faction.KindFromGoodwill"/>) rather than a second notion of hostility owned by the AI
    /// layer — a raid is hostile because <see cref="FactionManager.RandomEnemyFaction"/> picked a faction
    /// whose relation says so, and that is the same answer this gives.
    /// </summary>
    public static class AttackTargetsUtility
    {
        /// <summary>
        /// Whether <paramref name="a"/> would attack <paramref name="b"/>. Two sources, and nothing else:
        /// <list type="bullet">
        /// <item><b>Faction relation.</b> Both sides have a <see cref="Pawn.faction"/> and those factions are
        /// <see cref="FactionRelationKind.Hostile"/> to one another. A pawn with no faction at all — every
        /// wild animal, and any pawn generated outside a faction — is nobody's enemy by relation, which is
        /// what keeps a hunt a hunt rather than a war.</item>
        /// <item><b>A personal grudge.</b> One of them is currently <see cref="HuntUtility.IsAngry"/> at the
        /// other (<see cref="MindState.Pawn_MindState.angryAt"/> — set by a failed tame or by wounding prey).
        /// RimWorld's manhunter is hostile to <i>everybody</i>; this port's <c>angryAt</c> names one specific
        /// pawn, so this reads it literally and the grudge stays personal. That is the honest read of the
        /// field the animals and hunting lanes actually built, not a narrowing of RimWorld for its own
        /// sake — and it is why a provoked muffalo charges the archer who shot it and walks past everyone
        /// else.</item>
        /// </list>
        /// Symmetric on purpose: a pawn one of whose neighbours has decided to kill it is at war whether it
        /// agreed or not, so the hunter who provoked an animal may fight back without needing a grudge of its
        /// own.
        /// </summary>
        public static bool HostileTo(Pawn a, Pawn b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (ReferenceEquals(a, b)) return false;

            if (IsAngryAt(a, b) || IsAngryAt(b, a)) return true;

            Faction? fa = a.faction;
            Faction? fb = b.faction;
            return fa != null && fb != null && fa.HostileTo(fb);
        }

        /// <summary>Currently holding an unexpired grudge against this specific pawn.</summary>
        public static bool IsAngryAt(Pawn pawn, Pawn other) =>
            pawn.mindState != null && ReferenceEquals(pawn.mindState.angryAt, other) && HuntUtility.IsAngry(pawn);

        /// <summary>
        /// No longer worth attacking (RimWorld: <c>IAttackTarget.ThreatDisabled</c> / <c>Pawn.ThreatDisabled</c>).
        /// <b>Downed counts, and that is the whole point:</b> a downed pawn in this port is alive, still
        /// spawned and still a perfectly valid <see cref="LocalTargetInfo"/> — the capture loop
        /// (<c>Factions.WardenUtility</c>) depends on exactly that — so without this test an attacker would
        /// keep emptying its magazine into somebody already on the ground. A dead pawn likewise stays spawned
        /// (this codebase has no <c>Corpse</c> Thing; <c>JobDriver_Hunt</c> and <c>Settlement.PruneDeadCitizens</c>
        /// both record that), so "stop shooting corpses" needs saying out loud here too.
        /// </summary>
        public static bool ThreatDisabled(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            return pawn.Dead || pawn.Downed || pawn.Destroyed || !pawn.Spawned;
        }
    }

    /// <summary>
    /// Picks the enemy a pawn should go for (RimWorld: <c>Verse.AI.AttackTargetFinder.BestAttackTarget</c>).
    /// <para/>
    /// <b>What RimWorld's version does that this does not.</b> RimWorld scores candidates on a blend of
    /// distance, threat, line of sight, whether they are already shooting at you and how many friends are
    /// already on them, all fed by a per-map <c>AttackTargetsCache</c>. This walks the map's pawn list and
    /// takes the nearest reachable valid enemy — the same simplification
    /// <see cref="JobGiver_GetFood"/> makes against <c>FoodUtility.BestFoodSourceOnMap</c>, and enough to
    /// prove the loop (acquire → close → cast → target goes down → re-acquire). Ties break on
    /// <see cref="Thing.thingIDNumber"/> so two pawns scanning the same instant always agree, which matters
    /// because determinism is a feature: nothing here touches <c>Rand</c> at all.
    /// </summary>
    public static class AttackTargetFinder
    {
        public static Pawn? BestAttackTarget(Pawn searcher, float maxRange, Func<Pawn, bool>? validator = null)
        {
            if (searcher == null) throw new ArgumentNullException(nameof(searcher));
            Map.Map? map = searcher.Map;
            if (map == null || AttackTargetsUtility.ThreatDisabled(searcher)) return null;

            float maxRangeSquared = maxRange * maxRange;
            Pawn? best = null;
            float bestDistSq = float.MaxValue;

            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (!(pawnsOnMap[i] is Pawn candidate)) continue;

                // Cheapest tests first, and the order is load-bearing rather than cosmetic (see this class's
                // remarks on cost): the constant think tree calls this for every Full-tier pawn every
                // ConstantThinkTreeTuning.IntervalTicks, so this loop runs O(population) times per pawn and
                // O(population squared) times per interval across a settlement. Distance is two int
                // subtractions; AttackTargetsUtility.HostileTo is two grudge checks and a faction-relation
                // lookup. Every one of these is a pure predicate with no side effect, so reordering them
                // cannot change which pawn is returned — only what it costs to find out. Measured at the
                // Full-tier budget in docs/perf/constant-think-tree.md.
                float distSq = (candidate.Position - searcher.Position).LengthHorizontalSquared;
                if (distSq > maxRangeSquared) continue;
                if (best != null && !Closer(distSq, candidate, bestDistSq, best)) continue;
                if (AttackTargetsUtility.ThreatDisabled(candidate)) continue;
                if (!AttackTargetsUtility.HostileTo(searcher, candidate)) continue;
                if (validator != null && !validator(candidate)) continue;

                // Reachability last: it rebuilds regions on demand, so it is the one expensive test here and
                // is only paid for a candidate that has already won on distance.
                if (!Reachability.CanReach(searcher, candidate, PathEndMode.Touch)) continue;

                best = candidate;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>Nearest wins; equal distance breaks on thing id so the choice never depends on map
        /// iteration order.</summary>
        private static bool Closer(float distSq, Pawn candidate, float bestDistSq, Pawn best) =>
            distSq < bestDistSq || (distSq == bestDistSq && candidate.thingIDNumber < best.thingIDNumber);
    }
}
