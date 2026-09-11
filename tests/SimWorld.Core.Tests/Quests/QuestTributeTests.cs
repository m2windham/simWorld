using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Director;
using SimWorld.Quests;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Quests
{
    /// <summary>
    /// A tribute demand that can actually be answered (<see cref="QuestPart_Tribute"/>).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> <c>Quest_Tribute</c> shipped for four
    /// batches as a script that set <c>demandSilver</c> from the threat points, sent a letter reading "A rival
    /// civilization demands tribute", waited out the offer and ended <see cref="QuestEndOutcome.Fail"/> —
    /// always, and identically, whether the civilization was rich or destitute. Nothing read
    /// <c>demandSilver</c>; no coin moved in either direction; refusing cost nothing. Measured on the fixture
    /// <see cref="A_tribute_demand_used_to_be_a_letter_and_nothing_else"/> builds, the old behaviour produced:
    /// quest state <c>EndedFail</c>; the seat's silver unchanged; goodwill with the demanding faction
    /// unchanged; and a chronicle holding nothing about it. The quest said a rival had threatened reprisal and
    /// no reprisal existed.
    ///
    /// <para/>It is the mirror of the defect <see cref="QuestRewardSink"/> was written for. Value could not
    /// enter the civilization; it could not leave it either.
    /// </summary>
    public class QuestTributeTests : ContentTestBase
    {
        public QuestTributeTests(CoreContentFixture content) : base(content)
        {
            // Find's letters/quest/storyteller/faction services are thread-static and not covered by
            // ContentTestBase's Find.Reset(); give every test a clean slate explicitly, as QuestTests does.
            Find.LetterStack = new LetterStack();
            Find.QuestManager = new QuestManager();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            QuestGen.ResetIdCounterForTests();
        }

        // ---- fixtures ----

        private const string Rival = "OutlanderCivilization";

        private static ThingDef Silver => EconomyThingDefOf.Silver;

        private static Settlement Town(string name, int tile, int foundingTick) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, name, foundingTick);

        private static CivilizationTarget TargetOf(params Settlement[] settlements)
        {
            var target = new CivilizationTarget();
            target.SetSettlements(settlements);
            return target;
        }

        /// <summary>Poses a civilization holding <paramref name="silver"/>, registered with the storyteller so
        /// <see cref="QuestCivilization.Current"/> resolves it the way it does in a running game with no
        /// <see cref="Game"/> built. Returns the seat.</summary>
        private static Settlement CivilizationHolding(int silver)
        {
            Settlement seat = Town("Seat", 1, foundingTick: 0);
            if (silver > 0) seat.AddStore(Silver, silver);
            Find.Storyteller.RegisterTarget(TargetOf(seat));
            return seat;
        }

        private static void TwoFactions()
        {
            var player = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Us", "F_Us");
            var other = new Faction(DefDatabase<FactionDef>.GetNamed(Rival), "Them", "F_Them");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(other);
        }

        private static Faction Player => Find.FactionManager.OfPlayer!;

        private static Faction RivalFaction =>
            Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed(Rival))!;

        /// <summary>Runs a bare tribute part, the way the quest does, and hands back what it decided.</summary>
        private static TributeOutcome Demand(float silver, float onPayment = 0f, float onRefusal = -15f)
        {
            var quest = new Quest { id = 1, initiateSignal = "quest1.initiate" };
            var part = new QuestPart_Tribute
            {
                inSignal = quest.initiateSignal,
                silverDemanded = silver,
                factionDefName = Rival,
                goodwillOnPayment = onPayment,
                goodwillOnRefusal = onRefusal,
            };
            quest.AddPart(part);
            quest.SendSignal(quest.initiateSignal);
            return part.Outcome!.Value;
        }

        private static IEnumerable<string> ChronicleHeadlines() =>
            Find.Storyteller.Chronicle.Select(e => e.incidentDefName);

        // ---- the headline ----

        [Fact]
        public void A_tribute_demand_used_to_be_a_letter_and_nothing_else()
        {
            TwoFactions();
            Settlement seat = CivilizationHolding(500);
            int goodwillBefore = Player.GoodwillWith(RivalFaction);

            TributeOutcome outcome = Demand(200f);

            // The civilization could cover it, so it did — and the world shows it in both directions at once.
            Assert.True(outcome.Paid);
            Assert.Equal(200, outcome.HandedOver);
            Assert.Equal(300, seat.StoreCountOf(Silver));
            Assert.True(outcome.ChangedTheWorld);

            // ...and the civilization's record says it happened, the way a raid's does.
            Assert.Contains(ChronicleHeadlines(), h => h.StartsWith("Tribute paid:", StringComparison.Ordinal));

            // Paying buys the absence of a reprisal, not a friendship.
            Assert.Equal(goodwillBefore, Player.GoodwillWith(RivalFaction));
        }

        [Fact]
        public void Refusing_costs_the_relation_which_is_the_reprisal_the_quest_promises()
        {
            TwoFactions();
            Settlement seat = CivilizationHolding(50);
            int goodwillBefore = Player.GoodwillWith(RivalFaction);

            TributeOutcome outcome = Demand(200f);

            Assert.False(outcome.Paid);
            Assert.Equal(0, outcome.HandedOver);
            Assert.Equal(50, seat.StoreCountOf(Silver));
            Assert.True(Player.GoodwillWith(RivalFaction) < goodwillBefore,
                "the demand went unmet and the relation did not move, so refusing was free again");
            Assert.True(outcome.ChangedTheWorld);
            Assert.Contains(ChronicleHeadlines(), h => h.StartsWith("Tribute refused:", StringComparison.Ordinal));
        }

        // ---- the rules the branch rests on ----

        /// <summary>
        /// All or nothing, which is the decision recorded in <see cref="QuestPart_Tribute"/>'s own doc: a
        /// civilization one coin short refuses outright rather than handing over what it has. A part-payment
        /// would need a rule for what fraction buys what fraction of goodwill, and there is no such number to
        /// source.
        /// </summary>
        [Fact]
        public void A_civilization_one_coin_short_pays_nothing_rather_than_what_it_has()
        {
            TwoFactions();
            Settlement seat = CivilizationHolding(199);

            TributeOutcome outcome = Demand(200f);

            Assert.False(outcome.Paid);
            Assert.Equal(199, seat.StoreCountOf(Silver));

            // ...and exactly enough pays in full. The boundary is >=, not >.
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Settlement exact = CivilizationHolding(200);
            TributeOutcome met = Demand(200f);
            Assert.True(met.Paid);
            Assert.Equal(0, exact.StoreCountOf(Silver));
        }

        /// <summary>The treasury is never overdrawn. <see cref="Settlement.SetStoreCount"/> clamps at zero, so
        /// a demand larger than the holdings could have silently zeroed the ledger and reported success; the
        /// affordability check is what stops it, and this is the test that keeps that true.</summary>
        [Fact]
        public void A_demand_larger_than_the_treasury_never_empties_it()
        {
            TwoFactions();
            Settlement seat = CivilizationHolding(120);

            Demand(100000f);

            Assert.Equal(120, seat.StoreCountOf(Silver));
        }

        /// <summary>
        /// Determinism, for the same reason <see cref="QuestRewardSink"/> takes no roll: this runs inside
        /// <see cref="QuestManager.QuestManagerTick"/>, so a draw here would shift every subsequent roll in
        /// the game by the accident of when an offer happened to lapse.
        /// </summary>
        [Fact]
        public void Answering_a_demand_takes_no_roll_at_all()
        {
            TwoFactions();
            CivilizationHolding(500);
            uint before = Rand.Current.Iterations;

            Demand(200f);

            Assert.Equal(before, Rand.Current.Iterations);
        }

        /// <summary>A civilization that has lost every settlement has no treasury at all. It refuses, because
        /// it cannot pay — not because it chose to — and the outcome says which.</summary>
        [Fact]
        public void A_civilization_with_no_settlements_refuses_because_it_cannot_pay()
        {
            TwoFactions();
            Find.Storyteller.RegisterTarget(TargetOf());

            TributeOutcome outcome = Demand(200f);

            Assert.False(outcome.Paid);
            Assert.Equal(0, outcome.Held);
            Assert.Equal("", outcome.Recipient);
        }

        /// <summary>A solo start has no rivals, and the script still names one. The relation cannot move; what
        /// must not happen is the part pretending it did, or throwing.</summary>
        [Fact]
        public void A_demand_from_a_faction_this_world_does_not_have_still_resolves()
        {
            Settlement seat = CivilizationHolding(50);

            TributeOutcome outcome = Demand(200f);

            Assert.False(outcome.Paid);
            Assert.Equal(0, outcome.GoodwillApplied);
            Assert.Equal(50, seat.StoreCountOf(Silver));
        }

        /// <summary>A demand that rounds to nothing is not a demand. It costs nothing and is met, rather than
        /// producing a reprisal for failing to hand over zero coins.</summary>
        [Fact]
        public void A_demand_that_rounds_to_nothing_is_met_for_free()
        {
            TwoFactions();
            Settlement seat = CivilizationHolding(10);
            int goodwillBefore = Player.GoodwillWith(RivalFaction);

            TributeOutcome outcome = Demand(0.4f);

            Assert.True(outcome.Paid);
            Assert.Equal(0, outcome.Demanded);
            Assert.Equal(10, seat.StoreCountOf(Silver));
            Assert.Equal(goodwillBefore, Player.GoodwillWith(RivalFaction));
            Assert.DoesNotContain(ChronicleHeadlines(), h => h.StartsWith("Tribute", StringComparison.Ordinal));
        }

        // ---- the shipped script, end to end ----

        /// <summary>
        /// The script itself, run the way a game runs it: accepted, then ticked until the offer's delay
        /// elapses and the demand is answered. This is the test that would have caught the original defect —
        /// the old script reached <c>EndedFail</c> here with a full treasury and an unmoved relation.
        /// </summary>
        [Theory]
        [InlineData(100000, true, QuestState.EndedSuccess)]
        [InlineData(0, false, QuestState.EndedFailed)]
        public void The_shipped_script_ends_on_whether_the_civilization_could_pay(
            int silverHeld, bool expectPaid, QuestState expectedState)
        {
            TwoFactions();
            Settlement seat = CivilizationHolding(silverHeld);
            int goodwillBefore = Player.GoodwillWith(RivalFaction);

            // The demand is the quest's own threat points, and IncidentWorker_GiveQuest is what puts them on
            // the slate — so the fixture has to as well, or the script generates a demand for nothing and
            // every civilization "pays" it. Asserted below rather than assumed.
            var slate = new Slate();
            slate.Set("points", 250f);

            Quest quest = QuestGen.Generate(
                DefDatabase<QuestScriptDef>.GetNamed("Quest_Tribute"), slate);
            Find.QuestManager.Add(quest);
            quest.Accept();

            var demanded = quest.PartsListForReading.OfType<QuestPart_Tribute>().Single();
            Assert.True(demanded.silverDemanded > 0f,
                "the script demanded nothing, so both branches would read as paid");

            var delay = quest.PartsListForReading.OfType<QuestPart_Delay>().First();
            int ticks = delay.TicksLeft;
            Assert.True(ticks > 0, "the script's delay did not start, so the demand would never be answered");
            for (int i = 0; i < ticks; i++) Find.QuestManager.QuestManagerTick();

            Assert.NotNull(demanded.Outcome);
            Assert.Equal(expectPaid, demanded.Outcome!.Value.Paid);
            Assert.Equal(expectedState, quest.State);

            if (expectPaid)
            {
                // The demand is the quest's own threat points, so the figure is whatever the storyteller
                // bought it at — asserted as "something left the treasury", never as a literal.
                Assert.True(seat.StoreCountOf(Silver) < silverHeld,
                    "the tribute was paid and the treasury is no lighter for it");
                Assert.Equal(goodwillBefore, Player.GoodwillWith(RivalFaction));
            }
            else
            {
                Assert.True(Player.GoodwillWith(RivalFaction) < goodwillBefore,
                    "the tribute went unpaid and the rival did not mind");
            }
        }

        // ---- persistence ----

        /// <summary>
        /// The part keeps the demand and both branch signals, so a save taken while a tribute offer is open
        /// reloads into a quest that can still be answered. A reload that forgot the demand would silently
        /// turn every open tribute into a free one.
        /// </summary>
        [Fact]
        public void An_open_tribute_demand_survives_a_save_and_a_load()
        {
            TwoFactions();
            CivilizationHolding(500);

            var slate = new Slate();
            slate.Set("points", 250f);

            Quest quest = QuestGen.Generate(
                DefDatabase<QuestScriptDef>.GetNamed("Quest_Tribute"), slate);
            Find.QuestManager.Add(quest);
            quest.Accept();

            var before = quest.PartsListForReading.OfType<QuestPart_Tribute>().Single();
            Assert.True(before.silverDemanded > 0f, "a demand of zero would round-trip trivially");

            string xml = Scribe.SaveToString(Find.QuestManager, "questManager");
            QuestManager loaded = Scribe.Load<QuestManager>(
                xml, "questManager", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            var after = loaded.QuestsListForReading.Single()
                .PartsListForReading.OfType<QuestPart_Tribute>().Single();

            Assert.Equal(before.silverDemanded, after.silverDemanded);
            Assert.Equal(before.factionDefName, after.factionDefName);
            Assert.Equal(before.goodwillOnRefusal, after.goodwillOnRefusal);
            Assert.Equal(before.outSignalPaid, after.outSignalPaid);
            Assert.Equal(before.outSignalRefused, after.outSignalRefused);
        }
    }
}
