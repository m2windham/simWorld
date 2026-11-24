using System.Collections.Generic;
using System.Linq;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

namespace SimWorld.Tests.Factions
{
    /// <summary>
    /// Rival civilizations advance with the world (era-arc finding 5: eras buy strength, not bodies):
    /// <see cref="FactionAdvancement"/> narrows a raiding roster to the kinds of the era the world has reached,
    /// raises the faction's tech ceiling with it, and so the same points buy fewer, better-armed raiders.
    /// </summary>
    public class FactionAdvancementTests : ContentTestBase
    {
        public FactionAdvancementTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static FactionDef Def(string name) => DefDatabase<FactionDef>.GetNamed(name);

        private static EraDef Era(string name) => DefDatabase<EraDef>.GetNamed(name);

        private static Faction NewFaction(string def) => new Faction(Def(def), def, "F_" + def);

        private static void SetDay(float day) => Find.TickManager.DebugSetTicksGame((int)(day * GenDate.TicksPerDay));

        private static void AtEra(string era) => SetDay(FactionAdvancement.DayReached(Era(era)) + 1f);

        private static List<Pawn> Squad(Faction faction, float points, int seed)
        {
            Rand.Current = new RandomStream(seed);
            NameUseChecker.Clear();
            var parms = new PawnGroupMakerParms { faction = faction, groupKind = PawnGroupKindDefOf.Combat, points = points };
            return PawnGroupMakerUtility.GeneratePawns(parms);
        }

        // ---- the world's clock ----

        [Fact]
        public void The_world_reaches_each_era_in_ladder_order_on_the_pacing_intent()
        {
            List<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();
            Assert.Equal(0f, FactionAdvancement.DayReached(eras[0]));
            for (int i = 1; i < eras.Count; i++)
            {
                Assert.True(FactionAdvancement.DayReached(eras[i]) > FactionAdvancement.DayReached(eras[i - 1]),
                    eras[i].defName + " must come after " + eras[i - 1].defName);
            }

            Assert.Same(eras[0], FactionAdvancement.WorldEraAt(0));
            foreach (EraDef era in eras)
            {
                int tick = (int)(FactionAdvancement.DayReached(era) * GenDate.TicksPerDay);
                Assert.Same(era, FactionAdvancement.WorldEraAt(tick));
                if (tick > 0) Assert.NotSame(era, FactionAdvancement.WorldEraAt(tick - 1));
            }
        }

        [Fact]
        public void A_rival_advances_with_the_world_and_the_player_does_not()
        {
            Faction tribe = NewFaction("TribalCivilization");
            Faction player = NewFaction("PlayerCivilization");

            Assert.Equal(TechLevel.Neolithic, FactionAdvancement.TechLevelOf(tribe));
            AtEra("Medieval");
            Assert.Same(Era("Medieval"), FactionAdvancement.EraOf(tribe));
            Assert.Equal(TechLevel.Medieval, FactionAdvancement.TechLevelOf(tribe));

            Assert.Null(FactionAdvancement.EraOf(player));
            Assert.Equal(player.def.techLevel, FactionAdvancement.TechLevelOf(player));
        }

        [Fact]
        public void A_rivals_tech_never_falls_below_its_def()
        {
            Faction outlanders = NewFaction("OutlanderCivilization");
            Assert.Equal(TechLevel.Industrial, FactionAdvancement.TechLevelOf(outlanders));
        }

        // ---- the roster ----

        [Fact]
        public void At_the_start_a_tribe_fields_its_stone_age_band_exactly_as_before()
        {
            PawnGroupMaker maker = Def("TribalCivilization").GetGroupMaker(PawnGroupKindDefOf.Combat)!;
            List<PawnGenOption> roster = FactionAdvancement.CurrentRoster(maker.options!, NewFaction("TribalCivilization"));
            Assert.Equal(new[] { "Raider_Melee", "Raider_Bow" }, roster.Select(o => o.kind.defName));
        }

        [Theory]
        [InlineData("Bronze")]
        [InlineData("Classical")]
        [InlineData("Medieval")]
        [InlineData("Industrial")]
        public void A_rival_fields_only_the_kinds_of_the_era_the_world_has_reached(string era)
        {
            AtEra(era);
            foreach (string def in new[] { "TribalCivilization", "RoughOutlanders" })
            {
                PawnGroupMaker maker = Def(def).GetGroupMaker(PawnGroupKindDefOf.Combat)!;
                List<PawnGenOption> roster = FactionAdvancement.CurrentRoster(maker.options!, NewFaction(def));
                Assert.NotEmpty(roster);
                Assert.All(roster, o => Assert.Same(Era(era), o.kind.era));
            }
        }

        [Fact]
        public void A_roster_of_kinds_with_no_era_is_left_whole()
        {
            AtEra("Medieval");
            PawnGroupMaker maker = Def("OutlanderCivilization").GetGroupMaker(PawnGroupKindDefOf.Combat)!;
            Assert.Same(maker.options, FactionAdvancement.CurrentRoster(maker.options!, NewFaction("OutlanderCivilization")));
        }

        // ---- what a raid is ----

