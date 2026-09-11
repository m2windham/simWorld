using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>One line of the narrator's record (SimWorld hook: the chronicle other systems/UI can read out).</summary>
    public sealed class ChronicleEntry : IExposable
    {
        public int tick;
        public string incidentDefName = "";
        public string targetLabel = "";
        public float points;

        /// <summary>Set only on a death entry (see <see cref="Storyteller.RecordDeath"/>); null otherwise. A
        /// small enum, never a string — see <see cref="Pawns.DeathCause"/>.</summary>
        public DeathCause? deathCause;

        /// <summary>Whether <see cref="MomentCurator"/> judged this entry worth remembering — a first, a
        /// turning point, or a broken record (tracker item <c>quests.moments</c>). See that class's own doc for
        /// the three rules; most chronicle entries never set this.</summary>
        public bool isMoment;

        public ChronicleEntry()
        {
        }

        public ChronicleEntry(int tick, string incidentDefName, string targetLabel, float points)
        {
            this.tick = tick;
            this.incidentDefName = incidentDefName;
            this.targetLabel = targetLabel;
            this.points = points;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref incidentDefName, "incidentDefName", "");
            Scribe_Values.Look(ref targetLabel, "targetLabel", "");
            Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref deathCause, "deathCause");
            Scribe_Values.Look(ref isMoment, "isMoment");
        }
    }

    /// <summary>
    /// The threat director (RimWorld: <c>Verse.Storyteller</c>). SimWorld's narrator-facing translation gives it
    /// a persona (<see cref="StorytellerDef.persona"/>) and a running <see cref="Chronicle"/>, but the mechanics —
    /// comps deciding incidents every interval, threat points, adaptation, refire spacing — are RimWorld's.
    /// Register targets with <see cref="RegisterTarget"/> (done automatically by <see cref="CivilizationTarget"/>
    /// when constructed with a storyteller); wire <see cref="StorytellerTick"/> into a tick loop (RimWorld runs it
    /// from the map's post-tickers) to drive it.
    /// </summary>
    public sealed class Storyteller : IExposable
    {
        /// <summary>RimWorld's constant: the storyteller reconsiders incidents every 1000 ticks (1/60 of a day).</summary>
        public const int IncidentCycleLengthTicks = 1000;

        /// <summary>SimWorld hook: how many chronicle entries the narrator's record keeps before the oldest drop off.</summary>
        public const int ChronicleCapacity = 500;

        public StorytellerDef def = null!;
        public DifficultyDef difficulty = null!;
        public StoryWatcher_Adaptation adaptation = new StoryWatcher_Adaptation();
        public IncidentQueue incidentQueue = new IncidentQueue();

        /// <summary>Decides which chronicle entries are worth remembering as a moment (tracker item
        /// <c>quests.moments</c>) — see <see cref="MomentCurator"/>'s own doc.</summary>
        public MomentCurator moments = new MomentCurator();

        /// <summary>Raised after every attempted firing, successful or not.</summary>
        public event Action<FiringIncident>? IncidentFired;

        private readonly List<StorytellerComp> comps = new List<StorytellerComp>();
        private readonly List<IIncidentTarget> allIncidentTargets = new List<IIncidentTarget>();
        private readonly List<ChronicleEntry> chronicle = new List<ChronicleEntry>();

        public IReadOnlyList<StorytellerComp> Comps => comps;
        public IReadOnlyList<IIncidentTarget> AllIncidentTargets => allIncidentTargets;
        public IReadOnlyList<ChronicleEntry> Chronicle => chronicle;

        /// <summary>The civilization's curated history — the chronicle entries <see cref="MomentCurator"/> has
        /// judged worth remembering (tracker item <c>quests.moments</c>). Unlike <see cref="Chronicle"/> this
        /// list is never trimmed by <see cref="ChronicleCapacity"/> — see <see cref="MomentCurator.Moments"/>'s
        /// own doc for why.</summary>
        public IReadOnlyList<ChronicleEntry> Moments => moments.Moments;

        /// <summary>Parameterless for <see cref="Sim.Find"/>'s auto-create; leaves def/difficulty unset until assigned.</summary>
        public Storyteller()
        {
        }

        public Storyteller(StorytellerDef def, DifficultyDef difficulty)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.difficulty = difficulty ?? throw new ArgumentNullException(nameof(difficulty));
            RebuildComps();
        }

        /// <summary>(Re)builds the runtime comp list from <see cref="def"/>. Comps are stateless, so this is safe to call any time.</summary>
        public void RebuildComps()
        {
            comps.Clear();
            if (def?.comps == null) return;
            foreach (StorytellerCompProperties p in def.comps)
            {
                var comp = (StorytellerComp)Activator.CreateInstance(p.compClass)!;
                comp.props = p;
                comps.Add(comp);
            }
        }

        public void RegisterTarget(IIncidentTarget target)
        {
            if (target != null && !allIncidentTargets.Contains(target)) allIncidentTargets.Add(target);
        }

        public void DeregisterTarget(IIncidentTarget target) => allIncidentTargets.Remove(target);

        /// <summary>Call once per game tick (RimWorld runs this from the map's post-tickers). Only acts every <see cref="IncidentCycleLengthTicks"/> ticks.</summary>
        public void StorytellerTick()
        {
            if (Find.TickManager.TicksGame % IncidentCycleLengthTicks != 0) return;
            MakeIncidentsForInterval();
            incidentQueue.IncidentQueueTick();
        }

        /// <summary>Runs every comp against every registered target for the current interval, and ticks adaptation.</summary>
        public void MakeIncidentsForInterval()
        {
            for (int t = 0; t < allIncidentTargets.Count; t++)
            {
                IIncidentTarget target = allIncidentTargets[t];
                for (int c = 0; c < comps.Count; c++)
                {
                    foreach (FiringIncident fi in comps[c].MakeIntervalIncidents(target))
                    {
                        TryFire(fi);
                    }
                }
            }
            adaptation.AdaptationTick(difficulty);
        }

        /// <summary>Executes a firing incident, recording it in the chronicle on success and always raising <see cref="IncidentFired"/>.</summary>
        public bool TryFire(FiringIncident fi)
        {
            if (fi?.def?.Worker == null) return false;
            bool result = fi.def.Worker.TryExecute(fi.parms);
            if (result) RecordChronicle(fi);
            IncidentFired?.Invoke(fi);
            return result;
        }

        private void RecordChronicle(FiringIncident fi)
        {
            var entry = new ChronicleEntry(Find.TickManager.TicksGame, fi.def.defName, DescribeTarget(fi.parms.target), fi.parms.points);
            chronicle.Add(entry);
            while (chronicle.Count > ChronicleCapacity) chronicle.RemoveAt(0);
            // Category = the incident's own defName: stable across every firing of the same incident, so this
            // flags "first raid ever", "first trader caravan ever" and so on — never one moment per firing.
            moments.Consider(entry, fi.def.defName);
        }

        /// <summary>
        /// SimWorld hook: lets a system without its own <see cref="IncidentDef"/> firing (the quest system,
        /// so far) append a free-form line to the chronicle. Reuses <see cref="ChronicleEntry.incidentDefName"/>
        /// to hold the text rather than adding a field, since every consumer already treats that field as the
        /// entry's headline.
        /// <para/>
        /// Hands the entry it wrote back to the caller. Almost every call site ignores it; the one that does
        /// not is <see cref="ChronicleFame"/>, which needs the entry in order to put it through a
        /// <see cref="MomentCurator"/> rule the line's own category cannot express — see that class's doc.
        /// A return value rather than a second overload taking a rule, because the alternative is this method
        /// knowing about every rule there will ever be.
        /// </summary>
        public ChronicleEntry RecordChronicle(string text)
        {
            var entry = new ChronicleEntry(Find.TickManager.TicksGame, text, "", 0f);
            chronicle.Add(entry);
            while (chronicle.Count > ChronicleCapacity) chronicle.RemoveAt(0);
            moments.Consider(entry, MomentCurator.CategoryForFreeform(text));
            return entry;
        }

        /// <summary>
        /// Minimal, dedicated extension of the chronicle for a pawn's death (SimWorld hook, called by
        /// <see cref="FamilyManager.HandleDeath"/>): reuses <see cref="ChronicleEntry.incidentDefName"/> for a
        /// stable headline ("Death") and <see cref="ChronicleEntry.targetLabel"/> for who, the same way
        /// <see cref="RecordChronicle(string)"/> already reuses those fields for free-form lines, and adds only
        /// the one new field a death genuinely needs: <see cref="ChronicleEntry.deathCause"/>.
        /// <paramref name="detail"/> is the pawn's hidden-lifespan-budget history in one short string (see
        /// <see cref="Pawn_AgeTracker.AdjustLifespan"/>) — how the Chronicle ends up able to say *why* someone
        /// lived long or died young — appended to the label only when there is one; a plain death (no
        /// adjustments ever recorded) reads as a plain death.
        /// </summary>
        public void RecordDeath(Pawn pawn, DeathCause cause, string detail = "")
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            string label = string.IsNullOrEmpty(detail) ? pawn.Label : pawn.Label + " (" + detail + ")";
            var entry = new ChronicleEntry(Find.TickManager.TicksGame, "Death", label, 0f) { deathCause = cause };
            chronicle.Add(entry);
            while (chronicle.Count > ChronicleCapacity) chronicle.RemoveAt(0);

            // Two independent rules can both apply to the same death (the first death by this cause, and/or a
            // new longevity record) — MomentCurator's own isMoment guard keeps that a single Moments entry.
            moments.Consider(entry, "Death:" + cause);
            moments.ConsiderDeathRecord(entry, pawn.ageTracker.AgeBiologicalYearsFloat);

            // A death the curator judged worth remembering is exactly "the chronicle singled them out by name"
            // — the trigger Pawn_TierTracker.Notify_ChronicleNamed's own doc says a director-policy decision
            // must supply (deliberately not wired to every RecordDeath call, only to ones that matter).
            if (entry.isMoment) pawn.tier.Notify_ChronicleNamed();
        }

        private static string DescribeTarget(IIncidentTarget target) =>
            target is CivilizationTarget ? "the civilization" : target?.GetUniqueLoadID() ?? "?";

        public void ExposeData()
        {
            StorytellerDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            DifficultyDef? diff = difficulty;
            Scribe_Defs.Look(ref diff, "difficulty");
            difficulty = diff!;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RebuildComps();
            }

            StoryWatcher_Adaptation? a = adaptation;
            Scribe_Deep.Look(ref a, "adaptation");
            adaptation = a ?? new StoryWatcher_Adaptation();

            IncidentQueue? q = incidentQueue;
            Scribe_Deep.Look(ref q, "incidentQueue");
            incidentQueue = q ?? new IncidentQueue();

            MomentCurator? m = moments;
            Scribe_Deep.Look(ref m, "moments");
            moments = m ?? new MomentCurator();

            List<ChronicleEntry>? chronicleList = new List<ChronicleEntry>(chronicle);
            Scribe_Collections.Look(ref chronicleList, "chronicle", LookMode.Deep);
            chronicle.Clear();
            if (chronicleList != null) chronicle.AddRange(chronicleList);
        }
    }
}
