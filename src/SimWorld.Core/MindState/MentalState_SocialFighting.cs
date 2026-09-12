using SimWorld.AI;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;

namespace SimWorld.MindState
{
    /// <summary>
    /// A brief mutual scuffle (RimWorld: <c>Verse.AI.MentalState_SocialFighting</c>). <see
    /// cref="SocialFightUtility.TryStartSocialFight"/> starts one instance per participant — each pawn's own
    /// <see cref="MentalStateHandler"/> owns its own state, exactly like any other mental state — then links
    /// the two together via <see cref="otherPawn"/> right after both exist (neither side knows the other at
    /// construction time: <see cref="MentalStateHandler.TryStartMentalState"/> only ever passes the one pawn
    /// it belongs to).
    ///
    /// <para/><b>This class used to throw the punches itself, and that was the defect.</b> Its
    /// <see cref="MentalStateTick"/> built a <c>Verb_MeleeAttack</c> and cast it at <see cref="otherPawn"/>
    /// <i>at distance zero, every swing, with no map, no position and no reach</i>. RimWorld's mental state
    /// does nothing of the kind: it is lifecycle only, and the blows come from a job
    /// (<c>RimWorld.JobGiver_SocialFighting</c> → <c>JobDefOf.SocialFight</c>), which returns null unless
    /// <c>otherPawn.Spawned &amp;&amp; otherPawn.Map == pawn.Map</c> and then has to <i>walk across the map</i>
    /// and stand next to the other pawn before a fist can land. Measured on this port over eight in-game
    /// days, the consequence was exact and large: the citizens of a settlement <b>nobody had ever opened</b>
    /// — a settlement with no map in existence at all — beat each other from nowhere, picked up pain and
    /// injuries, and three to five of twenty-five founders were gone by day eight with no world for any of
    /// it to have happened in. See <c>docs/WORK-REGISTER.md</c> §9a.
    ///
    /// <para/>So the swinging moved to <see cref="JobGiver_SocialFighting"/> and
    /// <see cref="JobDriver_SocialFight"/>, which go through the same <c>Toils_Combat.AttackTarget</c> loop a
    /// raider's melee uses — approach, reach, swing on the verb's own cooldown — with the bare fists
    /// <see cref="AttackVerbUtility.NaturalWeaponFor"/> already builds for an unarmed pawn. That last point
    /// is RimWorld's too and matters: RimWorld picks the fight's verb out of <c>pawn.verbTracker.AllVerbs</c>,
    /// which holds a pawn's <i>natural</i> tools and never its equipped weapon, which is why a colonist with
    /// a knife on their belt still brawls with their fists.
    ///
    /// <para/>What is left here is what RimWorld's own class does: hold the link, stop when the fight can no
    /// longer be had, and leave both parties a memory of it on the way out.
    /// </summary>
    public class MentalState_SocialFighting : MentalState
    {
        /// <summary>
        /// The other participant. Null only for the instant between <c>Activator.CreateInstance</c> (inside
        /// <see cref="MentalStateHandler.TryStartMentalState"/>) and <see
        /// cref="SocialFightUtility.TryStartSocialFight"/> setting it a moment later on the same call stack —
        /// every <see cref="MentalStateTick"/> after that sees it populated. Scribe'd as a cross-reference
        /// (same shape as <see cref="Thoughts.Thought_Memory.otherPawn"/>) since two independent <see
        /// cref="Pawn_MindState"/>s point at each other rather than one owning the other.
        /// </summary>
        public Pawn? otherPawn;

        public MentalState_SocialFighting()
        {
        }

        public MentalState_SocialFighting(Pawn pawn) : base(pawn)
        {
        }

        /// <summary>
        /// The fight cannot go on (RimWorld: <c>MentalState_SocialFighting.ShouldStop</c>, which asks exactly
        /// this — the other party is gone, down or dead, or is no longer in this same fight).
        /// </summary>
        public bool ShouldStop
        {
            get
            {
                if (otherPawn == null) return true;
                if (otherPawn.Dead || otherPawn.Downed) return true;
                return !IsOtherPawnSocialFightingWithMe;
            }
        }

        private bool IsOtherPawnSocialFightingWithMe =>
            otherPawn != null
            && otherPawn.mindState.mentalStateHandler.CurState is MentalState_SocialFighting otherFight
            && ReferenceEquals(otherFight.otherPawn, pawn);

        public override void MentalStateTick()
        {
            if (ShouldStop)
            {
                RecoverFromState();
                return;
            }
            base.MentalStateTick();
        }

        /// <summary>
        /// Ending (RimWorld: <c>MentalState_SocialFighting.PostEnd</c>, three things, all of them ported).
        /// The running fight job is dropped — a pawn who has stopped fighting must not keep swinging;
        /// the other side is stood down with this one rather than each noticing independently a tick later;
        /// and <b>both parties come away with a memory of the fight, half the time a good one</b>. That last
        /// part was missing entirely, and its absence is one of the reasons brawling fed itself here: a
        /// scuffle could only ever cost mood (<c>Insulted</c> on the way in, injuries and witnesses on the
        /// way out) and never clear the air, which is exactly what RimWorld's 50/50
        /// <c>HadCatharticFight</c>/<c>HadAngeringFight</c> pair is for.
        /// </summary>
        public override void PostEnd()
        {
            base.PostEnd();

            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);

            if (IsOtherPawnSocialFightingWithMe)
            {
                otherPawn!.mindState.mentalStateHandler.CurState!.RecoverFromState();
            }

            if (!pawn.Dead && otherPawn != null && !otherPawn.Dead && pawn.needs.mood != null)
            {
                Thoughts.ThoughtDef memory = Rand.Value < 0.5f
                    ? SocialFightThoughtDefOf.HadCatharticFight
                    : SocialFightThoughtDefOf.HadAngeringFight;
                pawn.needs.mood.thoughts.memories.TryGainMemory(memory, otherPawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Pawn? o = otherPawn;
            Scribe_References.Look(ref o, "otherPawn");
            otherPawn = o;
        }
    }
}
