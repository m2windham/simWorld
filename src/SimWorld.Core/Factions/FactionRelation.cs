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
    /// in sync so the relation reads the same from either side.
    /// </summary>
    public sealed class FactionRelation : IExposable
    {
        public Faction? other;

        public int baseGoodwill;

        public FactionRelationKind kind = FactionRelationKind.Neutral;

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
        }

        public override string ToString() => (other?.name ?? "?") + ": " + kind + " (" + baseGoodwill + ")";
    }
}
