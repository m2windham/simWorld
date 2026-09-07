using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Social
{
    /// <summary>
    /// A kind of relation between two pawns (RimWorld: <c>RimWorld.PawnRelationDef</c>): friend, rival, lover,
    /// ex-spouse, and the family relations demography already implies (spouse, parent, child, sibling).
    /// <see cref="Worker"/> decides whether it holds between two given pawns — for the family kinds that reads
    /// <see cref="Pawn_RelationsTracker.spouseId"/>/<c>parentIdA</c>/<c>parentIdB</c>/<c>childIds</c> directly
    /// rather than a second store that could disagree with demography's own (see the individual
    /// <see cref="PawnRelationWorker"/> subclasses in <c>PawnRelationWorkers.cs</c>); everything else is
    /// recorded as a <see cref="DirectPawnRelation"/> on each side (RimWorld's own shape: <c>Pawn_RelationsTracker
    /// .DirectRelations</c>).
    /// </summary>
    public class PawnRelationDef : Def
    {
        /// <summary>Flat opinion contribution when this relation holds between two pawns (see
        /// <see cref="Pawn_RelationsTracker.OpinionOf"/>). SimWorld's own magnitudes — not verified against
        /// RimWorld's decompiled source in this environment — chosen for a self-consistent ordering (marriage
        /// &gt; lover &gt; parent/child &gt; friend &gt; sibling &gt; ex-spouse &gt; rival) and pinned by tests
        /// asserting that ordering, not the literal numbers.</summary>
        public float opinionOffset;

        /// <summary>True for a relation demography's own ids already imply (spouse, parent, child, sibling):
        /// nothing is stored for these beyond what <see cref="Pawn_RelationsTracker"/> already carries.</summary>
        public bool familial;

        public Type workerClass = typeof(PawnRelationWorker);

        private PawnRelationWorker? workerInt;

        public PawnRelationWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (PawnRelationWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(PawnRelationWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from PawnRelationWorker.";
            }
        }
    }

    /// <summary>
    /// One stored, non-family relation instance (RimWorld: <c>Verse.DirectPawnRelation</c>). Held on
    /// <see cref="Pawn_RelationsTracker.directRelations"/> in both directions when the relation is mutual
    /// (friend, rival, lover, ex-spouse) — see <see cref="SocialUtility.AddMutualRelation"/> — the same
    /// "store on both sides" shape <see cref="FamilyManager.FoundHousehold"/> already uses for
    /// <c>spouseId</c>. <see cref="otherPawnId"/> is a load-referenceable id rather than an object reference,
    /// matching every other cross-pawn field on this tracker (<c>familyId</c>, <c>spouseId</c>…) so Scribe
    /// round-trips without deferred cross-ref resolution.
    /// </summary>
    public sealed class DirectPawnRelation : IExposable
    {
        public PawnRelationDef def = null!;
        public int otherPawnId = Pawn_RelationsTracker.None;
        public int startTicks;

        public DirectPawnRelation()
        {
        }

        public DirectPawnRelation(PawnRelationDef def, int otherPawnId, int startTicks)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.otherPawnId = otherPawnId;
            this.startTicks = startTicks;
        }

        public void ExposeData()
        {
            PawnRelationDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref otherPawnId, "otherPawnId", Pawn_RelationsTracker.None);
            Scribe_Values.Look(ref startTicks, "startTicks");
        }

        public override string ToString() => (def?.defName ?? "?") + "->" + otherPawnId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Decides whether a <see cref="PawnRelationDef"/> holds between two pawns (RimWorld:
    /// <c>RimWorld.PawnRelationWorker</c>). The default checks <see cref="Pawn_RelationsTracker.directRelations"/>
    /// — the right behaviour for every stored (non-family) kind; family kinds override this in
    /// <c>PawnRelationWorkers.cs</c> to read demography's ids instead of anything stored here.
    /// </summary>
    public class PawnRelationWorker
    {
        public PawnRelationDef def = null!;

        public virtual bool InRelation(Pawn me, Pawn other)
        {
            if (me == null || other == null || me == other) return false;
            return me.relations.HasDirectRelation(def, other.thingIDNumber);
        }
    }
}
