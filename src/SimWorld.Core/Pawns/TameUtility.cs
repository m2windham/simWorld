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
                animal.mindState.tameness = 1f;
                tamer.skills?.Learn(SkillDefOf.Animals, AnimalTuning.TameSuccessXp);
                return true;
            }

            if (Rand.Chance(AnimalTuning.TameFailAngerChance * animal.RaceProps.wildness))
            {
                animal.mindState.angryAt = tamer;
                animal.mindState.angryUntilTick = Find.TickManager.TicksGame + AnimalTuning.TameFailAngerDurationTicks;
            }
            return false;
        }
    }
}
