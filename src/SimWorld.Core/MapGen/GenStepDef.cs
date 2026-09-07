using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.MapGen
{
    /// <summary>
    /// One stage of local map generation (RimWorld: <c>Verse.GenStepDef</c>), mirroring
    /// <see cref="global::SimWorld.World.Gen.WorldGenStepDef"/>. <see cref="MapGenerator"/> runs every step
    /// named in a <see cref="MapGeneratorDef"/>'s <see cref="MapGeneratorDef.genSteps"/> in ascending
    /// <see cref="order"/>.
    /// </summary>
    public class GenStepDef : Def
    {
        /// <summary>Ascending run order; gaps of 100 (as with <c>WorldGenStepDef</c>) so a mod can slot a step between two others.</summary>
        public int order;

        public Type workerClass = typeof(GenStep);

        private GenStep? workerInt;

        public GenStep Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (GenStep)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (!typeof(GenStep).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from GenStep.";
            }
        }
    }
}
