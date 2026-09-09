using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// The situational half of a precept's mood consequence (<see cref="PreceptDef.moodThought"/>) — this
    /// module's own precept/thought core loop, built on exactly the precedent the brief names:
    /// <see cref="God.ThoughtWorker_UnderEdict"/>. Active for a pawn exactly while some precept in the active
    /// <see cref="Ideo"/> (<see cref="IdeoManager.Current"/>) names this <see cref="ThoughtDef"/> as its
    /// <c>moodThought</c>, that precept's <see cref="PreceptWorker.AppliesTo"/> applies to the pawn, and its
    /// <see cref="PreceptWorker.CurrentState"/> says so. Scans <see cref="PreceptDef"/> rather than storing a
    /// back-reference, the same direction <see cref="God.ThoughtWorker_UnderEdict"/> scans
    /// <see cref="God.EdictDef"/> — the def that adds the consequence is the one that names it.
    /// <para/>
    /// Because <see cref="SituationalThoughtHandler"/> already recomputes this on its own cadence, a precept's
    /// mood effect appears and disappears the instant the underlying condition does (a citizen dons or removes
    /// apparel, gains or loses a role) — no explicit per-pawn grant or removal call needed.
    /// </summary>
    public sealed class ThoughtWorker_UnderPrecept : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            Ideo? ideo = IdeoManager.Current;
            if (ideo == null) return ThoughtState.Inactive;

            var precepts = ideo.Precepts;
            for (int i = 0; i < precepts.Count; i++)
            {
                PreceptDef precept = precepts[i];
                if (precept.moodThought != def) continue;
                if (!precept.Worker.AppliesTo(pawn)) continue;
                return precept.Worker.CurrentState(pawn);
            }
            return ThoughtState.Inactive;
        }
    }
}
