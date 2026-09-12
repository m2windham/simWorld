using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>Scribe holder for round-tripping a dead pawn.</summary>
    public class DeathAttributionHolder : IExposable
    {
        public List<Pawn>? pawns;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    /// <summary>
    /// Who killed this pawn, as far as the death record can say (<see cref="Pawn_HealthTracker.KilledByAnotherPawn"/>).
    ///
    /// <para/><b>Why this is worth a test of its own.</b> <c>DeathCauseDamage</c> cannot answer it. A citizen
    /// beaten to death by another citizen and a miner crushed under the roof they just mined out from under
    /// themselves both die of <c>Blunt</c>, and <c>Blunt</c>'s own <c>deathMessage</c> reads "{0} has been
    /// beaten to death" either way. <c>docs/WORK-REGISTER.md</c> §9a read a founded settlement's entire first
    /// week of deaths as murders on exactly that evidence.
    /// </summary>
    public class DeathAttributionTests : ContentTestBase
    {
        public DeathAttributionTests(CoreContentFixture content) : base(content)
        {
        }

        private static DamageDef Blunt => DefDatabase<DamageDef>.GetNamed("Blunt");

        /// <summary>
        /// One blow to the torso, comfortably past <c>HealthTuning.LethalDamageThreshold</c> — what a
        /// collapsing roof does and what a fist cannot. The part is named rather than rolled for, because an
        /// unaimed blow lands wherever coverage sends it and most parts are survivable even at 500: destroying
        /// a lung takes the injury with it, and the pawn walks away with one lung. The torso is the core part,
        /// so it is never destroyed and the injury stays on the books.
        /// </summary>
        private static Pawn KilledBy(object? instigator)
        {
            Pawn victim = NewHuman("Victim");
            BodyPartRecord torso = victim.health.hediffSet.GetNotMissingParts().First(p => p.IsCorePart);
            Blunt.Worker.Apply(new DamageInfo(Blunt, 500f, 0f, instigator, torso), victim);
            return victim;
        }

        [Fact]
        public void A_pawn_killed_by_another_pawn_records_it()
        {
            Pawn killer = NewHuman("Killer");
            Pawn victim = KilledBy(killer);

            Assert.True(victim.Dead);
            Assert.True(victim.health.DiedViolently);
            Assert.True(victim.health.KilledByAnotherPawn);
        }

        [Fact]
        public void A_pawn_killed_by_nobody_is_not_recorded_as_killed_by_a_pawn()
        {
            // The collapsing-roof case: Building.RoofCollapseUtility applies Blunt with no instigator at all.
            Pawn victim = KilledBy(null);

            Assert.True(victim.Dead);
            Assert.True(victim.health.DiedViolently, "roof-collapse damage is still violence; it just has nobody behind it");
            Assert.False(victim.health.KilledByAnotherPawn);
        }

        /// <summary>
        /// The case that used to lose the answer altogether. <c>Hediff_MissingPart.PostAdd</c> ran
        /// <c>Pawn_HealthTracker.RestorePart</c> without RimWorld's <c>checkStateChange: false</c>, so a pawn
        /// killed by having a vital organ destroyed died from inside a call that had no
        /// <see cref="DamageInfo"/> to hand — the death record did not know what had killed them, and both
        /// <c>DiedViolently</c> and <c>KilledByAnotherPawn</c> read false for a murder.
        /// </summary>
        [Fact]
        public void A_killing_blow_that_destroys_a_body_part_still_records_what_killed_them()
        {
            Pawn killer = NewHuman("Killer");
            Pawn victim = NewHuman("Victim");
            BodyPartRecord brain = victim.health.hediffSet.GetBrain()!;

            Blunt.Worker.Apply(new DamageInfo(Blunt, 500f, 0f, killer, brain), victim);

            Assert.True(victim.Dead);
            Assert.Contains(victim.health.hediffSet.hediffs,
                h => h.def == DefDatabase<HediffDef>.GetNamed("MissingBodyPart"));
            Assert.Same(Blunt, victim.health.DeathCauseDamage);
            Assert.True(victim.health.DiedViolently);
            Assert.True(victim.health.KilledByAnotherPawn);
        }

        [Fact]
        public void A_living_pawn_is_not_recorded_as_killed_by_anyone()
        {
            Pawn p = NewHuman();
            Assert.False(p.health.KilledByAnotherPawn);
        }

        [Fact]
        public void Scribe_round_trips_the_killer_flag_with_the_rest_of_the_death_record()
        {
            Pawn killer = NewHuman("Killer");
            Pawn victim = KilledBy(killer);
            Assert.True(victim.health.KilledByAnotherPawn);

            var holder = new DeathAttributionHolder { pawns = new List<Pawn> { victim } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            DeathAttributionHolder loaded = Scribe.Load<DeathAttributionHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.True(loaded.pawns![0].health.KilledByAnotherPawn);
            Assert.Same(Blunt, loaded.pawns[0].health.DeathCauseDamage);
        }
    }
}
