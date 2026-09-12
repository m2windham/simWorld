using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Picks a way to take a break and issues the job for it (RimWorld: <c>RimWorld.JobGiver_GetJoy</c>).
    /// Every <see cref="JoyGiverDef"/> gets a weight — its own <see cref="JoyGiver.GetChance"/> damped by
    /// <c>(1 − tolerance)^5</c>, RimWorld's own curve — a giver is drawn by weight, and if that giver cannot
    /// produce a job right now (no open sky, nobody to sit with) its weight is zeroed and the draw repeats.
    /// A kind the pawn is bored of is skipped outright, exactly as RimWorld skips it.
    ///
    /// <para/><b>This is the tier the humanlike think tree did not have.</b> See
    /// <see cref="JoyUtility"/> for what its absence cost.
    /// </summary>
    public class JobGiver_GetJoy : ThinkNode_JobGiver
    {
        /// <summary>RimWorld's own <c>JoyBuffer</c>: a need this close to full is not worth a job.</summary>
        public const float JoyBuffer = 0.99f;

        /// <summary>Floor under the tolerance damping so a thoroughly-tolerated activity stays reachable
        /// rather than becoming impossible (RimWorld: <c>Mathf.Max(0.001f, b)</c>).</summary>
        private const float MinToleranceWeight = 0.001f;

        /// <summary>The exponent RimWorld damps a giver's weight by as tolerance to its kind builds.</summary>
        private const float ToleranceWeightExponent = 5f;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Need_Joy? joy = pawn.needs.joy;
            if (joy == null || pawn.Map == null) return null;
            if (joy.CurLevel >= JoyBuffer) return null;

            IReadOnlyList<JoyGiverDef> givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;
            if (givers.Count == 0) return null;

            var weights = new float[givers.Count];
            var index = new Dictionary<JoyGiverDef, int>(givers.Count);
            for (int i = 0; i < givers.Count; i++)
            {
                JoyGiverDef giver = givers[i];
                index[giver] = i;
                weights[i] = 0f;

                if (joy.tolerances.BoredOf(giver.joyKind)) continue;
                if (!giver.Worker.CanBeGivenTo(pawn)) continue;

                float tolerance = joy.tolerances[giver.joyKind];
                float damping = Math.Max(MinToleranceWeight, MathF.Pow(1f - tolerance, ToleranceWeightExponent));
                weights[i] = Math.Max(0f, giver.Worker.GetChance(pawn)) * damping;
            }

            for (int attempt = 0; attempt < givers.Count; attempt++)
            {
                if (!GenCollection.TryRandomElementByWeight(givers, d => weights[index[d]], Rand.Current, out JoyGiverDef chosen))
                {
                    break;
                }
                Job? job = chosen.Worker.TryGiveJob(pawn);
                if (job != null) return job;
                weights[index[chosen]] = 0f;
            }
            return null;
        }
    }

    /// <summary>
    /// The same thing, offered to a citizen who has run out of work (RimWorld:
    /// <c>RimWorld.JobGiver_IdleJoy</c>, which sits at the bottom of the humanlike tree above wandering).
    /// Refuses for the first in-game day exactly as RimWorld's does — a band that has just arrived somewhere
    /// has more pressing things to do than sit about, and the constant is RimWorld's own 60,000 ticks.
    /// </summary>
    public sealed class JobGiver_IdleJoy : JobGiver_GetJoy
    {
        public const int GameStartNoIdleJoyTicks = 60000;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (pawn.needs.joy == null) return null;
            if (Find.TickManager.TicksGame < GameStartNoIdleJoyTicks) return null;
            return base.TryGiveJob(pawn);
        }
    }
}
