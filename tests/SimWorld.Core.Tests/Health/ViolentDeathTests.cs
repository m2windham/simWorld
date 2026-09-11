using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Telling a killing from a death (<c>DamageDef.externalViolence</c>).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> Nothing in the port read the flag, so
    /// every death was the same event: an old woman dying in her sleep, a man cut down in the street and a
    /// patient lost on the operating table were indistinguishable to the storyteller, to the people in the
    /// room and to the letter stack. Three things now turn on it — the adaptation the storyteller pays, the
    /// memory the witnesses keep, and what the player is told — and each is asserted here in both
    /// directions, because a classifier that says "violent" to everything is exactly as useless as none.
    /// </summary>
    public class ViolentDeathTests : ContentTestBase
    {
        public ViolentDeathTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            CorpseDefGenerator.EnsureGenerated();
        }

        private static StoryWatcher_Adaptation Adaptation => Find.Storyteller.adaptation;

        private static DifficultyDef Medium => DefDatabase<DifficultyDef>.GetNamed("Medium");

        private static IReadOnlyList<Letter> Letters => Find.LetterStack.LettersListForReading;

        private static CoreMap NewMap(int size) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Faction PlayerFaction()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Find.FactionManager.Add(f);
            return f;
        }

        /// <summary>Quiet time on the storyteller's clock, so a penalty has something to cut into and cannot
        /// hide behind the zero floor <see cref="StoryWatcher_Adaptation.Notify_ColonistDied"/> clamps to.</summary>
        private static float BuildQuietTime()
        {
            for (int i = 0; i < 4000; i++) Adaptation.AdaptationTick(Medium);
            Assert.True(Adaptation.AdaptDays > StoryWatcher_Adaptation.DeathAdaptDaysPenalty);
            return Adaptation.AdaptDays;
        }

        /// <summary>Registers a civilization holding these pawns — the roster the threat curve reads, which
        /// is what decides whose death counts (see <see cref="StorytellerPawnEvents"/>).</summary>
        private static void CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
        }

        private static DamageInfo Violence(float amount = 999f) =>
            new DamageInfo(DamageDefOf.Bullet, amount);

        // ---- the classification itself ----

        [Fact]
        public void The_body_says_whether_it_was_killed()
        {
            Pawn shot = NewHuman("Shot");
            shot.health.Kill(Violence(), null);
            Assert.True(shot.health.DiedViolently);
            Assert.Same(DamageDefOf.Bullet, shot.health.DeathCauseDamage);

            Pawn old = NewHuman("Old");
            old.health.Kill(null, null);
            Assert.False(old.health.DiedViolently);

            Pawn patient = NewHuman("Patient");
            patient.health.Kill(new DamageInfo(SurgeryDamageDefOf.SurgicalCut, 999f), null);
            Assert.True(patient.Dead);
            Assert.False(patient.health.DiedViolently);

            Assert.False(NewHuman("Alive").health.DiedViolently);
        }

        [Fact]
        public void Exactly_one_shipped_damage_is_not_violence()
        {
            // The distinction has to exist in content or the reader is decoration. It is a surgeon's knife,
            // and it is the one the port already used to kill patients with — as plain Cut, which is why a
            // botched operation would have read as a murder the moment anything consulted this flag.
            List<string> nonViolent = DefDatabase<DamageDef>.AllDefsListForReading
                .Where(d => !d.externalViolence).Select(d => d.defName).ToList();

            Assert.Equal(new[] { "SurgicalCut" }, nonViolent);
        }

        // ---- the storyteller ----

        [Fact]
        public void A_citizen_killed_eases_the_storyteller()
        {
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.Kill(Violence(), null);

            Assert.Equal(quiet - StoryWatcher_Adaptation.DeathAdaptDaysPenalty, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Dying_of_something_that_is_not_violence_does_not()
        {
            // The whole reason the raid lane raised Notify_ColonistDied from SettlementRaidResolver instead
            // of from the death funnel: without a classifier, a civilization of long-lived people would have
            // read to the storyteller as one under constant attack.
            float quiet = BuildQuietTime();
            Pawn age = NewHuman("Age");
            Pawn surgery = NewHuman("Surgery");
            CivilizationOf(age, surgery);

            age.health.Kill(null, null);
            surgery.health.Kill(new DamageInfo(SurgeryDamageDefOf.SurgicalCut, 999f), null);

            Assert.True(age.Dead);
            Assert.True(surgery.Dead);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void Killing_an_attacker_does_not_ease_the_storyteller()
        {
            // The guard that matters most, and the twin of the downing side's: the death funnel runs for
            // raiders and animals too, so an unguarded hook would go easier on a town every time it won.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);
            Pawn raider = NewHuman("Raider");

            raider.health.Kill(Violence(), null);

            Assert.Equal(quiet, Adaptation.AdaptDays, 4);
        }

        [Fact]
        public void A_raid_death_is_still_charged_exactly_once()
        {
            // SettlementRaidResolver settles its battle arithmetically and kills through
            // FamilyManager.HandleDeath, which hands Kill no DamageInfo — so those deaths read as
            // non-violent here and stay the resolver's to count. Belt and braces against double-charging.
            float quiet = BuildQuietTime();
            Pawn citizen = NewHuman("Citizen");
            CivilizationOf(citizen);

            citizen.health.Kill(null, null);
            Assert.Equal(quiet, Adaptation.AdaptDays, 4);

            Adaptation.Notify_ColonistDied();
            Assert.Equal(quiet - StoryWatcher_Adaptation.DeathAdaptDaysPenalty, Adaptation.AdaptDays, 4);
        }

        // ---- the people who watched ----

        [Fact]
        public void A_killing_in_view_is_remembered_and_a_quiet_death_is_not()
        {
            CoreMap map = NewMap(40);
            Faction ours = PlayerFaction();

            Pawn victim = NewHuman("Victim");
            victim.faction = ours;
            GenSpawn.Spawn(victim, new IntVec3(5, 0, 5), map);
            Pawn witness = NewHuman("Witness");
            witness.faction = ours;
            GenSpawn.Spawn(witness, new IntVec3(6, 0, 5), map);
            Pawn farAway = NewHuman("FarAway");
            farAway.faction = ours;
            GenSpawn.Spawn(farAway, new IntVec3(35, 0, 35), map);

            Assert.Equal(1, PawnDiedThoughtsUtility.Notify_PawnDied(victim, Violence()));
            Assert.Contains(witness.needs.mood!.thoughts.memories.Memories, m => m.def == DeathThoughtDefOf.WitnessedDeathAlly);
            Assert.DoesNotContain(farAway.needs.mood!.thoughts.memories.Memories, m => m.def == DeathThoughtDefOf.WitnessedDeathAlly);

            // A death with no violence behind it leaves the room unchanged — that is the flag doing the work.
            Pawn quiet = NewHuman("Quiet");
            quiet.faction = ours;
            GenSpawn.Spawn(quiet, new IntVec3(5, 0, 6), map);
            Assert.Equal(0, PawnDiedThoughtsUtility.Notify_PawnDied(quiet, null));
            Assert.Single(witness.needs.mood.thoughts.memories.Memories, m => m.def == DeathThoughtDefOf.WitnessedDeathAlly);
        }

        [Fact]
        public void Only_their_own_peoples_deaths_are_remembered()
        {
            CoreMap map = NewMap(20);
            Faction ours = PlayerFaction();
            var theirs = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");
            Find.FactionManager.Add(theirs);

            Pawn raider = NewHuman("Raider");
            raider.faction = theirs;
            GenSpawn.Spawn(raider, new IntVec3(5, 0, 5), map);
            Pawn citizen = NewHuman("Citizen");
            citizen.faction = ours;
            GenSpawn.Spawn(citizen, new IntVec3(6, 0, 5), map);

            Assert.Equal(0, PawnDiedThoughtsUtility.Notify_PawnDied(raider, Violence()));
            Assert.DoesNotContain(citizen.needs.mood!.thoughts.memories.Memories, m => m.def == DeathThoughtDefOf.WitnessedDeathAlly);
        }

        [Fact]
        public void A_hunted_animal_is_dinner_not_a_bereavement()
        {
            CoreMap map = NewMap(20);
            Faction ours = PlayerFaction();

            var dog = new Pawn(Husky, "Dog");
            dog.faction = ours;
            GenSpawn.Spawn(dog, new IntVec3(5, 0, 5), map);
            Pawn hunter = NewHuman("Hunter");
            hunter.faction = ours;
            GenSpawn.Spawn(hunter, new IntVec3(6, 0, 5), map);

            Assert.Equal(0, PawnDiedThoughtsUtility.Notify_PawnDied(dog, Violence()));
        }

        [Fact]
        public void The_memory_runs_through_the_shipped_thought_and_its_nullifying_traits()
        {
            // Psychopath is on the shipped def's nullifyingTraits, so the giver never has to know about
            // traits at all — ThoughtHandlers refuses it. This pins that the wiring goes through the real
            // thought rather than around it.
            CoreMap map = NewMap(20);
            Faction ours = PlayerFaction();

            Pawn victim = NewHuman("Victim");
            victim.faction = ours;
            GenSpawn.Spawn(victim, new IntVec3(5, 0, 5), map);
            Pawn psychopath = NewHuman("Psychopath");
            psychopath.faction = ours;
            psychopath.story.traits.GainTrait(new Trait(Trait("Psychopath")));
            GenSpawn.Spawn(psychopath, new IntVec3(6, 0, 5), map);

            Assert.Equal(0, PawnDiedThoughtsUtility.Notify_PawnDied(victim, Violence()));
            Assert.Empty(psychopath.needs.mood!.thoughts.memories.Memories);
        }

        // ---- what the player is told ----

        [Fact]
        public void The_letter_says_what_killed_them()
        {
            Pawn citizen = NewHuman("Sera");
            citizen.faction = PlayerFaction();

            citizen.health.Kill(Violence(), null);

            Letter let = Assert.Single(Letters);
            Assert.Equal(DeathLetterDefOf.Death, let.def);
            Assert.Contains("Sera", let.label);
            // Bullet's own shipped deathMessage, which nothing in the core read until now.
            Assert.Equal(PawnUtility.FormatWithPawn(DamageDefOf.Bullet.deathMessage, citizen), let.text);
            Assert.DoesNotContain("{0}", let.text);
        }

        [Fact]
        public void A_death_with_nothing_to_blame_still_says_so()
        {
            Pawn citizen = NewHuman("Elder");
            citizen.faction = PlayerFaction();

            citizen.health.Kill(null, null);

            Letter let = Assert.Single(Letters);
            Assert.Contains("Elder", let.text);
            Assert.DoesNotContain("shot", let.text);
        }

        [Fact]
        public void A_botched_operation_reads_as_one()
        {
            Pawn patient = NewHuman("Patient");
            patient.faction = PlayerFaction();

            patient.health.Kill(new DamageInfo(SurgeryDamageDefOf.SurgicalCut, 999f), null);

            Assert.Contains("surgery", Assert.Single(Letters).text);
        }

        [Fact]
        public void Nobody_is_told_about_a_stranger()
        {
            NewHuman("Nobody").health.Kill(Violence(), null);
            Assert.Empty(Letters);
        }

        // ---- persistence ----

        [Fact]
        public void How_someone_died_survives_a_save_and_load()
        {
            Pawn citizen = NewHuman("Sera");
            citizen.health.Kill(Violence(), null);

            string xml = Scribe.SaveToString(citizen, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.True(loaded.Dead);
            Assert.True(loaded.health.DiedViolently);
            Assert.Same(DamageDefOf.Bullet, loaded.health.DeathCauseDamage);
        }
    }
}
