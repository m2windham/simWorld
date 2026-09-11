using System;
using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The pre-filtered set <see cref="AttackTargetFinder.BestAttackTarget"/> scans instead of the whole
    /// population (RimWorld: <c>Verse.AI.AttackTargetsCache</c>).
    ///
    /// <para/><b>The problem it solves.</b> A scan asks "who on this map is hostile to me?". Hostility comes
    /// from two places and only two (<see cref="AttackTargetsUtility.HostileTo"/>): a faction relation, and a
    /// personal grudge. Walking every pawn to find out costs O(population) per call, and the constant think
    /// tree calls it once per Full-tier pawn per <see cref="ConstantThinkTreeTuning.IntervalTicks"/> — so
    /// O(population squared) per interval across a settlement. This keeps the answer as an index instead:
    /// spawned pawns filed by faction, plus the (usually empty) list of pawns holding a grudge. A searcher
    /// then visits only the buckets whose faction is hostile to it, which in peacetime is none at all.
    ///
    /// <para/><b>The invariant, and why it is the weak one on purpose.</b> This index is a <i>superset</i> of
    /// the true candidate set, never an answer in itself: <see cref="AttackTargetFinder"/> still applies every
    /// predicate it always did — range, <see cref="AttackTargetsUtility.ThreatDisabled"/>,
    /// <see cref="AttackTargetsUtility.HostileTo"/>, the caller's validator, reachability — to each candidate
    /// this hands it. That is deliberate, and it is what makes the cache safe to get slightly wrong: a stale
    /// entry can only cost a few nanoseconds, because the thing that decides whether a pawn is shot is the
    /// live predicate and not the index. The <i>only</i> way this class can change behaviour is by
    /// <b>omitting</b> a pawn that should have been considered, so every invalidation path below exists to
    /// protect the superset property and nothing else.
    ///
    /// <para/><b>Every way a pawn enters or leaves the set.</b>
    /// <list type="bullet">
    /// <item><b>Spawn / despawn</b> — <see cref="RegisterTarget"/> / <see cref="DeregisterTarget"/>, called
    /// from <see cref="Map.MapPawns.RegisterPawn"/> / <see cref="Map.MapPawns.DeRegisterPawn"/>, which
    /// <c>Thing.SpawnSetup</c> and <c>Thing.DeSpawn</c> already call for every pawn. Nothing new had to be
    /// hooked.</item>
    /// <item><b>Death</b> — <c>Pawn.Notify_Died</c> runs <c>CorpseMaker.MakeAndSpawnCorpseFor</c>, which
    /// de-spawns the pawn and spawns a <c>Corpse</c> in its place. A dead pawn therefore leaves through the
    /// despawn path on the tick it dies, and the corpse is not a <see cref="Pawn"/> and never enters at
    /// all.</item>
    /// <item><b>Tier demotion</b> — <c>Settlement.DespawnNonFullCitizens</c> de-spawns a citizen that falls
    /// out of <c>PawnTier.Full</c>; same despawn path.</item>
    /// <item><b>Faction change</b> — <see cref="Notify_FactionChanged"/>, from <see cref="Pawn.faction"/>'s
    /// setter (which is why that is a property and not the plain field it used to be). Recruiting a prisoner,
    /// releasing one and taming an animal all re-file through it.</item>
    /// <item><b>A grudge</b> — <see cref="Notify_GrudgeChanged"/>, from
    /// <c>Pawn_MindState.angryAt</c>'s setter. A grudge is symmetric
    /// (<see cref="AttackTargetsUtility.HostileTo"/> reads it from both sides), so the pawn holding one has
    /// to be visible to its victim as well as the other way round, and no faction bucket would ever show a
    /// wild animal to the hunter it turned on.</item>
    /// <item><b>Downing</b> — deliberately <i>not</i> an invalidation path. A downed pawn stays in the index
    /// and is rejected by <see cref="AttackTargetsUtility.ThreatDisabled"/> at scan time, which is also what
    /// lets it come back into play if it is healed without anything having to notice.</item>
    /// <item><b>Load</b> — nothing here is ever Scribed. <see cref="Map.MapPawns"/> is not saved either; a
    /// load re-spawns every Thing through <c>Map.FinalizeLoading</c>, and this index is rebuilt by the
    /// registration path that re-spawn already runs. A cache that restored itself from a save could load
    /// stale; this one cannot, because there is nothing to restore.</item>
    /// </list>
    ///
    /// <para/><b>Where it lives, and why that is not RimWorld's answer.</b> RimWorld hangs
    /// <c>attackTargetsCache</c> off <c>Map</c> and registers from <c>Thing.SpawnSetup</c>, because its
    /// <c>IAttackTarget</c> covers turrets and other buildings as well as pawns. Everything this port's
    /// <see cref="AttackTargetFinder"/> can return is a <see cref="Pawn"/>, so the index hangs off
    /// <see cref="Map.MapPawns"/> instead — which is where RimWorld itself keeps the faction-keyed pawn
    /// indexes (<c>MapPawns.SpawnedPawnsInFaction</c>), and which already has the register/deregister pair
    /// this needs. When a turret or a building becomes attackable, the honest move is to widen this to an
    /// <c>IAttackTarget</c> index on the map, not to bolt a second registry onto the side.
    /// </summary>
    public sealed class AttackTargetsCache
    {
        private readonly Dictionary<Faction, List<Pawn>> byFaction = new Dictionary<Faction, List<Pawn>>();

        /// <summary>Every faction with at least one pawn spawned here. A list rather than the dictionary's own
        /// key collection because a scan walks it once per call and a <c>List</c> indexer costs less than a
        /// dictionary enumerator; it is never more than a handful of entries long.</summary>
        private readonly List<Faction> factionsPresent = new List<Faction>();

        /// <summary>
        /// Pawns spawned here whose <c>mindState.angryAt</c> names somebody. Usually empty, and bounded above
        /// by the map's population.
        /// <para/>
        /// <b>Expiry is not tracked here, on purpose.</b> A grudge lapses when
        /// <c>mindState.angryUntilTick</c> passes, silently, with nothing to notify — and a pawn is only ever
        /// removed from this list when its grudge is cleared outright or it leaves the map. Trying to prune on
        /// expiry instead would mean reading <c>angryUntilTick</c> at scan time, and the two call sites that
        /// start a grudge (<c>TameUtility.TryTame</c>, <c>HuntUtility.TryProvokeRevenge</c>) write
        /// <c>angryAt</c> on one line and <c>angryUntilTick</c> on the next — so a scan landing between them
        /// would see a grudge that had "already expired" and drop the pawn from the index for good. A lapsed
        /// entry costs one rejected candidate; a dropped one loses a fight.
        /// </summary>
        private readonly List<Pawn> grudgeHolders = new List<Pawn>();

        /// <summary>Factions with a pawn spawned here (see <see cref="factionsPresent"/>).</summary>
        public IReadOnlyList<Faction> FactionsPresent => factionsPresent;

        /// <summary>Pawns spawned here that hold a grudge against somebody (see <see cref="grudgeHolders"/>).</summary>
        public IReadOnlyList<Pawn> GrudgeHolders => grudgeHolders;

        /// <summary>Spawned pawns of <paramref name="faction"/> on this map (RimWorld:
        /// <c>MapPawns.SpawnedPawnsInFaction</c>).</summary>
        public IReadOnlyList<Pawn> PawnsInFaction(Faction faction)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            return byFaction.TryGetValue(faction, out List<Pawn>? list) ? list : Array.Empty<Pawn>();
        }

        /// <summary>Everything in the index, counted — for tests and for asserting the index empties out when
        /// the map does.</summary>
        public int FiledPawnCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < factionsPresent.Count; i++) total += byFaction[factionsPresent[i]].Count;
                return total;
            }
        }

        /// <summary>A pawn has arrived on this map. Files it under its faction (a pawn with no faction is
        /// nobody's enemy by relation and is deliberately not filed at all — it can only ever be reached
        /// through <see cref="grudgeHolders"/>).</summary>
        public void RegisterTarget(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            Faction? faction = pawn.faction;
            if (faction != null) BucketFor(faction).Add(pawn);
            if (pawn.mindState?.angryAt != null && !grudgeHolders.Contains(pawn)) grudgeHolders.Add(pawn);
        }

        /// <summary>A pawn has left this map — died, was demoted out of Full tier, walked off, or was
        /// destroyed.</summary>
        public void DeregisterTarget(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            grudgeHolders.Remove(pawn);

            Faction? faction = pawn.faction;
            if (faction != null && byFaction.TryGetValue(faction, out List<Pawn>? list) && list.Remove(pawn))
            {
                if (list.Count == 0) DropBucket(faction);
                return;
            }

            // The pawn was not where its current faction says it should be. That should be unreachable —
            // Notify_FactionChanged re-files every change made while spawned — so this is a self-heal, not a
            // path with a caller: leaving a despawned pawn filed would not produce a wrong target (the scan
            // rejects !Spawned) but it would leak, and a leak in a registry is how the next lane inherits a
            // bug it did not write. Costs a full walk only on the miss.
            RemoveFromEveryBucket(pawn);
        }

        /// <summary>
        /// <paramref name="pawn"/>'s faction has just changed while it is spawned here (RimWorld:
        /// <c>AttackTargetsCache.Notify_FactionChanged</c>). Re-files it from
        /// <paramref name="oldFaction"/>'s bucket into its new one.
        /// </summary>
        public void Notify_FactionChanged(Pawn pawn, Faction? oldFaction)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (oldFaction != null && byFaction.TryGetValue(oldFaction, out List<Pawn>? old))
            {
                old.Remove(pawn);
                if (old.Count == 0) DropBucket(oldFaction);
            }
            Faction? now = pawn.faction;
            if (now == null) return;
            List<Pawn> bucket = BucketFor(now);
            if (!bucket.Contains(pawn)) bucket.Add(pawn);
        }

        /// <summary><paramref name="pawn"/> has just started or dropped a grudge while spawned here. Nothing
        /// reads <c>angryUntilTick</c>: see <see cref="grudgeHolders"/> for why lapsing is not an event.</summary>
        public void Notify_GrudgeChanged(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            bool holds = pawn.mindState?.angryAt != null;
            if (holds)
            {
                if (!grudgeHolders.Contains(pawn)) grudgeHolders.Add(pawn);
            }
            else
            {
                grudgeHolders.Remove(pawn);
            }
        }

        private List<Pawn> BucketFor(Faction faction)
        {
            if (!byFaction.TryGetValue(faction, out List<Pawn>? list))
            {
                list = new List<Pawn>();
                byFaction[faction] = list;
                factionsPresent.Add(faction);
            }
            return list;
        }

        private void DropBucket(Faction faction)
        {
            byFaction.Remove(faction);
            factionsPresent.Remove(faction);
        }

        private void RemoveFromEveryBucket(Pawn pawn)
        {
            for (int i = factionsPresent.Count - 1; i >= 0; i--)
            {
                Faction faction = factionsPresent[i];
                List<Pawn> list = byFaction[faction];
                if (!list.Remove(pawn)) continue;
                if (list.Count == 0) DropBucket(faction);
            }
        }
    }
}
