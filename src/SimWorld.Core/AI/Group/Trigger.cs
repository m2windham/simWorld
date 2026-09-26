using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// Why a <see cref="Lord"/> stopped owning a pawn (RimWorld: <c>Verse.AI.Group.PawnLostCondition</c>).
    /// Only the members this port can reach are here; RimWorld's list also has <c>Vanished</c>,
    /// <c>LeftVoluntarily</c>, <c>Drafted</c> and <c>ForcedToJoinOtherLord</c>, and none of those has a cause
    /// in this port.
    /// </summary>
    public enum PawnLostCondition : byte
    {
        Undefined,

        /// <summary>Downed or dead. With <see cref="MadePrisoner"/>, the only kinds of loss
        /// <see cref="Lord.numPawnsLostViolently"/> counts — RimWorld's own rule, in <c>Lord.Notify_PawnLost</c>.</summary>
        IncappedOrKilled,

        /// <summary>Taken prisoner while still standing. Unreachable in practice (capture here needs the pawn
        /// down, so it is lost as <see cref="IncappedOrKilled"/> first) and kept because it is the other
        /// violent loss RimWorld counts.</summary>
        MadePrisoner,

        /// <summary>No longer this lord's faction.</summary>
        ChangedFaction,

        /// <summary>Walked off the map. Not violent: a raider who got away was not beaten.</summary>
        ExitedMap,
    }

    /// <summary>What a <see cref="Trigger"/> is being asked about (RimWorld: <c>Verse.AI.Group.TriggerSignalType</c>,
    /// trimmed to the two signals this port raises).</summary>
    public enum TriggerSignalType : byte
    {
        Undefined,
        Tick,
        PawnLost,
    }

    /// <summary>One event offered to a lord's transitions (RimWorld: <c>Verse.AI.Group.TriggerSignal</c>).</summary>
    public struct TriggerSignal
    {
        public TriggerSignalType type;
        public Pawn? thing;
        public PawnLostCondition condition;

        public static TriggerSignal ForTick => new TriggerSignal { type = TriggerSignalType.Tick };
    }

    /// <summary>
    /// A condition that fires a <see cref="Transition"/> (RimWorld: <c>Verse.AI.Group.Trigger</c>). A trigger
    /// that has to remember something between signals keeps it in <see cref="data"/>, which is what the lord
    /// saves — the trigger itself is rebuilt from the lord job on load, exactly as RimWorld rebuilds it.
    /// </summary>
    public abstract class Trigger
    {
        public TriggerData? data;

        public abstract bool ActivateOn(Lord lord, TriggerSignal signal);

        /// <summary>Called on every trigger of every transition leaving the toil the lord just entered.</summary>
        public virtual void SourceToilBecameActive(Transition transition, LordToil? previousToil)
        {
        }
    }

    /// <summary>The saved half of a <see cref="Trigger"/> (RimWorld: <c>Verse.AI.Group.TriggerData</c>).</summary>
    public abstract class TriggerData : IExposable
    {
        public abstract void ExposeData();
    }

    /// <summary>
    /// Fires once enough of the lord's pawns have been lost violently (RimWorld:
    /// <c>Verse.AI.Group.Trigger_FractionPawnsLost</c>, ported 1:1). Asked only on a
    /// <see cref="TriggerSignalType.PawnLost"/> signal, and counts against everyone the lord ever held, so a
    /// squad that has already had a raider walk off still breaks at the same number of casualties.
    /// </summary>
    public sealed class Trigger_FractionPawnsLost : Trigger
    {
        private readonly float fraction;

        public Trigger_FractionPawnsLost(float fraction)
        {
            this.fraction = fraction;
        }

        public float Fraction => fraction;

        public override bool ActivateOn(Lord lord, TriggerSignal signal) =>
            signal.type == TriggerSignalType.PawnLost
            && lord.numPawnsLostViolently >= lord.numPawnsEverGained * fraction;
    }

    /// <summary>
    /// Fires after the lord has spent <see cref="Duration"/> ticks in one of this transition's source toils
    /// (RimWorld: <c>Verse.AI.Group.Trigger_TicksPassed</c>, ported 1:1 — including that it fires on the tick
    /// the count goes <i>past</i> the duration, not the tick it reaches it, and that re-entering a source toil
    /// from a toil that was not one resets the count).
    /// </summary>
    public sealed class Trigger_TicksPassed : Trigger
    {
        private readonly int duration;

        public Trigger_TicksPassed(int tickLimit)
        {
            data = new TriggerData_TicksPassed();
            duration = tickLimit;
        }

        public int Duration => duration;

        private TriggerData_TicksPassed Data
        {
            get
            {
                // A save written before this trigger held data, or one whose data failed to load, restarts the
                // count rather than throwing — RimWorld's BackCompatibility.TriggerDataTicksPassedNull does the
                // same.
                if (!(data is TriggerData_TicksPassed d))
                {
                    d = new TriggerData_TicksPassed();
                    data = d;
                }
                return d;
            }
        }

        public int TicksLeft => System.Math.Max(duration - Data.ticksPassed, 0);

        public override bool ActivateOn(Lord lord, TriggerSignal signal)
        {
            if (signal.type != TriggerSignalType.Tick) return false;
            TriggerData_TicksPassed d = Data;
            d.ticksPassed++;
            return d.ticksPassed > duration;
        }

        public override void SourceToilBecameActive(Transition transition, LordToil? previousToil)
        {
            if (previousToil == null || !transition.sources.Contains(previousToil)) Data.ticksPassed = 0;
        }
    }

    /// <summary>How long a <see cref="Trigger_TicksPassed"/> has been counting (RimWorld:
    /// <c>Verse.AI.Group.TriggerData_TicksPassed</c>).</summary>
    public sealed class TriggerData_TicksPassed : TriggerData
    {
        public int ticksPassed;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref ticksPassed, "ticksPassed");
        }
    }
}
