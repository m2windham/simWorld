using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// "Order this person to do this thing" — the individual, one-time cell of
    /// <c>docs/design/player-first.md</c> §4, and until <c>MapCommands.Orders.cs</c> the largest hole in the
    /// player's hands. <c>AI.Pawn_JobTracker.QueueJob</c>, <c>AI.Job.playerForced</c> and
    /// <c>AI.JobGiver_DirectedOrder</c> were built, tested and wired into the humanlike think tree above
    /// <c>JobGiver_Work</c>, and nothing in <c>src/</c> had ever called any of them (§10 names it "the single
    /// highest-leverage gap").
    ///
    /// <para/><see cref="A_citizen_carries_out_a_direct_order_and_then_goes_back_to_ordinary_work"/> is the
    /// test this file exists for. Nothing in it touches a job driver, a work giver or a think node: it gives
    /// an order the way a host would (two integers and a <c>defName</c>, through <see cref="MapCommands"/>
    /// alone) and then drives the real per-tick loop until the settlement has done it. It fails against the
    /// code this lane started from at its first line — the command does not exist — and what it asserts
    /// afterwards is the half that is easy to fake and hard to earn: the citizen goes back to their own
    /// standing work priorities, unprompted, with those priorities unchanged.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapCommandsOrdersTests : ContentTestBase
    {
        public MapCommandsOrdersTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // -------------------------------------------------------------------------------------------
        // Setup, mirroring MapCommandsTests' own — deliberately duplicated rather than shared, so this
        // lane adds a file instead of editing one (CLAUDE.md, "add a file rather than edit a shared one").
        // -------------------------------------------------------------------------------------------

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        /// <summary>Founds a settlement and opens it — attention <b>and</b> a generated interior.
        /// <see cref="MapCommands"/> resolves its map off <c>Find.God.Attention.FocusedSettlement</c> and has
        /// no other way in, so this two-step is the only way these tests can reach one.</summary>
        private static Settlement OpenedSettlement(string seed)
        {
            Game game = NewSoloGame(seed);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static Pawn AnyCitizenOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.First(p => p.Spawned && p.Map == map);

        private static List<Pawn> AllCitizensOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();

        /// <summary>
        /// Every citizen on the map stops choosing work of their own accord. Not a convenience: it is what
        /// makes the headline test mean anything. A settlement of twenty people generates jobs constantly, and
        /// "the rock got mined" proves nothing if somebody else on the map would have mined it anyway. With
        /// the whole band switched off, the only thing on this map that can produce work is the player's
        /// order and whatever standing rule the test then hands back to one named citizen.
        /// </summary>
        private static void SilenceEveryonesStandingWork(Settlement settlement, CoreMap map)
        {
            foreach (Pawn p in AllCitizensOn(settlement, map)) p.workSettings!.DisableAll();
        }

        /// <summary>The nearest mineable rock this pawn could actually be sent to — the same physical gate
        /// <c>AI.WorkGiver_Miner.HasJobOnThing</c> applies (mineable, spawned, holding no roof up, reachable),
        /// walked nearest-first so the test gets one predictable answer.</summary>
        private static Thing NearestMineableRock(CoreMap map, Pawn miner)
        {
            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing t = buildings[i];
                if (!t.def.mineable || t.Destroyed || !t.Spawned) continue;
                int distSq = (t.Position - miner.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(t)) continue;
                if (!Reachability.CanReach(miner, t, PathEndMode.Touch)) continue;
                best = t;
                bestDistSq = distSq;
            }
            return best ?? throw new InvalidOperationException("No safely mineable, reachable rock on this map.");
        }

        private static IntVec3 FreeStandableCellNear(CoreMap map, IntVec3 near)
        {
            var target = new IntVec3(
                Math.Clamp(near.x, 0, map.Size.x - 1),
                0,
                Math.Clamp(near.z, 0, map.Size.z - 1));
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 candidate = target + pattern[i];
                if (!GenGrid.InBounds(candidate, map)) continue;
                if (!GenGrid.Standable(candidate, map)) continue;
                if (map.zoneManager.ZoneAt(candidate) != null) continue;
                if (map.edificeGrid[candidate] != null) continue;
                return candidate;
            }
            throw new InvalidOperationException("No free cell found near " + target + ".");
        }

        /// <summary>The furthest-out free cell from <paramref name="walker"/>'s corner of the map that they
        /// could actually walk to. Reachability matters here and not in
        /// <see cref="FreeStandableCellNear"/>: a generated interior has rock formations with standable
        /// pockets inside them, and a citizen sent into one would fail the job honestly and immediately —
        /// which is correct behaviour but is not what a test about arriving somewhere wants to measure.</summary>
        private static IntVec3 FarReachableCellFrom(CoreMap map, Pawn walker)
        {
            var corner = new IntVec3(map.Size.x - 4, 0, map.Size.z - 4);
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 candidate = corner + pattern[i];
                if (!GenGrid.InBounds(candidate, map)) continue;
                if (!GenGrid.Standable(candidate, map)) continue;
                if (map.zoneManager.ZoneAt(candidate) != null) continue;
                if (map.edificeGrid[candidate] != null) continue;
                if (!Reachability.CanReach(walker, candidate, PathEndMode.Touch)) continue;
                return candidate;
            }
            throw new InvalidOperationException("No reachable free cell near " + corner + ".");
        }

        /// <summary>
        /// The real tick loop, stopped the moment the thing being waited for has happened
        /// (<see cref="TickManager.DoSingleTick"/>, the same clock <see cref="ContentTestBase.RunTicks"/>
        /// drives — <c>RunTicks(0, …)</c> is used purely for its registration step). Polling rather than a
        /// fixed tick count because what is asserted here is transient: a citizen who has walked to a cell
        /// picks up their next job and leaves it again, so "did they ever get there" cannot be read off the
        /// end of a fixed run.
        /// </summary>
        private static bool RunUntil(int maxTicks, Func<bool> condition, params Pawn[] pawns)
        {
            RunTicks(0, pawns);
            TickManager tm = Find.TickManager;
            for (int i = 0; i < maxTicks; i++)
            {
                if (condition()) return true;
                tm.DoSingleTick();
            }
            return condition();
        }

        private static bool AtOrBeside(IntVec3 where, IntVec3 cell) =>
            Math.Abs(where.x - cell.x) <= 1 && Math.Abs(where.z - cell.z) <= 1;

        /// <summary>A bleeding, tendable wound — RimWorld's own "Cut" injury bleeds, and this is the same
        /// two-line setup <c>Health.DoctorAITests</c> uses to make an emergency patient.</summary>
        /// <summary>Bleeding badly enough to be emergency doctoring: cuts over the limbs until the patient will
        /// bleed to death inside RimWorld's eighteen hours (<see cref="HealthAIUtility.ShouldBeTendedNowUrgent"/>).
        /// One cut on an arm used to be enough; under that rule it is ordinary doctoring.</summary>
        private static void MakeBleedingWound(Pawn p)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            string[] parts = { "left arm", "right arm", "left leg", "right leg", "torso" };
            for (int i = 0; i < 20 && !HealthAIUtility.ShouldBeTendedNowUrgent(p); i++)
            {
                cut.Worker.Apply(new DamageInfo(cut, 6f, hitPart: p.RaceProps.body!.GetPartByLabel(parts[i % parts.Length])!), p);
            }
        }

        // ===========================================================================================
        // The headline: an order is carried out, and the standing rule comes back on its own.
        // ===========================================================================================

        /// <summary>
        /// <b>The proof that "send her" reaches the world, and then lets go of it.</b>
        ///
        /// <para/>One citizen is given a single standing rule — they grow things, and nothing else; mining in
        /// particular is switched off for them and for every other person on the map. The player then orders
        /// that citizen, by id, to mine one specific rock: a job their own priorities say they do not do, and
        /// that nobody on this map would ever choose. Three things then have to be true, and only the first is
        /// the easy one:
        /// <list type="number">
        /// <item>The rock is mined out — the order reached the simulation and the simulation carried it, over
        /// the real per-tick loop, through <c>JobGiver_DirectedOrder</c> and <c>JobDriver_Mine</c>, with
        /// nothing in this test calling either.</item>
        /// <item>The citizen goes back to sowing the field, <b>on their own</b>. Nothing cancels the order and
        /// nothing re-issues the standing rule; <c>Job.playerForced</c> lives on the one finished job, so when
        /// it ends there is simply nothing left saying otherwise.</item>
        /// <item>Their work priorities read exactly as they did before. A one-time act interrupts a standing
        /// rule; it does not rewrite it (<c>player-first.md</c> §5). If the command had "helpfully" switched
        /// Mining on to make the order work, this is what would catch it.</item>
        /// </list>
        /// </summary>
        [Fact]
        public void A_citizen_carries_out_a_direct_order_and_then_goes_back_to_ordinary_work()
        {
            Settlement settlement = OpenedSettlement("player-orders-a-citizen");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();

            // Nobody on this map chooses any work at all...
            SilenceEveryonesStandingWork(settlement, map);

            // ...except this one, who grows things. That is her whole standing rule.
            Pawn worker = AnyCitizenOn(settlement, map);
            worker.skills!.GetSkill(SkillDefOf.Plants)!.Level = 20;
            worker.workSettings!.SetPriority(WorkTypeDefOf.Growing, 1);

            // The field, marked by the player through the command surface like everything else here. Wild
            // plants are cleared for the same reason MapCommandsTests clears them: WorkGiver_GrowerHarvest
            // sees every mature plant on the map, zone or not, and a generated interior ships hundreds.
            IntVec3 field = worker.Position;
            map.terrainGrid.SetTerrain(field, TerrainDefOf.Soil);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", new[] { field }).Outcome);
            foreach (Thing wild in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).ToList()) wild.Destroy();

            Thing rock = NearestMineableRock(map, worker);
            Assert.Equal(0, worker.workSettings!.GetPriority(WorkTypeDefOf.Mining));

            // The whole command surface for this act: who, what, and what to do it to.
            MapCommandResult ordered = MapCommands.OrderJob(worker.thingIDNumber, "Mine", rock.thingIDNumber);

            Assert.Equal(MapCommandOutcome.Done, ordered.Outcome);
            Assert.True(ordered.Changed);
            // Queuing an order does not touch the grid it outranks.
            Assert.Equal(0, worker.workSettings!.GetPriority(WorkTypeDefOf.Mining));

            // 1. She did it.
            Assert.True(
                RunUntil(8000, () => rock.Destroyed, everyone),
                "the ordered rock was never mined out");

            // 2. She went back to her own standing rule, unprompted.
            Assert.True(
                RunUntil(8000, () => map.thingGrid.ThingsListAt(field).Any(t => t.def == Def("Plant_Potato")), everyone),
                "the citizen never returned to the work her own priorities called for");
            Assert.False(worker.jobs.curJob?.playerForced == true);

            // 3. The standing rule was never rewritten to make any of that possible.
            Assert.Equal(0, worker.workSettings!.GetPriority(WorkTypeDefOf.Mining));
            Assert.True(worker.workSettings!.GetPriority(WorkTypeDefOf.Growing) > 0);
        }

        /// <summary>The other overload, and the simplest order there is: go and stand there. A bare cell, no
        /// Thing involved — "send her", <c>player-first.md</c> §4's individual one-time cell in its most
        /// literal form.</summary>
        [Fact]
        public void An_order_can_name_a_bare_cell_and_the_citizen_walks_to_it()
        {
            Settlement settlement = OpenedSettlement("player-sends-a-citizen");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn walker = AnyCitizenOn(settlement, map);
            IntVec3 destination = FarReachableCellFrom(map, walker);
            Assert.False(AtOrBeside(walker.Position, destination), "test setup assumption broken: she is already there");

            MapCommandResult ordered = MapCommands.OrderJob(walker.thingIDNumber, "Goto", destination);

            Assert.Equal(MapCommandOutcome.Done, ordered.Outcome);
            // JobDriver_Goto ends on PathEndMode.Touch, so arriving means on the cell or beside it.
            Assert.True(
                RunUntil(8000, () => AtOrBeside(walker.Position, destination), everyone),
                "the citizen never reached the cell she was sent to");
        }

        /// <summary>
        /// <b>The half of "interrupt" this port deliberately does not do.</b> RimWorld's right-click
        /// prioritise calls <c>TryTakeOrderedJob</c> and throws away whatever the pawn was mid-way through;
        /// <c>player-first.md</c> §5 takes the stricter line instead — "in-flight work is never pre-empted:
        /// atomicity of the current act beats freshness of the rule" — and <c>QueueJob</c> is built for
        /// exactly that. A second order given to a citizen already carrying out the first does not disturb it:
        /// she finishes where she was sent, then goes where she was sent next. This is worth pinning rather
        /// than leaving as a comment, because "orders take effect immediately" is the assumption anyone
        /// porting from RimWorld will arrive with.
        /// </summary>
        [Fact]
        public void A_second_order_pre_empts_the_first_rather_than_waiting_its_turn()
        {
            Settlement settlement = OpenedSettlement("player-orders-twice");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn walker = AnyCitizenOn(settlement, map);
            IntVec3 first = FarReachableCellFrom(map, walker);
            IntVec3 second = walker.Position;

            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(walker.thingIDNumber, "Goto", first).Outcome);
            RunTicks(2, everyone);
            Assert.Equal(first, walker.jobs.curJob!.targetA.Cell);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(walker.thingIDNumber, "Goto", second).Outcome);

            // THE ORDER TAKES EFFECT NOW, not when the errand in hand happens to finish. This test pinned the
            // opposite behaviour when it was written, and was right to: player-first.md §5 then read "in-flight
            // work is never pre-empted" without qualification. That rule came from research into standing
            // rules, where it holds, and had been over-generalised to cover explicit acts it was never evidence
            // about. RimWorld's prioritise pre-empts and so do Dwarf Fortress's active squad orders; §5 now
            // says so, and this asserts it at the tick the command returns rather than several hundred later.
            Assert.Equal(second, walker.jobs.curJob!.targetA.Cell);

            Assert.True(RunUntil(8000, () => AtOrBeside(walker.Position, second), everyone),
                "the citizen never got to the second errand");
        }

        /// <summary>
        /// The other half of §5, unchanged and still true: pre-emption is what an explicit <i>order</i> does,
        /// not what the citizen's own standing rules do. Once the ordered job ends, nothing about the citizen
        /// is left marked — <see cref="Job.playerForced"/> lives on the job, not the pawn — so they fall back
        /// to their own priorities without being told to.
        /// </summary>
        [Fact]
        public void A_pre_empting_order_still_reverts_and_leaves_the_standing_rules_untouched()
        {
            Settlement settlement = OpenedSettlement("player-order-reverts");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn walker = AnyCitizenOn(settlement, map);
            IntVec3 somewhere = FarReachableCellFrom(map, walker);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(walker.thingIDNumber, "Goto", somewhere).Outcome);
            RunTicks(2, everyone);
            Assert.True(walker.jobs.curJob!.playerForced, "the order should be marked as the player's");

            Assert.True(RunUntil(8000, () => AtOrBeside(walker.Position, somewhere), everyone),
                "the citizen never reached the ordered cell");

            // A few ticks past arrival: whatever they are doing now, it is not still the player's order.
            RunTicks(20, everyone);
            Assert.False(walker.jobs.curJob?.playerForced == true,
                "the citizen is still carrying the player's order after it completed");
        }

        // ===========================================================================================
        // A bad decision must be allowed to be bad.
        // ===========================================================================================

        /// <summary>
        /// The case CLAUDE's own brief names, built rather than asserted in prose: the settlement's doctor,
        /// with a bleeding patient he would otherwise be treating this second, is ordered to go and stand in a
        /// corner instead. That is a terrible decision and it is the player's to make. The setup is checked
        /// against the doctoring machinery itself — <c>WorkGiver_Tend</c> really does have this patient for
        /// this doctor right now — so "he went to the corner" is a choice the order overrode, not an absence
        /// of anything better to do.
        /// </summary>
        [Fact]
        public void Ordering_the_doctor_away_from_a_bleeding_patient_is_accepted_not_refused()
        {
            Settlement settlement = OpenedSettlement("player-orders-the-doctor-away");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn doctor = everyone[0];
            Pawn patient = everyone[1];
            doctor.workSettings!.SetPriority(WorkTypeDefOf.Doctor, 1);
            MakeBleedingWound(patient);

            // The premise, asked of the doctoring machinery rather than assumed: this doctor has this patient.
            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            Assert.True(TendUtility.HasAnythingToTend(patient),
                "test setup assumption broken: the patient has nothing tendable");
            Assert.True(emergency.HasJobOnThing(doctor, patient),
                "test setup assumption broken: the doctor would not have tended this patient anyway");

            IntVec3 corner = FarReachableCellFrom(map, doctor);
            MapCommandResult ordered = MapCommands.OrderJob(doctor.thingIDNumber, "Goto", corner);

            // Not refused. Not warned about. Not quietly re-ordered into something sensible.
            Assert.Equal(MapCommandOutcome.Done, ordered.Outcome);

            // And it is what he actually does: an order starts the moment it is given, so the think tree — its
            // emergency-work tier, which outranks every need, included — is not asked again until it is done.
            RunTicks(2, everyone);
            Assert.Equal("Goto", doctor.jobs.curJob?.def.defName);
            Assert.True(doctor.jobs.curJob!.playerForced);
        }

        /// <summary>
        /// The order equivalent of <c>MapCommandsTests.A_wall_placed_nowhere_useful_is_accepted_not_refused</c>:
        /// a citizen sent to an empty patch of ground to do nothing at all, while a field she is the only
        /// person able to sow sits unsown. Useless by any judgement the simulation could form, and accepted
        /// exactly like any other order — the command has no opinion about whether an act is worth doing.
        /// </summary>
        [Fact]
        public void An_order_to_go_and_stand_somewhere_useless_is_accepted_not_refused()
        {
            Settlement settlement = OpenedSettlement("player-orders-a-folly");
            CoreMap map = settlement.InteriorMap!;
            SilenceEveryonesStandingWork(settlement, map);

            Pawn worker = AnyCitizenOn(settlement, map);
            worker.workSettings!.SetPriority(WorkTypeDefOf.Growing, 1);
            IntVec3 field = worker.Position;
            map.terrainGrid.SetTerrain(field, TerrainDefOf.Soil);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", new[] { field }).Outcome);

            IntVec3 nowhere = FreeStandableCellNear(map, new IntVec3(2, 0, 2));

            MapCommandResult ordered = MapCommands.OrderJob(worker.thingIDNumber, "Goto", nowhere);

            Assert.Equal(MapCommandOutcome.Done, ordered.Outcome);
            Assert.True(ordered.Changed);
        }

        // ===========================================================================================
        // Cancelling.
        // ===========================================================================================

        [Fact]
        public void Cancelling_withdraws_an_order_the_citizen_has_not_started_yet()
        {
            Settlement settlement = OpenedSettlement("player-changes-her-mind");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn worker = AnyCitizenOn(settlement, map);
            Thing rock = NearestMineableRock(map, worker);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(worker.thingIDNumber, "Mine", rock.thingIDNumber).Outcome);

            MapCommandResult cancelled = MapCommands.CancelOrder(worker.thingIDNumber);

            Assert.Equal(MapCommandOutcome.Done, cancelled.Outcome);
            RunTicks(2000, everyone);
            Assert.False(rock.Destroyed, "a withdrawn order was carried out anyway");
            Assert.False(worker.jobs.curJob?.playerForced == true);
        }

        /// <summary>
        /// Cancelling an order already in hand ends it and hands the citizen straight back to their own work,
        /// in the same call — the revert half of the pattern, on the cancellation path. This is the one place
        /// this surface deliberately differs from <c>CancelDesignation</c>, which leaves a Frame alone because
        /// materials are committed to it; an interrupted job has committed nothing.
        /// </summary>
        [Fact]
        public void Cancelling_ends_an_order_in_hand_and_hands_the_citizen_back_to_her_own_work()
        {
            Settlement settlement = OpenedSettlement("player-recalls-a-citizen");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn walker = AnyCitizenOn(settlement, map);
            IntVec3 destination = FarReachableCellFrom(map, walker);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(walker.thingIDNumber, "Goto", destination).Outcome);

            RunTicks(2, everyone);
            Assert.True(walker.jobs.curJob?.playerForced == true,
                "test setup assumption broken: she never started the order");

            MapCommandResult cancelled = MapCommands.CancelOrder(walker.thingIDNumber);

            Assert.Equal(MapCommandOutcome.Done, cancelled.Outcome);
            // The think tree ran inside EndCurrentJob, so she is already doing something of her own choosing —
            // and whatever it is, it is not the player's order.
            Assert.False(walker.jobs.curJob?.playerForced == true);
        }

        [Fact]
        public void Cancelling_when_there_is_nothing_to_cancel_is_a_no_op()
        {
            Settlement settlement = OpenedSettlement("cancel-with-no-orders");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            MapCommandResult result = MapCommands.CancelOrder(citizen.thingIDNumber);

            Assert.Equal(MapCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
        }

        // ===========================================================================================
        // The physically impossible, refused with the right named outcome — one test per reason.
        // ===========================================================================================

        [Fact]
        public void No_game_running_is_refused_as_no_map()
        {
            // ContentTestBase leaves Find.CurrentGame null; nothing here starts a game at all.
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.OrderJob(1, "Goto", new IntVec3(1, 0, 1)).Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.OrderJob(1, "Mine", 2).Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.CancelOrder(1).Outcome);
        }

        [Fact]
        public void An_unopened_settlement_is_refused_as_no_map()
        {
            Game game = NewSoloGame("orders-before-opening");
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Null(settlement.InteriorMap);

            MapCommandResult result = MapCommands.OrderJob(1, "Goto", new IntVec3(1, 0, 1));

            Assert.Equal(MapCommandOutcome.NoMap, result.Outcome);
        }

        [Fact]
        public void An_unknown_job_def_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-unknown-job");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            MapCommandResult result = MapCommands.OrderJob(citizen.thingIDNumber, "NoSuchJobDefAtAll", citizen.Position);

            Assert.Equal(MapCommandOutcome.UnknownDef, result.Outcome);
        }

        [Fact]
        public void An_id_that_names_nobody_on_this_map_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-unknown-citizen");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            MapCommandResult ordered = MapCommands.OrderJob(int.MaxValue, "Goto", citizen.Position);
            Assert.Equal(MapCommandOutcome.Refused, ordered.Outcome);

            MapCommandResult cancelled = MapCommands.CancelOrder(int.MaxValue);
            Assert.Equal(MapCommandOutcome.Refused, cancelled.Outcome);
        }

        [Fact]
        public void An_id_that_names_a_thing_rather_than_a_person_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-a-log-about");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            Thing logs = ThingMaker.MakeThing(Def("WoodLog"));
            GenSpawn.Spawn(logs, FreeStandableCellNear(map, citizen.Position), map);

            MapCommandResult result = MapCommands.OrderJob(logs.thingIDNumber, "Goto", citizen.Position);

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        /// <summary>
        /// <c>ThinkTrees_Animal.xml</c> is a real second tree with no <c>JobGiver_DirectedOrder</c> in it at
        /// all (<c>AnimalAITests</c> pins that), so an order given to an animal would sit in a queue nothing
        /// ever reads. That is a physical impossibility, not a judgement, and it is refused rather than
        /// accepted-and-silently-dropped.
        /// </summary>
        [Fact]
        public void An_animal_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-a-husky-about");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            var husky = new Pawn(Husky, "Rex");
            GenSpawn.Spawn(husky, FreeStandableCellNear(map, citizen.Position), map);

            MapCommandResult result = MapCommands.OrderJob(husky.thingIDNumber, "Goto", citizen.Position);

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        /// <summary>
        /// <c>Pawn.Tick</c> returns before <c>JobTrackerTick</c> for anyone below <see cref="PawnTier.Full"/>
        /// (spec §11.3), so an Interval or Statistical citizen has no jobs at all and an order to one would
        /// never be looked at. Demotion here is the ordinary path — the attention sweep telling the tracker
        /// nobody is looking at this citizen any more.
        /// </summary>
        [Fact]
        public void A_citizen_who_is_no_longer_simulated_in_full_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-a-coarse-citizen");
            CoreMap map = settlement.InteriorMap!;

            // Whoever the attention sweep will actually let go of: a citizen who is significant for some other
            // reason (a role, a chronicle mention) stays Full however the camera moves, and which of a
            // generated band that is depends on the seed.
            Pawn? citizen = null;
            foreach (Pawn candidate in AllCitizensOn(settlement, map))
            {
                candidate.tier.Notify_AttentionChanged(false);
                if (candidate.tier.Tier == PawnTier.Full) continue;
                citizen = candidate;
                break;
            }
            Assert.NotNull(citizen);

            MapCommandResult result = MapCommands.OrderJob(citizen!.thingIDNumber, "Goto", citizen.Position);

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        /// <summary>A citizen who has died takes no orders. (Death also spawns a corpse and takes the pawn off
        /// the map, so in practice the refusal arrives by the "nobody here with that id" route rather than the
        /// explicit dead check; both are <see cref="MapCommandOutcome.Refused"/> and both are correct, which
        /// is why this asserts the outcome rather than the sentence.)</summary>
        [Fact]
        public void A_citizen_who_has_died_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-the-dead-about");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            int id = citizen.thingIDNumber;
            citizen.health.Kill(null, null);
            Assert.True(citizen.Dead);

            MapCommandResult result = MapCommands.OrderJob(id, "Goto", new IntVec3(1, 0, 1));

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        [Fact]
        public void A_target_cell_off_the_map_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-off-the-map");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            var offMap = new IntVec3(map.Size.x + 5, 0, map.Size.z + 5);

            MapCommandResult result = MapCommands.OrderJob(citizen.thingIDNumber, "Goto", offMap);

            Assert.Equal(MapCommandOutcome.OffMap, result.Outcome);
        }

        [Fact]
        public void A_target_id_that_is_on_no_map_is_refused()
        {
            Settlement settlement = OpenedSettlement("orders-at-a-ghost");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            MapCommandResult result = MapCommands.OrderJob(citizen.thingIDNumber, "Mine", int.MaxValue);

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        /// <summary>
        /// A target that exists but is the wrong kind of thing for the job is the player's mistake to make,
        /// not the command surface's to prevent: the order is accepted, the driver fails it honestly inside
        /// the simulation, and the citizen goes back to work. The line between this and
        /// <see cref="A_target_id_that_is_on_no_map_is_refused"/> is exactly the line the class doc draws —
        /// impossible versus merely wrong.
        /// </summary>
        [Fact]
        public void A_target_that_is_the_wrong_kind_of_thing_is_accepted_and_fails_in_the_simulation()
        {
            Settlement settlement = OpenedSettlement("orders-mining-a-log");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);

            Pawn citizen = AnyCitizenOn(settlement, map);
            Thing logs = ThingMaker.MakeThing(Def("WoodLog"));
            GenSpawn.Spawn(logs, FreeStandableCellNear(map, citizen.Position), map);

            MapCommandResult result = MapCommands.OrderJob(citizen.thingIDNumber, "Mine", logs.thingIDNumber);

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            // And the settlement survives being told to do the impossible: no exception, no stuck citizen.
            RunTicks(600, everyone);
            Assert.False(citizen.Dead);
        }

        // ===========================================================================================
        // Persistence and the seam.
        // ===========================================================================================

        /// <summary>
        /// This surface stores nothing of its own — an order <i>is</i> a <see cref="Job"/> on the citizen's
        /// own queue, and <c>Pawn_JobTracker.ExposeData</c> already scribes that queue. What has to survive a
        /// save is the one bit that makes it an order rather than ordinary work, because without it
        /// <c>DequeueDirectedOrder</c> would walk straight past a loaded order for ever. So this round-trips
        /// exactly the object <c>MapCommands.OrderJob</c> queues.
        /// </summary>
        [Fact]
        public void A_queued_order_survives_a_save_and_load()
        {
            var order = new Job(JobDefOf.Mine, new LocalTargetInfo(new IntVec3(4, 0, 7))) { playerForced = true };

            Job read = Scribe.Load<Job>(Scribe.SaveToString(order, "order"), "order");

            Assert.True(read.playerForced);
            Assert.Same(JobDefOf.Mine, read.def);
            Assert.Equal(new IntVec3(4, 0, 7), read.targetA.Cell);
        }

        /// <summary>
        /// The seam, from the side <c>MapCommandsTests</c> does not cover. That file checks what
        /// <see cref="MapCommandResult"/> hands <i>out</i>; this checks what the commands take <i>in</i>,
        /// which is the half these two methods actually widened — before them nothing on this surface named a
        /// person at all, and "order this pawn" is the request most likely to be answered one day by taking a
        /// <c>Pawn</c>. Every parameter must be a value, an id or a <c>defName</c>: a host that could pass a
        /// live object would already be holding one.
        /// </summary>
        [Fact]
        public void Every_command_takes_only_values_ids_and_defNames()
        {
            var forbidden = new (Type Type, string Why)[]
            {
                (typeof(Def), "a Def — the host would be holding one to pass it"),
                (typeof(Thing), "a live Thing"),
                (typeof(Pawn), "a live Pawn — an id is the handle, not the person"),
                (typeof(CoreMap), "the Map itself — which map is the simulation's answer, never the caller's"),
                (typeof(Settlement), "a live Settlement"),
                (typeof(global::SimWorld.World.World), "the World itself"),
            };

            MethodInfo[] commands = typeof(MapCommands)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            int checkedCommands = 0;
            foreach (MethodInfo command in commands)
            {
                checkedCommands++;
                Assert.Equal(typeof(MapCommandResult), command.ReturnType);

                foreach (ParameterInfo parameter in command.GetParameters())
                {
                    Type type = parameter.ParameterType;
                    Type element = type.IsGenericType ? type.GetGenericArguments()[0] : type;
                    foreach ((Type bad, string why) in forbidden)
                    {
                        string where = command.Name + "(" + parameter.Name + ")";
                        Assert.False(bad.IsAssignableFrom(type), where + " takes " + why);
                        Assert.False(bad.IsAssignableFrom(element), where + " takes a collection of " + why);
                    }
                }
            }

            // Guards the guard: a reflection filter that quietly matches nothing passes for ever.
            Assert.True(checkedCommands >= 7, $"expected the whole command surface, found {checkedCommands} methods");
        }
    }
}
