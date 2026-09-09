using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Pawns.Generation
{
    /// <summary>
    /// Dresses a generated pawn from its <see cref="PawnKindDef"/>'s apparel loadout (RimWorld:
    /// <c>RimWorld.PawnApparelGenerator</c>), consuming <see cref="PawnKindDef.apparelTags"/>/
    /// <see cref="PawnKindDef.apparelMoneyRange"/> the same way <see cref="PawnWeaponGenerator"/> consumes the
    /// weapon half — see that class's own remarks for the shared shape (tag match, tech ceiling from the
    /// pawn's faction, money-range gate, weighted pick). A kind with no <see cref="PawnKindDef.apparelTags"/>
    /// at all is never dressed, matching <see cref="PawnWeaponGenerator"/>'s "no tags, no gear" rule.
    ///
    /// <b>Deviation:</b> RimWorld's real generator spends one shared money budget across layers and can force
    /// specific "required" apparel by tag. This instead offers every eligible candidate exactly once, in a
    /// randomized weighted order, keeping whichever don't conflict with what's already worn
    /// (<see cref="Pawn_ApparelTracker.WouldConflictWithWorn"/>) — simpler than layer-by-layer, and it gets the
    /// same practical outcome for non-conflicting pieces (a shirt and pants both fit; two Shell-layer pieces
    /// over the same torso do not, and only one wins). Each candidate is still gated on
    /// <see cref="PawnKindDef.apparelMoneyRange"/> individually, the same rule <see cref="PawnWeaponGenerator"/>
    /// applies to its one weapon. No candidates fitting at all leaves the pawn undressed, rather than erroring.
    /// </summary>
    public static class PawnApparelGenerator
    {
        public static void TryGenerateApparelFor(Pawn pawn, PawnGenerationRequest request)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawn.RaceProps.Humanlike) return;

            PawnKindDef? kind = pawn.kindDef;
            if (kind?.apparelTags == null || kind.apparelTags.Count == 0) return;

            // No faction on the request: no tech ceiling, mirroring PawnWeaponGenerator.
            TechLevel? maxTech = request.Faction?.def.techLevel;

            List<ThingDef> pool = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(td => td.IsApparel
                    && td.apparel!.tags != null
                    && td.apparel.tags.Any(kind.apparelTags.Contains)
                    && (maxTech == null || td.techLevel <= maxTech.Value)
                    && kind.apparelMoneyRange.Includes(td.BaseMarketValue))
                .ToList();

            while (pool.Count > 0)
            {
                if (!GenCollection.TryRandomElementByWeight((IReadOnlyList<ThingDef>)pool, td => td.generateCommonality, Rand.Current, out ThingDef picked))
                {
                    break;
                }
                pool.Remove(picked);
                if (pawn.apparel.WouldConflictWithWorn(picked)) continue;

                var apparel = (ThingWithComps)ThingMaker.MakeThing(picked);
                pawn.apparel.Wear(apparel);
            }
        }
    }
}
