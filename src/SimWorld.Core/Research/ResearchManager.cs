using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// Tracks research progress and the currently active project (RimWorld: <c>RimWorld.ResearchManager</c>).
    /// Reached ambiently through <see cref="Find.ResearchManager"/>, the same way <see cref="Find.TickManager"/> is.
    /// </summary>
    public class ResearchManager : IExposable
    {
        /// <summary>Research points earned per work tick at 100% work speed (RimWorld: <c>ResearchManager.ResearchPointsPerWorkTick</c>).</summary>
        public const float ResearchPointsPerWorkTick = 0.00825f;

        private ResearchProjectDef? currentProj;
        private Dictionary<ResearchProjectDef, float> progress = new Dictionary<ResearchProjectDef, float>();
        private TechLevel researcherTechLevel = TechLevel.Neolithic;

        /// <summary>Raised whenever a project finishes, whether from work, <see cref="FinishProject"/>, or debug.</summary>
        public event Action<ResearchProjectDef>? ProjectFinished;

        /// <summary>
        /// Raised when the civilization enters a new era: the era left behind (null only when the ladder had
        /// no era below the one reached) and the era reached. Fires exactly once per era crossed, and only
        /// from a project actually finishing — never from loading a save.
        /// </summary>
        public event Action<EraDef?, EraDef>? EraReached;

        public ResearchProjectDef? CurrentProj
        {
            get => currentProj;
            set => currentProj = value;
        }

        /// <summary>
        /// Tech level the current researcher(s) work at, feeding <see cref="ResearchProjectDef.CostFactor"/>.
        /// Defaults to Neolithic. A scenario sets the starting floor
        /// (<c>ScenPart_StartingEra</c>); after that it rises on its own as the era ladder advances — see
        /// <see cref="AdvanceTechLevelToEra"/>. It is never lowered, so a civilization that has learned to
        /// work at an age does not forget how.
        /// </summary>
        public TechLevel ResearcherTechLevel
        {
            get => researcherTechLevel;
            set => researcherTechLevel = value;
        }

        /// <summary>Converts raw work ticks into research points at the base rate; applies no cost factor.</summary>
        public static float PointsFromWorkTicks(float workTicks) => workTicks * ResearchPointsPerWorkTick;

        public float GetProgress(ResearchProjectDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return progress.TryGetValue(def, out float value) ? value : 0f;
        }

        public bool IsFinished(ResearchProjectDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return GetProgress(def) >= def.baseCost;
        }

        public float ProgressPercent(ResearchProjectDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return def.baseCost <= 0f ? 1f : GenMath.Clamp01(GetProgress(def) / def.baseCost);
        }

        /// <summary>Not finished, and every visible and hidden prerequisite is finished.</summary>
        public bool CanBeResearchedNow(ResearchProjectDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return def.CanStartNow;
        }

        /// <summary>
        /// Adds <paramref name="amount"/> research points (already in points, e.g. from <see cref="PointsFromWorkTicks"/>)
        /// to <see cref="CurrentProj"/>, reduced by its <see cref="ResearchProjectDef.CostFactor"/> against
        /// <see cref="ResearcherTechLevel"/>. Finishes the project once its cost is reached. A no-op with no current project.
        /// </summary>
        public void ResearchPerformed(float amount, Pawn? researcher)
        {
            if (currentProj == null) return;
            float factor = currentProj.CostFactor(researcherTechLevel);
            float applied = factor > 0f ? amount / factor : amount;
            float newProgress = GetProgress(currentProj) + applied;
            progress[currentProj] = newProgress;
            if (newProgress >= currentProj.baseCost)
            {
                FinishProject(currentProj, false, researcher);
            }
        }

        /// <summary>
        /// Marks <paramref name="def"/> finished outright: sets its progress to its full cost, clears it as
        /// <see cref="CurrentProj"/> if it was current, and raises <see cref="ProjectFinished"/>.
        /// </summary>
        public void FinishProject(ResearchProjectDef def, bool doCompletionDialog = false, Pawn? researcher = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            // Read the era before the progress write, not from a persisted "last era" field: the era a
            // civilization is in is derived from what it has finished, so the honest before/after pair is
            // the one that brackets the mutation. It also means loading a save can never re-fire history —
            // nothing outside this method moves the ladder.
            EraDef? eraBefore = CurrentEra;

            progress[def] = def.baseCost;
            if (currentProj == def) currentProj = null;
            Notify_ResearchProjectFinished(def);
            ProjectFinished?.Invoke(def);
            CheckEraTransition(eraBefore);
            _ = doCompletionDialog; // UI concern only; SimWorld has no completion dialog yet.
            _ = researcher; // kept for parity with RimWorld's call sites and future credit/letter text.
        }

        /// <summary>Hook for subclasses or later systems to react to a finished project without subscribing to the event.</summary>
        protected virtual void Notify_ResearchProjectFinished(ResearchProjectDef def)
        {
            AdvanceTechLevelToEra();
        }

        /// <summary>
        /// Fires <see cref="EraReached"/> once for every era the civilization crossed by finishing a project.
        /// One project can complete more than one era at a time — a later era whose spine was already done
        /// becomes reachable the moment the era below it completes — and each of those is a separate moment
        /// in the civilization's history, so each gets its own event rather than one event for the jump.
        /// </summary>
        private void CheckEraTransition(EraDef? eraBefore)
        {
            EraDef? eraNow = CurrentEra;
            if (eraNow == null || eraNow == eraBefore) return;

            List<EraDef> ordered = OrderedEras();
            int fromIndex = eraBefore != null ? ordered.IndexOf(eraBefore) : -1;
            int toIndex = ordered.IndexOf(eraNow);
            if (toIndex <= fromIndex) return; // the ladder never runs backwards; progress only ever grows.

            EraDef? previous = eraBefore;
            for (int i = fromIndex + 1; i <= toIndex; i++)
            {
                EraDef reached = ordered[i];
                Notify_EraReached(previous, reached);
                EraReached?.Invoke(previous, reached);
                previous = reached;
            }
        }

        /// <summary>
        /// Hook for subclasses to react to the civilization entering a new era. The base implementation
        /// writes the Chronicle line and the letter (see <see cref="EraTransitionUtility"/>); override to
        /// replace those, or subscribe to <see cref="EraReached"/> to add to them.
        /// </summary>
        protected virtual void Notify_EraReached(EraDef? from, EraDef to)
        {
            EraTransitionUtility.Notify_EraReached(from, to);
        }

        /// <summary>
        /// Raises <see cref="ResearcherTechLevel"/> to the tech level of the era the civilization has actually
        /// reached, never lowering it.
        ///
        /// Without this the level was set once at scenario start and never again, so a civilization that
        /// climbed from a tribal start went on paying <see cref="ResearchProjectDef.CostFactor"/>'s
        /// above-your-level penalty — measured at 2.5x across the whole authored tree — for research it was by
        /// then entirely capable of (<c>docs/research/tech-reachability.md</c> §5.5). That tax was doing pacing
        /// work by accident; the honest lever for pacing is project cost, not a stale wire.
        /// </summary>
        public void AdvanceTechLevelToEra()
        {
            TechLevel eraLevel = CurrentEra?.techLevel ?? TechLevel.Undefined;
            if (eraLevel > researcherTechLevel) researcherTechLevel = eraLevel;
        }

        /// <summary>
        /// Marks a project finished as part of setting a game up, with none of the consequences a project
        /// finishing during play has: no <see cref="ProjectFinished"/>, no <see cref="EraReached"/>, and so no
        /// chronicle line and no letter. A scenario that starts a civilization in the bronze age is saying
        /// where its history begins, not narrating a bronze age the player watched it reach — announcing
        /// those would open every game with a stack of era letters for eras nobody lived through.
        /// <see cref="AdvanceTechLevelToEra"/> still runs, so the civilization researches at the level the
        /// seeded progress earns it.
        /// </summary>
        public void SetProjectFinishedForSetup(ResearchProjectDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            progress[def] = def.baseCost;
            if (currentProj == def) currentProj = null;
            AdvanceTechLevelToEra();
        }

        /// <summary>Debug/testing: instantly finishes every loaded project.</summary>
        public void DebugSetAllProjectsFinished()
        {
            foreach (ResearchProjectDef def in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (!IsFinished(def)) FinishProject(def);
            }
        }

        /// <summary>RimWorld re-scans mod-added projects on load; SimWorld's content is not moddable yet, so this is a no-op.</summary>
        public void ReapplyAllMods()
        {
        }

        /// <summary>Every loaded era, earliest first. A later, still-empty era (no seed content yet) sorts after one with content.</summary>
        private static List<EraDef> OrderedEras()
        {
            var eras = new List<EraDef>(DefDatabase<EraDef>.AllDefsListForReading);
            eras.Sort((a, b) => a.order.CompareTo(b.order));
            return eras;
        }

        /// <summary>
        /// The furthest era reached by unbroken progression from the start of the ladder: the last era in
        /// order such that it and every era before it are complete, or the earliest era when even that one
        /// is not complete yet. Walking from the start (rather than simply taking the highest complete era)
        /// matters because an era past the authored seed content has no projects and so is vacuously
        /// "complete" (see <see cref="EraDef.Progress"/>) without a civilization ever having reached it.
        /// </summary>
        public EraDef? CurrentEra
        {
            get
            {
                List<EraDef> ordered = OrderedEras();
                if (ordered.Count == 0) return null;
                EraDef current = ordered[0];
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (!ordered[i].IsComplete) break;
                    current = ordered[i];
                }
                return current;
            }
        }

        /// <summary>The era immediately after <see cref="CurrentEra"/> by order, or null at (or past) the last era.</summary>
        public EraDef? NextEra
        {
            get
            {
                List<EraDef> ordered = OrderedEras();
                EraDef? current = CurrentEra;
                if (current == null) return null;
                int index = ordered.IndexOf(current);
                return index >= 0 && index + 1 < ordered.Count ? ordered[index + 1] : null;
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref currentProj, "currentProj");
            Dictionary<ResearchProjectDef, float>? dict = progress;
            Scribe_Collections.Look(ref dict, "progress", LookMode.Def, LookMode.Value);
            progress = dict ?? new Dictionary<ResearchProjectDef, float>();
            Scribe_Values.Look(ref researcherTechLevel, "researcherTechLevel", TechLevel.Neolithic);
        }
    }
}
