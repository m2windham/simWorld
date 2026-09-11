using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Letters;

namespace SimWorld.Conditions
{
    /// <summary>
    /// One kind of standing condition (RimWorld: <c>RimWorld.GameConditionDef</c>): a heat wave, a cold snap,
    /// a flashstorm. The def carries what the condition <i>is</i> and how long it lasts; the behaviour lives
    /// in <see cref="conditionClass"/>, instantiated per firing by <see cref="GameConditionMaker"/>.
    ///
    /// <para/><b>Translation: the condition names its incident, not the other way round.</b> RimWorld puts
    /// the link on the incident — <c>IncidentDef.gameCondition</c> and <c>IncidentDef.durationDays</c>, read
    /// by <c>IncidentWorker_MakeGameCondition</c>. Both of those would be new fields on
    /// <c>Director/IncidentDefs.cs</c>, the Director's single most shared source file, and CLAUDE.md is
    /// explicit about what to do instead: "Where a shared file genuinely must change, prefer inverting the
    /// relationship so it does not." So the relationship is inverted. A <see cref="GameConditionDef"/> names
    /// the <see cref="IncidentDef"/> that brings it about (<see cref="incident"/>) and carries its own
    /// <see cref="durationDays"/>, and <see cref="IncidentWorker_MakeGameCondition"/> matches from this side.
    /// The content reads the same either way — <c>HeatWave</c> the incident makes <c>HeatWave</c> the
    /// condition — and not one line of <c>IncidentDefs.cs</c> had to move.
    ///
    /// <para/><b>Fields only one condition class reads.</b> <see cref="temperatureOffset"/> means something to
    /// <see cref="GameCondition_TemperatureOffset"/> and nothing to anyone else, exactly as
    /// <c>IncidentDef.diseaseIncident</c> already means something only to <c>IncidentWorker_Disease</c>. It
    /// is on the def rather than in the class because two defs (heat wave, cold snap) share one class and
    /// differ only in that number. Tuning that belongs to a single def with a single class — the flashstorm's
    /// strike cadence — stays a constant on that class instead, the way <c>Things.Fire</c> keeps its own.
    /// </summary>
    public class GameConditionDef : Def
    {
        /// <summary>The <see cref="GameCondition"/> subclass this def instantiates (RimWorld:
        /// <c>GameConditionDef.conditionClass</c>). Content names it in full.</summary>
        public Type conditionClass = typeof(GameCondition);

        /// <summary>
        /// How long a firing of this condition lasts, in days (RimWorld: <c>IncidentDef.durationDays</c> —
        /// see the class doc for why it lives here). Rolled once, through the seeded stream, when the
        /// condition is made.
        /// </summary>
        public FloatRange durationDays = new FloatRange(1f, 3f);

        /// <summary>
        /// The incident that makes this condition, or null for a condition nothing fires (one a quest or a
        /// scenario would install directly). See the class doc: this is the inverted half of RimWorld's
        /// <c>IncidentDef.gameCondition</c>.
        /// </summary>
        public IncidentDef? incident;

        /// <summary>
        /// °C this condition adds to the outdoor temperature of every map it reaches — read by
        /// <see cref="GameCondition_TemperatureOffset"/> and ignored by every other condition class.
        /// <para/>
        /// <b>Constants.</b> RimWorld's heat wave and cold snap override <c>TemperatureOffset()</c> with one
        /// hardcoded number each; those numbers are recalled here rather than sourced from a decompile, so
        /// per CLAUDE.md the tests pin the behaviour they produce — a heat wave makes it hotter than it was,
        /// far enough to stop plants growing and speed rot; a cold snap drops it below freezing and stops rot
        /// dead — never these literals.
        /// </summary>
        public float temperatureOffset;

        /// <summary>Letter sent when this condition starts, or null to start it quietly.</summary>
        public LetterDef? letterDef;

        /// <summary>Sent as a letter when the condition ends (RimWorld: <c>GameConditionDef.endMessage</c>);
        /// null ends it quietly.</summary>
        public string? endMessage;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (conditionClass == null || !typeof(GameCondition).IsAssignableFrom(conditionClass))
            {
                yield return "conditionClass must derive from GameCondition.";
            }
            if (conditionClass != null && conditionClass.IsAbstract)
            {
                yield return "conditionClass " + conditionClass.Name + " is abstract.";
            }
            if (durationDays.min <= 0f || durationDays.max < durationDays.min)
            {
                yield return "durationDays must be a positive ascending range.";
            }
        }
    }

    /// <summary>The conditions the code itself names (RimWorld: <c>RimWorld.GameConditionDefOf</c>). Its own
    /// <c>[DefOf]</c> class in its own file's namespace rather than an addition to a shared one —
    /// <see cref="DefOfHelper"/> binds by scanning every <c>[DefOf]</c> type (CLAUDE.md).</summary>
    [DefOf]
    public static class GameConditionDefOf
    {
        /// <summary>Weeks of unusual heat over the whole civilization.</summary>
        public static GameConditionDef HeatWave = null!;

        /// <summary>The mirror of <see cref="HeatWave"/>: unusual cold.</summary>
        public static GameConditionDef ColdSnap = null!;

        /// <summary>A lightning storm that sets things alight wherever it reaches a map.</summary>
        public static GameConditionDef Flashstorm = null!;
    }

    /// <summary>
    /// The three incidents this module took off <c>IncidentWorker_Placeholder</c>. Bound here, in the
    /// module that gave them behaviour, rather than appended to <c>Director.IncidentDefOf</c> — a new
    /// <c>[DefOf]</c> class in its own namespace cannot collide with a lane mid-edit on that file
    /// (CLAUDE.md), and the binding itself is the guard: the content test fails the load if any of these
    /// three defNames stops existing, which is exactly the day <see cref="GameConditionDef.incident"/> would
    /// otherwise silently resolve to nothing and all three would go quiet again.
    /// </summary>
    [DefOf]
    public static class ConditionIncidentDefOf
    {
        public static IncidentDef HeatWave = null!;

        public static IncidentDef ColdSnap = null!;

        public static IncidentDef Flashstorm = null!;
    }
}