        [Fact]
        public void A_medieval_raid_carries_medieval_arms()
        {
            AtEra("Medieval");
            Faction tribe = NewFaction("TribalCivilization");
            List<Pawn> squad = Squad(tribe, 1000f, 404);
            Assert.NotEmpty(squad);
            foreach (Pawn raider in squad)
            {
                ThingWithComps? weapon = raider.equipment.Primary;
                Assert.NotNull(weapon);
                Assert.Same(Era("Medieval"), EraAvailability.EraOf(weapon!.def));
                Assert.Contains(raider.apparel.WornApparel, a => Armoury.IsArmour(a.def));
            }
        }

        [Fact]
        public void The_same_points_buy_fewer_better_armed_raiders_in_a_later_era()
        {
            Faction tribe = NewFaction("TribalCivilization");
            float points = 1200f;
            float prevCount = float.MaxValue;
            float prevArmour = -1f;
            foreach (string era in new[] { "SticksAndStones", "Bronze", "Classical", "Medieval" })
            {
                AtEra(era);
                int count = 0, squads = 0;
                float armour = 0f;
                for (int seed = 0; seed < 10; seed++)
                {
                    List<Pawn> squad = Squad(tribe, points, 900 + seed);
                    squads++;
                    count += squad.Count;
                    armour += squad.Sum(p => p.apparel.WornApparel.Sum(a => a.def.GetStatValueAbstract(ArmouryDefOf.ArmorRating_Sharp)));
                }
                float meanCount = count / (float)squads;
                float meanArmourPerRaider = armour / count;
                Assert.True(meanCount < prevCount, $"{era}: {meanCount} raiders per squad, not fewer than {prevCount}");
                Assert.True(meanArmourPerRaider > prevArmour, $"{era}: {meanArmourPerRaider} armour per raider, not more than {prevArmour}");
                prevCount = meanCount;
                prevArmour = meanArmourPerRaider;
            }
        }

        [Fact]
        public void Within_each_line_a_later_eras_raider_costs_more_and_carries_better_gear()
        {
            string[] melee = { "Raider_Melee", "Raider_Spearman", "Raider_Legionary", "Raider_ManAtArms" };
            string[] ranged = { "Raider_Bow", "Raider_Bowman", "Raider_Longbowman", "Raider_Crossbowman", "Raider_Rifleman" };
            AssertLine(melee, MeleeGear);
            AssertLine(ranged, RangedGear);
        }

        private static void AssertLine(string[] line, System.Func<PawnKindDef, float> gear)
        {
            for (int i = 1; i < line.Length; i++)
            {
                PawnKindDef before = DefDatabase<PawnKindDef>.GetNamed(line[i - 1]);
                PawnKindDef after = DefDatabase<PawnKindDef>.GetNamed(line[i]);
                Assert.True(after.combatPower > before.combatPower, after.defName + " must cost more than " + before.defName);
                Assert.True(gear(after) > gear(before), after.defName + " must be better armed than " + before.defName);
            }
        }

        // Weapon by its line's score plus the best armour its tags can draw: the gear a kind is priced for.
        private static float MeleeGear(PawnKindDef kind) =>
            Best(kind, d => MaterialLadder.IsMeleeWeapon(d) ? MaterialLadder.WeaponScore(d, null) : 0f) + 10f * BestArmour(kind);

        private static float RangedGear(PawnKindDef kind) =>
            Best(kind, d => Armoury.RangedScore(d)) + 10f * BestArmour(kind);

        private static float Best(PawnKindDef kind, System.Func<ThingDef, float> score) =>
            DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.IsWeapon && d.weaponTags != null && d.weaponTags.Any(kind.weaponTags!.Contains)
                    && kind.weaponMoneyRange.Includes(d.BaseMarketValue))
                .Select(score).DefaultIfEmpty(0f).Max();

        // Every armour piece (not plain clothing) the kind's tags draw, summed: a vest and a helmet count twice.
        private static float BestArmour(PawnKindDef kind) =>
            DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => Armoury.IsArmour(d) && d.apparel!.tags != null && d.apparel.tags.Any(kind.apparelTags!.Contains))
                .Sum(d => d.GetStatValueAbstract(ArmouryDefOf.ArmorRating_Sharp));

        // ---- save/load ----

        [Fact]
        public void A_saved_and_loaded_game_clock_gives_a_rival_the_same_era_and_roster()
        {
            AtEra("Classical");
            Faction tribe = NewFaction("TribalCivilization");
            EraDef? before = FactionAdvancement.EraOf(tribe);
            List<string> rosterBefore = FactionAdvancement.CurrentRoster(
                tribe.def.GetGroupMaker(PawnGroupKindDefOf.Combat)!.options!, tribe).Select(o => o.kind.defName).ToList();

            string tickXml = Scribe.SaveToString(Find.TickManager, "tickManager");
            string factionXml = Scribe.SaveToString(tribe, "faction");
            Find.TickManager = Scribe.Load<TickManager>(tickXml, "tickManager", out IReadOnlyList<string> tickErrors);
            Faction loaded = Scribe.Load<Faction>(factionXml, "faction", out IReadOnlyList<string> factionErrors);

            Assert.Empty(tickErrors);
            Assert.Empty(factionErrors);
            Assert.Same(before, FactionAdvancement.EraOf(loaded));
            Assert.Equal(rosterBefore, FactionAdvancement.CurrentRoster(
                loaded.def.GetGroupMaker(PawnGroupKindDefOf.Combat)!.options!, loaded).Select(o => o.kind.defName));
        }
    }
}
