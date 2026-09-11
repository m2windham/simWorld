using System;
using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Shared predicates for the <c>Hunt</c> work type (RimWorld: the static helpers on
    /// <c>RimWorld.WorkGiver_HunterHunt</c>, plus the verb selection <c>JobDriver_Hunt</c> reaches for via
    /// <c>Pawn.TryGetAttackVerb</c>). Split out of <see cref="WorkGiver_Hunt"/> so the giver's "is this worth
    /// starting" test and the driver's "what do I actually swing" are the same code, not two drifting copies.
    /// </summary>
    public static class HuntUtility
    {
        /// <summary>
        /// The first ranged attack the pawn's wielded weapon offers, or null (RimWorld: the
        /// <c>equipment.PrimaryEq.PrimaryVerb</c> half of <c>WorkGiver_HunterHunt.HasHuntingWeapon</c>). Reads
        /// <see cref="Pawn_EquipmentTracker.Primary"/> — this port's documented stand-in for "the weapon this
        /// pawn currently wields".
        /// </summary>
        public static VerbProperties? RangedVerbPropsFor(Pawn pawn) => AttackVerbUtility.RangedVerbPropsFor(pawn);

        /// <summary>
        /// RimWorld: <c>WorkGiver_HunterHunt.HasHuntingWeapon</c> — a hunter needs a <b>ranged</b> weapon, and
        /// a pawn without one never gets a hunting job at all (RimWorld warns about it separately with
        /// <c>Alert_HunterLacksRangedWeapon</c> rather than letting the giver hand out a melee hunt). Ported
        /// as-is. <b>Consequence worth knowing:</b> no <see cref="PawnKindDef"/> this port ships gives
        /// <c>Colonist</c>/<c>Villager</c> any <see cref="ThingDef.weaponTags"/>, so
        /// <see cref="Pawns.Generation.PawnWeaponGenerator"/> never arms an ordinary citizen and no generated
        /// settlement hunts until something puts a bow in someone's hands — see this module's report.
        /// <para/>
        /// RimWorld's own version additionally rejects an <c>onlyManualCast</c> or EMP verb; neither field
        /// exists on this port's <see cref="VerbProperties"/>, so there is nothing to reject.
        /// </summary>
        public static bool HasHuntingWeapon(Pawn pawn) => RangedVerbPropsFor(pawn) != null;

        /// <summary>
        /// A wild animal, alive or freshly killed, that is a legitimate hunting target. "Wild" is
        /// <see cref="WorkGiver_TameAnimals"/>'s own test (an <see cref="RaceProperties.Animal"/> with no
        /// <see cref="Pawn.faction"/>) — nobody's livestock, nobody's pet.
        /// <para/>
        /// A <b>dead</b> wild animal still qualifies: with no <c>Corpse</c> Thing in this codebase a carcass
        /// simply stays on the map as a dead <see cref="Pawn"/>, and a hunt interrupted between the kill and
        /// the butchery would otherwise strand it there forever. <see cref="JobDriver_Hunt"/> skips straight
        /// to butchering when its target is already dead.
        /// </summary>
        public static bool IsHuntableAnimal(Pawn animal)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            return animal.RaceProps.Animal && animal.faction == null && animal.Spawned && !animal.Destroyed;
        }

        /// <summary>
        /// Whether <paramref name="hunter"/> should start a fight with this <i>live</i> animal at all
        /// (translation — see <see cref="HuntingTuning.MaxPreyBodySizeRatio"/>: RimWorld leaves the judgement
        /// entirely to the player, who is not here to make it). Two refusals:
        /// <list type="bullet">
        /// <item>Prey more than <see cref="HuntingTuning.MaxPreyBodySizeRatio"/>× the hunter's own
        /// <see cref="Pawn.BodySize"/> — too big to take on alone.</item>
        /// <item>An animal already turned on somebody (<see cref="MindState.Pawn_MindState.angryAt"/>) — it is
        /// a manhunter now, not prey. This is also what stops a hunt that provoked its target from being
        /// re-offered on the very next job search, an immediate provoke/abort loop.</item>
        /// </list>
        /// Never consulted for a dead animal: a carcass fights nobody, however large it was.
        /// </summary>
        public static bool IsSafeToHunt(Pawn hunter, Pawn animal)
        {
            if (hunter == null) throw new ArgumentNullException(nameof(hunter));
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (animal.Dead) return true;
            if (IsAngry(animal)) return false;
            float hunterSize = hunter.BodySize > 0f ? hunter.BodySize : 1f;
            return animal.BodySize <= hunterSize * HuntingTuning.MaxPreyBodySizeRatio;
        }

        /// <summary>Currently marked angry at somebody and not yet expired — the same read
        /// <see cref="ThinkNode_ConditionalAngryAtHandler"/> makes.</summary>
        public static bool IsAngry(Pawn animal)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            return animal.mindState?.angryAt != null && Find.TickManager.TicksGame < animal.mindState.angryUntilTick;
        }

        /// <summary>
        /// The attack this hunter will actually use: its wielded weapon's ranged verb if it has one, else the
        /// first melee <see cref="Tool"/> on that weapon (RimWorld: <c>JobDriver_Hunt</c> casts through
        /// <c>Pawn.TryGetAttackVerb</c>, which falls back to melee the same way). Null when the pawn carries
        /// nothing it can attack with at all — including an empty-handed pawn, since no race in this port's
        /// content defines natural "fists"/"teeth" <see cref="Tool"/>s
        /// (<see cref="MindState.MentalState_SocialFighting"/>'s own doc records that gap and builds a
        /// synthetic fists tool in code; a hunt deliberately does not, because punching a muffalo is exactly
        /// the fight <see cref="IsSafeToHunt"/> exists to prevent).
        /// <para/>
        /// A fresh <see cref="Verb"/> per call: it carries warmup/burst/cooldown state, so a caller driving an
        /// attack must build one and keep it, not call this every tick.
        /// <para/>
        /// <b>The body of this moved.</b> The combat lane needed the same selection and extracted it to
        /// <see cref="AttackVerbUtility.TryGetAttackVerb"/> rather than write a second copy, so the hunt and
        /// the fight can never disagree about which verb a pawn swings. The hunt's one real difference is the
        /// argument below: no natural-weapon fallback, for exactly the reason this doc already gave.
        /// </summary>
        public static Verb? MakeHuntVerb(Pawn hunter) =>
            AttackVerbUtility.TryGetAttackVerb(hunter, allowNaturalWeapon: false);

        /// <summary>
        /// How close the hunter must be for <paramref name="verb"/> to reach, in cells. A ranged verb answers
        /// with its own <see cref="VerbProperties.range"/>; a melee one cannot, because
        /// <see cref="MeleeVerbUtility"/> never sets that field and it keeps its 90-cell ranged default — see
        /// <see cref="HuntingTuning.MeleeReachCells"/>.
        /// </summary>
        public static float EffectiveRange(Verb verb) => AttackVerbUtility.EffectiveRange(verb);

        /// <summary>
        /// Whether the shot/swing that resolved this very tick actually drew blood. Gated on
        /// <see cref="Verb.LastShotTick"/> because <c>LastShot</c>/<c>LastResult</c> persist after the tick
        /// they were set on — without the gate one wound would be counted on every subsequent tick of the
        /// hunt.
        /// </summary>
        public static bool WoundedThisTick(Verb verb)
        {
            if (verb == null) throw new ArgumentNullException(nameof(verb));
            if (verb.LastShotTick != Find.TickManager.TicksGame) return false;
            if (verb is Verb_LaunchProjectile ranged) return ranged.LastShot?.DamageResult?.wounded == true;
            if (verb is Verb_MeleeAttack melee) return melee.LastResult?.Outcome == MeleeAttackOutcome.Hit;
            return false;
        }

        /// <summary>
        /// RimWorld's famous hunting revenge: wounded prey turns manhunter and comes for the hunter. Rolled
        /// once per wounding hit at <see cref="HuntingTuning.RevengeChancePerWildnessOnWound"/> × the race's
        /// wildness, and expressed through the <see cref="MindState.Pawn_MindState.angryAt"/> field the
        /// animals module already uses for a failed tame's identical consequence.
        /// <para/>
        /// <b>The animal now fights back.</b> This doc used to end "the animal does not fight back": the
        /// think tree tier that reads <c>angryAt</c> (<see cref="ThinkNode_ConditionalAngryAtHandler"/>)
        /// routed it to <see cref="JobGiver_WanderAnywhere"/>, because nothing in this codebase had an attack
        /// <see cref="Job"/> or an attack <see cref="ThinkNode_JobGiver"/> to route it to. The combat lane
        /// added both, and that tier now leads with <see cref="JobGiver_Manhunter"/> — provoked prey turns
        /// and comes for the hunter with real damage behind it. The other two consequences are unchanged: the
        /// hunt breaks off (<see cref="JobDriver_Hunt"/> ends the job) and <see cref="IsSafeToHunt"/> refuses
        /// that animal until the anger expires.
        /// </summary>
        /// <returns>True if the animal was provoked by this hit.</returns>
        public static bool TryProvokeRevenge(Pawn animal, Pawn hunter)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (hunter == null) throw new ArgumentNullException(nameof(hunter));
            if (animal.Dead || animal.mindState == null) return false;

            if (!Rand.Chance(HuntingTuning.RevengeChancePerWildnessOnWound * animal.RaceProps.wildness)) return false;

            animal.mindState.angryAt = hunter;
            animal.mindState.angryUntilTick = Find.TickManager.TicksGame + HuntingTuning.RevengeAngerDurationTicks;
            return true;
        }
    }
}
