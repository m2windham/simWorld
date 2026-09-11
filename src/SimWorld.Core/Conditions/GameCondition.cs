using System;
using SimWorld.Sim;

namespace SimWorld.Conditions
{
    /// <summary>
    /// One condition currently in force (RimWorld: <c>RimWorld.GameCondition</c>): it starts, it offsets what
    /// it offsets for a while, and it ends. A condition is owned by exactly one
    /// <see cref="GameConditionManager"/> and never moves between them.
    ///
    /// <para/><b>Two tick hooks, not one — and why this port pulls where RimWorld pushes.</b> RimWorld gives a
    /// condition a single <c>GameConditionTick</c> and lets it reach out through <c>GameCondition.AffectedMaps</c>
    /// (its owner map, or <c>Find.Maps</c> for a world-scale condition) whenever it needs to touch a map. That
    /// needs a live registry of open maps. This port has one — <c>Sim.Game.Maps</c> — but it exists only while
    /// a <c>Game</c> does, and a condition is not allowed to quietly stop working when it does not.
    /// So the direction is reversed: <see cref="GameConditionTick"/> runs once per game tick from the owning
    /// manager and owns everything scope-wide (the clock, expiry, scheduling), and
    /// <see cref="GameConditionTickOnMap"/> is called by each map's own manager, for that map, walking up its
    /// parent chain — so a map pulls the conditions that reach it instead of the condition pushing at a list
    /// of maps. A map already ticks and already walks that chain for
    /// <see cref="GameConditionManager.AggregateTemperatureOffset"/>, so this costs nothing new and needs no
    /// registry at all.
    ///
    /// <para/>The consequence to keep in mind when writing one: anything that must happen exactly once per
    /// tick (a countdown, a random draw) belongs in <see cref="GameConditionTick"/>, because
    /// <see cref="GameConditionTickOnMap"/> runs once per open map. <see cref="GameCondition_Flashstorm"/> is
    /// the worked example.
    /// </summary>
    public abstract class GameCondition : IExposable
    {
        /// <summary>A <see cref="duration"/> of this means "until something removes it" (RimWorld:
        /// <c>GameCondition.Permanent</c>, which it carries as a separate bool).</summary>
        public const int PermanentDuration = -1;

        public GameConditionDef def = null!;

        /// <summary>Tick this condition started (RimWorld: <c>GameCondition.startTick</c>).</summary>
        public int startTick;

        /// <summary>How many ticks it runs for, or <see cref="PermanentDuration"/>.</summary>
        public int duration = PermanentDuration;

        /// <summary>The manager holding this condition; set by <see cref="GameConditionManager.RegisterCondition"/>
        /// and re-linked on load. Null only between construction and registration.</summary>
        public GameConditionManager? manager;

        public bool Permanent => duration < 0;

        public int TicksPassed => Find.TickManager.TicksGame - startTick;

        /// <summary>Ticks until this condition ends; <see cref="int.MaxValue"/> while it is permanent.</summary>
        public int TicksLeft => Permanent ? int.MaxValue : startTick + duration - Find.TickManager.TicksGame;

        public bool Expired => !Permanent && TicksLeft <= 0;

        /// <summary>Called once, when the condition is registered — after <see cref="def"/>,
        /// <see cref="startTick"/>, <see cref="duration"/> and <see cref="manager"/> are all set, and never
        /// again on load (RimWorld: <c>GameCondition.Init</c>).</summary>
        public virtual void Init()
        {
        }

        /// <summary>
        /// Called once, as the condition is removed — by expiry or by hand (RimWorld:
        /// <c>GameCondition.End</c>). The base sends <see cref="GameConditionDef.endMessage"/>, so a player
        /// told a heat wave started is also told it stopped; RimWorld sends that as a transient message and
        /// this port has no message channel, only <see cref="Letters.LetterStack"/>, so it arrives as a
        /// letter of the same <see cref="GameConditionDef.letterDef"/> the start used.
        /// </summary>
        public virtual void End()
        {
            if (def?.endMessage == null || def.letterDef == null) return;
            Find.LetterStack.ReceiveLetter(def.LabelCap, def.endMessage, def.letterDef);
        }

