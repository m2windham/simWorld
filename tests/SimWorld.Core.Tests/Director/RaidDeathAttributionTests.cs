using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// A raid is credited with the people it kills — including the ones who die of it later.
    ///
    /// <para/><b>The defect, from the player's side.</b> A raid wiped out a settlement and the loss summary said
    /// raids had killed nobody (<c>docs/perf/storyteller-populated</c>: seed 12345, 25 deaths, every one traced
    /// to a raid injury, none credited to a raid). Two holes, one behind the other:
    /// <list type="number">
    /// <item><see cref="IncidentWorker_RaidEnemy"/> never stamped its squad with
    /// <see cref="Pawn.spawnedByIncident"/>, so even a citizen shot dead on the spot was credited to nothing —
    /// and an unwatched raid, which credits from the same field, likewise.</item>
    /// <item>Most of those deaths were not on the spot. Citizens were downed and then bled out or died of an
    /// infected wound, with no <see cref="DamageInfo"/> at the moment of death and no raider left to ask. Fixing
    /// only the first hole would have credited the handful killed outright and left the rest where they
    /// were.</item>
    /// </list>
    /// The second is closed by the wound carrying its own provenance (<see cref="WoundProvenance"/>), so a
    /// death is credited to the incident behind the wound that caused it rather than to the proximate cause.
    /// What they <i>died of</i> — the ledger's other axis — is deliberately unchanged: blood loss is still an
    /// injury death and an infection still a disease death.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RaidDeathAttributionTests : ContentTestBase
    {
        public RaidDeathAttributionTests(CoreContentFixture content) : base(content)
        {
            Ablation.Clear();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
        }

        private const string Raid = "RaidEnemy";

        private const string Pack = "ManhunterPack";

        /// <summary>Generous cap on how long anything here is allowed to take to die; every death this class
        /// waits for lands well inside it, and a run that reaches it has failed rather than hung.</summary>
        private const int DeathDeadlineTicks = 3 * GenDate.TicksPerDay;

        private static DeathLedger Ledger => Find.Storyteller.deaths;

        // ---- fixtures ----

        private static void CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
        }

        private static Faction Raiders() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough", "F_Rough");

        /// <summary>A squad from the real raid incident, fired at a bare target — no settlement, no map — so it
        /// is generated, stamped and handed back without landing anywhere (the worker's own test pose).</summary>
        private static IReadOnlyList<Pawn> RaidSquad()
        {
            var worker = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
            Assert.True(worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), points = 300f, faction = Raiders() }));
            Assert.NotNull(worker.LastRaidPawns);
            Assert.NotEmpty(worker.LastRaidPawns!);
            return worker.LastRaidPawns!;
        }

        private static Pawn Raider() => RaidSquad()[0];

        /// <summary>A beast from the real manhunter incident. Fired at a settlement of a civilization nobody
        /// registered, so it resolves abstractly and whatever it does there is not ours to count.</summary>
        private static Pawn PackBeast()
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Elsewhere", 0);
            for (int i = 0; i < 4; i++) settlement.AddCitizen(NewHuman("Elsewhere" + i));
            var target = new CivilizationTarget();   // deliberately not registered
            target.SetSettlements(new[] { settlement });

            IncidentDef manhunter = DefDatabase<IncidentDef>.GetNamed(Pack);
            Assert.True(manhunter.Worker.TryExecute(new IncidentParms { target = target, points = 400f }));
            IReadOnlyList<Pawn>? pack = ((IncidentWorker_ManhunterPack)manhunter.Worker).LastPack;
            Assert.NotNull(pack);
            return pack!.First(p => !p.Dead);
        }

        private static BodyPartRecord Part(Pawn p, string label) =>
            p.RaceProps.body!.GetPartByLabel(label) ?? throw new InvalidOperationException("no part " + label);

        /// <summary>One blow through the real damage funnel, dealt by <paramref name="by"/> (null: nobody).</summary>
        private static DamageResult Hit(Pawn victim, Pawn? by, string damage, float amount, string part)
        {
            DamageDef def = DefDatabase<DamageDef>.GetNamed(damage);
            return def.Worker.Apply(new DamageInfo(def, amount, 0f, by, Part(victim, part)), victim);
        }

        /// <summary>Takes both legs off: down on the spot, and two fresh stumps that bleed and do not heal on
        /// their own, so nobody tending them means dying of it hours later.</summary>
        private static void DownWithBleedingStumps(Pawn victim, Pawn? by, string damage = "Bullet")
        {
            Hit(victim, by, damage, 35f, "left leg");
            Hit(victim, by, damage, 35f, "right leg");
        }

        /// <summary>Ticks until <paramref name="pawn"/> dies, reporting whether it was ever seen down on the way.</summary>
        private static bool RunUntilDead(Pawn pawn)
        {
            bool sawDowned = pawn.Downed;
            for (int elapsed = 0; elapsed < DeathDeadlineTicks && !pawn.Dead; elapsed += 250)
            {
                RunTicks(250, pawn);
                sawDowned |= pawn.Downed;
            }
            Assert.True(pawn.Dead, pawn.Label + " was still alive " + DeathDeadlineTicks + " ticks on: " + string.Join(", ", pawn.health.hediffSet.hediffs));
            return sawDowned;
        }

        private const int CohortSize = 40;

        /// <summary>
        /// A cohort each cut once by <paramref name="by"/> and left untended, ticked until the first wound turns
        /// septic through <see cref="HediffComp_Infecter"/> — the real roll, not a stand-in. The cut is the one
        /// <c>HealthTests.Untended_wounds_sometimes_get_infected</c> uses, which nobody bleeds out from.
        /// <para/>
        /// <see cref="CohortSize"/> is sized for the chance, not for the test: a cut is infected 15% of the time
        /// (RimWorld's own figure, which lane/downed ported in place of 40%), so twelve untended cuts all stay
        /// clean about one run in seven, and forty about one in 670.
        /// </summary>
        private static Pawn FirstInfected(Pawn? by, string cohortName)
        {
            var cohort = new List<Pawn>();
            for (int i = 0; i < CohortSize; i++)
            {
                Pawn p = NewHuman(cohortName + i);
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

        /// <summary>
        /// Carries an infection the rest of the way. Progression to lethal takes days of immunity racing
        /// severity and is <c>HealthTests</c>' business; what this class cares about is that the death at the
        /// end of it goes through the real funnel with the real culprit, which setting its severity does —
        /// the setter runs the same state check that the last day of an untreated infection would.
        /// </summary>
        private static Hediff DieOfInfection(Pawn pawn)
        {
            Hediff infection = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.WoundInfection)!;
            infection.Severity = infection.def.lethalSeverity;
            Assert.True(pawn.Dead);
            return infection;
        }

        // ---- the squad ----

        [Fact]
        public void Every_raider_remembers_the_incident_that_sent_it()
        {
            Assert.All(RaidSquad(), raider => Assert.Equal(Raid, raider.spawnedByIncident));
        }

        // ---- killed on the spot ----

        [Fact]
        public void A_citizen_killed_outright_by_a_raider_is_credited_to_the_raid()
        {
            Pawn victim = NewHuman("Victim");
            CivilizationOf(victim);

            Hit(victim, Raider(), "Bullet", 200f, "torso");

            Assert.True(victim.Dead);
            Assert.True(victim.health.KilledByAnotherPawn);
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(1, Ledger.AttributedTo(Raid));
        }

        // ---- killed later: the part that was missing ----

        /// <summary>
        /// The case the seed was full of. Downed by the raid, left untended, dead of blood loss hours later —
        /// with no blow at the moment of death and the raider who did it already dead. Nothing at the death
        /// can ask the raider anything; the wound has to know.
        /// </summary>
        [Fact]
        public void A_citizen_downed_in_a_raid_who_bleeds_out_later_is_credited_to_the_raid()
        {
            Pawn victim = NewHuman("Victim");
            CivilizationOf(victim);
            Pawn raider = Raider();

            DownWithBleedingStumps(victim, raider);
            int woundedAt = Find.TickManager.TicksGame;
            Assert.True(victim.Downed, "two legs off should down them on the spot");
            Assert.False(victim.Dead, "the blows themselves must not kill, or this is the other test");

            raider.health.Kill(null, null);   // gone long before the citizen dies: not ours, not counted

            RunUntilDead(victim);

            Assert.Null(victim.health.DeathCauseDamage);                           // no blow at the end
            Assert.Same(HediffDefOf.BloodLoss, victim.health.DeathCauseHediff);    // bled out
            Assert.True(victim.health.DeathTick - woundedAt >= 3 * GenDate.TicksPerHour,
                "died " + (victim.health.DeathTick - woundedAt) + " ticks after the raid — that is not 'later'");
            Assert.Equal(DeathCause.Injury, victim.health.CauseOfDeath);           // what they died of
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(1, Ledger.AttributedTo(Raid));                             // what killed them
        }

        [Fact]
        public void A_stump_the_raid_left_carries_the_raid()
        {
            Pawn victim = NewHuman("Victim");

            DownWithBleedingStumps(victim, Raider());

            List<Hediff_MissingPart> stumps = victim.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Where(m => m.IsFresh).ToList();
            Assert.Equal(2, stumps.Count);
            Assert.All(stumps, stump => Assert.Equal(Raid, stump.sourceIncident));
            Assert.Equal(Raid, WoundProvenance.OfBleeding(victim.health.hediffSet));
        }

        /// <summary>
        /// By-source says the raid; by-cause still says disease, and both are right: the two axes answer
        /// different questions (<see cref="DeathLedger"/>'s own doc). The infection is the real one the wound
        /// grew — only its last step to lethal is shortened, see <see cref="DieOfInfection"/>.
        /// </summary>
        [Fact]
        public void A_citizen_who_dies_of_an_infected_raid_wound_is_credited_to_the_raid()
        {
            Pawn sick = FirstInfected(Raider(), "Cohort");
            CivilizationOf(sick);

            Hediff infection = DieOfInfection(sick);

            Assert.Equal(Raid, infection.sourceIncident);
            Assert.Null(sick.health.DeathCauseDamage);
            Assert.Same(HediffDefOf.WoundInfection, sick.health.DeathCauseHediff);
            Assert.Equal(DeathCause.Disease, sick.health.CauseOfDeath);
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(1, Ledger.AttributedTo(Raid));
        }

        // ---- and the player is told ----

        /// <summary>The sentence the defect was about: after a raid, the loss summary names the raid — for the
        /// citizen shot dead, the one who bled out, and the one the infection took.</summary>
        [Fact]
        public void After_a_raid_the_loss_summary_names_the_raid_for_every_death_it_caused()
        {
            Pawn raider = Raider();

            Pawn shot = NewHuman("Shot");
            Pawn bled = NewHuman("Bled");
            Pawn sick = FirstInfected(raider, "Cohort");
            CivilizationOf(shot, bled, sick);

            Hit(shot, raider, "Bullet", 200f, "torso");
            DownWithBleedingStumps(bled, raider);
            RunUntilDead(bled);
            DieOfInfection(sick);

            LossSummary losses = GodViewSnapshot.Capture().Losses;

            Assert.Equal(3, losses.TotalDeaths);
            DeathSourceLine line = Assert.Single(losses.DeathsBySource);
            Assert.Equal(Raid, line.Source);
            Assert.Equal(3, line.Count);

            // What they died of is its own question, and it is unchanged.
            Assert.Equal(2, losses.DeathsByCause.Single(l => l.Cause == DeathCause.Injury).Count);
            Assert.Equal(1, losses.DeathsByCause.Single(l => l.Cause == DeathCause.Disease).Count);
        }

        // ---- the unwatched raid, which credits from the same field ----

        /// <summary>The resolver reads the squad's own stamp and credits every citizen it kills from the ledger's
        /// delta — the path that already worked for the pack and read null for every raid.</summary>
        [Fact]
        public void An_unwatched_raid_is_credited_with_everyone_it_kills()
        {
            int runsThatKilled = 0;
            var worker = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;

            for (int seed = 0; seed < 12; seed++)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();

                var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Raided" + seed, 0);
                for (int i = 0; i < 6; i++) settlement.AddCitizen(NewHuman("Villager" + seed + "_" + i));
                var target = new CivilizationTarget(Find.Storyteller);
                target.SetSettlements(new[] { settlement });

                Assert.True(worker.TryExecute(new IncidentParms { target = target, points = 800f, faction = Raiders() }));
                Assert.NotNull(worker.LastRaidOutcome);   // it did resolve abstractly

                Assert.Equal(Ledger.Total, Ledger.AttributedTo(Raid));
                if (Ledger.Total > 0) runsThatKilled++;
            }

            Assert.True(runsThatKilled > 0, "no raid in the sweep killed anybody — the path is untested");
        }

        // ---- it never over-claims ----

        /// <summary>The control for the bleed-out: the same wounds from somebody nobody sent, the same death,
        /// and nobody credited. The wound carries provenance; it does not invent it.</summary>
        [Fact]
        public void The_same_bleed_out_from_a_wound_nobody_sent_is_credited_to_nobody()
        {
            Pawn victim = NewHuman("Victim");
            Pawn neighbour = NewHuman("Neighbour");   // born here: no spawnedByIncident
            CivilizationOf(victim, neighbour);

            DownWithBleedingStumps(victim, neighbour);
            RunUntilDead(victim);

            Assert.Same(HediffDefOf.BloodLoss, victim.health.DeathCauseHediff);
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(0, Ledger.TotalAttributed);
        }

        /// <summary>
        /// Blood loss has no single cause, so it answers for what was bleeding into it when it became lethal —
        /// not for whatever first opened it. A raid wound that was tended and stopped bleeding does not own a
        /// death that a later, unrelated wound bled to the end.
        /// </summary>
        [Fact]
        public void A_bleed_out_belongs_to_what_was_bleeding_when_it_became_lethal()
        {
            Pawn victim = NewHuman("Victim");
            Pawn neighbour = NewHuman("Neighbour");
            CivilizationOf(victim, neighbour);

            Hit(victim, Raider(), "Cut", 20f, "torso");
            RunTicks(2000, victim);
            Hediff bloodLoss = victim.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)!;
            Assert.Equal(Raid, bloodLoss.sourceIncident);   // the raid opened it

            TendUtility.DoTend(victim, 1f);
            Assert.Equal(0f, victim.health.hediffSet.BleedRateTotal);

            DownWithBleedingStumps(victim, neighbour);
            RunUntilDead(victim);

            Assert.Same(HediffDefOf.BloodLoss, victim.health.DeathCauseHediff);
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(0, Ledger.AttributedTo(Raid));   // ...but the neighbour's wounds finished it
        }

        /// <summary>
        /// One wound from two blows belongs to whichever made more of it. A bruise is the shipped injury that
        /// merges; a raider's tap on a neighbour's beating does not make the beating the raid's, and the
        /// reverse does.
        /// </summary>
        [Theory]
        [InlineData(8f, 2f, null)]
        [InlineData(2f, 8f, Raid)]
        public void A_merged_wound_belongs_to_the_larger_blow(float fromNeighbour, float fromRaider, string? expected)
        {
            Pawn victim = NewHuman("Victim");
            Pawn neighbour = NewHuman("Neighbour");
            Pawn raider = Raider();

            Hit(victim, neighbour, "Blunt", fromNeighbour, "left arm");
            Hit(victim, raider, "Blunt", fromRaider, "left arm");

            Hediff_Injury bruise = Assert.Single(victim.health.hediffSet.hediffs.OfType<Hediff_Injury>());
            Assert.Equal("Bruise", bruise.def.defName);
            Assert.Equal(fromNeighbour + fromRaider, bruise.Severity, 3);
            Assert.Equal(expected, bruise.sourceIncident);
        }

        // ---- the incident that already worked ----

        /// <summary>
        /// The pack's attribution is not moved by any of this, and the two incidents do not bleed into each
        /// other: a pack kill is the pack's and a raid kill the raid's, in the same run. What is new for the
        /// pack is the same thing that is new for the raid — a citizen a beast mauls who bleeds out later is
        /// now the pack's too, where before it was nobody's.
        /// </summary>
        [Fact]
        public void A_pack_is_still_credited_with_its_own_kills_and_the_raid_with_none_of_them()
        {
            Pawn beast = PackBeast();
            Assert.Equal(Pack, beast.spawnedByIncident);
            Pawn raider = Raider();

            Pawn mauled = NewHuman("Mauled");
            Pawn bled = NewHuman("Bled");
            Pawn shot = NewHuman("Shot");
            CivilizationOf(mauled, bled, shot);

            Hit(mauled, beast, "Bite", 200f, "torso");
            DownWithBleedingStumps(bled, beast, "Bite");
            RunUntilDead(bled);
            Hit(shot, raider, "Bullet", 200f, "torso");

            Assert.Equal(3, Ledger.Total);
            Assert.Equal(2, Ledger.AttributedTo(Pack));
            Assert.Equal(1, Ledger.AttributedTo(Raid));
        }

        // ---- a save in the middle of it ----

        /// <summary>
        /// Saved while bleeding, loaded, and bled out after: the reloaded wounds still trace back to the raid,
        /// and so does the death. The raider is not in the save at all — only the wound's own stamp is — which is
        /// the point: provenance that needed the killer alive and referenced would not survive a reload.
        /// </summary>
        [Fact]
        public void A_saved_and_reloaded_wound_still_traces_back_to_the_raid()
        {
            Pawn victim = NewHuman("Victim");
            DownWithBleedingStumps(victim, Raider());
            WoundProvenance.AccrueBloodLoss(victim, 0.1f);   // mid-bleed, so blood loss is in the save too

            string xml = Scribe.SaveToString(victim, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            List<Hediff_MissingPart> stumps = loaded.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Where(m => m.IsFresh).ToList();
            Assert.Equal(2, stumps.Count);
            Assert.All(stumps, stump => Assert.Equal(Raid, stump.sourceIncident));
            Assert.Equal(Raid, loaded.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)!.sourceIncident);

            CivilizationOf(loaded);
            RunUntilDead(loaded);

            Assert.Same(HediffDefOf.BloodLoss, loaded.health.DeathCauseHediff);
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(1, Ledger.AttributedTo(Raid));
        }
    }
}
