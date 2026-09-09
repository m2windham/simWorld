using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A named recipe for generating pawns of one race (RimWorld: <c>RimWorld.PawnKindDef</c>): which backstory
    /// pools to draw from, age/trait overrides, and a combat-power number other systems can use to balance
    /// encounters. <see cref="Generation.PawnGenerator"/> consumes this.
    /// </summary>
    public class PawnKindDef : Def
    {
        public ThingDef? race;

        /// <summary>Backstory <see cref="BackstoryDef.spawnCategories"/> pools this kind draws childhood/adulthood from.</summary>
        public List<string>? backstoryCategories;

        /// <summary>-1 (the default) means "use the race's own <see cref="RaceProperties.ageGenerationCurve"/> unclamped".</summary>
        public float minGenerationAge = -1f;
        public float maxGenerationAge = -1f;

        public float combatPower;
        public bool isFighter;

        public List<BackstoryTrait>? forcedTraits;
        public List<TraitDef>? disallowedTraits;

        // ---- gear (RimWorld: PawnKindDef's weapon/apparel loadout fields) ----

        /// <summary>Weapon tags <see cref="Generation.PawnWeaponGenerator"/> matches against
        /// <see cref="ThingDef.weaponTags"/> to pick this kind's weapon (RimWorld: <c>PawnKindDef.weaponTags</c>).
        /// Null/empty means this kind is never armed.</summary>
        public List<string>? weaponTags;

        /// <summary>Market-value band a generated weapon's price must fall in (RimWorld: <c>PawnKindDef.weaponMoneyRange</c>).</summary>
        public FloatRange weaponMoneyRange = FloatRange.Zero;

        /// <summary>
        /// Apparel tags an eventual apparel generator would match against a matching <c>ThingDef.apparelTags</c>
        /// (RimWorld: <c>PawnKindDef.apparelTags</c>). <b>Not yet consumed:</b> no apparel content and no
        /// pawn wear-tracking exist in SimWorld yet (no <c>ThingDef.apparel</c>, no <c>Pawn_ApparelTracker</c>),
        /// so there is nothing for a <c>PawnApparelGenerator</c> to spend this against — see
        /// <c>docs/status.json</c>'s <c>pawngen.gear</c> entry for exactly what's missing. Ported now, alongside
        /// <see cref="weaponTags"/>, so content authored against this field's shape does not need revisiting
        /// once that half lands.
        /// </summary>
        public List<string>? apparelTags;

        /// <summary>Market-value band an eventual apparel generator's picks must fall in (RimWorld: <c>PawnKindDef.apparelMoneyRange</c>). Not yet consumed — see <see cref="apparelTags"/>.</summary>
        public FloatRange apparelMoneyRange = FloatRange.Zero;

        public RaceProperties RaceProps => race!.race!;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (race == null) yield return "kind has no race.";
            else if (race.race == null) yield return "kind's race ThingDef '" + race.defName + "' has no RaceProperties.";
            if (weaponMoneyRange.min > weaponMoneyRange.max) yield return "weaponMoneyRange min must not exceed max.";
            if (apparelMoneyRange.min > apparelMoneyRange.max) yield return "apparelMoneyRange min must not exceed max.";
        }
    }
}
