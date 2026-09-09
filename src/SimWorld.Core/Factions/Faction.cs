using System;
using System.Collections.Generic;
using System.Text;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// One civilization instance (RimWorld: <c>RimWorld.Faction</c>): identity for settlements/saves to
    /// reference, plus its diplomatic relation with every other faction. Ideology/memes are out of scope.
    /// </summary>
    public class Faction : IExposable, ILoadReferenceable
    {
        /// <summary>Interval <see cref="FactionTick"/> re-checks natural goodwill drift, in ticks (RimWorld-style hash-interval cadence; see that method's doc comment).</summary>
        public const int GoodwillCheckInterval = 2500;

        public FactionDef def = null!;
        public string name = "";
        public string loadID = "";

        /// <summary>True once this faction has been wiped out; RandomEnemyFaction/etc. exclude it by default.</summary>
        public bool defeated;

        private readonly List<FactionRelation> relations = new List<FactionRelation>();

        /// <summary>Fractional goodwill carried between <see cref="GoodwillCheckInterval"/> checks so a slow daily rate still eventually moves a whole point.</summary>
        private float goodwillDriftAccumulator;

        /// <summary>Raised after <see cref="TryAffectGoodwillWith"/> actually changes goodwill: (this, other, actual change applied, reason).</summary>
        public event Action<Faction, Faction, int, string?>? GoodwillChanged;

        /// <summary>Raised after a goodwill change crosses a relation-kind threshold: (this, other, new kind cast to int, reason).</summary>
        public event Action<Faction, Faction, int, string?>? RelationKindChanged;

        public IReadOnlyList<FactionRelation> Relations => relations;

        /// <summary>For Scribe's deep-load construction.</summary>
        public Faction()
        {
        }

        public Faction(FactionDef def, string name, string loadID)
        {
            this.def = def;
            this.name = name;
            this.loadID = loadID;
        }

        public string GetUniqueLoadID() => loadID;

        // ---- relations ----

        /// <summary>
        /// This faction's relation object with <paramref name="other"/>, or null when <paramref name="allowNull"/>
        /// and none exists yet (RimWorld: <c>Faction.RelationWith</c>). Throws instead when a relation is
        /// expected to already exist and <paramref name="allowNull"/> is false.
        /// </summary>
        public FactionRelation? RelationWith(Faction other, bool allowNull = false)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this))
            {
                if (allowNull) return null;
                throw new InvalidOperationException("A faction has no relation with itself (" + this + ").");
            }
            for (int i = 0; i < relations.Count; i++)
            {
                if (ReferenceEquals(relations[i].other, other)) return relations[i];
            }
            if (allowNull) return null;
            throw new InvalidOperationException("No relation recorded between " + this + " and " + other + ".");
        }

        private FactionRelation GetOrAddRelation(Faction other)
        {
            FactionRelation? existing = RelationWith(other, allowNull: true);
            if (existing != null) return existing;
            var created = new FactionRelation(other, 0, FactionRelationKind.Neutral);
            relations.Add(created);
            return created;
        }

        public int GoodwillWith(Faction other) => RelationWith(other, allowNull: true)?.baseGoodwill ?? 0;

        public FactionRelationKind RelationKindWith(Faction other) => RelationWith(other, allowNull: true)?.kind ?? FactionRelationKind.Neutral;

        public bool HostileTo(Faction other) => !ReferenceEquals(other, this) && RelationKindWith(other) == FactionRelationKind.Hostile;

        /// <summary>RimWorld's thresholds: ≤ -75 is Hostile, ≥ 75 is Ally, otherwise Neutral.</summary>
        public static FactionRelationKind KindFromGoodwill(int goodwill)
        {
            if (goodwill <= -75) return FactionRelationKind.Hostile;
            if (goodwill >= 75) return FactionRelationKind.Ally;
            return FactionRelationKind.Neutral;
        }

        /// <summary>
        /// Sets both sides of the relation directly, bypassing the day-to-day goodwill flow (RimWorld:
        /// <c>Faction.SetRelation</c>/<c>SetRelationDirect</c>). Used at world generation and for scripted
        /// diplomacy changes; does not raise <see cref="GoodwillChanged"/>/<see cref="RelationKindChanged"/>.
        /// </summary>
        public void SetRelationDirect(Faction other, FactionRelationKind kind, int goodwill)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            int clamped = GenMath.Clamp(goodwill, -100, 100);
            FactionRelation mine = GetOrAddRelation(other);
            mine.baseGoodwill = clamped;
            mine.kind = kind;
            FactionRelation theirs = other.GetOrAddRelation(this);
            theirs.baseGoodwill = clamped;
            theirs.kind = kind;
        }

        /// <summary>
        /// Seeds the initial relation with <paramref name="other"/> at world generation (RimWorld:
        /// <c>FactionGenerator</c>'s initial-relation rules):
        /// permanentEnemy on either side always locks Hostile at -100; a player-vs-NPC pair draws from the
        /// NPC's <see cref="FactionDef.startingGoodwill"/>; an NPC-vs-NPC pair starts Neutral at 0
        /// (RimWorld does not roll rival-NPC relations against each other at generation).
        /// </summary>
        public void TryMakeInitialRelationsWith(Faction other, RandomStream rand)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (ReferenceEquals(other, this)) return;

            if (def.permanentEnemy || other.def.permanentEnemy)
            {
                SetRelationDirect(other, FactionRelationKind.Hostile, -100);
                // A permanent enemy is permanently at war, not merely permanently Hostile — see WarState's
                // own doc for why the two are kept separate everywhere else. This is the one place they are
                // forced to agree at creation, since "permanent enemy" has no lesser reading.
                SetWarState(other, WarState.War);
                return;
            }

            if (def.isPlayer != other.def.isPlayer)
            {
                Faction npc = def.isPlayer ? other : this;
                int goodwill = rand.Range(npc.def.startingGoodwill);
                SetRelationDirect(other, KindFromGoodwill(goodwill), goodwill);
                return;
            }

            SetRelationDirect(other, FactionRelationKind.Neutral, 0);
        }

        /// <summary>
        /// Changes goodwill with <paramref name="other"/> by <paramref name="goodwillChange"/>, clamped to
        /// [-100, 100] and kept symmetric on both sides (RimWorld: <c>Faction.TryAffectGoodwillWith</c>).
        /// A no-op (returns false) for a permanent-enemy pair, a zero change, or a change the clamp fully
        /// absorbs. <paramref name="canSendMessage"/>/<paramref name="canSendHostilityLetter"/> are kept for
        /// signature parity with RimWorld's call sites; no message/letter system exists yet to honor them.
        /// </summary>
        public bool TryAffectGoodwillWith(Faction other, int goodwillChange, bool canSendMessage = true, bool canSendHostilityLetter = true, string? reason = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this) || goodwillChange == 0) return false;
            if (def.permanentEnemy || other.def.permanentEnemy) return false;

            int current = GoodwillWith(other);
            int updated = GenMath.Clamp(current + goodwillChange, -100, 100);
            int actualChange = updated - current;
            if (actualChange == 0) return false;

            FactionRelationKind oldKind = RelationKindWith(other);
            GetOrAddRelation(other).baseGoodwill = updated;
            other.GetOrAddRelation(this).baseGoodwill = updated;

            FactionRelationKind newKind = KindFromGoodwill(updated);
            if (newKind != oldKind)
            {
                GetOrAddRelation(other).kind = newKind;
                other.GetOrAddRelation(this).kind = newKind;
            }

            GoodwillChanged?.Invoke(this, other, actualChange, reason);
            if (newKind != oldKind)
            {
                RelationKindChanged?.Invoke(this, other, (int)newKind, reason);
            }
            return true;
        }

        // ---- war and peace (SimWorld's own translation — see WarState's own doc) ----

        /// <summary>
        /// Goodwill change <see cref="DeclareWar"/> applies. This port's own number: RimWorld has no
        /// player-invoked war declaration to source one from — its own hostility swings come only from
        /// incidents (raids, attacks), never a deliberate act. Deliberately oversized (goodwill only ever
        /// spans [-100, 100]) so a declaration always lands at the -100 floor regardless of where the
        /// relation started, the same "clamp does the guaranteeing" idiom <see cref="TryMakeInitialRelationsWith"/>
        /// already relies on for permanent enemies.
        /// </summary>
        public const int WarDeclarationGoodwillChange = -200;

        /// <summary>Goodwill gain <see cref="MakePeace"/> applies. This port's own number — a de-escalation, not a reset: peace does not by itself erase enough history to leave Hostile territory (goodwill stays a separate, softer readout from WarState — see that enum's own doc).</summary>
        public const int PeaceGoodwillChange = 20;

        /// <summary>Currently at war with <paramref name="other"/> (see <see cref="WarState"/>). False between any faction and itself.</summary>
        public bool WarWith(Faction other) => !ReferenceEquals(other, this) && RelationWith(other, allowNull: true)?.warState == WarState.War;

        /// <summary>
        /// Explicit war entry (RimWorld has no equivalent act — see <see cref="Factions.WarState"/>'s own
        /// doc for why this civilization-scale game gives war its own state instead of reading it off
        /// goodwill). Refuses (returns false, changes nothing) when already at war with
        /// <paramref name="other"/>, or while an active non-aggression <see cref="Treaty"/> holds
        /// between the two (the entry condition a pact exists to enforce — see <see cref="HasActiveTreaty"/>);
        /// otherwise sets <see cref="WarState.War"/> symmetrically and drops goodwill toward the Hostile
        /// floor via <see cref="WarDeclarationGoodwillChange"/>.
        /// </summary>
        public bool DeclareWar(Faction other, string? reason = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return false;
            if (WarWith(other)) return false;
            if (HasActiveTreaty(other, t => t.nonAggression)) return false;

            SetWarState(other, WarState.War);
            TryAffectGoodwillWith(other, WarDeclarationGoodwillChange, canSendMessage: false, canSendHostilityLetter: false, reason: reason ?? "DeclaredWar");
            return true;
        }

        /// <summary>
        /// Explicit war exit. Refuses (returns false, changes nothing) for a permanent-enemy pair (never
        /// leaves war — matches <see cref="TryAffectGoodwillWith"/>'s own permanent-enemy refusal) or when
        /// not currently at war with <paramref name="other"/>; otherwise sets <see cref="WarState.Peace"/>
        /// symmetrically and applies <see cref="PeaceGoodwillChange"/>. See <see cref="SignTreaty"/> for the
        /// content-driven version of this same exit (a non-aggression pact signed while at war calls this).
        /// </summary>
        public bool MakePeace(Faction other, string? reason = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return false;
            if (def.permanentEnemy || other.def.permanentEnemy) return false;
            if (!WarWith(other)) return false;

            SetWarState(other, WarState.Peace);
            TryAffectGoodwillWith(other, PeaceGoodwillChange, canSendMessage: false, canSendHostilityLetter: false, reason: reason ?? "MadePeace");
            return true;
        }

        private void SetWarState(Faction other, WarState state)
        {
            GetOrAddRelation(other).warState = state;
            other.GetOrAddRelation(this).warState = state;
        }

        // ---- treaties (SimWorld's own translation — see TreatyDef's own doc) ----

        /// <summary>
        /// Signs <paramref name="def"/> between this faction and <paramref name="other"/>: records a
        /// <see cref="Treaty"/> on both sides' <see cref="FactionRelation.treaties"/>, applies
        /// <see cref="TreatyDef.signingGoodwill"/>, and — when <paramref name="def"/> carries
        /// <see cref="TreatyDef.nonAggression"/> and the two are currently at war — calls
        /// <see cref="MakePeace"/> first (signing a non-aggression pact while at war is itself how a war
        /// ends here; RimWorld has no equivalent act). Refuses (returns false, changes nothing) for a
        /// permanent-enemy pair, which can never hold any treaty.
        /// </summary>
        public bool SignTreaty(Faction other, TreatyDef def, string? reason = null)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (ReferenceEquals(other, this)) return false;
            if (this.def.permanentEnemy || other.def.permanentEnemy) return false;

            if (def.nonAggression && WarWith(other))
            {
                MakePeace(other, reason ?? ("Treaty: " + def.LabelCap));
            }

            int now = Find.TickManager.TicksGame;
            GetOrAddRelation(other).treaties.Add(new Treaty(def, now));
            other.GetOrAddRelation(this).treaties.Add(new Treaty(def, now));

            if (def.signingGoodwill != 0)
            {
                TryAffectGoodwillWith(other, def.signingGoodwill, reason: reason ?? ("Treaty: " + def.LabelCap));
            }
            return true;
        }

        /// <summary>Any treaty with <paramref name="other"/> — active right now, per <see cref="Treaty.IsActive"/> — whose def satisfies <paramref name="matches"/>.</summary>
        public bool HasActiveTreaty(Faction other, Func<TreatyDef, bool> matches)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            FactionRelation? rel = RelationWith(other, allowNull: true);
            if (rel == null) return false;

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < rel.treaties.Count; i++)
            {
                Treaty t = rel.treaties[i];
                if (t.IsActive(now) && matches(t.def)) return true;
            }
            return false;
        }

        /// <summary>Shorthand for the entry condition <see cref="DeclareWar"/> itself already checks.</summary>
        public bool HasNonAggressionPactWith(Faction other) => HasActiveTreaty(other, d => d.nonAggression);

        /// <summary>
        /// Best (largest) <see cref="TreatyDef.tradeAccessPriceGain"/> among every currently active
        /// <see cref="TreatyDef.tradeAccess"/> treaty with <paramref name="other"/>; 0 when none. Feeds
        /// <c>TradeDeal.settlementGain</c> the same slot a negotiator's own gain occupies — see
        /// <c>Economy.SettlementTradeUtility.OpenSession</c>.
        /// </summary>
        public float TradeAccessPriceGainWith(Faction other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            FactionRelation? rel = RelationWith(other, allowNull: true);
            if (rel == null) return 0f;

            int now = Find.TickManager.TicksGame;
            float best = 0f;
            for (int i = 0; i < rel.treaties.Count; i++)
            {
                Treaty t = rel.treaties[i];
                if (t.IsActive(now) && t.def.tradeAccess) best = Math.Max(best, t.def.tradeAccessPriceGain);
            }
            return best;
        }

        /// <summary>
        /// Call once per game tick (RimWorld: <c>Faction.FactionTick</c>). Only acts every
        /// <see cref="GoodwillCheckInterval"/> ticks (RimWorld re-checks faction goodwill on a coarse
        /// interval rather than every tick); each check adds this def's daily gain/fall rate, prorated by
        /// the interval's share of a day, toward <see cref="FactionDef.naturalColonyGoodwill"/>'s midpoint.
        /// The fractional remainder accumulates in <see cref="goodwillDriftAccumulator"/> so a slow rate
        /// (well under 1/day) still moves a whole goodwill point every few checks instead of never rounding
        /// up. No-op for the player's own faction, when no player faction is registered, or against a
        /// permanent enemy (whose relation <see cref="TryAffectGoodwillWith"/> already refuses to move).
        /// </summary>
        public void FactionTick()
        {
            if (Find.TickManager.TicksGame % GoodwillCheckInterval != 0) return;
            ApplyNaturalGoodwillDrift();
        }

        private void ApplyNaturalGoodwillDrift()
        {
            if (def.isPlayer) return;
            Faction? player = Find.FactionManager.OfPlayer;
            if (player == null || ReferenceEquals(player, this)) return;
            if (def.permanentEnemy || player.def.permanentEnemy) return;

            float target = def.naturalColonyGoodwill.Average;
            int current = GoodwillWith(player);
            float diff = target - current;
            if (diff > -0.5f && diff < 0.5f)
            {
                goodwillDriftAccumulator = 0f;
                return;
            }

            float dailyRate = diff > 0f ? def.goodwillDailyGain : def.goodwillDailyFall;
            if (dailyRate <= 0f) return;

            goodwillDriftAccumulator += dailyRate * GoodwillCheckInterval / (float)GenDate.TicksPerDay;
            int step = (int)goodwillDriftAccumulator;
            if (step <= 0) return;
            goodwillDriftAccumulator -= step;

            int signedStep = diff > 0f ? step : -step;
            if (diff > 0f && signedStep > diff) signedStep = (int)Math.Ceiling(diff);
            if (diff < 0f && signedStep < diff) signedStep = (int)Math.Floor(diff);
            if (signedStep == 0) return;

            TryAffectGoodwillWith(player, signedStep, canSendMessage: false, canSendHostilityLetter: false, reason: "NaturalGoodwillDrift");
        }

        public string GetReportText()
        {
            var sb = new StringBuilder();
            sb.Append(name).Append(" (").Append(def.LabelCap).Append(')');
            if (defeated) sb.Append(" [defeated]");
            foreach (FactionRelation rel in relations)
            {
                sb.Append('\n').Append("  ").Append(rel.other?.name ?? "?").Append(": ").Append(rel.kind).Append(" (").Append(rel.baseGoodwill).Append(')');
            }
            return sb.ToString();
        }

        public void ExposeData()
        {
            FactionDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref loadID, "loadID", "");
            Scribe_Values.Look(ref defeated, "defeated");

            List<FactionRelation>? r = new List<FactionRelation>(relations);
            Scribe_Collections.Look(ref r, "relations", LookMode.Deep);
            relations.Clear();
            if (r != null) relations.AddRange(r);
        }

        public override string ToString() => name;
    }
}
