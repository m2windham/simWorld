using System;

namespace SimWorld.Work
{
    /// <summary>
    /// Broad categories of work a pawn can be barred from (RimWorld: <c>WorkTags</c>). Traits, and later
    /// backstories/genes/health, OR these together into a combined mask that disables matching skills and
    /// work types regardless of what the colonist's priority grid says.
    /// </summary>
    [Flags]
    public enum WorkTags
    {
        None = 0,
        ManualDumb = 1,
        ManualSkilled = 2,
        Violent = 4,
        Caring = 8,
        Social = 16,
        Commoner = 32,
        Intellectual = 64,
        Animals = 128,
        Artistic = 256,
        Crafting = 512,
        Cooking = 1024,
        Firefighting = 2048,
        Cleaning = 4096,
        Hauling = 8192,
        PlantWork = 16384,
        Mining = 32768,
        Hunting = 65536,
        Constructing = 131072,
        Shooting = 262144,

        /// <summary>Every work type at once, regardless of its own tags.</summary>
        AllWork = 524288,
    }
}
