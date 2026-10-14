using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
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
    /// <c>Notify_ColonistDowned</c> and its place in the curve existed, and no line of <c>src/</c> raised it —
    /// so adaptation only ever eased after a death, and a colony that was mauled without losing anybody
    /// looked, to the narrator, exactly as untroubled as one that had spent the month farming.
    ///
    /// <para/><b>RimWorld's rule, which this suite now pins in full.</b> A downing is charged only when
    /// violence put the pawn down (<c>dinfo</c> present and <see cref="DamageDef.externalViolence"/>), and it
    /// is charged at the end of the tick, so a pawn downed and killed in one tick pays for the death alone.
    /// This port used to charge any downing but a life stage's, at once; a citizen who collapsed from hunger
    /// moved the curve like one cut down by a raider, and a citizen killed outright paid twice. The tests
    /// that used <c>ForceDowned</c> to stand for "a downing" now put the pawn down with a real blow, since a
    /// forced downing has no blow behind it and is (correctly) not charged.
    ///
    /// <para/>What a downing costs is RimWorld's population curve, read where it is charged. The cases here
    /// compare against that curve and against each other, never a literal.
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

        /// <summary>What a violent downing costs at the population alive now.</summary>
        private static float Downed => StoryWatcher_Adaptation.LossFromColonistViolentlyDownedByPopulation.Evaluate(
            StorytellerPawnEvents.CivilizationPopulation());

        /// <summary>What a death costs at the population left now (asked after it).</summary>
        private static float Death => StoryWatcher_Adaptation.LossFromColonistLostByPostPopulation.Evaluate(
            StorytellerPawnEvents.CivilizationPopulation());

        /// <summary>A stretch of quiet on the clock, so a penalty has something to cut into: 90 days takes
        /// several casualties before it nears the -60 floor the watcher clamps to.</summary>
        private static float BuildQuietTime()
        {
            Adaptation.Debug_OffsetAdaptDays(90f);
            return Adaptation.AdaptDays;
        }

        /// <summary>The end of the tick: the watcher charges the violent downings the tick collected.</summary>
        private static void Settle() => Adaptation.AdaptationWatcherTick(Medium);

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

        private static DamageInfo Violence(float amount = 35f) =>
            new DamageInfo(DefDatabase<DamageDef>.GetNamed("Bullet"), amount);

        /// <summary>Both legs shot off through the real damage funnel: down on the spot, by violence, with the
        /// <see cref="DamageInfo"/> that did it reaching <c>Pawn_HealthTracker.CheckForStateChange</c>.</summary>
        private static void ShootDown(Pawn pawn)
        {
            DamageDef bullet = DefDatabase<DamageDef>.GetNamed("Bullet");
            foreach (string leg in new[] { "left leg", "right leg" })
            {
                BodyPartRecord part = pawn.RaceProps.body!.GetPartByLabel(leg)!;
                bullet.Worker.Apply(new DamageInfo(bullet, 35f, hitPart: part), pawn);
            }
        }

        // ---- the headline ----

        [Fact]
        public void A_citizen_put_down_by_violence_moves_the_storytellers_adaptation()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            // Through the real funnel — the health tracker's own mobile-to-down transition with the blow that
            // caused it, not the hook by hand. Before this lane's predecessor nothing in the core raised
            // Notify_ColonistDowned at all.
            ShootDown(citizen);
            Assert.True(citizen.health.Downed);

            // Held to the end of the tick, not charged on the spot.
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
            Settle();

            Assert.True(Adaptation.AdaptDays < quiet,
                "a citizen went down and the storyteller's quiet-time clock did not move");
            Assert.Equal(quiet - Downed, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_downing_costs_the_storyteller_less_than_a_death()
        {
            // The ordering the two curves encode: losing someone outright is worse news than losing them
            // for a while. Asserted as an ordering rather than against either number.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            ShootDown(citizen);
            Settle();
            float costOfDowning = quiet - Adaptation.AdaptDays;

            float beforeDeath = Adaptation.AdaptDays;
            citizen.health.Kill(null, null);
            float costOfDeath = beforeDeath - Adaptation.AdaptDays;

            Assert.True(costOfDowning > 0f);
            Assert.True(costOfDeath > costOfDowning,
                "a downing (" + costOfDowning + " days) did not cost less than a death (" + costOfDeath + ")");
        }

        [Fact]
        public void A_downing_that_violence_did_not_cause_is_not_charged()
        {
            // RimWorld: no dinfo, or damage that is not external violence, and the downing is not an
            // adaptation event. ForceDowned stands for every collapse that has no blow behind it — blood
            // loss, hunger, disease — all of which arrive here with a null DamageInfo.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.ForceDowned = true;
            Settle();

            Assert.True(citizen.health.Downed);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_surgeons_knife_is_not_violence()
        {
            // The one damage def in the shipped content (besides the miner's pick) that is not externalViolence.
            // A downing it causes is a patient on a table, not a casualty.
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);
            float quiet = BuildQuietTime();

            Assert.False(StorytellerPawnEvents.Notify_PawnDowned(citizen, new DamageInfo(SurgeryDamageDefOf.SurgicalCut, 35f)));
            Settle();

            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        // ---- not counted twice ----

        [Fact]
        public void A_citizen_downed_and_then_dying_pays_for_each_once()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            ShootDown(citizen);
            Settle();
            float afterDowning = Adaptation.AdaptDays;
            Assert.Equal(quiet - Downed, afterDowning, 4);

            // Pawn_HealthTracker.Kill sets the dead state directly and never passes back through MakeDowned,
            // so the downing is not charged again on the way out. The death is charged, once, whatever it was:
            // this one carries no DamageInfo, like a bleed-out, and RimWorld's AdaptationEvent.Died does not
            // ask (DeathAdaptationTests).
            citizen.health.Kill(null, null);

            Assert.True(citizen.Dead);
            Assert.Equal(afterDowning - Death, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_citizen_downed_and_killed_in_the_same_tick_pays_for_the_death_alone()
        {
            // RimWorld holds a downing until the end of the tick, and a death in that tick takes it back
            // (pawnsJustDownedThisTick). One explosion that fells and kills a citizen is one casualty.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            ShootDown(citizen);
            citizen.health.Kill(null, null);
            Settle();

            Assert.True(citizen.Dead);
            Assert.Equal(quiet - Death, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Staying_down_through_further_damage_is_charged_once()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            ShootDown(citizen);
            Settle();
            float afterDowning = Adaptation.AdaptDays;

            // Every later health re-check on an already-downed pawn (a hediff tick, another hit) re-enters
            // the same state machine, with and without a blow; only the mobile-to-down transition may charge.
            for (int i = 0; i < 20; i++) citizen.health.CheckForStateChange(null, null);
            for (int i = 0; i < 5; i++) citizen.health.CheckForStateChange(Violence(), null);
            Settle();

            Assert.True(citizen.health.Downed);
            Assert.Equal(afterDowning, Adaptation.AdaptDays, 4);
            Assert.Equal(quiet - Downed, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Getting_back_up_and_going_down_again_is_a_second_event()
        {
            // The other side of the same rule, and the one that keeps the first from being a suppression:
            // being downed twice over a campaign is twice the bad news, exactly as it is in RimWorld.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            ShootDown(citizen);
            Settle();
            float afterFirst = Adaptation.AdaptDays;

            // Healed in an instant: the wounds go, the state machine is asked, the pawn is up again.
            citizen.health.hediffSet.Clear();
            citizen.health.Notify_HediffSetChanged();
            citizen.health.CheckForStateChange(null, null);
            Assert.False(citizen.health.Downed);

            ShootDown(citizen);
            Settle();

            Assert.True(citizen.health.Downed);
            Assert.True(Adaptation.AdaptDays < afterFirst, "the second downing did not cost anything");
            Assert.Equal(quiet - 2f * Downed, Adaptation.AdaptDays, 4);
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
            ShootDown(raider);
            Settle();

            Assert.True(raider.health.Downed);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Downing_an_animal_does_not_move_it()
        {
            // Humanlike only, as RimWorld's own filter is — and asserted with the animal *on the roster* and
            // the blow violent, so it is the species test doing the work and not membership or the damage.
            float quiet = BuildQuietTime();
            var husky = new Pawn(Husky, "Dog");
            CivilizationOf(husky);

            Assert.False(StorytellerPawnEvents.Notify_PawnDowned(husky, Violence()));
            Settle();

            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_pawn_belonging_to_no_civilization_at_all_does_not_move_it()
        {
            // Membership is read off the storyteller's own targets. A pawn nobody is telling a story about —
            // a wanderer, a test fixture — is not a loss the narrator should react to.
            float quiet = BuildQuietTime();
            Pawn stranger = NewHuman("Stranger");

            ShootDown(stranger);
            Settle();

            Assert.True(stranger.health.Downed);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
            Assert.False(StorytellerPawnEvents.Notify_PawnDowned(stranger, Violence()));
        }

        // ---- persistence ----

        [Fact]
        public void What_a_downing_cost_the_storyteller_survives_a_save_and_a_load()
        {
            // The hook keeps no state of its own once the tick has settled; what it moves is the adaptation
            // clock, which is saved with the storyteller. A downing the save forgets is a downing that did
            // nothing, one reload later.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);
            ShootDown(citizen);
            Settle();

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
