using System.Collections.Generic;
using SimWorld.Health;

namespace SimWorld.Defs
{
    /// <summary>
    /// What a Thing needs to be worn (RimWorld: <c>RimWorld.ApparelProperties</c>): which
    /// <see cref="BodyPartGroupDef"/>s it covers, which <see cref="Things.ApparelLayerDef"/>s it occupies, and
    /// the tags <see cref="Pawns.PawnKindDef.apparelTags"/> matches against — the same shape
    /// <see cref="ThingDef.weaponTags"/>/<see cref="Pawns.PawnKindDef.weaponTags"/> already use for weapons,
    /// mirrored here because RimWorld itself nests apparel's tags inside <c>ApparelProperties</c> rather than
    /// bare on the def the way it does for a weapon's.
    /// </summary>
    public class ApparelProperties
    {
        public List<BodyPartGroupDef>? bodyPartGroups;
        public List<Things.ApparelLayerDef>? layers;

        /// <summary>Tags <see cref="Pawns.PawnKindDef.apparelTags"/> matches against (RimWorld: <c>ApparelProperties.tags</c>).</summary>
        public List<string>? tags;

        public bool CoversBodyPartGroup(BodyPartGroupDef group) => bodyPartGroups != null && bodyPartGroups.Contains(group);

        /// <summary>Whether any covered group includes <paramref name="part"/> (RimWorld: <c>ApparelProperties.CoversBodyPart</c>, reached the same way <see cref="Combat.IArmorSource.Covers"/> is for armor).</summary>
        public bool CoversBodyPart(BodyPartRecord part)
        {
            if (bodyPartGroups == null || part == null) return false;
            for (int i = 0; i < bodyPartGroups.Count; i++)
            {
                if (part.IsInGroup(bodyPartGroups[i])) return true;
            }
            return false;
        }

        public bool HasLayer(Things.ApparelLayerDef layer) => layers != null && layers.Contains(layer);

        /// <summary>Two apparel pieces can be worn together unless they share both a layer and a body part
        /// group (RimWorld: <c>ApparelUtility.CanWearTogether</c>, minus its optional-layer overrides — no
        /// content here needs one).</summary>
        public bool ConflictsWith(ApparelProperties other)
        {
            if (other == null || layers == null || other.layers == null) return false;
            bool sharesLayer = false;
            for (int i = 0; i < layers.Count && !sharesLayer; i++)
            {
                sharesLayer = other.layers.Contains(layers[i]);
            }
            if (!sharesLayer) return false;

            if (bodyPartGroups == null || other.bodyPartGroups == null) return false;
            for (int i = 0; i < bodyPartGroups.Count; i++)
            {
                if (other.bodyPartGroups.Contains(bodyPartGroups[i])) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Things-layer half of apparel on <see cref="ThingDef"/>. Kept in its own partial file — the same split
    /// <c>ThingDef.Things.cs</c>/<c>ThingDef.Crafting.cs</c>/<c>ThingDef.Combat.cs</c> already use — so this
    /// pass adds a slice of ThingDef without touching a file another lane owns.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>Present only on a wearable ThingDef (RimWorld: <c>ThingDef.apparel</c>).</summary>
        public ApparelProperties? apparel;

        public bool IsApparel => apparel != null;
    }
}
