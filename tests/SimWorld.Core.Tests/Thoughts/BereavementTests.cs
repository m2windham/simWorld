using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;

using Xunit;

namespace SimWorld.Tests.Thoughts
{
    /// <summary>
    /// A settlement feels it when one of its own dies, including the deaths nobody was there to see.
    ///
    /// <para/><b>Found by measuring, not by reading.</b> A probe run showed an unwatched settlement losing a
    /// citizen to a manhunter pack — the ablation put that death squarely on the pack — while its mood swing
    /// stayed 0.08 against a watched settlement's 0.21. Somebody died of violence and nothing moved.
    ///
    /// <para/><b>Three gates, any one of them fatal.</b> An abstractly-resolved death reaches
    /// <c>PawnDiedThoughtsUtility</c> with no <c>DamageInfo</c> (so it reads as non-violent and returns at
    /// the first line), on a victim that was never spawned (so it has no map), with no spawned witnesses to
    /// stand near it. The first test here reproduces exactly that and asserts the witness path still, quite
    /// correctly, gives nothing — because the fix is not to loosen that gate.
    ///
    /// <para/>The witness gate is right: age, disease and a surgery that went wrong should not traumatise a
    /// room. What was missing is the memory that was never about violence in the first place.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BereavementTests : ContentTestBase
    {
        public BereavementTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            CorpseDefGenerator.EnsureGenerated();
        }

        private static ThoughtDef Known => DefDatabase<ThoughtDef>.GetNamed("KnowColonistDied");

        private static ThoughtDef Witnessed => DefDatabase<ThoughtDef>.GetNamed("WitnessedDeathAlly");

