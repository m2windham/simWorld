using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Tests.Pawns;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Whether a citizen who goes down survives it — the recovery chain RimWorld has and a raid leans on:
    /// someone tends the wounds, someone carries the patient to a bed, and the patient wins the race against
    /// the infections that follow.
    /// <para/>
    /// Written by lane/downed after the populated storyteller run wiped out its founding band. Following
    /// every downing through a replay of that run showed the first two links working in play — tend jobs
    /// reached most raid casualties within a few hundred ticks and stopped the bleeding, and rescue jobs
    /// carried the long-downed to the one bed there was — and the third one broken: wound infections killed
    /// citizens who had been tended almost continuously, because this port's <c>WoundInfection</c> stages
    /// summed several infections into a consciousness of zero long before any of them reached lethal
    /// severity, and because wounds got infected at two to three times RimWorld's rate. The content and
    /// <see cref="HediffComp_Infecter"/> now carry RimWorld's own numbers; the tests below pin the rules
    /// those numbers produce rather than the numbers.
    /// </summary>
    public class DownedRecoveryTests : ContentTestBase
    {
        public DownedRecoveryTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static and ContentTestBase does not reset it — see DoctorAITests.
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction faction, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            p.faction = faction;
            return p;
        }

        private static Thing SpawnBed(CoreMap map, IntVec3 cell)
        {
            Thing bed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bed"));
            GenSpawn.Spawn(bed, cell, map);
            return bed;
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void Cut(Pawn p, string part, float amount)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            cut.Worker.Apply(new DamageInfo(cut, amount, hitPart: Part(p, part)), p);
        }

        /// <summary>Five deep cuts: enough bleeding that a patient nobody reaches bleeds out inside a day.</summary>
        private static void RaidWounds(Pawn p)
        {
            foreach (string part in new[] { "torso", "left leg", "right leg", "left arm", "right arm" }) Cut(p, part, 10f);
        }

        private static float BloodLoss(Pawn p) => p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;

        private static float Consciousness(Pawn p) => p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);

        private static List<Hediff> Infect(Pawn p, params string[] parts)
        {
            var made = new List<Hediff>();
            foreach (string part in parts) made.Add(p.health.AddHediff(HediffDefOf.WoundInfection, Part(p, part)));
            return made;
        }

        /// <summary>Ticks in hours, keeping food and rest full so health alone decides the outcome, and hands
        /// each hour to <paramref name="eachHour"/> — HealthTests.RunDays's shape.</summary>
        private static void RunHours(int hours, Action? eachHour, params Pawn[] pawns)
        {
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

        /// <summary>Whatever needs tending, tended, the way a doctor on hand keeps doing it.</summary>
        private static void TendEverything(Pawn p, float quality)
        {
            for (int guard = 0; guard < 20 && !p.Dead && TendUtility.HasAnythingToTend(p); guard++)
            {
                TendUtility.DoTend(p, quality, TendUtility.MaxQualityNoMedicine);
            }
        }

        // ---- the first two links, in play ----

        [Fact]
        public void A_downed_bleeding_colonist_is_tended_and_carried_to_a_bed_by_a_colonist_nobody_told_to()
        {
            CoreMap map = NewMap(12);
            Faction colony = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), colony, "Doctor");
            Pawn patient = SpawnHuman(map, new IntVec3(10, 0, 10), colony, "Patient");
            Cut(patient, "left arm", 6f);
            patient.health.ForceDowned = true;
            Thing bed = SpawnBed(map, new IntVec3(1, 0, 1));
            Assert.True(patient.health.hediffSet.BleedRateTotal > 0f);

            // No job is started by hand: the doctor's own think tree has to find both.
            var chosen = new HashSet<JobDef>();
            for (int i = 0; i < 6000 && patient.Position != bed.Position; i++)
            {
                RunTicks(1, doctor, patient);
                if (doctor.jobs.curJob != null) chosen.Add(doctor.jobs.curJob.def);
            }

            Assert.Contains(JobDefOf.TendPatient, chosen);
            Assert.Contains(JobDefOf.Rescue, chosen);
            Assert.Equal(bed.Position, patient.Position);
            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
        }

        [Fact]
        public void A_tended_bleeding_wound_bleeds_less_than_an_untended_one()
        {
            Pawn tended = NewHuman("Tended");
            Pawn untended = NewHuman("Untended");
            Cut(tended, "left leg", 10f);
            Cut(untended, "left leg", 10f);
            Assert.Equal(untended.health.hediffSet.BleedRateTotal, tended.health.hediffSet.BleedRateTotal, 5);

            TendUtility.DoTend(tended, 0.5f, TendUtility.MaxQualityNoMedicine);

            Assert.True(tended.health.hediffSet.BleedRateTotal < untended.health.hediffSet.BleedRateTotal);

            RunHours(6, null, tended, untended);
            Assert.True(BloodLoss(tended) < BloodLoss(untended),
                "six hours on, the tended pawn must have lost less blood than the one nobody tended");
        }

        // ---- the chain, end to end ----

        [Fact]
        public void A_downed_colonist_who_is_rescued_and_tended_survives_wounds_that_kill_one_left_alone()
        {
            Faction colony = NewFaction("Colony");

            CoreMap attended = NewMap(12);
            Pawn doctor = SpawnHuman(attended, new IntVec3(0, 0, 0), colony, "Doctor");
            Pawn saved = SpawnHuman(attended, new IntVec3(10, 0, 10), colony, "Saved");
            Thing bed = SpawnBed(attended, new IntVec3(1, 0, 1));

            CoreMap alone = NewMap(12);
            Pawn abandoned = SpawnHuman(alone, new IntVec3(10, 0, 10), colony, "Abandoned");

            foreach (Pawn p in new[] { saved, abandoned })
            {
                RaidWounds(p);
                p.health.ForceDowned = true;
            }
            Assert.Equal(abandoned.health.hediffSet.BleedRateTotal, saved.health.hediffSet.BleedRateTotal, 5);

            RunTicks(GenDate.TicksPerDay, doctor, saved, abandoned);

            Assert.True(abandoned.Dead, "sanity: these wounds kill a downed pawn nobody reaches inside a day");
            Assert.Same(HediffDefOf.BloodLoss, abandoned.health.DeathCauseHediff);
            Assert.False(saved.Dead, "the same wounds, tended and rescued, must not kill");
            Assert.Equal(bed.Position, saved.Position);
        }

        // ---- the third link: infection, RimWorld's rules ----

        /// <summary>
        /// RimWorld's rule, in the wiki's words: "If the patient can become immune before their worst infection
        /// reaches 100%, they have beaten all their infections." Several infections at once may down a pawn —
        /// the last stage caps consciousness at 0.1 — but none of them kills until one reaches lethal severity.
        /// Before lane/downed four infections here summed to a consciousness of zero and killed.
        /// </summary>
        [Fact]
        public void Several_infections_at_their_worst_stage_down_a_citizen_without_killing_them()
        {
            Pawn p = NewHuman();
            List<Hediff> infections = Infect(p, "torso", "left leg", "right leg", "left arm");
            foreach (Hediff h in infections) h.Severity = 0.95f;

            Assert.False(p.Dead, "no infection has reached lethal severity, so none of them may have killed");
            Assert.True(p.Downed);
            Assert.True(Consciousness(p) > HealthTuning.MinCapableLevel);
            Assert.True(Consciousness(p) <= 0.1f + 1e-4f, "the last stage caps consciousness rather than subtracting from it");

            infections[0].Severity = 1f;
            Assert.True(p.Dead, "reaching lethal severity still kills");
            Assert.Same(HediffDefOf.WoundInfection, p.health.DeathCauseHediff);
        }

        /// <summary>
        /// The survival this lane exists for, measured in play before the fix: citizens tended through
        /// several infections at once, whose immunity was nearly won, died anyway. A citizen tended as a
        /// settlement's doctors tend lives.
        /// </summary>
        [Fact]
        public void A_citizen_tended_through_several_infections_at_once_lives_once_immunity_wins()
        {
            Pawn p = NewHuman();
            Infect(p, "torso", "left leg", "right leg", "left arm");

            float peak = 0f;
            RunHours(GenDate.HoursPerDay * 3, () =>
            {
                TendEverything(p, TendUtility.MaxQualityNoMedicine);
                foreach (Hediff h in p.health.hediffSet.hediffs)
                {
                    if (h.def == HediffDefOf.WoundInfection) peak = Math.Max(peak, h.Severity);
                }
            }, p);

            Assert.True(peak > 0.62f, "sanity: the infections must have got as far as the old extreme stage, where they used to kill");
            Assert.False(p.Dead, "tended every time it could be, the citizen must win the race");
            float worstLeft = p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.WoundInfection).Select(h => h.Severity).DefaultIfEmpty(0f).Max();
            Assert.True(worstLeft < peak, "three days on, immunity must be driving every infection back down");
        }

        [Fact]
        public void An_untended_infection_still_kills()
        {
            Pawn p = NewHuman();
            Infect(p, "left leg");

            RunHours(GenDate.HoursPerDay * 2, null, p);

            Assert.True(p.Dead, "untended, the infection outruns immunity (RimWorld: +0.84/day against immunity's slower climb)");
            Assert.Same(HediffDefOf.WoundInfection, p.health.DeathCauseHediff);
        }

        [Fact]
        public void An_infection_is_tended_more_often_than_a_day_apart()
        {
            // RimWorld tends an infection for 12 hours; this port used to give it 24.
            Pawn p = NewHuman();
            Hediff infection = Infect(p, "left leg")[0];
            TendUtility.DoTend(p, 0.5f, TendUtility.MaxQualityNoMedicine);
            Assert.True(infection.IsTended);

            RunHours(GenDate.HoursPerDay / 2 + 1, null, p);

            Assert.False(infection.IsTended, "a tend must have lapsed within half a day");
            Assert.True(infection.TendableNow());
        }

        /// <summary>
        /// Whether a wound gets infected, sampled rather than pinned: the chance is RimWorld's own (a cut is
        /// 15%, per the wiki's def dump) and a tend scales it down along RimWorld's line from 85% at quality 0
        /// to 5% at quality 1. A band around the untended rate, and a direction for the tended one. Before
        /// lane/downed a cut here was infected 40% of the time.
        /// </summary>
        [Fact]
        public void Cuts_get_infected_at_about_RimWorlds_rate_and_well_tended_ones_far_less_often()
        {
            int untended = CountInfectedCuts(tendQuality: null);
            int tended = CountInfectedCuts(tendQuality: 1f);

            // 300 cuts at 15%: 45 expected, a standard deviation of about 6.
            Assert.InRange(untended, 25, 70);
            Assert.True(tended * 3 < untended, "tended " + tended + " vs untended " + untended);
        }

        [Fact]
        public void The_tended_infection_factor_runs_along_RimWorlds_line()
        {
            Assert.Equal(HediffComp_Infecter.TendedChanceFactorAtQualityZero, HediffComp_Infecter.TendedChanceFactor(0f), 5);
            Assert.Equal(HediffComp_Infecter.TendedChanceFactorAtQualityOne, HediffComp_Infecter.TendedChanceFactor(1f), 5);
            Assert.True(HediffComp_Infecter.TendedChanceFactor(0.3f) > HediffComp_Infecter.TendedChanceFactor(0.7f));
            Assert.True(HediffComp_Infecter.TendedChanceFactor(0f) < 1f, "even a tend of quality 0 lowers the chance");
        }

        /// <summary>Fifteen pawns, twenty shallow cuts each — shallow so no pawn comes near death and every
        /// cut's infection roll is actually taken. Only the cuts are ticked: the roll is theirs alone.</summary>
        private static int CountInfectedCuts(float? tendQuality)
        {
            string[] parts = { "torso", "left leg", "right leg", "left arm", "right arm" };
            int infected = 0;
            for (int n = 0; n < 15; n++)
            {
                Pawn p = NewHuman("P" + n);
                for (int i = 0; i < 20; i++) Cut(p, parts[i % parts.Length], 1f);
                List<Hediff> cuts = p.health.hediffSet.hediffs.Where(h => h.def.defName == "Cut").ToList();
                Assert.Equal(20, cuts.Count);
                if (tendQuality.HasValue) TendUtility.DoTend(p, tendQuality.Value, 1f);

                int maxDelay = HealthTuning.InfectionDelayRange.max + 1;
                for (int t = 0; t < maxDelay; t++)
                {
                    for (int c = 0; c < cuts.Count; c++) cuts[c].Tick();
                }
                Assert.False(p.Dead);
                infected += p.health.hediffSet.hediffs.Count(h => h.def == HediffDefOf.WoundInfection);
            }
            return infected;
        }

        // ---- save and load ----

        [Fact]
        public void A_citizen_downed_by_tended_infections_round_trips_through_Scribe()
        {
            Pawn p = NewHuman();
            List<Hediff> infections = Infect(p, "torso", "left leg");
            infections[0].Severity = 0.95f;
            infections[1].Severity = 0.5f;
            TendEverything(p, 0.6f);
            RunTicks(100, p);
            Assert.True(p.Downed);

            var holder = new PawnHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Pawn q = loaded.pawns![0];

            List<Hediff> before = p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.WoundInfection).ToList();
            List<Hediff> after = q.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.WoundInfection).ToList();
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Part?.Label, after[i].Part?.Label);
                Assert.Equal(before[i].Severity, after[i].Severity, 4);
                Assert.Equal(before[i].CurStageIndex, after[i].CurStageIndex);
                Assert.Equal(before[i].IsTended, after[i].IsTended);
                Assert.Equal(before[i].TendQuality, after[i].TendQuality, 4);
            }
            Assert.Equal(p.health.immunity.GetImmunity(HediffDefOf.WoundInfection), q.health.immunity.GetImmunity(HediffDefOf.WoundInfection), 4);
            Assert.Equal(p.Downed, q.Downed);
            Assert.Equal(Consciousness(p), Consciousness(q), 4);
        }
    }
}
