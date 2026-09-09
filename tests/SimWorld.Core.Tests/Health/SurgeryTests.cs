using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Surgery: a bill queued on a patient, an outcome rolled against the surgeon's competence, and an effect
    /// that is never silently nothing.
    /// </summary>
    public class SurgeryTests : ContentTestBase
    {
        public SurgeryTests(CoreContentFixture content) : base(content)
        {
        }

        private static RecipeDef Amputate => DefDatabase<RecipeDef>.GetNamed("RemoveBodyPart");

        private static RecipeDef Excise => DefDatabase<RecipeDef>.GetNamed("ExciseInfection");

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        /// <summary>A surgeon with a chosen Medicine level; skill is the only thing the outcome roll reads.</summary>
        private static Pawn Surgeon(int medicineLevel)
        {
            Pawn doctor = NewHuman("Doctor");
            doctor.skills!.GetSkill(global::SimWorld.Work.SkillDefOf.Medicine)!.Level = medicineLevel;
            return doctor;
        }

        // ---- content ----

        [Fact]
        public void Surgery_content_loads_and_carries_a_worker()
        {
            Assert.Empty(Content.Result.Errors);

            Assert.True(Amputate.isSurgery);
            Assert.IsType<Recipe_RemoveBodyPart>(Amputate.Worker);
            Assert.IsType<Recipe_RemoveHediff>(Excise.Worker);
            Assert.Equal(HediffDefOf.WoundInfection, Excise.removesHediff);

            // An ordinary crafting recipe keeps the do-nothing base worker: the seam is opt-in.
            RecipeDef cooking = DefDatabase<RecipeDef>.GetNamed("CookMealSimple");
            Assert.False(cooking.isSurgery);
            Assert.Equal(typeof(RecipeWorker), cooking.Worker.GetType());
        }

        // ---- the bill ----

        [Fact]
        public void A_surgery_bill_lives_on_the_patient_and_is_done_once()
        {
            Pawn patient = NewHuman("Patient");
            BodyPartRecord leg = Part(patient, "left leg");
            patient.health.surgeryBills.AddBill(new Bill_Medical(Amputate, leg));

            Assert.Equal(1, patient.health.surgeryBills.Count);

            // A null surgeon never fails — nobody is performing it — so this isolates "the bill is consumed".
            Assert.True(SurgeryUtility.PerformNextSurgery(patient, null));

            Assert.Equal(0, patient.health.surgeryBills.Count);
            Assert.False(SurgeryUtility.PerformNextSurgery(patient, null));
        }

        [Fact]
        public void A_surgery_bill_survives_a_save_and_still_knows_which_part()
        {
            Pawn patient = NewHuman("Patient");
            BodyPartRecord leg = Part(patient, "right leg");
            patient.health.surgeryBills.AddBill(new Bill_Medical(Amputate, leg));

            string xml = Scribe.SaveToString(patient, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(1, loaded.health.surgeryBills.Count);
            var bill = Assert.IsType<Bill_Medical>(loaded.health.surgeryBills[0]);
            Assert.Equal(Amputate, bill.recipe);

            // The part is an address in the saved data; it must come back as a part of THIS pawn's own body.
            Assert.NotNull(bill.part);
            Assert.Equal("right leg", bill.part!.Label);
            Assert.Contains(bill.part, loaded.RaceProps.body!.AllParts);
        }

        // ---- outcomes ----

        [Fact]
        public void A_successful_amputation_takes_the_part_off()
        {
            Pawn patient = NewHuman("Patient");
            BodyPartRecord leg = Part(patient, "left leg");
            Assert.False(patient.health.hediffSet.PartIsMissing(leg));

            Amputate.Worker.ApplyOnPawn(patient, leg, null, null);

            Assert.True(patient.health.hediffSet.PartIsMissing(leg));
            Assert.False(patient.Dead);
        }

        [Fact]
        public void Excising_an_infection_removes_it()
        {
            Pawn patient = NewHuman("Patient");
            BodyPartRecord torso = Part(patient, "torso");
            patient.health.AddHediff(HediffDefOf.WoundInfection, torso);
            Assert.True(patient.health.hediffSet.HasHediff(HediffDefOf.WoundInfection, torso));

            Excise.Worker.ApplyOnPawn(patient, torso, null, null);

            Assert.False(patient.health.hediffSet.HasHediff(HediffDefOf.WoundInfection, torso));
        }

        [Fact]
        public void A_better_surgeon_fails_less_often()
        {
            var worker = (Recipe_Surgery)Amputate.Worker;

            float novice = worker.SuccessChance(Surgeon(0));
            float middling = worker.SuccessChance(Surgeon(10));
            float expert = worker.SuccessChance(Surgeon(20));

            Assert.True(novice < middling, $"a novice ({novice:F2}) should do worse than a middling surgeon ({middling:F2}).");
            Assert.True(middling < expert, $"a middling surgeon ({middling:F2}) should do worse than an expert ({expert:F2}).");
            Assert.InRange(expert, 0f, 1f);
        }

        [Fact]
        public void A_harder_operation_fails_more_often_than_an_easy_one_for_the_same_surgeon()
        {
            Pawn doctor = Surgeon(10);

            float easy = ((Recipe_Surgery)Amputate.Worker).SuccessChance(doctor);
            float hard = ((Recipe_Surgery)Excise.Worker).SuccessChance(doctor);

            Assert.True(hard < easy,
                $"excising an infection ({hard:F2}) should be harder than an amputation ({easy:F2}) for the same surgeon.");
        }

        [Fact]
        public void A_botched_operation_hurts_the_patient_rather_than_doing_nothing()
        {
            // A surgeon with no skill at all, rolled enough times that the failure branch is certain to run.
            // What matters is that failure is visible: the patient is worse off than before.
            Pawn doctor = Surgeon(0);
            int botched = 0;

            for (int i = 0; i < 40; i++)
            {
                Pawn patient = NewHuman("Patient" + i);
                BodyPartRecord leg = Part(patient, "left leg");
                float healthBefore = patient.health.summaryHealth.SummaryHealthPercent;

                Amputate.Worker.ApplyOnPawn(patient, leg, doctor, null);

                bool partGone = patient.health.hediffSet.PartIsMissing(leg);
                if (partGone) continue;

                botched++;
                Assert.True(patient.Dead || patient.health.summaryHealth.SummaryHealthPercent < healthBefore,
                    "a failed operation should injure the patient, not silently do nothing.");
            }

            Assert.True(botched > 0, "an unskilled surgeon should have botched at least one of forty operations.");
        }

        [Fact]
        public void A_surgery_only_offers_parts_it_could_actually_be_done_on()
        {
            Pawn patient = NewHuman("Patient");
            var worker = (Recipe_Surgery)Amputate.Worker;

            List<BodyPartRecord> before = worker.GetPartsToApplyOn(patient).ToList();
            Assert.NotEmpty(before);
            Assert.DoesNotContain(patient.RaceProps.body!.corePart, before);

            BodyPartRecord leg = Part(patient, "left leg");
            Assert.Contains(leg, before);

            Amputate.Worker.ApplyOnPawn(patient, leg, null, null);

            // A part already gone is not offered again.
            Assert.DoesNotContain(leg, worker.GetPartsToApplyOn(patient).ToList());
        }

        [Fact]
        public void Installing_a_part_replaces_whatever_was_there()
        {
            // The install worker ships without content — a prosthetic is an item, and no such ThingDef exists
            // yet (see Recipes_Surgery.xml). The worker itself is real, so it is tested against a recipe built
            // here, the same way other modules test a worker with a throwaway def.
            var install = new RecipeDef
            {
                defName = "Test_InstallProsthetic",
                isSurgery = true,
                targetsBodyPart = true,
                workerClass = typeof(Recipe_InstallBodyPart),
                addsHediff = DefDatabase<HediffDef>.GetNamed("SimpleProstheticLeg"),
            };

            Pawn patient = NewHuman("Patient");
            BodyPartRecord leg = Part(patient, "left leg");
            Amputate.Worker.ApplyOnPawn(patient, leg, null, null);
            Assert.True(patient.health.hediffSet.PartIsMissing(leg));

            install.Worker.ApplyOnPawn(patient, leg, null, null);

            Assert.False(patient.health.hediffSet.PartIsMissing(leg));
            Assert.True(patient.health.hediffSet.HasHediff(install.addsHediff!, leg));
        }
    }
}
