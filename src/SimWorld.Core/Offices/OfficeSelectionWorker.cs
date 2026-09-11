using System.Collections.Generic;

using SimWorld.Pawns;

namespace SimWorld.Offices
{
    /// <summary>
    /// What a seat is being filled for: the place it belongs to, in the terms the selection rules actually
    /// need. A settlement seat carries that settlement's name and founding tick; a civilization seat carries
    /// the faction's name and the earliest founding tick among its settlements, so "old enough to remember
    /// the founding" means the same thing at either scale.
    /// </summary>
    public readonly struct OfficeContext
    {
        public OfficeContext(string placeLabel, int foundingTick)
        {
            PlaceLabel = placeLabel;
            FoundingTick = foundingTick;
        }

        /// <summary>The settlement's name, or the civilization's. Used for the chronicle line, never for identity.</summary>
        public string PlaceLabel { get; }

        /// <summary>The tick this place came into being (<see cref="World.Settlement.foundingTick"/>, or the
        /// earliest one across a civilization's settlements).</summary>
        public int FoundingTick { get; }
    }

    /// <summary>
    /// How a seat is filled. Deliberately three separate questions rather than one "pick the best citizen",
    /// because they have very different costs and very different meanings:
    /// <list type="number">
    /// <item><description><see cref="IsEligible"/> — may this citizen hold the seat at all. Cheap, asked of
    /// everyone.</description></item>
    /// <item><description><see cref="CandidacyStrength"/> — who <i>stands</i>. Cheap, asked of everyone
    /// eligible; the strongest <see cref="OfficeDef.candidatePoolSize"/> claims become the candidates.</description></item>
    /// <item><description><see cref="Score"/> — who <i>wins</i> among those candidates. May be expensive
    /// (the esteem rule reads the whole electorate's opinion), which is exactly why the pool above bounds how
    /// often it is asked.</description></item>
    /// </list>
    ///
    /// <para/><b>Nothing here draws from <see cref="Sim.Rand"/>, not even the seeded stream.</b> Determinism
    /// in this codebase normally means "every draw goes through a seeded <c>RandomStream</c>"; an election is
    /// stronger than that — it is a pure function of state the simulation already holds (ages, ids, opinions),
    /// so two runs from the same save seat the same citizen without a stream to keep in step, and a save
    /// round-trip cannot re-roll a leader. Ties are broken by <see cref="Things.Thing.thingIDNumber"/>
    /// ascending, the same stable, seniority-shaped tie-break <see cref="God.AttentionBudget"/> already uses
    /// for the same reason.
    ///
    /// <para/><b>Elapsed time never selects anybody.</b> The rules below read age and seniority, which are
    /// facts about a person, but no seat is ever filled or emptied merely because time passed: a seat changes
    /// hands only when its holder dies, leaves the roster, or stops being eligible. <see cref="StillHolds"/>
    /// is what makes that an explicit rule rather than an accident — an incumbent is re-checked, never
    /// re-elected against.
    /// </summary>
    public class OfficeSelectionWorker
    {
        public OfficeDef def = null!;

        /// <summary>
        /// May this citizen hold the seat. The base rule is the one every office shares: a living, humanlike
        /// adult (<see cref="DevelopmentalStage.Adult"/>, read off the race's own life stages rather than an
        /// invented age constant). Overrides add to it; none may weaken it, since
        /// <see cref="OfficeManager"/> asks only this.
        /// </summary>
        public virtual bool IsEligible(Pawn pawn, OfficeContext context)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike) return false;
            return pawn.ageTracker?.CurLifeStage?.developmentalStage == DevelopmentalStage.Adult;
        }

        /// <summary>
        /// Whether a sitting holder keeps the seat. Defaults to <see cref="IsEligible"/> — tenure is for life,
        /// and the seat is not re-contested while it is occupied. That is the invariant that keeps this system
        /// thrash-free: were the winner recomputed every sweep, a settlement whose two most esteemed citizens
        /// traded places would trade the stewardship with them, and every handover costs a chronicle line, a
        /// work-priority rewrite and (through the tier) a promotion/demotion pair.
        /// </summary>
        public virtual bool StillHolds(Pawn pawn, OfficeContext context) => IsEligible(pawn, context);

        /// <summary>Who stands for an empty seat, higher first. The base rule is indifferent — every eligible
        /// citizen has an equal claim, so the pool is simply the most senior of them.</summary>
        public virtual float CandidacyStrength(Pawn pawn, OfficeContext context) => 0f;

        /// <summary>Who wins among the candidates, higher first; ties go to the more senior candidate. The
        /// base rule scores everyone alike, which makes the seat go to the strongest claim and, among equals,
        /// to the longest-established citizen.</summary>
        public virtual float Score(Pawn pawn, IReadOnlyList<Pawn> electorate, OfficeContext context) => 0f;
    }
}
