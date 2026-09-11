using System.Collections.Generic;

using SimWorld.Pawns;

namespace SimWorld.God
{
    /// <summary>
    /// The Full-tier cap inside the focused settlement: keep the <see cref="TieringTuning.FullTierBudget"/>
    /// most significant citizens at <see cref="PawnTier.Full"/> and hold the rest where they are, even while
    /// the player is looking straight at them (<c>docs/spec/simworld-spec.md</c> §11.3/§11.5).
    ///
    /// <para/><b>Why this exists.</b> <see cref="AttentionManager"/> already bounds Full tier to one
    /// settlement's live roster, which sounds like a bound and is not one: demography adds about 4.5% a year,
    /// so that roster doubles roughly every 17 years and crosses §11.3's measured Full ceiling somewhere
    /// around year 115-125 (67 citizens at year 10, 1,412 at year 100, 16,868 at year 160). "Population as
    /// large as we can" is the pillar the tier system exists to make affordable; a tier that only ever
    /// promotes is not a tier.
    ///
    /// <para/><b>The ordering, most significant first.</b> A cap needs a ranking between the four promotion
    /// reasons and nothing expressed one. This is it:
    /// <list type="number">
    /// <item><description><see cref="Pawn_TierTracker.HasRole"/> — a leader or founder is significant by
    /// station; the civilization's story runs through them.</description></item>
    /// <item><description><see cref="Pawn_TierTracker.ChronicleNamed"/> — the chronicle already singled them
    /// out by name; they are part of the told history.</description></item>
    /// <item><description><see cref="Pawn_TierTracker.RelatedToPromoted"/> — significant by association, one
    /// hop from someone who matters.</description></item>
    /// <item><description><see cref="Pawn_TierTracker.Attending"/> — the player is looking at their
    /// settlement.</description></item>
    /// </list>
    /// The reasoning is the shape of the sets, not a hierarchy of importance. The first three are properties
    /// of <b>the person</b> and are held by few: a settlement has one leader, a handful of named figures, a
    /// ring of kin around them. The fourth is a property of <b>the camera</b> and is held by the entire
    /// roster at once — it is the only one of the four that grows with the population, so it is the only one
    /// that can ever overrun a budget. Attention must therefore be the first thing to yield when the roster
    /// exceeds what Full tier costs: the cap holds back the merely-looked-at, never the leader.
    ///
    /// <para/><b>The tie-break is <see cref="Things.Thing.thingIDNumber"/>, ascending.</b> Rank 4 is the
    /// whole roster minus a few, so almost every seat is decided by the tie-break and it carries more weight
    /// than a tie-break usually does. It was chosen for three properties, in this order:
    /// <list type="bullet">
    /// <item><description><b>Stable.</b> Assigned once at construction, never reused, Scribed with the
    /// <see cref="Things.Thing"/> and restored on load (<c>Thing.ExposeData</c> also winds the allocator past
    /// it), so the same roster produces the same seating across two runs and across a save boundary. Nothing
    /// else on a citizen is both unique and unchanging — a name is not unique, an age moves, a tier is an
    /// output of this very decision and would make the ordering self-referential.</description></item>
    /// <item><description><b>Monotone in creation order</b>, which means the tie-break is seniority: the
    /// longer-established citizen keeps the seat and a newborn lands at the bottom of the queue. That is a
    /// defensible answer to "who does the god's attention reach" and not merely an arbitrary one.</description></item>
    /// <item><description><b>It is what makes growth thrash-free.</b> Because every new citizen sorts below
    /// every existing one, demography can add a thousand people to an over-budget settlement without
    /// displacing a single incumbent. A tie-break that moved with the population — anything hashed, anything
    /// re-drawn per sweep — would reshuffle the seated set every time someone was born, which is the failure
    /// mode a cap has to avoid: a citizen promoted and demoted on alternating sweeps is worse than no cap at
    /// all, because the catch-up work on every promotion is paid over and over.</description></item>
    /// </list>
    ///
    /// <para/><b>The cap is a demotion constraint, never a promotion clock</b> (§11.3's rule, unchanged).
    /// Nothing here promotes anybody: it only decides whose attention counts. A citizen the budget cannot
    /// seat stays at exactly the tier they were already at — <see cref="PawnTier.Interval"/> for the ordinary
    /// case of a settlement whose focus arrived while everyone was Interval, and <see cref="PawnTier.Statistical"/>
    /// for one who had already settled there, because lifting them to Interval would be a promotion granted
    /// by the cap rather than by significance. And it is lossless in identity either way: a held-back citizen
    /// keeps their real <c>Pawn</c> and is never spilled into <see cref="World.Settlement.StatisticalPopulation"/>,
    /// the bare cohort count that has no <c>Pawn</c> behind a person at all.
    /// </summary>
    public static class AttentionBudget
    {
        /// <summary>Rank of the significance reason that seats this citizen, 0 (most significant) to 3. See the
        /// class doc for the ordering and why it runs this way round.</summary>
        public static int RankOf(Pawn_TierTracker tier)
        {
            if (tier.HasRole) return 0;
            if (tier.ChronicleNamed) return 1;
            if (tier.RelatedToPromoted) return 2;
            return 3; // attending, and nothing else
        }