        /// <summary>Once per game tick, from the manager that owns this condition. See the class doc.</summary>
        public virtual void GameConditionTick()
        {
        }

        /// <summary>Once per game tick <i>per open map this condition reaches</i>, from that map's own
        /// manager. See the class doc for why this is separate.</summary>
        public virtual void GameConditionTickOnMap(Map.Map map)
        {
        }

        /// <summary>°C this condition adds to the outdoor temperature of every map it reaches (RimWorld:
        /// <c>GameCondition.TemperatureOffset</c>). Summed with every other active condition's by
        /// <see cref="GameConditionManager.AggregateTemperatureOffset"/> and folded into the weather module's
        /// existing arithmetic — see <c>Weather.WeatherManager</c>.</summary>
        public virtual float TemperatureOffset() => 0f;

        /// <summary>What a letter or the god view calls this condition.</summary>
        public virtual string Label => def?.LabelCap ?? GetType().Name;

        public virtual void ExposeData()
        {
            GameConditionDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref duration, "duration", PermanentDuration);
        }
    }

    /// <summary>
    /// Builds a condition from its def (RimWorld: <c>RimWorld.GameConditionMaker</c>). The one place
    /// <see cref="GameConditionDef.conditionClass"/> is instantiated, so every condition in the game is born
    /// with its def, its start tick and its duration already set — <see cref="GameCondition.Init"/> can rely
    /// on all three.
    /// </summary>
    public static class GameConditionMaker
    {
        /// <summary>A condition of <paramref name="def"/> running for <paramref name="duration"/> ticks
        /// (<see cref="GameCondition.PermanentDuration"/> for one that does not expire on its own).</summary>
        public static GameCondition MakeCondition(GameConditionDef def, int duration)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            var condition = (GameCondition)Activator.CreateInstance(def.conditionClass)!;
            condition.def = def;
            condition.startTick = Find.TickManager.TicksGame;
            condition.duration = duration;
            return condition;
        }

        /// <summary>A condition running for <paramref name="days"/> in-game days, rounded to whole ticks.</summary>
        public static GameCondition MakeConditionForDays(GameConditionDef def, float days) =>
            MakeCondition(def, (int)(days * GenDate.TicksPerDay));
    }

    /// <summary>
    /// A condition that is nothing but a standing temperature offset — a heat wave, a cold snap (RimWorld:
    /// <c>GameCondition_HeatWave</c> / <c>GameCondition_ColdSnap</c>, one class each with the number
    /// hardcoded in an override).
    ///
    /// <para/><b>One class, two defs.</b> The two RimWorld classes differ in exactly one number and nothing
    /// else, so this port keeps the class and moves the number to
    /// <see cref="GameConditionDef.temperatureOffset"/>. A third temperature condition — a long winter, a
    /// climate cycle — then needs content and no code.
    ///
    /// <para/><b>What it reuses rather than rebuilds.</b> Nothing here computes a temperature. The weather
    /// module already owns the outdoor-temperature arithmetic (<c>Weather.GenTemperature</c>) and already
    /// takes an offset argument; <c>Weather.WeatherManager.UpdateOutdoorTemperature</c> adds this condition's
    /// aggregate to the weather's own offset, and every consumer that already read
    /// <c>Map.outdoorTemperature</c> — rooms, plant growth, corpse rot — sees the heat wave without a line
    /// of change. <c>WeatherDef.temperatureOffset</c>'s own doc named this as the seam a real GameCondition
    /// system would plug into; this is that plug.
    /// </summary>
    public class GameCondition_TemperatureOffset : GameCondition
    {
        public override float TemperatureOffset() => def?.temperatureOffset ?? 0f;
    }
}
