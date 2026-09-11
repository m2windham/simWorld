using SimWorld.Combat;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Conditions
{
    /// <summary>
    /// A lightning storm (RimWorld: <c>RimWorld.GameCondition_Flashstorm</c>). While it lasts it throws a
    /// strike every so often at every map it reaches; a strike lands on an unroofed cell, sets whatever is
    /// standing there alight and throws a small flame blast around it.
    ///
    /// <para/><b>What it reuses rather than rebuilds.</b> Every strike goes through the fire module's own
    /// ignition path — <see cref="FireUtility.TryStartFireIn"/> — so a flashstorm's fires are ordinary
    /// <see cref="Fire"/> Things: they grow, damage what they stand on, spread, are beaten out by
    /// <c>AI.JobDriver_BeatFire</c>, and are put out by rain through <c>Weather.WeatherManager</c>. Nothing
    /// here is a second kind of fire. The blast is likewise <see cref="GenExplosion.DoExplosion"/> with the
    /// existing <c>Flame</c> DamageDef, so a pawn caught by one takes a real burn injury and starts burning
    /// through <c>Health.DamageWorker_Flame</c> — the path that module's doc already describes.
    ///
    /// <para/><b>Translation: no storm centre.</b> RimWorld's flashstorm picks one cell when it starts and
    /// confines its strikes to a disc of <c>areaRadius</c> around it. A condition at civilization scale (see
    /// <see cref="GameConditionManager"/>) has no single cell to centre on — the cell would mean nothing on
    /// the second map it reached — so a strike lands anywhere on the map it strikes. The only consumer is
    /// what catches fire, and that cannot tell the difference.
    ///
    /// <para/><b>Translation: an unwatched storm burns nothing, and that is deliberate.</b> The condition is
    /// registered at civilization scale, so it exists, ticks and ends whether or not the god is watching —
    /// but a strike needs something to set alight, and a settlement nobody has entered has no Things at all,
    /// only a store ledger and a population count. The raid lane could resolve an unwatched raid abstractly
    /// because both sides were priced in the same <c>PawnKindDef.combatPower</c>; there is no comparable
    /// currency that would price "how much of a settlement a lightning fire takes", and inventing one would
    /// be a civilization-scale mechanic RimWorld does not have. So this stops at the honest line: the storm
    /// is real, its fires are real wherever there is a map, and a settlement nobody has opened is not
    /// silently burned by numbers nobody can check.
    ///
    /// <para/><b>Constants.</b> RimWorld's shapes, recalled rather than sourced from a decompile in this
    /// sandbox. Per CLAUDE.md the tests pin the behaviour they produce — a storm strikes many times over its
    /// life, a strike only ignites what can burn, a roof keeps the lightning out — never these literals.
    /// </summary>
    public class GameCondition_Flashstorm : GameCondition
    {
        /// <summary>Ticks between strikes. Rolled fresh after every strike, so a storm's rhythm is uneven
        /// rather than metronomic.</summary>
        public static readonly IntRange TicksBetweenStrikes = new IntRange(400, 1500);

        /// <summary>How big a fire a strike starts, before the fuel under it has any say (RimWorld's own
        /// lightning-strike ignition is a small range like this one).</summary>
        public static readonly FloatRange StrikeFireSize = new FloatRange(0.1f, 0.6f);

        /// <summary>Radius of the flame blast a strike throws around itself.</summary>
        public const float StrikeBlastRadius = 1.9f;

        /// <summary>Damage that blast deals at its centre.</summary>
        public const float StrikeBlastDamage = 10f;

        /// <summary>
        /// How many cells a strike tries before giving up on finding open sky. Lightning does not come
        /// through a roof (RimWorld picks its strike cell with <c>Standable &amp;&amp; !Roofed</c>), and a
        /// bounded search is what keeps that a cheap check rather than a scan of a fully-roofed map.
        /// </summary>
        public const int MaxStrikeCellAttempts = 10;

        /// <summary>Absolute tick the next strike is due. Advanced only in <see cref="GameConditionTick"/>,
        /// which runs once per game tick however many maps the storm reaches.</summary>
        private int nextStrikeTick;

        /// <summary>The tick the most recent strike was decided on, or -1 before the first one. This, rather
        /// than a transient flag, is what <see cref="GameConditionTickOnMap"/> tests — so the same strike
        /// lands on every map the storm reaches on that tick, and a save taken mid-storm reloads without a
        /// half-resolved strike in it.</summary>
        private int lastStrikeTick = -1;

        /// <summary>How many strikes this storm has thrown, over every map. A cheap handle for a test or an
        /// inspector to ask "did this storm actually do anything"; nothing in the simulation reads it.</summary>
        private int strikeCount;

        public int StrikeCount => strikeCount;

        public override void Init()
        {
            ScheduleNextStrike();
        }

        public override void GameConditionTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextStrikeTick) return;
            lastStrikeTick = now;
            ScheduleNextStrike();
        }

        public override void GameConditionTickOnMap(Map.Map map)
        {
            if (map == null || lastStrikeTick != Find.TickManager.TicksGame) return;
            DoStrike(map);
        }

        /// <summary>
        /// One strike on one map: hold the rain off, find open sky, ignite what is standing there, blast the
        /// rest.
        /// <para/>
        /// The rain hold is RimWorld's own pairing — a fire incident quenched by the downpour it landed in is
        /// an incident that did nothing — through <c>Weather.WeatherDecider.DisableRainFor</c>, which that
        /// class had written down as the one thing it was missing a caller for. It is refreshed at every
        /// strike, for slightly longer than the longest gap between two, so the hold lasts exactly as long as
        /// the storm does and not a tick more.
        /// </summary>
        private void DoStrike(Map.Map map)
        {
            map.weatherManager.Decider.DisableRainFor(TicksBetweenStrikes.max + 1);
            if (!TryFindStrikeCell(map, out IntVec3 cell)) return;
            strikeCount++;
            FireUtility.TryStartFireIn(cell, map, Rand.Range(StrikeFireSize));
            GenExplosion.DoExplosion(
                cell, map, StrikeBlastRadius, FireDamageDefOf.Flame, this, StrikeBlastDamage, damageFalloff: true);
        }

        /// <summary>A random unroofed cell, or false when <see cref="MaxStrikeCellAttempts"/> tries found
        /// only roof — a storm over a fully covered settlement strikes the roof and stops there.</summary>
        private static bool TryFindStrikeCell(Map.Map map, out IntVec3 cell)
        {
            for (int i = 0; i < MaxStrikeCellAttempts; i++)
            {
                var candidate = new IntVec3(Rand.Range(0, map.Size.x), 0, Rand.Range(0, map.Size.z));
                if (!GenGrid.InBounds(candidate, map)) continue;
                if (GenGrid.Roofed(candidate, map)) continue;
                cell = candidate;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        private void ScheduleNextStrike()
        {
            nextStrikeTick = Find.TickManager.TicksGame + Rand.Range(TicksBetweenStrikes);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextStrikeTick, "nextStrikeTick");
            Scribe_Values.Look(ref lastStrikeTick, "lastStrikeTick", -1);
            Scribe_Values.Look(ref strikeCount, "strikeCount");
        }
    }
}
