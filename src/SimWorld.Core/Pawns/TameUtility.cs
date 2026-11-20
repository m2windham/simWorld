using System;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Rolls and consequences for one taming attempt (RimWorld: <c>RimWorld.TameUtility</c>). The exact
    /// chance curve RimWorld weighs a handler's Animals skill against an animal's wildness with is not
    /// available to this port; <see cref="TameChance"/> is this port's own stand-in — see
    /// <see cref="AnimalTuning"/> for the constants and which trend each one backs.
    /// </summary>
    public static class TameUtility
    {
        /// <summary>Chance in [0, 1] that one taming attempt by a handler with <paramref name="animalsSkill"/>
        /// succeeds against an animal of the given <paramref name="wildness"/> (0 = trivial, 1 = extreme).</summary>
        public static float TameChance(float wildness, int animalsSkill)
        {
            float chance = AnimalTuning.TameBaseChance
                + animalsSkill * AnimalTuning.TameSkillFactorPerLevel
                - wildness * AnimalTuning.TameWildnessFactor;
            return GenMath.Clamp01(chance);
        }

        /// <summary>
        /// One taming attempt: <paramref name="tamer"/> against <paramref name="animal"/>. On success the
        /// animal joins the tamer's faction and its <see cref="MindState.Pawn_MindState.tameness"/> is set
        /// full; on failure it may turn against the tamer instead (RimWorld: a failed tame can make the
        /// animal a manhunter that attacks; see <see cref="MindState.Pawn_MindState.angryAt"/>'s own doc for
        /// why this port's consequence is behavioural — reroute the animal's think tree — rather than a
        /// Combat-module attack, which is out of this lane's boundary). Returns whether the tame succeeded.
        /// </summary>
        public static bool TryTame(Pawn animal, Pawn tamer)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (tamer == null) throw new ArgumentNullException(nameof(tamer));

            int skill = tamer.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            float chance = TameChance(animal.RaceProps.wildness, skill);

            if (Rand.Chance(chance))
            {
                animal.faction = tamer.faction;
                // A tamed animal is no longer anybody's Tame (or Hunt) target (RimWorld removes the designation
                // when the animal joins — recalled, not re-read).
                animal.Map?.designationManager.RemoveAllDesignationsOn(animal);
                animal.mindState.tameness = 1f;
                ClearMind(animal);
                tamer.skills?.Learn(SkillDefOf.Animals, AnimalTuning.TameSuccessXp);
                return true;
            }

            // The race's own chance, not a function of wildness (RimWorld: manhunterOnTameFailChance). It
            // was half the wildness, which made a failed tame of a chicken turn it on the handler one time
            // in four; in RimWorld a chicken, a husky or a muffalo never does. Those fights were the largest
            // single source of wounds in a threat-free settlement (docs/perf/storyteller-pressure).
            if (Rand.Chance(animal.RaceProps.manhunterOnTameFailChance))
            {
                // RimWorld: StartManhunterBecauseOfPawnAction("AnimalManhunterFromTaming"), the same call
                // revenge on damage makes, packmates and all.
                animal.mindState.StartManhunterBecauseOfPawnAction(tamer);
            }
            return false;
        }

        /// <summary>
        /// An animal that has just changed sides stops what it was doing and forgets who it was angry at
        /// (RimWorld: <c>Pawn.SetFaction</c> ends in <c>ClearMind</c>, which resets the mind state — the
        /// manhunter state with it — and stops every job).
        ///
        /// <para/><b>What its absence did.</b> A failed attempt can leave an animal angry at its handler for
        /// hours (<see cref="ManhunterRecovery"/>), and wounded prey turns on its
        /// hunter the same way. The next handler to succeed changed the animal's faction and nothing else, so
        /// the settlement now owned an animal that was still mid-attack on one of its own people, with the
        /// grudge that had started it still running. Measured on a founding band of twenty with every
        /// storyteller threat switched off: 34 of 118 wounds in twelve days came from animals carrying the
        /// settlement's own faction (<c>docs/perf/storyteller-pressure</c>).
        /// </summary>
        private static void ClearMind(Pawn animal)
        {
            animal.mindState.angryAt = null;
            animal.mindState.angryUntilTick = -1;
            animal.mindState.manhunterUntilTick = -1;
            if (animal.Spawned && animal.jobs?.curJob != null)
            {
                animal.jobs.EndCurrentJob(AI.JobCondition.InterruptForced);
            }
        }
    }
}
