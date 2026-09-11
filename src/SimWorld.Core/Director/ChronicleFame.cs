using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// <b>What earns a living citizen individual distinction</b> — the policy behind
    /// <see cref="Pawn_TierTracker.Notify_ChronicleNamed"/>, which that method's own doc deliberately left
    /// open for a director to supply. Answer: <b>outliving everyone the civilization has ever known.</b>
    ///
    /// <para/><b>Why this needed a policy at all, and why the obvious one is wrong.</b>
    /// <see cref="Storyteller.RecordChronicle(string)"/> and <see cref="Storyteller.RecordDeath"/> are called
    /// for every birth and every death unconditionally (<see cref="FamilyManager"/>), so "has a chronicle
    /// entry" would make every citizen significant from the moment they were born and the tier would never
    /// demote anyone — the tier tracker says exactly this. The chronicle is the civilization's news. What is
    /// wanted is its landmarks, which is what <see cref="MomentCurator"/> already curates: a first, a
    /// turning point, a record. This reads from there.
    ///
    /// <para/><b>The rule.</b> Once per demographic sweep, the oldest living citizen is measured against the
    /// longest life this civilization has ever known — every life that ended
    /// (<see cref="MomentCurator.ConsiderDeathRecord"/>) and every life already recognised here, one ratchet
    /// (<see cref="MomentCurator.LongestLifeYearsKnown"/>). When they pass it, the narrator writes a moment
    /// and they are named. Nobody else is named by anything in this class.
    ///
    /// <para/><b>Why this is rare by construction rather than by a threshold.</b> Three things, and none of
    /// them is a tuned number:
    /// <list type="number">
    /// <item><b>It is a ratchet.</b> Each new name must beat every life before it, so it fires less and less
    /// often the longer a game runs — the same shape <see cref="MomentCurator"/>'s death-record rule already
    /// has, and the reason a routine century there produces a handful of moments rather than hundreds.</item>
    /// <item><b>At most one living citizen holds it at a time.</b> While the record-holder lives they are the
    /// oldest, so the ratchet rises with them every sweep (<see cref="MomentCurator.AdvanceLifeRecord"/>) and
    /// nobody can take it from them; a successor can only appear after the incumbent is dead. That is not a
    /// cap imposed on the rule, it is what the rule means, and it makes the cost to
    /// <see cref="God.AttentionBudget"/>'s fixed 500-seat Full-tier budget exactly one seat.</item>
    /// <item><b>It cannot be earned by arriving, working or being looked at</b> — only by a whole life. There
    /// is no rate at which citizens can be produced that produces named citizens faster.</item>
    /// </list>
    /// That third point is the one that matters, and it is the lesson of
    /// <see cref="MigrationManager.ProcessArrivals"/>: that method used to mark every arriving migrant a
    /// role-holder, a flag which is also never cleared, at roughly 25 arrivals per settlement per century —
    /// which would have filled the budget with people whose only distinction was having turned up. A policy
    /// that fires per event scales with the population; one that fires per record does not.
    ///
    /// <para/><b>Why the record, of everything the curator knows.</b> The <see cref="MomentCurator"/>'s other
    /// two rules are firsts — the first raid, the first birth, the first trader — and a first can happen only
    /// once per category, so a policy built on them would name four or five people in a civilization's
    /// opening years and then nothing, ever. A record can always be broken, so this keeps producing figures
    /// for as long as there is a civilization to produce them, at a rate that falls rather than grows. And a
    /// long life is a thing this simulation actually models rather than a label: lifespan here is a hidden
    /// budget that nutrition, injury, disease and medicine all spend or extend
    /// (<see cref="Pawn_AgeTracker.AdjustLifespan"/>, whose reasons the chronicle already prints at a death),
    /// so the oldest citizen is the one the civilization's history has the most to say about. That is what
    /// "individual distinction" ought to mean here.
    ///
    /// <para/><b>What was considered and rejected.</b>
    /// <list type="bullet">
    /// <item><i>Anyone named in a Moment.</i> Most moments have no person in them at all (an era crossing, a
    /// raid, a founding, an edict), and the ones that do are firsts — see above. It would also have meant
    /// threading a subject pawn through every chronicle call site.</item>
    /// <item><i>Anyone present at a Moment.</i> Unbounded in exactly the way the budget cannot survive: an
    /// era crossing would name the entire roster at once.</item>
    /// <item><i>Holding office, surviving a raid, founding a household.</i> The first is already
    /// <see cref="Pawn_TierTracker.Notify_RoleChanged"/>'s job and would be a second flag for one fact; the
    /// other two are per-event and scale with the population, which is the migration mistake again.</item>
    /// <item><i>Naming the dead only.</i> That is the policy this codebase already had
    /// (<see cref="Storyteller.RecordDeath"/> names a pawn whose death the curator flagged), and
    /// <c>docs/WORK-REGISTER.md</c> is blunt about what is wrong with it: "a policy for the dead and none at
    /// all for the living". A tier that decides how much simulation a citizen gets has nothing to say about
    /// someone who has stopped being simulated.</item>
    /// </list>
    ///
    /// <para/><b>One-directional, and budgeted for.</b> A chronicle entry is never unwritten, so whoever this
    /// names is <see cref="PawnTier.Full"/> for the rest of their life and
    /// <see cref="God.AttentionBudget"/> ranks them second of four. Bullet 2 above is what makes that
    /// affordable: the standing cost is one seat, and the number of named citizens who have ever lived grows
    /// like the number of records, not like the population.
    /// </summary>
    public static class ChronicleFame
    {
        /// <summary>
        /// Considers <paramref name="citizens"/> for the one distinction this class grants, and returns the
        /// citizen newly named, or null — which is the answer almost every time it is called. Wired into
        /// <see cref="FamilyManager.DemographyTick"/>, so it runs at the demographic cadence (once a year)
        /// against whatever population is being swept, and reads a civilization-wide record: two settlements
        /// sweeping their own rosters share one doyen between them rather than having one each.
        /// <para/>
        /// Called after the sweep's deaths, so it can only ever name someone who is alive at the end of it —
        /// a citizen who dies this very year is the death path's business, not this one's.
        /// </summary>
        public static Pawn? ConsiderDoyen(IReadOnlyList<Pawn> citizens)
        {
            if (citizens == null) throw new ArgumentNullException(nameof(citizens));

            Pawn? doyen = OldestLiving(citizens);
            if (doyen == null) return null;

            MomentCurator curator = Find.Storyteller.moments;
            float ageYears = doyen.ageTracker.AgeBiologicalYearsFloat;
            if (ageYears <= curator.LongestLifeYearsKnown) return null;

            // The reigning holder simply going on living. Every year of theirs is a new longest life and the
            // ratchet has to follow it — otherwise the year after they were named they would pass their own
            // record again and be announced annually — but a civilization does not remark on it twice.
            if (doyen.tier.ChronicleNamed)
            {
                curator.AdvanceLifeRecord(ageYears);
                return null;
            }

            ChronicleEntry entry = Find.Storyteller.RecordChronicle(ChronicleLine(doyen, curator.LongestLifeYearsKnown));

            // Curated as a record rather than left to the line's own category: "Longevity" would be a first
            // the first time and routine news for ever after, which is the opposite of what a record is.
            curator.ConsiderLifeRecord(entry, ageYears);
            doyen.tier.Notify_ChronicleNamed();
            return doyen;
        }

        /// <summary>The oldest living humanlike, ties broken by <see cref="Things.Thing.thingIDNumber"/>
        /// ascending so the answer is the same on every run and across a save — the same tie-break and the
        /// same reason as <see cref="God.AttentionBudget"/>'s own ordering.</summary>
        private static Pawn? OldestLiving(IReadOnlyList<Pawn> citizens)
        {
            Pawn? best = null;
            float bestAge = float.NegativeInfinity;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p == null || p.Dead || !p.RaceProps.Humanlike) continue;
                float age = p.ageTracker.AgeBiologicalYearsFloat;
                if (age < bestAge) continue;
                if (age > bestAge || best == null || p.thingIDNumber < best.thingIDNumber)
                {
                    best = p;
                    bestAge = age;
                }
            }
            return best;
        }

        /// <summary>"Category: detail", the shape every free-form chronicle line in this codebase uses and
        /// <see cref="MomentCurator.CategoryForFreeform"/> reads. The line says what was surpassed when there
        /// was anything to surpass, so the chronicle reads as a succession rather than a bare fact.</summary>
        internal static string ChronicleLine(Pawn doyen, float previousRecordYears) =>
            previousRecordYears < 0f
                ? "Longevity: " + doyen.Label + " is the oldest person the civilization has known."
                : "Longevity: " + doyen.Label + " has outlived every life the civilization has known, at "
                  + (int)doyen.ageTracker.AgeBiologicalYearsFloat + " years.";
    }
}
