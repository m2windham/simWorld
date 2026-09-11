using System;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>How badly a citizen wants open sky (RimWorld: <c>RimWorld.OutdoorsCategory</c>). Read off the
    /// need's level, so a citizen who has just stepped indoors is still "Free" until the need has had time to
    /// fall.</summary>
    public enum OutdoorsCategory
    {
        Entombed,
        Trapped,
        CabinFeverSevere,
        CabinFeverLight,
        NeedFreshAir,
        Free,
    }

    /// <summary>
    /// Time under open sky (RimWorld: <c>RimWorld.Need_Outdoors</c>). Rises fast outdoors, falls slowly under
    /// a roof, and how fast depends on both the roof overhead and whether the cell is still open to the
    /// weather — a citizen standing under a thin constructed roof in an otherwise open yard is better off
    /// than one sealed in a mountain.
    ///
    /// <para/><b>Why this class exists.</b> Outdoors shipped as a <see cref="Need_Environment"/>: a seeker
    /// chasing whatever <see cref="MapEnvironmentSampler"/> answered for it, and the sampler answers null for
    /// everything but Beauty. Null means "use the Def's base level", the need starts at that same base level,
    /// and a seeker already sitting on its target does nothing — so Outdoors was frozen at 0.5 for every
    /// citizen in every game since it was added. RimWorld's Need_Outdoors is not a seeker at all; it is a
    /// plain <see cref="Need"/> with its own interval, and everything it reads (a roof grid and whether the
    /// cell uses outdoor temperature) this port already has. This is that class, ported.
    ///
    /// <para/><b>The deltas are per-day rates.</b> RimWorld's constants below are multiplied by
    /// <c>0.0025f</c> before they are applied, and <c>0.0025 == 1/400 == one 150-tick interval as a fraction
    /// of a day</c> — so "8" means the gauge fills from empty in an eighth of a day outdoors under open sky,
    /// and "-0.32" means a thin-roofed indoor life drains it in about three days.
    ///
    /// <para/><b>Cost, measured.</b> A citizen who is on a map now pays one roof-grid index and one
    /// <see cref="RoomTracker.RoomAt"/> dictionary lookup per <see cref="Need.IntervalTicks"/>, where as a
    /// frozen seeker they paid nothing. Timed in a Release build over 200,000 calls after a warm-up — the
    /// method <c>docs/perf/baseline.md</c> uses — that is <b>0.085 µs</b> per call indoors and 0.077 µs
    /// outdoors, i.e. <b>0.034 ms per citizen-day</b> and <b>0.017 s per in-game day</b> for a full
    /// <see cref="Pawns.TieringTuning.FullTierBudget"/> of 500. Against the 1.71 ms per citizen-day all eight
    /// needs cost together at N=500 (baseline §2's measurement, re-run at 500), this need is ~2% of the needs
    /// line and ~0.2% of a whole citizen; beauty's sample, for scale, measures 0.5-3.5 µs on the same box. An
    /// Interval-tier citizen pays it 30 times a day rather than 400, through
    /// <see cref="NeedIntervalBulk"/>.
    ///
    /// <para/><b>Two inputs RimWorld has and this port does not</b>, both recorded rather than invented:
    /// <list type="bullet">
    /// <item><description><c>pawn.InBed()</c> weakens a negative delta to a fifth. There is no bed-occupancy
    /// query here (<c>Need_Rest</c> learns about beds by having the sleeping job push
    /// <c>lastRestEffectiveness</c> at it rather than by asking), so an indoor citizen loses outdoors need at
    /// the full rate while asleep. It lands with whatever gives the core a "which bed is this pawn in"
    /// question to ask.</description></item>
    /// <item><description><c>pawn.needs.PrefersIndoors</c> (RimWorld's Undergrounder trait and the Biotech
    /// indoor-dweller gene) pins the need at 1 and hides it. Neither trait nor gene is ported, so nobody here
    /// prefers indoors and the need always runs.</description></item>
    /// </list>
    /// </summary>
    public class Need_Outdoors : Need
    {
        /// <summary>Level change per in-game day for each of RimWorld's six situations
        /// (<c>Need_Outdoors.Delta_*</c>), before <see cref="PerIntervalFactor"/> turns them into a slice.</summary>
        public const float Delta_IndoorsThickRoof = -0.45f;
        public const float Delta_OutdoorsThickRoof = -0.4f;
        public const float Delta_IndoorsThinRoof = -0.32f;
        public const float Delta_OutdoorsThinRoof = 1f;
        public const float Delta_IndoorsNoRoof = 5f;
        public const float Delta_OutdoorsNoRoof = 8f;

        /// <summary>A thin roof over an indoor cell never drives the need below this (RimWorld:
        /// <c>Minimum_IndoorsThinRoof</c>) — ordinary indoor life is miserable, not entombing. Thick roof is
        /// the one situation with no floor at all, which is what makes a mountain base different in kind.</summary>
        public const float Minimum_IndoorsThinRoof = 0.2f;

        /// <summary>RimWorld's <c>num *= 0.0025f</c>: one <see cref="Need.IntervalTicks"/> slice as a fraction
        /// of a day (1/400).</summary>
        public const float PerIntervalFactor = 0.0025f;

        public Need_Outdoors(Pawn pawn) : base(pawn)
        {
        }

        /// <summary>RimWorld: <c>Need_Outdoors.CurCategory</c>. The bands are RimWorld's own.</summary>
        public OutdoorsCategory CurCategory
        {
            get
            {
                float level = CurLevel;
                if (level > 0.8f) return OutdoorsCategory.Free;
                if (level > 0.6f) return OutdoorsCategory.NeedFreshAir;
                if (level > 0.4f) return OutdoorsCategory.CabinFeverLight;
                if (level >= 0.2f) return OutdoorsCategory.CabinFeverSevere;
                if (level > 0.05f) return OutdoorsCategory.Trapped;
                return OutdoorsCategory.Entombed;
            }
        }

        /// <summary>A citizen starts their life having been outside (RimWorld: <c>SetInitialLevel</c> sets 1,
        /// not the Def's <c>baseLevel</c>).</summary>
        public override void SetInitialLevel()
        {
            CurLevel = 1f;
        }

        /// <summary>Level change per day in the citizen's current situation; positive is fresh air. Public so
        /// a test can pin the ordering of the six situations without simulating each one for a day.</summary>
        public float DeltaPerDay
        {
            get
            {
                float perDay;
                float minimum;
                Situation(out perDay, out minimum);
                return perDay;
            }
        }

        /// <summary>The citizen's current situation: how fast the need moves per day, and how low that
        /// movement may take it. Only an indoor thick roof — a mountain — has no floor.</summary>
        private void Situation(out float perDay, out float minimum)
        {
            minimum = Minimum_IndoorsThinRoof;
            Map.Map? map = pawn.Map;
            RoofDef? roof = map != null ? GenGrid.GetRoof(pawn.Position, map) : null;

            // Off-map counts as outdoor air in RimWorld too (`!pawn.Spawned || ...UsesOutdoorTemperature`),
            // which is what keeps a caravanning citizen from developing cabin fever in transit.
            if (UsesOutdoorTemperature(map))
            {
                if (roof == null) perDay = Delta_OutdoorsNoRoof;
                else perDay = roof.isThickRoof ? Delta_OutdoorsThickRoof : Delta_OutdoorsThinRoof;
                return;
            }

            if (roof == null)
            {
                perDay = Delta_IndoorsNoRoof;
            }
            else if (!roof.isThickRoof)
            {
                perDay = Delta_IndoorsThinRoof;
            }
            else
            {
                perDay = Delta_IndoorsThickRoof;
                minimum = 0f;
            }
        }

        public override void NeedInterval()
        {
            Advance(IntervalTicks);
        }

        /// <summary>O(1) bulk equivalent (see <see cref="Need.NeedIntervalBulk"/>): the rate depends on where
        /// the citizen is standing and not at all on the need's own level, so holding it constant across the
        /// span is exact for a citizen who has not moved and the same approximation as everyone else's for one
        /// who has. Scaling by the raw tick count rather than by whole slices keeps the part-interval
        /// remainder, which is the base class's first rule for this method.</summary>
        public override void NeedIntervalBulk(int elapsedTicks)
        {
            Advance(elapsedTicks);
        }

        private void Advance(int ticks)
        {
            if (ticks <= 0 || IsFrozen) return;

            float perDay;
            float minimum;
            Situation(out perDay, out minimum);
            float change = perDay * PerIntervalFactor * (ticks / (float)IntervalTicks);

            if (change < 0f)
            {
                // Never past the floor, and never *up* to it either: a citizen already below the floor when
                // they walked in stays where they are rather than being handed fresh air by a ceiling.
                CurLevel = Math.Min(CurLevel, Math.Max(CurLevel + change, minimum));
            }
            else
            {
                CurLevel = Math.Min(CurLevel + change, MaxLevel);
            }
        }

        /// <summary>
        /// RimWorld's <c>IntVec3.UsesOutdoorTemperature(map)</c>: the cell is open to the weather — it is not
        /// in any enclosed room, or the room it is in reaches the map edge or has a cell with no roof over it.
        /// <see cref="Room.TouchesOutside"/> is this port's name for <c>Room.UsesOutdoorTemperature</c> and
        /// says so at its own declaration.
        /// <para/>
        /// Rooms are lazy: <see cref="RoomTracker.RoomAt"/> answers null for every cell until the map has
        /// ticked once. Null reads as outdoors, which is both the right answer for an un-flooded map (nothing
        /// has been built) and the safe one (a citizen gains fresh air rather than silently suffocating).
        /// </summary>
        private bool UsesOutdoorTemperature(Map.Map? map)
        {
            if (map == null) return true;
            Room? room = map.roomTracker.RoomAt(pawn.Position);
            return room == null || room.TouchesOutside;
        }
    }
}
