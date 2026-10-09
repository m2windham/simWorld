using System.Collections.Generic;

using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// The bridge from a thing happening to a pawn to the storyteller hearing about it (RimWorld:
    /// <c>Verse.Storyteller.Notify_PawnEvent</c>, which forwards to <c>StoryWatcher_Adaptation</c>). Narrowed
    /// to the one event this port raises from a pawn's own state machine — being downed.
    ///
    /// <para/><b>Why this is a class and not a line in <see cref="StoryWatcher_Adaptation"/>.</b> The watcher's
    /// two hooks take no pawn, so whoever raises them has to decide who counts first.
    /// <c>Pawn_HealthTracker.MakeDowned</c> is the one funnel every downing in the port goes through (and
    /// <c>Pawn_HealthTracker.Kill</c> the one every death does, see <see cref="StorytellerDeathEvents"/>),
    /// and both run for raiders and for animals just as much as for citizens. Somebody therefore has
    /// to answer "is this one of ours" before the watcher is touched, and RimWorld answers it in exactly this
    /// position (its <c>Notify_PawnEvent</c> returns early for a non-player or non-humanlike pawn). Getting
    /// it wrong is not a small bug: an unguarded hook would ease the storyteller every time the colony
    /// knocked down an attacker, which is backwards.
    ///
    /// <para/><b>Who counts as ours.</b> Not <c>Pawn.faction</c>, and the reason has changed since this was
    /// written. It used to be that a faction test would exempt precisely the people the curve is about, because
    /// a settlement's citizens were generated without a faction at all; they carry their settlement's faction
    /// now (<c>World.SettlementFounder</c>), so that argument is gone. The roster test stays on its own
    /// merits: a faction can own several civilizations' worth of settlements and the storyteller tells a story
    /// about the ones registered with it, so roster membership is the narrower and more honest question.
    /// The civilization's
    /// people are whoever <see cref="IIncidentTarget.PlayerPawnsForStoryteller"/> reports across the targets
    /// registered with the storyteller — the identical roster the threat curve itself reads
    /// (<see cref="StorytellerUtility.DefaultThreatPointsNow"/>), so "whose downing moves adaptation" and
    /// "whose presence sets the threat level" can never disagree.
    ///
    /// <para/><b>Cost.</b> One walk of the civilization's live roster per downing. A downing is a rare,
    /// notable event and that roster is the attended (Full/Interval) slice by
    /// <c>World.Settlement.Citizens</c>' own design, never a settlement's whole population — so this is not
    /// on any hot path, and it never materialises a Statistical citizen.
    /// </summary>
    public static class StorytellerPawnEvents
    {
        /// <summary>
        /// Tells the storyteller's adaptation watcher that a citizen was put down by violence, if this pawn is
        /// one of ours. Returns whether the hook was actually raised, so a caller (and a test) can tell
        /// "counted" from "an attacker fell over" or "collapsed from bleeding".
        ///
        /// <para/><b>RimWorld's rule, in full.</b> <c>StoryWatcher_Adaptation.Notify_PawnEvent</c> charges a
        /// downing only when there is a <paramref name="dinfo"/> and its damage is
        /// <see cref="DamageDef.externalViolence"/> for the pawn — somebody hit them, as opposed to blood loss,
        /// hunger, disease or a life stage taking their legs. And it does not charge at once: the pawn is
        /// queued until the end of the tick, and a death in that tick withdraws the entry
        /// (<see cref="StoryWatcher_Adaptation.Notify_ColonistDied"/>), so the person downed and killed by the
        /// same explosion pays for the death alone. This port used to charge every downing but a life stage's,
        /// at once — so a citizen who collapsed from hunger moved the curve like one cut down by a raider,
        /// and one killed outright paid twice.
        ///
        /// <para/><b>Charged once per downing, and never for a death.</b> The caller,
        /// <c>Pawn_HealthTracker.MakeDowned</c>, runs only on the transition from mobile to down (its caller
        /// guards with <c>if (!Downed)</c>), so a pawn who stays down through further damage is charged once;
        /// and <c>Pawn_HealthTracker.Kill</c> sets the dead state directly without ever passing through
        /// <c>MakeDowned</c>, so dying never raises this. A citizen who is downed by a blow and bleeds out
        /// later moves the curve once here and once on the death side
        /// (<see cref="StorytellerDeathEvents.Notify_PawnDied"/>, which charges every death of ours): two
        /// events at two times, which RimWorld charges separately too.
        /// </summary>
        public static bool Notify_PawnDowned(Pawn pawn, DamageInfo? dinfo)
        {
            if (!IsCivilizationMember(pawn)) return false;
            if (dinfo == null || !dinfo.Def.externalViolence) return false;
            Find.Storyteller.adaptation.Notify_ColonistDowned(pawn);
            return true;
        }

        /// <summary>
        /// How many humanlike people of the civilization are alive: the population RimWorld's adaptation
        /// losses are read against (<c>PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonists</c>),
        /// taken from the same roster the threat curve and <see cref="IsCivilizationMember"/> use. Downed
        /// people count; the dead do not.
        /// </summary>
        public static int CivilizationPopulation()
        {
            int population = 0;
            IReadOnlyList<IIncidentTarget> targets = Find.Storyteller.AllIncidentTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                foreach (Pawn member in targets[i].PlayerPawnsForStoryteller)
                {
                    if (!member.Dead && member.RaceProps.Humanlike) population++;
                }
            }
            return population;
        }

        /// <summary>
        /// Humanlike and on the roster of a civilization the storyteller is telling a story about — see the
        /// class doc for why membership is asked this way and not of <c>Pawn.faction</c>.
        ///
        /// <para/><paramref name="allowDead"/> exists for the death side (<see cref="StorytellerDeathEvents"/>):
        /// <c>Pawn_HealthTracker.Kill</c> sets the dead state before it notifies anybody, so a hook raised
        /// from a death is always looking at a pawn who is already dead. The roster still holds them at that
        /// instant — a settlement drops its dead in a later sweep — which is exactly why the question can
        /// still be answered. Downing keeps the alive test, where it is a real guard.
        /// </summary>
        public static bool IsCivilizationMember(Pawn? pawn, bool allowDead = false)
        {
            if (pawn == null || (pawn.Dead && !allowDead) || !pawn.RaceProps.Humanlike) return false;

            IReadOnlyList<IIncidentTarget> targets = Find.Storyteller.AllIncidentTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                foreach (Pawn member in targets[i].PlayerPawnsForStoryteller)
                {
                    if (ReferenceEquals(member, pawn)) return true;
                }
            }
            return false;
        }
    }
}
