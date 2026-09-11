using System.Collections.Generic;

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
    /// two hooks take no pawn: <see cref="StoryWatcher_Adaptation.Notify_ColonistDied"/> is raised by
    /// <see cref="SettlementRaidResolver"/>, which has already decided who counts (only named citizens of the
    /// settlement it just resolved a raid against), so the filtering lives at that call site. Downing has no
    /// such call site — <c>Pawn_HealthTracker.MakeDowned</c> is the one funnel every downing in the port goes
    /// through, and it runs for raiders and for animals just as much as for citizens. Somebody therefore has
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
        /// Tells the storyteller's adaptation watcher that a citizen went down, if this pawn is one. Returns
        /// whether the hook was actually raised, so a caller (and a test) can tell "counted" from "an
        /// attacker fell over".
        ///
        /// <para/><b>Charged once per downing, and never for a death.</b> The caller,
        /// <c>Pawn_HealthTracker.MakeDowned</c>, runs only on the transition from mobile to down (its caller
        /// guards with <c>if (!Downed)</c>), so a pawn who stays down through further damage is charged once;
        /// and <c>Pawn_HealthTracker.Kill</c> sets the dead state directly without ever passing through
        /// <c>MakeDowned</c>, so dying never raises this. A citizen who is downed and then killed therefore
        /// moves the curve once here, and again only if the separate, raid-scoped
        /// <see cref="StoryWatcher_Adaptation.Notify_ColonistDied"/> call site also fires for them — two
        /// events at two times, which is what both RimWorld and this port charge separately.
        /// </summary>
        public static bool Notify_PawnDowned(Pawn pawn)
        {
            if (!IsCivilizationMember(pawn)) return false;
            Find.Storyteller.adaptation.Notify_ColonistDowned();
            return true;
        }

        /// <summary>Humanlike, alive, and on the roster of a civilization the storyteller is telling a story
        /// about — see the class doc for why membership is asked this way and not of <c>Pawn.faction</c>.</summary>
        private static bool IsCivilizationMember(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike) return false;

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
