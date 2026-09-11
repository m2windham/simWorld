using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Offices
{
    /// <summary>
    /// Keeps every <see cref="OfficeDef"/>'s seat filled: who holds it, who takes it when it falls empty, and
    /// what the chronicle says about both.
    ///
    /// <para/><b>Stateless on purpose, and that is the module's central decision.</b> A seat's holder is the
    /// citizen carrying that office's <see cref="OfficeDef.role"/>, which already lives on the pawn
    /// (<see cref="Work.Pawn_WorkSettings.Role"/>) and already Scribes with them — so this class is a static
    /// utility with no fields, nothing registered with <see cref="Find"/>, and nothing deep-saved by
    /// <see cref="Game"/>. <c>MigrationManager</c> is the same shape for the same reason: every bit of state
    /// the decision needs already belongs to the pawn it is about. The consequences are worth stating:
    /// <list type="bullet">
    /// <item><description>A save round-trip cannot desynchronise a seat from its holder, because the holder
    /// <i>is</i> the seat. There is no second list to disagree with the first.</description></item>
    /// <item><description>An office's <see cref="OfficeDef.role"/> is reserved for its office. Anything else
    /// that writes one onto a citizen — <see cref="Work.WorkPolicyUtility.ApplyRoleToPopulation"/> sweeping a
    /// whole settlement, say — is corrected on the next sweep: the impostors are stripped, and the rightful
    /// holder (still the incumbent, if they are alive and eligible) is re-seated. Self-healing rather than
    /// guarded, because there is no seat object to guard.</description></item>
    /// <item><description>The one thing a persistent roster would buy is remembering that a seat <i>used</i>
    /// to be held, so a chronicle line could read "succeeds the late N" rather than "takes the seat". That is
    /// wording, not mechanism — see <see cref="ReconcileSeat"/> for how far this gets without it and where it
    /// honestly stops.</description></item>
    /// </list>
    ///
    /// <para/><b>What a seat does.</b> Three things, and only the first is about the tier system:
    /// <list type="number">
    /// <item><description>Its holder is significant by station: <c>Pawn_TierTracker.Notify_RoleChanged</c>,
    /// rank 0 in <see cref="God.AttentionBudget"/>'s ordering, so the Full-tier cap can never displace a
    /// leader however large the settlement grows.</description></item>
    /// <item><description>Its holder carries the office's <see cref="Work.RoleDef"/>, which really does
    /// change what they do — <see cref="Work.Pawn_WorkSettings.SetRole"/> lifts that role's work types to the
    /// first-attempted priority and turns detailed priorities on, so the think tree's own work scan
    /// (<see cref="Work.Pawn_WorkSettings.WorkGiversInOrderNormal"/>) reorders for them. A town with an empty
    /// stewardship has nobody doing the steward's work first.</description></item>
    /// <item><description>Its holder carries the office's <see cref="OfficeDef.holderThought"/> for exactly
    /// as long as they hold it (<see cref="ThoughtWorker_HoldsOffice"/>), which reaches
    /// <c>Need_Mood</c> and through it everything that already reads mood — birth chance, migration's
    /// settlement quality, the god view's rollup.</description></item>
    /// </list>
    ///
    /// <para/><b>One line of wiring, and nothing else outside this folder.</b> <see cref="Tick"/> is the
    /// civilization-wide entry point, self-gated to <see cref="OfficeTuning.ReconcileIntervalTicks"/> so a
    /// caller can hand it every tick for the price of a modulo — exactly the shape
    /// <c>Building.SettlementConstructionInitiative.Tick</c> already uses, and wired the same way, as a single
    /// <c>PostTickers.Add</c> in <c>Sim/Game.cs</c>'s <c>WireTickHooks</c>. That one line is this module's
    /// entire footprint in a file it does not own; being stateless is what keeps it to one line rather than
    /// the four a Scribed manager would need.
    /// </summary>
    public static class OfficeManager
    {
        // ---- queries ----

        /// <summary>
        /// The office this citizen holds, or null. Reads their own <see cref="Work.Pawn_WorkSettings.Role"/>
        /// against the loaded <see cref="OfficeDef"/>s — a handful of reference comparisons, cheap enough to
        /// sit behind a situational thought that recalculates every ten ticks.
        /// </summary>
        public static OfficeDef? OfficeOf(Pawn? pawn)
        {
            Work.RoleDef? role = pawn?.workSettings?.Role;
            if (role == null) return null;

            IReadOnlyList<OfficeDef> offices = DefDatabase<OfficeDef>.AllDefsListForReading;
            for (int i = 0; i < offices.Count; i++)
            {
                if (offices[i].role == role) return offices[i];
            }
            return null;
        }

        /// <summary>Whether this citizen holds <paramref name="office"/> right now.</summary>
        public static bool Holds(Pawn? pawn, OfficeDef office) =>
            office != null && pawn != null && !pawn.Dead && pawn.workSettings?.Role == office.role;

        /// <summary>
        /// Who holds <paramref name="office"/> among <paramref name="roster"/>, or null when the seat is
        /// empty. Answers from the roster rather than from a stored pointer — see the class doc.
        /// </summary>
        public static Pawn? HolderIn(OfficeDef office, IReadOnlyList<Pawn> roster)
        {
            if (office == null) throw new ArgumentNullException(nameof(office));
            if (roster == null) throw new ArgumentNullException(nameof(roster));

            for (int i = 0; i < roster.Count; i++)
            {
                if (Holds(roster[i], office)) return roster[i];
            }
            return null;
        }

        /// <summary>The citizen holding a settlement-scope office in <paramref name="settlement"/>, or null.</summary>
        public static Pawn? HolderIn(OfficeDef office, World.Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            return HolderIn(office, settlement.Citizens);
        }

        // ---- the sweep ----

        /// <summary>Civilization-wide entry point for a tick loop; self-gated, so most ticks cost a modulo.</summary>
        public static void Tick()
        {
            if (Find.TickManager.TicksGame % OfficeTuning.ReconcileIntervalTicks != 0) return;
            World.World? world = Find.World;
            if (world == null) return;
            ReconcileWorld(world);
        }

        /// <summary>
        /// Brings every seat in the world into agreement with who is actually alive and eligible. Offices are
        /// taken in <see cref="OfficeDef.precedence"/> order and a citizen already seated is no longer a
        /// candidate, which is what keeps "one office per citizen" true without a rule anywhere else.
        /// <para/>
        /// Cost is O(offices x live citizens) with no ordering at all in the steady state: a seat whose holder
        /// is alive and eligible is left alone after a single scan of the roster, and the expensive half — the
        /// electorate sort and the esteem sum — is paid only on a seat that is actually empty. Statistical
        /// cohort members are never materialised: <see cref="World.Settlement.StatisticalPopulation"/> is a
        /// count with no <c>Pawn</c> behind a person, so it is skipped rather than walked, the same guarantee
        /// <see cref="God.AttentionManager.Reconcile"/> and <see cref="God.GodRollup"/> make.
        /// </summary>
        public static void ReconcileWorld(World.World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var settlements = new List<World.Settlement>();
            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is World.Settlement settlement) settlements.Add(settlement);
            }
            if (settlements.Count == 0) return;

            foreach (OfficeDef office in OfficesInPrecedenceOrder())
            {
                if (office.scope == OfficeScope.Civilization) ReconcileCivilization(office, settlements);
                else ReconcileSettlements(office, settlements);
            }
        }

        /// <summary>
        /// Reconciles every settlement-scope seat of one settlement. The single-settlement entry point:
        /// useful to a caller that has just changed one town's roster and does not want to sweep a planet, and
        /// the shape the tests drive succession through.
        /// </summary>
        public static void Reconcile(World.Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            foreach (OfficeDef office in OfficesInPrecedenceOrder())
            {
                if (office.scope != OfficeScope.Settlement) continue;
                ReconcileSeat(office, settlement.Citizens, ContextFor(settlement));
            }
        }

        private static void ReconcileSettlements(OfficeDef office, List<World.Settlement> settlements)
        {
            for (int i = 0; i < settlements.Count; i++)
            {
                ReconcileSeat(office, settlements[i].Citizens, ContextFor(settlements[i]));
            }
        }

        /// <summary>
        /// One seat per faction, drawn from every settlement that faction owns. Settlements with no faction
        /// are skipped entirely rather than lumped together: a civilization-scope seat belongs to a
        /// civilization, and "the settlements nobody owns" is not one. Factions are visited in the order their
        /// first settlement appears in <see cref="World.World.worldObjects"/>, which is stable across a save.
        /// </summary>
        private static void ReconcileCivilization(OfficeDef office, List<World.Settlement> settlements)
        {
            var order = new List<Factions.Faction>();
            var rosters = new Dictionary<Factions.Faction, List<Pawn>>();
            var founded = new Dictionary<Factions.Faction, int>();
            var named = new Dictionary<Factions.Faction, string>();

            for (int i = 0; i < settlements.Count; i++)
            {
                World.Settlement settlement = settlements[i];
                Factions.Faction? faction = settlement.faction;
                if (faction == null) continue;

                if (!rosters.TryGetValue(faction, out List<Pawn>? roster))
                {
                    order.Add(faction);
                    roster = new List<Pawn>();
                    rosters[faction] = roster;
                    founded[faction] = settlement.foundingTick;
                    named[faction] = string.IsNullOrEmpty(faction.name) ? settlement.name : faction.name;
                }
                roster.AddRange(settlement.Citizens);
                if (settlement.foundingTick < founded[faction]) founded[faction] = settlement.foundingTick;
            }

            for (int i = 0; i < order.Count; i++)
            {
                Factions.Faction faction = order[i];
                ReconcileSeat(office, rosters[faction], new OfficeContext(named[faction], founded[faction]));
            }
        }

        /// <summary>
        /// The whole mechanism, for one seat over one roster.
        ///
        /// <para/><b>Incumbency first.</b> A living, still-eligible holder keeps the seat and nothing else
        /// happens — no election, no chronicle line, no work-priority rewrite. That is what makes the sweep
        /// idempotent and thrash-free: running it twice over an unchanged roster is the second run agreeing
        /// with the first, the same property <see cref="God.AttentionBudget.Apply"/> is built around.
        ///
        /// <para/><b>And if two citizens somehow carry the seat's role, the office's own rule decides between
        /// them</b> — <see cref="Choose"/>, the same ordering an election uses — rather than "whoever the
        /// roster happens to list first", which would make the outcome depend on list order. Because that
        /// ordering is total and a subset preserves it, the sitting holder wins against any single pretender
        /// they already beat in the election that seated them. No chronicle line either way: nothing acceded,
        /// an anomaly was corrected.
        ///
        /// <para/><b>Then succession.</b> A holder who has died, left the roster, or stopped being eligible
        /// vacates, and the seat is refilled from the candidates — or stays empty when there are none, which
        /// for the eldership is not a failure but the end of the office (see
        /// <see cref="OfficeSelectionWorker_Eldest"/>).
        ///
        /// <para/><b>What the chronicle gets, and the one thing it does not.</b> Every seating writes an
        /// "Accession" line, which is edge-triggered by construction (a seated office is never re-seated), so
        /// the succession after a leader's death is always recorded and <see cref="Director.MomentCurator"/>
        /// flags the first one as a moment. A death <i>caught in office</i> — the holder still on the roster
        /// when the sweep reaches them — additionally writes a "Succession" line naming them. That second line
        /// is best-effort and says so: <c>Settlement.SyncCitizenSpawns</c> prunes the dead from the roster on
        /// its own rare-tick cadence, so a leader who dies and is pruned before this sweep runs leaves only
        /// their own death entry plus their successor's accession. The mechanism is exact either way; only the
        /// wording of one line depends on the timing, and buying certainty would mean persisting a roster this
        /// module is deliberately built without.
        /// </summary>
        private static void ReconcileSeat(OfficeDef office, IReadOnlyList<Pawn> roster, OfficeContext context)
        {
            OfficeSelectionWorker worker = office.Worker;

            List<Pawn>? carriers = null;
            List<Pawn>? vacating = null;
            Pawn? diedInOffice = null;

            for (int i = 0; i < roster.Count; i++)
            {
                Pawn pawn = roster[i];
                if (pawn?.workSettings?.Role != office.role) continue;

                if (pawn.Dead)
                {
                    diedInOffice ??= pawn;
                    (vacating ??= new List<Pawn>()).Add(pawn);
                }
                else if (worker.StillHolds(pawn, context))
                {
                    (carriers ??= new List<Pawn>()).Add(pawn);
                }
                else
                {
                    // An incumbent who has stopped being eligible. The role goes: the office decides who
                    // carries it, not whoever wrote it last.
                    (vacating ??= new List<Pawn>()).Add(pawn);
                }
            }

            Pawn? incumbent = null;
            if (carriers != null)
            {
                incumbent = carriers.Count == 1 ? carriers[0] : Choose(office, carriers, Electorate(roster), context);
                for (int i = 0; i < carriers.Count; i++)
                {
                    if (carriers[i] != incumbent) (vacating ??= new List<Pawn>()).Add(carriers[i]);
                }
            }

            if (vacating != null)
            {
                for (int i = 0; i < vacating.Count; i++) Vacate(vacating[i], office);
            }

            if (diedInOffice != null)
            {
                Find.Storyteller.RecordChronicle(
                    "Succession: " + diedInOffice.Label + " died holding the " + office.label + " of " + context.PlaceLabel + ".");
            }

            if (incumbent != null) return;

            Pawn? elected = Elect(office, roster, context);
            if (elected != null) Seat(elected, office, context);
        }

        private static void Seat(Pawn pawn, OfficeDef office, OfficeContext context)
        {
            pawn.workSettings.SetRole(office.role);
            pawn.tier.Notify_RoleChanged(true);
            Find.Storyteller.RecordChronicle(
                "Accession: " + pawn.Label + " takes the " + office.label + " of " + context.PlaceLabel + ".");
        }

        /// <summary>
        /// Strips the office: the role goes (reverting every work type it touched to the default, which
        /// <see cref="Work.Pawn_WorkSettings.SetRole"/> guarantees leaves no trace of the tenure), and the
        /// station stops holding them at Full tier.
        /// <para/>
        /// <c>Notify_RoleChanged(false)</c> is called only on a citizen this module is actually unseating —
        /// never swept over a roster — because that flag has a second, older caller:
        /// <c>MigrationManager</c> sets it on an arriving household founder, and clearing it for citizens this
        /// module never seated would silently demote people it has no business deciding about. The one case
        /// the two callers genuinely overlap on is stated rather than hidden: a migrant founder who later
        /// takes a station and then loses it loses both claims at once, because <c>hasRole</c> is a single
        /// flag with no record of who set it. Recording that would mean the tier tracker keeping a reason per
        /// setter, which is a change to a file this module does not own and a bigger claim than this edge
        /// case is worth.
        /// </summary>
        private static void Vacate(Pawn pawn, OfficeDef office)
        {
            _ = office;
            pawn.workSettings?.SetRole(null);
            pawn.tier?.Notify_RoleChanged(false);
        }

        /// <summary>
        /// Fills an empty seat: everyone eligible and not already seated elsewhere stands, the strongest
        /// <see cref="OfficeDef.candidatePoolSize"/> claims become the candidates, and the highest
        /// <see cref="OfficeSelectionWorker.Score"/> among those takes it. Ties — in candidacy and in score
        /// alike — go to the lower <see cref="Things.Thing.thingIDNumber"/>, which is
        /// <see cref="God.AttentionBudget"/>'s own tie-break and for its own reasons: unique, immutable,
        /// saved with the pawn, and monotone in creation order, so it reads as seniority rather than as an
        /// arbitrary coin toss and two runs over the same roster seat the same citizen.
        /// </summary>
        private static Pawn? Elect(OfficeDef office, IReadOnlyList<Pawn> roster, OfficeContext context)
        {
            OfficeSelectionWorker worker = office.Worker;

            var standing = new List<Pawn>();
            for (int i = 0; i < roster.Count; i++)
            {
                Pawn pawn = roster[i];
                if (pawn == null || pawn.Dead) continue;
                if (OfficeOf(pawn) != null) continue; // already holds a seat; one office per citizen
                if (!worker.IsEligible(pawn, context)) continue;
                standing.Add(pawn);
            }
            return standing.Count == 0 ? null : Choose(office, standing, Electorate(roster), context);
        }

        /// <summary>
        /// The ordering itself, shared by an election and by resolving two claimants to one seat: sort by
        /// <see cref="OfficeSelectionWorker.CandidacyStrength"/> descending then
        /// <see cref="Things.Thing.thingIDNumber"/> ascending, take the first
        /// <see cref="OfficeDef.candidatePoolSize"/>, and among those take the highest
        /// <see cref="OfficeSelectionWorker.Score"/> — replacing only on a strictly better score, so an equal
        /// score leaves the seat with whoever sorted earlier and therefore with the more senior citizen.
        /// <para/>
        /// Total (ids are unique), so the sort is deterministic regardless of <see cref="List{T}.Sort"/> being
        /// unstable, and — because a subset preserves the order of a superset — the winner over any subset
        /// containing the sitting holder is still the sitting holder. That is what makes reusing this for the
        /// two-claimants case safe rather than a second, subtly different rule.
        /// </summary>
        private static Pawn Choose(
            OfficeDef office, List<Pawn> standing, IReadOnlyList<Pawn> electorate, OfficeContext context)
        {
            OfficeSelectionWorker worker = office.Worker;

            standing.Sort((a, b) =>
            {
                int byClaim = worker.CandidacyStrength(b, context).CompareTo(worker.CandidacyStrength(a, context));
                return byClaim != 0 ? byClaim : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });

            int pool = Math.Min(office.candidatePoolSize, standing.Count);
            Pawn best = standing[0];
            float bestScore = worker.Score(best, electorate, context);
            for (int i = 1; i < pool; i++)
            {
                float score = worker.Score(standing[i], electorate, context);
                if (score > bestScore)
                {
                    best = standing[i];
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>The <see cref="OfficeTuning.ElectorateCap"/> longest-established living citizens of the
        /// roster — see <see cref="OfficeSelectionWorker_Esteem"/> for why an election is not put to forty
        /// thousand people. Returns the roster untouched when it is already within the cap, which is the
        /// common case and costs no sort.</summary>
        private static IReadOnlyList<Pawn> Electorate(IReadOnlyList<Pawn> roster)
        {
            if (roster.Count <= OfficeTuning.ElectorateCap) return roster;

            var living = new List<Pawn>(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] != null && !roster[i].Dead) living.Add(roster[i]);
            }
            if (living.Count <= OfficeTuning.ElectorateCap) return living;

            living.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            return living.GetRange(0, OfficeTuning.ElectorateCap);
        }

        /// <summary>Loaded offices, lowest <see cref="OfficeDef.precedence"/> first and then by
        /// <see cref="Def.defName"/> so two offices sharing a precedence still resolve in one fixed order
        /// rather than in whatever order the content files happened to load.</summary>
        private static List<OfficeDef> OfficesInPrecedenceOrder()
        {
            var offices = new List<OfficeDef>(DefDatabase<OfficeDef>.AllDefsListForReading);
            offices.Sort((a, b) =>
            {
                int byPrecedence = a.precedence.CompareTo(b.precedence);
                return byPrecedence != 0 ? byPrecedence : string.CompareOrdinal(a.defName, b.defName);
            });
            return offices;
        }

        private static OfficeContext ContextFor(World.Settlement settlement) =>
            new OfficeContext(settlement.name, settlement.foundingTick);
    }
}
