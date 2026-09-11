using System;
using SimWorld.God;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// <b>Who fights — this port's replacement for the draft.</b>
    /// <para/>
    /// In RimWorld a colonist fights when the player drafts them, or when a hostile is close enough to hit
    /// them (<c>JobGiver_ReactToCloseMeleeThreat</c>). There is no draft here: spec §10 is explicit that the
    /// god "issues edicts and goals that enter the think tree above routine work, instead of drafting
    /// individuals", and <see cref="EdictDef"/> is the shape that already carries a civilization-scale
    /// standing policy down to one pawn. So the draft splits into a floor everybody gets for free and a
    /// standing order that raises it:
    /// <list type="number">
    /// <item><b>Anyone armed defends.</b> A pawn carrying a weapon engages any hostile within
    /// <see cref="CombatAITuning.TargetAcquireRadius"/>. This is symmetric and needs no policy on either
    /// side: it is what makes a raider (who is always armed — its <see cref="PawnKindDef.weaponTags"/> see to
    /// that) walk in and attack, and equally what makes an armed citizen meet it. Nobody has to be told.</item>
    /// <item><b>Anyone at all fights back at arm's length.</b> An unarmed pawn still swings at a hostile
    /// inside <see cref="CombatAITuning.MeleeReachCells"/> — RimWorld's "or when a hostile is adjacent" half
    /// of the draft rule, kept 1:1. Cornered is cornered.</item>
    /// <item><b>The <c>TakeUpArms</c> edict raises an unarmed citizen to the first rule.</b> While it is
    /// active and <see cref="EdictWorker.AppliesTo"/> reaches them, an unarmed citizen goes looking for the
    /// enemy at full acquire radius instead of waiting to be cornered. That is the decision the god actually
    /// gets to make, and it costs public mood like every other edict.</item>
    /// </list>
    /// <b>What this deliberately does not do: nobody flees.</b> There is no humanlike counterpart to
    /// <see cref="JobGiver_AnimalFlee"/> here, and none of the twelve shipped <c>MentalStateDef</c>s is a
    /// panic-flight. A citizen who will not fight simply keeps working — which is, as it happens, exactly
    /// what an undrafted RimWorld colonist does during a raid, so the visible behaviour is right even though
    /// the reason is a gap. Adding "and the rest run" means a flee job giver, a fear model and somewhere to
    /// run to; it is recorded here as absent rather than improvised.
    /// <para/>
    /// <b>Consequence worth knowing.</b> No <see cref="PawnKindDef"/> this port ships gives
    /// <c>Colonist</c>/<c>Villager</c> any weapon tags (<see cref="HuntUtility.HasHuntingWeapon"/> records
    /// the same thing for hunting), so in a generated settlement <i>every</i> citizen is unarmed and rules 2
    /// and 3 are the only ones that fire for them. A raid on a civilization that has not issued
    /// <c>TakeUpArms</c> is met by people who fight only once the raiders are on top of them. That is a
    /// content gap making itself felt through working mechanics, not a mechanics gap.
    /// </summary>
    public static class CombatPostureUtility
    {
        /// <summary>
        /// Able to fight at all right now. Violence being disabled is the one hard refusal (RimWorld:
        /// <c>WorkTags.Violent</c>, which <see cref="WorkGiver_Hunt"/> already checks the same way) — no
        /// edict overrides a pacifist, exactly as no edict turns on a work type a pawn is disabled from
        /// (<see cref="EdictWorker.AppliesTo"/>'s own rule).
        /// </summary>
        public static bool CanFight(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (pawn.Map == null || AttackTargetsUtility.ThreatDisabled(pawn)) return false;
            return !pawn.WorkTagIsDisabled(WorkTags.Violent);
        }

        /// <summary>
        /// How far this pawn will go looking for an enemy, in cells — the numeric form of the three rules in
        /// this class's own doc. An animal always uses the full radius: a tamed one defends its faction's
        /// settlement and a manhunter chases its grudge, and neither has a weapon to be judged on.
        /// </summary>
        public static float TargetAcquireRadiusFor(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawn.RaceProps.Humanlike) return CombatAITuning.TargetAcquireRadius;
            if (AttackVerbUtility.HasEquippedWeapon(pawn) || IsMustered(pawn)) return CombatAITuning.TargetAcquireRadius;
            return CombatAITuning.MeleeReachCells;
        }

        /// <summary>
        /// Whether a standing edict has called this citizen to arms. Reads <see cref="GodManager.IsActive"/>
        /// live and holds no state of its own, the same way <see cref="JobGiver_Edicts"/> and
        /// <see cref="ThoughtWorker_UnderEdict"/> do — so retiring the edict leaves no trace here either: the
        /// very next think-tree pass simply stops finding it active, and every citizen is back to the floor.
        /// </summary>
        public static bool IsMustered(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            EdictDef takeUpArms = CombatAIDefOf.TakeUpArms;
            return takeUpArms != null && Find.God.IsActive(takeUpArms) && takeUpArms.Worker.AppliesTo(pawn);
        }
    }
}
