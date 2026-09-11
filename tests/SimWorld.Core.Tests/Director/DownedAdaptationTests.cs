using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// A citizen going down reaching the storyteller (<see cref="StorytellerPawnEvents.Notify_PawnDowned"/> →
    /// <see cref="StoryWatcher_Adaptation.Notify_ColonistDowned"/>).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> This is the exact twin of the
    /// <see cref="StoryWatcher_Adaptation.Notify_ColonistDied"/> defect the raid lane fixed:
    /// <c>Notify_ColonistDowned</c>, its <see cref="StoryWatcher_Adaptation.DownedAdaptDaysPenalty"/> constant
    /// and its place in the curve all existed, and no line of <c>src/</c> raised it — so adaptation only ever
    /// eased after a death, and a colony that was mauled without losing anybody looked, to the narrator,
    /// exactly as untroubled as one that had spent the month farming.
    ///
    /// <para/><b>The wiring is deliberately not a copy of the death side.</b> The raid lane raised
    /// <c>Notify_ColonistDied</c> from <see cref="SettlementRaidResolver"/> specifically, and deliberately not
    /// from <c>FamilyManager.HandleDeath</c>, so that dying of old age would not ease the storyteller.
    /// Downing has no comparable non-threat cause to exclude and exactly one funnel —
    /// <c>Pawn_HealthTracker.MakeDowned</c> — so the hook goes there, which is where RimWorld raises it too.
    /// What downing does have, and death did not, is that the funnel runs for raiders and animals as well as
    /// for citizens: the guard those tests pin is the whole reason
    /// <see cref="StorytellerPawnEvents"/> exists as a class rather than as one more line.
    /// </summary>
    public class DownedAdaptationTests : ContentTestBase
    {
        public DownedAdaptationTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

        private static StoryWatcher_Adaptation Adaptation => Find.Storyteller.adaptation;

        private static DifficultyDef Medium => DefDatabase<DifficultyDef>.GetNamed("Medium");

        /// <summary>A stretch of quiet on the clock, so a penalty has something to cut into and cannot be
        /// hidden by the zero floor <see cref="StoryWatcher_Adaptation.Notify_ColonistDowned"/> clamps to.</summary>
        private static float BuildQuietTime()
        {
            for (int i = 0; i < 4000; i++) Adaptation.AdaptationTick(Medium);
            Assert.True(Adaptation.AdaptDays > StoryWatcher_Adaptation.DeathAdaptDaysPenalty
                                               + StoryWatcher_Adaptation.DownedAdaptDaysPenalty,
                "needed a peaceful stretch on the clock before a downing could be seen to cut it short");
            return Adaptation.AdaptDays;
        }

        /// <summary>Registers a civilization with the storyteller holding these pawns — the same roster the
        /// threat curve reads, which is what decides whose downing counts.</summary>
        private static CivilizationTarget CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
            return target;
        }

        private static Faction Raiders() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough", "F_Rough");

        // ---- the headline ----

        [Fact]
        public void A_downed_citizen_moves_the_storytellers_adaptation()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            // Through the real funnel — the health tracker's own mobile-to-down transition, not the hook by
            // hand. Before this lane nothing in the core raised Notify_ColonistDowned at all.
            citizen.health.ForceDowned = true;

            Assert.True(citizen.health.Downed);
            Assert.True(Adaptation.AdaptDays < quiet,
                "a citizen went down and the storyteller's quiet-time clock did not move");
            Assert.Equal(quiet - StoryWatcher_Adaptation.DownedAdaptDaysPenalty, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_downing_costs_the_storyteller_less_than_a_death()
        {
            // The ordering the two constants encode: losing someone outright is worse news than losing them
            // for a while. Asserted as an ordering rather than against either literal, both of which are
            // documented approximations of RimWorld's own.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.ForceDowned = true;
            float costOfDowning = quiet - Adaptation.AdaptDays;

            float beforeDeath = Adaptation.AdaptDays;
            Adaptation.Notify_ColonistDied();
            float costOfDeath = beforeDeath - Adaptation.AdaptDays;

            Assert.True(costOfDowning > 0f);
            Assert.True(costOfDeath > costOfDowning,
                "a downing (" + costOfDowning + " days) did not cost less than a death (" + costOfDeath + ")");
        }

        // ---- not counted twice ----

        [Fact]
        public void A_citizen_downed_and_then_killed_does_not_move_adaptation_twice()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.ForceDowned = true;
            float afterDowning = Adaptation.AdaptDays;
            Assert.Equal(quiet - StoryWatcher_Adaptation.DownedAdaptDaysPenalty, afterDowning, 4);

            // Pawn_HealthTracker.Kill sets the dead state directly and never passes back through MakeDowned,
            // so the same loss is not charged again on the way out.
            citizen.health.Kill(null, null);

            Assert.True(citizen.Dead);
            Assert.Equal(afterDowning, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Staying_down_through_further_damage_is_charged_once()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.ForceDowned = true;
            float afterDowning = Adaptation.AdaptDays;

            // Every later health re-check on an already-downed pawn (a hediff tick, another hit) re-enters
            // the same state machine; only the mobile-to-down transition may charge.
            for (int i = 0; i < 20; i++) citizen.health.CheckForStateChange(null, null);

            Assert.True(citizen.health.Downed);
            Assert.Equal(afterDowning, Adaptation.AdaptDays, 4);
            Assert.Equal(quiet - StoryWatcher_Adaptation.DownedAdaptDaysPenalty, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Getting_back_up_and_going_down_again_is_a_second_event()
        {
            // The other side of the same rule, and the one that keeps the first from being a suppression:
            // being downed twice over a campaign is twice the bad news, exactly as it is in RimWorld.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.ForceDowned = true;
            citizen.health.ForceDowned = false;
            Assert.False(citizen.health.Downed);
            citizen.health.ForceDowned = true;

            Assert.Equal(quiet - 2f * StoryWatcher_Adaptation.DownedAdaptDaysPenalty, Adaptation.AdaptDays, 4);
        }

        // ---- whose downing counts ----

        [Fact]
        public void Downing_an_attacker_does_not_ease_the_storyteller()
        {
            // The guard that matters most. Pawn_HealthTracker.MakeDowned runs for every pawn alive, so an
            // unguarded hook would have made the storyteller go easier on a colony every time it knocked an
            // enemy over — the exact opposite of what the adaptation curve is for.
            float quiet = BuildQuietTime();
            CivilizationOf(NewHuman("Citizen"));

            Pawn raider = NewHuman("Raider");
            raider.faction = Raiders();
            raider.health.ForceDowned = true;

            Assert.True(raider.health.Downed);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Downing_an_animal_does_not_move_it()
        {
            // Humanlike only, as RimWorld's own filter is — and asserted with the animal *on the roster*, so
            // it is the species test doing the work and not membership.
            float quiet = BuildQuietTime();
            var husky = new Pawn(Husky, "Dog");
            CivilizationOf(husky);

            husky.health.ForceDowned = true;

            Assert.True(husky.health.Downed);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_pawn_belonging_to_no_civilization_at_all_does_not_move_it()
        {
            // Membership is read off the storyteller's own targets. A pawn nobody is telling a story about —
            // a wanderer, a test fixture — is not a loss the narrator should react to.
            float quiet = BuildQuietTime();
            Pawn stranger = NewHuman("Stranger");

            stranger.health.ForceDowned = true;

            Assert.True(stranger.health.Downed);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
            Assert.False(StorytellerPawnEvents.Notify_PawnDowned(stranger));
        }

        // ---- persistence ----

        [Fact]
        public void What_a_downing_cost_the_storyteller_survives_a_save_and_a_load()
        {
            // The hook keeps no state of its own; what it moves is the adaptation clock, which is saved with
            // the storyteller. A downing the save forgets is a downing that did nothing, one reload later.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);
            citizen.health.ForceDowned = true;

            float afterDowning = Adaptation.AdaptDays;
            Assert.True(afterDowning < quiet);

            string xml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            global::SimWorld.Director.Storyteller loaded = Scribe.Load<global::SimWorld.Director.Storyteller>(
                xml, "storyteller", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(afterDowning, loaded.adaptation.AdaptDays, 4);
            Assert.Equal(Adaptation.TotalThreatPointsFactor(Medium), loaded.adaptation.TotalThreatPointsFactor(Medium), 4);
        }
    }
}
