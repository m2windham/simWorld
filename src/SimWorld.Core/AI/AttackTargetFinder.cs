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
        /// <item><b>A faction that attacks factionless people.</b> One side is a humanlike with no faction and
        /// the other belongs to a faction whose def sets
        /// <see cref="FactionDef.hostileToFactionlessHumanlikes"/> — see <see cref="HostileToFactionless"/>
        /// for the measurement that made this a defect rather than a nicety.</item>
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
        ///
        /// <para/><b>Custody outranks both</b> (RimWorld: <c>GenHostility</c> never returns a colony's own
        /// prisoner as an enemy of that colony). A captured pawn keeps the <see cref="Pawn.faction"/> it came
        /// from — only a <see cref="Faction.prisoners"/> lookup says who holds it, see <c>Pawn_GuestTracker</c>
        /// — so by faction relation alone a warden and the prisoner it is feeding are still at war, and were:
        /// until <see cref="WorkGiver_Warden_DeliverFood"/> there was no warden job whose patient was ever
        /// <i>not</i> <see cref="Pawn.Downed"/>, and <see cref="ThreatDisabled"/> screens the downed out, so
        /// nothing ever reached this. The first job to walk a warden up to a prisoner still on its feet had
        /// the warden punch it — through the constant think tree's own "anyone at all fights back at arm's
        /// length" floor (<see cref="CombatPostureUtility"/> rule 2), mid-delivery. Held and holder do not
        /// fight, in either direction: a prison break — RimWorld's one case where they do — is a mechanic
        /// this port does not have, and inventing it here to justify the faction relation would be inventing
        /// it in the wrong place.
        /// <para/>
        /// <b>These two sources are also the whole design of <see cref="AttackTargetsCache"/></b>, which
        /// indexes exactly them so a scan need not ask this question of every pawn alive.
        /// </summary>
        public static bool HostileTo(Pawn a, Pawn b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (ReferenceEquals(a, b)) return false;

            if (InCustodyOfEachOther(a, b)) return false;

            if (IsAngryAt(a, b) || IsAngryAt(b, a)) return true;

            Faction? fa = a.faction;
            Faction? fb = b.faction;
            if (fa != null && fb != null) return fa.HostileTo(fb);
            return HostileToFactionless(fa, b) || HostileToFactionless(fb, a);
        }

        /// <summary>
        /// <see cref="FactionDef.hostileToFactionlessHumanlikes"/>'s reader: <paramref name="faction"/> — if it
        /// exists and its def says so — attacks <paramref name="other"/> when <paramref name="other"/> is a
        /// humanlike belonging to no faction at all.
        ///
        /// <para/><b>Why this was a defect and not a nicety.</b> The audit recorded this flag as blocked on
        /// "a factionless-pawn population existing". One existed, and it was not a corner case: it was every
        /// civilian in the game. <c>World.SettlementFounder</c> generated each founding citizen through a
        /// <c>PawnGenerationRequest</c> that named no faction, and <c>Factions.PawnGroupMaker</c> was the only
        /// thing in <c>src/</c> that ever passed one — so a settlement had a faction and not one of its
        /// citizens did. With hostility resting on both sides having a faction, that made a raid on a
        /// watched settlement a pantomime. Measured on a shipped-content settlement founded the ordinary way
        /// and a real <c>RaidEnemy</c> firing onto its interior: <b>30 citizens, 14 raiders standing on the
        /// same map, 0 of 14 raiders saw a citizen as hostile, 0 of 30 citizens saw a raider as hostile, and
        /// not one pawn on either side reached for an attack job.</b> Everybody kept farming. The combat
        /// module was reachable from play and this pairing could never reach it, because every combat test
        /// hands its own pawns a faction by hand.
        ///
        /// <para/>RimWorld puts the same rule in the same place — its <c>GenHostility</c> treats a factionless
        /// humanlike as hostile to any faction whose def sets the flag — though its factionless population is
        /// wild men and quest pawns rather than everybody. Wiring it fixed the pairing the content actually
        /// asks for: <c>RoughOutlanders</c>, the one shipped faction that sets the flag, and also the one
        /// that is <see cref="FactionDef.permanentEnemy"/> and raids most often. It did <i>not</i> fix
        /// <c>TribalCivilization</c> or <c>OutlanderCivilization</c> raids, which set it false and therefore
        /// still found nobody to fight — recorded here as a citizen-faction gap in the World module rather
        /// than papered over by turning the flag on for content that did not ask for it.
        ///
        /// <para/><b>That gap is closed, and this rule is narrower for it.</b> A settlement's citizens carry
        /// their settlement's faction now (<c>World.SettlementFounder</c>, measured in
        /// <c>Tests.World.CitizenFactionTests</c>: 15 of 15 raiders and 30 of 30 citizens hostile, both sides
        /// taking attack jobs), so a raid on a town is a fight by faction relation and this clause carries
        /// none of it. It is still wired and still reachable — the factionless humanlikes left are released
        /// prisoners (<see cref="Pawn_GuestTracker.Release"/> clears the faction outright) and pawns generated
        /// outside any settlement, which is roughly the population RimWorld aims it at in the first place.
        /// </summary>
        private static bool HostileToFactionless(Faction? faction, Pawn other) =>
            faction != null
            && faction.def.hostileToFactionlessHumanlikes
            && other.faction == null
            && other.RaceProps.Humanlike;

        /// <summary>True when either of these two is currently held prisoner by the other's faction — see
        /// <see cref="HostileTo"/>'s own remarks for why that ends the fight rather than starting one.</summary>
        private static bool InCustodyOfEachOther(Pawn a, Pawn b) =>
            IsHeldBy(a, b.faction) || IsHeldBy(b, a.faction);

        private static bool IsHeldBy(Pawn prisoner, Faction? captors)
        {
            if (captors == null) return false;
            Faction? host = CaptureUtility.FindHostFaction(prisoner);
            return host != null && ReferenceEquals(host, captors);
        }

        /// <summary>Currently holding an unexpired grudge against this specific pawn.</summary>
        public static bool IsAngryAt(Pawn pawn, Pawn other) =>
            pawn.mindState != null && ReferenceEquals(pawn.mindState.angryAt, other) && HuntUtility.IsAngry(pawn);

        /// <summary>
        /// No longer worth attacking (RimWorld: <c>IAttackTarget.ThreatDisabled</c> / <c>Pawn.ThreatDisabled</c>).
        /// <b>Downed counts, and that is the whole point:</b> a downed pawn in this port is alive, still
        /// spawned and still a perfectly valid <see cref="LocalTargetInfo"/> — the capture loop
        /// (<c>Factions.WardenUtility</c>) depends on exactly that — so without this test an attacker would
        /// keep emptying its magazine into somebody already on the ground.
        /// <para/>
        /// The <see cref="Thing.Dead"/> and <see cref="Thing.Spawned"/> halves now overlap, and both are kept:
        /// a death leaves the map (<c>Things.CorpseMaker.MakeAndSpawnCorpseFor</c> de-spawns the pawn and puts
        /// a <c>Corpse</c> on the cell), so a dead pawn is normally also an unspawned one. Normally, not
        /// always — a pawn that dies with no map never gets a body, and <c>Pawn.Dead</c> is the only thing
        /// that says so. This is also the predicate that makes <see cref="AttackTargetsCache"/> safe to keep
        /// simple: the index is free to still hold somebody who has just been downed, because the answer to
        /// "may I shoot them" is taken from here and never from the index.
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
    /// already on them. This takes the nearest reachable valid enemy — the same simplification
    /// <see cref="JobGiver_GetFood"/> makes against <c>FoodUtility.BestFoodSourceOnMap</c>, and enough to
    /// prove the loop (acquire → close → cast → target goes down → re-acquire). Ties break on
    /// <see cref="Thing.thingIDNumber"/> so two pawns scanning the same instant always agree, which matters
    /// because determinism is a feature: nothing here touches <c>Rand</c> at all.
    /// <para/>
    /// <b>Where the candidates come from</b> is <see cref="AttackTargetsCache"/>, which is the part that used
    /// to be missing: this walked every pawn on the map, so one scan cost O(population) and the constant
    /// think tree turned that into O(population squared) per interval across a settlement. The scoring below
    /// is unchanged — and that is the point, because the two entry points here run the <i>same</i> loop over
    /// two different candidate sets. <see cref="BestAttackTargetUncached"/> is the whole population, and it
    /// exists so a test can assert the two agree; if they ever disagree the fault can only be in the index,
    /// never in the scoring, because there is only one copy of the scoring.
    /// </summary>
    public static class AttackTargetFinder
    {
        /// <summary>
        /// Bench-only escape hatch for A/B-measuring what the index is worth, exactly as
        /// <see cref="PathFinder.DisableRegionCorridor"/> is for path sharing and
        /// <see cref="ConstantThinkTreeTuning.IntervalTicksOverride"/> is for the constant tree. Setting it
        /// routes every <see cref="BestAttackTarget"/> call through <see cref="BestAttackTargetUncached"/>,
        /// which is the behaviour that shipped before this index existed — so a single bench process can
        /// measure both, minutes apart, on the same box and the same scenario. Nothing in the simulation core
        /// ever assigns it, and both settings return the same answers (that is what
        /// <c>AttackTargetsCacheTests</c> asserts); only the cost differs.
        /// See <c>tools/bench/SimWorld.Bench/Suites/AttackTargetScanSuite.cs</c>.
        /// </summary>
        public static bool BypassCache;

        /// <summary>
        /// The nearest enemy worth going for, found through <see cref="AttackTargetsCache"/>. This is what the
        /// simulation calls.
        /// </summary>
        public static Pawn? BestAttackTarget(Pawn searcher, float maxRange, Func<Pawn, bool>? validator = null)
        {
            if (BypassCache) return BestAttackTargetUncached(searcher, maxRange, validator);
            if (searcher == null) throw new ArgumentNullException(nameof(searcher));
            Map.Map? map = searcher.Map;
            if (map == null || AttackTargetsUtility.ThreatDisabled(searcher)) return null;

            var scan = new Scan(searcher, maxRange, validator);
            AttackTargetsCache cache = map.mapPawns.AttackTargets;

            // 1. Every faction hostile to ours, and nobody else's. In peacetime there is one faction on the
            //    map and it is not hostile to itself, so this loop ends having looked at no pawn at all —
            //    which is the entire reason this class has a cache. 1b and 1c below are the same question
            //    asked of the third hostility rule, which no faction bucket can answer.
            Faction? ours = searcher.faction;
            if (ours != null)
            {
                IReadOnlyList<Faction> present = cache.FactionsPresent;
                for (int f = 0; f < present.Count; f++)
                {
                    Faction other = present[f];
                    if (!ours.HostileTo(other)) continue;
                    IReadOnlyList<Pawn> bucket = cache.PawnsInFaction(other);
                    for (int i = 0; i < bucket.Count; i++) scan.Consider(bucket[i]);
                }

                // 1b. Everyone here who belongs to nobody, when our own faction attacks such people
                //     (FactionDef.hostileToFactionlessHumanlikes). No faction bucket can offer them — they
                //     are in none — so without this the index would be a strict subset of the true candidate
                //     set for a raider of such a faction, which is the one way this class is allowed to be
                //     wrong and is not.
                if (ours.def.hostileToFactionlessHumanlikes)
                {
                    IReadOnlyList<Pawn> loose = cache.FactionlessHumanlikes;
                    for (int i = 0; i < loose.Count; i++) scan.Consider(loose[i]);
                }
            }
            else if (searcher.RaceProps.Humanlike)
            {
                // 1c. The mirror, and the half that actually matters in a generated game: *we* are the
                //     factionless humanlike (every settlement citizen is — see AttackTargetsUtility.HostileTo)
                //     and step 1 above cannot run at all for us, because we have no faction whose hostiles to
                //     look up. A citizen therefore has to ask the other way round: which factions standing
                //     here attack people like me?
                IReadOnlyList<Faction> present = cache.FactionsPresent;
                for (int f = 0; f < present.Count; f++)
                {
                    Faction other = present[f];
                    if (!other.def.hostileToFactionlessHumanlikes) continue;
                    IReadOnlyList<Pawn> bucket = cache.PawnsInFaction(other);
                    for (int i = 0; i < bucket.Count; i++) scan.Consider(bucket[i]);
                }
            }

            // 2. Anybody holding a grudge — against us or against somebody else; the predicate chain sorts
            //    that out. Skipping the ones a hostile bucket has already offered keeps a grudge-holding
            //    raider from being scored twice (harmless, since Consider takes a minimum under a total
            //    order, but pointless).
            IReadOnlyList<Pawn> grudgeHolders = cache.GrudgeHolders;
            for (int i = 0; i < grudgeHolders.Count; i++)
            {
                Pawn holder = grudgeHolders[i];
                if (ours != null && holder.faction != null && ours.HostileTo(holder.faction)) continue;
                scan.Consider(holder);
            }

            // 3. Our own grudge, which points wherever it likes — at a friend, at a wild animal, at somebody
            //    of no faction at all. Nothing above would have offered it.
            //
            //    The map test is not belt-and-braces: a grudge is a plain reference and survives the pawn it
            //    names walking off this map onto another one, where it is still Spawned and therefore still
            //    passes ThreatDisabled. Both other candidate sources are per-map registries and cannot offer
            //    such a pawn at all; this one has to say so itself, or the cached scan would find a target the
            //    walk-the-whole-map scan could not (JobGiver_Manhunter makes the same test for the same
            //    reason).
            Pawn? ownGrudge = searcher.mindState?.angryAt;
            if (ownGrudge != null && ReferenceEquals(ownGrudge.Map, map) &&
                !(ours != null && ownGrudge.faction != null && ours.HostileTo(ownGrudge.faction)) &&
                !Holds(grudgeHolders, ownGrudge))
            {
                scan.Consider(ownGrudge);
            }

            return scan.Best;
        }

        /// <summary>
        /// The same answer, taken the slow way: every pawn on the map, no index consulted. <b>Not for the
        /// simulation</b> — this is the reference implementation <c>AttackTargetsCacheTests</c> pins
        /// <see cref="BestAttackTarget"/> against over randomly generated map states, which is the strongest
        /// correctness statement available about a cache. It shares <see cref="Scan"/> with the cached path
        /// on purpose, so the only thing the comparison can catch is a candidate the index failed to offer.
        /// </summary>
        public static Pawn? BestAttackTargetUncached(Pawn searcher, float maxRange, Func<Pawn, bool>? validator = null)
        {
            if (searcher == null) throw new ArgumentNullException(nameof(searcher));
            Map.Map? map = searcher.Map;
            if (map == null || AttackTargetsUtility.ThreatDisabled(searcher)) return null;

            var scan = new Scan(searcher, maxRange, validator);
            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (pawnsOnMap[i] is Pawn candidate) scan.Consider(candidate);
            }
            return scan.Best;
        }

        /// <summary>Membership test by index rather than <c>IEnumerable.Contains</c>, which would allocate an
        /// enumerator on a path that runs once per pawn per interval. The list is all but always empty.</summary>
        private static bool Holds(IReadOnlyList<Pawn> list, Pawn pawn)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], pawn)) return true;
            }
            return false;
        }

        /// <summary>
        /// One candidate weighed against the best so far. A mutable struct so both entry points above can run
        /// it over their own candidate set without allocating anything and without a second copy of the
        /// predicate chain.
        /// </summary>
        private struct Scan
        {
            private readonly Pawn searcher;
            private readonly float maxRangeSquared;
            private readonly Func<Pawn, bool>? validator;
            private float bestDistSq;

            public Scan(Pawn searcher, float maxRange, Func<Pawn, bool>? validator)
            {
                this.searcher = searcher;
                maxRangeSquared = maxRange * maxRange;
                this.validator = validator;
                bestDistSq = float.MaxValue;
                Best = null;
            }

            public Pawn? Best { get; private set; }

            public void Consider(Pawn candidate)
            {
                // Cheapest tests first, and the order is load-bearing rather than cosmetic: distance is two
                // int subtractions; AttackTargetsUtility.HostileTo is two grudge checks and a faction-relation
                // lookup. Every one of these is a pure predicate with no side effect, so reordering them
                // cannot change which pawn is returned — only what it costs to find out. Measured in
                // docs/perf/constant-think-tree.md.
                float distSq = (candidate.Position - searcher.Position).LengthHorizontalSquared;
                if (distSq > maxRangeSquared) return;
                if (Best != null && !Closer(distSq, candidate, bestDistSq, Best)) return;
                if (AttackTargetsUtility.ThreatDisabled(candidate)) return;
                if (!AttackTargetsUtility.HostileTo(searcher, candidate)) return;
                if (validator != null && !validator(candidate)) return;

                // Reachability last: it rebuilds regions on demand, so it is the one expensive test here and
                // is only paid for a candidate that has already won on distance.
                if (!Reachability.CanReach(searcher, candidate, PathEndMode.Touch)) return;

                Best = candidate;
                bestDistSq = distSq;
            }

            /// <summary>Nearest wins; equal distance breaks on thing id. A total order over distinct pawns,
            /// which is what makes the answer independent of the order candidates arrive in — and therefore
            /// the same whether they came from the index or from a walk of the whole map.</summary>
            private static bool Closer(float distSq, Pawn candidate, float bestDistSq, Pawn best) =>
                distSq < bestDistSq || (distSq == bestDistSq && candidate.thingIDNumber < best.thingIDNumber);
        }
    }
}
