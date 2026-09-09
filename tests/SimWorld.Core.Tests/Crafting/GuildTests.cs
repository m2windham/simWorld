using System.Collections.Generic;
using System.Linq;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;
using Xunit;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// Guilds: RimWorld's workbench bill queue moved up to a settlement (<c>crafting.guilds</c>). The bills
    /// themselves are the Crafting module's own and already tested; what is pinned here is the translation —
    /// members come from a role, labour comes from their skill, ingredients come out of the settlement's
    /// stores and products go back into them, and a guild that cannot get materials waits.
    /// </summary>
    [Collection("GlobalDefs")]
    public class GuildTests : ContentTestBase
    {
        public GuildTests(CoreContentFixture content) : base(content)
        {
        }

        private static Settlement Town(string name = "Stoneford", int tile = 11) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, name, 0);

        private static RecipeDef Blocks => DefDatabase<RecipeDef>.GetNamed("Make_Blocks_Sandstone");

        private static ThingDef Chunk => DefDatabase<ThingDef>.GetNamed("ChunkSandstone");

        private static ThingDef BlocksProduct => Blocks.products![0].thingDef;

        /// <summary>The masons' recipe is gated on Masonry; a guild will not work an unresearched recipe, so
        /// every test that wants output has to give the civilization the knowledge first.</summary>
        private static void KnowMasonry() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));

        private Pawn Mason(Settlement settlement, int craftingLevel)
        {
            Pawn pawn = NewHuman();
            pawn.workSettings.SetRole(GuildDefOf.MasonsGuild.role);
            SkillRecord? skill = pawn.skills.GetSkill(GuildDefOf.MasonsGuild.skill);
            if (skill != null) skill.Level = craftingLevel;
            settlement.AddCitizen(pawn);
            return pawn;
        }

        private static Guild GuildFor(Settlement settlement, int repeatCount = 100)
        {
            var guild = new Guild(GuildDefOf.MasonsGuild, settlement);
            guild.Bills.AddBill(new Bill_Production(Blocks)
            {
                repeatMode = BillRepeatMode.RepeatCount,
                repeatCount = repeatCount,
            });
            return guild;
        }

        [Fact]
        public void The_shipped_guild_is_staffed_by_the_role_it_names_and_nobody_else()
        {
            Settlement town = Town();
            Guild guild = GuildFor(town);

            Mason(town, 5);
            Pawn stranger = NewHuman();
            town.AddCitizen(stranger);

            Assert.Single(guild.Members);
            Assert.DoesNotContain(stranger, guild.Members);
        }

        [Fact]
        public void A_guild_turns_the_settlements_chunks_into_the_settlements_blocks()
        {
            KnowMasonry();
            Settlement town = Town();
            Guild guild = GuildFor(town);
            Mason(town, 20);
            town.AddStore(Chunk, 5);

            int before = town.StoreCountOf(BlocksProduct);

            // A batch of blocks costs more labour than one interval buys, so the guild banks work across a
            // few of them — which is the point of WorkAccumulated. Two working days is generous room.
            int iterations = 0;
            for (int i = 0; i < 60 && iterations == 0; i++) iterations += guild.GuildTick(i * 2000);

            Assert.True(iterations > 0, "a staffed guild with materials and knowledge produced nothing");
            Assert.True(town.StoreCountOf(BlocksProduct) > before, "the products never reached the settlement");
            Assert.True(town.StoreCountOf(Chunk) < 5, "the ingredients were never consumed");
        }

        [Fact]
        public void Without_materials_a_guild_waits_rather_than_conjuring_them()
        {
            KnowMasonry();
            Settlement town = Town();
            Guild guild = GuildFor(town);
            Mason(town, 20);

            for (int i = 0; i < 60; i++) Assert.Equal(0, guild.GuildTick(i * 2000));
            Assert.Equal(0, town.StoreCountOf(BlocksProduct));
            Assert.Equal(0, town.StoreCountOf(Chunk));
            Assert.Equal(0f, guild.WorkAccumulated);
        }

        [Fact]
        public void A_recipe_the_civilization_has_not_researched_yet_is_not_worked()
        {
            Settlement town = Town();
            Guild guild = GuildFor(town);
            Mason(town, 20);
            town.AddStore(Chunk, 10);

            // No KnowMasonry() — the knowledge is the gate, not the materials or the people.
            for (int i = 0; i < 60; i++) Assert.Equal(0, guild.GuildTick(i * 2000));
            Assert.Equal(10, town.StoreCountOf(Chunk));
        }

        [Fact]
        public void With_nobody_in_the_guild_nothing_is_made()
        {
            KnowMasonry();
            Settlement town = Town();
            Guild guild = GuildFor(town);
            town.AddStore(Chunk, 10);

            Assert.Equal(0f, guild.WorkThisInterval(0));
            for (int i = 0; i < 60; i++) Assert.Equal(0, guild.GuildTick(i * 2000));
        }

        [Fact]
        public void More_members_and_better_ones_both_mean_more_work()
        {
            Settlement small = Town("Small", 1);
            Guild smallGuild = GuildFor(small);
            Mason(small, 5);

            Settlement crowded = Town("Crowded", 2);
            Guild crowdedGuild = GuildFor(crowded);
            Mason(crowded, 5);
            Mason(crowded, 5);

            Settlement skilled = Town("Skilled", 3);
            Guild skilledGuild = GuildFor(skilled);
            Mason(skilled, 20);

            float one = smallGuild.WorkThisInterval(0);
            Assert.True(crowdedGuild.WorkThisInterval(0) > one, "a second member added no labour");
            Assert.True(skilledGuild.WorkThisInterval(0) > one, "a master worked no faster than an apprentice");
        }

        [Fact]
        public void The_statistical_cohort_works_too_and_never_more_of_it_than_the_town_has()
        {
            Settlement town = Town();
            town.AddStatisticalPeople(40);
            Guild guild = GuildFor(town);

            Assert.Equal(0f, guild.WorkThisInterval(0));

            guild.StatisticalMembers = 25;
            float withCohort = guild.WorkThisInterval(0);
            Assert.True(withCohort > 0f, "an assigned statistical cohort contributed no labour");

            // Ask for more members than the settlement has and the answer is what it has, not what was asked.
            guild.StatisticalMembers = 400;
            Assert.Equal(40, guild.StatisticalMembers);
        }

        [Fact]
        public void The_cohorts_sampled_skill_is_the_same_every_time_for_the_same_town_and_tick()
        {
            Settlement town = Town();
            town.AddStatisticalPeople(100);
            Guild guild = GuildFor(town);
            guild.StatisticalMembers = 100;

            Assert.Equal(guild.WorkThisInterval(4000), guild.WorkThisInterval(4000), 3);
        }

        [Fact]
        public void A_target_count_bill_reads_the_settlements_own_stores_and_stops_when_it_is_met()
        {
            KnowMasonry();
            Settlement town = Town();
            var guild = new Guild(GuildDefOf.MasonsGuild, town);
            var bill = new Bill_Production(Blocks)
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = 5,
            };
            guild.Bills.AddBill(bill);
            Mason(town, 20);
            town.AddStore(Chunk, 50);

            // The granary already holds more than the order asks for, so the masons do not start.
            town.AddStore(BlocksProduct, 50);
            Assert.Equal(50, guild.CountProducts(bill));
            for (int i = 0; i < 60; i++) Assert.Equal(0, guild.GuildTick(i * 2000));
        }

        [Fact]
        public void A_suspended_bill_is_not_worked()
        {
            KnowMasonry();
            Settlement town = Town();
            Guild guild = GuildFor(town);
            guild.Bills[0].suspended = true;
            Mason(town, 20);
            town.AddStore(Chunk, 10);

            for (int i = 0; i < 60; i++) Assert.Equal(0, guild.GuildTick(i * 2000));
        }

        [Fact]
        public void The_shipped_guild_only_lists_recipes_it_may_actually_work()
        {
            GuildDef def = GuildDefOf.MasonsGuild;
            Assert.NotEmpty(def.recipes);
            Assert.True(def.Allows(Blocks));
            Assert.All(def.recipes, r => Assert.NotEmpty(r.products!));

            // ConfigErrors is what keeps an unworkable guild out of content; the content test asserts the load
            // is clean, and this asserts the rule that makes that meaningful is actually being applied.
            Assert.Empty(def.ConfigErrors());
        }

        [Fact]
        public void Guilds_survive_a_save_and_reload_with_their_bills_and_their_settlement()
        {
            KnowMasonry();
            Settlement town = Town();
            var manager = new GuildManager();
            Guild guild = manager.Establish(GuildDefOf.MasonsGuild, town);
            guild.StatisticalMembers = 7;
            guild.Bills.AddBill(new Bill_Production(Blocks)
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = 42,
            });

            string xml = Scribe.SaveToString(manager, "guilds");
            GuildManager loaded = Scribe.Load<GuildManager>(xml, "guilds", Content.Database);

            Guild reloaded = Assert.Single(loaded.Guilds);
            Assert.Same(GuildDefOf.MasonsGuild, reloaded.Def);
            Assert.Equal(7, reloaded.StatisticalMembers);
            Bill_Production bill = Assert.IsType<Bill_Production>(Assert.Single(reloaded.Bills.Bills));
            Assert.Same(Blocks, bill.recipe);
            Assert.Equal(42, bill.targetCount);

            // The settlement is matched back by world tile, since a WorldObject cannot be saved by reference.
            Assert.Null(reloaded.Settlement);
            var world = new SimWorld.World.World();
            world.worldObjects.Add(town);
            loaded.ResolveSettlements(world);
            Assert.Same(town, reloaded.Settlement);
        }

        [Fact]
        public void The_manager_finds_the_guilds_of_one_settlement_among_several()
        {
            Settlement a = Town("A", 1);
            Settlement b = Town("B", 2);
            var manager = new GuildManager();
            Guild inA = manager.Establish(GuildDefOf.MasonsGuild, a);
            manager.Establish(GuildDefOf.MasonsGuild, b);

            Assert.Same(inA, Assert.Single(manager.GuildsIn(a)));
            Assert.Equal(2, manager.Guilds.Count);
        }
    }
}