        /// <summary>
        /// Tells every living citizen of <paramref name="settlement"/> that the player is attending it, and
        /// tells the ones past the budget that their attention does not count. The only caller is
        /// <see cref="AttentionManager"/>, through both of its paths (the immediate
        /// <see cref="AttentionManager.Focus"/> and the periodic <see cref="AttentionManager.Reconcile"/>), so
        /// the seating a player sees the instant they open a settlement is the same seating the sweep would
        /// have reached.
        ///
        /// <para/>Idempotent by construction: the ranking reads only the four significance flags and an
        /// immutable id, never the tier it is about to set, so running it twice with an unchanged roster is
        /// the second run agreeing with the first. That is what the anti-thrash test pins.
        /// </summary>
        public static void Apply(World.Settlement settlement)
        {
            IReadOnlyList<Pawn> citizens = settlement.Citizens;

            // A roster inside the budget needs no ordering at all — everybody gets a seat. This is the common
            // case for most of a campaign (a founding band is 20-40, and §11.3's table only reaches the budget
            // after a century) and it keeps the ordinary sweep O(roster) with no allocation, paying for the
            // sort only where there is actually a decision to make.
            if (citizens.Count <= TieringTuning.FullTierBudget)
            {
                for (int i = 0; i < citizens.Count; i++)
                {
                    Pawn citizen = citizens[i];
                    if (citizen.Dead) continue;
                    citizen.tier.Notify_AttentionChanged(true, withinBudget: true);
                }
                return;
            }

            // The dead are excluded rather than seated: they are still on the roster until the settlement's
            // next SyncCitizenSpawns prunes them, and a corpse holding a seat would keep a living citizen out.
            var living = new List<Pawn>(citizens.Count);
            for (int i = 0; i < citizens.Count; i++)
            {
                if (!citizens[i].Dead) living.Add(citizens[i]);
            }

            living.Sort(SignificanceOrder);

            for (int i = 0; i < living.Count; i++)
            {
                living[i].tier.Notify_AttentionChanged(true, withinBudget: i < TieringTuning.FullTierBudget);
            }
        }

        /// <summary>
        /// The ordering itself: rank first, then <see cref="Things.Thing.thingIDNumber"/> ascending. Total —
        /// ids are unique — so the sort is deterministic regardless of <see cref="List{T}.Sort"/> being
        /// unstable, and two runs over the same roster seat the same people in the same order.
        /// </summary>
        private static int SignificanceOrder(Pawn a, Pawn b)
        {
            int rankA = RankOf(a.tier);
            int rankB = RankOf(b.tier);
            if (rankA != rankB) return rankA.CompareTo(rankB);
            return a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }
}
