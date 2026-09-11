using System;
using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Pawns;
using SimWorld.Social;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// What a pawn attacks with (RimWorld: <c>Verse.Pawn.TryGetAttackVerb</c> plus <c>Pawn_MeleeVerbs</c>).
    /// <para/>
    /// <b>This is the extraction the combat lane made.</b> The weapon half of it was already written, inside
    /// <see cref="HuntUtility.MakeHuntVerb"/> — the hunting lane needed "ranged verb if there is one, else a
    /// melee tool" first. Rather than write a second copy for combat, that body moved here and
    /// <see cref="HuntUtility.MakeHuntVerb"/>/<see cref="HuntUtility.EffectiveRange"/> now delegate, so the
    /// hunt and the fight can never disagree about which verb a given pawn swings. The hunt's one real
    /// difference survives as <c>allowNaturalWeapon</c> — see below.
    /// <para/>
    /// A fresh <see cref="Verb"/> per call: a <see cref="Verb"/> carries warmup/burst/cooldown state, so a
    /// caller driving an attack must build one and keep it for the life of the job, never call this per tick.
    /// </summary>
    public static class AttackVerbUtility
    {
        /// <summary>
        /// The first ranged attack the pawn's wielded weapon offers, or null (RimWorld: the
        /// <c>equipment.PrimaryEq.PrimaryVerb</c> half of <c>WorkGiver_HunterHunt.HasHuntingWeapon</c>).
        /// Reads <see cref="Pawn_EquipmentTracker.Primary"/> — this port's documented stand-in for "the
        /// weapon this pawn currently wields".
        /// </summary>
        public static VerbProperties? RangedVerbPropsFor(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            ThingWithComps? primary = pawn.equipment?.Primary;
            List<VerbProperties>? verbs = primary?.def.verbs;
            if (verbs == null) return null;
            for (int i = 0; i < verbs.Count; i++)
            {
                if (!verbs[i].IsMeleeAttack) return verbs[i];
            }
            return null;
        }

        /// <summary>Carrying something it can actually attack with — a ranged verb or a melee
        /// <see cref="Tool"/> — as opposed to being empty-handed and down to
        /// <see cref="NaturalWeaponFor"/>. <see cref="CombatPostureUtility"/> is the one caller that cares
        /// about the distinction, and its own doc says why.</summary>
        public static bool HasEquippedWeapon(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (RangedVerbPropsFor(pawn) != null) return true;
            List<Tool>? tools = pawn.equipment?.Primary?.def.tools;
            return tools != null && tools.Count > 0;
        }

        /// <summary>
        /// The attack this pawn will actually use: its wielded weapon's ranged verb if it has one, else the
        /// first melee <see cref="Tool"/> on that weapon, else — when
        /// <paramref name="allowNaturalWeapon"/> — its bare hands or teeth
        /// (<see cref="NaturalWeaponFor"/>). Null when it has nothing at all to attack with.
        /// <para/>
        /// <paramref name="allowNaturalWeapon"/> is false for exactly one caller,
        /// <see cref="HuntUtility.MakeHuntVerb"/>: punching a muffalo is the fight
        /// <see cref="HuntUtility.IsSafeToHunt"/> exists to prevent, so an empty-handed pawn is refused a
        /// hunt outright rather than sent at one bare-knuckled. Defending yourself with your fists when a
        /// raider is already on top of you is a different proposition, and that is the default.
        /// </summary>
        public static Verb? TryGetAttackVerb(Pawn pawn, bool allowNaturalWeapon = true)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            VerbProperties? ranged = RangedVerbPropsFor(pawn);
            if (ranged != null) return VerbUtility.MakeVerb(pawn, ranged);

            return TryGetMeleeVerb(pawn, allowNaturalWeapon);
        }

        /// <summary>
        /// The melee half of <see cref="TryGetAttackVerb"/>, on its own (RimWorld:
        /// <c>Pawn_MeleeVerbs.TryGetMeleeVerb</c>) — what <see cref="JobDriver_AttackMelee"/> asks for, since
        /// a pawn ordered into melee swings even while carrying a bow.
        /// </summary>
        public static Verb_MeleeAttack? TryGetMeleeVerb(Pawn pawn, bool allowNaturalWeapon = true)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            List<Tool>? tools = pawn.equipment?.Primary?.def.tools;
            if (tools != null)
            {
                for (int i = 0; i < tools.Count; i++)
                {
                    Verb_MeleeAttack? melee = MeleeVerbUtility.MakeVerb(pawn, tools[i]);
                    if (melee != null) return melee;
                }
            }

            if (!allowNaturalWeapon) return null;
            Tool? natural = NaturalWeaponFor(pawn);
            return natural == null ? null : MeleeVerbUtility.MakeVerb(pawn, natural);
        }

        /// <summary>
        /// The fists or teeth a pawn falls back on when it is carrying nothing (RimWorld: the <c>tools</c>
        /// list every race's ThingDef defines — a human's fists and teeth, an animal's bite, scratch or
        /// horns).
        /// <para/>
        /// <b>Translation, and why it is code and not content.</b> No race this port ships defines
        /// <see cref="Defs.ThingDef.tools"/> at all —
        /// <see cref="MindState.MentalState_SocialFighting"/> hit exactly this wall first and built its
        /// bare-knuckled <see cref="Tool"/> in code, and <see cref="HuntUtility.MakeHuntVerb"/> recorded the
        /// gap as the reason an empty-handed pawn simply cannot hunt. Filling it properly means adding a
        /// hand-written <c>tools</c> list to <c>Races_Humanlike.xml</c> and <c>Races_Animal.xml</c> — two
        /// shared content files, and three sets of invented per-species numbers — where one scaling rule
        /// (<see cref="CombatAITuning.NaturalWeaponPowerPerBodySize"/>) gives the same shipped animals the
        /// same ordering. So this follows the precedent rather than the 1:1 port, deliberately: the moment a
        /// race does declare <c>tools</c>, <see cref="TryGetMeleeVerb"/> already prefers them and this stops
        /// being reached for that race.
        /// <para/>
        /// A humanlike's fists reuse <see cref="SocialTuning.SocialFightFistPower"/> outright rather than
        /// introducing a second bare-fist number — one punch is one punch, whether it lands in a scuffle over
        /// an insult or on a raider. Null for anything that is neither Humanlike nor Animal.
        /// </summary>
        public static Tool? NaturalWeaponFor(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            if (pawn.RaceProps.Animal)
            {
                return new Tool
                {
                    label = "teeth",
                    capacities = new List<ToolCapacityDef> { CombatAIDefOf.Bite },
                    power = CombatAITuning.NaturalWeaponPowerPerBodySize * pawn.BodySize,

                    // This port's Tool has no power-derived armour-penetration fallback (its own field doc
                    // says content always sets the number), and there is no sourced natural-weapon value to
                    // set it to — so teeth and fists alike get none, exactly as the social fight's fists do.
                    armorPenetration = 0f,
                };
            }

            if (pawn.RaceProps.Humanlike)
            {
                return new Tool
                {
                    label = "fists",
                    capacities = new List<ToolCapacityDef> { ToolCapacityDefOf.Blunt },
                    power = SocialTuning.SocialFightFistPower,
                    armorPenetration = 0f,
                };
            }

            return null;
        }

        /// <summary>
        /// How close the attacker must be for <paramref name="verb"/> to reach, in cells. A ranged verb
        /// answers with its own <see cref="VerbProperties.range"/>; a melee one cannot, because
        /// <see cref="MeleeVerbUtility"/> never sets that field and it keeps its 90-cell ranged default — see
        /// <see cref="CombatAITuning.MeleeReachCells"/>.
        /// </summary>
        public static float EffectiveRange(Verb verb)
        {
            if (verb == null) throw new ArgumentNullException(nameof(verb));
            return verb.verbProps.IsMeleeAttack ? CombatAITuning.MeleeReachCells : verb.verbProps.range;
        }
    }
}
