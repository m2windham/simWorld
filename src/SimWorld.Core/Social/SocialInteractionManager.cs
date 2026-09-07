using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Social
{
    /// <summary>
    /// Runs the population-wide interaction sweep (RimWorld: <c>Pawn_InteractionsTracker</c>, one per pawn;
    /// SimWorld folds it into a single manager sweep the same way <see cref="FamilyManager.DemographyTick"/>
    /// folds marriage/birth/death into one pass rather than a per-pawn tick hook — see <c>docs/perf/baseline.md</c>
    /// §2 for why per-tick-per-pawn work is the thing to avoid). No spatial/room concept exists yet (Map/AI are
    /// out of this module's scope), so any two living, humanlike pawns in the given population can interact —
    /// narrowing that to "pawns actually near each other" is a follow-up once the map module can supply it.
    /// Stateless by design (nothing here needs to survive a save): the interval gate reads
    /// <see cref="Find.TickManager"/> directly, exactly as <c>Storyteller.StorytellerTick</c> does.
    /// </summary>
    public sealed class SocialInteractionManager
    {
        private readonly List<Pawn> tmpEligible = new List<Pawn>();

        /// <summary>Call once per game tick; only acts every <see cref="SocialTuning.InteractionIntervalTicks"/> ticks.</summary>
        public void SocialInteractionTick(IReadOnlyList<Pawn> population)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));
            if (Find.TickManager.TicksGame % SocialTuning.InteractionIntervalTicks != 0) return;
            RunInterval(population);
        }

        /// <summary>
        /// One interaction sweep, unconditionally (tests call this directly to avoid waiting out the interval
        /// gate). Iterates the population in ascending pawn-id order for determinism — same shape as
        /// <see cref="FamilyManager.ProcessMarriages"/> — rolls whether each eligible pawn attempts an
        /// interaction this round, picks a random partner from the rest of the population, then a weighted
        /// <see cref="InteractionDef"/> for that specific pair, and executes it.
        /// </summary>
        public void RunInterval(IReadOnlyList<Pawn> population)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));

            tmpEligible.Clear();
            for (int i = 0; i < population.Count; i++)
            {
                Pawn p = population[i];
                if (p != null && !p.Dead && p.RaceProps.Humanlike) tmpEligible.Add(p);
            }
            if (tmpEligible.Count < 2) return;
            tmpEligible.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            for (int i = 0; i < tmpEligible.Count; i++)
            {
                Pawn initiator = tmpEligible[i];
                if (!Rand.Chance(SocialTuning.InteractionChancePerPawnPerInterval)) continue;

                Pawn recipient = PickRecipient(i, tmpEligible);

                if (TryRandomInteraction(initiator, recipient, out InteractionDef chosen))
                {
                    chosen.Worker.Interacted(initiator, recipient);
                }
            }
        }

        /// <summary>Uniformly picks one of the other <c>eligible.Count - 1</c> pawns (excluding
        /// <paramref name="initiatorIndex"/> itself): draws from the <c>Count - 1</c> slots that remain once
        /// the initiator is removed, then shifts past its index — never biases toward any one fallback
        /// position the way "redraw the last element on a collision" would.</summary>
        private static Pawn PickRecipient(int initiatorIndex, List<Pawn> eligible)
        {
            int index = Rand.Range(0, eligible.Count - 1);
            if (index >= initiatorIndex) index++;
            return eligible[index];
        }

        /// <summary>Weighted pick across every <see cref="InteractionDef"/> in content for this specific pair
        /// (RimWorld: <c>InteractionUtility.GetRandomInteraction</c>). False when nothing is selectable (every
        /// worker returned weight 0 for this pair).</summary>
        public static bool TryRandomInteraction(Pawn initiator, Pawn recipient, out InteractionDef result)
        {
            return GenCollection.TryRandomElementByWeight(
                DefDatabase<InteractionDef>.AllDefsListForReading,
                d => d.Worker.RandomSelectionWeight(initiator, recipient),
                Rand.Current,
                out result!);
        }
    }
}
