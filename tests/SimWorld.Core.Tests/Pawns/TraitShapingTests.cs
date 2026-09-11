using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Pawns.Genes;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;

using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// Who a pawn is, shaping which traits it can have: <c>PawnKindDef</c> and <c>BackstoryDef</c>'s
    /// forced/disallowed trait lists, <c>TraitDef.requiredWorkTags</c>, and the per-degree overrides on
    /// <c>TraitDegreeData</c>. The generator has always honoured all of these; until this lane no shipped Def
    /// set any of them, so every colonist, tribesperson and raider drew from one undifferentiated pool.
    ///
    /// <para/>Everything here is asserted over many seeds and as a property — "never", "always", "strictly
    /// fewer" — rather than against one draw. A single generated pawn proves nothing about a filter, and a
    /// single pinned pawn would break the next time anyone adds a trait, which is exactly the churn this
    /// file's own content caused elsewhere.
    /// </summary>
    public class TraitShapingTests : ContentTestBase
    {
        public TraitShapingTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private const int Seeds = 120;

        private static PawnKindDef Kind(string defName) => DefDatabase<PawnKindDef>.GetNamed(defName);

        private static BackstoryDef Backstory(string defName) => DefDatabase<BackstoryDef>.GetNamed(defName);

        private static XenotypeDef Xenotype(string defName) => DefDatabase<XenotypeDef>.GetNamed(defName);

        /// <summary>A fresh pawn per seed, each from its own stream so one draw can never carry the test.</summary>
        private static IEnumerable<Pawn> Many(PawnKindDef kind, bool mustBeCapableOfViolence = false, XenotypeDef? xenotype = null, int seeds = Seeds)
        {
            for (int seed = 1; seed <= seeds; seed++)
            {
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();
                NameUseChecker.Clear();
                yield return PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind,
                    mustBeCapableOfViolence: mustBeCapableOfViolence,
                    fixedBiologicalAge: 30f,
                    xenotype: xenotype));
            }
        }

        private static bool Has(Pawn pawn, string trait) => pawn.story.traits.HasTrait(DefDatabase<TraitDef>.GetNamed(trait));

        // -----------------------------------------------------------------------------------------
        // The content is there at all. Without this the "never" assertions below could all pass on an
        // empty list, which is precisely the state this lane was sent to end.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Shipped_content_constrains_traits_by_kind_and_by_backstory()
        {
            Assert.Empty(Content.Result.Errors);

            IReadOnlyList<PawnKindDef> kinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            Assert.Contains(kinds, k => k.forcedTraits != null && k.forcedTraits.Count > 0);
            Assert.Contains(kinds, k => k.disallowedTraits != null && k.disallowedTraits.Count > 0);

            IReadOnlyList<BackstoryDef> backstories = DefDatabase<BackstoryDef>.AllDefsListForReading;
            Assert.Contains(backstories, b => b.forcedTraits != null && b.forcedTraits.Count > 0);
            Assert.Contains(backstories, b => b.disallowedTraits != null && b.disallowedTraits.Count > 0);

            IReadOnlyList<TraitDef> traits = DefDatabase<TraitDef>.AllDefsListForReading;
            Assert.Contains(traits, t => t.requiredWorkTags != WorkTags.None);
            Assert.Contains(traits, t => t.degreeDatas.Any(d => d.disabledWorkTags != WorkTags.None));
            Assert.Contains(traits, t => t.degreeDatas.Any(d => d.theOnlyAllowedMentalBreaks != null && d.theOnlyAllowedMentalBreaks.Count > 0));
            Assert.Contains(traits, t => t.degreeDatas.Any(d => d.disallowedMentalBreaks != null && d.disallowedMentalBreaks.Count > 0));

            // A forced trait that names a degree other than the trait's first, which is the only thing that
            // makes BackstoryTrait.degree worth carrying.
            Assert.Contains(
                backstories.Where(b => b.forcedTraits != null).SelectMany(b => b.forcedTraits!),
                t => t.def != null && t.degree != t.def.degreeDatas[0].degree);
        }

        // -----------------------------------------------------------------------------------------
        // PawnKindDef
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_kind_that_disallows_traits_never_rolls_them_and_an_unconstrained_kind_does()
        {
            PawnKindDef villager = Kind("Villager");
            List<string> barred = villager.disallowedTraits!.Select(t => t.defName).ToList();
            Assert.NotEmpty(barred);

            foreach (Pawn pawn in Many(villager))
            {
                foreach (string trait in barred)
                {
                    Assert.False(Has(pawn, trait), "a villager rolled " + trait);
                }
            }

            // The control, and the half that makes the above mean anything: those traits are live content that
            // a kind with no list really does roll. Colonist deliberately carries no disallowedTraits.
            var seenOnColonists = new HashSet<string>();
            foreach (Pawn pawn in Many(PawnKindDefOf.Colonist))
            {
                foreach (string trait in barred)
                {
                    if (Has(pawn, trait)) seenOnColonists.Add(trait);
                }
            }
            Assert.Equal(barred.OrderBy(t => t), seenOnColonists.OrderBy(t => t));
        }

        [Fact]
        public void A_kind_that_forces_a_trait_grants_it_to_every_pawn_that_can_hold_it()
        {
            PawnKindDef melee = Kind("Raider_Melee");
            BackstoryTrait forced = melee.forcedTraits!.Single();
            TraitDef def = forced.def!;

            // Generated the way raids actually generate raiders. A forced trait is still refused to a pawn who
            // cannot meet its requiredWorkTags, and Brawler requires Violent, so this is the population the
            // "always" claim is true of.
            foreach (Pawn pawn in Many(melee, mustBeCapableOfViolence: true))
            {
                Assert.True(pawn.story.traits.HasTrait(def, forced.degree), "a melee raider lacks " + def.defName);
                Assert.True(pawn.story.traits.GetTrait(def)!.scenForced);
                Assert.InRange(pawn.story.traits.allTraits.Count, 2, 3);
            }

            // And the mirror in the same content: the ranged raider kinds rule the same trait out.
            foreach (string ranged in new[] { "Raider_Bow", "Raider_Gunner" })
            {
                foreach (Pawn pawn in Many(Kind(ranged), mustBeCapableOfViolence: true, seeds: 40))
                {
                    Assert.False(pawn.story.traits.HasTrait(def), ranged + " rolled " + def.defName);
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // BackstoryDef
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_backstory_that_forces_a_trait_grants_it_at_the_degree_it_names()
        {
            var seen = new Dictionary<string, int>();

            foreach (Pawn pawn in Many(PawnKindDefOf.Colonist).Concat(Many(PawnKindDefOf.Tribesperson)))
            {
                foreach (BackstoryDef backstory in pawn.story.AllBackstories)
                {
                    if (backstory.forcedTraits == null) continue;
                    foreach (BackstoryTrait forced in backstory.forcedTraits)
                    {
                        seen[backstory.defName] = seen.TryGetValue(backstory.defName, out int n) ? n + 1 : 1;
                        Assert.True(pawn.story.traits.HasTrait(forced.def!, forced.degree),
                            pawn.story.TitleShort + " lacks the " + forced.def!.defName + " its backstory forces");
                    }
                }
            }

            // Every backstory that forces something has to have actually come up, or the loop above asserted
            // nothing at all.
            List<string> forcing = DefDatabase<BackstoryDef>.AllDefsListForReading
                .Where(b => b.forcedTraits != null && b.forcedTraits.Count > 0)
                .Select(b => b.defName)
                .ToList();
            Assert.Equal(forcing.OrderBy(n => n), seen.Keys.OrderBy(n => n));
        }

        [Fact]
        public void A_backstory_that_disallows_a_trait_never_appears_beside_it()
        {
            var seen = new HashSet<string>();

            foreach (Pawn pawn in Many(PawnKindDefOf.Colonist).Concat(Many(PawnKindDefOf.Tribesperson)))
            {
                foreach (BackstoryDef backstory in pawn.story.AllBackstories)
                {
                    if (backstory.disallowedTraits == null) continue;
                    seen.Add(backstory.defName);
                    foreach (TraitDef barred in backstory.disallowedTraits)
                    {
                        Assert.False(pawn.story.traits.HasTrait(barred),
                            backstory.defName + " came up holding the " + barred.defName + " it forbids");
                    }
                }
            }

            List<string> forbidding = DefDatabase<BackstoryDef>.AllDefsListForReading
                .Where(b => b.disallowedTraits != null && b.disallowedTraits.Count > 0)
                .Select(b => b.defName)
                .ToList();
            Assert.Equal(forbidding.OrderBy(n => n), seen.OrderBy(n => n));
        }

        [Fact]
        public void A_kinds_constraints_and_its_backstories_constraints_both_bind_at_once()
        {
            PawnKindDef melee = Kind("Raider_Melee");
            TraitDef brawler = melee.forcedTraits!.Single().def!;
            TraitDef squeamish = Backstory("TribalHunter").disallowedTraits!.Single();

            int sawHunter = 0;
            foreach (Pawn pawn in Many(melee, mustBeCapableOfViolence: true))
            {
                bool hunter = pawn.story.AllBackstories.Any(b => b.defName == "TribalHunter");
                if (!hunter) continue;
                sawHunter++;

                Assert.True(pawn.story.traits.HasTrait(brawler));      // the kind's forced trait
                Assert.False(pawn.story.traits.HasTrait(squeamish));   // the backstory's disallowed one
            }

            Assert.True(sawHunter > 0, "no melee raider drew the tribal hunter adulthood; the test proved nothing");
        }

        // -----------------------------------------------------------------------------------------
        // TraitDef.requiredWorkTags
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_trait_requiring_work_the_pawn_cannot_do_is_never_given_to_it()
        {
            List<TraitDef> gated = DefDatabase<TraitDef>.AllDefsListForReading
                .Where(t => t.requiredWorkTags != WorkTags.None)
                .ToList();
            Assert.NotEmpty(gated);

            int sawBlockedPawn = 0;
            foreach (Pawn pawn in Many(PawnKindDefOf.Colonist).Concat(Many(PawnKindDefOf.Tribesperson)))
            {
                WorkTags disabled = pawn.CombinedDisabledWorkTags;
                bool blocked = false;
                foreach (TraitDef def in gated)
                {
                    if ((disabled & def.requiredWorkTags) == WorkTags.None) continue;
                    blocked = true;
                    Assert.False(pawn.story.traits.HasTrait(def),
                        "a pawn who cannot do " + def.requiredWorkTags + " holds " + def.defName);
                }
                if (blocked) sawBlockedPawn++;
            }

            Assert.True(sawBlockedPawn > 0,
                "no generated pawn was barred from the work these traits require, so the gate was never tested");
        }

        [Fact]
        public void A_forced_trait_is_refused_the_same_way_a_rolled_one_is()
        {
            // Raider_Melee forces Brawler, which requires Violent, and draws from the Tribal pool where the
            // elder adulthood disables Violent. Generated without the violence requirement the raid path uses,
            // that combination really does come up — and when it does the forced trait is refused rather than
            // producing a pawn who is both incapable of violence and a brawler.
            PawnKindDef melee = Kind("Raider_Melee");
            TraitDef brawler = melee.forcedTraits!.Single().def!;

            int sawPacifistRaider = 0;
            foreach (Pawn pawn in Many(melee, seeds: 240))
            {
                if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    Assert.True(pawn.story.traits.HasTrait(brawler));
                    continue;
                }

                sawPacifistRaider++;
                Assert.False(pawn.story.traits.HasTrait(brawler),
                    "a raider incapable of violence was forced into the brawler trait anyway");
            }

            Assert.True(sawPacifistRaider > 0, "no violence-disabled raider came up; the refusal was never exercised");
        }

        // -----------------------------------------------------------------------------------------
        // TraitDegreeData.disabledWorkTags — the per-degree override
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_trait_degree_disables_exactly_the_work_that_degree_names()
        {
            TraitDef squeamishness = DefDatabase<TraitDef>.GetNamed("Squeamishness");

            Pawn mild = NewHuman("Mild");
            mild.story.traits.GainTrait(new Trait(squeamishness, 1));
            Assert.True(mild.WorkTypeIsDisabled(WorkTypeDefOf.Doctor));
            Assert.False(mild.WorkTypeIsDisabled(WorkTypeDefOf.Hunting));

            Pawn deep = NewHuman("Deep");
            deep.story.traits.GainTrait(new Trait(squeamishness, 2));
            Assert.True(deep.WorkTypeIsDisabled(WorkTypeDefOf.Doctor));
            Assert.True(deep.WorkTypeIsDisabled(WorkTypeDefOf.Hunting));

            // The mild degree declares nothing of its own and inherits the trait's tags; the deep one overrides
            // them — and, because a degree REPLACES rather than adds, has to restate what it still bars.
            Assert.Equal(WorkTags.None, squeamishness.DataAtDegree(1).disabledWorkTags);
            Assert.Equal(squeamishness.disabledWorkTags, squeamishness.DisabledWorkTagsAtDegree(1));
            Assert.NotEqual(WorkTags.None, squeamishness.DataAtDegree(2).disabledWorkTags);
            Assert.Equal(squeamishness.DataAtDegree(2).disabledWorkTags, squeamishness.DisabledWorkTagsAtDegree(2));
            WorkTags mildTags = squeamishness.DisabledWorkTagsAtDegree(1);
            WorkTags deepTags = squeamishness.DisabledWorkTagsAtDegree(2);
            Assert.Equal(mildTags, deepTags & mildTags);
            Assert.NotEqual(mildTags, deepTags);

            // A skill behind barred work reads as zero however high it was rolled.
            Pawn generated = Many(PawnKindDefOf.Colonist, seeds: 1).Single();
            generated.story.traits.GainTrait(new Trait(squeamishness, 2));
            Assert.Equal(0, generated.skills.GetSkill(SkillDefOf.Medicine)!.Level);
        }

        // -----------------------------------------------------------------------------------------
        // Genes, which shape the same pool from the other side
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_germline_that_bars_work_shapes_the_backstories_and_traits_rolled_after_it()
        {
            // Stillfolk carries Gene_Pacifist (disabledWorkTags: Violent). The germline is applied before
            // backstories and traits, so both filters see it.
            List<TraitDef> violent = DefDatabase<TraitDef>.AllDefsListForReading
                .Where(t => (t.requiredWorkTags & WorkTags.Violent) != WorkTags.None)
                .ToList();
            List<string> violentBackstories = DefDatabase<BackstoryDef>.AllDefsListForReading
                .Where(b => (b.requiredWorkTags & WorkTags.Violent) != WorkTags.None)
                .Select(b => b.defName)
                .ToList();
            Assert.NotEmpty(violent);
            Assert.NotEmpty(violentBackstories);

            foreach (Pawn pawn in Many(PawnKindDefOf.Tribesperson, xenotype: Xenotype("Stillfolk")))
            {
                Assert.True(pawn.WorkTagIsDisabled(WorkTags.Violent));
                foreach (TraitDef def in violent)
                {
                    Assert.False(pawn.story.traits.HasTrait(def), "a pacifist holds " + def.defName);
                }
                foreach (BackstoryDef backstory in pawn.story.AllBackstories)
                {
                    Assert.DoesNotContain(backstory.defName, violentBackstories);
                }
            }

            // The same kind without the germline really does produce both, so the absence above is the gene's
            // doing and not an empty pool.
            bool sawViolentTrait = false;
            bool sawViolentBackstory = false;
            foreach (Pawn pawn in Many(PawnKindDefOf.Tribesperson))
            {
                sawViolentTrait |= violent.Any(d => pawn.story.traits.HasTrait(d));
                sawViolentBackstory |= pawn.story.AllBackstories.Any(b => violentBackstories.Contains(b.defName));
            }
            Assert.True(sawViolentTrait && sawViolentBackstory);
        }

        [Fact]
        public void A_gene_that_bars_work_is_visible_to_the_skills_the_moment_it_lands()
        {
            // The bug this guards: Pawn_GeneTracker changed a pawn's disabled work without telling anything
            // that caches it, so a skill record that had already been asked "are you disabled?" kept answering
            // no, and a pacifist reported a rolled shooting level.
            Pawn pawn = NewHuman("Germline");
            pawn.skills.GetSkill(SkillDefOf.Shooting)!.Level = 8;
            Assert.Equal(8, pawn.skills.GetSkill(SkillDefOf.Shooting)!.Level);

            pawn.genes.SetXenotype(Xenotype("Stillfolk"));

            Assert.True(pawn.WorkTagIsDisabled(WorkTags.Violent));
            Assert.Equal(0, pawn.skills.GetSkill(SkillDefOf.Shooting)!.Level);
            Assert.True(pawn.skills.GetSkill(SkillDefOf.Shooting)!.TotallyDisabled);

            foreach (Pawn generated in Many(PawnKindDefOf.Colonist, xenotype: Xenotype("Stillfolk"), seeds: 20))
            {
                Assert.Equal(0, generated.skills.GetSkill(SkillDefOf.Shooting)!.Level);
                Assert.Equal(0, generated.skills.GetSkill(SkillDefOf.Melee)!.Level);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Contradictory content is a load error, not a quietly wrong pawn
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_def_that_forces_a_trait_it_also_disallows_is_a_config_error()
        {
            TraitDef greedy = DefDatabase<TraitDef>.GetNamed("Greedy");

            var kind = new PawnKindDef
            {
                defName = "TestContradiction",
                race = Human,
                forcedTraits = new List<BackstoryTrait> { new BackstoryTrait { def = greedy } },
                disallowedTraits = new List<TraitDef> { greedy },
            };
            Assert.Contains(kind.ConfigErrors(), e => e.Contains("disallows it at the same time"));

            var backstory = new BackstoryDef
            {
                defName = "TestContradictionBackstory",
                title = "contradiction",
                forcedTraits = new List<BackstoryTrait> { new BackstoryTrait { def = greedy } },
                disallowedTraits = new List<TraitDef> { greedy },
            };
            Assert.Contains(backstory.ConfigErrors(), e => e.Contains("disallows it at the same time"));
        }

        [Fact]
        public void A_forced_trait_at_a_degree_the_trait_does_not_have_is_a_config_error()
        {
            // Without this the generator silently substitutes the trait's first degree, so content asking for
            // "steadfast" could ship a colony of iron-willed soldiers and read as correct.
            var kind = new PawnKindDef
            {
                defName = "TestBadDegree",
                race = Human,
                forcedTraits = new List<BackstoryTrait> { new BackstoryTrait { def = Trait("Nerves"), degree = 7 } },
            };
            Assert.Contains(kind.ConfigErrors(), e => e.Contains("degree 7"));
        }

        [Fact]
        public void Forcing_two_conflicting_traits_or_the_same_one_twice_is_a_config_error()
        {
            TraitDef psychopath = DefDatabase<TraitDef>.GetNamed("Psychopath");
            TraitDef kindTrait = DefDatabase<TraitDef>.GetNamed("Kind");
            Assert.True(psychopath.ConflictsWith(kindTrait), "this test needs two traits that actually conflict");

            var conflicting = new PawnKindDef
            {
                defName = "TestConflict",
                race = Human,
                forcedTraits = new List<BackstoryTrait>
                {
                    new BackstoryTrait { def = psychopath },
                    new BackstoryTrait { def = kindTrait },
                },
            };
            Assert.Contains(conflicting.ConfigErrors(), e => e.Contains("forces conflicting traits"));

            var duplicated = new PawnKindDef
            {
                defName = "TestDuplicate",
                race = Human,
                forcedTraits = new List<BackstoryTrait>
                {
                    new BackstoryTrait { def = kindTrait },
                    new BackstoryTrait { def = kindTrait },
                },
            };
            Assert.Contains(duplicated.ConfigErrors(), e => e.Contains("more than once"));

            var empty = new PawnKindDef
            {
                defName = "TestNullForced",
                race = Human,
                forcedTraits = new List<BackstoryTrait> { new BackstoryTrait() },
            };
            Assert.Contains(empty.ConfigErrors(), e => e.Contains("no def"));
        }

        // -----------------------------------------------------------------------------------------
        // Save/load
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Scribe_round_trip_preserves_a_forced_trait_and_its_degree()
        {
            Pawn pawn = Many(Kind("Raider_Melee"), mustBeCapableOfViolence: true, seeds: 1).Single();
            Pawn soldierly = NewHuman("Steadfast");
            soldierly.story.adulthood = Backstory("Soldier");
            soldierly.story.traits.GainTrait(new Trait(Trait("Nerves"), 1, forced: true));
            soldierly.story.traits.GainTrait(new Trait(DefDatabase<TraitDef>.GetNamed("Squeamishness"), 2));

            var holder = new PawnHolder { pawns = new List<Pawn> { pawn, soldierly } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            for (int i = 0; i < holder.pawns!.Count; i++)
            {
                Pawn before = holder.pawns[i];
                Pawn after = loaded.pawns![i];
                Assert.Equal(
                    before.story.traits.allTraits.Select(t => t.def.defName + ":" + t.degree + ":" + t.scenForced),
                    after.story.traits.allTraits.Select(t => t.def.defName + ":" + t.degree + ":" + t.scenForced));
                Assert.Equal(before.CombinedDisabledWorkTags, after.CombinedDisabledWorkTags);
            }
        }
    }
}