        /// <summary>Registers a civilization holding these pawns — the roster that decides who counts as one
        /// of ours, and therefore who mourns.</summary>
        private static void CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
        }

        private static int MemoryCount(Pawn pawn, ThoughtDef def) =>
            pawn.needs?.mood?.thoughts?.memories?.Memories.Count(m => ReferenceEquals(m.def, def)) ?? 0;

        // ---- the hole, reproduced ----

        /// <summary>
        /// The exact shape of the abstract kill: no damage, no map, nobody spawned. Asserts the witness path
        /// gives nothing — which is correct and stays correct — and that bereavement covers it anyway.
        /// </summary>
        [Fact]
        public void A_death_nobody_saw_is_still_felt_by_the_people_who_knew_them()
        {
            Pawn victim = NewHuman("Lost");
            Pawn a = NewHuman("Mourner");
            Pawn b = NewHuman("AlsoMourner");
            CivilizationOf(victim, a, b);

            // Exactly what SettlementRaidResolver -> FamilyManager.HandleDeath does: no DamageInfo at all.
            victim.health.Kill(null, null, DeathCause.Injury);

            Assert.Equal(0, MemoryCount(a, Witnessed));   // the witness path is right to say nothing
            Assert.Equal(1, MemoryCount(a, Known));
            Assert.Equal(1, MemoryCount(b, Known));
        }

        [Fact]
        public void The_dead_do_not_mourn_themselves()
        {
            Pawn victim = NewHuman("Lost");
            Pawn other = NewHuman("Mourner");
            CivilizationOf(victim, other);

            victim.health.Kill(null, null, DeathCause.Injury);

            Assert.Equal(0, MemoryCount(victim, Known));
            Assert.Equal(1, MemoryCount(other, Known));
        }

        // ---- who counts ----

        [Fact]
        public void A_stranger_dying_is_not_a_bereavement()
        {
            Pawn ours = NewHuman("Ours");
            Pawn stranger = NewHuman("Stranger");
            CivilizationOf(ours);   // stranger deliberately outside the roster

            stranger.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);

            Assert.Equal(0, MemoryCount(ours, Known));
        }

        /// <summary>
        /// Bereavement is not gated on violence, and that is the difference between it and trauma. A
        /// settlement mourns whoever it lost, however they went.
        /// </summary>
        [Fact]
        public void A_quiet_death_is_mourned_the_same_as_a_violent_one()
        {
            Pawn old = NewHuman("Elder");
            Pawn mourner = NewHuman("Mourner");
            CivilizationOf(old, mourner);

            old.health.Kill(null, null, DeathCause.Age);

            Assert.Equal(1, MemoryCount(mourner, Known));
        }

        [Fact]
        public void An_animal_dying_is_not_one_of_us()
        {
            Pawn person = NewHuman("Person");
            var dog = new Pawn(Husky, "Dog");
            CivilizationOf(person, dog);

            dog.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);

            Assert.Equal(0, MemoryCount(person, Known));
        }

        // ---- the two memories are different things ----

        /// <summary>
        /// Somebody who was there gets both: they saw it, and they knew them. Stacking is the point — the
        /// witness memory is the extra, not a replacement, which is why the two defs have different durations
        /// and different nullifying traits.
        /// </summary>
        [Fact]
        public void Somebody_who_was_there_carries_both_memories()
        {
            var map = new global::SimWorld.Map.Map(20, 20, global::SimWorld.Map.TerrainDefOf.Soil);
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Find.FactionManager.Add(faction);

            Pawn victim = NewHuman("Lost");
            Pawn witness = NewHuman("Saw");
            victim.faction = faction;
            witness.faction = faction;
            CivilizationOf(victim, witness);

            GenSpawn.Spawn(victim, new global::SimWorld.Map.IntVec3(5, 0, 5), map);
            GenSpawn.Spawn(witness, new global::SimWorld.Map.IntVec3(6, 0, 5), map);

            victim.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);

            Assert.Equal(1, MemoryCount(witness, Witnessed));
            Assert.Equal(1, MemoryCount(witness, Known));
        }

        /// <summary>
        /// The claim the probe could not see, tested where it can be: an <b>unspawned</b> citizen's actual
        /// mood level falls after the settlement loses somebody. Granting a memory is not the same as the
        /// settlement feeling it — <c>Need_Seeker.CurInstantLevel</c> is derived from
        /// <c>thoughts.TotalMoodOffset()</c>, and a coarse-tier citizen only folds its memories in on
        /// <c>NeedIntervalBulk</c>, so this walks the whole path from death to a number a rollup would read.
        ///
        /// <para/>A probe run over fourteen days showed no change from this fix, which was a limitation of
        /// the readout rather than of the mechanism: one KnowColonistDied is -3 mood, i.e. -0.03 on the 0..1
        /// scale the probe reports, and that dip lands inside an existing peak-to-trough range of 0.08
        /// without widening it. Peak-to-trough is the wrong statistic for a single death. This is the right
        /// one.
        /// </summary>
        [Fact]
        public void An_unspawned_citizen_mood_actually_falls_when_the_settlement_loses_somebody()
        {
            Pawn survivor = NewHuman("Survivor");
            Pawn lost = NewHuman("Lost");
            CivilizationOf(survivor, lost);

            Assert.False(survivor.Spawned, "the whole point is a citizen nobody is watching");

            survivor.needs.mood!.NeedIntervalBulk(GenTicks.TickLongInterval);
            float before = survivor.needs.mood.CurLevelPercentage;

            lost.health.Kill(null, null, DeathCause.Injury);
            survivor.needs.mood.NeedIntervalBulk(GenTicks.TickLongInterval);

            Assert.True(
                survivor.needs.mood.CurLevelPercentage < before,
                $"mood was {before:F4} before the death and {survivor.needs.mood.CurLevelPercentage:F4} after");
        }

        [Fact]
        public void Losing_several_people_weighs_more_than_losing_one()
        {
            Pawn survivor = NewHuman("Survivor");
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            CivilizationOf(survivor, a, b);

            a.health.Kill(null, null, DeathCause.Injury);
            float afterOne = survivor.needs.mood!.thoughts.TotalMoodOffset();

            b.health.Kill(null, null, DeathCause.Injury);
            float afterTwo = survivor.needs.mood!.thoughts.TotalMoodOffset();

            // A band rather than a literal: the def's stackedEffectMultiplier is tuning, and the claim is
            // that a second loss lands at all rather than that it lands at any particular weight.
            Assert.True(afterTwo < afterOne, "a second death must weigh on somebody who already lost one");
        }
    }
}
