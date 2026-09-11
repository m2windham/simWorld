using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

namespace SimWorld.Tests.Factions
{
    /// <summary>
    /// The <see cref="FactionDef"/> flags that decide when a civilization may raid and how far it will go:
    /// <see cref="FactionDef.earliestRaidDays"/>, <see cref="FactionDef.autoFlee"/> and
    /// <see cref="FactionDef.canMakeRandomly"/>, plus <see cref="PawnKindDef.isFighter"/> on the squad it
    /// fields. All four were set by shipped content and read by no line of <c>src/</c>.
    ///
    /// <para/>Nothing here asserts a tuning literal: each is pinned as an ordering, a band or a round trip,
    /// per CLAUDE.md, because none of the underlying numbers could be sourced from RimWorld in this sandbox.
    /// </summary>
    public class FactionRaidRulesTests : ContentTestBase
    {
        public FactionRaidRulesTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        private static FactionDef Def(string defName) => DefDatabase<FactionDef>.GetNamed(defName);

        private static Faction NewFaction(string defName, string name) => new Faction(Def(defName), name, "F_" + name);

        private static void SetDay(int day) => Find.TickManager.DebugSetTicksGame(day * GenDate.TicksPerDay);

        // ---- earliestRaidDays ----

        [Fact]
        public void The_shipped_raiders_disagree_about_how_long_they_wait()
        {
            // The ordering the rest of this section rests on, read off content rather than restated: the
            // permanent enemy does not wait, the two settled civilizations do, and they do not wait equally.
            Assert.Equal(0, Def("RoughOutlanders").earliestRaidDays);
            Assert.True(Def("TribalCivilization").earliestRaidDays > 0);
            Assert.True(Def("OutlanderCivilization").earliestRaidDays > Def("TribalCivilization").earliestRaidDays);
        }

        [Fact]
        public void A_faction_may_not_raid_before_its_own_earliest_day_and_may_after()
        {
            Faction tribal = NewFaction("TribalCivilization", "Tribal");
            Faction rough = NewFaction("RoughOutlanders", "Rough");

            SetDay(0);
            Assert.False(FactionRaidRules.CanRaidYet(tribal));
            Assert.True(FactionRaidRules.CanRaidYet(rough), "a faction that declares day 0 is never held back");

            SetDay(tribal.def.earliestRaidDays);
            Assert.True(FactionRaidRules.CanRaidYet(tribal));
        }

        [Fact]
        public void A_brand_new_civilization_is_not_raided_by_a_faction_whose_content_says_it_waits()
        {
            // The defect in one fixture: on day zero the only hostile is a civilization whose own def says it
            // has not started raiding. Before this was read, the raid fired anyway.
            Faction player = NewFaction("PlayerCivilization", "Player");
            Faction tribal = NewFaction("TribalCivilization", "Tribal");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            player.SetRelationDirect(tribal, FactionRelationKind.Hostile, -100);

            var target = new CivilizationTarget();
            var parms = new IncidentParms { target = target, points = 400f };

            SetDay(0);
            Assert.False(IncidentDefOf.RaidEnemy.Worker.CanFireNow(parms));

            SetDay(tribal.def.earliestRaidDays);
            Assert.True(IncidentDefOf.RaidEnemy.Worker.CanFireNow(parms));
        }

        [Fact]
        public void A_faction_that_does_not_wait_can_raid_on_day_zero()
        {
            // The other half, and the reason this is a gate rather than a blanket early-game grace period:
            // RoughOutlanders are permanentEnemy and declare day 0, so a world that has one is raidable
            // immediately. The storyteller's own pacing is what holds the first raid back for the other two.
            Faction player = NewFaction("PlayerCivilization", "Player");
            Faction rough = NewFaction("RoughOutlanders", "Rough");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rough);
            player.SetRelationDirect(rough, FactionRelationKind.Hostile, -100);

