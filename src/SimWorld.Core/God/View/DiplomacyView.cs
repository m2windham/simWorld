using System.Collections.Generic;

using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// Where a relation stands, translated from <see cref="Factions.FactionRelationKind"/> rather than
    /// exposing it directly — its own copy, the same way every other view type on this seam is its own copy of
    /// whatever simulation type it reads from (<c>docs/spec/simworld-spec.md</c> §12a: the host gets values,
    /// never a type it could use to reach back into the simulation).
    /// </summary>
    public enum DiplomaticStanding
    {
        Hostile,
        Neutral,
        Ally,
    }

    /// <summary>One treaty, active or lapsed, as the view needs it — read half of the write
    /// <see cref="GodCommands.SignTreaty"/> offers.</summary>
    public sealed class TreatyView
    {
        internal TreatyView(string defName, string label, bool nonAggression, bool tradeAccess, bool active, int? expiresAtTick)
        {
            DefName = defName;
            Label = label;
            NonAggression = nonAggression;
            TradeAccess = tradeAccess;
            Active = active;
            ExpiresAtTick = expiresAtTick;
        }

        /// <summary>The handle to pass back to <see cref="GodCommands.SignTreaty"/> to sign another of these —
        /// not a <see cref="Factions.TreatyDef"/>, for the same reason nothing else on this seam is a Def.</summary>
        public string DefName { get; }

        public string Label { get; }

        public bool NonAggression { get; }

        public bool TradeAccess { get; }

        /// <summary>Still in force right now (<see cref="Factions.Treaty.IsActive"/>) — a lapsed treaty stays in
        /// this list rather than disappearing, the same "history, not a set" reading
        /// <see cref="FactionRelation.treaties"/> itself keeps.</summary>
        public bool Active { get; }

        /// <summary>The tick this treaty lapses at, or null when it never does (<see cref="Factions.TreatyDef.durationDays"/> &lt;= 0).</summary>
        public int? ExpiresAtTick { get; }

        internal static List<TreatyView> AllOf(FactionRelation? relation, int now)
        {
            if (relation == null || relation.treaties.Count == 0) return new List<TreatyView>();

            var views = new List<TreatyView>(relation.treaties.Count);
            foreach (Treaty t in relation.treaties)
            {
                views.Add(new TreatyView(
                    t.def.defName, t.def.LabelCap, t.def.nonAggression, t.def.tradeAccess,
                    t.IsActive(now), t.def.durationDays > 0 ? t.ExpiresAtTick : (int?)null));
            }
            return views;
        }
    }

    /// <summary>
    /// This civilization's relation with one other: who they are, how they feel about us, and what state that
    /// relation is in — the read half of the diplomacy seam (see <c>GodCommands.Diplomacy.cs</c> for the write
    /// half). <c>docs/design/player-first.md</c>'s own §10 audit named this exactly: the machinery to answer
    /// "who exists, how do they feel about us, what state is the relation in" was already built and had no
    /// reader.
    /// </summary>
    public sealed class FactionRelationView
    {
        internal FactionRelationView(
            string civilizationId, string name, string civilizationDefName, string civilizationLabel,
            bool defeated, int goodwill, DiplomaticStanding standing, bool atWar, IReadOnlyList<TreatyView> treaties)
        {
            CivilizationId = civilizationId;
            Name = name;
            CivilizationDefName = civilizationDefName;
            CivilizationLabel = civilizationLabel;
            Defeated = defeated;
            Goodwill = goodwill;
            Standing = standing;
            AtWar = atWar;
            Treaties = treaties;
        }

        /// <summary>The handle to pass back to every <c>GodCommands</c> diplomacy method — a
        /// <see cref="Factions.Faction.loadID"/>, stable across a save the way <c>SettlementSummary.Tile</c> and
        /// an <see cref="EdictOption.DefName"/> already are, and never the <see cref="Factions.Faction"/> itself.</summary>
        public string CivilizationId { get; }

        public string Name { get; }

        /// <summary>What kind of civilization this is (its <see cref="Factions.FactionDef.defName"/>) — flavor
        /// and filtering, not a handle; <see cref="CivilizationId"/> is the handle.</summary>
        public string CivilizationDefName { get; }

        public string CivilizationLabel { get; }

        /// <summary>Wiped out — <see cref="Factions.Faction.defeated"/>. Still listed rather than dropped, so
        /// the player can see a civilization is gone rather than have it quietly vanish from the roster.</summary>
        public bool Defeated { get; }

        /// <summary>Our own civilization's goodwill with them, [-100, 100] — <see cref="Factions.Faction.GoodwillWith"/>.</summary>
        public int Goodwill { get; }

        public DiplomaticStanding Standing { get; }

        /// <summary>Formally at war right now (<see cref="Factions.WarState.War"/>) — kept separate from
        /// <see cref="Standing"/> being <see cref="DiplomaticStanding.Hostile"/> the same way
        /// <see cref="Factions.WarState"/>'s own doc keeps the two apart: a pair can be diplomatically hostile
        /// without a formal war, but a declared war always reads Hostile too.</summary>
        public bool AtWar { get; }

        public IReadOnlyList<TreatyView> Treaties { get; }

        /// <summary>Every other non-hidden civilization's relation with <paramref name="player"/>, in
        /// <paramref name="all"/>'s own order. Empty (not throwing) when <paramref name="player"/> is null — no
        /// player civilization yet means nothing to read a relation from, the same "not started" reading
        /// <see cref="GodViewSnapshot.ContentLoaded"/> gives the rest of the snapshot.</summary>
        internal static List<FactionRelationView> RelationsOf(Faction? player, IEnumerable<Faction> all)
        {
            var views = new List<FactionRelationView>();
            if (player == null) return views;

            int now = Find.TickManager.TicksGame;
            foreach (Faction other in all)
            {
                if (ReferenceEquals(other, player)) continue;

                FactionRelation? relation = player.RelationWith(other, allowNull: true);
                views.Add(new FactionRelationView(
                    other.loadID,
                    other.name,
                    other.def.defName,
                    other.def.LabelCap,
                    other.defeated,
                    player.GoodwillWith(other),
                    ToStanding(player.RelationKindWith(other)),
                    player.WarWith(other),
                    TreatyView.AllOf(relation, now)));
            }
            return views;
        }

        private static DiplomaticStanding ToStanding(FactionRelationKind kind) => kind switch
        {
            FactionRelationKind.Hostile => DiplomaticStanding.Hostile,
            FactionRelationKind.Ally => DiplomaticStanding.Ally,
            _ => DiplomaticStanding.Neutral,
        };
    }
}
