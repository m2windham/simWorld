using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Offices
{
    /// <summary>
    /// The mood half of holding a station (<see cref="OfficeDef.holderThought"/>): active for a citizen
    /// exactly while they hold an office that names this <see cref="ThoughtDef"/>. Scans
    /// <see cref="OfficeDef"/> rather than storing a back-reference, the same direction
    /// <see cref="God.ThoughtWorker_UnderEdict"/> reads <c>EdictDef.moodThought</c> — the def that adds the
    /// consequence is the one that names it.
    /// <para/>
    /// Because <see cref="SituationalThoughtHandler"/> recomputes on its own cadence, the thought appears the
    /// instant <see cref="OfficeManager"/> seats someone and disappears the instant it unseats them, with no
    /// grant or revoke call and nothing left on a citizen who is no longer the steward. That is also what
    /// carries an office into every aggregate that already reads mood — birth chance, migration's settlement
    /// quality, the god view's rollup — without any of them learning what an office is.
    /// </summary>
    public sealed class ThoughtWorker_HoldsOffice : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            OfficeDef? office = OfficeManager.OfficeOf(pawn);
            if (office == null || office.holderThought != def) return ThoughtState.Inactive;
            return ThoughtState.ActiveDefault;
        }
    }
}
