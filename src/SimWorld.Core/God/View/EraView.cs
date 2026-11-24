using SimWorld.Research;

namespace SimWorld.God.View
{
    /// <summary>
    /// The view's door onto <see cref="EraAvailability"/>: whether a thing, named by defName, belongs to the era
    /// the civilization is working through. The host drops what this says is out of era from the lists it draws
    /// (a stockpile filter, a bill's ingredients) without reaching into <c>SimWorld.Research</c> for it — every
    /// handle on this seam is a defName, so the host never holds a <c>ThingDef</c> to ask with.
    /// </summary>
    public static class EraView
    {
        /// <summary>True when <paramref name="defName"/>'s era is no later than the era being worked through,
        /// or when it names no loaded thing (this hides things, it does not vouch for them). Null or empty is
        /// shown too.</summary>
        public static bool IsAvailableInCurrentEra(string? defName) =>
            string.IsNullOrEmpty(defName) || EraAvailability.IsAvailableInCurrentEra(defName!);
    }
}
