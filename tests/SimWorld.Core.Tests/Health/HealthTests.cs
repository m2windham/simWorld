using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Tests.Pawns;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Health
{
    public class HealthTests : ContentTestBase
    {
        public HealthTests(CoreContentFixture content) : base(content)
        {
        }

        private static BodyPartRecord Part(Pawn p, string label)
        {
            return p.RaceProps.body!.GetPartByLabel(label) ?? throw new InvalidOperationException("no part " + label);
        }

        private static DamageResult Hit(Pawn p, string damage, float amount, string? partLabel = null)
        {
            DamageDef def = DefDatabase<DamageDef>.GetNamed(damage);
            BodyPartRecord? part = partLabel == null ? null : Part(p, partLabel);
            return def.Worker.Apply(new DamageInfo(def, amount, hitPart: part), p);
        }

        private static HediffDef HediffNamed(string name) => DefDatabase<HediffDef>.GetNamed(name);

        private static float Level(Pawn p, string capacity) => p.health.capacities.GetLevel(DefDatabase<PawnCapacityDef>.GetNamed(capacity));

        /// <summary>Runs days of ticks while keeping food and rest topped up so only health drives the outcome.</summary>
        private static void RunDays(float days, Action? eachHour = null, params Pawn[] pawns)
        {
            int hours = (int)Math.Round(days * GenDate.HoursPerDay);
            for (int h = 0; h < hours; h++)
            {
                RunTicks(GenDate.TicksPerHour, pawns);
                foreach (Pawn p in pawns)
                {
                    if (p.Dead) continue;
                    if (p.needs.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel;
                    if (p.needs.rest != null) p.needs.rest.CurLevel = 1f;
                }
                eachHour?.Invoke();
            }
        }

        // ---- content and body ----

        [Fact]
        public void Health_content_is_present()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(2, DefDatabase<BodyDef>.DefCount);
            Assert.Equal(11, DefDatabase<PawnCapacityDef>.DefCount);
            Assert.Equal(23, DefDatabase<BodyPartTagDef>.DefCount);
            // Cut/Stab/Blunt/Bullet/Bite/Burn, plus Flame (system: fire — Damages_Fire.xml) and SurgicalCut
            // (Damages_Surgery.xml — the one shipped damage that is not violence).
            Assert.Equal(8, DefDatabase<DamageDef>.DefCount);
            Assert.True(DefDatabase<HediffDef>.DefCount >= 16);
            Assert.NotNull(HediffDefOf.MissingBodyPart);
            Assert.NotNull(PawnCapacityDefOf.Moving);
            Assert.NotNull(BodyPartTagDefOf.ConsciousnessSource);
            Assert.NotNull(DamageDefOf.Cut);
            Assert.Same(DefDatabase<BodyDef>.GetNamed("Human"), Human.race!.body);
        }

        [Fact]
        public void Body_coverage_resolves_to_a_tree_that_sums_to_one()
        {
            foreach (BodyDef body in DefDatabase<BodyDef>.AllDefsListForReading)
            {
                Assert.NotNull(body.corePart);
                Assert.Same(body.corePart, body.AllParts[0]);
                Assert.Equal(0, body.corePart!.Index);
                float sum = body.AllParts.Sum(p => p.coverageAbs);
                Assert.InRange(sum, 0.999f, 1.001f);
                foreach (BodyPartRecord part in body.AllParts)
                {
                    Assert.Same(body, part.body);
                    Assert.True(part.coverageAbs >= 0f);
                    foreach (BodyPartRecord child in part.parts) Assert.Same(part, child.parent);
                }
            }
            BodyDef human = Human.race!.body!;
            Assert.Equal(0.15f, human.corePart!.coverageAbs, 3);
            Assert.True(human.GetPartByLabel("left leg")!.IsAncestorOf(human.GetPartByLabel("left big toe")!));
            Assert.Equal(2, human.GetPartsWithTag(BodyPartTagDefOf.MovingLimbCore).Count);
            Assert.Single(human.GetPartsWithTag(BodyPartTagDefOf.ConsciousnessSource));
        }

        [Fact]
        public void Healthy_pawn_has_full_capacities_and_full_health()
        {
            Pawn p = NewHuman();
            foreach (PawnCapacityDef c in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
            {
                Assert.Equal(1f, p.health.capacities.GetLevel(c));
            }
            Assert.Equal(1f, p.health.summaryHealth.SummaryHealthPercent);
            Assert.Equal(PawnHealthState.Mobile, p.health.State);
            Assert.Equal(0f, p.health.hediffSet.PainTotal);
            Assert.Equal(30f, p.health.hediffSet.GetPartHealth(Part(p, "left leg")));
        }

        [Fact]
        public void Animal_body_lacks_hands_and_speech()
        {
            var dog = new Pawn(Husky, "Rex");
            Assert.False(dog.Dead);
            Assert.Equal(1f, Level(dog, "Moving"));
            Assert.Equal(1f, Level(dog, "Consciousness"));
            Assert.Equal(0f, Level(dog, "Manipulation"));
            Assert.Equal(0f, Level(dog, "Talking"));
        }

        // ---- injuries and capacities ----

        [Fact]
        public void Injury_lowers_part_health_efficiency_and_moving()
        {
            Pawn p = NewHuman();
            DamageResult result = Hit(p, "Cut", 15f, "left leg");
            Assert.True(result.wounded);
            Assert.Single(result.hediffs);
            Assert.Equal(15f, p.health.hediffSet.GetPartHealth(Part(p, "left leg")));
            Assert.Equal(0.5f, PawnCapacityUtility.CalculatePartEfficiency(p.health.hediffSet, Part(p, "left leg")), 3);
            float moving = Level(p, "Moving");
            Assert.InRange(moving, 0.5f, 0.99f);
            Assert.Equal(0.93f, p.health.summaryHealth.SummaryHealthPercent, 3);
            Assert.False(p.Downed);
        }

        [Fact]
        public void Damage_picks_wound_type_by_part_surface()
        {
            Pawn p = NewHuman();
            Assert.Equal("Bruise", Hit(p, "Blunt", 3f, "left arm").hediffs[0].def.defName);
            Assert.Equal("Crack", Hit(p, "Blunt", 3f, "left femur").hediffs[0].def.defName);
            Assert.Equal("Crush", Hit(p, "Blunt", 3f, "stomach").hediffs[0].def.defName);
            Assert.Equal("Cut", Hit(p, "Cut", 3f, "left arm").hediffs[0].def.defName);
        }

        [Fact]
        public void Random_hits_land_by_absolute_coverage()
        {
            Pawn p = NewHuman();
            var rand = new RandomStream(7);
            var counts = new Dictionary<BodyPartRecord, int>();
            for (int i = 0; i < 3000; i++)
            {
                BodyPartRecord part = p.health.hediffSet.GetRandomNotMissingPart(null, BodyPartHeight.Undefined, BodyPartDepth.Undefined, rand)!;
                counts[part] = counts.TryGetValue(part, out int c) ? c + 1 : 1;
            }
            BodyPartRecord torso = p.RaceProps.body!.corePart!;
            Assert.Equal(counts.Values.Max(), counts[torso]);
            Assert.InRange(counts[torso] / 3000f, 0.11f, 0.19f);
        }

        [Fact]
        public void Destroying_a_part_removes_its_subtree_and_leaves_a_fresh_missing_part()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 5f, "left foot");
            Hit(p, "Cut", 30f, "left leg");
            BodyPartRecord leg = Part(p, "left leg");
            Assert.True(p.health.hediffSet.PartIsMissing(leg));
            Assert.True(p.health.hediffSet.PartIsMissing(Part(p, "left foot")));
            Assert.True(p.health.hediffSet.PartIsMissing(Part(p, "left big toe")));
            Assert.Equal(0f, p.health.hediffSet.GetPartHealth(leg));
            Hediff_MissingPart missing = Assert.Single(p.health.hediffSet.GetHediffs<Hediff_MissingPart>());
            Assert.Same(leg, missing.Part);
            Assert.True(missing.IsFresh);
            Assert.Empty(p.health.hediffSet.GetHediffs<Hediff_Injury>());
            Assert.True(p.health.hediffSet.BleedRateTotal > 0f);
            Assert.Equal(0.86f, p.health.hediffSet.GetCoverageOfNotMissingNaturalParts(), 3);

            TendUtility.DoTend(p, 1f);
            Assert.False(missing.IsFresh);
            Assert.Equal(0f, p.health.hediffSet.BleedRateTotal);
        }

        [Fact]
        public void Losing_both_legs_downs_but_does_not_kill()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 30f, "left leg");
            Assert.False(p.Downed);
            Hit(p, "Cut", 30f, "right leg");
            Assert.True(p.Downed);
            Assert.False(p.Dead);
            Assert.Equal(0f, Level(p, "Moving"));
            Assert.Equal(PawnHealthState.Down, p.health.State);
        }

        [Fact]
        public void Prosthetic_restores_a_missing_leg()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 30f, "left leg");
            BodyPartRecord leg = Part(p, "left leg");
            p.health.RestorePart(leg);
            Assert.False(p.health.hediffSet.PartIsMissing(leg));
            p.health.AddHediff(HediffNamed("SimpleProstheticLeg"), leg);
            Assert.Equal(0.85f, PawnCapacityUtility.CalculatePartEfficiency(p.health.hediffSet, leg), 3);
            Assert.Equal(0.85f, PawnCapacityUtility.CalculatePartEfficiency(p.health.hediffSet, Part(p, "left foot")), 3);
            Assert.True(p.health.hediffSet.PartIsMissing(Part(p, "left foot")));
            Assert.Equal(0f, p.health.hediffSet.PainTotal);
            Assert.Equal(0.925f, Level(p, "Moving"), 3);
            Assert.False(p.Downed);
        }

        [Fact]
        public void Destroying_the_brain_kills()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 10f, "brain");
            Assert.True(p.Dead);
            Assert.Equal(PawnHealthState.Dead, p.health.State);
            Assert.Equal(0f, Level(p, "Consciousness"));
            Assert.Equal(0f, p.health.summaryHealth.SummaryHealthPercent);
            Assert.Same(DamageDefOf.Cut, p.health.DeathCauseDamage);
        }

        [Fact]
        public void Destroying_the_heart_kills()
        {
            Pawn p = NewHuman();
            Hit(p, "Stab", 15f, "heart");
            Assert.True(p.Dead);
        }

        [Fact]
        public void Total_injury_beyond_the_lethal_threshold_kills()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 20f, "torso");
            foreach (string part in new[] { "left shoulder", "right shoulder", "left leg", "right leg", "left arm", "right arm" })
            {
                Hit(p, "Cut", 25f, part);
            }
            Assert.True(p.health.hediffSet.TotalInjurySeverity() >= HealthTuning.LethalDamageThreshold);
            Assert.True(p.health.ShouldBeDeadFromLethalDamageThreshold());
            Assert.True(p.Dead);
        }

        [Fact]
        public void Dead_pawns_stop_ticking()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 10f, "brain");
            float food = p.needs.food!.CurLevel;
            RunTicks(1000, p);
            Assert.Equal(food, p.needs.food!.CurLevel);
        }

        // ---- pain and consciousness ----

        [Fact]
        public void Pain_lowers_consciousness()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 20f, "torso");
            Assert.Equal(0.25f, p.health.hediffSet.PainTotal, 3);
            Assert.Equal(0.875f, Level(p, "Consciousness"), 3);
            Assert.False(p.Downed);
        }

        [Fact]
        public void Pain_shock_downs_and_healing_lifts_it()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 20f, "torso");
            Hit(p, "Cut", 25f, "left shoulder");
            Hit(p, "Cut", 25f, "right shoulder");
            Assert.Equal(0.875f, p.health.hediffSet.PainTotal, 3);
            Assert.True(p.health.InPainShock);
            Assert.True(p.Downed);
            Assert.False(p.Dead);

            foreach (Hediff_Injury injury in p.health.hediffSet.GetHediffs<Hediff_Injury>().ToList()) injury.Heal(30f);
            Assert.Equal(0f, p.health.hediffSet.PainTotal);
            Assert.False(p.Downed);
        }

        [Fact]
        public void Force_downed_overrides_the_body()
        {
            Pawn p = NewHuman();
            p.health.ForceDowned = true;
            Assert.True(p.Downed);
            p.health.ForceDowned = false;
            Assert.False(p.Downed);
        }

        [Fact]
        public void Pain_thought_scales_with_pain()
        {
            Pawn p = NewHuman();
            ThoughtWorker pain = DefDatabase<ThoughtDef>.GetNamed("Pain").Worker!;
            Assert.False(pain.CurrentState(p).Active);
            Hit(p, "Cut", 20f, "torso");
            ThoughtState state = pain.CurrentState(p);
            Assert.True(state.Active);
            Assert.Equal(1, state.StageIndex);
            Assert.True(p.needs.mood!.thoughts.TotalMoodOffset() <= -10f);
        }

        // ---- bleeding and healing ----

        [Fact]
        public void Bleeding_builds_blood_loss_until_tended()
        {
            Pawn p = NewHuman();
            Hit(p, "Cut", 20f, "torso");
            Assert.Equal(1.2f, p.health.hediffSet.BleedRateTotal, 3);
            RunTicks(600, p);
            Hediff? bloodLoss = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            Assert.NotNull(bloodLoss);
            Assert.InRange(bloodLoss!.Severity, 0.005f, 0.02f);

            List<Hediff> tended = TendUtility.DoTend(p, 1f);
            Assert.Single(tended);
            Assert.True(tended[0].IsTended);
            Assert.Equal(0f, p.health.hediffSet.BleedRateTotal);
            RunDays(0.5f, null, p);
            Assert.False(p.health.hediffSet.HasHediff(HediffDefOf.BloodLoss));
        }

        [Fact]
        public void Blood_loss_stages_apply_capacity_modifiers()
        {
            Pawn p = NewHuman();
            Hediff bloodLoss = p.health.AddHediff(HediffDefOf.BloodLoss);
            bloodLoss.Severity = 0.5f;
            Assert.Equal("severe", bloodLoss.CurStage!.label);
            Assert.Equal(0.7f, Level(p, "Consciousness"), 3);
            Assert.Equal(0.4f, Level(p, "Moving"), 3);
            Assert.False(p.Downed);
            bloodLoss.Severity = 0.7f;
            Assert.Equal(0.1f, Level(p, "Consciousness"), 3);
            Assert.True(p.Downed);
            bloodLoss.Severity = 1f;
            Assert.True(p.Dead);
            Assert.Same(HediffDefOf.BloodLoss, p.health.DeathCauseHediff);
        }

        [Fact]
        public void Wounds_heal_eight_points_per_day()
        {
            Pawn p = NewHuman();
            Hediff_Injury bruise = (Hediff_Injury)Hit(p, "Blunt", 10f, "left arm").hediffs[0];
            RunDays(1f, null, p);
            Assert.Equal(2f, bruise.Severity, 2);
            RunDays(0.5f, null, p);
            Assert.Empty(p.health.hediffSet.GetHediffs<Hediff_Injury>());
        }

        [Fact]
        public void Tended_wounds_heal_faster()
        {
            Pawn p = NewHuman();
            Hit(p, "Blunt", 10f, "left arm");
            TendUtility.DoTend(p, 1f);
            RunDays(0.75f, null, p);
            Assert.Empty(p.health.hediffSet.GetHediffs<Hediff_Injury>());
        }

        [Fact]
        public void Injuries_can_scar_and_stop_healing()
        {
            Pawn p = NewHuman();
            Hediff_Injury cut = (Hediff_Injury)Hit(p, "Cut", 10f, "left arm").hediffs[0];
            HediffComp_GetsPermanent comp = cut.TryGetComp<HediffComp_GetsPermanent>()!;
            comp.permanentDamageThreshold = 5f;
            cut.Heal(6f);
            Assert.True(cut.IsPermanent);
            Assert.Equal(5f, cut.Severity);
            Assert.Equal(0f, cut.BleedRate);
            Assert.False(cut.CanHealNaturally());
            Assert.Contains("permanent", cut.Label);
            Assert.Equal(5f * 0.00625f, cut.PainOffset, 4);
            RunDays(1f, null, p);
            Assert.Equal(5f, cut.Severity);
        }

        // ---- disease and immunity ----

        [Fact]
        public void Untended_wounds_sometimes_get_infected()
        {
            var pawns = new List<Pawn>();
            for (int i = 0; i < 20; i++)
            {
                Pawn p = NewHuman("P" + i);
                Hit(p, "Cut", 10f, "torso");
                pawns.Add(p);
            }
            RunTicks(45000, pawns.ToArray());
            int infected = pawns.Count(p => p.health.hediffSet.HasHediff(HediffDefOf.WoundInfection));
            Assert.InRange(infected, 1, 19);
            Assert.All(pawns, p => Assert.False(p.Dead));

            Pawn sick = pawns.First(p => p.health.hediffSet.HasHediff(HediffDefOf.WoundInfection));
            Assert.True(sick.health.hediffSet.AnyHediffMakesSickThought);
            Assert.True(DefDatabase<ThoughtDef>.GetNamed("Sick").Worker!.CurrentState(sick).Active);
            Assert.NotNull(sick.health.immunity.GetImmunityRecord(HediffDefOf.WoundInfection));
        }

        [Fact]
        public void Immunity_beats_flu_without_treatment()
        {
            Pawn p = NewHuman();
            HediffDef flu = HediffNamed("Flu");
            p.health.AddHediff(flu);
            RunDays(1.5f, null, p);
            Assert.False(p.Dead);
            Assert.Equal(1f, p.health.immunity.GetImmunity(flu));
            Assert.True(p.health.hediffSet.HasHediff(flu));
            RunDays(1.5f, null, p);
            Assert.False(p.health.hediffSet.HasHediff(flu));
            Assert.False(p.Dead);
        }

        [Fact]
        public void Plague_kills_untended_but_a_tended_pawn_survives()
        {
            Pawn untended = NewHuman("Untended");
            Pawn tended = NewHuman("Tended");
            HediffDef plague = HediffNamed("Plague");
            untended.health.AddHediff(plague);
            tended.health.AddHediff(plague);
            RunDays(2f, () => TendUtility.DoTend(tended, 1f), untended, tended);
            Assert.True(untended.Dead);
            Assert.Same(plague, untended.health.DeathCauseHediff);
            Assert.False(tended.Dead);
            Assert.Equal(1f, tended.health.immunity.GetImmunity(plague));
            Assert.True(tended.health.hediffSet.GetFirstHediffOfDef(plague)!.IsTended);
        }

        [Fact]
        public void Hediff_stages_change_hunger_and_thoughts()
        {
            Pawn p = NewHuman();
            HediffDef flu = HediffNamed("Flu");
            Hediff h = p.health.AddHediff(flu);
            Assert.Equal(1f, p.HungerRate);
            h.Severity = 0.5f;
            Assert.Equal(1.2f, p.HungerRate, 3);

            var thought = new ThoughtDef { defName = "TestFluThought", hediff = flu, workerClass = typeof(ThoughtWorker_Hediff) };
            thought.stages.Add(new ThoughtStage { label = "sniffles", baseMoodEffect = -2f });
            thought.stages.Add(new ThoughtStage { label = "fever", baseMoodEffect = -8f });
            ThoughtState state = thought.Worker!.CurrentState(p);
            Assert.True(state.Active);
            Assert.Equal(1, state.StageIndex);
            h.Severity = 0.9f;
            Assert.Equal(1, thought.Worker!.CurrentState(p).StageIndex);
        }

        [Fact]
        public void Starvation_builds_malnutrition_and_eating_clears_it()
        {
            Pawn p = NewHuman();
            p.needs.food!.CurLevel = 0f;
            RunTicks(GenDate.TicksPerDay, p);
            Hediff? malnutrition = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition);
            Assert.NotNull(malnutrition);
            Assert.InRange(malnutrition!.Severity, 0.10f, 0.12f);
            Assert.False(p.Dead);

            RunDays(1.25f, null, p);
            Assert.False(p.health.hediffSet.HasHediff(HediffDefOf.Malnutrition));
        }

        [Fact]
        public void Tending_treats_bleeding_before_disease()
        {
            Pawn p = NewHuman();
            p.health.AddHediff(HediffNamed("Flu"));
            Hediff cut = Hit(p, "Cut", 10f, "left arm").hediffs[0];
            Assert.True(TendUtility.HasAnythingToTend(p));
            List<Hediff> first = TendUtility.DoTend(p, 1f);
            Assert.Single(first);
            Assert.Same(cut, first[0]);
            List<Hediff> second = TendUtility.DoTend(p, 1f);
            Assert.Single(second);
            Assert.Same(HediffNamed("Flu"), second[0].def);
            Assert.Empty(TendUtility.DoTend(p, 1f));
        }

        // ---- save / load ----

        [Fact]
        public void Health_state_survives_a_save_and_load()
        {
            Pawn p = NewHuman("Ash");
            Hit(p, "Cut", 12f, "torso");
            TendUtility.DoTend(p, 0.8f);
            Hit(p, "Cut", 30f, "left leg");
            p.health.AddHediff(HediffNamed("Flu"));
            RunTicks(3000, p);
            Assert.True(p.Downed || !p.Downed);

            var holder = new PawnHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Pawn q = loaded.pawns![0];

            Assert.Equal(p.health.State, q.health.State);
            Assert.Equal(p.health.hediffSet.hediffs.Count, q.health.hediffSet.hediffs.Count);
            for (int i = 0; i < p.health.hediffSet.hediffs.Count; i++)
            {
                Hediff a = p.health.hediffSet.hediffs[i];
                Hediff b = q.health.hediffSet.hediffs[i];
                Assert.Same(a.def, b.def);
                Assert.Equal(a.GetType(), b.GetType());
                Assert.Equal(a.Part?.Label, b.Part?.Label);
                Assert.Equal(a.Severity, b.Severity, 4);
                Assert.Equal(a.IsTended, b.IsTended);
                Assert.Equal(a.TendQuality, b.TendQuality, 4);
            }
            Assert.True(q.health.hediffSet.PartIsMissing(Part(q, "left leg")));
            Assert.Equal(p.health.immunity.GetImmunity(HediffNamed("Flu")), q.health.immunity.GetImmunity(HediffNamed("Flu")), 4);
            Assert.Equal(p.health.hediffSet.PainTotal, q.health.hediffSet.PainTotal, 4);
            Assert.Equal(Level(p, "Moving"), Level(q, "Moving"), 4);
            RunTicks(10, q);
            Assert.False(q.Dead);
        }
    }
}