            SetDay(0);
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 400f };
            Assert.True(IncidentDefOf.RaidEnemy.Worker.CanFireNow(parms));
        }

        [Fact]
        public void A_caller_that_pins_the_faction_still_gets_it()
        {
            // Selection is what the day gate narrows, exactly as RimWorld's does. A caller naming a faction
            // outright (a test, a forced incident, a quest) is overriding selection, not asking for it, and
            // a gate that silently refused such a call would make forcing a raid unreliable.
            Faction player = NewFaction("PlayerCivilization", "Player");
            Faction tribal = NewFaction("TribalCivilization", "Tribal");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            player.SetRelationDirect(tribal, FactionRelationKind.Hostile, -100);

            SetDay(0);
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 400f, faction = tribal };
            Assert.True(IncidentDefOf.RaidEnemy.Worker.CanFireNow(parms));
        }

        [Fact]
        public void Randy_is_the_storyteller_the_gate_exists_for()
        {
            // Evidence for the claim in FactionRaidRules.CanRaidYet's own doc, read off shipped content: two
            // of the three storytellers hold ThreatBig back past every shipped earliestRaidDays on their own,
            // and the third declares no day floor anywhere — so for Randy the faction's own flag was the only
            // thing that could have provided it.
            int latestShippedRaidDay = DefDatabase<FactionDef>.AllDefsListForReading.Max(d => d.earliestRaidDays);

            foreach (string name in new[] { "Cassandra_Classic", "Phoebe_Chillax" })
            {
                StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(name);
                float floor = def.comps!.OfType<StorytellerCompProperties_OnOffCycle>()
                    .Where(c => ReferenceEquals(c.category, IncidentCategoryDefOf.ThreatBig))
                    .Min(c => c.minDaysPassed);
                Assert.True(floor > latestShippedRaidDay, name + " does not out-wait the latest earliestRaidDays");
            }

            StorytellerDef randy = DefDatabase<StorytellerDef>.GetNamed("Randy_Random");
            Assert.Empty(randy.comps!.OfType<StorytellerCompProperties_OnOffCycle>());
            Assert.Contains(
                randy.comps!.OfType<StorytellerCompProperties_RandomMain>().SelectMany(c => c.categoryWeights!),
                e => ReferenceEquals(e.category, IncidentCategoryDefOf.ThreatBig));
        }

        // ---- autoFlee ----

        [Fact]
        public void A_band_that_flees_cannot_lose_as_much_of_itself_as_one_that_does_not()
        {
            Faction flees = NewFaction("TribalCivilization", "Tribal");     // autoFlee true
            Faction stays = NewFaction("RoughOutlanders", "Rough");          // autoFlee false
            Assert.True(flees.def.autoFlee);
            Assert.False(stays.def.autoFlee);

            const int Band = 12;
            int fleeing = FactionRaidRules.MaxRaidersLost(flees, Band);
            int standing = FactionRaidRules.MaxRaidersLost(stays, Band);

            Assert.Equal(Band, standing);                 // fights to the last raider
            Assert.InRange(fleeing, 1, Band - 1);         // breaks off before it is wiped out
            Assert.True(fleeing < standing);
        }

        [Fact]
        public void Even_the_smallest_fleeing_band_leaves_somebody_behind()
        {
            // No engagement is free to either side — SettlementRaidResolver's own rule, and a two-man band
            // that withdrew having lost nobody would break it.
            Faction flees = NewFaction("TribalCivilization", "Tribal");
            Assert.Equal(0, FactionRaidRules.MaxRaidersLost(flees, 0));
            for (int band = 1; band <= 4; band++)
            {
                Assert.InRange(FactionRaidRules.MaxRaidersLost(flees, band), 1, band);
            }
        }

        [Fact]
        public void A_squad_with_no_faction_behind_it_fights_to_the_last()
        {
            Assert.Equal(7, FactionRaidRules.MaxRaidersLost(null, 7));
        }

        [Fact]
        public void An_abstract_raid_never_kills_more_raiders_than_their_faction_will_leave()
        {
            // The integration: the resolver's raider casualties respect the cap, whichever way the roll goes,
            // and a faction that does not flee is the only one that can be wiped out. Asserted over many
            // resolutions rather than one, because whether the settlement holds is a roll.
            var seen = new Dictionary<string, int>();
            foreach (string defName in new[] { "TribalCivilization", "RoughOutlanders" })
            {
                int worst = 0;
                for (int trial = 0; trial < 60; trial++)
                {
                    Rand.Current = new RandomStream(9000 + trial);
                    Pawn.ResetThingIdCounter();
                    NameUseChecker.Clear();
                    Find.Storyteller = new global::SimWorld.Director.Storyteller(
                        DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"),
                        DefDatabase<DifficultyDef>.GetNamed("Medium"));

                    Faction attacker = NewFaction(defName, defName + trial);
                    var town = new Settlement(WorldObjectDefOf.Settlement, 4, null, "Holdfast", 0);
                    for (int i = 0; i < 20; i++) town.AddCitizen(NewHuman("C" + trial + "_" + i));
                    town.AddStatisticalPeople(4000); // heavily outmatches the band, so the raiders usually lose

                    var band = new List<Pawn>();
                    for (int i = 0; i < 8; i++) band.Add(NewHuman("R" + trial + "_" + i));

                    int cap = FactionRaidRules.MaxRaidersLost(attacker, band.Count);
                    SettlementRaidOutcome outcome = SettlementRaidResolver.Resolve(town, band, attacker, Rand.Current);

                    Assert.True(outcome.RaidersKilled <= cap,
                        defName + " lost " + outcome.RaidersKilled + " raiders against a cap of " + cap);
                    worst = System.Math.Max(worst, outcome.RaidersKilled);
                }
                seen[defName] = worst;
            }

            Assert.True(seen["RoughOutlanders"] > seen["TribalCivilization"],
                "a faction that never withdraws should be able to lose more of its band than one that does "
                + "(rough=" + seen["RoughOutlanders"] + ", tribal=" + seen["TribalCivilization"] + ")");
        }

        // ---- canMakeRandomly ----

        [Fact]
        public void A_faction_that_may_not_be_made_randomly_never_emerges_on_its_own()
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "canMakeRandomly", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Test", 4, soloStart: true);

            FactionDef tribal = Def("TribalCivilization");
            Assert.Contains(tribal, EmergenceManager.EligibleFactionDefs(world));

            // Shipped content sets this false only on the player's own def, which the isPlayer test already
            // removes — so the flag is exercised by flipping one that is otherwise eligible, and put back.
            // Tests reading DefDatabase.Global share one collection and never run beside each other.
            tribal.canMakeRandomly = false;
            try
            {
                Assert.DoesNotContain(tribal, EmergenceManager.EligibleFactionDefs(world));
            }
            finally
            {
                tribal.canMakeRandomly = true;
            }

            Assert.Contains(tribal, EmergenceManager.EligibleFactionDefs(world));
        }

        [Fact]
        public void The_player_civilization_says_it_may_not_be_made_randomly_and_is_not()
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "canMakeRandomly-player", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Test", 4, soloStart: true);

            Assert.False(Def("PlayerCivilization").canMakeRandomly);
            Assert.All(EmergenceManager.EligibleFactionDefs(world), d => Assert.True(d.canMakeRandomly));
        }

        // ---- isFighter ----

        [Fact]
        public void Every_shipped_combat_roster_is_made_of_fighters()
        {
            // The measurement this wiring is built on, restated as an invariant rather than as the 3,866
            // raiders it was taken over: composition never drew a non-combatant because no roster offered
            // one. Now the roster is filtered and the loader complains, so this cannot silently stop being
            // true.
            foreach (FactionDef def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                PawnGroupMaker? maker = def.GetGroupMaker(PawnGroupKindDefOf.Combat);
                if (maker?.options == null) continue;
                Assert.All(maker.options, o => Assert.True(o.kind.isFighter,
                    def.defName + "'s Combat roster lists the non-fighter " + o.kind.defName));
            }
        }

        [Fact]
        public void A_raid_is_made_only_of_fighters_at_every_budget()
        {
            foreach (string defName in new[] { "TribalCivilization", "OutlanderCivilization", "RoughOutlanders" })
            {
                Faction faction = NewFaction(defName, defName);
                foreach (float points in new[] { 100f, 400f, 800f })
                {
                    Rand.Current = new RandomStream(1234);
                    Pawn.ResetThingIdCounter();
                    NameUseChecker.Clear();
                    var parms = new PawnGroupMakerParms { faction = faction, groupKind = PawnGroupKindDefOf.Combat, points = points };
                    List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(parms);
                    Assert.NotEmpty(squad);
                    Assert.All(squad, p => Assert.True(p.kindDef!.isFighter, defName + " fielded " + p.kindDef!.defName));
                }
            }
        }

        [Fact]
        public void A_non_combatant_in_a_combat_roster_is_a_config_error_and_is_not_fielded()
        {
            PawnKindDef villager = DefDatabase<PawnKindDef>.GetNamed("Villager");
            PawnKindDef raider = DefDatabase<PawnKindDef>.GetNamed("Raider_Melee");
            Assert.False(villager.isFighter);

            var maker = new PawnGroupMaker
            {
                kindDef = PawnGroupKindDefOf.Combat,
                options = new List<PawnGenOption>
                {
                    new PawnGenOption { kind = villager, selectionWeight = 10f },
                    new PawnGenOption { kind = raider, selectionWeight = 1f },
                },
            };

            Assert.Contains(maker.ConfigErrors(), e => e.Contains("Villager") && e.Contains("isFighter"));

            var def = new FactionDef { defName = "TestRaiders", pawnGroupMakers = new List<PawnGroupMaker> { maker } };
            var faction = new Faction(def, "Test", "F_Test");
            Rand.Current = new RandomStream(5);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();

            List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(
                new PawnGroupMakerParms { faction = faction, groupKind = PawnGroupKindDefOf.Combat, points = 400f });

            Assert.NotEmpty(squad);
            Assert.All(squad, p => Assert.Same(raider, p.kindDef));
        }

        [Fact]
        public void A_non_combat_group_is_not_filtered()
        {
            // Only a war band is fighters. A Peaceful or Trader group is exactly where a non-combatant
            // belongs, so the filter is asked of the group kind and not of the option.
            PawnKindDef villager = DefDatabase<PawnKindDef>.GetNamed("Villager");
            PawnGroupKindDef peaceful = DefDatabase<PawnGroupKindDef>.AllDefsListForReading
                .First(d => !ReferenceEquals(d, PawnGroupKindDefOf.Combat));

            var maker = new PawnGroupMaker
            {
                kindDef = peaceful,
                options = new List<PawnGenOption> { new PawnGenOption { kind = villager, selectionWeight = 1f } },
            };
            var def = new FactionDef { defName = "TestVisitors", pawnGroupMakers = new List<PawnGroupMaker> { maker } };
            var faction = new Faction(def, "Visitors", "F_Visitors");
            Rand.Current = new RandomStream(6);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();

            Assert.Empty(maker.ConfigErrors());
            List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(
                new PawnGroupMakerParms { faction = faction, groupKind = peaceful, points = 200f });
            Assert.NotEmpty(squad);
            Assert.All(squad, p => Assert.Same(villager, p.kindDef));
        }

        // ---- scribe ----

        [Fact]
        public void A_faction_saved_and_reloaded_still_answers_the_same_raid_questions()
        {
            // These rules hold no state of their own — they read a Faction's def. So the round trip that
            // matters is that a saved faction comes back pointing at the same def and therefore gives the
            // same answers, days gate included.
            var before = new List<Faction>
            {
                NewFaction("RoughOutlanders", "Rough"),
                NewFaction("OutlanderCivilization", "Outlander"),
            };

            string xml = Scribe.SaveToString(new FactionListRoot { factions = before }, "root");
            FactionListRoot after = Scribe.Load<FactionListRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(before.Count, after.factions.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Faction was = before[i];
                Faction now = after.factions[i];
                Assert.Same(was.def, now.def);

                SetDay(0);
                Assert.Equal(FactionRaidRules.CanRaidYet(was), FactionRaidRules.CanRaidYet(now));
                SetDay(was.def.earliestRaidDays);
                Assert.Equal(FactionRaidRules.CanRaidYet(was), FactionRaidRules.CanRaidYet(now));
                Assert.Equal(FactionRaidRules.MaxRaidersLost(was, 10), FactionRaidRules.MaxRaidersLost(now, 10));
            }
        }

        private sealed class FactionListRoot : IExposable
        {
            public List<Faction> factions = new List<Faction>();

            public void ExposeData()
            {
                List<Faction>? f = factions;
                Scribe_Collections.Look(ref f, "factions", LookMode.Deep);
                factions = f ?? new List<Faction>();
            }
        }
    }
}
