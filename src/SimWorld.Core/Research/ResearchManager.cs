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
        /// <see cref="ResearcherTechLevel"/> and scaled by the difficulty's
        /// <see cref="Director.DifficultyDef.researchSpeedFactor"/>. Finishes the project once its cost is
        /// reached. A no-op with no current project.
        /// <para/>
        /// The difficulty factor goes here rather than on <see cref="Stats.StatDefOf.ResearchSpeed"/> — the
        /// same placement RimWorld uses — for two reasons: this is the one funnel every source of research
        /// progress passes through (a work tick today, anything else later), and the stat is a property *of a
        /// pawn*, which a game-wide setting is not. A pawn's research speed should not change because the
        /// player picked a harder game.
        /// </summary>
        public void ResearchPerformed(float amount, Pawn? researcher)
        {
            if (currentProj == null) return;
            float factor = currentProj.CostFactor(researcherTechLevel);
            float applied = factor > 0f ? amount / factor : amount;
            applied *= Director.DifficultyUtility.ResearchSpeedFactor;
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
            // After the era check, so a crossing is announced against the authored ladder before anything is
            // minted past its end (research.endless).
            EnsureSomethingToResearch();
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

        /// <summary>
        /// Debug/testing: instantly finishes every loaded project. Endless extension is suppressed while it
        /// runs — otherwise finishing the last authored project would open an age, which this loop would then
        /// finish, which would open another, forever. The civilization is left exactly where the authored tree
        /// ends, with nothing to research, which is the state endless tech is the answer to.
        /// <para/>
        /// Note that "every loaded project" includes another civilization's generated tail if one is in the
        /// same process, which is why a test that wants a clean start on the tail seeds the authored tree with
        /// <see cref="SetProjectFinishedForSetup"/> instead.
        /// </summary>
        public void DebugSetAllProjectsFinished()
        {
            // A snapshot, because FinishProject can now add projects to the database.
            var all = new List<ResearchProjectDef>(DefDatabase<ResearchProjectDef>.AllDefsListForReading);
            suppressEndlessExtension = true;
            try
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (!IsFinished(all[i])) FinishProject(all[i]);
                }
            }
            finally
            {
                suppressEndlessExtension = false;
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
            // First, and deliberately: a generated project is a real Def in the database, so every reference
            // below — the current project, every key of the progress dictionary — can only resolve once the
            // seed and the age count are known and the tail has been re-minted. Read those two, rebuild, then
            // read the references that point into it.
            Scribe_Values.Look(ref endlessSeed, "endlessSeed");
            Scribe_Values.Look(ref endlessSeedCaptured, "endlessSeedCaptured", false);
            Scribe_Values.Look(ref endlessAges, "endlessAges");
            if (Scribe.mode == LoadSaveMode.LoadingVars) RemintEndlessTree();

            Scribe_Defs.Look(ref currentProj, "currentProj");
            Dictionary<ResearchProjectDef, float>? dict = progress;
            Scribe_Collections.Look(ref dict, "progress", LookMode.Def, LookMode.Value);
            progress = dict ?? new Dictionary<ResearchProjectDef, float>();
            Scribe_Values.Look(ref researcherTechLevel, "researcherTechLevel", TechLevel.Neolithic);
        }

        // ---- Endless tech (research.endless) ----

        private int endlessAges;

        private int endlessSeed;

        /// <summary>Whether <see cref="endlessSeed"/> is the seed this civilization's tail was actually
        /// derived from, or merely a zero nobody has resolved yet.</summary>
        private bool endlessSeedCaptured;

        /// <summary>Set while a bulk finish is in progress; see <see cref="DebugSetAllProjectsFinished"/>.
        /// Not saved: it is only ever true inside one call.</summary>
        private bool suppressEndlessExtension;

        /// <summary>
        /// Raised when the civilization opens an age past the end of the authored ladder: the age's index and
        /// its generated label. Fires once per age, and only from an age actually being opened during play —
        /// never from loading a save, which re-mints the same ages silently.
        /// </summary>
        public event Action<int, string>? EndlessAgeOpened;

        /// <summary>
        /// How many ages exist past the authored tree. Zero for every civilization that has not finished it,
        /// which is nearly all of them — endless tech costs nothing until it is reached.
        /// </summary>
        public int EndlessAges => endlessAges;

        /// <summary>How many generated projects exist along each track: the tail's depth.</summary>
        public int EndlessDepth => AgeRegister == null ? 0 : endlessAges * AgeRegister.projectsPerTrack;

        /// <summary>
        /// The label of the furthest age this civilization has opened, or null while it is still inside the
        /// authored ladder. The god view's continuation of <see cref="CurrentEra"/>: past Exotic, this is what
        /// a civilization would say the present age is called.
        /// </summary>
        public string? CurrentEndlessAgeLabel =>
            endlessAges <= 0 || AgeRegister == null ? null : AgeRegister.LabelFor(endlessAges - 1, EndlessSeed);

        /// <summary>
        /// The seed the generated tail is derived from: the world's own, captured the first time anything asks
        /// and saved from then on.
        ///
        /// <para/>Before this existed the tail was a pure function of content, so every civilization that ever
        /// finished the authored tree researched the same inventions in the same order. Deriving from the
        /// world seed instead is what makes two games diverge past the ladder and two games on one seed agree;
        /// it is saved rather than re-read from the world so that re-minting on load cannot depend on which
        /// manager the save happens to restore first.
        /// </summary>
        public int EndlessSeed
        {
            get
            {
                if (!endlessSeedCaptured)
                {
                    endlessSeed = Find.World?.info.seed ?? 0;
                    endlessSeedCaptured = true;
                }
                return endlessSeed;
            }
            set
            {
                endlessSeed = value;
                endlessSeedCaptured = true;
            }
        }

        /// <summary>The one register the endless tail names its ages in, or null when content ships none.</summary>
        private static EndlessAgeDef? AgeRegister
        {
            get
            {
                IReadOnlyList<EndlessAgeDef> all = DefDatabase<EndlessAgeDef>.AllDefsListForReading;
                return all.Count > 0 ? all[0] : null;
            }
        }

        /// <summary>
        /// True when nothing this civilization could be working on can be started: everything reachable is
        /// finished. This is the condition endless tech exists for, and it is deliberately about
        /// <i>reachability</i> rather than about completion — a project whose prerequisites can never be met
        /// is not something a civilization is still able to work on.
        ///
        /// <para/>Generated projects belonging to <i>another</i> civilization are skipped. The
        /// <see cref="DefDatabase"/> is process-wide and outlives a game, so without that a second game in the
        /// same process reads the first one's unfinished tail as its own remaining work and never extends past
        /// the authored tree at all.
        /// </summary>
        public bool NothingLeftToResearch
        {
            get
            {
                int seed = EndlessSeed;
                IReadOnlyList<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ResearchProjectDef project = all[i];
                    if (EndlessResearch.IsGenerated(project) && !EndlessResearch.BelongsTo(project, seed)) continue;
                    if (project.CanStartNow) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// True once every foundation of the newest age is finished: the civilization has pushed the leading
        /// edge of the tail as far as it currently goes, whether or not it bothered with that age's optional
        /// work. False while no age exists at all.
        /// </summary>
        public bool EndlessFrontierReached
        {
            get
            {
                EndlessAgeDef? ages = AgeRegister;
                if (ages == null || endlessAges <= 0) return false;
                IReadOnlyList<EndlessResearchDef> tracks = DefDatabase<EndlessResearchDef>.AllDefsListForReading;
                if (tracks.Count == 0) return false;

                // Asks, never mints: a property that quietly extended the tree by being read would make the
                // frontier depend on who looked at it.
                for (int i = 0; i < tracks.Count; i++)
                {
                    ResearchProjectDef? foundation =
                        EndlessResearch.Existing(ages, tracks[i], endlessAges - 1, 0, EndlessSeed, DefDatabase.Global);
                    if (foundation == null || !IsFinished(foundation)) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Opens one more age past the authored tree. Ordinarily called for you — see
        /// <see cref="EnsureSomethingToResearch"/> — but public so a scenario or a test can reach past the
        /// authored tree deliberately.
        /// </summary>
        public void ExtendEndlessTree()
        {
            EndlessAgeDef? ages = AgeRegister;
            IReadOnlyList<EndlessResearchDef> tracks = DefDatabase<EndlessResearchDef>.AllDefsListForReading;
            if (ages == null || tracks.Count == 0) return;

            int opened = endlessAges;
            endlessAges++;
            EndlessResearch.MintAge(ages, tracks, opened, EndlessSeed, DefDatabase.Global);
            Notify_EndlessAgeOpened(ages, opened);
            EndlessAgeOpened?.Invoke(opened, ages.LabelFor(opened, EndlessSeed));
        }

        /// <summary>
        /// Hook for subclasses to react to an age opening. The base implementation writes the chronicle line
        /// and the letter (see <see cref="EndlessAgeUtility"/>); override to replace those, or subscribe to
        /// <see cref="EndlessAgeOpened"/> to add to them.
        /// </summary>
        protected virtual void Notify_EndlessAgeOpened(EndlessAgeDef ages, int ageIndex)
        {
            EndlessAgeUtility.Notify_AgeOpened(ages, ageIndex, EndlessSeed);
        }

        /// <summary>
        /// Opens an age when the civilization has reached the frontier — either because there is nothing at
        /// all left to start, or because it has finished every foundation of the newest age and the tail's
        /// leading edge is where it is standing. Called after every finished project, so a civilization that
        /// has exhausted the authored tree always has a next thing to work on and never silently stops, which
        /// is what "endless" has to mean mechanically.
        ///
        /// <para/>The second condition is what stops the tail collapsing into a queue. Without it an age only
        /// opens once every optional project in the one before it has been bought, so there is never a choice
        /// between pressing on and consolidating — the same failure the authored tree was restructured to fix
        /// (<c>docs/research/tech-reachability.md</c> §11).
        /// </summary>
        public void EnsureSomethingToResearch()
        {
            if (suppressEndlessExtension) return;
            if (AgeRegister == null) return;
            if (DefDatabase<EndlessResearchDef>.AllDefsListForReading.Count == 0) return;

            // A loop rather than a single extension: a civilization loading a save, or one whose scenario
            // seeded it deep, can be several ages behind where its own progress already stands.
            int guard = 0;
            while ((NothingLeftToResearch || EndlessFrontierReached) && guard++ < MaxEndlessCatchUpAges)
            {
                ExtendEndlessTree();
            }
        }

        /// <summary>A bound on the catch-up loop above, so a content set with no workable track cannot spin
        /// forever minting projects nothing can start.</summary>
        private const int MaxEndlessCatchUpAges = 64;

        /// <summary>
        /// Rebuilds every age a save says exists, without announcing any of them: a loaded civilization has
        /// already lived its ages, and narrating them on load would open every reloaded game with a stack of
        /// letters for history the player watched happen. The same rule
        /// <see cref="SetProjectFinishedForSetup"/> follows for a seeded era.
        /// <para/>
        /// Idempotent: an age already in the database is returned rather than duplicated, so loading twice
        /// into one process is harmless.
        /// </summary>
        private void RemintEndlessTree()
        {
            if (endlessAges <= 0) return;
            EndlessAgeDef? ages = AgeRegister;
            IReadOnlyList<EndlessResearchDef> tracks = DefDatabase<EndlessResearchDef>.AllDefsListForReading;
            if (ages == null || tracks.Count == 0) return;

            for (int ageIndex = 0; ageIndex < endlessAges; ageIndex++)
            {
                EndlessResearch.MintAge(ages, tracks, ageIndex, EndlessSeed, DefDatabase.Global);
            }
        }
    }
}
