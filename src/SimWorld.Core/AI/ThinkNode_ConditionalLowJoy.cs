using SimWorld.Needs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// True while the pawn's recreation need has fallen to <see cref="JoyCategory.Low"/> or worse — the gate
    /// on the humanlike think tree's recreation tier, in the same shape
    /// <see cref="ThinkNode_ConditionalHungry"/> and <see cref="ThinkNode_ConditionalTired"/> already give
    /// food and rest.
    ///
    /// <para/><b>A translation, and a named one.</b> RimWorld does not gate its recreation tier on the need
    /// at all: it gates on the pawn's <i>timetable</i> (<c>JoyUtility.TimetablePreventsGettingJoy</c>), a
    /// per-hour Work/Recreation/Sleep/Anything assignment the player sets, and a colonist in a Recreation
    /// block takes a break whether or not the need is low. This port has no timetable module, so there is
    /// nothing to read; gating on the need instead is the same answer the two need tiers above already
    /// settled on, and it keeps a citizen working rather than idling the moment recreation is not urgent.
    /// The threshold is <see cref="Need_Joy"/>'s own <see cref="Need_Joy.ThreshLow"/> rather than a new
    /// number, so the tier turns on exactly where the need's own bands say the pawn is short of recreation.
    /// </summary>
    public sealed class ThinkNode_ConditionalLowJoy : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            Need_Joy? joy = pawn.needs.joy;
            return joy != null && joy.CurLevel < Need_Joy.ThreshLow;
        }
    }
}
