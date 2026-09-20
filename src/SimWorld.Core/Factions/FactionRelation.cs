using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>How two factions stand with each other (RimWorld: <c>RimWorld.FactionRelationKind</c>), derived from goodwill by <see cref="Faction.KindFromGoodwill"/>'s thresholds.</summary>
    public enum FactionRelationKind
    {
        Hostile,
        Neutral,
        Ally,
    }

    /// <summary>
    /// One faction's side of a relation with another (RimWorld: <c>RimWorld.FactionRelation</c>). Each of
    /// the two factions involved holds its own instance; <see cref="Faction.TryAffectGoodwillWith"/> and
    /// <see cref="Faction.SetRelationDirect"/> keep both sides' <see cref="baseGoodwill"/>/<see cref="kind"/>
    /// in sync so the relation reads the same from either side — <see cref="Faction.DeclareWar"/>,
    /// <see cref="Faction.MakePeace"/> and <see cref="Faction.SignTreaty"/> do the same for
    /// <see cref="warState"/>/<see cref="treaties"/>.
    /// </summary>
    public sealed class FactionRelation : IExposable
    {
        public Faction? other;

        public int baseGoodwill;

        public FactionRelationKind kind = FactionRelationKind.Neutral;

        /// <summary>SimWorld's own explicit war/peace state for this relation — see <see cref="WarState"/>'s own doc for why it is kept separate from <see cref="kind"/>.</summary>
        public WarState warState = WarState.Peace;

        /// <summary>
        /// <see cref="Sim.TickManager.TicksGame"/> the current war began, or null while at peace (or before any
        /// war has ever been declared on this relation). Not touched by <see cref="Faction.DeclareWar"/>/
        /// <see cref="Faction.MakePeace"/> themselves — SimWorld's core diplomacy machinery has no notion of
        /// "how long" a war has run, see those methods' own docs — every real caller in this codebase goes
        /// through <see cref="DiplomacyActions"/> instead, which is what actually stamps and clears this field.
        /// A relation forced straight to <see cref="WarState.War"/> outside that path (a permanent-enemy pair
        /// at world generation, <see cref="Faction.TryMakeInitialRelationsWith"/>) simply never gets one, which
        /// is why <see cref="DiplomacyAI"/> treats a null here as "never assess this war for exhaustion" rather
        /// than "just started" — that pair can never make peace anyway (<see cref="Faction.MakePeace"/> refuses
        /// a permanent enemy outright), so there is nothing to assess.
        /// </summary>
        public int? warStartTick;

        /// <summary>Every treaty ever signed between these two factions, expired ones included (see <see cref="Treaty.IsActive"/> — this list is never pruned, so it also doubles as this relation's own treaty history).</summary>
        public List<Treaty> treaties = new List<Treaty>();

        /// <summary>For Scribe's deep-load construction.</summary>
        public FactionRelation()
        {
        }

        public FactionRelation(Faction other, int baseGoodwill, FactionRelationKind kind)
        {
            this.other = other;
            this.baseGoodwill = baseGoodwill;
            this.kind = kind;
        }

        public void ExposeData()
        {
            Faction? o = other;
            Scribe_References.Look(ref o, "other");
            other = o;
            Scribe_Values.Look(ref baseGoodwill, "baseGoodwill");
            Scribe_Values.Look(ref kind, "kind", FactionRelationKind.Neutral);
            Scribe_Values.Look(ref warState, "warState", WarState.Peace);
            Scribe_Values.Look(ref warStartTick, "warStartTick");

            List<Treaty>? t = new List<Treaty>(treaties);
            Scribe_Collections.Look(ref t, "treaties", LookMode.Deep);
            treaties.Clear();
            if (t != null) treaties.AddRange(t);
        }

        public override string ToString() => (other?.name ?? "?") + ": " + kind + " (" + baseGoodwill + ")" + (warState == WarState.War ? " [war]" : "");
    }
}
