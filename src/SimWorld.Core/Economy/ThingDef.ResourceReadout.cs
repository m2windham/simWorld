using SimWorld.Economy;

namespace SimWorld.Economy
{
    /// <summary>
    /// Whether a kind of item is one a settlement counts as a resource, and how near the top of the count it
    /// reads (RimWorld: <c>RimWorld.ResourceCountPriority</c>). Declared lowest first, so the default —
    /// <see cref="Uncounted"/> — is what a def that says nothing gets: a weapon, a coat, a rock chunk.
    ///
    /// <para/><b>Counted or not is the part that matters; the rest is recalled.</b> Which shipped defs carry
    /// which of <see cref="Last"/>, <see cref="Middle"/> and <see cref="First"/> was recalled from RimWorld
    /// and not checked against a decompile, and several shipped defs (coal, flint, salt, tin, copper) have no
    /// RimWorld counterpart to check against at all. Nothing in the simulation reads the difference between
    /// the three: it only orders a readout. <c>GodViewSeamFieldsTests</c> pins the counted set and the
    /// ordering rule, never one def's priority.
    /// </summary>
    public enum ResourceCountPriority
    {
        Uncounted,
        Last,
        Middle,
        First,
    }
}

namespace SimWorld.Defs
{
    /// <summary>
    /// The resource readout's addition to ThingDef (RimWorld: <c>ThingDef.resourceReadoutPriority</c> and
    /// <c>ThingDef.CountAsResource</c>, which <c>RimWorld.ResourceCounter</c> filters by). Its own partial
    /// file for the reason every other slice of ThingDef has one — see <c>Defs/ThingDef.cs</c>.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>Where this def reads in a count of what a settlement holds, or
        /// <see cref="ResourceCountPriority.Uncounted"/> — the default — to be left out of it.</summary>
        public ResourceCountPriority resourceReadoutPriority;

        /// <summary>Whether a settlement counts this as a resource at all. False for gear and for rubble:
        /// things a settlement has lying about rather than things it stores against need.</summary>
        public bool CountAsResource => resourceReadoutPriority != ResourceCountPriority.Uncounted;
    }
}
