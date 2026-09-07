using System;
using System.Collections.Generic;
using SimWorld.Sim;
using SimWorld.Social;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A pawn's family relations: household, spouse, parents, children and generation (SimWorld's own addition
    /// — RimWorld has no direct equivalent tracker to port). Kept as its own tracker rather than folded into
    /// <see cref="Pawn_StoryTracker"/>: every other tracker hung off <see cref="Pawn"/> is already split by
    /// concern (health, needs, mind state, skills, work settings, age), and family relations have their own
    /// lifecycle — marriage, birth, bereavement — driven by <see cref="FamilyManager"/> rather than by anything
    /// story/biography-related, so giving them their own tracker keeps that separation rather than growing
    /// story tracking into a grab-bag.
    /// <para/>
    /// Every relation is stored as a load-referenceable pawn id (<see cref="Thing.thingIDNumber"/>), never an
    /// object reference, so Scribe round-trips cleanly without needing deferred cross-ref resolution — a
    /// demography pass resolves ids against whatever population list it was given (see
    /// <see cref="FamilyManager.ProcessBirths"/>).
    /// </summary>
    public class Pawn_RelationsTracker : IExposable
    {
        /// <summary>Sentinel for "no family"/"no pawn" in the id fields below.</summary>
        public const int None = -1;

        private readonly Pawn pawn;

        public int familyId = None;
        public int spouseId = None;
        public int parentIdA = None;
        public int parentIdB = None;
        public List<int> childIds = new List<int>();

        /// <summary>Generations removed from a founding, family-less pawn; a fresh household inherits the
        /// couple's own generation (see <see cref="FamilyManager.FoundHousehold"/>), a newborn is one past it.</summary>
        public int generation;

        /// <summary>Tick a bereavement marker was set (spouse/parent/child died); the thoughts system reads
        /// this later. -1 = none pending.</summary>
        public int bereavementTick = -1;

        public DeathCause? bereavementCause;

        /// <summary>
        /// Stored, non-family relations (friend, rival, lover, ex-spouse — see <see cref="PawnRelationDef"/>).
        /// Family relations (spouse, parent, child, sibling) are never stored here: they are derived on demand
        /// from <see cref="spouseId"/>/<see cref="parentIdA"/>/<see cref="parentIdB"/>/<see cref="childIds"/>
        /// by the matching <c>PawnRelationWorker</c>, so there is exactly one place either kind of relation can
        /// disagree with itself. SimWorld's own addition, mirroring RimWorld's own
        /// <c>Pawn_RelationsTracker.DirectRelations</c> shape.
        /// </summary>
        public List<DirectPawnRelation> directRelations = new List<DirectPawnRelation>();

        public Pawn_RelationsTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public bool IsMarried => spouseId != None;

        public bool HasFamily => familyId != None;

        /// <summary>Read-only view of the two parent ids, for callers that want to iterate both uniformly.</summary>
        public IReadOnlyList<int> ParentIds => new[] { parentIdA, parentIdB };

        public void SetParents(int parentA, int parentB)
        {
            parentIdA = parentA;
            parentIdB = parentB;
        }

        public void Notify_ChildBorn(int childId)
        {
            childIds.Add(childId);
        }

        /// <summary>Marks this pawn as freshly bereaved; overwrites any earlier pending marker with the most
        /// recent loss rather than queuing a history of them (SimWorld hook: a future thoughts worker consumes
        /// this the same way it reads any other momentary flag).</summary>
        public void Notify_Bereaved(DeathCause cause)
        {
            bereavementTick = Find.TickManager.TicksGame;
            bereavementCause = cause;
        }

        // ---- Social layer (SimWorld.Social) ----

        public bool HasDirectRelation(PawnRelationDef def, int otherPawnId)
        {
            for (int i = 0; i < directRelations.Count; i++)
            {
                if (directRelations[i].def == def && directRelations[i].otherPawnId == otherPawnId) return true;
            }
            return false;
        }

        public void AddDirectRelation(PawnRelationDef def, int otherPawnId)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (HasDirectRelation(def, otherPawnId)) return;
            directRelations.Add(new DirectPawnRelation(def, otherPawnId, Find.TickManager.TicksGame));
        }

        public void RemoveDirectRelation(PawnRelationDef def, int otherPawnId)
        {
            directRelations.RemoveAll(r => r.def == def && r.otherPawnId == otherPawnId);
        }

        /// <summary>Every stored relation with <paramref name="otherPawnId"/>, family relations excluded (those never appear here — see <see cref="directRelations"/>).</summary>
        public IEnumerable<DirectPawnRelation> DirectRelationsWith(int otherPawnId)
        {
            foreach (DirectPawnRelation r in directRelations)
            {
                if (r.otherPawnId == otherPawnId) yield return r;
            }
        }

        /// <summary>
        /// This pawn's opinion of <paramref name="other"/> (RimWorld: <c>Pawn_RelationsTracker.OpinionOf</c>):
        /// relation type (family, derived from demography's ids, plus any stored <see cref="DirectPawnRelation"/>),
        /// social memories about that specific pawn, personality traits, and a stable per-pair compatibility
        /// factor — see <see cref="SocialUtility.OpinionOf"/> for the composition. Clamped to
        /// [<see cref="SocialTuning.MinOpinion"/>, <see cref="SocialTuning.MaxOpinion"/>].
        /// </summary>
        public int OpinionOf(Pawn other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return other == pawn ? 0 : SocialUtility.OpinionOf(pawn, other);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref familyId, "familyId", None);
            Scribe_Values.Look(ref spouseId, "spouseId", None);
            Scribe_Values.Look(ref parentIdA, "parentIdA", None);
            Scribe_Values.Look(ref parentIdB, "parentIdB", None);
            List<int>? children = childIds;
            Scribe_Collections.Look(ref children, "childIds", LookMode.Value);
            childIds = children ?? new List<int>();
            Scribe_Values.Look(ref generation, "generation");
            Scribe_Values.Look(ref bereavementTick, "bereavementTick", -1);
            Scribe_Values.Look(ref bereavementCause, "bereavementCause");
            List<DirectPawnRelation>? relations = directRelations;
            Scribe_Collections.Look(ref relations, "directRelations", LookMode.Deep);
            directRelations = relations ?? new List<DirectPawnRelation>();
        }
    }
}
