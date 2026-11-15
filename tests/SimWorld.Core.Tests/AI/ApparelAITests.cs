using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Content;
using SimWorld.Defs;
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
    /// crafting.billcreators, the consumer half: citizens put on apparel lying on the map
    /// (<see cref="JobGiver_OptimizeApparel"/> / <see cref="JobDriver_Wear"/>) and an unarmed one takes up a
    /// weapon (<see cref="JobGiver_PickUpOpportunisticWeapon"/> / <see cref="JobDriver_Equip"/>), through the
    /// shipped humanlike think tree. The scoring constants are recalled, so the tests pin behaviour: a bare
    /// citizen dresses, a worn-out piece is traded for a whole one and never the other way round.
    /// </summary>
    public class ApparelAITests : ContentTestBase
    {
        public ApparelAITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(14, 14, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Citizen")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static ThingWithComps MakeShirt(float hitPointsFraction = 1f)
        {
            var shirt = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"), Def("Cloth"));
            shirt.HitPoints = (int)(shirt.MaxHitPoints * hitPointsFraction);
            return shirt;
        }

        [Fact]
        public void The_jobs_bind_and_the_humanlike_tree_carries_both_givers()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ApparelJobDefOf.Wear);
            Assert.NotNull(ApparelJobDefOf.Equip);
            string tree = System.IO.File.ReadAllText(System.IO.Path.Combine(
                CoreContent.DataDirectory!, "Core", "Defs", "ThinkTreeDefs", "ThinkTrees_Humanlike.xml"));
            Assert.Contains("SimWorld.AI.JobGiver_OptimizeApparel", tree);
            Assert.Contains("SimWorld.AI.JobGiver_PickUpOpportunisticWeapon", tree);
        }

        [Fact]
        public void A_bare_citizen_walks_to_a_shirt_and_puts_it_on()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1));
            ThingWithComps shirt = MakeShirt();
            GenSpawn.Spawn(shirt, new IntVec3(10, 0, 10), map);

            RunTicks(1500, citizen);

            Assert.Contains(shirt, citizen.apparel.WornApparel);
            Assert.False(shirt.Spawned);
        }

        [Fact]
        public void A_worn_out_shirt_is_traded_for_a_whole_one_and_dropped()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1));
            ThingWithComps rag = MakeShirt(0.1f);
            citizen.apparel.Wear(rag);
            ThingWithComps whole = MakeShirt();
            GenSpawn.Spawn(whole, new IntVec3(10, 0, 10), map);

            RunTicks(1500, citizen);

            Assert.Contains(whole, citizen.apparel.WornApparel);
            Assert.DoesNotContain(rag, citizen.apparel.WornApparel);
            Assert.True(rag.Spawned, "the replaced piece is dropped where the citizen stood");
        }

        [Fact]
        public void A_citizen_never_trades_down_and_waits_out_the_cooldown_before_looking_again()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1));
            ThingWithComps whole = MakeShirt();
            citizen.apparel.Wear(whole);
            ThingWithComps rag = MakeShirt(0.1f);
            GenSpawn.Spawn(rag, new IntVec3(10, 0, 10), map);

            Assert.True(JobGiver_OptimizeApparel.ApparelScoreGain(citizen, rag) < JobGiver_OptimizeApparel.MinScoreGainToCare);
            RunTicks(1500, citizen);

            Assert.Contains(whole, citizen.apparel.WornApparel);
            Assert.True(rag.Spawned);
            int next = citizen.mindState.nextApparelOptimizeTick;
            Assert.InRange(next - Find.TickManager.TicksGame, 1,
                JobGiver_OptimizeApparel.ApparelOptimizeCheckIntervalMax);
        }

        [Fact]
        public void Covering_bare_skin_with_a_worn_out_piece_beats_upgrading_a_worn_out_shirt()
        {
            // RimWorld's x10 for apparel that replaces nothing: a citizen in a rag of a shirt and no trousers
            // wants even tatty trousers more than a whole shirt.
            Pawn citizen = NewHuman("HalfDressed");
            citizen.apparel.Wear(MakeShirt(0.1f));
            var tattyPants = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Pants"), Def("Cloth"));
            tattyPants.HitPoints = (int)(tattyPants.MaxHitPoints * 0.3f);

            float pants = JobGiver_OptimizeApparel.ApparelScoreGain(citizen, tattyPants);
            float shirt = JobGiver_OptimizeApparel.ApparelScoreGain(citizen, MakeShirt());

            Assert.True(shirt >= JobGiver_OptimizeApparel.MinScoreGainToCare);
            Assert.True(pants > shirt, "pants " + pants + " vs shirt " + shirt);
        }

        [Fact]
        public void A_barely_better_shirt_is_not_worth_the_walk()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1));
            ThingWithComps worn = MakeShirt(0.51f);
            citizen.apparel.Wear(worn);
            ThingWithComps whole = MakeShirt();
            GenSpawn.Spawn(whole, new IntVec3(10, 0, 10), map);

            float gain = JobGiver_OptimizeApparel.ApparelScoreGain(citizen, whole);
            Assert.InRange(gain, 0.0001f, JobGiver_OptimizeApparel.MinScoreGainToCare - 0.0001f);
            RunTicks(1500, citizen);

            Assert.Contains(worn, citizen.apparel.WornApparel);
            Assert.True(whole.Spawned);
        }

        [Fact]
        public void An_unarmed_citizen_takes_up_a_knife_across_the_map_and_an_armed_one_leaves_it()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1));
            var knife = (ThingWithComps)ThingMaker.MakeThing(Def("MeleeWeapon_Knife"));
            GenSpawn.Spawn(knife, new IntVec3(12, 0, 12), map);

            RunTicks(1500, citizen);

            Assert.Same(knife, citizen.equipment.Primary);
            Assert.False(knife.Spawned);

            var second = (ThingWithComps)ThingMaker.MakeThing(Def("MeleeWeapon_Knife"));
            GenSpawn.Spawn(second, new IntVec3(12, 0, 12), map);
            RunTicks(1500, citizen);

            Assert.Same(knife, citizen.equipment.Primary);
            Assert.True(second.Spawned);
        }

        [Fact]
        public void A_citizen_who_cannot_do_violence_leaves_the_knife()
        {
            CoreMap map = NewMap();
            Pawn pacifist = SpawnHuman(map, new IntVec3(1, 0, 1), "Pacifist");
            pacifist.story.adulthood = DefDatabase<BackstoryDef>.GetNamed("TribalElder");
            var knife = (ThingWithComps)ThingMaker.MakeThing(Def("MeleeWeapon_Knife"));
            GenSpawn.Spawn(knife, new IntVec3(12, 0, 12), map);

            RunTicks(1500, pacifist);

            Assert.Null(pacifist.equipment.Primary);
            Assert.True(knife.Spawned);
        }

        [Fact]
        public void The_apparel_cooldown_round_trips_through_Scribe()
        {
            Pawn p = NewHuman("Saved");
            p.mindState.nextApparelOptimizeTick = 4321;

            string xml = Scribe.SaveToString(p, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(4321, loaded.mindState.nextApparelOptimizeTick);
        }
    }
}
