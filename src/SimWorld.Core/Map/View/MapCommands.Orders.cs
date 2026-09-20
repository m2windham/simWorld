using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The individual, one-time cell of <c>docs/design/player-first.md</c> §4 — "order this person to do this
    /// thing" — and the one command on this surface whose subject is a named citizen rather than a piece of
    /// ground.
    ///
    /// <para/><b>Its own file, deliberately.</b> <see cref="MapCommands"/> is <c>partial</c> so that a lane
    /// adding a verb adds a file instead of editing a shared one; see CLAUDE.md, "Add a file rather than edit
    /// a shared one". Everything here reuses <see cref="MapCommandResult"/>, <see cref="MapCommandOutcome"/>
    /// and <c>ResolveMap</c> from that file unchanged — no new result type, no new outcome value, no new way
    /// for a host to name a map.
    ///
    /// <para/><b>This adds no mechanism. It is a caller.</b> <c>AI.Pawn_JobTracker.QueueJob</c>,
    /// <c>AI.Job.playerForced</c> and <c>AI.JobGiver_DirectedOrder</c> were all built, tested and wired into
    /// <c>ThinkTrees_Humanlike.xml</c> above <c>JobGiver_Work</c> — and, until this file, never called from
    /// <c>src/</c> even once. <c>docs/design/player-first.md</c> §10 names that as "the single highest-leverage
    /// gap", and §9's closing rule is why: <i>a mechanism with no caller is not a feature</i>. So nothing below
    /// decides anything about jobs. It resolves a citizen and a <see cref="JobDef"/> from the two handles a
    /// host is allowed to hold (an integer id and a <c>defName</c>), builds the <see cref="Job"/> the think
    /// tree was already waiting for, and queues it.
    ///
    /// <para/><b>Interrupt-then-revert, and where each half actually lives.</b> The pattern RimWorld ("this
    /// jumps the queue without touching the priority grid"), Dwarf Fortress (active versus passive orders) and
    /// Oxygen Not Included (sub-priority inside a tier) converged on independently, and which
    /// <c>player-first.md</c> §5 states as a rule: <i>a one-time act interrupts a standing rule; it does not
    /// rewrite it</i>. Both halves were already built here and neither is re-implemented:
    /// <list type="bullet">
    /// <item><b>Interrupt</b> is tree position. <c>JobGiver_DirectedOrder</c> sits above
    /// <c>JobGiver_Edicts</c> and <c>JobGiver_Work</c>, so the next time the citizen asks the tree what to do,
    /// the order is what comes back — whatever their work priorities say, including a work type the player has
    /// switched off entirely. Nothing here touches <c>Pawn_WorkSettings</c>; the grid reads exactly the same
    /// after the order as before it.</item>
    /// <item><b>Revert</b> is the absence of state. <see cref="Job.playerForced"/> is a flag on one
    /// <see cref="Job"/>, not on the citizen. When the ordered job ends, <c>Pawn_JobTracker.EndCurrentJob</c>
    /// runs the tree again, <c>JobGiver_DirectedOrder</c> finds an empty queue and declines, and the citizen
    /// falls through to their ordinary work on their own. There is nothing to un-set, which is why there is no
    /// persisted state in this file and no Scribe line to go with it.</item>
    /// </list>
    ///
    /// <para/><b>What an order does <i>not</i> do: throw away the job in hand.</b> A queued order is taken at
    /// the citizen's next think-tree pass, which is when their current job ends — <c>player-first.md</c> §5,
    /// "in-flight work is never pre-empted: atomicity of the current act beats freshness of the rule". This is
    /// a deliberate divergence from RimWorld, whose right-click prioritise calls <c>TryTakeOrderedJob</c> and
    /// starts immediately; the rule in this repository is the stricter one and it wins here. Two consequences
    /// worth stating rather than leaving to be discovered: an order given to a citizen already carrying a long
    /// job waits for it, and — because the three need tiers (food, rest, recreation) sit <i>above</i>
    /// <c>JobGiver_DirectedOrder</c> in the humanlike tree, as that file's own comments explain at length — a
    /// starving or exhausted citizen eats or sleeps first and then carries the order out. Neither is a refusal
    /// and neither drops the order: it stays queued until it is taken.
    ///
    /// <para/><b>What is refused, and what is emphatically not.</b> The class doc on <see cref="MapCommands"/>
    /// is the rule and it is not relaxed here: only the physically impossible. Nobody on this map with that
    /// id; a <c>defName</c> no <see cref="JobDef"/> carries; a target cell off the map or a target id that is
    /// on no map; and a citizen whose think tree can never reach the directed-order tier at all — dead, an
    /// animal (the animal tree has no such tier, a fact <c>AnimalAITests</c> pins), or below
    /// <see cref="PawnTier.Full"/> (<c>Pawn.Tick</c> never reaches <c>JobTrackerTick</c> for them, so the order
    /// would sit in a queue nothing ever reads). Each of those means the order could never be carried out by
    /// any sequence of events, which is the only bar this surface applies.
    ///
    /// <para/><b>Everything else is accepted, including orders that are plainly bad.</b> Ordering the
    /// settlement's only doctor to go dig rocks while somebody bleeds out succeeds. Ordering a citizen to walk
    /// into an empty corner and stand there succeeds. Ordering work the citizen's own standing priorities have
    /// switched off succeeds — that is the whole point of a one-time act. An order whose target the driver
    /// cannot actually use fails the way any job fails, inside the simulation
    /// (<c>JobCondition.Incompletable</c>), and the citizen goes back to work; it is not second-guessed at the
    /// seam. <b>Do not add a "can this citizen really do this?" check to any method here.</b> The read model is
    /// where a warning belongs, so a host can show the player the cost before they commit — never the command
    /// that carries their decision out.
    ///
    /// <para/><b>Work types are not checked, and that is a gap rather than a decision.</b> RimWorld can refuse
    /// "incapable of this whole work type" because its order arrives through a <c>WorkGiver</c>, which knows
    /// its own <c>WorkTypeDef</c>. Here the host names a <see cref="JobDef"/> directly and <b>this codebase has
    /// no link at all from a <see cref="JobDef"/> to a <c>Work.WorkTypeDef</c></b> — not a field, not a
    /// convention, nothing. Deriving one would mean inventing content (a mapping table, or a new field on the
    /// shared <c>AI/JobDef.cs</c>), and inventing a translation to justify a refusal is the wrong way round:
    /// under <c>player-first.md</c> a missing refusal costs a bad order the player asked for, while an invented
    /// one silently overrules them. So a citizen incapable of violence can be ordered to attack today; the job
    /// runs and its driver deals with them being useless at it. If that link is ever built for another reason,
    /// this is where it would be read.
    /// </summary>
    public static partial class MapCommands
    {
        /// <summary>
        /// Orders the citizen with <paramref name="citizenThingId"/> to perform <paramref name="jobDefName"/>
        /// at <paramref name="targetCell"/> — "go and do that, there". The id is the same
        /// <c>PawnView.ThingId</c> the host already draws people by, and the <c>defName</c> is the same kind of
        /// handle every other command here takes, so nothing about this call needs the host to hold a live
        /// object.
        ///
        /// <para/>The order joins the back of the citizen's directed-order queue and is taken at their next
        /// think-tree pass; see the class doc for why that is not immediate and why it does not need to be.
        /// Refuses only <see cref="MapCommandOutcome.NoMap"/>, <see cref="MapCommandOutcome.UnknownDef"/>,
        /// <see cref="MapCommandOutcome.OffMap"/> and the <see cref="MapCommandOutcome.Refused"/> cases the
        /// class doc lists — never because the order is a poor one.
        /// </summary>
        public static MapCommandResult OrderJob(int citizenThingId, string jobDefName, IntVec3 targetCell)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? refusal = ResolveOrderSubject(
                citizenThingId, jobDefName, map, out Pawn? citizen, out JobDef? jobDef);
            if (refusal != null) return refusal;

            if (!GenGrid.InBounds(targetCell, map)) return MapCommandResult.OffMap(targetCell);

            return QueueOrder(citizen!, jobDef!, new LocalTargetInfo(targetCell), targetCell.ToString());
        }

        /// <summary>
        /// Orders the citizen with <paramref name="citizenThingId"/> to perform <paramref name="jobDefName"/>
        /// on the Thing with <paramref name="targetThingId"/> — "go and do that, to <i>that</i>". The rock to
        /// mine, the patient to tend, the animal to tame: everything <c>MapViewSnapshot</c> hands the host
        /// carries the id this takes.
        ///
        /// <para/>Same refusals as the cell overload, with the target's own one in place of
        /// <see cref="MapCommandOutcome.OffMap"/>: a <paramref name="targetThingId"/> matching nothing on the
        /// open settlement's map is <see cref="MapCommandOutcome.Refused"/> — the exact counterpart of a cell
        /// off the edge, since a Thing that is not here is not somewhere a citizen could ever walk to. A target
        /// that exists but is the wrong <i>kind</i> of thing for this job is <b>not</b> refused; that is the
        /// job driver's business, in the simulation, where it fails honestly and the citizen moves on.
        /// </summary>
        public static MapCommandResult OrderJob(int citizenThingId, string jobDefName, int targetThingId)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? refusal = ResolveOrderSubject(
                citizenThingId, jobDefName, map, out Pawn? citizen, out JobDef? jobDef);
            if (refusal != null) return refusal;

            Thing? target = FindThingById(map, targetThingId);
            if (target == null)
            {
                return MapCommandResult.Refused(
                    "Nothing with id " + targetThingId + " is on the open settlement's map.");
            }

            return QueueOrder(citizen!, jobDef!, new LocalTargetInfo(target), target.Label);
        }

        /// <summary>
        /// Withdraws the player's standing orders for one citizen — the undo for <see cref="OrderJob"/>, and
        /// the "cancel that" of <c>player-first.md</c> §3. Every order still waiting is dropped, and an order
        /// the citizen is already carrying out is ended too; the think tree runs immediately afterwards, so
        /// they are back on their ordinary work priorities the same tick rather than standing idle.
        ///
        /// <para/><b>Why this ends work in progress where <see cref="CancelDesignation"/> refuses to.</b> That
        /// method leaves a <c>Building.Frame</c> alone on purpose — materials are already committed to it, so
        /// abandoning it destroys something. An interrupted job commits nothing: <c>EndCurrentJob</c> releases
        /// every reservation the citizen held and the world is exactly as it was. And this is the player
        /// withdrawing their <i>own</i> order, which is not the simulation second-guessing an act in flight —
        /// §5's "in-flight work is never pre-empted" is a rule about the machinery reconsidering, not about the
        /// player changing their mind.
        ///
        /// <para/>A citizen with nothing to cancel is <see cref="MapCommandOutcome.NoChange"/> rather than a
        /// failure — the world already matches what was asked for, the same treatment
        /// <see cref="CancelDesignation"/> gives an empty cell. Ordinary work the citizen chose for themselves
        /// is never touched: only jobs carrying <see cref="Job.playerForced"/> are.
        /// </summary>
        public static MapCommandResult CancelOrder(int citizenThingId)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            Pawn? citizen = FindThingById(map, citizenThingId) as Pawn;
            if (citizen == null)
            {
                return MapCommandResult.Refused(
                    "No one with id " + citizenThingId + " is on the open settlement's map.");
            }

            // Draining the queue through the same internal accessor JobGiver_DirectedOrder pulls from, rather
            // than reaching into Pawn_JobTracker's private list: one definition of "a queued player order"
            // stays in that class, and this file needs no edit to it. It stops at the first null because that
            // is the queue reporting itself empty of player-forced work.
            int cancelled = 0;
            while (citizen.jobs.DequeueDirectedOrder() != null) cancelled++;

            bool endedInFlight = citizen.jobs.curJob?.playerForced == true;
            if (endedInFlight)
            {
                // startNewJob defaults true, so the think tree runs here and the citizen picks their ordinary
                // work back up in this same call — the revert half of the class doc's pattern, on the
                // cancellation path.
                citizen.jobs.EndCurrentJob(JobCondition.InterruptForced);
                cancelled++;
            }

            if (cancelled == 0) return MapCommandResult.NoChange(citizen.Label + " has no orders to cancel.");

            return MapCommandResult.Done(
                "Cancelled " + cancelled + " order(s) for " + citizen.Label
                + (endedInFlight ? ", including the one in hand." : "."));
        }

        // -----------------------------------------------------------------------------------------------
        // Resolution — the same "never take a handle from the caller" discipline ResolveMap applies.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves the two things every order needs — which citizen, and which <see cref="JobDef"/> — or
        /// returns the refusal to hand back. The citizen is checked first because they are the subject of the
        /// sentence: "no such person" is a better answer to <c>OrderJob(stale id, typo'd def)</c> than "no such
        /// job", and a host holding a stale id is the likelier of the two mistakes.
        /// </summary>
        private static MapCommandResult? ResolveOrderSubject(
            int citizenThingId,
            string? jobDefName,
            SimWorld.Map.Map map,
            out Pawn? citizen,
            out JobDef? jobDef)
        {
            citizen = null;
            jobDef = null;

            Thing? thing = FindThingById(map, citizenThingId);
            if (thing == null)
            {
                return MapCommandResult.Refused(
                    "No one with id " + citizenThingId + " is on the open settlement's map.");
            }
            if (!(thing is Pawn pawn))
            {
                return MapCommandResult.Refused(
                    thing.Label + " (id " + citizenThingId + ") is a thing, not a person — it takes no orders.");
            }

            MapCommandResult? cannotTakeOrders = WhyThisPawnCannotTakeOrders(pawn);
            if (cannotTakeOrders != null) return cannotTakeOrders;

            jobDef = string.IsNullOrEmpty(jobDefName) ? null : DefDatabase<JobDef>.GetNamedSilentFail(jobDefName);
            if (jobDef == null) return MapCommandResult.UnknownDef(jobDefName);

            citizen = pawn;
            return null;
        }

        /// <summary>
        /// The three states in which an order could never be carried out by any sequence of events, so
        /// accepting one would be accepting something that will silently never happen. Each is a property of
        /// the machinery, not a judgement about the order: see the class doc.
        /// </summary>
        private static MapCommandResult? WhyThisPawnCannotTakeOrders(Pawn pawn)
        {
            if (pawn.Dead) return MapCommandResult.Refused(pawn.Label + " is dead.");

            if (!pawn.RaceProps.Humanlike)
            {
                // ThinkTrees_Animal.xml is a real second tree, not the humanlike one with branches skipped,
                // and it carries no JobGiver_DirectedOrder at all (AnimalAITests pins that). A queued order
                // would never be dequeued by anything.
                return MapCommandResult.Refused(
                    pawn.Label + " is an animal, and animals take no directed orders.");
            }

            if (pawn.tier != null && pawn.tier.Tier != PawnTier.Full)
            {
                // Pawn.Tick returns before JobTrackerTick for anyone below Full (spec §11.3 tiering), so an
                // Interval or Statistical citizen has no jobs at all — the queue would never be read.
                return MapCommandResult.Refused(
                    pawn.Label + " is not being simulated closely enough to take orders right now.");
            }

            return null;
        }

        /// <summary>
        /// The one write this whole file performs: build the <see cref="Job"/> the think tree has been waiting
        /// for since <c>JobGiver_DirectedOrder</c> was written, mark it as the player's, and start it.
        ///
        /// <para/><b>An explicit order pre-empts the job in hand. A standing rule does not.</b> This started
        /// out queueing, on the strength of <c>docs/design/player-first.md</c> §5's "in-flight work is never
        /// pre-empted" — and that rule was over-generalised when it was written. It comes from research into
        /// how <i>standing rules</i> behave, where the finding is real and worth keeping: a priority table that
        /// yanked people off half-finished work every time it was re-evaluated would thrash. It says nothing
        /// about a one-time act. RimWorld's right-click prioritise pre-empts, and so do Dwarf Fortress's active
        /// squad orders; the doc now draws the line where those two do, and this call matches it.
        ///
        /// <para/>The player-facing reason is the one that settles it. Someone who orders a citizen to go and
        /// fight a fire, and then watches them finish hauling a rock first, has been ignored — and a lever
        /// whose effect arrives at an unpredictable later moment is not a lever they can plan with. §5's own
        /// atomicity argument protects against thrashing from rules, not against the player.
        ///
        /// <para/>What this does <i>not</i> change: the order still reverts. <see cref="Job.playerForced"/>
        /// stays a flag on one job rather than state on the citizen, so when it ends the think tree runs again
        /// and they return to their own priorities. And needs still reassert themselves the moment the ordered
        /// job finishes, because the need tiers sit above <c>JobGiver_DirectedOrder</c> — a citizen can be
        /// ordered away from a meal, and will go back to it after. That is a bad decision the player is allowed
        /// to make.
        /// </summary>
        private static MapCommandResult QueueOrder(
            Pawn citizen, JobDef jobDef, LocalTargetInfo target, string targetLabel)
        {
            citizen.jobs.StartJob(
                new Job(jobDef, target) { playerForced = true }, JobCondition.InterruptForced);
            return MapCommandResult.Done(citizen.Label + " is ordered to " + jobDef.defName + ": " + targetLabel + ".");
        }

        /// <summary>
        /// The map's own Things, by the id the read model hands out (<c>PawnView.ThingId</c>,
        /// <c>ThingView.ThingId</c>) — and only this map's, which is what keeps a host from reaching a
        /// settlement it does not currently have open by guessing an integer. Pawns are registered in
        /// <c>ListerThings</c> alongside everything else (<c>Thing.SpawnSetup</c>), so one scan answers both
        /// "which citizen" and "which target".
        /// </summary>
        private static Thing? FindThingById(SimWorld.Map.Map map, int thingId)
        {
            System.Collections.Generic.IReadOnlyList<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].thingIDNumber == thingId) return things[i];
            }
            return null;
        }
    }
}
