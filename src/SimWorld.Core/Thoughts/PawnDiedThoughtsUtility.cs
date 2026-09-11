using System.Collections.Generic;

using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// What a death does to the people who saw it (RimWorld: <c>RimWorld.PawnDiedOrDownedThoughtsUtility</c>,
    /// narrowed to the one memory this port ships content for).
    ///
    /// <para/><b>The distinction this class exists to make.</b> <see cref="DamageDef.externalViolence"/> is
    /// RimWorld's answer to "was this a killing or just a death", and until now nothing in this port read it:
    /// an old woman dying in her bed and a man cut down in the street were the same event to every system
    /// downstream. They are not the same event to the people in the room. A violent death in view leaves a
    /// four-day memory (<c>WitnessedDeathAlly</c>, mood -5, stacking to five); a quiet one leaves none, which
    /// is why the flag is asked before anything else here.
    ///
    /// <para/><b>The shipped <c>WitnessedDeathAlly</c> ThoughtDef had no giver at all</b> — it has been in
    /// <c>Thoughts_Memories.xml</c> since the thoughts module landed, complete with its
    /// <c>nullifyingTraits</c> for Psychopath and Bloodlust, and the only line in the repository that
    /// mentioned it was an assertion in <c>MoodTests</c>. The audit's field checks cannot see a def that
    /// nothing grants; this one was found by following <c>externalViolence</c> to the consumer it wanted.
    ///
    /// <para/><b>Trims, recorded rather than hidden.</b> RimWorld picks between <c>WitnessedDeathAlly</c>,
    /// <c>WitnessedDeathNonAlly</c> and <c>WitnessedDeathBloodlust</c>, and tests line of sight as well as
    /// distance. This port ships only the ally memory, so only an ally's death is remembered, and the witness
    /// test is radius alone — <see cref="SightRadius"/> — because a wall between two pawns is a question the
    /// map can answer but nothing else in this port asks yet. Both are narrowings, never widenings: nobody
    /// gains a memory here who would not also gain one in RimWorld.
    /// </summary>
    public static class PawnDiedThoughtsUtility
    {
        /// <summary>
        /// How far a death is felt, in cells. <b>Unsourced:</b> RimWorld's own witness radius is a literal in
        /// <c>PawnDiedOrDownedThoughtsUtility</c> that could not be read from here, so this is this port's own
        /// stand-in. Tests pin the shape — someone standing over the body remembers it, someone across the
        /// map does not — never this number.
        /// </summary>
        public const float SightRadius = 12f;

        /// <summary>
        /// Gives every ally who was close enough the memory of watching <paramref name="victim"/> die, when
        /// the death was violent. Returns how many pawns gained it, so a caller — and a test — can tell
        /// "nobody was watching" from "the death did not count as a killing".
        /// </summary>
        public static int Notify_PawnDied(Pawn victim, DamageInfo? dinfo)
        {
            if (victim == null) return 0;

            // The whole point of the flag: only a killing traumatises. Age, starvation, disease and a
            // surgery that went wrong all arrive here with either no damage at all or damage that declares
            // itself non-violent, and leave the room unchanged.
            if (dinfo == null || !dinfo.Def.externalViolence) return 0;

            // Animals die violently all the time — this port hunts them for food — and RimWorld's witness
            // thoughts are a humanlike's memory of a humanlike. A dead deer is dinner, not a bereavement.
            if (!victim.RaceProps.Humanlike) return 0;

            Map.Map? map = victim.Map;
            if (map == null) return 0;

            int given = 0;
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn witness = spawned[i];
                if (ReferenceEquals(witness, victim) || witness.Dead || !witness.RaceProps.Humanlike) continue;
                if (witness.needs?.mood == null) continue;

                // "Ally" is the victim's own side. Faction identity rather than non-hostility on purpose:
                // a neutral trader watching a stranger die is a bystander, and this memory is for the people
                // who have lost one of their own.
                if (witness.faction == null || !ReferenceEquals(witness.faction, victim.faction)) continue;
                if (!witness.Position.InHorDistOf(victim.Position, SightRadius)) continue;

                // Psychopath and Bloodlust are nullifyingTraits on the shipped def, so ThoughtHandlers
                // refuses the memory for them without this class knowing anything about traits.
                if (witness.needs.mood.thoughts.memories.TryGainMemory(DeathThoughtDefOf.WitnessedDeathAlly) != null) given++;
            }
            return given;
        }
    }
}
