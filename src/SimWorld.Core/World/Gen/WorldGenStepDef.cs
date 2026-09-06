using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.World.Gen
{
    /// <summary>One stage of world generation (RimWorld: <c>Verse.WorldGenStepDef</c>). <c>WorldGenerator</c> runs every loaded step in ascending <see cref="order"/>.</summary>
    public class WorldGenStepDef : Def
    {
        /// <summary>Ascending run order; RimWorld convention leaves gaps (100, 200, ...) so a mod can slot a step between two others.</summary>
        public int order;

        public Type workerClass = typeof(WorldGenStep);

        /// <summary>Re-run by <see cref="global::SimWorld.World.World.RegenerateGrid"/> on load. False for the Factions step: settlements/factions are saved directly instead (see <see cref="global::SimWorld.World.World.ExposeData"/>).</summary>
        public bool regenerateOnLoad = true;

        private WorldGenStep? workerInt;

        public WorldGenStep Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (WorldGenStep)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(WorldGenStep).IsAssignableFrom(workerClass)) yield return "workerClass must derive from WorldGenStep.";
        }
    }
}
