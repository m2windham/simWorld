using System;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Combat
{
    /// <summary>Where a verb sits in its attack cycle (RimWorld: the internal state of <c>Verse.Verb</c>).</summary>
    public enum VerbState
    {
        Idle,
        WarmingUp,
        Bursting,
        Cooldown,
    }

    /// <summary>
    /// A way of attacking: warmup, then a burst of one or more shots spaced <c>ticksBetweenBurstShots</c>
    /// apart, then cooldown (RimWorld: <c>Verse.Verb</c>). Both ranged and melee attacks share this state
    /// machine; <see cref="TryCastShot"/> is what one shot in the burst actually resolves. Nothing fires
    /// inside <see cref="TryStartCastOn"/> itself — every state transition, including the first shot, happens
    /// inside <see cref="VerbTick"/>, so a caller always drives the attack one simulated tick at a time.
    /// </summary>
    public abstract class Verb
    {
        public readonly VerbProperties verbProps;
        public readonly Pawn caster;

        private VerbState state = VerbState.Idle;
        private int stateTicksLeft;
        private int shotsRemainingInBurst;
        private Pawn? currentTarget;
        private float currentDistance;
        private int lastShotTick = -1;

        protected Verb(Pawn caster, VerbProperties verbProps)
        {
            this.caster = caster ?? throw new ArgumentNullException(nameof(caster));
            this.verbProps = verbProps ?? throw new ArgumentNullException(nameof(verbProps));
        }

        public VerbState State => state;

        /// <summary>Game tick <see cref="Sim.Find.TickManager"/> was at when the most recent shot fired; -1 before any shot.</summary>
        public int LastShotTick => lastShotTick;

        /// <summary>Idle and the caster is able to act; a fresh <see cref="TryStartCastOn"/> only succeeds here.</summary>
        public bool Available() => state == VerbState.Idle && !caster.Dead;

        /// <summary>Ticks are the sim's fixed 60/second rate (RimWorld: <c>GenTicks.TicksPerRealSecond</c>).</summary>
        public static int SecondsToTicks(float seconds) => Math.Max(0, GenTicks.SecondsToTicks(seconds));

        /// <summary>Arms the verb against <paramref name="target"/>, <paramref name="distance"/> cells away. Fails if not <see cref="Available"/>.</summary>
        public bool TryStartCastOn(Pawn target, float distance)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!Available()) return false;

            currentTarget = target;
            currentDistance = distance;
            shotsRemainingInBurst = Math.Max(1, verbProps.burstShotCount);
            state = VerbState.WarmingUp;
            stateTicksLeft = Math.Max(1, SecondsToTicks(verbProps.warmupTime));
            return true;
        }

        /// <summary>Advances the verb by one game tick; call once per tick while not <see cref="Available"/>.</summary>
        public void VerbTick()
        {
            if (state == VerbState.Idle) return;
            stateTicksLeft--;
            if (stateTicksLeft > 0) return;

            switch (state)
            {
                case VerbState.WarmingUp:
                case VerbState.Bursting:
                    FireOneShot();
                    if (shotsRemainingInBurst > 0)
                    {
                        state = VerbState.Bursting;
                        stateTicksLeft = Math.Max(1, verbProps.ticksBetweenBurstShots);
                    }
                    else
                    {
                        BeginCooldown();
                    }
                    break;
                case VerbState.Cooldown:
                    state = VerbState.Idle;
                    break;
            }
        }

        private void FireOneShot()
        {
            lastShotTick = Find.TickManager.TicksGame;
            shotsRemainingInBurst--;
            TryCastShot(currentTarget!, currentDistance);
        }

        private void BeginCooldown()
        {
            int cooldownTicks = SecondsToTicks(verbProps.defaultCooldownTime);
            if (cooldownTicks > 0)
            {
                state = VerbState.Cooldown;
                stateTicksLeft = cooldownTicks;
            }
            else
            {
                state = VerbState.Idle;
            }
        }

        /// <summary>Resolves one shot of the burst against <paramref name="target"/>, <paramref name="distance"/> cells away.</summary>
        protected abstract void TryCastShot(Pawn target, float distance);
    }

    /// <summary>Constructs a <see cref="Verb"/> from its <see cref="VerbProperties"/> (RimWorld: <c>VerbProperties.CreateVerb</c>).</summary>
    public static class VerbUtility
    {
        public static Verb MakeVerb(Pawn caster, VerbProperties props)
        {
            if (caster == null) throw new ArgumentNullException(nameof(caster));
            if (props == null) throw new ArgumentNullException(nameof(props));
            return (Verb)Activator.CreateInstance(props.verbClass, caster, props)!;
        }
    }
}
