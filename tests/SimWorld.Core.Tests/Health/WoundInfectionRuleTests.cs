using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Tests.Pawns;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Whether a wound goes septic, and how well a tend works against the infection that follows (lane:
    /// post-raid bleed). Measured on seed 12345 after the injuries from animals and brawls were gone: twenty
    /// five people, plenty of medicine, and eight of them still died of wound infection in 60 days, each a
    /// close finish — immunity at 0.76 to 1.00 when the infection won. Two rules were off, and both only ever
    /// in the infection's favour:
    /// <list type="bullet">
    /// <item>The infecter ignored the wound's size. RimWorld scales the chance by 0.1 at severity 1 up to 1.0
    /// at severity 12, and by whether the pawn is already fighting one.</item>
    /// <item>A tend's +/-0.25 quality variance was clipped to its downside: the medicine's quality ceiling was
    /// given the tend's own base quality, so a tend could roll below its base and never above it.</item>
    /// </list>
    /// The factors asserted are the 1.0 decompile's (<c>HediffComp_Infecter</c>, <c>ImmunityHandler</c>); the
    /// rest is behaviour: a ceiling, a mean, an ordering.
    /// </summary>
    public class WoundInfectionRuleTests : ContentTestBase
    {
        public WoundInfectionRuleTests(CoreContentFixture content) : base(content)
        {
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void Cut(Pawn p, string part, float amount)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            cut.Worker.Apply(new DamageInfo(cut, amount, hitPart: Part(p, part)), p);
        }

        /// <summary>
        /// One cut of the given severity on the core part of each of <paramref name="pawns"/> pawns of
        /// <paramref name="race"/>, counting how many turn septic. Only the cut is ticked, and only while it is
        /// still waiting to roll: a wound that will never infect has nothing to wait out.
        /// </summary>
        private static int CountInfected(ThingDef race, float severity, int pawns, int firstSeed)
        {
            int infected = 0;
            for (int n = 0; n < pawns; n++)
            {
                var p = new Pawn(race, "P" + n);
                Rand.Current = new RandomStream(firstSeed + n);
                DamageDef cutDef = DefDatabase<DamageDef>.GetNamed("Cut");
                cutDef.Worker.Apply(new DamageInfo(cutDef, severity, hitPart: p.RaceProps.body!.corePart), p);
                Hediff cut = p.health.hediffSet.hediffs.Single(h => h.def.defName == "Cut");
                var comp = cut.TryGetComp<HediffComp_Infecter>()!;
                for (int t = 0; t < HealthTuning.InfectionDelayRange.max + 1 && comp.TicksUntilInfect > 0; t++) cut.Tick();
                infected += p.health.hediffSet.hediffs.Count(h => h.def == HediffDefOf.WoundInfection);
            }
            return infected;
        }

        // ---- the wound's own size ----

        [Fact]
        public void The_severity_factor_is_RimWorlds_curve()
        {
            // 1.0 decompile: InfectionChanceFactorFromSeverityCurve (1 -> 0.1, 12 -> 1.0), clamped at its ends.
            Assert.Equal(0.1f, HediffComp_Infecter.SeverityFactor(1f), 5);
            Assert.Equal(1f, HediffComp_Infecter.SeverityFactor(12f), 5);
            Assert.Equal(0.1f, HediffComp_Infecter.SeverityFactor(0.2f), 5);
            Assert.Equal(1f, HediffComp_Infecter.SeverityFactor(40f), 5);
            Assert.True(HediffComp_Infecter.SeverityFactor(4f) > HediffComp_Infecter.SeverityFactor(2f));
        }

        [Fact]
        public void A_small_wound_is_far_less_likely_to_go_septic_than_a_ruinous_one()
        {
            // Same def, same part, same chance in the def: only the size differs. Before, a scratch of
            // severity 2 was exactly as likely to infect as a wound of 12.
            const int pawns = 300;
            int big = CountInfected(Human, severity: 12f, pawns, firstSeed: 1000);
            int small = CountInfected(Human, severity: 2f, pawns, firstSeed: 1000);

            Assert.True(big > 20, "enough ruinous wounds go septic to say anything: " + big);
            Assert.True(small * 3 < big, "small " + small + " vs ruinous " + big);
        }

        [Fact]
        public void An_animals_wounds_are_a_tenth_as_likely_to_be_candidates_for_infection()
        {
            // HealthTuning.AnimalsInfectionChanceFactor, 0.1 in the decompile.
            const int pawns = 300;
            int human = CountInfected(Human, severity: 12f, pawns, firstSeed: 5000);
            int husky = CountInfected(Husky, severity: 12f, pawns, firstSeed: 5000);

            Assert.True(human > 20);
            Assert.True(husky * 4 < human, "husky " + husky + " vs human " + human);
        }

        // ---- a pawn already fighting one ----

        [Fact]
        public void A_pawn_who_has_the_infection_on_that_part_or_has_built_the_immunity_is_not_handed_another()
        {
            Pawn p = NewHuman("P");
            BodyPartRecord torso = Part(p, "torso");
            BodyPartRecord leg = Part(p, "left leg");

            Assert.Equal(1f, p.health.immunity.DiseaseContractChanceFactor(HediffDefOf.WoundInfection, torso));

            // Already infected on the torso: no second one there, but the leg is still open.
            p.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.WoundInfection, p, torso), torso);
            p.health.immunity.EnsureRecord(HediffDefOf.WoundInfection).immunity = 0f;
            Assert.Equal(0f, p.health.immunity.DiseaseContractChanceFactor(HediffDefOf.WoundInfection, torso));
            Assert.Equal(1f, p.health.immunity.DiseaseContractChanceFactor(HediffDefOf.WoundInfection, leg));

            // Immunity builds while it is fought: half way to 0.6 halves the chance, and 0.6 closes it.
            p.health.immunity.GetImmunityRecord(HediffDefOf.WoundInfection)!.immunity = 0.3f;
            Assert.Equal(0.5f, p.health.immunity.DiseaseContractChanceFactor(HediffDefOf.WoundInfection, leg), 4);
            p.health.immunity.GetImmunityRecord(HediffDefOf.WoundInfection)!.immunity = 0.6f;
            Assert.True(p.health.immunity.DiseaseContractChanceFactor(HediffDefOf.WoundInfection, leg) <= 0.001f);
        }

        [Fact]
        public void A_wound_that_comes_due_while_the_pawn_is_immune_does_not_infect()
        {
            int infected = 0;
            for (int n = 0; n < 200; n++)
            {
                Pawn p = NewHuman("P" + n);
                Rand.Current = new RandomStream(9000 + n);
                p.health.immunity.EnsureRecord(HediffDefOf.WoundInfection).immunity = 0.9f;
                Cut(p, "torso", 12f);
                Hediff cut = p.health.hediffSet.hediffs.Single(h => h.def.defName == "Cut");
                var comp = cut.TryGetComp<HediffComp_Infecter>()!;
                for (int t = 0; t < HealthTuning.InfectionDelayRange.max + 1 && comp.TicksUntilInfect > 0; t++) cut.Tick();
                infected += p.health.hediffSet.hediffs.Count(h => h.def == HediffDefOf.WoundInfection);
            }
            Assert.Equal(0, infected);
        }

        // ---- the tend ----

        private static float TendQualityOf(Pawn doctor, Thing? medicine, int seed)
        {
            Pawn patient = NewHuman("Patient");
            Rand.Current = new RandomStream(seed);
            Cut(patient, "torso", 6f);
            Hediff cut = patient.health.hediffSet.hediffs.Single(h => h.def.defName == "Cut");
            TendUtility.DoTendWithMedicine(doctor, patient, medicine);
            return cut.TendQuality;
        }

        private static Pawn DoctorOfSkill(int level)
        {
            Pawn doctor = NewHuman("Doctor");
            doctor.skills.GetSkill(SkillDefOf.Medicine)!.Level = level;
            return doctor;
        }

        [Fact]
        public void A_tend_with_no_medicine_averages_its_base_quality_and_is_not_clipped_to_the_downside()
        {
            // The +/-0.25 variance is symmetric, and the floor at 0 only raises the mean of a poor tend. The
            // port capped the result at the tend's own base quality, so every tend that rolled high was cut
            // back to the base: a skill-5 doctor with nothing in hand (base about 0.14) averaged about 0.09.
            Pawn doctor = DoctorOfSkill(5);
            float baseQuality = TendUtility.CalculateBaseTendQuality(doctor, null, null);
            Assert.InRange(baseQuality, 0.05f, 0.4f);

            const int tends = 400;
            float total = 0f;
            float highest = 0f;
            for (int i = 0; i < tends; i++)
            {
                float q = TendQualityOf(doctor, medicine: null, seed: 100 + i);
                total += q;
                highest = System.Math.Max(highest, q);
            }
            float mean = total / tends;

            Assert.True(mean >= baseQuality * 0.95f, "mean tend quality " + mean + " is below the base " + baseQuality);
            Assert.True(highest > baseQuality + 0.1f, "the variance never carried a tend above its base: highest " + highest);
        }

        [Fact]
        public void A_medicines_quality_ceiling_is_the_medicines_own_not_the_tends_base()
        {
            // A top doctor with herbal medicine: base 0.6, ceiling 0.7. The variance may carry it up to the
            // ceiling and not past it.
            Pawn doctor = DoctorOfSkill(20);
            var herbal = Def("MedicineHerbal");
            float ceiling = TendUtility.MaxQualityFor(herbal);
            float baseQuality = TendUtility.CalculateBaseTendQuality(doctor, null, herbal);
            Assert.True(baseQuality < ceiling);

            float highest = 0f;
            for (int i = 0; i < 300; i++)
            {
                Thing medicine = ThingMaker.MakeThing(herbal);
                highest = System.Math.Max(highest, TendQualityOf(doctor, medicine, seed: 700 + i));
            }

            Assert.True(highest > baseQuality + 0.02f, "never above the base: " + highest + " vs " + baseQuality);
            Assert.True(highest <= ceiling + 1e-4f, "past the medicine's ceiling: " + highest + " vs " + ceiling);
        }

        [Fact]
        public void Industrial_medicine_in_a_good_doctors_hands_can_tend_past_what_herbal_ever_does()
        {
            Pawn doctor = DoctorOfSkill(20);
            float herbalCeiling = TendUtility.MaxQualityFor(Def("MedicineHerbal"));
            float highest = 0f;
            for (int i = 0; i < 300; i++)
            {
                Thing medicine = ThingMaker.MakeThing(Def("MedicineIndustrial"));
                highest = System.Math.Max(highest, TendQualityOf(doctor, medicine, seed: 1500 + i));
            }
            Assert.True(highest > herbalCeiling, "highest " + highest);
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        // ---- save and load ----

        [Fact]
        public void A_wounds_infection_state_round_trips_through_Scribe()
        {
            // A wound that was never going to infect (-2) must still be that after a load, and a pending one
            // must keep counting down: neither may fall back to "uninitialised".
            Pawn never = NewHuman("Never");
            Pawn pending = NewHuman("Pending");
            Cut(never, "torso", 6f);
            Cut(pending, "torso", 6f);
            Hediff neverCut = never.health.hediffSet.hediffs.Single(h => h.def.defName == "Cut");
            Hediff pendingCut = pending.health.hediffSet.hediffs.Single(h => h.def.defName == "Cut");
            var neverComp = neverCut.TryGetComp<HediffComp_Infecter>()!;
            var pendingComp = pendingCut.TryGetComp<HediffComp_Infecter>()!;

            // Drive both states explicitly rather than depend on a roll.
            for (int t = 0; t < HealthTuning.InfectionDelayRange.max + 1 && neverComp.TicksUntilInfect > 0; t++) neverCut.Tick();
            int tick = 0;
            while (pendingComp.TicksUntilInfect <= 0 && tick < 200)
            {
                Cut(pending, "left leg", 6f);
                pendingCut = pending.health.hediffSet.hediffs.Where(h => h.def.defName == "Cut").Last();
                pendingComp = pendingCut.TryGetComp<HediffComp_Infecter>()!;
                tick++;
            }
            Assert.True(pendingComp.TicksUntilInfect > 0, "found a wound that will roll for infection");
            for (int t = 0; t < 100; t++) pendingCut.Tick();
            int remaining = pendingComp.TicksUntilInfect;
            int neverState = neverComp.TicksUntilInfect;

            var holder = new PawnHolder { pawns = new List<Pawn> { never, pending } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var loadedNever = loaded.pawns![0].health.hediffSet.hediffs.Single(h => h.def.defName == "Cut").TryGetComp<HediffComp_Infecter>()!;
            Assert.Equal(neverState, loadedNever.TicksUntilInfect);

            var loadedPending = loaded.pawns[1].health.hediffSet.hediffs.Where(h => h.def.defName == "Cut")
                .Select(h => h.TryGetComp<HediffComp_Infecter>()!).Single(c => c.TicksUntilInfect == remaining);
            Assert.True(loadedPending.TicksUntilInfect > 0);
        }
    }
}
