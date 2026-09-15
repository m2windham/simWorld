using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>
    /// <b>Sleep for a citizen with no map to lie down on</b> — the third instance of the seam
    /// <c>Economy.SettlementLarder</c> opened for food and <see cref="AbstractRecreation"/> mirrored for
    /// recreation, one need over again.
    ///
    /// <para/><b>The defect this closes, and why it was the same defect twice over.</b> Sleep in this port is
    /// reached exactly one way: the humanlike think tree's rest tier
    /// (<c>AI.ThinkNode_ConditionalTired</c>) hands a tired pawn to <c>AI.JobGiver_GetRest</c>, which
    /// <b>returns null the instant <c>pawn.Map</c> is null</b>, and the job it would have given
    /// (<c>AI.JobDriver_LayDown</c>) walks to a cell on a map and sets <c>Pawn.Asleep</c> there. So a citizen
    /// of a settlement nobody has opened has nowhere to lie down, <see cref="Need_Rest.Resting"/> is never
    /// true for them, and <see cref="Need_Rest"/> can only ever fall. Measured independently across three
    /// seeds on a <c>TribalStart</c> settlement of twenty-five founders nobody opened, eight in-game days:
    /// <b>mean rest 0.95 → 0.00 by day two, and it stayed at 0.00 for the rest of the run</b>, with
    /// <c>Tired</c> at −20 mood points a head — the largest single term left in an unwatched settlement's
    /// mood ledger once food and recreation had both been closed (<c>docs/WORK-REGISTER.md</c> §10).
    ///
    /// <para/>That is the third time one shape has produced the same regression: an entire subsystem built
    /// map-side, meeting citizens who tick (<c>Sim.CitizenTickRegistry</c>) without a map to act in. Food
    /// starved them, recreation pinned them at its worst stage, and sleep pinned them at exhaustion. This
    /// closes the third in the second's shape deliberately rather than inventing a third one.
    ///
    /// <para/><b>The predicate is <c>!Spawned</c>, and deliberately not a tier.</b>
    /// <c>Economy.SettlementLarder</c> argues this at length and every word of it carries here: what an
    /// abstract citizen lacks is not fidelity, it is <i>a world to act in</i>. <c>JobGiver_GetRest</c>'s first
    /// line is <c>pawn.Map == null</c>, so a citizen who is not spawned cannot reach a bed — or the ground —
    /// by any map-side route no matter what tier they hold. Reading the tier instead would be wrong in both
    /// directions: a <c>Full</c>-tier citizen of an unopened settlement has no map (the case this exists for,
    /// and the case <c>Sim.Game.NewGame</c> produces on tick one), and a demoted citizen briefly still
    /// standing on a generated interior does have one. <see cref="Pawns.Pawn.Spawned"/> is exactly the
    /// question "is there a map under this pawn", which is exactly what decides whether the map path can
    /// serve them.
    ///
    /// <para/><b>Nothing here invents rest.</b> The gate is the think tree's own rest gate
    /// (<c>ThinkNode_ConditionalTired</c>: any category but <see cref="RestCategory.Rested"/>), the recovery
    /// is credited through <see cref="Need_Rest.RestGainPerTick"/> — the same product of
    /// <see cref="Need_Rest.BaseRestGainPerTick"/>, <see cref="Need_Rest.lastRestEffectiveness"/> and
    /// <c>Pawn.RestRateMultiplier</c> that <see cref="Need_Rest.NeedInterval"/> pays a sleeping pawn on a map
    /// — and the fall it is repaying is untouched. A citizen whose heart or lungs are damaged rests slower
    /// here by exactly the factor they would rest slower by on a map.
    ///
    /// <para/><b>A whole night at once, not a trickle — and why that is the honest shape rather than a
    /// shortcut.</b> Sleep on a map arrives in sessions: the tier fires below the tired threshold and the
    /// driver then pays <see cref="Need_Rest.RestGainPerTick"/> every tick until the need is full. An
    /// abstract citizen's sleep has to arrive the same way or the two halves of the game would sit at
    /// different levels of the same need for no reason but which one has a map. What it does not spend is the
    /// <i>time</i>: an abstract citizen has no clock to spend it on, which is what spec §11.3 means by a tier
    /// whose full state exists but only advances on the coarse buckets. Amplitude and period match the map
    /// path; only the lying down is missing. <see cref="AbstractRecreation"/> says the same of recreation and
    /// for the same reason.
    ///
    /// <para/><b>Recorded translation: the night is a fixed span, and it is slept on bare ground.</b> Two
    /// decisions here have no RimWorld equivalent, because RimWorld has no unwatched settlements — it simply
    /// stops simulating a pawn who leaves the map — so per CLAUDE.md they are recorded rather than improvised:
    /// <list type="number">
    /// <item><b>How long a night is.</b> The map path's sleep ends at a level (<c>JobDriver_LayDown</c> runs
    /// until <c>CurLevel >= 0.999</c>), which an abstract citizen cannot express because it spends no time.
    /// <see cref="SleepTicks"/> is that same night measured as a span instead — the ticks a map-side sleep
    /// takes to climb from the rest tier's own threshold to full on bare ground — so it is read off the two
    /// constants this port already committed to (<see cref="Need_Rest.ThreshTired"/> and
    /// <see cref="Need_Rest.BaseRestGainPerTick"/>) rather than chosen. The consequence is worth stating: a
    /// citizen who went to sleep only just below the threshold wakes full and the remainder is lost, exactly
    /// as a map-side sleeper's is, while one who was run down to exhaustion gets one night's worth and no
    /// more, and needs a second night to come all the way back. <c>AbstractRestTests</c> pins that as
    /// behaviour rather than pinning the literal.</item>
    /// <item><b>There is no bed.</b> A bed is a <c>Thing</c> on a map (<c>AI.RestUtility.FindBedFor</c>
    /// returns null with no map, and <c>Settlement.Stores</c> holds counted goods, not built furniture), so an
    /// abstract citizen sleeps rough at <see cref="Need_Rest.lastRestEffectiveness"/>'s own documented
    /// ground default of 1 — the same thing <c>JobGiver_GetRest</c> does for a pawn on a map with no bed it
    /// can reach. That field is therefore <i>reset</i> here rather than merely read: it is the last bed a
    /// citizen slept in, and a citizen demoted off an interior where they had a real bed would otherwise keep
    /// drawing that bed's multiplier for ever in a settlement nobody can see. The asymmetry is the honest one
    /// and it runs the other way from <see cref="AbstractRecreation"/>'s: an unwatched citizen always finds
    /// their night, but always sleeps on the ground, so a watched settlement that has built beds rests
    /// <i>better</i> than an unwatched one rather than worse.</item>
    /// </list>
    ///
    /// <para/><b>No roll, ever.</b> This runs inside the tick loop, where a single draw would shift every
    /// subsequent roll in the game (<c>Economy.SettlementLarder</c>, <c>Economy.SettlementStockInitiative</c>
    /// and <see cref="AbstractRecreation"/> all say the same thing about themselves). There is nothing to
    /// draw: whether a night is due is a threshold, and how much it repays is arithmetic over the pawn's own
    /// rate. <c>AbstractRestTests</c> pins it on <c>Rand.Current.Iterations</c>.
    ///
    /// <para/><b>No state of its own.</b> Everything is re-derived from the need and the pawn on each pass,
    /// so there is nothing here to Scribe; what little state a night touches
    /// (<see cref="Need_Rest.lastRestEffectiveness"/>) <see cref="Need_Rest.ExposeData"/> already writes.
    /// <b>Deliberately not a sleeping flag</b>: <c>Pawn.Asleep</c> is the map path's own state, cleared only
    /// by <c>JobDriver_LayDown.Notify_Ending</c> and by damage, and an abstract citizen carrying it would
    /// arrive on a generated interior (<c>World.Settlement.SyncCitizenSpawns</c>) asleep with no job to wake
    /// them — <c>ThinkNode_ConditionalTired</c> refuses a pawn who is already asleep, so they would work and
    /// wander with <see cref="Need_Rest.Resting"/> true and their rest rising for ever. A session that spends
    /// no time needs no flag, and cannot leave one behind.
    ///
    /// <para/><b>Cost.</b> Two field reads per citizen per need interval (<c>Spawned</c>, then the need's own
    /// category) and nothing else on the overwhelming majority of passes; the arithmetic is paid once per
    /// citizen per night, which is once per ~45,000 ticks. It is called from
    /// <see cref="Need_Rest.NeedInterval"/> and its bulk twin rather than from a ticker of its own, because
    /// that is already the one place per pawn per interval where rest is thought about.
    /// </summary>
    public static class AbstractRest
    {
        /// <summary>
        /// How long a night is, in ticks: the span a map-side sleep takes to climb from the rest tier's own
        /// threshold (<see cref="Need_Rest.ThreshTired"/>, the level below which
        /// <c>AI.ThinkNode_ConditionalTired</c> sends a pawn to bed) all the way to full, at
        /// <see cref="Need_Rest.BaseRestGainPerTick"/> — bare ground, an unimpaired pawn. Derived from the
        /// two constants rather than chosen; see the class doc's recorded translation. Lands at about 7.6
        /// in-game hours, which is a night.
        /// </summary>
        public const float SleepTicks = (1f - Need_Rest.ThreshTired) / Need_Rest.BaseRestGainPerTick;

        /// <summary>
        /// One pass of off-map sleep, called from <see cref="Need_Rest.NeedInterval"/> and from its bulk
        /// twin. It takes no elapsed span and needs none, for the reason
        /// <see cref="AbstractRecreation.JoyInterval"/> gives: sleep arrives in whole sessions rather than as
        /// a per-tick rate, and the threshold below is what decides whether another one is due — so the Full
        /// tier's 150-tick call and the Interval tier's 2,000-tick call reach the same answer from the same
        /// state, which is exactly what <see cref="Need.NeedIntervalBulk"/>'s contract asks of a rate that is
        /// constant across the span.
        /// </summary>
        public static void RestInterval(Pawn pawn, Need_Rest rest)
        {
            if (pawn == null || rest == null) return;

            // A citizen standing on a live map walks to a bed and sleeps in it; this never touches them.
            // Same predicate, same reason, as Economy.SettlementLarder's and AbstractRecreation's.
            if (pawn.Spawned) return;

            // Suspended or otherwise held: the need is not falling either, so nothing is owed.
            if (rest.IsFrozen) return;

            // Already asleep, and therefore already gaining through Need_Rest itself. In a running game this
            // cannot be an abstract citizen (Pawn.Asleep is the map path's flag, and Pawn_TierTracker.Demote
            // ends the sleeping job — clearing it — on the way off a map), but paying a sleeper twice is the
            // kind of thing that would go unnoticed, so it is refused rather than assumed impossible.
            if (rest.Resting) return;

            // The think tree's own rest gate (AI.ThinkNode_ConditionalTired). Above it a citizen is awake and
            // doing something else, so an off-map one is too — without this an abstract citizen would sit
            // permanently full and a watched settlement would look worse than an unwatched one for no reason
            // but the map.
            if (rest.CurCategory == RestCategory.Rested) return;

            // Bare ground, and reset rather than read: see the class doc. This is the same write
            // AI.JobDriver_LayDown makes at the head of every sleep it starts without a bed.
            rest.lastRestEffectiveness = 1f;
            rest.CurLevel += rest.RestGainPerTick * SleepTicks;
        }
    }
}
