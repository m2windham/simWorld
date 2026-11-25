using System;
using System.Collections.Generic;
using System.Xml.Linq;
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

        /// <summary>The era the civilization is in (<see cref="CurrentEra"/>). Null until first read, and then
        /// derived from research alone: a new game, or a save from before the era was kept.</summary>
        private EraDef? currentEra;

        /// <summary>Raised whenever a project finishes, whether from work, <see cref="FinishProject"/>, or debug.</summary>
        public event Action<ResearchProjectDef>? ProjectFinished;

        /// <summary>
        /// Raised when the civilization enters a new era: the era left behind (null only when the ladder had
        /// no era below the one reached) and the era entered. Fires exactly once per era crossed, from a project
        /// finishing or from <see cref="TryAdvanceEra"/> finding an era's conditions met — never from loading a
        /// save and never from setting a game up.
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
            // FinishProject writes the finished progress itself and reads the era before that write, so the
            // finishing amount must not be written here first or it never sees an era crossing.
            if (newProgress >= currentProj.baseCost)
            {
                ResearchProjectDef finished = currentProj;
                FinishProject(finished, false, researcher);
                ResearchFinishedLetter.Send(finished, researcher);
                return;
            }
            progress[currentProj] = newProgress;
        }

        /// <summary>
        /// Marks <paramref name="def"/> finished outright: sets its progress to its full cost, clears it as
        /// <see cref="CurrentProj"/> if it was current, and raises <see cref="ProjectFinished"/>.
        /// </summary>
        public void FinishProject(ResearchProjectDef def, bool doCompletionDialog = false, Pawn? researcher = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            // Read before the progress write, so a new game's first read derives the era from the research it
            // had, not from this project. Loading a save can never re-fire history: the era is kept, and only
            // TryAdvanceEra moves it.
            _ = CurrentEra;

            progress[def] = def.baseCost;
            if (currentProj == def) currentProj = null;
            Notify_ResearchProjectFinished(def);
            ProjectFinished?.Invoke(def);
            TryAdvanceEra();
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
        /// Moves the civilization on from <see cref="CurrentEra"/> while the way out of it is open
        /// (<see cref="EraDef.GateOpen"/>: its research spine finished and its conditions met), firing
        /// <see cref="EraReached"/> once for every era entered. One call can cross more than one era (the player
        /// may have researched ahead), and each is its own moment, so each gets its own event. Called when a
        /// project finishes and on the god layer's long tick (<see cref="God.GodManager.GodTick"/>), which is how
        /// an era whose research was done turns once the town meets its conditions. The ladder never runs
        /// backwards: a condition that fails after the turn does not take the era away.
        /// </summary>
        /// <returns>Whether the civilization entered at least one era.</returns>
        public bool TryAdvanceEra()
        {
            EraDef? from = CurrentEra;
            if (from == null) return false;
            EraConditionContext? context = null;
            bool advanced = false;
            while (true)
            {
                EraDef? next = NextEra;
                if (next == null || !from.IsComplete) break;
                if (from.conditions.Count > 0)
                {
                    context ??= EraConditionContext.Now();
                    if (!from.ConditionsMet(context)) break;
                }
                currentEra = next;
                advanced = true;
                AdvanceTechLevelToEra();
                Notify_EraReached(from, next);
                EraReached?.Invoke(from, next);
                from = next;
            }
            return advanced;
        }

        /// <summary>The era research alone puts a civilization in: walking the ladder from the start, the
        /// earliest era whose research spine is not finished, or the last era once every one is.</summary>
        private static EraDef? ResearchEra()
        {
            List<EraDef> ordered = OrderedEras();
            if (ordered.Count == 0) return null;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!ordered[i].IsComplete) return ordered[i];
            }
            return ordered[ordered.Count - 1];
        }

        /// <summary>Setting a game up skips the conditions: it says where history begins, it does not live
        /// through it. Silent, as <see cref="SetProjectFinishedForSetup"/> is, and never backwards.</summary>
        private void AdvanceEraForSetup()
        {
            EraDef? research = ResearchEra();
            if (research == null) return;
            EraDef? current = CurrentEra;
            if (current == null || research.order > current.order) currentEra = research;
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
        /// Raises <see cref="ResearcherTechLevel"/> to the tech level of the era the civilization has mastered
        /// (<see cref="MasteredEra"/>, the one before the era it is in), never lowering it. Mastered, not current:
        /// a civilization in the Bronze age is learning bronze, and pays RimWorld's above-your-level premium on
        /// it until it has, which is the price the era pacing was derived with.
        ///
        /// Without this the level was set once at scenario start and never again, so a civilization that
        /// climbed from a tribal start went on paying <see cref="ResearchProjectDef.CostFactor"/>'s
        /// above-your-level penalty — measured at 2.5x across the whole authored tree — for research it was by
        /// then entirely capable of (<c>docs/research/tech-reachability.md</c> §5.5). That tax was doing pacing
        /// work by accident; the honest lever for pacing is project cost, not a stale wire.
        /// </summary>
        public void AdvanceTechLevelToEra()
        {
            TechLevel eraLevel = MasteredEra?.techLevel ?? TechLevel.Undefined;
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
            AdvanceEraForSetup();
            AdvanceTechLevelToEra();
        }

        /// <summary>
        /// Debug/testing: instantly finishes every project this civilization can see. Endless extension is
        /// suppressed while it runs — otherwise finishing the last authored project would open an age, which
        /// this loop would then finish, which would open another, forever. The civilization is left exactly
        /// where the authored tree ends, with nothing to research, which is the state endless tech is the
        /// answer to.
        /// <para/>
        /// "Every project this civilization can see" (<see cref="AllProjects"/>) never includes another
        /// civilization's generated tail any more — each game's endless projects live in its own register —
        /// so calling this no longer risks finishing a tail this civilization never lived.
        /// </summary>
        public void DebugSetAllProjectsFinished()
        {
            // A snapshot, because FinishProject can now add projects to the register.
            var all = new List<ResearchProjectDef>(AllProjects);
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
        /// <b>The era the civilization is in</b>: the age whose research it is learning and whose edicts it may
        /// issue (<c>docs/design/era-progression.md</c> §1). A new game is in the first era. The civilization
        /// leaves an era for the next once that era's research spine is finished and its conditions are met
        /// (<see cref="EraDef.GateOpen"/>, <see cref="TryAdvanceEra"/>), so the next era is the one it is working
        /// toward (<see cref="NextEra"/>) and the one before is the one it has mastered (<see cref="MasteredEra"/>).
        /// A scenario that starts in an era has finished every era before it.
        /// <para/>
        /// Kept as state, not re-derived on every read: conditions read the settlement, which can fall back, and
        /// an age once entered is not taken away. The first read of a new game, or of a save from before the era
        /// was kept, derives it from research alone: the earliest era whose spine is unfinished, walking from the
        /// start of the ladder (an era past the authored content has no projects and is vacuously complete, so
        /// the walk must not skip ahead to it).
        /// <para/>
        /// <b>Was</b> the last era complete, falling back to the first while even that one was not, so a fresh
        /// start was "in" an era it had not finished and every later game was "in" the last one it had. An
        /// Agrarian village start was then in Sticks and stones and refused the Agrarian edicts.
        /// </summary>
        public EraDef? CurrentEra
        {
            get
            {
                currentEra ??= ResearchEra();
                return currentEra;
            }
        }

        /// <summary>The era the civilization is working toward: the one immediately after <see cref="CurrentEra"/>
        /// by order, or null in the last era.</summary>
        public EraDef? NextEra => Adjacent(+1);

        /// <summary>The era the civilization has mastered: the one immediately before <see cref="CurrentEra"/>, or
        /// null in the first era. Research is priced at its tech level (<see cref="AdvanceTechLevelToEra"/>).</summary>
        public EraDef? MasteredEra => Adjacent(-1);

        private EraDef? Adjacent(int step)
        {
            EraDef? current = CurrentEra;
            if (current == null) return null;
            List<EraDef> ordered = OrderedEras();
            int index = ordered.IndexOf(current) + step;
            return index >= 0 && index < ordered.Count ? ordered[index] : null;
        }

        public void ExposeData()
        {
            // First, and deliberately: a generated project is a real Def, but one that lives in this game's
            // own endlessDatabase rather than DefDatabase.Global, so every reference below — the current
            // project, every key of the progress dictionary — can only resolve once the seed and the age
            // count are known and the tail has been re-minted into it. Read those two, rebuild, then read the
            // references that point into it.
            Scribe_Values.Look(ref endlessSeed, "endlessSeed");
            Scribe_Values.Look(ref endlessSeedCaptured, "endlessSeedCaptured", false);
            Scribe_Values.Look(ref endlessAges, "endlessAges");
            if (Scribe.mode == LoadSaveMode.LoadingVars) RemintEndlessTree();

            // Not Scribe_Defs / Scribe_Collections(LookMode.Def): those resolve a saved defName against
            // ScribeLoader.Defs alone (the session's static content database), which never holds an endless
            // project — see EndlessResearch's own doc on why one must never be written into it. The XML shape
            // is identical either way (a defName string), so this is a change to how a loaded name resolves,
            // not to what a save contains.
            ExposeProjectRef(ref currentProj, "currentProj");

            Dictionary<string, float>? progressByName = Scribe.mode == LoadSaveMode.Saving ? ToNameKeyed(progress) : null;
            Scribe_Collections.Look(ref progressByName, "progress", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) progress = FromNameKeyed(progressByName);

            Scribe_Values.Look(ref researcherTechLevel, "researcherTechLevel", TechLevel.Neolithic);
            // Absent from a save made before the era was kept: left null, and the first read derives it.
            Scribe_Defs.Look(ref currentEra, "currentEra");
        }

        /// <summary>Saves/loads a single research project reference by defName, resolving against
        /// <see cref="GetProject"/> — authored or this game's own endless one — rather than against
        /// <see cref="ScribeLoader.Defs"/>, which only ever knows about authored content.</summary>
        private void ExposeProjectRef(ref ResearchProjectDef? project, string label)
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                    Scribe.saver.WriteElement(label, project == null ? "null" : project.defName);
                    break;

                case LoadSaveMode.LoadingVars:
                {
                    XElement? node = Scribe.loader.CurParent?.Element(label);
                    string name = node?.Value?.Trim() ?? "";
                    if (node == null || name.Length == 0 || name == "null" || ScribeExtractor.IsNull(node))
                    {
                        project = null;
                    }
                    else
                    {
                        project = GetProject(name);
                        if (project == null)
                        {
                            Scribe.loader.Error("Could not load reference to ResearchProjectDef named '" + name + "' for '" + label + "'.");
                        }
                    }
                    break;
                }
            }
        }

        private static Dictionary<string, float> ToNameKeyed(Dictionary<ResearchProjectDef, float> source)
        {
            var byName = new Dictionary<string, float>(source.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<ResearchProjectDef, float> entry in source)
            {
                byName[entry.Key.defName] = entry.Value;
            }
            return byName;
        }

        /// <summary>The inverse of <see cref="ToNameKeyed"/>: resolves each saved name against
        /// <see cref="GetProject"/>. A name neither the authored tree nor this game's own tail recognizes is
        /// reported and dropped, rather than silently discarded the way the generic Def-lookMode collection
        /// helper would (it casts an unresolved reference to null and <c>BuildDictionary</c> skips a null key).</summary>
        private Dictionary<ResearchProjectDef, float> FromNameKeyed(Dictionary<string, float>? byName)
        {
            var result = new Dictionary<ResearchProjectDef, float>();
            if (byName == null) return result;
            foreach (KeyValuePair<string, float> entry in byName)
            {
                ResearchProjectDef? def = GetProject(entry.Key);
                if (def == null)
                {
                    Scribe.loader.Error("Could not load reference to ResearchProjectDef named '" + entry.Key + "' in 'progress'.");
                    continue;
                }
                result[def] = entry.Value;
            }
            return result;
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
        /// Where this game's own endless projects live — never <see cref="DefDatabase.Global"/>. A generated
        /// project is a real <see cref="ResearchProjectDef"/>, so every consumer that already handles an
        /// authored one keeps working unchanged; it is just registered here instead of in the process-wide
        /// database. <see cref="DefDatabase.Global"/> is written only when content loads, and is shared by
        /// every game in the process, so a second game sharing it must never be handed the first one's
        /// invented tail (the defect <c>EndlessResearch</c>'s own doc used to accept and paper over with
        /// <see cref="EndlessResearch.BelongsTo"/>). Not saved: <see cref="RemintEndlessTree"/> rebuilds it
        /// from <see cref="endlessSeed"/> and <see cref="endlessAges"/> on load, before anything below tries
        /// to resolve a reference into it.
        /// </summary>
        private readonly DefDatabase endlessDatabase = new DefDatabase();

        /// <summary>
        /// Every research project this civilization can see: the authored tree — shared process-wide,
        /// loaded once into <see cref="DefDatabase.Global"/> at content load — plus every project this game
        /// itself has minted past its end (research.endless, in <see cref="endlessDatabase"/>). The one place
        /// a reader asks for "every project" instead of <see cref="DefDatabase{T}"/> directly: since nothing
        /// ever mints an endless project into the shared database any more, this is also the reason a second
        /// game in the same process can no longer see this one's tail through any reader.
        /// </summary>
        public IReadOnlyList<ResearchProjectDef> AllProjects
        {
            get
            {
                IReadOnlyList<ResearchProjectDef> authored = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
                IReadOnlyList<ResearchProjectDef> mine = endlessDatabase.For<ResearchProjectDef>().AllDefsListForReading;
                if (mine.Count == 0) return authored;

                var all = new List<ResearchProjectDef>(authored.Count + mine.Count);
                all.AddRange(authored);
                all.AddRange(mine);
                return all;
            }
        }

        /// <summary>The project named <paramref name="defName"/> — authored or this game's own endless one —
        /// or null when neither register has it. What a save resolves a research project reference against
        /// (see <see cref="ExposeProjectRef"/>/<see cref="FromNameKeyed"/>) and what a player picks a project
        /// by name through (<c>GodCommands.SetResearchProject</c>).</summary>
        public ResearchProjectDef? GetProject(string defName)
        {
            if (defName == null) throw new ArgumentNullException(nameof(defName));
            return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName)
                ?? endlessDatabase.For<ResearchProjectDef>().GetNamedSilentFail(defName);
        }

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
        /// <para/><see cref="AllProjects"/> already excludes another civilization's generated tail — each
        /// game's endless projects live in its own register, never the process-wide <see cref="DefDatabase"/>
        /// — so this no longer has to filter one out by seed the way it once did.
        /// </summary>
        public bool NothingLeftToResearch
        {
            get
            {
                IReadOnlyList<ResearchProjectDef> all = AllProjects;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].CanStartNow) return false;
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
                        EndlessResearch.Existing(ages, tracks[i], endlessAges - 1, 0, EndlessSeed, endlessDatabase);
                    if (foundation == null || !IsFinished(foundation)) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Opens one more age past the authored tree. Ordinarily called for you — see
        /// <see cref="EnsureSomethingToResearch"/> — but public so a scenario or a test can reach past the
        /// authored tree deliberately. Does nothing once <see cref="MaxEndlessAges"/> ages exist.
        /// </summary>
        public void ExtendEndlessTree()
        {
            EndlessAgeDef? ages = AgeRegister;
            IReadOnlyList<EndlessResearchDef> tracks = DefDatabase<EndlessResearchDef>.AllDefsListForReading;
            if (ages == null || tracks.Count == 0) return;
            if (endlessAges >= MaxEndlessAges(ages, tracks)) return;

            int opened = endlessAges;
            endlessAges++;
            EndlessResearch.MintAge(ages, tracks, opened, EndlessSeed, endlessDatabase);
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
                int before = endlessAges;
                ExtendEndlessTree();
                if (endlessAges == before) break; // at MaxEndlessAges: nothing more will open
            }
        }

        /// <summary>
        /// The most ages this civilization's tail can hold: as many whole ages as fit in one
        /// <see cref="DefDatabase"/> registry, because every generated project is a real
        /// <see cref="ResearchProjectDef"/> and <see cref="Def.index"/> is a <c>ushort</c>
        /// (<see cref="DefDatabase.MaxDefsPerType"/>). With the shipped six tracks of four that is 2730 ages —
        /// over 65,000 generated projects, far past anything a game researches — so "endless" ends here
        /// rather than in an <see cref="OverflowException"/> halfway through minting an age. An age is
        /// never half-opened: the bound counts whole ages.
        /// </summary>
        public static int MaxEndlessAges(EndlessAgeDef ages, IReadOnlyList<EndlessResearchDef> tracks)
        {
            if (ages == null) throw new ArgumentNullException(nameof(ages));
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            int perAge = tracks.Count * ages.projectsPerTrack;
            return perAge <= 0 ? 0 : DefDatabase.MaxDefsPerType / perAge;
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
        /// Idempotent: an age already in <see cref="endlessDatabase"/> is returned rather than duplicated, so
        /// loading twice into one process is harmless.
        /// </summary>
        private void RemintEndlessTree()
        {
            if (endlessAges <= 0) return;
            EndlessAgeDef? ages = AgeRegister;
            IReadOnlyList<EndlessResearchDef> tracks = DefDatabase<EndlessResearchDef>.AllDefsListForReading;
            if (ages == null || tracks.Count == 0) return;

            for (int ageIndex = 0; ageIndex < endlessAges; ageIndex++)
            {
                EndlessResearch.MintAge(ages, tracks, ageIndex, EndlessSeed, endlessDatabase);
            }
        }
    }
}
