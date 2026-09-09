using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>
    /// One thing an animal can be trained to do (RimWorld: <c>RimWorld.TrainableDef</c>). <see cref="steps"/>
    /// is how many successful <see cref="Pawn_TrainingTracker.Train"/> calls it takes to learn;
    /// <see cref="minTrainability"/> is the race-level bar (<see cref="RaceProperties.trainability"/>) an
    /// animal must clear to ever learn it at all; <see cref="prerequisites"/> must already be learned first.
    /// </summary>
    public class TrainableDef : Def
    {
        public int steps = 1;

        /// <summary>Not consumed yet: this port has no player-facing "wanted" checkbox (see
        /// <see cref="Pawn_TrainingTracker.CanBeTrained"/>'s own doc), so every learnable def is always
        /// pursued. Kept for parity with RimWorld content and for whatever UI eventually reads it.</summary>
        public bool defaultTrainable;

        public TrainabilityDef minTrainability = null!;

        public List<TrainableDef>? prerequisites;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (steps <= 0) yield return "steps must be positive.";
            if (minTrainability == null) yield return "minTrainability is required.";
        }
    }

    /// <summary>The training steps this pass's content ships. Attack/Guard/Tackle are RimWorld's own but are
    /// out of scope here: they only mean anything once an animal can actually fight, which is Combat's job
    /// and outside this lane's boundary — see this module's report.</summary>
    [DefOf]
    public static class TrainableDefOf
    {
        public static TrainableDef Obedience = null!;
        public static TrainableDef Release = null!;
        public static TrainableDef Rescue = null!;
        public static TrainableDef Haul = null!;
    }
}
