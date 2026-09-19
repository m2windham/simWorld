using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Director
{
    /// <summary>
    /// A pack of animals turns on a settlement (RimWorld: <c>IncidentWorker_ManhunterPack</c>).
    ///
    /// <para/><b>What this replaces.</b> <see cref="IncidentWorker_ThreatEvent"/>, whose entire body was
    /// <c>=&gt; parms.points &gt; 0f</c>. The incident fired, reported success to the storyteller, spent its
    /// refire timer, counted against the threat budget — and spawned nothing. Shipped content pointing at a
    /// no-op, which is worse than absent content, because every system downstream believed a threat had
    /// happened.
    ///
    /// <para/><b>Where it lands is the player's decision, and that is the point.</b> A watched settlement
    /// fights the pack on a real map with real animals; an unwatched one resolves abstractly through
    /// <see cref="SettlementRaidResolver"/>, and does worse. This is not a new rule — it is exactly what
    /// <see cref="IncidentWorker_RaidEnemy"/> already does, asked the same way
    /// (<c>God.AttentionManager</c>'s focused tile). A god can watch one settlement. Threats do not queue up
    /// politely at that one. So the standing question "where am I looking" acquires a cost, which is what
    /// makes attention a currency rather than a camera.
    ///
    /// <para/><b>The pack turns among them; it does not march in.</b> A raid arrives at a map edge and walks,
    /// carried by <c>AI.DutyDefOf.AssaultSettlement</c> — and <c>ThinkTrees_Animal.xml</c> has no
    /// <c>ThinkNode_Duty</c> in it, so an animal handed that duty would simply ignore it. Edge-spawning this
    /// pack would put it a hundred cells outside every acquire radius with nothing to walk it in, and
    /// <c>AI.JobGiver_WanderAnywhere</c> would take over: a "threat" that mills about in a field. That is the
    /// stranded-squad bug <see cref="IncidentWorker_RaidEnemy"/>'s own doc records paying for, and it is not
    /// worth paying twice. Turning among the settlement is also what the incident describes — animals that
    /// were already there and snapped — and it reads to the player immediately instead of after a walk.
    ///
    /// <para/><b>Its dice are its own.</b> Every roll comes from <see cref="NamedRand"/>, never the ambient
    /// stream, and the pack is composed <i>before</i> the watched/unwatched branch — so both arms of a probe
    /// run draw identically and the difference between them is the thing being measured rather than a
    /// reshuffle. See <see cref="NamedRand"/>'s own doc for why that is not optional.
    /// </summary>
    public sealed class IncidentWorker_ManhunterPack : IncidentWorker
    {
        /// <summary>The stream every roll in this incident comes from, qualified by the tick so two firings in
        /// one game differ while a replay of the same game does not.</summary>
        private const string StreamName = "ManhunterPack";

        /// <summary>
        /// How long the pack stays hostile. Not sourced from RimWorld — its manhunter state ends on a mental
        /// state recovery roll this port has no equivalent of for animals, so a deadline stands in. Long
        /// enough that a settlement cannot simply wait indoors for it to pass, short enough that survivors
        /// are not hunted forever. Pinned by behaviour in the tests rather than by this literal.
        /// </summary>
        private const int ManhunterDurationTicks = GenDate.TicksPerDay;

        /// <summary>
        /// Points per unit of a kind's <see cref="PawnKindDef.combatPower"/>, i.e. how big a pack the
        /// storyteller's threat budget buys. A settlement is not a battlefield and its people are not
        /// soldiers, so a pack is deliberately cheaper per head than a raid squad of the same points.
        /// </summary>
        private const float PointsPerPower = 1f;

        private const int MinPack = 2;

        private const int MaxPack = 12;

        /// <summary>Animals below this are not a threat to anybody and turning them manhunter is a joke rather
        /// than an incident — the shipped <c>Chicken</c> is combat power 4.</summary>
        private const float MinCredibleCombatPower = 10f;

        /// <summary>How far from its anchor the pack scatters when it turns.</summary>
        private const int ScatterRadius = 8;

        private const int ScatterTries = 12;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));

            RandomStream rand = NamedRand.For(
                StreamName + "|" + (Find.TickManager?.TicksGame ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Composed before anything is asked about where it lands, so the watched and unwatched arms of the
            // same seed draw the same pack and differ only in what happens to it.
            PawnKindDef? kind = ChooseKind();
            if (kind == null) return false;

            int count = PackSize(parms.points, kind, rand);
            if (count <= 0) return false;

            var civ = parms.target as CivilizationTarget;
            if (civ == null) return false;
            SimWorld.World.Settlement? settlement = civ.ChooseTargetSettlement(rand);

            // Ablated: everything above has happened — the storyteller picked this incident, the refire
            // timer is spent, the pack was sized — and nothing below will. The one draw Generate would have
            // taken is taken anyway so the stream lands where it would have, because a defect that advances
            // its own stream differently when off is a defect that cannot be measured. See Ablation.
            if (Ablation.IsDisabled(def?.defName))
            {
                _ = rand.Int;
                return true;
            }

            List<Pawn> pack = Generate(kind, count, rand);
            if (pack.Count == 0) return false;
            LastPack = pack;

            // Every animal in this pack remembers that this incident put it here, so a citizen it kills on a
            // watched map is attributed to the pack rather than merely to "Injury". See Pawn.spawnedByIncident.
            for (int i = 0; i < pack.Count; i++) pack[i].spawnedByIncident = def?.defName;

            Map.Map? map = ArrivalMapFor(civ, settlement);
            if (map != null)
            {
                TurnAmong(pack, map, rand);
                SendLetter(settlement, kind, pack.Count, watched: true);
                return true;
            }

            if (settlement != null)
            {
                // Nobody is looking. The pack is settled the way an unattended raid is — this port's one
                // answer to "what happens on a map that does not exist".
                // Attribution needs nothing here: every animal in the pack carries spawnedByIncident, and the
                // resolver credits each citizen it kills to the provenance of whoever killed them. Crediting
                // from the outcome's CitizensKilled instead was the first attempt and was wrong — it counted
                // deaths the ledger itself had declined to count, and reported four kills against a total of
                // zero. The resolver reads its own ledger delta, so the two axes cannot drift apart.
                SettlementRaidResolver.Resolve(settlement, pack, null, rand);

                SendLetter(settlement, kind, pack.Count, watched: false);
                return true;
            }

            return false;
        }

        /// <summary>The pack this worker last composed, for a test that wants to assert about it without
        /// re-deriving it. Mirrors <c>IncidentWorker_RaidEnemy.LastRaidPawns</c>.</summary>
        public IReadOnlyList<Pawn>? LastPack { get; private set; }

        /// <summary>
        /// The map this pack physically turns on, or null when it must resolve abstractly. A settlement's
        /// interior counts only while the god is watching that settlement; with no settlement at all — a
        /// target posed by hand — the bare <see cref="CivilizationTarget.Map"/> hook is honoured, exactly as
        /// <see cref="IncidentWorker_RaidEnemy"/> honours it.
        /// </summary>
        private static Map.Map? ArrivalMapFor(CivilizationTarget civ, SimWorld.World.Settlement? settlement)
        {
            if (settlement == null) return civ.MapFor(null);
            return IsWatched(settlement) ? civ.MapFor(settlement) : null;
        }

        // ---- composing the pack ----

        /// <summary>
        /// The heaviest credible animal in content, chosen deterministically rather than at random: with three
        /// shipped kinds a weighted roll is theatre, and a pack should be the same animal throughout because
        /// that is what a pack is. Returns null when content ships nothing that could threaten anyone, which
        /// is a real answer — the incident declines rather than inventing a menace out of chickens.
        /// </summary>
        private static PawnKindDef? ChooseKind()
        {
            PawnKindDef? best = null;
            IReadOnlyList<PawnKindDef> all = DefDatabase<PawnKindDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                PawnKindDef k = all[i];
                if (k.race?.race == null || k.race.race.Humanlike) continue;
                if (k.combatPower < MinCredibleCombatPower) continue;
                if (best == null
                    || k.combatPower > best.combatPower
                    || (k.combatPower == best.combatPower && string.CompareOrdinal(k.defName, best.defName) < 0))
                {
                    best = k;
                }
            }
            return best;
        }

        /// <summary>What the threat budget buys, clamped so a rich civilization does not get an unsurvivable
        /// wall of animals and a poor one still gets a pack rather than a single cross beast.</summary>
        private static int PackSize(float points, PawnKindDef kind, RandomStream rand)
        {
            float power = Math.Max(1f, kind.combatPower);
            int affordable = (int)Math.Floor(points * PointsPerPower / power);
            if (affordable < MinPack) affordable = MinPack;
            if (affordable > MaxPack) affordable = MaxPack;

            // A little spread so two firings at the same points are not identical, drawn from this incident's
            // own stream.
            return Math.Max(MinPack, affordable - rand.Range(0, 2));
        }

        /// <summary>
        /// Builds the pack with the ambient stream <b>pushed aside</b>, and this is not a flourish.
        ///
        /// <para/><see cref="PawnGenerator"/> rolls against <see cref="Rand.Current"/> internally — it has no
        /// idea this incident has a stream of its own — so generating a dozen animals silently spent 33 draws
        /// of the shared stream and shifted every later roll in the game. Switching the incident on would then
        /// have moved the weather, the births and the raids too, and a probe run "with the pack" versus
        /// "without" would have measured mostly that reshuffle. The test that caught it asserts the ambient
        /// stream's position is unchanged across a firing, which is the only way this stays true as the worker
        /// grows.
        ///
        /// <para/><see cref="Rand.PushState(int)"/> is RimWorld's own answer and restores the position exactly
        /// on pop, so the generator gets a seed this incident controls and the rest of the game gets its
        /// stream back untouched. <b>Holding a private stream is not enough on its own</b>: anything called
        /// that draws ambiently has to be scoped like this, or the discipline leaks through the helper.
        /// </summary>
        private static List<Pawn> Generate(PawnKindDef kind, int count, RandomStream rand)
        {
            var pack = new List<Pawn>(count);
            Rand.PushState(rand.Int);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    pack.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind)));
                }
            }
            finally
            {
                Rand.PopState();
            }
            return pack;
        }

        // ---- the watched path ----

        /// <summary>
        /// Puts the pack on the map among the people it is turning on, scattered rather than stacked, and
        /// makes it hostile. Hostility is set <b>after</b> spawning on purpose:
        /// <c>Pawn_MindState.manhunterUntilTick</c> files the animal in <c>AI.AttackTargetsCache</c>'s
        /// out-of-faction bucket as it is written, and a pawn with no map yet has no index to be filed in.
        /// </summary>
        private static void TurnAmong(IReadOnlyList<Pawn> pack, Map.Map map, RandomStream rand)
        {
            IntVec3 anchor = AnchorAmongCitizens(map, rand);
            int until = (Find.TickManager?.TicksGame ?? 0) + ManhunterDurationTicks;

            for (int i = 0; i < pack.Count; i++)
            {
                GenSpawn.Spawn(pack[i], ScatterNear(anchor, map, rand), map);
                pack[i].mindState.manhunterUntilTick = until;
            }
        }

        /// <summary>
        /// Where the pack turns: on somebody. Anchoring on a citizen rather than on a map edge is what keeps
        /// the pack inside <c>AI.JobGiver_AIFightEnemies</c>' acquire radius from its first tick — see the
        /// class doc on why an edge arrival would produce a herd standing in a field. Falls back to the map's
        /// middle on the empty map a test can build.
        /// </summary>
        private static IntVec3 AnchorAmongCitizens(Map.Map map, RandomStream rand)
        {
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            var humanlikes = new List<Pawn>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].RaceProps.Humanlike && !all[i].Dead) humanlikes.Add(all[i]);
            }

            if (humanlikes.Count == 0) return new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            return humanlikes[rand.Range(0, humanlikes.Count)].Position;
        }

        /// <summary>A standable cell near <paramref name="root"/>, falling back to it. The sibling of
        /// <c>IncidentWorker_RaidEnemy</c>'s <c>RandomClosewalkCellNear</c> and kept separate from it: the two
        /// scatter around different things for different reasons, and the raid worker's version is bound up
        /// with an edge arrival this incident deliberately does not do.</summary>
        private static IntVec3 ScatterNear(IntVec3 root, Map.Map map, RandomStream rand)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            int inRadius = GenRadial.NumCellsInRadius(ScatterRadius);
            for (int i = 0; i < ScatterTries; i++)
            {
                IntVec3 candidate = root + pattern[rand.Range(0, inRadius)];
                if (GenGrid.InBounds(candidate, map) && GenGrid.Standable(candidate, map)) return candidate;
            }
            return root;
        }

        // ---- shared with the raid worker in rule, not in code ----

        /// <summary>Whether the god currently has this settlement open. The same one-line question
        /// <c>IncidentWorker_RaidEnemy</c> asks, of the same authority, so "which settlement is being watched"
        /// keeps having exactly one definition.</summary>
        private static bool IsWatched(SimWorld.World.Settlement settlement) =>
            Find.God.Attention.FocusedTile == settlement.tile;

        /// <summary>
        /// The player is told either way. An unwatched settlement's mauling that produced no letter would be
        /// indistinguishable from nothing happening, and a threat the player never hears about cannot teach
        /// them anything about where to look next time — which is the entire loop this incident exists to
        /// feed.
        /// </summary>
        private static void SendLetter(SimWorld.World.Settlement? settlement, PawnKindDef kind, int count, bool watched)
        {
            string where = string.IsNullOrEmpty(settlement?.name) ? "a settlement" : settlement!.name;
            string text = watched
                ? $"A pack of {count} {kind.label} has turned manhunter at {where}."
                : $"Word reaches you late: a pack of {count} {kind.label} turned manhunter at {where} while your attention was elsewhere.";

            Find.LetterStack?.ReceiveLetter("Manhunter pack: " + where, text, LetterDefOf.ThreatBig);
        }
    }
}
