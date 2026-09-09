using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>
    /// Combat's slice of <see cref="ThingDef"/>: what a weapon is, for <see cref="Pawns.Generation.PawnWeaponGenerator"/>
    /// to pick from (RimWorld: <c>ThingDef.techLevel</c>/<c>weaponTags</c>/<c>generateCommonality</c>). Kept in
    /// its own partial file so parallel modules each add their slice without touching one another's files —
    /// see <c>Defs/ThingDef.cs</c> for the shared <c>partial</c> declaration every such file depends on.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>
        /// Technology a faction must have reached to be issued this weapon (RimWorld: <c>ThingDef.techLevel</c>).
        /// <see cref="TechLevel.Undefined"/> (the default) means no gate — every faction may use it.
        /// <see cref="Pawns.Generation.PawnWeaponGenerator"/> reads this against the generated pawn's own
        /// <see cref="Pawns.Pawn.faction"/>, never against player research (<c>ThingDef.IsResearchFinished</c>
        /// is deliberately not consulted here either — see that property's own doc comment).
        /// </summary>
        public TechLevel techLevel = TechLevel.Undefined;

        /// <summary>Tags <see cref="Pawns.PawnKindDef.weaponTags"/> matches against to decide which weapons a
        /// pawn kind may be issued (RimWorld: <c>ThingDef.weaponTags</c>). Null/empty means this def is never
        /// picked by <see cref="Pawns.Generation.PawnWeaponGenerator"/>.</summary>
        public List<string>? weaponTags;

        /// <summary>Relative pick weight among weapons that already passed the tag/tech/price filters
        /// (RimWorld: <c>ThingDef.generateCommonality</c>).</summary>
        public float generateCommonality = 1f;

        /// <summary>Has an attack of some kind — a ranged verb or a melee tool — so it is eligible to be
        /// issued as a weapon at all.</summary>
        public bool IsWeapon => category == ThingCategory.Item && ((verbs != null && verbs.Count > 0) || (tools != null && tools.Count > 0));
    }
}
