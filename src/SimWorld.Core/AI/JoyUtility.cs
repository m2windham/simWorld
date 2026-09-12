using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Needs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The arithmetic every recreation job shares (RimWorld: <c>RimWorld.JoyUtility</c>).
    ///
    /// <para/><b>Why this class exists at all.</b> Before it, <c>Need_Joy.GainJoy</c> had exactly one caller
    /// in the whole of <c>src/</c> — <c>Things.CompDrug</c> — so the only recreation available to a citizen
    /// of this civilization was narcotics, and every citizen sat at the recreation need's worst stage
    /// (−20 mood) from day two onward for ever. The need, its per-kind tolerance model and its mood thought
    /// were all built and tested; nothing produced the input. See <c>docs/WORK-REGISTER.md</c> §9a.
    /// </summary>
    public static class JoyUtility
    {
        /// <summary>
        /// Joy gained per tick by a job whose <see cref="JoyGiverDef.joyGainRate"/> is 1 (RimWorld:
        /// <c>JoyUtility.JoyTickCheckEnd</c>'s literal <c>joyGainRate * 0.36f / 2500f * delta</c>). A
        /// rate-1 session of 2500 ticks therefore fills 36% of the bar before tolerance damping.
        /// </summary>
        public const float JoyGainPerTickAtRate1 = 0.36f / 2500f;

        private static Dictionary<JobDef, JoyGiverDef>? giverByJob;

        /// <summary>
        /// The <see cref="JoyGiverDef"/> that authored a running recreation job, or null for a job that is
        /// not recreation. RimWorld reads <c>joyKind</c>/<c>joyGainRate</c> straight off <c>Job.def</c>; this
        /// port keeps them on the giver instead and looks back the other way — see
        /// <see cref="JoyGiverDef"/>'s own remarks for why. Built once and cached; the Def database does not
        /// change after load.
        /// </summary>
        public static JoyGiverDef? GiverForJob(JobDef jobDef)
        {
            if (jobDef == null) return null;
            Dictionary<JobDef, JoyGiverDef> map = giverByJob ??= BuildMap();
            return map.TryGetValue(jobDef, out JoyGiverDef? giver) ? giver : null;
        }

        /// <summary>Drops the cached job → giver map. Tests that swap the Def database call this; nothing in
        /// the game does.</summary>
        public static void ClearCache() => giverByJob = null;

        private static Dictionary<JobDef, JoyGiverDef> BuildMap()
        {
            var map = new Dictionary<JobDef, JoyGiverDef>();
            IReadOnlyList<JoyGiverDef> all = DefDatabase<JoyGiverDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                JoyGiverDef giver = all[i];
                if (giver.jobDef != null) map[giver.jobDef] = giver;
            }
            return map;
        }

        /// <summary>
        /// One tick of a recreation job: credit the joy, and say whether the need is now full (RimWorld:
        /// <c>JoyUtility.JoyTickCheckEnd</c>, which ends the job itself — here the driver does, because this
        /// port's <c>JobDriver.EndJobWith</c> is protected and a static helper cannot reach it).
        /// <para/>
        /// <paramref name="extraJoyGainFactor"/> is RimWorld's hook for a joy source's own
        /// <c>JoyGainFactor</c> stat (a better chess table is better recreation); nothing in this port's
        /// content sets one yet, so it is always 1.
        /// </summary>
        public static bool JoyTickCheckEnd(Pawn pawn, JoyKindDef joyKind, float joyGainRate, int delta = 1, float extraJoyGainFactor = 1f)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (joyKind == null) throw new ArgumentNullException(nameof(joyKind));

            Need_Joy? joy = pawn.needs.joy;
            if (joy == null) return true;
            joy.GainJoy(extraJoyGainFactor * joyGainRate * JoyGainPerTickAtRate1 * delta, joyKind);
            return joy.CurLevel > 0.9999f;
        }
    }
}
