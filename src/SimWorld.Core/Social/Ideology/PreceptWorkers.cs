using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Social.Ideology
{
    // Concrete PreceptWorkers this pass ships — each one a pure function of a citizen's own already-tracked
    // state, deliberately: nothing here needs a new hook into another lane's module (no "pawn ate human meat"
    // event stream, no room/map concept), only trackers this port already exposes (Pawn.story's traits,
    // Pawn.apparel's worn list). One worker drives any number of content-only PreceptDefs with opposing mood
    // signs — the divergent judgment ("nudity is comfortable" vs. "nudity is immodest") lives entirely in each
    // precept's own PreceptDef.moodThought stages, not in a second worker class, the same shape
    // Thoughts.ThoughtWorker_Hediff already uses for any number of hediffs.

    /// <summary>Active while the pawn holds <see cref="PreceptDef.trait"/> — the mechanism behind this pass's
    /// "Trait" issue (RimWorld: precepts approving/disapproving specific personality traits, e.g. Bloodlust).</summary>
    public class PreceptWorker_Trait : PreceptWorker
    {
        public override ThoughtState CurrentState(Pawn pawn)
        {
            if (def.trait == null) return ThoughtState.Inactive;
            return pawn.story.traits.HasTrait(def.trait) ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
        }
    }

    /// <summary>Active while the pawn is wearing no apparel at all (RimWorld's "Apparel"/"Nudity" issue).
    /// Reused for both an ideoligion that approves nudity and one that requires modesty — see this class's
    /// container doc for why the same condition serves both.</summary>
    public class PreceptWorker_Unclothed : PreceptWorker
    {
        public override ThoughtState CurrentState(Pawn pawn) =>
            pawn.apparel.WornApparel.Count == 0 ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
    }

    /// <summary>Active while the pawn currently holds this precept's own <see cref="PreceptDef.grantsRole"/>
    /// in the active <see cref="Ideo"/> — the role-holder's "fulfilling my calling" thought. Reads
    /// <see cref="IdeoManager.Current"/> directly rather than through a parameter: by the time
    /// <see cref="ThoughtWorker_UnderPrecept"/> reaches this worker it has already resolved the same ambient
    /// Ideo to find this precept in the first place, so there is nothing this worker could be asked about
    /// against a different one.</summary>
    public class PreceptWorker_RoleHolder : PreceptWorker
    {
        public override ThoughtState CurrentState(Pawn pawn)
        {
            if (def.grantsRole == null) return ThoughtState.Inactive;
            Ideo? ideo = IdeoManager.Current;
            if (ideo == null) return ThoughtState.Inactive;
            return ideo.Roles.RoleOf(pawn) == def.grantsRole ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
        }
    }
}
