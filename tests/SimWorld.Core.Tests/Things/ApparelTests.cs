using System.Collections.Generic;
using System.Linq;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// pawngen.apparel: ThingDef.apparel (body part groups, layers, tags), Pawn_ApparelTracker (wearing an
    /// item registers it as a real armor source with Combat's own PawnArmor hook), and PawnApparelGenerator
    /// consuming PawnKindDef.apparelTags/apparelMoneyRange the way PawnWeaponGenerator already consumes the
    /// weapon half.
    /// </summary>
    public class ApparelTests : ContentTestBase
    {
        public ApparelTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);
        private static BodyPartGroupDef Group(string defName) => DefDatabase<BodyPartGroupDef>.GetNamed(defName);
        private static ApparelLayerDef Layer(string defName) => DefDatabase<ApparelLayerDef>.GetNamed(defName);

        // ---- ApparelProperties ----

        [Fact]
        public void ConflictsWith_requires_a_shared_layer_and_a_shared_body_part_group()
        {
            ApparelProperties shirt = Def("Apparel_Shirt").apparel!; // OnSkin, Torso
            ApparelProperties pants = Def("Apparel_Pants").apparel!; // OnSkin, Legs
            ApparelProperties parka = Def("Apparel_Parka").apparel!; // Shell, Torso+Arms+Neck

            Assert.False(shirt.ConflictsWith(pants));
            Assert.False(shirt.ConflictsWith(parka));

            var secondOnSkinTorsoPiece = new ApparelProperties
            {
                bodyPartGroups = new List<BodyPartGroupDef> { Group("Torso") },
                layers = new List<ApparelLayerDef> { Layer("OnSkin") },
            };
            Assert.True(shirt.ConflictsWith(secondOnSkinTorsoPiece));
        }

        [Fact]
        public void CoversBodyPart_is_true_only_for_a_part_in_one_of_its_groups()
        {
            Pawn human = NewHuman();
            BodyPartRecord torso = human.RaceProps.body!.corePart!;
            BodyPartRecord skull = human.RaceProps.body!.GetPartsWithDef(DefDatabase<BodyPartDef>.GetNamed("Skull")).First();

            ApparelProperties shirt = Def("Apparel_Shirt").apparel!;
            Assert.True(shirt.CoversBodyPart(torso));
            Assert.False(shirt.CoversBodyPart(skull));
        }

        // ---- Pawn_ApparelTracker ----

        [Fact]
        public void Wearing_and_removing_apparel_updates_WornApparel_and_the_armor_registry()
        {
            Pawn pawn = NewHuman();
            var shirt = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"));

            pawn.apparel.Wear(shirt);
            Assert.True(pawn.apparel.IsWorn(shirt));
            Assert.Contains(shirt, pawn.apparel.WornApparel);
            Assert.Contains(PawnArmor.GetSources(pawn), s => s is ApparelArmorSource a && a.Apparel == shirt);

            Assert.True(pawn.apparel.RemoveApparel(shirt));
            Assert.False(pawn.apparel.IsWorn(shirt));
            Assert.DoesNotContain(PawnArmor.GetSources(pawn), s => s is ApparelArmorSource a && a.Apparel == shirt);
        }

        [Fact]
        public void WouldConflictWithWorn_reflects_the_worn_sets_own_conflicts()
        {
            Pawn pawn = NewHuman();
            var flakVest = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_FlakVest"));
            pawn.apparel.Wear(flakVest);

            // Same layer (Shell), overlapping group (Torso): conflicts.
            Assert.True(pawn.apparel.WouldConflictWithWorn(Def("Apparel_Parka")));
            // Different layer entirely: no conflict.
            Assert.False(pawn.apparel.WouldConflictWithWorn(Def("Apparel_Shirt")));
        }

        [Fact]
        public void Worn_apparel_actually_reduces_damage_through_ArmorUtility()
        {
            // The FlakVest's own ArmorRating_Sharp finally has a wearer: with it worn, ArmorUtility's roll
            // (previously skipped outright for every pawn, since armor was always exactly 0) now sometimes
            // deflects a Cut hit outright — reachable purely through Combat's pre-existing IArmorSource/PawnArmor
            // hook, with nothing in Combat itself touched.
            Pawn pawn = NewHuman();
            var vest = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_FlakVest"));
            pawn.apparel.Wear(vest);

            BodyPartRecord torso = pawn.RaceProps.body!.corePart!;
            StatDef armorStat = DefDatabase<StatDef>.GetNamed("ArmorRating_Sharp");
            Assert.True(vest.GetStatValue(armorStat) > 0f);

            Rand.Current = new RandomStream(1);
            bool everDeflected = false;
            for (int i = 0; i < 300 && !everDeflected; i++)
            {
                DamageDef dmg = DamageDefOf.Cut;
                ArmorUtility.GetPostArmorDamage(pawn, 10f, 0f, torso, ref dmg, out bool deflected, out _);
                if (deflected) everDeflected = true;
            }
            Assert.True(everDeflected, "flak vest's armor never once deflected a Cut hit across 300 rolls");
        }

        [Fact]
        public void Bare_skinned_pawn_has_the_same_zero_rating_natural_armor_as_before_apparel_existed()
        {
            Pawn pawn = NewHuman();
            StatDef armorStat = DefDatabase<StatDef>.GetNamed("ArmorRating_Sharp");
            Assert.All(PawnArmor.GetSources(pawn), s => Assert.Equal(0f, s.ArmorRating(armorStat, pawn.RaceProps.body!.corePart!)));
        }

        // ---- Scribe ----

        [Fact]
        public void Worn_apparel_survives_a_save_and_still_registers_as_armor()
        {
            Pawn pawn = NewHuman();
            var shirt = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"));
            pawn.apparel.Wear(shirt);

            string xml = Scribe.SaveToString(pawn, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.Single(loaded.apparel.WornApparel);
            Assert.Equal("Apparel_Shirt", loaded.apparel.WornApparel[0].def.defName);
            Assert.Contains(PawnArmor.GetSources(loaded), s => s is ApparelArmorSource);
        }

        // ---- PawnApparelGenerator ----

        [Fact]
        public void Generation_dresses_a_pawn_within_tags_and_money_with_no_conflicts_among_worn_pieces()
        {
            var kind = new PawnKindDef
            {
                defName = "Test_ApparelKind",
                race = Human,
                apparelTags = new List<string> { "Civil" },
                apparelMoneyRange = new FloatRange(0f, 50f),
            };

            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind));

            Assert.NotEmpty(pawn.apparel.WornApparel);
            foreach (ThingWithComps piece in pawn.apparel.WornApparel)
            {
                Assert.Contains("Civil", piece.def.apparel!.tags!);
                Assert.True(piece.def.BaseMarketValue <= 50f);
                // FlakVest/SimpleHelmet are Soldier-tagged and Industrial-tech only: never eligible here.
                Assert.NotEqual("Apparel_FlakVest", piece.def.defName);
                Assert.NotEqual("Apparel_SimpleHelmet", piece.def.defName);
            }

            IReadOnlyList<ThingWithComps> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                for (int j = i + 1; j < worn.Count; j++)
                {
                    Assert.False(worn[i].def.apparel!.ConflictsWith(worn[j].def.apparel!),
                        worn[i].def.defName + " and " + worn[j].def.defName + " were both worn but conflict.");
                }
            }
        }

        [Fact]
        public void A_kind_with_no_apparel_tags_is_never_dressed()
        {
            var kind = new PawnKindDef { defName = "Test_NoApparelKind", race = Human };
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind));
            Assert.Empty(pawn.apparel.WornApparel);
        }

        [Fact]
        public void A_faction_tech_ceiling_excludes_apparel_above_its_own_tech_level()
        {
            var faction = new global::SimWorld.Factions.Faction
            {
                def = new global::SimWorld.Factions.FactionDef { defName = "Test_NeolithicFaction", techLevel = TechLevel.Neolithic },
            };
            var kind = new PawnKindDef
            {
                defName = "Test_SoldierKind",
                race = Human,
                apparelTags = new List<string> { "Soldier" },
                apparelMoneyRange = new FloatRange(0f, 1000f),
            };

            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction: faction));

            // Shirt/Pants are tagged for every faction (Civil, Tribal, Soldier) and Neolithic, so those still
            // fit; FlakVest/SimpleHelmet are Soldier-only *and* Industrial, so a Neolithic faction can issue
            // neither — the same tech ceiling PawnWeaponGenerator already enforces for weapons.
            Assert.All(pawn.apparel.WornApparel, piece => Assert.Equal(TechLevel.Neolithic, piece.def.techLevel));
            Assert.DoesNotContain(pawn.apparel.WornApparel, piece => piece.def.defName == "Apparel_FlakVest");
            Assert.DoesNotContain(pawn.apparel.WornApparel, piece => piece.def.defName == "Apparel_SimpleHelmet");
        }
    }
}
