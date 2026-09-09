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

            List<Treaty>? t = new List<Treaty>(treaties);
            Scribe_Collections.Look(ref t, "treaties", LookMode.Deep);
            treaties.Clear();
            if (t != null) treaties.AddRange(t);
        }

        public override string ToString() => (other?.name ?? "?") + ": " + kind + " (" + baseGoodwill + ")" + (warState == WarState.War ? " [war]" : "");
    }
}
