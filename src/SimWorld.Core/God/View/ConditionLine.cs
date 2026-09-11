using System;
using System.Collections.Generic;

using SimWorld.Conditions;

namespace SimWorld.God.View
{
    /// <summary>
    /// One condition standing over the civilization, as the god view needs it (<c>docs/spec/simworld-spec.md</c>
    /// §12a).
    ///
    /// <para/><b>Why this widens <c>God/View</c> when weather deliberately did not.</b> The weather lane
    /// argued — correctly — that weather is settlement-scale: it exists only where a <c>Map</c> exists, which
    /// is the one settlement the god has opened, and a civilization-scale readout has no business carrying
    /// what the sky is doing over one town. A game condition in this port is the opposite by construction. It
    /// is registered on <c>World.World.gameConditionManager</c> precisely because the storyteller's target is
    /// the whole civilization (see <see cref="GameConditionManager"/>), it holds over every settlement at
    /// once, it outlives whichever map happened to be open, and it lasts days — a heat wave is exactly the
    /// kind of thing a god should be able to see without descending into a town to notice the thermometer.
    /// Leaving it out would also leave the god view unable to explain what it already shows: a civilization
    /// whose food is spoiling and whose crops have stopped has a reason, and this is it.
    ///
    /// <para/>Map-local conditions are deliberately <i>not</i> reported here. Nothing in the shipped content
    /// makes one, and if something does, it will be settlement-scale for the same reason weather is — it
    /// belongs to whatever detail query eventually serves one opened settlement, not to the civilization view.
    ///
    /// <para/>Values and a defName only, like every other part of the snapshot: a host cannot reach a
    /// <c>GameConditionDef</c> through this and so cannot reach a condition class through it either.
    /// </summary>
    public sealed class ConditionLine
    {
        internal ConditionLine(string defName, string label, string description, int startTick, int ticksLeft, bool permanent, float temperatureOffset)
        {
            DefName = defName;
            Label = label;
            Description = description;
            StartTick = startTick;
            TicksLeft = ticksLeft;
            Permanent = permanent;
            TemperatureOffset = temperatureOffset;
        }

        /// <summary>The handle — a <c>GameConditionDef</c>'s defName, never the def.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Description { get; }

        /// <summary>Tick the condition started, so a host can say how long it has been going.</summary>
        public int StartTick { get; }

        /// <summary>Ticks until it ends. <see cref="int.MaxValue"/> when <see cref="Permanent"/>; check that
        /// flag rather than testing this against a threshold.</summary>
        public int TicksLeft { get; }

        public bool Permanent { get; }

        /// <summary>°C this condition is adding to every outdoor temperature in the civilization; 0 for one
        /// that does not touch the thermometer. Carried because it is the difference between "a heat wave"
        /// and "a heat wave bad enough to matter", and the host would otherwise have to hardcode per-defName
        /// knowledge to draw that distinction.</summary>
        public float TemperatureOffset { get; }

        /// <summary>Every civilization-scale condition in force right now, in the order they started.</summary>
        internal static List<ConditionLine> ActiveOn(GameConditionManager? manager)
        {
            var lines = new List<ConditionLine>();
            if (manager == null) return lines;

            IReadOnlyList<GameCondition> active = manager.ActiveConditions;
            for (int i = 0; i < active.Count; i++)
            {
                GameCondition condition = active[i];
                lines.Add(new ConditionLine(
                    condition.def?.defName ?? "",
                    condition.Label,
                    condition.def?.description ?? "",
                    condition.startTick,
                    condition.TicksLeft,
                    condition.Permanent,
                    condition.TemperatureOffset()));
            }
            return lines;
        }
    }
}
