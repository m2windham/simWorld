using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// What an animal's blow is, and where any melee blow lands (lane: post-raid bleed). The port gave every
    /// animal one synthetic "teeth" tool — power 10 x body size, always a bite — so a muffalo, whose real
    /// attacks are a head, four leg strikes and a bite it rarely uses, bit every time, and every bite is a
    /// wound that can go septic where a bruise cannot. And a melee blow could pick an internal organ outright.
    /// The figures asserted are sourced (the species pages on the RimWorld wiki, cited in
    /// <c>Races_Animal.xml</c>) or are properties of the weighting RimWorld uses.
    /// </summary>
    public class AnimalBiteTests : ContentTestBase
    {
        public AnimalBiteTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Muffalo => Def("Muffalo");

        private static ThingDef Chicken => Def("Chicken");

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Beast")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>The damage def each of N picks of the race's native melee verb would deal.</summary>
        private static Dictionary<string, int> SampleBlows(ThingDef race, int n)
        {
            var counts = new Dictionary<string, int>();
            var animal = new Pawn(race, "Sample");
            for (int i = 0; i < n; i++)
            {
                Verb_MeleeAttack verb = AttackVerbUtility.TryGetMeleeVerb(animal)!;
                string kind = verb.verbProps.meleeDamageDef!.defName;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
            return counts;
        }

        // ---- the tools ----

        [Fact]
        public void The_shipped_animals_declare_natural_tools_and_each_one_resolves_to_a_maneuver()
        {
            foreach (ThingDef race in new[] { Muffalo, Chicken, Husky })
            {
                Assert.NotNull(race.tools);
                Assert.NotEmpty(race.tools!);
                foreach (Tool tool in race.tools!)
                {
                    Assert.True(tool.power > 0f);
                    Assert.NotNull(ManeuverUtility.FindManeuver(tool));
                }
            }
        }

        [Fact]
        public void A_muffalos_blows_are_mostly_blunt_and_only_sometimes_a_bite()
        {
            // The muffalo's bite is a 0.5-factor tool next to a 13-power head and four 10-power leg strikes,
            // under RimWorld's squared weight: about one blow in twelve. The port made every one a bite.
            const int n = 3000;
            Dictionary<string, int> blows = SampleBlows(Muffalo, n);
            float bites = blows.GetValueOrDefault("Bite") / (float)n;
            float blunt = blows.GetValueOrDefault("Blunt") / (float)n;

            Assert.InRange(bites, 0.04f, 0.14f);
            Assert.True(blunt > 0.8f, $"expected a muffalo's blows to be mostly blunt, got {blunt:P0}");
            Assert.True(blows.Count >= 2, "more than one kind of blow in the mix");
        }

        [Fact]
        public void A_chickens_blows_are_small_and_a_huskys_are_mixed_between_claws_and_teeth()
        {
            // A chicken cannot hurt like a muffalo or a dog...
            Assert.True(Chicken.tools!.Max(t => t.power) < Muffalo.tools!.Min(t => t.power));
            Assert.True(Chicken.tools!.Max(t => t.power) < Husky.tools!.Min(t => t.power));

            // ...and a husky claws as well as bites: neither all of its blows.
            Dictionary<string, int> blows = SampleBlows(Husky, 2000);
            Assert.True(blows.GetValueOrDefault("Bite") > 200, "teeth are used");
            Assert.True(blows.GetValueOrDefault("Cut") > 200, "claws are used (scratch is a cut here)");
        }

        [Fact]
        public void A_tool_that_hits_twice_as_hard_is_four_times_as_likely_to_be_chosen()
        {
            var weak = new Tool { power = 5f, chanceFactor = 1f };
            var strong = new Tool { power = 10f, chanceFactor = 1f };
            var shy = new Tool { power = 10f, chanceFactor = 0.5f };
            Assert.Equal(4f, AttackVerbUtility.NativeToolWeight(strong) / AttackVerbUtility.NativeToolWeight(weak), 3);
            Assert.Equal(0.5f, AttackVerbUtility.NativeToolWeight(shy) / AttackVerbUtility.NativeToolWeight(strong), 3);
        }

        [Fact]
        public void Asking_whether_a_pawn_can_fight_spends_no_random_numbers_and_agrees_with_actually_picking()
        {
            var muffalo = new Pawn(Muffalo, "Beast");
            Pawn human = NewHuman("Person");

            Rand.Current = new RandomStream(4242);
            Assert.True(AttackVerbUtility.CanMeleeAttack(muffalo));
            Assert.True(AttackVerbUtility.CanMeleeAttack(human));
            float afterAsking = Rand.Value;
            float fresh = new RandomStream(4242).Value;
            Assert.Equal(fresh, afterAsking);

            Assert.NotNull(AttackVerbUtility.TryGetMeleeVerb(muffalo));
        }

        [Fact]
        public void A_race_with_no_tools_still_falls_back_to_the_synthesised_natural_weapon()
        {
            // The fists of a humanlike are still the fallback, and still a Blunt blow.
            Pawn human = NewHuman("Person");
            Verb_MeleeAttack verb = AttackVerbUtility.TryGetMeleeVerb(human)!;
            Assert.Equal("Blunt", verb.verbProps.meleeDamageDef!.defName);
        }

        // ---- a fight uses the whole kit ----

        [Fact]
        public void A_fighting_animal_re_chooses_its_tool_as_RimWorld_does_so_one_fight_mixes_claws_and_teeth()
        {
            // Before: the verb was built once per job and swung until the job ended, so a husky that picked
            // its teeth bit for the whole fight. RimWorld re-chooses every 60 ticks (Pawn_MeleeVerbs).
            CoreMap map = NewMap(12, 12);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(5, 0, 5), "Husky");
            Pawn victim = SpawnAnimal(map, Muffalo, new IntVec3(6, 0, 5), "Victim"); // a lot of animal to chew on
            husky.mindState.angryAt = victim;
            husky.mindState.angryUntilTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;

            RunTicks(1500, husky, victim);

            var kinds = victim.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => h.def.defName).Distinct().ToList();
            Assert.True(victim.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count() >= 4, "the husky really did fight");
            Assert.True(kinds.Count >= 2, "expected claws and teeth in one fight, saw: " + string.Join(",", kinds));
        }

        // ---- where a blow lands ----

        [Fact]
        public void A_melee_blow_lands_on_the_outside_of_the_body_never_on_an_organ()
        {
            // 1500 swings, each at a fresh downed target (a downed pawn cannot dodge, so every swing that is
            // not a plain miss lands). Before: BodyPartDepth was left open and a share of blows chose a lung, a
            // kidney or the heart directly.
            int landed = 0;
            int inside = 0;
            for (int i = 0; i < 1500; i++)
            {
                Pawn attacker = NewHuman("Attacker");
                Pawn target = NewHuman("Target");
                target.health.ForceDowned = true;

                Verb_MeleeAttack verb = AttackVerbUtility.TryGetMeleeVerb(attacker)!;
                Assert.True(verb.TryStartCastOn(target, 1f));
                verb.VerbTick();

                MeleeAttackResult? result = verb.LastResult;
                if (result?.Outcome != MeleeAttackOutcome.Hit) continue;
                landed++;
                if (result.HitPart!.depth != BodyPartDepth.Outside) inside++;
            }

            Assert.True(landed > 500, "enough swings landed to say anything: " + landed);
            Assert.Equal(0, inside);
        }
    }
}
