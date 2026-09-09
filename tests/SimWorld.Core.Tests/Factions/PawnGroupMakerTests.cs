using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Factions
{
    /// <summary>Squad composition (director.raids): PawnGenOption/PawnGroupMaker/PawnGroupMakerUtility spending points against a faction's own tech-gated roster.</summary>
    public class PawnGroupMakerTests : ContentTestBase
    {
        public PawnGroupMakerTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");
        private static FactionDef RoughDef => DefDatabase<FactionDef>.GetNamed("RoughOutlanders");
        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");
        private static PawnKindDef Kind(string defName) => DefDatabase<PawnKindDef>.GetNamed(defName);

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        // ---- content ----

        [Fact]
        public void Content_has_expected_group_kind_and_raid_strategy_defs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(3, DefDatabase<PawnGroupKindDef>.DefCount);
            Assert.Equal(2, DefDatabase<RaidStrategyDef>.DefCount);
            Assert.NotNull(PawnGroupKindDefOf.Combat);
            Assert.NotNull(RaidStrategyDefOf.ImmediateAttack);

            RaidStrategyDef siege = DefDatabase<RaidStrategyDef>.GetNamed("Siege");
            Assert.Equal(TechLevel.Industrial, siege.minTechLevel);
        }

        [Fact]
        public void Raiding_factions_each_have_a_combat_pawnGroupMaker()
        {
            Assert.NotNull(TribalDef.GetGroupMaker(PawnGroupKindDefOf.Combat));
            Assert.NotNull(OutlanderDef.GetGroupMaker(PawnGroupKindDefOf.Combat));
            Assert.NotNull(RoughDef.GetGroupMaker(PawnGroupKindDefOf.Combat));
            // The player's own def never raids, and was never given one.
            Assert.Null(PlayerDef.GetGroupMaker(PawnGroupKindDefOf.Combat));
        }

        // ---- ChoosePawnGenOptionsByPoints ----

        [Fact]
        public void ChoosePawnGenOptionsByPoints_never_exceeds_budget_by_more_than_the_cheapest_overshoot()
        {
            var cheap = new PawnGenOption { kind = Kind("Raider_Melee"), selectionWeight = 1f }; // combatPower 35
            var options = new List<PawnGenOption> { cheap };
            var rand = new RandomStream(1);

            for (int trial = 0; trial < 50; trial++)
            {
                List<PawnGenOption> chosen = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(100f, options, rand);
                float totalCost = chosen.Sum(o => o.Cost);
                Assert.True(totalCost <= 100f + cheap.Cost, "spent " + totalCost + " against a 100-point budget with only a 35-point option");
                Assert.NotEmpty(chosen);
            }
        }

        [Fact]
        public void ChoosePawnGenOptionsByPoints_never_empty_even_when_budget_is_below_the_cheapest_option()
        {
            var pricey = new PawnGenOption { kind = Kind("Raider_Gunner"), selectionWeight = 1f }; // combatPower 70
            var options = new List<PawnGenOption> { pricey };
            var rand = new RandomStream(2);

            List<PawnGenOption> chosen = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(1f, options, rand);

            Assert.Single(chosen);
            Assert.Same(pricey, chosen[0]);
        }

        [Fact]
        public void ChoosePawnGenOptionsByPoints_spends_more_of_a_bigger_budget()
        {
            var options = new List<PawnGenOption>
            {
                new PawnGenOption { kind = Kind("Raider_Melee"), selectionWeight = 1f },
                new PawnGenOption { kind = Kind("Raider_Bow"), selectionWeight = 1f },
            };
            var rand = new RandomStream(3);

            float SpentOver(float budget, int trials)
            {
                float total = 0f;
                for (int i = 0; i < trials; i++)
                {
                    total += PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(budget, options, rand).Sum(o => o.Cost);
                }
                return total / trials;
            }

            float smallAvg = SpentOver(60f, 200);
            float bigAvg = SpentOver(600f, 200);
            Assert.True(bigAvg > smallAvg * 3f, "expected a 10x budget to spend materially more (small=" + smallAvg + ", big=" + bigAvg + ")");
        }

        [Fact]
        public void ChoosePawnGenOptionsByPoints_is_deterministic_for_the_same_seed()
        {
            var options = new List<PawnGenOption>
            {
                new PawnGenOption { kind = Kind("Raider_Melee"), selectionWeight = 2f },
                new PawnGenOption { kind = Kind("Raider_Bow"), selectionWeight = 1f },
                new PawnGenOption { kind = Kind("Raider_Gunner"), selectionWeight = 1f },
            };

            List<PawnGenOption> a = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(400f, options, new RandomStream(555));
            List<PawnGenOption> b = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(400f, options, new RandomStream(555));

            Assert.Equal(a.Select(o => o.kind.defName), b.Select(o => o.kind.defName));
        }

        // ---- PawnGroupMakerUtility.GeneratePawns (squad composition, tech gating) ----

        [Fact]
        public void GeneratePawns_from_tribal_faction_never_fields_an_industrial_raider()
        {
            Faction tribal = NewFaction(TribalDef, "Tribal");
            Rand.Current = new RandomStream(4321);
            Pawn.ResetThingIdCounter();

            for (int trial = 0; trial < 30; trial++)
            {
                var parms = new PawnGroupMakerParms { faction = tribal, groupKind = PawnGroupKindDefOf.Combat, points = 800f };
                List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(parms);
                Assert.NotEmpty(squad);
                Assert.DoesNotContain(squad, p => p.kindDef!.defName == "Raider_Gunner");
                Assert.All(squad, p => Assert.Same(tribal, p.faction));
            }
        }

        [Fact]
        public void GeneratePawns_from_outlander_faction_can_field_gunners()
        {
            Faction outlander = NewFaction(OutlanderDef, "Outlander");
            Rand.Current = new RandomStream(99);
            Pawn.ResetThingIdCounter();

            bool sawGunner = false;
            for (int trial = 0; trial < 30 && !sawGunner; trial++)
            {
                var parms = new PawnGroupMakerParms { faction = outlander, groupKind = PawnGroupKindDefOf.Combat, points = 400f };
                List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(parms);
                if (squad.Any(p => p.kindDef!.defName == "Raider_Gunner")) sawGunner = true;
            }
            Assert.True(sawGunner, "expected an industrial-tech faction to field a Raider_Gunner across 30 large-budget squads");
        }

        [Fact]
        public void GeneratePawns_faction_with_no_group_maker_yields_an_empty_squad()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            var parms = new PawnGroupMakerParms { faction = player, groupKind = PawnGroupKindDefOf.Combat, points = 500f };

            Assert.Empty(PawnGroupMakerUtility.GeneratePawns(parms));
        }

        [Fact]
        public void GeneratePawns_is_deterministic_for_the_same_seed()
        {
            Faction rough = NewFaction(RoughDef, "Rough");

            Rand.Current = new RandomStream(2024);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            var parmsA = new PawnGroupMakerParms { faction = rough, groupKind = PawnGroupKindDefOf.Combat, points = 300f };
            List<Pawn> squadA = PawnGroupMakerUtility.GeneratePawns(parmsA);

            Rand.Current = new RandomStream(2024);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            var parmsB = new PawnGroupMakerParms { faction = rough, groupKind = PawnGroupKindDefOf.Combat, points = 300f };
            List<Pawn> squadB = PawnGroupMakerUtility.GeneratePawns(parmsB);

            Assert.Equal(squadA.Select(p => p.kindDef!.defName), squadB.Select(p => p.kindDef!.defName));
            Assert.Equal(squadA.Select(p => p.Name!.ToStringFull), squadB.Select(p => p.Name!.ToStringFull));
        }

        // ---- gear tech gating on generated raiders ----

        [Fact]
        public void Tribal_raiders_are_never_armed_with_an_industrial_weapon()
        {
            Faction tribal = NewFaction(TribalDef, "Tribal2");
            Rand.Current = new RandomStream(77);
            Pawn.ResetThingIdCounter();

            for (int trial = 0; trial < 20; trial++)
            {
                var parms = new PawnGroupMakerParms { faction = tribal, groupKind = PawnGroupKindDefOf.Combat, points = 300f };
                foreach (Pawn pawn in PawnGroupMakerUtility.GeneratePawns(parms))
                {
                    ThingDef? weapon = pawn.equipment.Primary?.def;
                    if (weapon == null) continue;
                    Assert.True(weapon.techLevel <= TechLevel.Neolithic, weapon.defName + " (" + weapon.techLevel + ") issued to a Neolithic faction's raider");
                }
            }
        }

        // ---- Scribe round trip ----

        private sealed class RaidRoundTripRoot : IExposable
        {
            public Faction faction = null!;
            public List<Pawn> pawns = null!;

            public void ExposeData()
            {
                Faction? f = faction;
                Scribe_Deep.Look(ref f, "faction");
                faction = f!;
                List<Pawn>? p = pawns;
                Scribe_Collections.Look(ref p, "pawns", LookMode.Deep);
                pawns = p ?? new List<Pawn>();
            }
        }

        [Fact]
        public void Scribe_round_trip_of_a_generated_raid_squad_preserves_faction_and_gear()
        {
            Faction rough = NewFaction(RoughDef, "RoughRT");
            Rand.Current = new RandomStream(31337);
            Pawn.ResetThingIdCounter();
            var parms = new PawnGroupMakerParms { faction = rough, groupKind = PawnGroupKindDefOf.Combat, points = 250f };
            List<Pawn> squad = PawnGroupMakerUtility.GeneratePawns(parms);
            Assert.NotEmpty(squad);

            var root = new RaidRoundTripRoot { faction = rough, pawns = squad };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            RaidRoundTripRoot loaded = Scribe.Load<RaidRoundTripRoot>(xml, "root", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(squad.Count, loaded.pawns.Count);
            for (int i = 0; i < squad.Count; i++)
            {
                Assert.Same(loaded.faction, loaded.pawns[i].faction);
                Assert.Equal(squad[i].kindDef!.defName, loaded.pawns[i].kindDef!.defName);

                ThingDef? originalWeapon = squad[i].equipment.Primary?.def;
                ThingDef? restoredWeapon = loaded.pawns[i].equipment.Primary?.def;
                Assert.Equal(originalWeapon?.defName, restoredWeapon?.defName);
            }
        }
    }
}
