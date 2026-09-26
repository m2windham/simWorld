using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Stats
{
    /// <summary>
    /// Multiplies a stat by another stat read off the pawn's own bed (RimWorld: <c>RimWorld.StatPart_BedStat</c>,
    /// 1.0 decompile — trimmed of its caravan-bed branch, which this port has no counterpart for). Content
    /// wires this onto <c>ImmunityGainSpeed</c> naming <c>BedRestEffectiveness</c>, so a pawn actually lying in
    /// a bed (<see cref="RestUtility.InBed"/>) has that bed's own <see cref="Health.HealthStatDefOf.BedRestEffectiveness"/>
    /// folded in on top of <see cref="StatPart_Resting"/>'s flat resting bonus — RimWorld's real "×107% for a
    /// regular bed" on top of "×110% for resting", not instead of it. A pawn not in any bed passes through
    /// at ×1: there is no "ground" stat value to look up, only the absence of this factor.
    /// </summary>
    public class StatPart_BedStat : StatPart
    {
        public StatDef? stat;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (stat == null || !(req.Thing is Pawn pawn)) return;
            Thing? bed = pawn.CurrentBed();
            if (bed == null) return;
            val *= bed.GetStatValue(stat);
        }
    }
}
