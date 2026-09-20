using System.Collections.Generic;
using System.Linq;

using SimWorld.God.View;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The drill-down from the civilization aggregate to one named person's state and history
    /// (<c>docs/design/player-first.md</c> §7, <c>docs/spec/simworld-spec.md</c> §12a).
    ///
    /// <para/><b>What these tests are actually defending.</b> Frostpunk 2 moved from named citizens to
    /// faction-level statistics and was reviewed on exactly what it lost: <i>"your citizens are reduced to
    /// faceless statistics, and when a handful of them die from exposure to the cold, it doesn't sting the way
    /// it did in the first game."</i> That is the nearest published failure to where this project stands — we
    /// simulate individuals at RimWorld depth and are building a civilization-scale view over the top, so the
    /// depth is not the risk; burying it is. §7 makes the unbroken path a hard requirement, and a requirement
    /// with no test is an intention.
    ///
    /// <para/>The three that matter most, and why:
    /// <list type="bullet">
    /// <item><description><see cref="The_path_from_the_civilization_to_one_persons_history_is_unbroken"/> walks
    /// aggregate → settlement → named citizen → that citizen's history with every step going through the seam
    /// and none reaching into the simulation. If a future change breaks a rung, this is what says so.</description></item>
    /// <item><description><see cref="A_citizen_who_died_is_still_reachable_in_history"/> is the Frostpunk case
    /// itself. A drill-down that dead-ends at a death loses exactly the person who mattered most.</description></item>
    /// <item><description><see cref="Listing_a_settlements_people_changes_nobodys_tier"/> keeps the view from
    /// becoming a promotion mechanism. A drill-down that promoted whoever it drew would make attention a
    /// function of curiosity and leak §11.3's Full-tier budget straight through the view.</description></item>
    /// </list>
    ///
    /// <para/><b>The history these tests read is real.</b> Every one of them runs the game's own tick loop
    /// (<c>RunTicks</c> with a live <see cref="Game"/>, so world tick, storyteller and the office sweep all
    /// run) and kills people through <c>FamilyManager.HandleDeath</c> — the one funnel every real cause of
    /// death already uses. Nothing here hand-builds a <c>ChronicleEntry</c>; a seam tested against a
    /// hand-built history proves only that the seam can read a list a test wrote.
    /// </summary>
    public class CitizenViewTests : ContentTestBase
    {
        public CitizenViewTests(CoreContentFixture content) : base(content)
        {
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 24);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        /// <summary>Long enough for the office sweep (<see cref="OfficeTuning.ReconcileIntervalTicks"/>) to
        /// seat the settlement's offices and chronicle the accessions — history the tick loop produced rather
        /// than history a test wrote.</summary>
        private static void RunUntilHistoryExists() => RunTicks(OfficeTuning.ReconcileIntervalTicks + 1);

        private static IReadOnlyDictionary<int, Pawn> ById(Settlement settlement) =>
            settlement.Citizens.ToDictionary(p => p.thingIDNumber);

        // ---- the requirement itself ----

        /// <summary>
        /// §7's hard requirement, end to end: <i>"there must be an unbroken path from the civilization
        /// aggregate down to one named person's actual state and history."</i>
        ///
        /// <para/>Every rung below is a seam call. The test never touches <c>Find</c>, a <c>Settlement</c>, a
        /// <c>Pawn</c> or the storyteller to get from one rung to the next — it carries only the values the
        /// previous rung handed it (a tile, then a thing id), which is exactly what the Unity host has. A
        /// version of this test that reached into the simulation between steps would pass over a broken path.
        /// </summary>
        [Fact]
        public void The_path_from_the_civilization_to_one_persons_history_is_unbroken()
        {
            NewSoloGame("citizen-drilldown");
            RunUntilHistoryExists();

            // 1. The civilization aggregate — the rollup a god view opens on.
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            Assert.True(snapshot.Civilization.TotalPopulation > 0);
            Assert.NotEmpty(snapshot.Settlements);

            // 2. One settlement, reached by the handle the aggregate carries.
            SettlementSummary summary = snapshot.Settlements[0];
            SettlementRoster? roster = GodViewSnapshot.CitizensOf(summary.Tile);
            Assert.NotNull(roster);
            Assert.NotEmpty(roster!.Citizens);
            Assert.Equal(summary.Tile, roster.Tile);

            // 3. One named person, reached by the handle the roster carries. Named: the row is enough to pick
            //    somebody out of a crowd, which is the whole difference from a population count.
            CitizenLine line = roster.Citizens.First(c => c.OfficeDefName != null);
            Assert.False(string.IsNullOrWhiteSpace(line.Name));

            CitizenView? view = GodViewSnapshot.Citizen(line.ThingId);
            Assert.NotNull(view);
            Assert.True(view!.IsLive);
            Assert.Equal(line.Name, view.Name);
            Assert.Equal(line.ThingId, view.ThingId);

            // 4. Their actual state: not a band, the real record.
            Assert.NotEmpty(view.Needs);
            Assert.NotEmpty(view.Skills);
            Assert.NotNull(view.HealthFraction);

            // 5. And their history — the half that makes a person rather than a row. The office sweep
            //    chronicled this citizen's accession by name while the tick loop was running; nothing in this
            //    test wrote it.
            Assert.NotEmpty(view.History);
            Assert.Contains(view.History, h => h.Text.Contains("Accession") && h.Text.Contains(line.Name));
        }

        /// <summary>
        /// The Frostpunk case. A citizen dies, the roster stops listing them — correctly, they are not a
        /// citizen any more — and the civilization still remembers them by name, with the death in the record.
        ///
        /// <para/>This is the one that carries the emotional weight, so it is worth being plain about what
        /// would otherwise happen: <c>Settlement.PruneDeadCitizens</c> drops the dead off the roster on a rare
        /// cadence, so a drill-down keyed only on a live roster loses the person on the tick after they die.
        /// That is the same shape as reducing citizens to faceless statistics, one layer down — the aggregate
        /// still shows a death, and there is no longer anyone it happened to.
        /// </summary>
        [Fact]
        public void A_citizen_who_died_is_still_reachable_in_history()
        {
            Game game = NewSoloGame("citizen-death");
            Settlement settlement = PlayerSettlement(game);
            RunUntilHistoryExists();

            SettlementRoster before = GodViewSnapshot.CitizensOf(settlement.tile)!;
            CitizenLine victimLine = before.Citizens[0];

            // Setup reaches into the simulation on purpose — there is no god command that kills a citizen and
            // there should not be. It goes through the real death funnel every cause already uses, so the
            // chronicle line is the one a real death writes.
            Pawn victim = settlement.Citizens.First(p => p.thingIDNumber == victimLine.ThingId);
            Find.FamilyManager.HandleDeath(victim, DeathCause.Injury, ById(settlement));

            // Past the roster prune, so this is the state after the simulation has finished forgetting them.
            RunTicks(SettlementTuning.CitizenMapSyncIntervalTicks * 2 + 1);

            SettlementRoster after = GodViewSnapshot.CitizensOf(settlement.tile)!;
            Assert.DoesNotContain(after.Citizens, c => c.ThingId == victimLine.ThingId);

            // The path past the end of a life. The host held the name off the row it drew; that is all it
            // needs.
            CitizenView remembered = GodViewSnapshot.Remembered(victimLine.Name);
            Assert.False(remembered.IsLive);
            Assert.Equal(victimLine.Name, remembered.Name);
            Assert.NotEmpty(remembered.History);
            Assert.Contains(remembered.History, h => h.Text.Contains("Death") && h.Text.Contains(victimLine.Name));

            // And it under-claims about everything it cannot answer rather than reporting a person with no
            // needs, no skills and perfect health.
            Assert.Equal(CitizenFidelity.NotTracked, remembered.NeedsFidelity);
            Assert.Equal(CitizenFidelity.NotTracked, remembered.HealthFidelity);
            Assert.Equal(CitizenFidelity.NotTracked, remembered.HediffFidelity);
            Assert.Null(remembered.HealthFraction);
            Assert.Null(remembered.Tier);
            Assert.Empty(remembered.Needs);
            Assert.Empty(remembered.Skills);
        }

        /// <summary>
        /// A citizen who died where there is a body to find is still openable in full, not merely remembered:
        /// their traits, their skills and their relationships are all still there to read. Losing somebody is
        /// the moment a player most wants to look at them.
        /// </summary>
        [Fact]
        public void A_citizen_who_died_on_an_open_map_can_still_be_opened_in_full()
        {
            Game game = NewSoloGame("citizen-death-open");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            RunUntilHistoryExists();

            CitizenLine victimLine = GodViewSnapshot.CitizensOf(settlement.tile)!
                .Citizens.First(c => settlement.Citizens.Any(p => p.thingIDNumber == c.ThingId && p.Spawned));
            Pawn victim = settlement.Citizens.First(p => p.thingIDNumber == victimLine.ThingId);

            Find.FamilyManager.HandleDeath(victim, DeathCause.Injury, ById(settlement));
            RunTicks(SettlementTuning.CitizenMapSyncIntervalTicks * 2 + 1);

            Assert.DoesNotContain(
                GodViewSnapshot.CitizensOf(settlement.tile)!.Citizens, c => c.ThingId == victimLine.ThingId);

            CitizenView? view = GodViewSnapshot.Citizen(victimLine.ThingId);
            Assert.NotNull(view);
            Assert.True(view!.IsLive);
            Assert.True(view.Dead);
            Assert.Equal(victimLine.Name, view.Name);
            Assert.NotEmpty(view.Skills);
            Assert.Contains(view.History, h => h.Text.Contains("Death"));
        }

        // ---- the tiering constraints ----

        /// <summary>
        /// Reading a settlement's roster must not promote anybody. The list query is meant to be callable on a
        /// settlement, which means it is callable often; if it moved a tier it would be a promotion mechanism
        /// wearing a readout's clothes, and §11.3's Full-tier budget would be spent by whatever the player
        /// happened to look at.
        ///
        /// <para/>Driven against a settlement holding all three tiers at once, because a test where everybody
        /// is Full could not tell a no-op from a promotion.
        /// </summary>
        [Fact]
        public void Listing_a_settlements_people_changes_nobodys_tier()
        {
            Game game = NewSoloGame("citizen-tier-stable");
            Settlement settlement = PlayerSettlement(game);
            RunTicks(1);

            // A spread of tiers, reached the way the simulation reaches it: attention leaves, and a citizen
            // who stays insignificant settles.
            foreach (Pawn p in settlement.Citizens.Take(4).ToList())
            {
                p.tier.Notify_AttentionChanged(false);
            }
            foreach (Pawn p in settlement.Citizens.Take(2).ToList())
            {
                p.tier.DemoteToStatistical();
            }

            Dictionary<int, PawnTier> before = settlement.Citizens.ToDictionary(p => p.thingIDNumber, p => p.tier.Tier);
            Assert.True(before.Values.Distinct().Count() >= 2, "the test needs more than one tier present to mean anything");

            SettlementRoster roster = GodViewSnapshot.CitizensOf(settlement.tile)!;
            GodViewSnapshot.CitizensOf(settlement.tile);
            GodViewSnapshot.CitizensOf(settlement.tile);

            foreach (Pawn p in settlement.Citizens)
            {
                Assert.Equal(before[p.thingIDNumber], p.tier.Tier);
            }

            // And the roster reports the tier it found rather than the one it would prefer.
            foreach (CitizenLine line in roster.Citizens)
            {
                Assert.Equal(before[line.ThingId], line.Tier);
            }

            // The expensive query is held to the same rule: opening one person does not promote them either.
            int statisticalId = roster.Citizens.First(c => c.Tier == PawnTier.Statistical).ThingId;
            CitizenView? view = GodViewSnapshot.Citizen(statisticalId);
            Assert.NotNull(view);
            Assert.Equal(PawnTier.Statistical, view!.Tier);
            foreach (Pawn p in settlement.Citizens)
            {
                Assert.Equal(before[p.thingIDNumber], p.tier.Tier);
            }
        }

        /// <summary>
        /// The over-claim rule, which this project keeps finding the wrong side of: a zero that means "we did
        /// not look" is indistinguishable on screen from a zero that means "there is nothing there", and the
        /// second is the reading a player takes.
        ///
        /// <para/>So a Statistical citizen's view reports their identity and history as real, says plainly that
        /// their needs and health are cohort samples rather than measurements, and reports <b>no hediffs at
        /// all</b> with <see cref="CitizenFidelity.NotTracked"/> beside them — because the tier does not
        /// advance a hediff set, so whatever it still holds is frozen at whatever it was when they left
        /// Interval. An empty list with <see cref="CitizenFidelity.Tracked"/> beside it would be a clean bill
        /// of health nobody checked.
        /// </summary>
        [Fact]
        public void A_statistical_citizens_view_reports_absence_rather_than_zero()
        {
            Game game = NewSoloGame("citizen-statistical-honesty");
            Settlement settlement = PlayerSettlement(game);
            RunTicks(1);

            Pawn settled = settlement.Citizens[0];
            settled.tier.Notify_AttentionChanged(false);
            settled.tier.DemoteToStatistical();
            Assert.Equal(PawnTier.Statistical, settled.tier.Tier);

            CitizenView view = GodViewSnapshot.Citizen(settled.thingIDNumber)!;
            Assert.Equal(PawnTier.Statistical, view.Tier);

            // Cannot report: the hediff set. Empty, and labelled as unlooked-at rather than as clean.
            Assert.Empty(view.Hediffs);
            Assert.Equal(CitizenFidelity.NotTracked, view.HediffFidelity);

            // Reports, but says it is a sample rather than a measurement.
            Assert.Equal(CitizenFidelity.Sampled, view.NeedsFidelity);
            Assert.Equal(CitizenFidelity.Sampled, view.HealthFidelity);
            Assert.Equal(CitizenFidelity.Sampled, view.MoodFidelity);
            Assert.All(view.Needs, n => Assert.Equal(CitizenFidelity.Sampled, n.Fidelity));

            // Can report, for real, at every tier: who they are. This is the half §7 exists to protect — a
            // cohort member is still a named person with a life behind them.
            Assert.False(string.IsNullOrWhiteSpace(view.Name));
            Assert.NotNull(view.AgeBiologicalYears);
            Assert.NotEmpty(view.Skills);
            Assert.NotEmpty(view.Traits);

            // A citizen at the attended settlement, by contrast, is measured — so the fidelity flags carry
            // information rather than always saying the same thing.
            CitizenView attended = GodViewSnapshot.Citizen(
                settlement.Citizens.First(p => p.tier.Tier == PawnTier.Full).thingIDNumber)!;
            Assert.Equal(CitizenFidelity.Tracked, attended.NeedsFidelity);
            Assert.Equal(CitizenFidelity.Tracked, attended.HediffFidelity);
        }

        /// <summary>
        /// A settlement's roster lists everybody it has an individual record of and states, as a count, how
        /// many people it does not — the bare Statistical cohort, which by that tier's own design has no
        /// <c>Pawn</c> object and therefore no name, age or history to show. The three numbers have to add up,
        /// or the view is quietly losing people.
        /// </summary>
        [Fact]
        public void The_roster_accounts_for_the_people_it_cannot_list()
        {
            Game game = NewSoloGame("citizen-roster-accounting");
            Settlement settlement = PlayerSettlement(game);
            settlement.AddStatisticalPeople(40_000);

            SettlementRoster roster = GodViewSnapshot.CitizensOf(settlement.tile)!;

            Assert.Equal(settlement.Citizens.Count, roster.Citizens.Count);
            Assert.Equal(40_000, roster.UnlistedCohortPopulation);
            Assert.Equal(roster.Citizens.Count + roster.UnlistedCohortPopulation, roster.TotalPopulation);
            Assert.Equal(settlement.TotalPopulation, roster.TotalPopulation);

            // The cohort is counted, never materialised: forty thousand people did not become forty thousand
            // rows. That is the constraint §11.3 exists for, and the one a naive drill-down would break first.
            Assert.True(roster.Citizens.Count < 100);
        }

        // ---- what the rows and the detail actually carry ----

        /// <summary>
        /// A roster row has to be enough to pick a person out — a name, an age, what they do, how they are —
        /// and deliberately not enough to reconstruct them. This pins the first half; <c>GodViewSeamIntegrity
        /// Tests</c> pins the second, structurally, over the whole namespace.
        /// </summary>
        [Fact]
        public void A_roster_row_is_enough_to_pick_somebody_out()
        {
            Game game = NewSoloGame("citizen-row");
            Settlement settlement = PlayerSettlement(game);
            RunUntilHistoryExists();

            SettlementRoster roster = GodViewSnapshot.CitizensOf(settlement.tile)!;

            Assert.All(roster.Citizens, line =>
            {
                Assert.False(string.IsNullOrWhiteSpace(line.Name));
                Assert.False(string.IsNullOrWhiteSpace(line.FullName));
                Assert.True(line.AgeBiologicalYears >= 0);
                Assert.InRange(line.HealthFraction, 0f, 1f);
                if (line.MoodLevel.HasValue) Assert.InRange(line.MoodLevel.Value, 0f, 1f);
            });

            // Distinct people, not a repeated template.
            Assert.Equal(roster.Citizens.Count, roster.Citizens.Select(c => c.ThingId).Distinct().Count());
            Assert.True(roster.Citizens.Select(c => c.Name).Distinct().Count() > 1);

            // The office the settlement really seated shows up on the row of the citizen who really holds it,
            // read back through the office system rather than restated here.
            CitizenLine holder = roster.Citizens.First(c => c.OfficeDefName != null);
            Pawn pawn = settlement.Citizens.First(p => p.thingIDNumber == holder.ThingId);
            Assert.Equal(OfficeManager.OfficeOf(pawn)!.defName, holder.OfficeDefName);
            Assert.NotNull(holder.RoleDefName);
        }

        /// <summary>
        /// Relationships come back named, in both directions, and they come from the simulation's own relation
        /// workers rather than from a second reading of the family fields that could drift from it. Sibling is
        /// the case that proves it: nothing stores a sibling link at all — it is derived from shared parents —
        /// so a view that read <c>Pawn_RelationsTracker</c>'s fields would simply never report one.
        /// </summary>
        [Fact]
        public void Relationships_come_back_named_and_from_the_simulations_own_rules()
        {
            Game game = NewSoloGame("citizen-relations");
            Settlement settlement = PlayerSettlement(game);
            RunTicks(1);

            Pawn parent = settlement.Citizens[0];
            Pawn childA = settlement.Citizens[1];
            Pawn childB = settlement.Citizens[2];

            parent.relations.Notify_ChildBorn(childA.thingIDNumber);
            parent.relations.Notify_ChildBorn(childB.thingIDNumber);
            childA.relations.SetParents(parent.thingIDNumber, Pawn_RelationsTracker.None);
            childB.relations.SetParents(parent.thingIDNumber, Pawn_RelationsTracker.None);

            CitizenView parentView = GodViewSnapshot.Citizen(parent.thingIDNumber)!;
            Assert.Contains(parentView.Relationships,
                r => r.RelationDefName == "Child" && r.OtherThingId == childA.thingIDNumber && r.OtherName == childA.Label);
            Assert.All(parentView.Relationships, r => Assert.False(string.IsNullOrWhiteSpace(r.RelationLabel)));

            CitizenView childView = GodViewSnapshot.Citizen(childA.thingIDNumber)!;
            Assert.Contains(childView.Relationships,
                r => r.RelationDefName == "Parent" && r.OtherThingId == parent.thingIDNumber);

            // Derived, stored nowhere — the reason this reads the workers.
            Assert.Contains(childView.Relationships,
                r => r.RelationDefName == "Sibling" && r.OtherThingId == childB.thingIDNumber);
        }

        // ---- absence, reported as absence ----

        /// <summary>
        /// Stale handles answer "no" rather than throwing or inventing something. A settlement destroyed
        /// between two snapshots, and a citizen who died and left no body, look exactly like this from the
        /// host's side — the same case <see cref="GodCommandOutcome.UnknownSettlement"/> already names on the
        /// command half of the seam.
        /// </summary>
        [Fact]
        public void An_unknown_settlement_or_citizen_reports_absence()
        {
            NewSoloGame("citizen-unknown");
            RunTicks(1);

            Assert.Null(GodViewSnapshot.CitizensOf(-1));
            Assert.Null(GodViewSnapshot.CitizensOf(int.MaxValue));
            Assert.Null(GodViewSnapshot.Citizen(-999));
        }

        /// <summary>
        /// A name the chronicle has never mentioned gets an empty history, not an error. "Nothing is recorded
        /// about this person" is a true answer a host can draw; an exception is a thing a host has to guess
        /// the meaning of.
        /// </summary>
        [Fact]
        public void A_name_the_chronicle_never_mentions_has_an_empty_history_rather_than_an_error()
        {
            NewSoloGame("citizen-unremembered");
            RunUntilHistoryExists();

            CitizenView view = GodViewSnapshot.Remembered("Nobodyatallwhoeverlived");

            Assert.False(view.IsLive);
            Assert.Empty(view.History);
            Assert.Equal("Nobodyatallwhoeverlived", view.Name);
        }

        /// <summary>
        /// The queries are taken when they are called, not sliced out of whatever snapshot the host happens to
        /// be holding — so each result carries its own tick and a host can tell whether the two agree. The
        /// alternative is the "a live reference tears" failure <c>GodViewSnapshot</c> was built to avoid, one
        /// level down: a citizen drawn as though they were current at a tick they were not.
        /// </summary>
        [Fact]
        public void A_drill_down_carries_the_tick_it_was_taken_at()
        {
            Game game = NewSoloGame("citizen-tick");
            Settlement settlement = PlayerSettlement(game);
            RunTicks(1);

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            SettlementRoster atCapture = GodViewSnapshot.CitizensOf(settlement.tile)!;
            Assert.Equal(snapshot.TicksGame, atCapture.TicksGame);

            RunTicks(120);

            SettlementRoster later = GodViewSnapshot.CitizensOf(settlement.tile)!;
            Assert.True(later.TicksGame > snapshot.TicksGame);
            Assert.Equal(later.TicksGame, GodViewSnapshot.Citizen(later.Citizens[0].ThingId)!.TicksGame);
        }
    }
}
