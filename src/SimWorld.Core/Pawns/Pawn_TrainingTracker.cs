using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Which <see cref="TrainableDef"/>s this animal has learned and how far along each one is (RimWorld:
    /// <c>RimWorld.Pawn_TrainingTracker</c>). More than a bool per def: <see cref="GetSteps"/> accumulates
    /// one point per successful <see cref="Train"/> call toward that def's <see cref="TrainableDef.steps"/>
    /// threshold, and <see cref="TrainingTrackerTick"/> can claw a point back if the animal goes untended —
    /// see that method's own doc for the decay rule and which numbers in it are unsourced.
    /// </summary>
    public sealed class Pawn_TrainingTracker : IExposable
    {
        private readonly Pawn pawn;

        private Dictionary<TrainableDef, int> steps = new Dictionary<TrainableDef, int>();
        private Dictionary<TrainableDef, bool> learned = new Dictionary<TrainableDef, bool>();

        /// <summary>Tick of the last successful <see cref="Train"/> call (any def); -1 before the first one.</summary>
        private int lastTrainedTick = -1;

        public Pawn_TrainingTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public int GetSteps(TrainableDef td) => steps.TryGetValue(td, out int v) ? v : 0;

        public bool HasLearned(TrainableDef td) => learned.TryGetValue(td, out bool v) && v;

        /// <summary>
        /// Whether <paramref name="td"/> could ever be learned by this animal at all: tamed (RimWorld only
        /// trains a colony's own animals), of a race whose <see cref="RaceProperties.trainability"/> clears
        /// this def's own bar, and with every prerequisite already learned. <b>Deviation:</b> RimWorld also
        /// gates this behind a per-def player "wanted" checkbox; no such UI exists here, so anything
        /// otherwise eligible is always wanted — see <see cref="NextToTrain"/>.
        /// </summary>
        public bool CanBeTrained(TrainableDef td)
        {
            if (td == null) throw new ArgumentNullException(nameof(td));
            if (!pawn.RaceProps.Animal || pawn.faction == null) return false;
            TrainabilityDef? trainability = pawn.RaceProps.trainability;
            if (trainability == null || trainability.intelligenceOrder < td.minTrainability.intelligenceOrder) return false;
            if (td.prerequisites != null)
            {
                foreach (TrainableDef prereq in td.prerequisites)
                {
                    if (!HasLearned(prereq)) return false;
                }
            }
            return true;
        }

        /// <summary>The first not-yet-learned, currently learnable def, in content order — what a trainer
        /// works toward next absent any player-chosen priority (see <see cref="CanBeTrained"/>'s doc).</summary>
        public TrainableDef? NextToTrain()
        {
            foreach (TrainableDef td in DefDatabase<TrainableDef>.AllDefsListForReading)
            {
                if (HasLearned(td)) continue;
                if (CanBeTrained(td)) return td;
            }
            return null;
        }

        /// <summary>
        /// One completed training session on <paramref name="td"/>: advances its step count, marking it
        /// learned once <see cref="TrainableDef.steps"/> is reached, and resets the decay clock (RimWorld:
        /// any session with the animal counts as attention, not only the def being worked on this time).
        /// </summary>
        public void Train(TrainableDef td, Pawn? trainer)
        {
            if (td == null) throw new ArgumentNullException(nameof(td));
            lastTrainedTick = Find.TickManager.TicksGame;
            int cur = GetSteps(td) + 1;
            steps[td] = cur;
            if (cur >= td.steps) learned[td] = true;
            trainer?.skills?.Learn(Work.SkillDefOf.Animals, AnimalTuning.TrainXp);
        }

        /// <summary>
        /// Periodic upkeep (RimWorld: <c>Pawn_TrainingTracker</c>'s learned-training degrade): an untended
        /// animal can lose a step it already earned. Gated by
        /// <see cref="AnimalTuning.TrainingDecayCheckIntervalTicks"/> via <see cref="Pawn.IsHashIntervalTick"/>,
        /// so this is a periodic hash-interval check, never per-tick work. "Untended" means no
        /// <see cref="Train"/> call landed within <see cref="AnimalTuning.TrainingDecayGraceTicks"/> of now;
        /// inside that grace window nothing decays regardless of the MTB roll. Neither the grace window nor
        /// the per-check MTB (<see cref="AnimalTuning.TrainingDecayMtbDays"/>) is a RimWorld-sourced number —
        /// this port's own stand-in, pinned by <c>AITests</c> only on trend (decay never happens inside the
        /// grace window; it can happen well outside it).
        /// </summary>
        public void TrainingTrackerTick()
        {
            if (!pawn.IsHashIntervalTick(AnimalTuning.TrainingDecayCheckIntervalTicks)) return;
            if (steps.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (lastTrainedTick >= 0 && now - lastTrainedTick < AnimalTuning.TrainingDecayGraceTicks) return;

            var keys = new List<TrainableDef>(steps.Keys);
            foreach (TrainableDef td in keys)
            {
                int cur = steps[td];
                if (cur <= 0) continue;
                if (!Rand.MTBEventOccurs(AnimalTuning.TrainingDecayMtbDays, GenDate.TicksPerDay, AnimalTuning.TrainingDecayCheckIntervalTicks)) continue;
                cur--;
                steps[td] = cur;
                if (cur < td.steps) learned[td] = false;
            }
        }

        public void ExposeData()
        {
            Dictionary<TrainableDef, int>? s = steps;
            Scribe_Collections.Look(ref s, "steps", LookMode.Def, LookMode.Value);
            steps = s ?? new Dictionary<TrainableDef, int>();

            Dictionary<TrainableDef, bool>? l = learned;
            Scribe_Collections.Look(ref l, "learned", LookMode.Def, LookMode.Value);
            learned = l ?? new Dictionary<TrainableDef, bool>();

            Scribe_Values.Look(ref lastTrainedTick, "lastTrainedTick", -1);
        }
    }
}
