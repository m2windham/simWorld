using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// A settlement learning that one of its own is dead — as distinct from watching it happen, which is
    /// <see cref="PawnDiedThoughtsUtility"/>'s job.
    ///
    /// <para/><b>The hole this closes was found by measuring, not by reading.</b> A probe run showed an
    /// unwatched settlement losing a citizen to a manhunter pack while its mood swing stayed 0.08 against a
    /// watched settlement's 0.21, and the ablation put the difference in deaths squarely on the pack. A
    /// settlement lost somebody to violence and nothing moved. The reason is that an abstractly-resolved
    /// death fails <b>three independent gates</b> in the witness path, any one of which alone would silence
    /// it: the kill arrives with no <c>DamageInfo</c> (so it reads as non-violent), the victim was never
    /// spawned (so it has no map), and there are no spawned witnesses to stand near it. An unwatched
    /// settlement could lose people indefinitely and no survivor would ever mourn.
    ///
    /// <para/><b>This is the same shape as a defect fixed one phase earlier, in a different system.</b>
    /// <c>Director.StorytellerDeathEvents</c>' own doc already recorded the cause — "a raid settled
    /// abstractly by SettlementRaidResolver kills through FamilyManager.HandleDeath, which has no DamageInfo
    /// to hand Kill, so those deaths read as non-violent here" — and compensated for it in the storyteller's
    /// adaptation charge. Nobody compensated for it in grief. One known fact, two systems, one of them
    /// updated. (The adaptation charge has since stopped asking about violence at all, which is RimWorld's
    /// rule for a death; the compensation it needed went with the question.)
    ///
    /// <para/><b>The witness gate is right and is left alone.</b> Requiring
    /// <c>DamageDef.externalViolence</c> before traumatising a room is correct: age, disease and a surgery
    /// that went wrong genuinely should not. The trouble is only that an abstract mauling arrives looking
    /// exactly like old age. So this does not relax that gate — it adds the memory that was never gated on
    /// violence in the first place. RimWorld draws the same line, between <c>WitnessedDeathAlly</c> and
    /// <c>KnowColonistDied</c>, and for the same reason.
    ///
    /// <para/><b>Every death, not only violent ones.</b> A settlement mourns its dead however they died; that
    /// is what distinguishes bereavement from trauma. The witness memory stacks on top for the people who
    /// were actually there.
    ///
    /// <para/><b>Statistical-tier citizens cannot grieve, by construction.</b> There is no <c>Pawn</c> behind
    /// them to hold a memory — that is the tier's whole definition (spec §11.3). So a very large settlement's
    /// grief is carried by the people it has actually instantiated, which is the same compromise every other
    /// per-citizen effect in this port makes, and is noted here rather than discovered later.
    /// </summary>
    public static class BereavementUtility
    {
        /// <summary>
        /// Gives every other living member of <paramref name="victim"/>'s civilization the memory of losing
        /// them, and returns how many took it. Zero is an ordinary answer: a lone founder, a settlement whose
        /// remaining people are all Statistical, or a roster of psychopaths.
        /// </summary>
        public static int Notify_CitizenDied(Pawn victim)
        {
            if (victim == null) return 0;
            if (!victim.RaceProps.Humanlike) return 0;
            if (BereavementThoughtDefOf.KnowColonistDied == null) return 0;

            Director.Storyteller? storyteller = Find.Storyteller;
            if (storyteller == null) return 0;

            int given = 0;
            IReadOnlyList<Director.IIncidentTarget> targets = storyteller.AllIncidentTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                foreach (Pawn mourner in targets[i].PlayerPawnsForStoryteller)
                {
                    if (ReferenceEquals(mourner, victim) || mourner.Dead) continue;
                    if (mourner.needs?.mood?.thoughts?.memories == null) continue;

                    // Psychopath is a nullifyingTrait on the shipped def, so ThoughtHandlers refuses the
                    // memory for them without this class knowing anything about traits.
                    if (mourner.needs.mood.thoughts.memories.TryGainMemory(
                            BereavementThoughtDefOf.KnowColonistDied) != null)
                    {
                        given++;
                    }
                }
            }

            return given;
        }
    }
}
