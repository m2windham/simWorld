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
        /// Whether an animal may be tamed at all (RimWorld: <c>TameUtility.CanTame</c>): an animal that belongs
        /// to nobody and is not utterly untameable. A tamed animal, another faction's, a prisoner's and a
        /// wildness-1 animal are all refused.
        /// </summary>
        public static bool CanTame(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            return pawn.RaceProps.Animal && pawn.faction == null && pawn.RaceProps.wildness < 1f && !pawn.Dead;
        }

        /// <summary>
        /// Someone made an attempt on this animal in the last <see cref="AnimalTuning.MinTameIntervalTicks"/>
        /// (RimWorld: <c>TameUtility.TriedToTameTooRecently</c>). The work giver will not offer the animal
        /// and the driver will not make the attempt inside that window, so an animal that has just been tried
        /// is left alone for twelve hours rather than handed to the next idle citizen.
        /// </summary>
        public static bool TriedToTameTooRecently(Pawn animal)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            return Find.TickManager.TicksGame < animal.mindState.lastAssignedInteractTime + AnimalTuning.MinTameIntervalTicks;
        }

        /// <summary>
        /// Whether a handler can walk up to this animal and try right now (RimWorld:
        /// <c>WorkGiver_InteractAnimal.CanInteractWithAnimal</c> plus <c>Pawn.CanCasuallyInteractNow</c>): not
        /// downed, awake, and <b>not already turned on somebody</b> — RimWorld refuses to interact with a pawn
        /// in an aggro mental state, and an animal with a standing grudge or a manhunt is this port's version
        /// of one. Before this, a muffalo that had just mauled one citizen was the next citizen's tame target.
        /// </summary>
        public static bool CanInteractNow(Pawn animal)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (animal.Downed || animal.Asleep) return false;
            if (AI.HuntUtility.IsAngry(animal) || AI.AttackTargetsUtility.IsManhunting(animal)) return false;
            return true;
        }

        /// <summary>
        /// One taming attempt: <paramref name="tamer"/> against <paramref name="animal"/>, in RimWorld's order
        /// (<c>InteractionWorker_RecruitAttempt.Interacted</c>):
        /// <list type="number">
        /// <item>The attempt is logged on the animal (<see cref="Pawn_MindState.lastAssignedInteractTime"/>),
        /// win or lose, which is what starts the twelve-hour wait before anyone may try it again.</item>
        /// <item><b>Revenge is rolled first, on every attempt</b>
        /// (<c>CheckStartMentalStateBecauseRecruitAttempted</c>): the race's own
        /// <see cref="RaceProperties.manhunterOnTameFailChance"/>. If it comes up the animal turns on the
        /// tamer and the attempt goes no further. Muffalo, husky and chicken are 0 in RimWorld, so no one is
        /// ever mauled for trying to tame one; a bear or a thrumbo is not 0. The consequence is this port's
        /// personal grudge (<see cref="Pawn_MindState.angryAt"/>) where RimWorld's is a full manhunter — see
        /// <see cref="AnimalTuning.TameFailAngerDurationTicks"/>.</item>
        /// <item>Only then the success roll, <see cref="TameChance"/>. On success the animal joins the tamer's
        /// faction and is <b>calm</b> from that moment: RimWorld's <c>Pawn.SetFaction</c> ends with
        /// <c>ClearMind</c>, so a freshly tamed animal holds no grudge and no manhunt.</item>
        /// </list>
        /// Returns whether the tame succeeded.
        /// </summary>
        public static bool TryTame(Pawn animal, Pawn tamer)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (tamer == null) throw new ArgumentNullException(nameof(tamer));

            animal.mindState.lastAssignedInteractTime = Find.TickManager.TicksGame;

            if (animal.faction == null
                && !AI.HuntUtility.IsAngry(animal)
                && Rand.Chance(animal.RaceProps.manhunterOnTameFailChance))
            {
                animal.mindState.angryAt = tamer;
                animal.mindState.angryUntilTick = Find.TickManager.TicksGame + AnimalTuning.TameFailAngerDurationTicks;
                return false;
            }

            int skill = tamer.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            float chance = TameChance(animal.RaceProps.wildness, skill);

            if (!Rand.Chance(chance)) return false;

            animal.faction = tamer.faction;
            animal.mindState.ClearGrudges();
            animal.mindState.tameness = 1f;
            tamer.skills?.Learn(SkillDefOf.Animals, AnimalTuning.TameSuccessXp);
            return true;
        }
    }
}
