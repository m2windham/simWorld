using System;
using System.Collections.Generic;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.God
{
    /// <summary>
    /// Holds the civilization's active edicts (<c>docs/spec/simworld-spec.md</c> §10) — reached ambiently
    /// through <see cref="Find.God"/>, the same service-locator shape as <see cref="Find.Storyteller"/> and
    /// <see cref="Find.ResearchManager"/>. RimWorld has nothing to port here: the player there drafts
    /// individual colonists rather than issuing standing civilization-scale directives, so this whole class is
    /// SimWorld's own translation (recorded in <c>docs/status.json</c>'s <c>god</c> system entry).
    /// </summary>
    public sealed class GodManager : IExposable
    {
        private List<EdictDef> activeEdicts = new List<EdictDef>();
        private int lastGodTick = int.MinValue;

        public IReadOnlyList<EdictDef> ActiveEdicts => activeEdicts;

        /// <summary>The cached civilization-state aggregate the god view reads (§10's "rollup"). Not
        /// round-tripped through <see cref="ExposeData"/>: it is a derived cache recomputable from the living
        /// population at any time, the same way <c>SituationalThoughtHandler</c>'s own cache is never Scribed.</summary>
        public GodRollup Rollup { get; } = new GodRollup();

        public bool IsActive(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return activeEdicts.Contains(def);
        }

        /// <summary>
        /// True when <paramref name="def"/> could be activated right now: not already active, the civilization
        /// has reached <see cref="EdictDef.requiredEra"/> (via <see cref="ResearchManager.CurrentEra"/>'s own
        /// order, which walks the ladder from the start rather than taking the highest vacuously-complete era —
        /// see that property's own doc), and a slot remains under <see cref="GodTuning.MaxActiveEdicts"/>.
        /// </summary>
        public bool CanActivate(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (IsActive(def)) return false;
            if (activeEdicts.Count >= GodTuning.MaxActiveEdicts) return false;
            if (def.requiredEra != null)
            {
                EraDef? current = Find.ResearchManager.CurrentEra;
                if (current == null || current.order < def.requiredEra.order) return false;
            }
            return true;
        }

        /// <summary>
        /// Activates <paramref name="def"/>: refuses per <see cref="CanActivate"/>, otherwise applies its
        /// declarative fields (sets <see cref="EdictDef.researchFocus"/> as the current project, when it isn't
        /// already finished), calls its worker's <see cref="EdictWorker.Notify_Activated"/>, and records the
        /// Chronicle line. The think-tree push (<see cref="EdictDef.prioritizedWork"/>) and the mood
        /// consequence (<see cref="EdictDef.moodThought"/>) need no activation-time step at all — both read
        /// <see cref="IsActive"/> live (<c>SimWorld.AI.JobGiver_Edicts</c> and
        /// <see cref="ThoughtWorker_UnderEdict"/> respectively), which is exactly what lets
        /// <see cref="Deactivate"/> leave no trace.
        /// </summary>
        public bool Activate(EdictDef def)
        {
            if (!CanActivate(def)) return false;

            activeEdicts.Add(def);

            if (def.researchFocus != null && !Find.ResearchManager.IsFinished(def.researchFocus))
            {
                Find.ResearchManager.CurrentProj = def.researchFocus;
            }

            def.Worker.Notify_Activated();
            Find.Storyteller.RecordChronicle("Edict issued: " + def.LabelCap);
            return true;
        }

        /// <summary>Deactivates <paramref name="def"/>; false (no-op) if it was not active. Never touches
        /// <c>Pawn_WorkSettings</c> or removes the mood thought directly — both read live activation state, so
        /// there is nothing here to undo on that side; only the worker's own hook and the Chronicle line fire.</summary>
        public bool Deactivate(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!activeEdicts.Remove(def)) return false;

            def.Worker.Notify_Deactivated();
            Find.Storyteller.RecordChronicle("Edict rescinded: " + def.LabelCap);
            return true;
        }

        /// <summary>
        /// Self-gated the same way <c>Storyteller.StorytellerTick</c> and <c>FamilyManager.DemographyTick</c>
        /// are: a no-op except once every <see cref="GodTuning.GodTickIntervalTicks"/>, so a caller can wire it
        /// into a per-tick loop without the god layer costing anything most ticks. Runs every active edict's
        /// <see cref="EdictWorker.EdictTick"/> — the base worker has nothing periodic to do, so this is a
        /// no-op unless an edict overrides it.
        /// </summary>
        public void GodTick()
        {
            int now = Find.TickManager.TicksGame;
            if (lastGodTick != int.MinValue && now - lastGodTick < GodTuning.GodTickIntervalTicks) return;
            lastGodTick = now;

            for (int i = 0; i < activeEdicts.Count; i++)
            {
                activeEdicts[i].Worker.EdictTick();
            }
        }

        public void ExposeData()
        {
            List<EdictDef>? list = activeEdicts;
            Scribe_Collections.Look(ref list, "activeEdicts", LookMode.Def);
            activeEdicts = list ?? new List<EdictDef>();
            Scribe_Values.Look(ref lastGodTick, "lastGodTick", int.MinValue);
        }
    }
}
