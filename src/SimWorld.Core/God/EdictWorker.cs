using SimWorld.Pawns;

namespace SimWorld.God
{
    /// <summary>
    /// Runtime behaviour behind one <see cref="EdictDef"/> (RimWorld: no equivalent — mirrors
    /// <see cref="Work.WorkGiver"/>/<see cref="Thoughts.ThoughtWorker"/>'s def+worker shape). The base class
    /// does only what <see cref="EdictDef"/>'s own fields already describe declaratively — <see cref="GodManager"/>
    /// applies <see cref="EdictDef.prioritizedWork"/> (via the think tree), <see cref="EdictDef.researchFocus"/>
    /// and <see cref="EdictDef.moodThought"/> itself, without needing a worker override at all. Subclass only
    /// for behaviour those fields cannot express — see <see cref="EdictWorker_ExemptMinors"/>.
    /// </summary>
    public class EdictWorker
    {
        public EdictDef def = null!;

        /// <summary>Called once when <see cref="GodManager.Activate"/> succeeds.</summary>
        public virtual void Notify_Activated()
        {
        }

        /// <summary>Called once when <see cref="GodManager.Deactivate"/> runs, including when a save unloads
        /// while the edict is active — see <see cref="GodManager"/>'s own notes on why that case does not
        /// apply here yet.</summary>
        public virtual void Notify_Deactivated()
        {
        }

        /// <summary>Runs on <see cref="GodManager"/>'s own gated tick (rare/long bucket, not per tick) for
        /// every active edict. The base class has nothing periodic to do; override for an edict whose effect
        /// is ongoing rather than a one-time activation/deactivation step.</summary>
        public virtual void EdictTick()
        {
        }

        /// <summary>Whether this edict's consequences (mood thought, "leave no trace" work push) reach
        /// <paramref name="pawn"/>. Defaults to every citizen who can work at all — every non-Humanlike pawn
        /// (animals) is exempt by default since a civilization's edicts direct its citizens, not its livestock.</summary>
        public virtual bool AppliesTo(Pawn pawn) => pawn != null && pawn.RaceProps.Humanlike;
    }
}
