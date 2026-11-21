using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The player-facing label and description of any defName the seam hands out.
    ///
    /// <para/>Every handle on this seam is a defName so the host can never hold a Def, and that same rule left
    /// the host with no way to name a thing to the player: it showed <c>CookMealSimple</c>, <c>FueledStove</c>
    /// and <c>Gun_Revolver</c> on screen. Walking the <see cref="DefDatabase"/> itself is exactly what the host
    /// may not do, so this does it on the core's side and hands back strings — content's own
    /// <see cref="Def.LabelCap"/> and <see cref="Def.description"/>, so a label changes in one place, the XML.
    ///
    /// <para/><b>Which def, when a name is shared.</b> A defName is unique within its Def type, not across
    /// types. Things are what the seam names most, so <see cref="ThingDef"/> is looked up first, then
    /// <see cref="TerrainDef"/>, <see cref="RecipeDef"/> and <see cref="JobDef"/>, then any other Def type.
    ///
    /// <para/>Content-only and static, like <c>God.View.StorytellerCatalogue</c>: no game is needed.
    /// </summary>
    public static class DefLabels
    {
        /// <summary>The capitalised label for <paramref name="defName"/>, or <paramref name="defName"/> itself
        /// when no loaded def carries that name — the same fallback <see cref="Def.LabelCap"/> applies to a def
        /// with no label, so a host can call this blindly. Null or empty comes back empty.</summary>
        public static string LabelOf(string? defName)
        {
            if (string.IsNullOrEmpty(defName)) return string.Empty;
            return Resolve(defName!)?.LabelCap ?? defName!;
        }

        /// <summary>Content's description for <paramref name="defName"/>, or null when there is none or no such
        /// def is loaded.</summary>
        public static string? DescriptionOf(string? defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            string? description = Resolve(defName!)?.description;
            return string.IsNullOrEmpty(description) ? null : description;
        }

        /// <summary>Whether any loaded def carries <paramref name="defName"/>.</summary>
        public static bool IsKnown(string? defName) => !string.IsNullOrEmpty(defName) && Resolve(defName!) != null;

        internal static DefOption Option(Def def) => new DefOption(def.defName, def.LabelCap);

        private static Def? Resolve(string defName)
        {
            DefDatabase db = DefDatabase.Global;
            return (Def?)db.GetNamedSilentFail<ThingDef>(defName)
                ?? (Def?)db.GetNamedSilentFail<TerrainDef>(defName)
                ?? (Def?)db.GetNamedSilentFail<RecipeDef>(defName)
                ?? (Def?)db.GetNamedSilentFail<JobDef>(defName)
                ?? db.GetNamedSilentFail<Def>(defName);
        }
    }
}
