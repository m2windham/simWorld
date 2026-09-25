using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The storyteller notices the dead, including the ones who die of it later.
    ///
    /// <para/><b>The defect.</b> <see cref="StoryWatcher_Adaptation"/> is RimWorld's recovery window: casualties
    /// spend adapt-days, the threat-points factor falls, and the next threat lands lighter. This port charged a
    /// death only when the <see cref="DamageInfo"/> that ended it declared
    /// <see cref="DamageDef.externalViolence"/>. Most people a raid kills are not killed by the blow. They are
    /// downed and bleed out, or die of the wound's infection days later, and both of those reach
    /// <see cref="Pawn_HealthTracker.Kill"/> with <c>dinfo: null</c>. So the storyteller did not see them, and in
    /// the populated runs adaptation <i>rose</i> on the day six people bled out
    /// (<c>docs/perf/storyteller-populated</c>).
    ///
    /// <para/><b>RimWorld's rule, which these tests pin.</b> <c>Pawn.Kill</c> raises
    /// <c>AdaptationEvent.Died</c> without a <c>DamageInfo</c>, and <c>StoryWatcher_Adaptation.Notify_PawnEvent</c>
    /// charges it for any humanlike colonist who is not a prisoner. The violence test there belongs to the
    /// downing branch only (1.0 and 1.6 decompiles; see <see cref="StorytellerDeathEvents"/>'s doc). So a death
    /// is charged by <i>whose</i> it was, never by <i>what</i> it was. The wound's provenance
    /// (<see cref="WoundProvenance"/>) is not what decides it. That stamp answers a different question, on the
    /// death ledger's by-source axis.
    ///
    /// <para/>The assertions compare against the watcher's own named penalties or against each other, never a
    /// literal. Those constants are documented approximations of RimWorld's population curves, and this suite
    /// is about which deaths reach adaptation, not how much each one costs.
    /// </summary>
    [Collection("GlobalDefs")]
    public class DeathAdaptationTests : ContentTestBase
    {
        public DeathAdaptationTests(CoreContentFixture content) : base(content)
        {
            Ablation.Clear();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.FamilyManager = new FamilyManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
        }

        private const string Raid = "RaidEnemy";

        /// <summary>Generous cap on how long a bleed-out is allowed to take; a run that reaches it has failed,
        /// not hung.</summary>
        private const int DeathDeadlineTicks = 3 * GenDate.TicksPerDay;

        private const int InfectionCohortSize = 40;

        private static StoryWatcher_Adaptation Adaptation => Find.Storyteller.adaptation;

        private static float Death => StoryWatcher_Adaptation.DeathAdaptDaysPenalty;

        private static float Downed => StoryWatcher_Adaptation.DownedAdaptDaysPenalty;

        private static DifficultyDef Medium => DefDatabase<DifficultyDef>.GetNamed("Medium");

        // ---- fixtures ----

        /// <summary>
        /// Quiet time on the clock, so a charge has something to cut into and cannot hide behind the zero floor
        /// the watcher clamps to. A test ticks pawns through a bare <see cref="TickManager"/>, which never runs
        /// the storyteller's interval, so nothing else moves adaptation between here and the assertion.
        /// </summary>
        private static float BuildQuietTime()
        {
            for (int i = 0; i < 4000; i++) Adaptation.AdaptationTick(Medium);
            Assert.True(Adaptation.AdaptDays > 10f * (Death + Downed),
                "needed a peaceful stretch on the clock before a casualty could be seen to cut it short");
            return Adaptation.AdaptDays;
        }

        /// <summary>A civilization registered with the storyteller holding these pawns: the roster that decides
        /// whose death is ours (<see cref="StorytellerPawnEvents.IsCivilizationMember"/>).</summary>
        private static CivilizationTarget CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
            return target;
        }

        private static Faction Raiders() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough", "F_Rough");

        /// <summary>A raider from the real raid incident, fired at a bare target so it lands nowhere, and
        /// stamped with the incident that sent it.</summary>
        private static Pawn Raider()
        {
            var worker = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), points = 300f, faction = Raiders() }));
            Pawn raider = worker.LastRaidPawns!.First();
            Assert.Equal(Raid, raider.spawnedByIncident);
            return raider;
        }

        private static BodyPartRecord Part(Pawn p, string label) =>
            p.RaceProps.body!.GetPartByLabel(label) ?? throw new InvalidOperationException("no part " + label);

        /// <summary>One blow through the real damage funnel, dealt by <paramref name="by"/>.</summary>
        private static void Hit(Pawn victim, Pawn? by, string damage, float amount, string part)
        {
            DamageDef def = DefDatabase<DamageDef>.GetNamed(damage);
            def.Worker.Apply(new DamageInfo(def, amount, 0f, by, Part(victim, part)), victim);
        }

        /// <summary>Both legs off: down on the spot, with two fresh stumps that bleed and never close on their
        /// own, so an untended victim dies of blood loss hours later.</summary>
        private static void DownWithBleedingStumps(Pawn victim, Pawn? by)
        {
            Hit(victim, by, "Bullet", 35f, "left leg");
            Hit(victim, by, "Bullet", 35f, "right leg");
        }

        private static void RunUntilDead(Pawn pawn)
        {
            for (int elapsed = 0; elapsed < DeathDeadlineTicks && !pawn.Dead; elapsed += 250) RunTicks(250, pawn);
            Assert.True(pawn.Dead, pawn.Label + " was still alive " + DeathDeadlineTicks + " ticks on: " + string.Join(", ", pawn.health.hediffSet.hediffs));
        }

        /// <summary>
        /// The first of a cohort, each cut once by <paramref name="by"/> and left untended, whose wound turns
        /// septic through <see cref="HediffComp_Infecter"/>'s real roll. Forty because a cut is infected 15% of
        /// the time: forty clean cuts in a row is roughly a one-in-670 run.
        /// </summary>
        private static Pawn FirstInfected(Pawn by)
        {
            var cohort = new List<Pawn>();
            for (int i = 0; i < InfectionCohortSize; i++)
            {
                Pawn p = NewHuman("Cut" + i);
                Hit(p, by, "Cut", 10f, "torso");
                cohort.Add(p);
            }

            Pawn[] all = cohort.ToArray();
            for (int elapsed = 0; elapsed <= HealthTuning.InfectionDelayRange.max; elapsed += 1000)
            {
                Pawn? infected = cohort.FirstOrDefault(p => p.health.hediffSet.HasHediff(HediffDefOf.WoundInfection));
                if (infected != null) return infected;
                RunTicks(1000, all);
            }
            throw new InvalidOperationException("no wound in a cohort of " + cohort.Count + " became infected");
        }

        // ---- killed on the spot: this already worked, and has to keep working ----

        [Fact]
        public void A_citizen_killed_outright_by_a_raider_costs_the_storyteller_a_death()
        {
            float quiet = BuildQuietTime();
            Pawn victim = NewHuman("Victim");
            CivilizationOf(victim);

            Hit(victim, Raider(), "Bullet", 200f, "torso");

            Assert.True(victim.Dead);
            Assert.True(victim.health.DiedViolently);
            Assert.Equal(quiet - Death, Adaptation.AdaptDays, 4);
        }

        // ---- killed later: the defect ----

        /// <summary>
        /// The death the populated runs were full of. Downed by a raider, left untended, dead of blood loss
        /// hours later, with no blow at the moment of death and the raider already gone. The downing is charged
        /// when it happens and the death when it happens, as RimWorld charges them.
        /// </summary>
        [Fact]
        public void A_citizen_downed_in_a_raid_who_bleeds_out_later_costs_it_a_death()
        {
            float quiet = BuildQuietTime();
            Pawn victim = NewHuman("Victim");
            CivilizationOf(victim);
            Pawn raider = Raider();

            DownWithBleedingStumps(victim, raider);
            Assert.True(victim.Downed, "two legs off should down them on the spot");
            Assert.False(victim.Dead, "the blows themselves must not kill, or this is the other test");
            float afterDowning = Adaptation.AdaptDays;
            Assert.Equal(quiet - Downed, afterDowning, 4);

            raider.health.Kill(null, null);   // long gone before the citizen dies, and not ours

            RunUntilDead(victim);

            Assert.Null(victim.health.DeathCauseDamage);
            Assert.Same(HediffDefOf.BloodLoss, victim.health.DeathCauseHediff);
            Assert.False(victim.health.DiedViolently);
            Assert.Equal(afterDowning - Death, Adaptation.AdaptDays, 4);
        }

        /// <summary>The same for the wound that turns septic and kills days later: an infection is a disease
        /// death on the ledger's cause axis, and still a death of ours.</summary>
        [Fact]
        public void A_citizen_who_dies_of_an_infected_raid_wound_costs_it_a_death()
        {
            Pawn sick = FirstInfected(Raider());
            CivilizationOf(sick);
            float quiet = BuildQuietTime();

            Hediff infection = sick.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.WoundInfection)!;
            infection.Severity = infection.def.lethalSeverity;

            Assert.True(sick.Dead);
            Assert.Equal(Raid, infection.sourceIncident);
            Assert.Null(sick.health.DeathCauseDamage);
            Assert.Same(HediffDefOf.WoundInfection, sick.health.DeathCauseHediff);
            Assert.Equal(DeathCause.Disease, sick.health.CauseOfDeath);
            Assert.Equal(quiet - Death, Adaptation.AdaptDays, 4);
        }

        // ---- no violent root: RimWorld's rule ----

        /// <summary>
        /// RimWorld's <c>AdaptationEvent.Died</c> does not ask what killed the colonist, so old age, a plague
        /// that arrived as its own incident, and starvation each cost exactly what a killing costs. Asserted as
        /// that equality, which is the rule, rather than as a number.
        /// </summary>
        [Theory]
        [InlineData("age")]
        [InlineData("plague")]
        [InlineData("starvation")]
        public void A_death_with_no_violent_root_costs_what_a_killing_costs(string cause)
        {
            BuildQuietTime();

            Pawn shot = NewHuman("Shot");
            CivilizationOf(shot);
            float before = Adaptation.AdaptDays;
            Hit(shot, Raider(), "Bullet", 200f, "torso");
            Assert.True(shot.Dead);
            float costOfAKilling = before - Adaptation.AdaptDays;

            Pawn died = cause == "age"
                ? PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f))
                : NewHuman("Died");
            CivilizationTarget target = CivilizationOf(died);
            before = Adaptation.AdaptDays;

            switch (cause)
            {
                case "age":
                    died.ageTracker.DebugSetAge(500f);
                    Assert.True(died.ageTracker.ShouldDieOfAge());
                    Find.FamilyManager.ProcessDeathsFromAge(new List<Pawn> { died });
                    Assert.Equal(DeathCause.Age, died.health.CauseOfDeath);
                    break;

                case "plague":
                    Assert.True(DefDatabase<IncidentDef>.GetNamed("Disease_Plague").Worker.TryExecute(
                        new IncidentParms { target = target, points = 40f }));
                    Hediff plague = died.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("Plague"))!;
                    Assert.Equal("Disease_Plague", plague.sourceIncident);   // its own incident, no wound behind it
                    plague.Severity = plague.def.lethalSeverity;
                    Assert.Equal(DeathCause.Disease, died.health.CauseOfDeath);
                    break;

                case "starvation":
                    HediffDef malnutrition = DefDatabase<HediffDef>.GetNamed("Malnutrition");
                    died.health.AddHediff(malnutrition).Severity = malnutrition.lethalSeverity;
                    Assert.Equal(DeathCause.Starvation, died.health.CauseOfDeath);
                    break;
            }

            Assert.True(died.Dead);
            Assert.False(died.health.DiedViolently);
            Assert.True(costOfAKilling > 0f);
            Assert.Equal(costOfAKilling, before - Adaptation.AdaptDays, 4);
        }

        // ---- only ours ----

        /// <summary>The guard the wider rule has to keep: the death funnel runs for raiders too, and one who
        /// bleeds out after the colony shot his legs off must not ease the storyteller.</summary>
        [Fact]
        public void A_raider_who_bleeds_out_costs_the_storyteller_nothing()
        {
            float quiet = BuildQuietTime();
            Pawn defender = NewHuman("Defender");
            CivilizationOf(defender);
            Pawn raider = Raider();

            DownWithBleedingStumps(raider, defender);
            RunUntilDead(raider);

            Assert.Same(HediffDefOf.BloodLoss, raider.health.DeathCauseHediff);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        /// <summary>RimWorld's watcher returns early for a pawn that is not humanlike, and so does this one.</summary>
        [Fact]
        public void An_animal_on_the_roster_is_not_a_colonist()
        {
            float quiet = BuildQuietTime();
            var dog = new Pawn(Husky, "Dog");
            CivilizationOf(dog);

            dog.health.Kill(null, null);

            Assert.True(dog.Dead);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        // ---- the unwatched raid: once, not twice ----

        /// <summary>
        /// A raid settled abstractly kills through <c>FamilyManager.HandleDeath</c>, which reaches
        /// <see cref="Pawn_HealthTracker.Kill"/> like any other death. <see cref="SettlementRaidResolver"/> used
        /// to charge those deaths by hand as well, because the funnel then turned them away as non-violent.
        /// With both in place every one would be charged twice.
        /// </summary>
        [Fact]
        public void An_unwatched_raid_charges_each_citizen_it_kills_exactly_once()
        {
            var worker = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
            int runsThatKilled = 0;

            for (int run = 0; run < 12; run++)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();
                float quiet = BuildQuietTime();

                var settlement = new Settlement(WorldObjectDefOf.Settlement, run, null, "Raided" + run, 0);
                for (int i = 0; i < 6; i++) settlement.AddCitizen(NewHuman("Villager" + run + "_" + i));
                var target = new CivilizationTarget(Find.Storyteller);
                target.SetSettlements(new[] { settlement });

                Assert.True(worker.TryExecute(new IncidentParms { target = target, points = 800f, faction = Raiders() }));
                int killed = worker.LastRaidOutcome!.Value.CitizensKilled;

                Assert.Equal(quiet - killed * Death, Adaptation.AdaptDays, 4);
                if (killed > 0) runsThatKilled++;
            }

            Assert.True(runsThatKilled > 0, "no raid in the sweep killed anybody, so the path is untested");
        }

        // ---- a save in the middle of it ----

        /// <summary>
        /// Saved while bleeding, loaded, and dead after: the charge needs nothing a save could drop. It asks
        /// only whether the dead pawn was on the roster. The watcher's own state then survives a round trip
        /// with the charge in it.
        /// </summary>
        [Fact]
        public void A_citizen_who_bleeds_out_after_a_reload_still_costs_a_death_and_the_charge_is_saved()
        {
            Pawn victim = NewHuman("Victim");
            DownWithBleedingStumps(victim, Raider());
            WoundProvenance.AccrueBloodLoss(victim, 0.1f);

            string xml = Scribe.SaveToString(victim, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            Assert.True(loaded.Downed);

            CivilizationOf(loaded);
            float quiet = BuildQuietTime();
            RunUntilDead(loaded);

            Assert.Same(HediffDefOf.BloodLoss, loaded.health.DeathCauseHediff);
            Assert.Equal(quiet - Death, Adaptation.AdaptDays, 4);

            string watcherXml = Scribe.SaveToString(Adaptation, "adaptation");
            StoryWatcher_Adaptation reloaded = Scribe.Load<StoryWatcher_Adaptation>(watcherXml, "adaptation");
            Assert.Equal(Adaptation.AdaptDays, reloaded.AdaptDays, 4);
            Assert.Equal(Adaptation.TotalThreatPointsFactor(Medium), reloaded.TotalThreatPointsFactor(Medium), 4);
        }
    }
}
