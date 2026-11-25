using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// What a citizen expects of life, read from the age the civilization lives in (RimWorld:
    /// <c>ThoughtWorker_Expectations</c> over <c>ExpectationsUtility.CurrentExpectationFor</c>).
    ///
    /// <para/><b>The idea.</b> RimWorld gives every colonist a standing mood bonus that shrinks as the colony
    /// grows richer: a new camp of tents is easy to please, a wealthy colony is not. It is the largest single
    /// thought in an early colony and the reason a band with no beds, raw food and bare ground is not always near
    /// breaking. This port had no such thought, and the third playtest's town sat at 40-55% mood for three
    /// years with break alerts standing, five or ten points above the minor break line.
    ///
    /// <para/><b>Shaped by the arc.</b> RimWorld steps it by the home map's wealth. Here the town climbs through
    /// eras, and the era is the standard of living its people measure against: a band in Sticks and stones
    /// expects nothing, a Medieval town expects a good deal. So the stage is the current era's
    /// <see cref="EraDef.order"/>, one step per era from extremely low, held at the last stage past it. The
    /// mood values are RimWorld's six expectation levels (+30, +24, +18, +12, +6, 0; rimworldwiki.com, read for
    /// this lane, not checked against a decompile); which era takes which level is this port's choice.
    ///
    /// <para/>A citizen only: a raider, a trader or a captive has no stake in the town's age. Off outside a
    /// running game, as RimWorld's is off unless the program is playing: a bare test pose has no town.
    /// </summary>
    public sealed class ThoughtWorker_Expectations : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (!PawnUtility.IsColonist(pawn)) return ThoughtState.Inactive;
            if (Find.CurrentGame == null) return ThoughtState.Inactive;
            EraDef? era = Find.ResearchManager.CurrentEra;
            if (era == null || def.stages.Count == 0) return ThoughtState.Inactive;
            int stage = era.order < 0 ? 0 : era.order >= def.stages.Count ? def.stages.Count - 1 : era.order;
            return ThoughtState.ActiveAtStage(stage);
        }
    }
}
