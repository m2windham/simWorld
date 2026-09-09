using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Pawns.Generation
{
    /// <summary>
    /// Arms a generated pawn from its <see cref="PawnKindDef"/>'s weapon loadout (RimWorld:
    /// <c>RimWorld.PawnWeaponGenerator</c>). Candidates are every loaded weapon <see cref="ThingDef"/> whose
    /// <see cref="ThingDef.weaponTags"/> intersects <see cref="PawnKindDef.weaponTags"/> and whose
    /// <see cref="Crafting.ThingDef.BaseMarketValue"/> falls in <see cref="PawnKindDef.weaponMoneyRange"/>;
    /// when the pawn has a <see cref="Pawn.faction"/>, candidates are further capped to
    /// <c>ThingDef.techLevel &lt;= faction.def.techLevel</c> — RimWorld's own rule, and the mechanism that
    /// keeps a neolithic raiding faction from ever handing out an assault rifle. A kind with no
    /// <see cref="PawnKindDef.weaponTags"/> at all is never armed; a kind that can't afford anything in its
    /// budget (or whose tags match nothing at its faction's tech level) simply goes unarmed too, rather than
    /// erroring — matching RimWorld's own "no candidates, no weapon" behaviour.
    /// </summary>
    public static class PawnWeaponGenerator
    {
        public static void TryGenerateWeaponFor(Pawn pawn, PawnGenerationRequest request)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawn.RaceProps.Humanlike) return;

            PawnKindDef? kind = pawn.kindDef;
            if (kind?.weaponTags == null || kind.weaponTags.Count == 0) return;

            // No faction on the request: no tech ceiling (a directly-generated pawn isn't anyone's raider).
            TechLevel? maxTech = request.Faction?.def.techLevel;

            List<ThingDef> candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(td => td.IsWeapon
                    && td.weaponTags != null
                    && td.weaponTags.Any(kind.weaponTags.Contains)
                    && (maxTech == null || td.techLevel <= maxTech.Value)
                    && kind.weaponMoneyRange.Includes(td.BaseMarketValue))
                .ToList();
            if (candidates.Count == 0) return;

            if (!GenCollection.TryRandomElementByWeight((IReadOnlyList<ThingDef>)candidates, td => td.generateCommonality, Rand.Current, out ThingDef picked)) return;

            var weapon = (ThingWithComps)ThingMaker.MakeThing(picked);
            pawn.equipment.AddEquipment(weapon);
        }
    }
}
