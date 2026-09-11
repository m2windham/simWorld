using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Offices
{
    /// <summary>
    /// <b>Candidacy by seniority of years, selection by esteem.</b> The settlement's adults who have lived
    /// longest stand for the seat (<see cref="OfficeDef.candidatePoolSize"/> of them), and the one the
    /// settlement thinks best of takes it: the sum of every elector's
    /// <see cref="Pawn_RelationsTracker.OpinionOf"/> for that candidate.
    ///
    /// <para/><b>Why this is an election and not a dice roll.</b> Opinion is already real, already simulated
    /// state — it comes out of the social module's relations, traits, social memories and RimWorld's own
    /// per-pair compatibility hash (<see cref="Social.SocialUtility.OpinionOf"/>, which is documented as pure
    /// and side-effect free), so a citizen who has been insulting people for thirty years genuinely loses,
    /// and the result is reproducible without touching <see cref="Rand"/> at all. It also means the seat
    /// responds to the simulation rather than decorating it: an unpopular steward is one the settlement's own
    /// interactions made unpopular.
    ///
    /// <para/><b>The electorate is bounded, and that is a translation, not an optimisation.</b>
    /// <see cref="World.Settlement.Citizens"/> can hold tens of thousands of live <c>Pawn</c>s once a
    /// civilization has run for a century (the Full-tier budget bounds who is <i>ticked</i>, not who exists),
    /// and RimWorld's opinion mechanic is a colony-scale one: it models people who know each other. Summing
    /// forty thousand opinions of one candidate would be both meaningless and expensive, so the electorate is
    /// the <see cref="OfficeTuning.ElectorateCap"/> longest-established living citizens — the people who have
    /// been there long enough to have an opinion worth counting. Bounded by seniority rather than by an
    /// arbitrary slice so it is deterministic and so it does not churn as the town grows.
    /// </summary>
    public class OfficeSelectionWorker_Esteem : OfficeSelectionWorker
    {
        /// <summary>The eldest stand. Not "the most skilled" and not "the most related": the seat is the
        /// settlement's own, and a claim on it that the settlement can see for itself — how long someone has
        /// been alive among them — is the one this port can defend without inventing a merit metric.</summary>
        public override float CandidacyStrength(Pawn pawn, OfficeContext context) =>
            pawn.ageTracker?.AgeBiologicalYearsFloat ?? 0f;

        public override float Score(Pawn pawn, IReadOnlyList<Pawn> electorate, OfficeContext context)
        {
            float esteem = 0f;
            for (int i = 0; i < electorate.Count; i++)
            {
                Pawn elector = electorate[i];
                if (elector == pawn || elector.Dead || !elector.RaceProps.Humanlike) continue;
                esteem += elector.relations.OpinionOf(pawn);
            }
            return esteem;
        }
    }

    /// <summary>
    /// <b>The civilization's eldest: someone old enough to remember its founding.</b> Eligible only while
    /// their own biological age covers the whole span since the place was founded
    /// (<see cref="OfficeContext.FoundingTick"/>) — both sides of that comparison advance one tick per tick,
    /// so it is an invariant of a given citizen rather than something they age into or out of, and
    /// <c>Pawn_AgeTracker.AgeTickMothballed</c> keeps it exact for citizens at Interval and Statistical tier
    /// as well.
    ///
    /// <para/><b>This is how the seat retires itself, with no "retired" flag to save.</b> A settlement's
    /// stewardship is a job and is refilled forever; the eldership is a fact about history and cannot be. Once
    /// the last citizen who was alive at the founding has died, no one is eligible, the seat simply stays
    /// empty, and it can never be filled again — which is the right answer for the office and also the reason
    /// this whole module needs no persistent state (see <see cref="OfficeDef"/>). The honest limitation is
    /// named rather than hidden: this port records no arrival tick, so a migrant who joined later and is old
    /// enough qualifies too. The office is therefore "old enough to remember the founding", which is exactly
    /// what the data supports — not "was demonstrably there".
    /// </summary>
    public class OfficeSelectionWorker_Eldest : OfficeSelectionWorker
    {
        public override bool IsEligible(Pawn pawn, OfficeContext context)
        {
            if (!base.IsEligible(pawn, context)) return false;
            long sinceFounding = Find.TickManager.TicksGame - (long)context.FoundingTick;
            return pawn.ageTracker.ageBiologicalTicks >= sinceFounding;
        }

        /// <summary>Earliest-established first. Negated so that "higher is a stronger claim" holds for every
        /// worker, and so the winner is the same citizen the <see cref="Things.Thing.thingIDNumber"/>
        /// tie-break would have picked anyway — the eldership is seniority all the way down, with nothing left
        /// for <see cref="Score"/> to decide.</summary>
        public override float CandidacyStrength(Pawn pawn, OfficeContext context) => -pawn.thingIDNumber;
    }
}
