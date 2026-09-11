using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Needs
{
    /// <summary>
    /// The mood cost (or bonus) of what the citizen is looking at (RimWorld: <c>RimWorld.ThoughtWorker_Beauty</c>,
    /// and the <c>Beauty</c> ThoughtDef it drives). One band of <see cref="Need_Beauty.CurCategory"/> per
    /// stage, the same shape <see cref="Thoughts.ThoughtWorker_NeedJoy"/> already uses for recreation —
    /// negative stages for ugliness, nothing at all when the surroundings are unremarkable, positive stages
    /// for a room somebody has made beautiful.
    /// <para/>
    /// <b>This is the consumer that makes the beauty need matter</b>, and it is deliberately the only one:
    /// everything that wants to hurt or help mood through the look of a place — filth first among them —
    /// does it by carrying a <see cref="BeautyStatDefOf.Beauty"/> value and letting the sampler find it,
    /// exactly as RimWorld does, instead of each source growing a situational thought of its own. It reads
    /// the need's level and nothing else, so it costs O(1) on the 10-tick situational recalculation; all the
    /// real work happens once per 150 ticks in <see cref="MapEnvironmentSampler"/>.
    /// <para/>
    /// A citizen with no beauty need — an animal, anyone whose species is below <c>Humanlike</c> — has no
    /// opinion, which falls out of the need simply not being there.
    /// </summary>
    public class ThoughtWorker_NeedBeauty : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            Need_Beauty? beauty = pawn?.needs?.TryGetNeed<Need_Beauty>();
            if (beauty == null) return ThoughtState.Inactive;
            switch (beauty.CurCategory)
            {
                case BeautyCategory.Hideous: return ThoughtState.ActiveAtStage(0);
                case BeautyCategory.VeryUgly: return ThoughtState.ActiveAtStage(1);
                case BeautyCategory.Ugly: return ThoughtState.ActiveAtStage(2);
                case BeautyCategory.Pretty: return ThoughtState.ActiveAtStage(3);
                case BeautyCategory.VeryPretty: return ThoughtState.ActiveAtStage(4);
                case BeautyCategory.Beautiful: return ThoughtState.ActiveAtStage(5);
                default: return ThoughtState.Inactive;
            }
        }
    }
}
