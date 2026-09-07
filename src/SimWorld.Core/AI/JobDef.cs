using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// What kind of job this is (RimWorld: <c>Verse.AI.JobDef</c>): names the <see cref="JobDriver"/> that
    /// actually runs it. A <see cref="Job"/> is the instance (with targets); this is the shared definition.
    /// </summary>
    public class JobDef : Def
    {
        public Type driverClass = null!;

        /// <summary>Shown in an inspect pane once one exists; not otherwise read yet.</summary>
        public string? reportString;

        public JobDriver MakeDriver(Pawn pawn, Job job)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (driverClass == null) throw new InvalidOperationException("JobDef " + defName + " has no driverClass.");
            var driver = (JobDriver)Activator.CreateInstance(driverClass)!;
            driver.pawn = pawn;
            driver.job = job;
            return driver;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(JobDriver).IsAssignableFrom(driverClass)) yield return "driverClass must derive from JobDriver.";
        }
    }

    /// <summary>The core JobDefs this pass's job givers issue.</summary>
    [DefOf]
    public static class JobDefOf
    {
        public static JobDef Ingest = null!;
        public static JobDef LayDown = null!;
        public static JobDef GotoWander = null!;
        public static JobDef Mine = null!;
    }
}
