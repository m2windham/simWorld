using System;
using System.Collections.Generic;
using SimWorld.Defs;
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
    /// <para/>
    /// <b>Reacts to era transitions.</b> Subscribes to <see cref="ResearchManager.EraReached"/>
    /// (<see cref="EnsureSubscribedToEraTransitions"/>) so an edict never simply outlives the age it was
    /// issued in: <see cref="EdictDef.obsoleteEra"/> auto-deactivates on reaching it (own Chronicle line each,
    /// since each is a distinct piece of news), and newly-issuable edicts get one summary Chronicle line (never
    /// one per edict, and never a line at all when nothing unlocked). See
    /// <see cref="EnsureSubscribedToEraTransitions"/>'s own doc for why the subscription is idempotent rather
    /// than assumed to happen exactly once.
    /// </summary>
    public sealed class GodManager : IExposable
    {
        /// <summary>Every issued edict, in force or suspended: what holds a slot. Saved as "activeEdicts",
        /// its name from before suspension existed, so older saves load unchanged.</summary>
        private List<EdictDef> activeEdicts = new List<EdictDef>();

        /// <summary>Issued edicts whose <see cref="EdictDef.requiredOffice"/> is vacant (offices.edicts).</summary>
        private List<EdictDef> suspendedEdicts = new List<EdictDef>();

        /// <summary>Issued and not suspended; derived from the two lists above, never saved.</summary>
        private readonly List<EdictDef> inForce = new List<EdictDef>();
        private int lastGodTick = int.MinValue;

        /// <summary>The tick each issued edict was issued on, by defName: when an edict with a
        /// <see cref="EdictDef.durationDays"/> lapses. Saved.</summary>
        private Dictionary<string, int> issuedAtTick = new Dictionary<string, int>();

        /// <summary>The tick each edict with a <see cref="EdictDef.cooldownDays"/> can be issued again, by
        /// defName. Written when it ends; an entry in the past is spent. Saved.</summary>
        private Dictionary<string, int> availableAgainTick = new Dictionary<string, int>();
        private AttentionManager attention = new AttentionManager();

        /// <summary>The edicts in force right now: issued and not suspended. What every consumer of an
        /// edict's effect reads, so a suspended edict stops acting without any of them knowing why.</summary>
        public IReadOnlyList<EdictDef> ActiveEdicts => inForce;

        /// <summary>Every issued edict, suspended ones included: what counts against
        /// <see cref="GodTuning.MaxActiveEdicts"/>.</summary>
        public IReadOnlyList<EdictDef> IssuedEdicts => activeEdicts;

        /// <summary>Issued edicts waiting on a vacant seat.</summary>
        public IReadOnlyList<EdictDef> SuspendedEdicts => suspendedEdicts;

        /// <summary>
        /// Which settlement the god is looking at, and the only thing that drives
        /// <c>Pawns.Pawn_TierTracker.Notify_AttentionChanged</c> (spec §11.2/§11.3). Lives here rather than
        /// directly on <see cref="Game"/> because attention <i>is</i> the god's — this manager is already the
        /// god layer's state, is already deep-Scribed by the game, is already ticked by it
        /// (<see cref="GodTick"/>), and is already what the host's own seam resolves through
        /// (<see cref="View.GodCommands"/> reads <see cref="Find.God"/>). See
        /// <see cref="AttentionManager"/>'s own doc for what "attention" was defined to mean and why.
        /// </summary>
        public AttentionManager Attention => attention;

        /// <summary>The cached civilization-state aggregate the god view reads (§10's "rollup"). Not
        /// round-tripped through <see cref="ExposeData"/>: it is a derived cache recomputable from the living
        /// population at any time, the same way <c>SituationalThoughtHandler</c>'s own cache is never Scribed.</summary>
        public GodRollup Rollup { get; } = new GodRollup();

        /// <summary>
        /// Every code path that produces a live <see cref="GodManager"/> — <see cref="Find.God"/>'s first
        /// access, and a Scribe load (<see cref="Scribe.Load{T}(string,string,DefDatabase)"/> constructs a
        /// bare instance and calls <see cref="ExposeData"/> on it) — must end up subscribed to
        /// <see cref="ResearchManager.EraReached"/> exactly once, against whichever <see cref="ResearchManager"/>
        /// is live on this thread right now. So this constructor subscribes for the "brand new civilization"
        /// path, and <see cref="ExposeData"/>'s <see cref="LoadSaveMode.PostLoadInit"/> branch calls the same
        /// method again for the "loaded save" path — re-subscribing against whatever <c>Find.ResearchManager</c>
        /// resolves to once loading has settled, which is the one that matters. Both calls go through
        /// <see cref="EnsureSubscribedToEraTransitions"/>.
        /// </summary>
        public GodManager()
        {
            EnsureSubscribedToEraTransitions();
        }

        /// <summary>
        /// Unsubscribes then resubscribes <see cref="OnEraReached"/> against <c>Find.ResearchManager</c> —
        /// deliberately idempotent (never additive) rather than a bare <c>+=</c>, so calling it more than once
        /// (construction, then a Scribe load of the same instance, then a second load in the same test) can
        /// never leave two live subscriptions doubling every Chronicle line a transition writes. The
        /// unsubscribe is a safe no-op the first time it runs (removing a delegate that was never added).
        /// </summary>
        /// <remarks>
        /// Known edge, deliberately not guarded against: subscribing hands the <c>ResearchManager</c> a strong
        /// reference to this instance, so assigning a new <c>Find.God</c> <i>without</i> a <see cref="Find.Reset"/>
        /// in between leaves the old manager subscribed and narrating alongside the new one. A liveness check
        /// here would fix that at the cost of making any manager that is not <c>Find.God</c> silently inert —
        /// including a freshly Scribe-loaded one before it is installed — which is the worse surprise and the
        /// harder bug to see. <see cref="Find.Reset"/> is how a game hands over, and it drops both services
        /// together.
        /// </remarks>
        private void EnsureSubscribedToEraTransitions()
        {
            Find.ResearchManager.EraReached -= OnEraReached;
            Find.ResearchManager.EraReached += OnEraReached;
        }

        /// <summary>In force: issued and not suspended.</summary>
        public bool IsActive(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return activeEdicts.Contains(def) && !suspendedEdicts.Contains(def);
        }

        /// <summary>Issued, whether in force or suspended.</summary>
        public bool IsIssued(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return activeEdicts.Contains(def);
        }

        /// <summary>Issued, but waiting on its <see cref="EdictDef.requiredOffice"/>.</summary>
        public bool IsSuspended(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return suspendedEdicts.Contains(def);
        }

        /// <summary>
        /// Whether some living citizen of the civilization holds <paramref name="office"/> right now. The
        /// civilization is every settlement in the world, the same set <see cref="View.GodViewSnapshot"/> and
        /// <see cref="GodRollup"/> report as the god's. No world means no seat.
        /// </summary>
        public static bool IsSeated(Offices.OfficeDef office)
        {
            if (office == null) throw new ArgumentNullException(nameof(office));
            World.World? world = Find.World;
            if (world == null) return false;
            foreach (World.WorldObject obj in world.worldObjects)
            {
                if (obj is World.Settlement settlement && Offices.OfficeManager.HolderIn(office, settlement) != null) return true;
            }
            return false;
        }

        /// <summary>The words a refusal or a suspension uses for a vacant seat: "Needs a seated Steward."</summary>
        public static string NeedsSeatedReason(Offices.OfficeDef office) =>
            "Needs a seated " + office.role.LabelCap + ".";

        /// <summary>
        /// True when <paramref name="def"/> could be activated right now: not already active, the civilization
        /// has reached <see cref="EdictDef.requiredEra"/> (via <see cref="ResearchManager.CurrentEra"/>'s own
        /// order, which walks the ladder from the start rather than taking the highest vacuously-complete era —
        /// see that property's own doc), it has not yet reached <see cref="EdictDef.obsoleteEra"/>, and a slot
        /// remains under <see cref="GodTuning.MaxActiveEdicts"/>. In practice an edict past its own
        /// <see cref="EdictDef.obsoleteEra"/> is never found active in the first place — the era-transition
        /// handler deactivates it the moment that era is reached — but this still refuses a manual
        /// <see cref="Activate"/> for one that somehow is (defensive, not load-bearing).
        /// </summary>
        public bool CanActivate(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (IsIssued(def)) return false;
            if (activeEdicts.Count >= GodTuning.MaxActiveEdicts) return false;
            if (CooldownTicksLeft(def) > 0) return false;
            EraDef? current = Find.ResearchManager.CurrentEra;
            if (def.requiredEra != null)
            {
                if (current == null || current.order < def.requiredEra.order) return false;
            }
            if (def.obsoleteEra != null)
            {
                if (current != null && current.order >= def.obsoleteEra.order) return false;
            }
            if (def.requiredOffice != null && !IsSeated(def.requiredOffice)) return false;
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

            // Contradictory edicts are mutually exclusive (EdictDef.Contradicts): the newer order stands, and
            // the chronicle says which one it replaced.
            foreach (EdictDef contradicted in ContradictedBy(def))
            {
                DeactivateInternal(contradicted, "Edict rescinded: " + contradicted.LabelCap + ", contradicted by " + def.LabelCap);
            }

            activeEdicts.Add(def);
            issuedAtTick[def.defName] = Find.TickManager.TicksGame;
            RebuildInForce();

            if (def.researchFocus != null && !Find.ResearchManager.IsFinished(def.researchFocus))
            {
                Find.ResearchManager.CurrentProj = def.researchFocus;
            }

            def.Worker.Notify_Activated();
            Find.Storyteller.RecordChronicle("Edict issued: " + def.LabelCap);
            return true;
        }

        /// <summary>The issued edicts (in force or suspended) that <paramref name="def"/> contradicts, which
        /// <see cref="Activate"/> would rescind.</summary>
        public List<EdictDef> ContradictedBy(EdictDef def)
        {
            var found = new List<EdictDef>();
            for (int i = 0; i < activeEdicts.Count; i++)
            {
                if (def.Contradicts(activeEdicts[i])) found.Add(activeEdicts[i]);
            }
            return found;
        }

        /// <summary>Deactivates <paramref name="def"/>; false (no-op) if it was not active. Never touches
        /// <c>Pawn_WorkSettings</c> or removes the mood thought directly — both read live activation state, so
        /// there is nothing here to undo on that side; only the worker's own hook and the Chronicle line fire.</summary>
        public bool Deactivate(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return DeactivateInternal(def, "Edict rescinded: " + def.LabelCap);
        }

        /// <summary>Shared removal step for both a deliberate <see cref="Deactivate"/> and an automatic
        /// era-obsolescence deactivation (<see cref="OnEraReached"/>) — same mechanics, different Chronicle
        /// wording, so the "why" is legible either way instead of every rescission reading identically.</summary>
        private bool DeactivateInternal(EdictDef def, string chronicleLine)
        {
            if (!activeEdicts.Remove(def)) return false;
            issuedAtTick.Remove(def.defName);
            if (def.cooldownDays > 0f)
            {
                availableAgainTick[def.defName] = Find.TickManager.TicksGame + (int)(def.cooldownDays * GenDate.TicksPerDay);
            }
            // A suspended edict already had its worker told it stopped, when it was suspended.
            bool wasInForce = !suspendedEdicts.Remove(def);
            RebuildInForce();
            if (wasInForce) def.Worker.Notify_Deactivated();
            Find.Storyteller.RecordChronicle(chronicleLine);
            return true;
        }

        /// <summary>
        /// Self-gated the same way <c>Storyteller.StorytellerTick</c> and <c>FamilyManager.DemographyTick</c>
        /// are: a no-op except once every <see cref="GodTuning.GodTickIntervalTicks"/>, so a caller can wire it
        /// into a per-tick loop without the god layer costing anything most ticks. Runs every active edict's
        /// <see cref="EdictWorker.EdictTick"/> — the base worker has nothing periodic to do, so this is a
        /// no-op unless an edict overrides it — and then reconciles attention
        /// (<see cref="AttentionManager.Reconcile"/>), which is what actually holds the Full-tier population
        /// down to the settlement the god is looking at.
        /// </summary>
        public void GodTick()
        {
            int now = Find.TickManager.TicksGame;
            if (lastGodTick != int.MinValue && now - lastGodTick < GodTuning.GodTickIntervalTicks) return;
            lastGodTick = now;
            // An era whose research was done turns here once the town meets its conditions
            // (ResearchManager.TryAdvanceEra); a project finishing turns it too, without waiting for this.
            Find.ResearchManager.TryAdvanceEra();
            LapseEdictsThatRanTheirCourse(now);
            ReconcileSuspensions();
            for (int i = 0; i < inForce.Count; i++)
            {
                inForce[i].Worker.EdictTick();
            }

            attention.Reconcile();
        }

        /// <summary>Ticks of cooldown left before <paramref name="def"/> can be issued again; 0 when none.</summary>
        public int CooldownTicksLeft(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!availableAgainTick.TryGetValue(def.defName, out int at)) return 0;
            return Math.Max(0, at - Find.TickManager.TicksGame);
        }

        /// <summary>Ticks before an issued edict with a <see cref="EdictDef.durationDays"/> lapses, or null for one
        /// that stands until rescinded, or is not issued.</summary>
        public int? TicksUntilLapse(EdictDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.durationDays <= 0f || !issuedAtTick.TryGetValue(def.defName, out int at)) return null;
            return Math.Max(0, at + (int)(def.durationDays * GenDate.TicksPerDay) - Find.TickManager.TicksGame);
        }

        /// <summary>
        /// Ends every issued edict whose <see cref="EdictDef.durationDays"/> has run out, with a chronicle line of
        /// its own, and grants its <see cref="EdictDef.completedThought"/> to every living citizen it applies to.
        /// A suspended edict's clock keeps running: it was issued for a span, and a vacant seat does not extend
        /// it. Public so a test or a host can settle it without waiting for the cadence.
        /// </summary>
        public void LapseEdictsThatRanTheirCourse(int now)
        {
            for (int i = activeEdicts.Count - 1; i >= 0; i--)
            {
                EdictDef def = activeEdicts[i];
                if (def.durationDays <= 0f || !issuedAtTick.TryGetValue(def.defName, out int at)) continue;
                if (now < at + (int)(def.durationDays * GenDate.TicksPerDay)) continue;
                bool wasInForce = !suspendedEdicts.Contains(def);
                DeactivateInternal(def, "Edict ran its course: " + def.LabelCap + ".");
                if (wasInForce && def.completedThought != null) GrantCompletedThought(def);
            }
        }

        private static void GrantCompletedThought(EdictDef def)
        {
            World.World? world = Find.World;
            if (world == null) return;
            foreach (World.WorldObject obj in world.worldObjects)
            {
                if (!(obj is World.Settlement settlement)) continue;
                IReadOnlyList<Pawns.Pawn> roster = settlement.Citizens;
                for (int c = 0; c < roster.Count; c++)
                {
                    Pawns.Pawn p = roster[c];
                    if (p.Dead || p.needs?.mood == null || !def.Worker.AppliesTo(p)) continue;
                    p.needs.mood.thoughts.memories.TryGainMemory(def.completedThought!);
                }
            }
        }

        /// <summary>
        /// Suspends every issued edict whose <see cref="EdictDef.requiredOffice"/> has fallen vacant and
        /// resumes every suspended one whose seat is filled again, each with its own chronicle line: the
        /// near-miss and the recovery are both news. Runs on <see cref="GodTick"/>, the same long cadence the
        /// office sweep (<see cref="Offices.OfficeManager.Tick"/>) refills seats on, and a seat emptied by a
        /// sweep stays empty until the next one (<see cref="Offices.OfficeManager"/>'s interregnum), so a
        /// vacancy is seen. Public so a test or a host can settle it without waiting for the cadence.
        /// </summary>
        public void ReconcileSuspensions()
        {
            bool changed = false;
            for (int i = 0; i < activeEdicts.Count; i++)
            {
                EdictDef def = activeEdicts[i];
                if (def.requiredOffice == null) continue;
                bool seated = IsSeated(def.requiredOffice);
                bool suspended = suspendedEdicts.Contains(def);
                if (!seated && !suspended)
                {
                    suspendedEdicts.Add(def);
                    changed = true;
                    def.Worker.Notify_Deactivated();
                    Find.Storyteller.RecordChronicle(
                        "Edict suspended: " + def.LabelCap + ". " + NeedsSeatedReason(def.requiredOffice));
                }
                else if (seated && suspended)
                {
                    suspendedEdicts.Remove(def);
                    changed = true;
                    def.Worker.Notify_Activated();
                    Find.Storyteller.RecordChronicle(
                        "Edict resumed: " + def.LabelCap + ", now a " + def.requiredOffice.role.label + " is seated.");
                }
            }
            if (changed) RebuildInForce();
        }

        private void RebuildInForce()
        {
            inForce.Clear();
            for (int i = 0; i < activeEdicts.Count; i++)
            {
                if (!suspendedEdicts.Contains(activeEdicts[i])) inForce.Add(activeEdicts[i]);
            }
        }

        /// <summary>
        /// Reacts to the civilization entering a new era (<see cref="ResearchManager.EraReached"/>): retires
        /// every active edict this era makes obsolete, then — separately — announces whatever this era newly
        /// makes issuable. The two are independent passes over different sets (active edicts vs. every loaded
        /// edict) and each writes its own Chronicle line(s), so one civilization-wide event can legibly produce
        /// zero, one, or several lines depending what actually changed.
        /// </summary>
        private void OnEraReached(EraDef? from, EraDef to)
        {
            _ = from; // Only the era just reached matters here — see DeactivateEdictsObsoleteAt/ChronicleNewlyUnlockedEdicts.

            DeactivateEdictsObsoleteAt(to);
            ChronicleNewlyUnlockedEdicts(to);
        }

        /// <summary>
        /// Deactivates every active edict whose <see cref="EdictDef.obsoleteEra"/> the civilization has now
        /// reached or passed (<c>to.order &gt;= obsoleteEra.order</c> — see that field's own doc for why this
        /// is <c>&gt;=</c>, not <c>&gt;</c>). Walks backwards over <see cref="activeEdicts"/> so removing
        /// entries in place never skips a neighbour, and each deactivation gets its own line naming which era
        /// retired it — never folded into the "newly unlocked" summary line, because "this is gone" and "these
        /// are new" are different pieces of news even on the same transition.
        /// </summary>
        private void DeactivateEdictsObsoleteAt(EraDef to)
        {
            for (int i = activeEdicts.Count - 1; i >= 0; i--)
            {
                EdictDef def = activeEdicts[i];
                if (def.obsoleteEra != null && to.order >= def.obsoleteEra.order)
                {
                    DeactivateInternal(def, "Edict outgrown: " + def.LabelCap + " (" + to.LabelCap + ").");
                }
            }
        }

        /// <summary>
        /// One Chronicle line naming every loaded edict whose <see cref="EdictDef.requiredEra"/> is exactly
        /// the era just reached — the edicts this transition is the reason a civilization can newly issue.
        /// Exact-match rather than "requiredEra.order &lt;= to.order" deliberately: <see cref="ResearchManager"/>
        /// fires <see cref="ResearchManager.EraReached"/> once per era crossed even when one project finishes
        /// several at once, so every edict is caught by exactly one of those events regardless of how many
        /// fire together, with no double-count and no gap. Writes nothing at all when the set is empty — a
        /// transition unlocking no edict must not spam the Chronicle, per the brief.
        /// </summary>
        private void ChronicleNewlyUnlockedEdicts(EraDef to)
        {
            List<string>? unlocked = null;
            foreach (EdictDef def in DefDatabase<EdictDef>.AllDefsListForReading)
            {
                if (def.requiredEra != to) continue;
                (unlocked ??= new List<string>()).Add(def.LabelCap);
            }
            if (unlocked == null) return;

            Find.Storyteller.RecordChronicle(
                "New edicts available with the " + to.LabelCap + " age: " + string.Join(", ", unlocked) + ".");
        }

        public void ExposeData()
        {
            List<EdictDef>? list = activeEdicts;
            Scribe_Collections.Look(ref list, "activeEdicts", LookMode.Def);
            activeEdicts = list ?? new List<EdictDef>();
            List<EdictDef>? suspended = suspendedEdicts;
            Scribe_Collections.Look(ref suspended, "suspendedEdicts", LookMode.Def);
            suspendedEdicts = suspended ?? new List<EdictDef>();
            RebuildInForce();
            Scribe_Values.Look(ref lastGodTick, "lastGodTick", int.MinValue);
            Dictionary<string, int>? issued = issuedAtTick;
            Scribe_Collections.Look(ref issued, "issuedAtTick", LookMode.Value, LookMode.Value);
            issuedAtTick = issued ?? new Dictionary<string, int>();
            Dictionary<string, int>? again = availableAgainTick;
            Scribe_Collections.Look(ref again, "availableAgainTick", LookMode.Value, LookMode.Value);
            availableAgainTick = again ?? new Dictionary<string, int>();

            // One int (the focused world tile). Nothing here has to be re-applied to citizens after a load:
            // each citizen's own attention flag and tier round-trip through Pawn_TierTracker.ExposeData, so
            // the focus and the population it holds at Full come back already agreeing with each other —
            // which matters, because the world this tile names is deep-loaded after this manager is.
            AttentionManager? att = attention;
            Scribe_Deep.Look(ref att, "attention");
            attention = att ?? new AttentionManager();

            // Re-establish the EraReached subscription against whatever ResearchManager is live on this
            // thread once loading has settled — see EnsureSubscribedToEraTransitions's own doc. A loaded
            // GodManager that skipped this would sit there active but permanently deaf to every future era
            // transition: no crash, no test failure without one written for it, just edicts that quietly
            // stop reacting to the ladder the moment a save round-trips.
            if (Scribe.mode == LoadSaveMode.PostLoadInit) EnsureSubscribedToEraTransitions();
        }
    }
}
