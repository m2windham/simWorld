using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Pawns.Generation
{
    /// <summary>
    /// Builds a fully-populated <see cref="Pawn"/> from a <see cref="PawnGenerationRequest"/> (RimWorld:
    /// <c>PawnGenerator.GeneratePawn</c>). Order mirrors RimWorld: race/gender/age, then (humanlike only)
    /// backstories and traits, then skills, then a name and a light appearance roll.
    /// </summary>
    public static class PawnGenerator
    {
        private const int MaxGenerationTries = 20;
        private const float MinAdulthoodChronologicalAge = 20f;

        /// <summary>Raw skill points before backstory/trait gains and the age/final-adjustment curves (RimWorld's tuning).</summary>
        private static readonly SimpleCurve LevelRandomCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(0.5f, 150f),
            new CurvePoint(4f, 150f),
            new CurvePoint(5f, 25f),
            new CurvePoint(10f, 5f),
            new CurvePoint(15f, 0f),
        });

        /// <summary>Compresses the raw (unbounded) skill total into the 0..20 range before the age factor applies.</summary>
        private static readonly SimpleCurve LevelFinalAdjustmentCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(10f, 10f),
            new CurvePoint(20f, 16f),
            new CurvePoint(27f, 20f),
        });

        /// <summary>Young pawns cap out lower; pawns past their prime cap out higher (RimWorld's veterancy curve).</summary>
        private static readonly SimpleCurve AgeSkillMaxFactorCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(10f, 0.7f),
            new CurvePoint(35f, 1f),
            new CurvePoint(60f, 1.6f),
        });

        /// <summary>Generates one pawn, regenerating from scratch up to <see cref="MaxGenerationTries"/> times
        /// when <see cref="PawnGenerationRequest.MustBeCapableOfViolence"/> keeps failing.</summary>
        public static Pawn GeneratePawn(PawnGenerationRequest request)
        {
            for (int attempt = 0; attempt < MaxGenerationTries; attempt++)
            {
                Pawn pawn = GenerateInternal(request);
                if (!request.MustBeCapableOfViolence || !pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    return pawn;
                }
            }
            throw new InvalidOperationException(
                "Could not generate a violence-capable pawn of kind " + request.KindDef.defName + " after " + MaxGenerationTries + " tries.");
        }

        /// <summary>A freshly-born pawn: age 0, no backstories, no traits, name only, with a "Born" life event.</summary>
        public static Pawn GenerateNewborn(PawnKindDef kindDef, Gender? gender = null)
        {
            var request = new PawnGenerationRequest(kindDef, fixedGender: gender, newborn: true);
            return GeneratePawn(request);
        }

        private static Pawn GenerateInternal(PawnGenerationRequest request)
        {
            PawnKindDef kind = request.KindDef;
            ThingDef raceDef = kind.race ?? throw new InvalidOperationException("PawnKindDef " + kind.defName + " has no race.");
            RaceProperties race = raceDef.race ?? throw new InvalidOperationException("ThingDef " + raceDef.defName + " has no RaceProperties.");

            var pawn = new Pawn(raceDef);
            pawn.kindDef = kind;
            pawn.faction = request.Faction;

            GenerateGender(pawn, request);
            GenerateAge(pawn, request, race, kind);

            bool humanlike = race.Humanlike;
            if (humanlike && !request.Newborn)
            {
                GenerateBackstories(pawn, kind);
                GenerateTraits(pawn, kind);
                pawn.Notify_TraitsChanged();
            }

            GenerateSkills(pawn);
            GenerateAppearance(pawn);

            pawn.Name = humanlike ? PawnBioAndNameGenerator.GeneratePawnName(pawn) : PawnBioAndNameGenerator.GenerateName(pawn);

            if (request.Newborn)
            {
                pawn.story.RecordLifeEvent("Born", pawn.Label + " was born.");
            }
            else if (humanlike)
            {
                // A newborn never carries a weapon (matches the "no backstories/traits either" gate above);
                // runs after identity/appearance are settled so a retried generation (MustBeCapableOfViolence)
                // discards a consistent whole pawn rather than a half-armed one.
                PawnWeaponGenerator.TryGenerateWeaponFor(pawn, request);
            }

            // Rolled once here, for every humanlike pawn this generator produces (newborn or not) — see
            // Pawn_AgeTracker's "Hidden lifespan budget" section for why it stays hidden. Comes right after
            // the weapon roll (the RNG-sensitive step every pre-existing test already accounts for) and before
            // apparel, so this pass's new draw doesn't shift a single number any test already pinned.
            if (humanlike)
            {
                pawn.ageTracker.RollLifespanBudget(race);
            }

            if (humanlike && !request.Newborn)
            {
                // Last of all: pawngen.apparel is new this pass, so it goes after every RNG-sensitive step
                // above rather than between weapon generation and the lifespan roll, which would have shifted
                // that roll's draw for every humanlike pawn and, with it, every test that pins a lifespan or
                // age outcome.
                PawnApparelGenerator.TryGenerateApparelFor(pawn, request);
            }

            return pawn;
        }

        private static void GenerateGender(Pawn pawn, PawnGenerationRequest request)
        {
            pawn.gender = request.FixedGender ?? (Rand.Bool ? Gender.Male : Gender.Female);
        }

        private static void GenerateAge(Pawn pawn, PawnGenerationRequest request, RaceProperties race, PawnKindDef kind)
        {
            float bioAge;
            if (request.Newborn)
            {
                bioAge = 0f;
            }
            else if (request.FixedBiologicalAge.HasValue)
            {
                bioAge = request.FixedBiologicalAge.Value;
            }
            else if (race.ageGenerationCurve != null)
            {
                bioAge = Rand.ByCurve(race.ageGenerationCurve);
                if (kind.minGenerationAge >= 0f) bioAge = Math.Max(bioAge, kind.minGenerationAge);
                if (kind.maxGenerationAge >= 0f) bioAge = Math.Min(bioAge, kind.maxGenerationAge);
            }
            else
            {
                // No curve (animals, or a humanlike race that never defined one): uniform within (0, 0.8 * lifeExpectancy).
                float max = Math.Max(race.lifeExpectancy * 0.8f, 0.1f);
                bioAge = Rand.Range(0f, max);
            }

            float chronoAge = request.Newborn ? 0f : (request.FixedChronologicalAge ?? bioAge);

            pawn.ageTracker.ageBiologicalTicks = (long)(bioAge * GenDate.TicksPerYear);
            pawn.ageTracker.ageChronologicalTicks = (long)(chronoAge * GenDate.TicksPerYear);
        }

        private static void GenerateBackstories(Pawn pawn, PawnKindDef kind)
        {
            List<string> categories = kind.backstoryCategories ?? new List<string>();

            BackstoryDef? childhood = PickBackstory(BackstorySlot.Childhood, categories, null);
            if (childhood != null) pawn.story.childhood = childhood;

            if (pawn.ageTracker.AgeChronologicalYears >= MinAdulthoodChronologicalAge)
            {
                BackstoryDef? adulthood = PickBackstory(BackstorySlot.Adulthood, categories, childhood);
                if (adulthood != null) pawn.story.adulthood = adulthood;
            }
        }

        private static BackstoryDef? PickBackstory(BackstorySlot slot, List<string> categories, BackstoryDef? exclude)
        {
            List<BackstoryDef> candidates = DefDatabase<BackstoryDef>.AllDefsListForReading
                .Where(b => b.slot == slot && b.shuffleable && !ReferenceEquals(b, exclude) &&
                            (b.spawnCategories == null || b.spawnCategories.Count == 0 || b.spawnCategories.Any(categories.Contains)))
                .ToList();
            return candidates.Count == 0 ? null : Rand.Element((IReadOnlyList<BackstoryDef>)candidates);
        }

        private static void GenerateTraits(Pawn pawn, PawnKindDef kind)
        {
            var disallowed = new HashSet<TraitDef>();
            AddAll(disallowed, kind.disallowedTraits);
            foreach (BackstoryDef backstory in pawn.story.AllBackstories) AddAll(disallowed, backstory.disallowedTraits);

            foreach (BackstoryDef backstory in pawn.story.AllBackstories)
            {
                if (backstory.forcedTraits == null) continue;
                foreach (BackstoryTrait bt in backstory.forcedTraits) TryForceTrait(pawn, bt.def, bt.degree, disallowed);
            }
            if (kind.forcedTraits != null)
            {
                foreach (BackstoryTrait bt in kind.forcedTraits) TryForceTrait(pawn, bt.def, bt.degree, disallowed);
            }

            int target = Rand.RangeInclusive(2, 3);
            bool female = pawn.gender == Gender.Female;
            IReadOnlyList<TraitDef> allTraitDefs = DefDatabase<TraitDef>.AllDefsListForReading;

            int safety = 0;
            while (pawn.story.traits.allTraits.Count < target && safety < 200)
            {
                safety++;
                List<TraitDef> candidates = allTraitDefs.Where(td =>
                        !disallowed.Contains(td) &&
                        !pawn.story.traits.HasTrait(td) &&
                        !ConflictsWithExisting(pawn, td) &&
                        !RequiredTagsBlocked(pawn, td))
                    .ToList();
                if (candidates.Count == 0) break;

                if (!GenCollection.TryRandomElementByWeight(candidates, td => TraitCommonality(td, female), Rand.Current, out TraitDef picked))
                {
                    break;
                }
                pawn.story.traits.GainTrait(new Trait(picked, PickDegree(picked, female)));
            }
        }

        private static void TryForceTrait(Pawn pawn, TraitDef? def, int degree, HashSet<TraitDef> disallowed)
        {
            if (def == null || disallowed.Contains(def)) return;
            if (pawn.story.traits.HasTrait(def) || ConflictsWithExisting(pawn, def)) return;
            int useDegree = def.HasDegree(degree) ? degree : def.degreeDatas[0].degree;
            pawn.story.traits.GainTrait(new Trait(def, useDegree, forced: true));
        }

        private static bool ConflictsWithExisting(Pawn pawn, TraitDef candidate)
        {
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                if (trait.def.ConflictsWith(candidate) || candidate.ConflictsWith(trait.def)) return true;
            }
            return false;
        }

        private static bool RequiredTagsBlocked(Pawn pawn, TraitDef def)
        {
            return def.requiredWorkTags != WorkTags.None && (pawn.CombinedDisabledWorkTags & def.requiredWorkTags) != WorkTags.None;
        }

        private static float TraitCommonality(TraitDef def, bool female)
        {
            return female && def.commonalityFemale >= 0f ? def.commonalityFemale : def.commonality;
        }

        private static int PickDegree(TraitDef def, bool female)
        {
            if (def.degreeDatas.Count == 1) return def.degreeDatas[0].degree;
            return GenCollection.TryRandomElementByWeight(def.degreeDatas, d => d.commonality, Rand.Current, out TraitDegreeData picked)
                ? picked.degree
                : def.degreeDatas[0].degree;
        }

        /// <summary>Internal (not private) so tests can isolate the skill roll from the rest of generation.</summary>
        internal static void GenerateSkills(Pawn pawn)
        {
            float ageYears = pawn.ageTracker.AgeBiologicalYearsFloat;
            float ageFactor = AgeSkillMaxFactorCurve.Evaluate(ageYears);

            foreach (SkillRecord record in pawn.skills.skills)
            {
                if (record.TotallyDisabled)
                {
                    record.Level = 0;
                    record.passion = Passion.None;
                    continue;
                }

                float raw = Rand.ByCurve(LevelRandomCurve);
                raw += SkillGainFromBackstories(pawn, record.def);
                raw += SkillGainFromTraits(pawn, record.def);

                float adjusted = LevelFinalAdjustmentCurve.Evaluate(raw) * ageFactor;
                int level = GenMath.Clamp((int)Math.Round(adjusted, MidpointRounding.AwayFromZero), SkillRecord.MinLevel, SkillRecord.MaxLevel);
                record.Level = level;

                record.passion = Passion.None;
                if (level > 0)
                {
                    float chance = level * 0.11f;
                    float roll = Rand.Value;
                    if (roll < chance)
                    {
                        record.passion = roll < chance * 0.2f ? Passion.Major : Passion.Minor;
                    }
                }
            }
        }

        private static float SkillGainFromBackstories(Pawn pawn, SkillDef skill)
        {
            float total = 0f;
            foreach (BackstoryDef backstory in pawn.story.AllBackstories)
            {
                total += SumGains(backstory.skillGains, skill);
            }
            return total;
        }

        private static float SkillGainFromTraits(Pawn pawn, SkillDef skill)
        {
            float total = 0f;
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                total += SumGains(trait.CurrentData.skillGains, skill);
            }
            return total;
        }

        private static float SumGains(List<SkillGain>? gains, SkillDef skill)
        {
            if (gains == null) return 0f;
            float total = 0f;
            for (int i = 0; i < gains.Count; i++)
            {
                if (gains[i].skill == skill) total += gains[i].amount;
            }
            return total;
        }

        private static void GenerateAppearance(Pawn pawn)
        {
            pawn.story.melanin = Rand.Value;
            if (!pawn.RaceProps.Humanlike) return;

            string genderBody = pawn.gender == Gender.Female ? "Female" : "Male";
            var options = new[]
            {
                (label: genderBody, weight: 0.6f),
                (label: "Thin", weight: 0.15f),
                (label: "Fat", weight: 0.15f),
                (label: "Hulk", weight: 0.1f),
            };
            GenCollection.TryRandomElementByWeight(options, o => o.weight, Rand.Current, out var picked);
            pawn.story.bodyType = picked.label;
        }

        private static void AddAll(HashSet<TraitDef> set, List<TraitDef>? source)
        {
            if (source == null) return;
            foreach (TraitDef def in source) set.Add(def);
        }
    }
}
