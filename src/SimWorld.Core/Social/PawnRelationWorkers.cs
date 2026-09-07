using SimWorld.Pawns;

namespace SimWorld.Social
{
    /// <summary>
    /// "Other is my spouse" — reads <see cref="Pawn_RelationsTracker.spouseId"/> directly rather than storing
    /// a second, possibly-stale copy. Symmetric for free: if A's spouseId is B, demography guarantees B's
    /// spouseId is A (<see cref="FamilyManager.FoundHousehold"/> sets both sides at once).
    /// </summary>
    public sealed class PawnRelationWorker_Spouse : PawnRelationWorker
    {
        public override bool InRelation(Pawn me, Pawn other) =>
            me != null && other != null && me != other && me.relations.spouseId == other.thingIDNumber;
    }

    /// <summary>"Other is my parent" — reads <see cref="Pawn_RelationsTracker.ParentIds"/>, set once at birth
    /// by <see cref="FamilyManager"/> and never duplicated here.</summary>
    public sealed class PawnRelationWorker_Parent : PawnRelationWorker
    {
        public override bool InRelation(Pawn me, Pawn other)
        {
            if (me == null || other == null || me == other) return false;
            return me.relations.parentIdA == other.thingIDNumber || me.relations.parentIdB == other.thingIDNumber;
        }
    }

    /// <summary>"Other is my child" — the mirror of <see cref="PawnRelationWorker_Parent"/>, reading
    /// <see cref="Pawn_RelationsTracker.childIds"/> instead of a second parent-side list.</summary>
    public sealed class PawnRelationWorker_Child : PawnRelationWorker
    {
        public override bool InRelation(Pawn me, Pawn other)
        {
            if (me == null || other == null || me == other) return false;
            return me.relations.childIds.Contains(other.thingIDNumber);
        }
    }

    /// <summary>
    /// "Other shares at least one parent with me" — both pawns' own <see cref="Pawn_RelationsTracker.ParentIds"/>
    /// compared directly; no shared-sibling list is stored anywhere. Half-siblings (one shared parent) count,
    /// same as full siblings; demography does not yet distinguish the two.
    /// </summary>
    public sealed class PawnRelationWorker_Sibling : PawnRelationWorker
    {
        public override bool InRelation(Pawn me, Pawn other)
        {
            if (me == null || other == null || me == other) return false;
            int a1 = me.relations.parentIdA, a2 = me.relations.parentIdB;
            int b1 = other.relations.parentIdA, b2 = other.relations.parentIdB;
            if (a1 == Pawn_RelationsTracker.None && a2 == Pawn_RelationsTracker.None) return false;
            return (a1 != Pawn_RelationsTracker.None && (a1 == b1 || a1 == b2)) ||
                   (a2 != Pawn_RelationsTracker.None && (a2 == b1 || a2 == b2));
        }
    }
}
