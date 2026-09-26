using System.Collections.Generic;

using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// A group of pawns on a map acting as one — for this port, a raid (RimWorld: <c>Verse.AI.Group.Lord</c>).
    /// It holds the roster, runs its <see cref="LordJob"/>'s graph, and on each transition hands every member
    /// the new toil's duty. The one thing it adds to what <see cref="DutyDef"/> already did alone is memory
    /// of the group: how many it arrived with and how many it has lost, which is what "half of us are down,
    /// run" needs and no single pawn can know.
    ///
    /// <para/><b>Read from two decompiles</b>, RimWorld 1.0 (josh-m/RW-Decompile) and 1.6
    /// (32bitx64bit/RW-Decompiled-1.6), <c>Verse.AI.Group/Lord.cs</c>. Ported: the roster and its two counters,
    /// <c>SetJob</c> (including the flee toil it adds to every graph), <c>AddPawn</c>, <c>Notify_PawnLost</c>,
    /// <c>GotoToil</c>, <c>LordTick</c>, <c>CheckTransitionOnSignal</c>, and the save shape (toil and trigger
    /// data by index, the graph rebuilt on load). Not ported: everything a raid does not use — memos, building
    /// and pawn-damage signals, voluntarily-joinable lords, extra forbidden things, quest signals.
    ///
    /// <para/><b>The lord watches its members rather than being told.</b> RimWorld calls
    /// <c>Notify_PawnLost</c> from <c>Pawn_HealthTracker.MakeDowned</c>, <c>Pawn.Kill</c> and
    /// <c>Pawn.ExitMap</c>. Here the lord looks at its own roster at the top of every <see cref="LordTick"/>
    /// and raises the same notification for any member that is down, dead, gone from the map or no longer of
    /// its faction. The loss arrives up to one tick later than RimWorld's and in roster order rather than
    /// event order; neither changes which transition fires, because the one trigger that reads losses counts
    /// them. What it buys is that no health, death or despawn path anywhere in the port has to know lords
    /// exist.
    /// </summary>
    public sealed class Lord : IExposable, ILoadReferenceable
    {
        public LordManager? lordManager;

        public int loadID = -1;

        /// <summary>Seeds <see cref="LordJob.CreateGraph"/>. Saved, so the graph a load rebuilds — a raid's
        /// give-up time included — is the graph the lord had. RimWorld seeds the same call from the load id
        /// alone; see <see cref="LordMaker"/> for why this also carries the world.</summary>
        public int graphSeed;

        public Faction? faction;

        public List<Pawn> ownedPawns = new List<Pawn>();

        /// <summary>Everyone this lord has ever held — the denominator of
        /// <see cref="Trigger_FractionPawnsLost"/>.</summary>
        public int numPawnsEverGained;

        /// <summary>Members lost downed, dead or captured — the numerator. Walking off the map does not
        /// count.</summary>
        public int numPawnsLostViolently;

        public int ticksInToil;

        private LordJob? curJob;
        private StateGraph? graph;
        private LordToil? curLordToil;

        // Load-time only: which toil the lord was in and what its triggers remembered, keyed by index.
        private int tmpCurLordToilIdx = -1;
        private Dictionary<int, TriggerData>? tmpTriggerData;

        public LordJob? LordJob => curJob;

        public StateGraph? Graph => graph;

        public LordToil? CurLordToil => curLordToil;

        public Map.Map? Map => lordManager?.map;

        public string GetUniqueLoadID() => "Lord_" + loadID;

        /// <summary>
        /// Takes <paramref name="lordJob"/> and builds its graph (RimWorld: <c>Lord.SetJob</c>). This is also
        /// where the flee comes from: for a faction whose def sets <see cref="FactionDef.autoFlee"/>, every toil
        /// in the graph gains a high-priority transition to <see cref="LordToil_PanicFlee"/> on
        /// <see cref="Trigger_FractionPawnsLost"/>, with the message <c>MessageFightersFleeing</c>, "{0} from
        /// {1} are fleeing."
        ///
        /// <para/><b>The fraction is 1.0's.</b> RimWorld 1.0 passes a flat <c>0.5f</c>. 1.6 rolls it per lord
        /// from <c>FactionDef.attackersDownPercentageRangeForAutoFlee</c> (default 0.4~0.7, seeded by the load
        /// id). This port takes the 1.0 number because <see cref="FactionRaidRules.WithdrawAfterLosingFraction"/>
        /// already is that number for a raid resolved off the map, and one threshold read by both paths cannot
        /// drift apart; the 1.6 range would be a per-lord roll here and a field on the faction def.
        /// </summary>
        public void SetJob(LordJob lordJob)
        {
            curJob = lordJob;
            curLordToil = null;
            lordJob.lord = this;

            Rand.PushState(graphSeed);
            try
            {
                graph = lordJob.CreateGraph();
            }
            finally
            {
                Rand.PopState();
            }

            if (faction != null && !faction.def.isPlayer && faction.def.autoFlee && lordJob.AddFleeToil)
            {
                var panicFlee = new LordToil_PanicFlee();
                for (int i = 0; i < graph.lordToils.Count; i++)
                {
                    var flee = new Transition(graph.lordToils[i], panicFlee);
                    flee.AddPreAction(new TransitionAction_Message(
                        "Raiders fleeing",
                        TransitionAction_Message.PawnsPluralCap(faction) + " from " + faction.name + " are fleeing."));
                    flee.AddTrigger(new Trigger_FractionPawnsLost(FactionRaidRules.WithdrawAfterLosingFraction));
                    graph.AddTransition(flee, highPriority: true);
                }
                graph.AddToil(panicFlee);
            }

            for (int i = 0; i < graph.lordToils.Count; i++) graph.lordToils[i].lord = this;
        }

        /// <summary>Moves to <paramref name="newLordToil"/> and hands out its duty (RimWorld: <c>Lord.GotoToil</c>).</summary>
        public void GotoToil(LordToil newLordToil)
        {
            LordToil? previousToil = curLordToil;
            curLordToil?.Cleanup();
            curLordToil = newLordToil;
            ticksInToil = 0;
            newLordToil.lord = this;
            newLordToil.Init();
            if (graph != null)
            {
                for (int i = 0; i < graph.transitions.Count; i++)
                {
                    Transition transition = graph.transitions[i];
                    if (transition.sources.Contains(newLordToil)) transition.SourceToilBecameActive(transition, previousToil);
                }
            }
            newLordToil.UpdateAllDuties();
        }

        /// <summary>Takes <paramref name="pawn"/> into the roster and hands it the current toil's duty
        /// (RimWorld: <c>Lord.AddPawn</c>).</summary>
        public void AddPawn(Pawn pawn)
        {
            if (pawn == null || ownedPawns.Contains(pawn)) return;
            ownedPawns.Add(pawn);
            numPawnsEverGained++;
            curLordToil?.UpdateAllDuties();
        }

        /// <summary>
        /// A member is gone (RimWorld: <c>Lord.Notify_PawnLost</c>): out of the roster, counted if it was lost
        /// violently, the lord dissolved if it was the last, and otherwise the loss offered to the graph — which
        /// is where the flee fires.
        /// </summary>
        public void Notify_PawnLost(Pawn pawn, PawnLostCondition cond)
        {
            if (!ownedPawns.Contains(pawn)) return;
            RemovePawn(pawn, cond);
            if (cond == PawnLostCondition.IncappedOrKilled || cond == PawnLostCondition.MadePrisoner) numPawnsLostViolently++;

            if (lordManager == null || !lordManager.lords.Contains(this)) return;
            if (ownedPawns.Count == 0)
            {
                lordManager.RemoveLord(this);
                return;
            }
            CheckTransitionOnSignal(new TriggerSignal { type = TriggerSignalType.PawnLost, thing = pawn, condition = cond });
        }

        /// <summary>
        /// Out of the roster, and out of the duty the roster gave it (RimWorld: <c>Lord.RemovePawn</c>, which
        /// sets the duty to null).
        ///
        /// <para/><b>One deviation, for the raider who gets back up.</b> RimWorld clears the duty of a downed
        /// member and never looks at it again. If it self-heals, the last tier of RimWorld's humanlike think
        /// tree takes it: "If you're just here for no apparent reason, and not a colonist, leave the map — e.g.
        /// this happens for pawns who are downed during combat, then later self-heal" (<c>Humanlike.xml</c>,
        /// <c>JobGiver_ExitMapBest</c> under an inverted <c>ThinkNode_ConditionalColonist</c>). This port's
        /// tree has no such tier, and cannot grow one without making every pawn of another faction on any
        /// opened map a candidate — so a raider with no duty would get up and fight on alone, which is the
        /// wandering-raider bug the lord exists to end. So a member lost alive is handed that same job giver as
        /// its duty (<see cref="LordDutyDefOf.ExitMapBest"/>), which does nothing while it is down, and nothing
        /// for a prisoner (<see cref="JobGiver_ExitMapBest"/> refuses one), and walks it off the map if it
        /// stands up free. A member that walked off or died gets null, as in RimWorld.
        /// </summary>
        private void RemovePawn(Pawn pawn, PawnLostCondition cond)
        {
            ownedPawns.Remove(pawn);
            if (pawn.mindState == null) return;
            bool mayStandUpAgain = cond == PawnLostCondition.IncappedOrKilled && !pawn.Dead && pawn.Spawned;
            pawn.mindState.duty = mayStandUpAgain ? LordDutyDefOf.ExitMapBest : null;
        }

        /// <summary>Dissolving the lord: every remaining member loses its duty and its current job, so nobody
        /// goes on carrying orders from a group that no longer exists (RimWorld: <c>Lord.Cleanup</c>).</summary>
        public void Cleanup()
        {
            curLordToil?.Cleanup();
            for (int i = 0; i < ownedPawns.Count; i++)
            {
                Pawn pawn = ownedPawns[i];
                if (pawn.mindState != null) pawn.mindState.duty = null;
                if (pawn.Spawned && pawn.jobs?.curJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        /// <summary>
        /// One tick (RimWorld: <c>Lord.LordTick</c>): notice lost members (see the class doc), then offer the
        /// tick to the graph, then count it.
        /// </summary>
        public void LordTick()
        {
            if (!CheckForLostPawns()) return;
            CheckTransitionOnSignal(TriggerSignal.ForTick);
            ticksInToil++;
        }

        /// <summary>Raises <see cref="Notify_PawnLost"/> for every member no longer with the lord. Returns
        /// false when that dissolved the lord.</summary>
        private bool CheckForLostPawns()
        {
            int i = 0;
            while (i < ownedPawns.Count)
            {
                Pawn pawn = ownedPawns[i];
                PawnLostCondition cond = LostCondition(pawn);
                if (cond == PawnLostCondition.Undefined)
                {
                    i++;
                    continue;
                }
                Notify_PawnLost(pawn, cond);
                if (lordManager == null || !lordManager.lords.Contains(this)) return false;
            }
            return true;
        }

        private PawnLostCondition LostCondition(Pawn pawn)
        {
            if (pawn.Dead || pawn.Downed) return PawnLostCondition.IncappedOrKilled;
            if (!pawn.Spawned || !ReferenceEquals(pawn.Map, Map)) return PawnLostCondition.ExitedMap;
            if (!ReferenceEquals(pawn.faction, faction)) return PawnLostCondition.ChangedFaction;
            return PawnLostCondition.Undefined;
        }

        private bool CheckTransitionOnSignal(TriggerSignal signal)
        {
            if (graph == null || curLordToil == null) return false;
            for (int i = 0; i < graph.transitions.Count; i++)
            {
                Transition transition = graph.transitions[i];
                if (transition.sources.Contains(curLordToil) && transition.CheckSignal(this, signal)) return true;
            }
            return false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", -1);
            Scribe_Values.Look(ref graphSeed, "graphSeed");
            Scribe_References.Look(ref faction, "faction");

            // Only members still on the map: anyone else is not in the save to be pointed at. The lord drops
            // a departed member on the tick it leaves, so in a running game this filters nothing.
            List<Pawn>? pawns = Scribe.mode == LoadSaveMode.Saving ? ownedPawns.FindAll(p => p.Spawned) : ownedPawns;
            Scribe_Collections.Look(ref pawns, "ownedPawns", LookMode.Reference);
            ownedPawns = pawns ?? new List<Pawn>();

            Scribe_Deep.Look(ref curJob, "lordJob");
            Scribe_Values.Look(ref ticksInToil, "ticksInToil");
            Scribe_Values.Look(ref numPawnsEverGained, "numPawnsEverGained");
            Scribe_Values.Look(ref numPawnsLostViolently, "numPawnsLostViolently");

            if (Scribe.mode == LoadSaveMode.Saving && graph != null)
            {
                tmpCurLordToilIdx = curLordToil == null ? -1 : graph.lordToils.IndexOf(curLordToil);
                tmpTriggerData = new Dictionary<int, TriggerData>();
                int index = 0;
                for (int i = 0; i < graph.transitions.Count; i++)
                {
                    List<Trigger> triggers = graph.transitions[i].triggers;
                    for (int j = 0; j < triggers.Count; j++, index++)
                    {
                        if (triggers[j].data != null) tmpTriggerData.Add(index, triggers[j].data!);
                    }
                }
            }
            Scribe_Values.Look(ref tmpCurLordToilIdx, "curLordToilIdx", -1);
            Scribe_Collections.Look(ref tmpTriggerData, "triggerData", LookMode.Value, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit) FinishLoading();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                tmpCurLordToilIdx = -1;
                tmpTriggerData = null;
            }
        }

        /// <summary>Rebuilds the graph from the job, as RimWorld does, then puts back where the lord was and
        /// what its triggers remembered. Duties are not re-handed: each pawn saved its own.</summary>
        private void FinishLoading()
        {
            ownedPawns.RemoveAll(p => p == null);
            if (curJob == null) return;
            SetJob(curJob);
            if (graph == null) return;

            if (tmpCurLordToilIdx >= 0 && tmpCurLordToilIdx < graph.lordToils.Count) curLordToil = graph.lordToils[tmpCurLordToilIdx];
            else curLordToil = graph.StartingToil;

            if (tmpTriggerData != null)
            {
                int index = 0;
                for (int i = 0; i < graph.transitions.Count; i++)
                {
                    List<Trigger> triggers = graph.transitions[i].triggers;
                    for (int j = 0; j < triggers.Count; j++, index++)
                    {
                        if (tmpTriggerData.TryGetValue(index, out TriggerData? d)) triggers[j].data = d;
                    }
                }
            }
            tmpCurLordToilIdx = -1;
            tmpTriggerData = null;
        }

        public override string ToString() => "Lord_" + loadID + " (" + (curLordToil?.ToString() ?? "no toil") + ", " + ownedPawns.Count + " pawns)";
    }
}
