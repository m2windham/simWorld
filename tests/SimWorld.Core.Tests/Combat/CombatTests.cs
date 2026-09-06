using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Combat
{
    public class CombatTests : ContentTestBase
    {
        public CombatTests(CoreContentFixture content) : base(content)
        {
        }

        private static BodyPartRecord Part(Pawn p, string label) =>
            p.RaceProps.body!.GetPartByLabel(label) ?? throw new InvalidOperationException("no part " + label);

        private static void AdvanceVerb(global::SimWorld.Combat.Verb verb, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                verb.VerbTick();
            }
        }

        private static void RunVerbToIdle(global::SimWorld.Combat.Verb verb, int guardTicks = 20000)
        {
            int guard = 0;
            while (!verb.Available())
            {
                Find.TickManager.DoSingleTick();
                verb.VerbTick();
                if (++guard > guardTicks) throw new InvalidOperationException("verb never returned to Idle");
            }
        }

        private sealed class FixedArmor : IArmorSource
        {
            private readonly float rating;
            public FixedArmor(float rating) { this.rating = rating; }
            public float ArmorRating(StatDef armorStat, BodyPartRecord part) => rating;
            public bool Covers(BodyPartRecord part) => true;
        }

        // ---- content ----

        [Fact]
        public void Combat_content_loads_with_no_errors_and_defofs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(3, DefDatabase<DamageArmorCategoryDef>.DefCount);
            Assert.Equal(6, DefDatabase<ToolCapacityDef>.DefCount);
            Assert.Equal(6, DefDatabase<ManeuverDef>.DefCount);
            Assert.NotNull(DamageArmorCategoryDefOf.Sharp);
            Assert.NotNull(DamageArmorCategoryDefOf.Blunt);
            Assert.NotNull(DamageArmorCategoryDefOf.Heat);
            Assert.NotNull(ToolCapacityDefOf.Blunt);
            Assert.NotNull(ToolCapacityDefOf.Cut);
            Assert.NotNull(ToolCapacityDefOf.Stab);
            Assert.Same(DamageArmorCategoryDefOf.Sharp, DamageDefOf.Cut.armorCategory);
            Assert.Same(DamageArmorCategoryDefOf.Blunt, DamageDefOf.Blunt.armorCategory);

            ThingDef knife = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife");
            Assert.Equal(2, knife.tools!.Count);
            ThingDef revolver = DefDatabase<ThingDef>.GetNamed("Gun_Revolver");
            Assert.Single(revolver.verbs!);
            Assert.NotNull(DefDatabase<ThingDef>.GetNamed("Bullet_Revolver").projectile);
        }

        // ---- range bands and accuracy ----

        [Theory]
        [InlineData(3f, RangeCategory.Touch)]
        [InlineData(3.01f, RangeCategory.Short)]
        [InlineData(12f, RangeCategory.Short)]
        [InlineData(12.01f, RangeCategory.Medium)]
        [InlineData(25f, RangeCategory.Medium)]
        [InlineData(25.01f, RangeCategory.Long)]
        [InlineData(40f, RangeCategory.Long)]
        [InlineData(60f, RangeCategory.Long)]
        public void Range_categories_match_the_fixed_bands(float distance, RangeCategory expected)
        {
            Assert.Equal(expected, VerbProperties.GetRangeCategory(distance));
        }

        [Fact]
        public void Adjusted_accuracy_interpolates_between_band_anchors()
        {
            var props = new VerbProperties { accuracyTouch = 0.8f, accuracyShort = 0.6f, accuracyMedium = 0.4f, accuracyLong = 0.2f };
            Assert.Equal(0.8f, props.AdjustedAccuracy(0f), 4);
            Assert.Equal(0.8f, props.AdjustedAccuracy(3f), 4);
            Assert.Equal(0.7f, props.AdjustedAccuracy(7.5f), 4); // midpoint of the 3..12 band
            Assert.Equal(0.6f, props.AdjustedAccuracy(12f), 4);
            Assert.Equal(0.5f, props.AdjustedAccuracy(18.5f), 4); // midpoint of the 12..25 band
            Assert.Equal(0.2f, props.AdjustedAccuracy(40f), 4);
            Assert.Equal(0.2f, props.AdjustedAccuracy(90f), 4); // clamps beyond Long
        }

        // ---- shot report ----

        [Fact]
        public void Shot_report_raises_accuracy_to_the_power_of_distance()
        {
            Pawn shooter = NewHuman();
            Pawn target = NewHuman();
            var props = new VerbProperties { accuracyTouch = 1f, accuracyShort = 1f, accuracyMedium = 1f, accuracyLong = 1f };
            var verb = new Verb_LaunchProjectile(shooter, props);
            ShotReport report = ShotReport.HitReportFor(shooter, verb, target, 8f, Array.Empty<CoverInfo>());
            float accuracy = CombatStats.ShootingAccuracyPawn(4);
            Assert.Equal((float)Math.Pow(accuracy, 8f), report.factorFromShootingAccuracy, 5);
        }

        [Fact]
        public void Cover_reduces_total_estimated_hit_chance()
        {
            Pawn shooter = NewHuman();
            Pawn target = NewHuman();
            var props = new VerbProperties { accuracyTouch = 0.9f, accuracyShort = 0.9f, accuracyMedium = 0.9f, accuracyLong = 0.9f };
            var verb = new Verb_LaunchProjectile(shooter, props);
            ShotReport noCover = ShotReport.HitReportFor(shooter, verb, target, 5f, Array.Empty<CoverInfo>());
            ShotReport withCover = ShotReport.HitReportFor(shooter, verb, target, 5f, new[] { new CoverInfo(0.5f) });
            Assert.Equal(1f, noCover.PassCoverChance, 4);
            Assert.Equal(0.5f, withCover.PassCoverChance, 4);
            Assert.True(withCover.TotalEstimatedHitChance < noCover.TotalEstimatedHitChance);
            Assert.Equal(noCover.TotalEstimatedHitChance * 0.5f, withCover.TotalEstimatedHitChance, 4);
            Assert.Equal(0.5f, CoverUtility.CalculateOverallBlockChance(new[] { new CoverInfo(0.5f) }), 4);
        }

        [Fact]
        public void Aim_on_target_chance_is_floored_and_scaled_by_target_size()
        {
            Pawn shooter = NewHuman();
            Pawn target = NewHuman();
            var terribleAccuracy = new VerbProperties { accuracyTouch = 0.001f, accuracyShort = 0.001f, accuracyMedium = 0.001f, accuracyLong = 0.001f };
            var verb = new Verb_LaunchProjectile(shooter, terribleAccuracy);
            ShotReport report = ShotReport.HitReportFor(shooter, verb, target, 5f, Array.Empty<CoverInfo>());
            Assert.Equal(ShotReport.MinChance, report.AimOnTargetChance_StandardTarget, 5);

            var full = new VerbProperties { accuracyTouch = 1f, accuracyShort = 1f, accuracyMedium = 1f, accuracyLong = 1f };
            var verb2 = new Verb_LaunchProjectile(shooter, full);
            ShotReport smallTarget = ShotReport.HitReportFor(shooter, verb2, target, 0f, Array.Empty<CoverInfo>());
            Assert.Equal(1f, smallTarget.factorFromTargetSize, 4); // human body size 1 (floor is 0.5, doesn't apply here)
        }

        // ---- ranged verb state machine ----

        [Fact]
        public void Warmup_and_cooldown_take_exactly_the_configured_ticks()
        {
            ThingDef revolver = DefDatabase<ThingDef>.GetNamed("Gun_Revolver");
            VerbProperties props = revolver.verbs![0];
            int warmupTicks = global::SimWorld.Combat.Verb.SecondsToTicks(props.warmupTime);
            int cooldownTicks = global::SimWorld.Combat.Verb.SecondsToTicks(props.defaultCooldownTime);

            Pawn shooter = NewHuman();
            Pawn target = NewHuman();
            var verb = new Verb_LaunchProjectile(shooter, props);

            Assert.True(verb.TryStartCastOn(target, 5f));
            Assert.Equal(VerbState.WarmingUp, verb.State);
            Assert.False(verb.Available());

            AdvanceVerb(verb, warmupTicks - 1);
            Assert.Null(verb.LastShot);
            Assert.Equal(VerbState.WarmingUp, verb.State);

            AdvanceVerb(verb, 1);
            Assert.NotNull(verb.LastShot);
            Assert.Equal(VerbState.Cooldown, verb.State); // burst count 1: straight to cooldown

            AdvanceVerb(verb, cooldownTicks - 1);
            Assert.Equal(VerbState.Cooldown, verb.State);
            Assert.False(verb.Available());

            AdvanceVerb(verb, 1);
            Assert.Equal(VerbState.Idle, verb.State);
            Assert.True(verb.Available());
        }

        [Fact]
        public void Burst_fires_three_shots_spaced_ten_ticks_apart()
        {
            ThingDef rifle = DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle");
            VerbProperties props = rifle.verbs![0];
            Assert.Equal(3, props.burstShotCount);
            Assert.Equal(10, props.ticksBetweenBurstShots);

            Pawn shooter = NewHuman();
            Pawn target = NewHuman();
            var verb = new Verb_LaunchProjectile(shooter, props);

            Assert.True(verb.TryStartCastOn(target, 5f));
            AdvanceVerb(verb, global::SimWorld.Combat.Verb.SecondsToTicks(props.warmupTime));
            int shot1 = verb.LastShotTick;
            Assert.NotNull(verb.LastShot);

            AdvanceVerb(verb, 10);
            int shot2 = verb.LastShotTick;
            Assert.Equal(10, shot2 - shot1);

            AdvanceVerb(verb, 10);
            int shot3 = verb.LastShotTick;
            Assert.Equal(10, shot3 - shot2);

            Assert.Equal(VerbState.Cooldown, verb.State);
        }

        [Fact]
        public void Revolver_hit_rate_over_many_shots_matches_the_reported_chance()
        {
            ThingDef revolver = DefDatabase<ThingDef>.GetNamed("Gun_Revolver");
            VerbProperties props = revolver.verbs![0];
            Pawn shooter = NewHuman("Shooter");
            Pawn target = NewHuman("Target");
            var verb = new Verb_LaunchProjectile(shooter, props);

            ShotReport report = ShotReport.HitReportFor(shooter, verb, target, 10f, Array.Empty<CoverInfo>());
            float expected = report.TotalEstimatedHitChance;
            Assert.InRange(expected, 0.05f, 0.95f); // sanity: not a degenerate edge case

            int hits = 0;
            const int trials = 1000;
            for (int i = 0; i < trials; i++)
            {
                Assert.True(verb.TryStartCastOn(target, 10f));
                RunVerbToIdle(verb);
                Assert.NotNull(verb.LastShot);
                if (verb.LastShot!.Hit) hits++;
            }
            float hitRate = hits / (float)trials;
            Assert.InRange(hitRate, expected - 0.06f, expected + 0.06f);
        }

        [Fact]
        public void A_hit_wounds_the_target_with_the_projectiles_damage_and_armor_penetration()
        {
            ThingDef revolver = DefDatabase<ThingDef>.GetNamed("Gun_Revolver");
            VerbProperties props = revolver.verbs![0];
            ProjectileProperties projectile = DefDatabase<ThingDef>.GetNamed("Bullet_Revolver").projectile!;
            Pawn shooter = NewHuman("Shooter");

            // Find a shot that actually lands, then check the wound it left matches the projectile exactly.
            for (int i = 0; i < 200; i++)
            {
                Pawn target = NewHuman("Target" + i);
                var verb = new Verb_LaunchProjectile(shooter, props);
                Assert.True(verb.TryStartCastOn(target, 5f));
                RunVerbToIdle(verb);
                ShotResult shot = verb.LastShot!;
                if (!shot.Hit) continue;

                Assert.NotNull(shot.HitPart);
                Assert.NotNull(shot.DamageResult);
                Assert.True(shot.DamageResult!.wounded);
                Hediff injury = Assert.Single(shot.DamageResult.hediffs);
                Assert.Equal(projectile.damageAmountBase, injury.Severity, 3); // no armor: severity == raw damage amount
                return;
            }
            throw new InvalidOperationException("no shot connected in 200 tries");
        }

        // ---- melee ----

        [Theory]
        [InlineData(0, 0.5f)]
        [InlineData(5, 0.62f)]
        [InlineData(10, 0.78f)]
        [InlineData(15, 0.9f)]
        [InlineData(20, 0.98f)]
        public void Melee_hit_chance_curve_matches_its_anchor_points(int level, float expected)
        {
            Assert.Equal(expected, CombatStats.MeleeHitChance(level), 4);
        }

        [Theory]
        [InlineData(0, 0f)]
        [InlineData(5, 0.05f)]
        [InlineData(10, 0.1f)]
        [InlineData(15, 0.2f)]
        [InlineData(20, 0.3f)]
        public void Melee_dodge_chance_curve_matches_its_anchor_points(int level, float expected)
        {
            Assert.Equal(expected, CombatStats.MeleeDodgeChance(level), 4);
        }

        [Fact]
        public void Default_combat_skills_report_level_four()
        {
            Pawn p = NewHuman();
            Assert.Equal(4, CombatStats.Skills.ShootingLevel(p));
            Assert.Equal(4, CombatStats.Skills.MeleeLevel(p));
            Assert.Equal(CombatStats.MeleeHitChance(4), CombatStats.MeleeHitChanceFor(p), 4);
        }

        [Fact]
        public void Melee_attack_hit_wounds_a_random_part_with_the_tools_power()
        {
            ThingDef knife = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife");
            Tool cutTool = knife.tools!.First(t => t.HasCapacity(ToolCapacityDefOf.Cut));
            Pawn attacker = NewHuman("Attacker");

            for (int i = 0; i < 200; i++)
            {
                Pawn target = NewHuman("Target" + i);
                Verb_MeleeAttack verb = MeleeVerbUtility.MakeVerb(attacker, cutTool)!;
                Assert.True(verb.TryStartCastOn(target, 0f));
                AdvanceVerb(verb, 1); // melee has zero warmup: one tick always fires
                Assert.NotNull(verb.LastResult);
                if (verb.LastResult!.Outcome != MeleeAttackOutcome.Hit) continue;

                Assert.NotNull(verb.LastResult.HitPart);
                Assert.Equal(10f, verb.LastResult.DamageAmount, 3);
                Assert.NotNull(verb.LastResult.DamageResult);
                Assert.True(verb.LastResult.DamageResult!.wounded);
                return;
            }
            throw new InvalidOperationException("no melee hit landed in 200 tries");
        }

        [Fact]
        public void Downed_target_can_be_missed_or_hit_but_never_dodges()
        {
            ThingDef club = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club");
            Tool bluntTool = club.tools!.First(t => t.HasCapacity(ToolCapacityDefOf.Blunt));
            Pawn attacker = NewHuman("Attacker");
            Pawn target = NewHuman("Target");
            target.health.ForceDowned = true;
            Assert.True(target.Downed);

            bool sawHit = false;
            for (int i = 0; i < 200; i++)
            {
                Verb_MeleeAttack verb = MeleeVerbUtility.MakeVerb(attacker, bluntTool)!;
                Assert.True(verb.TryStartCastOn(target, 0f));
                AdvanceVerb(verb, 1);
                Assert.NotEqual(MeleeAttackOutcome.Dodged, verb.LastResult!.Outcome);
                if (verb.LastResult.Outcome == MeleeAttackOutcome.Hit) sawHit = true;
            }
            Assert.True(sawHit);
        }

        [Fact]
        public void Maneuvers_resolve_from_tool_capacity()
        {
            ThingDef knife = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife");
            Tool cutTool = knife.tools!.First(t => t.HasCapacity(ToolCapacityDefOf.Cut));
            Tool stabTool = knife.tools!.First(t => t.HasCapacity(ToolCapacityDefOf.Stab));
            Assert.Same(DefDatabase<ManeuverDef>.GetNamed("Slash"), ManeuverUtility.FindManeuver(cutTool));
            Assert.Same(DefDatabase<ManeuverDef>.GetNamed("Stab"), ManeuverUtility.FindManeuver(stabTool));
            Assert.Same(DamageDefOf.Cut, ManeuverUtility.FindManeuver(cutTool)!.verb.meleeDamageDef);
        }

        // ---- armor ----

        [Fact]
        public void No_armor_sources_leaves_damage_completely_unchanged()
        {
            Pawn p = NewHuman();
            BodyPartRecord torso = Part(p, "torso");
            DamageDef dmg = DamageDefOf.Cut;
            float amount = ArmorUtility.GetPostArmorDamage(p, 10f, 0f, torso, ref dmg, out bool deflected, out bool diminished);
            Assert.Equal(10f, amount);
            Assert.False(deflected);
            Assert.False(diminished);
            Assert.Same(DamageDefOf.Cut, dmg);
        }

        [Fact]
        public void Armor_at_or_below_penetration_never_deflects()
        {
            Pawn p = NewHuman();
            PawnArmor.Register(p, new FixedArmor(0.15f));
            BodyPartRecord torso = Part(p, "torso");
            for (int i = 0; i < 200; i++)
            {
                DamageDef dmg = DamageDefOf.Cut;
                float amount = ArmorUtility.GetPostArmorDamage(p, 10f, 0.2f, torso, ref dmg, out bool deflected, out bool diminished);
                Assert.False(deflected);
                Assert.False(diminished);
                Assert.Equal(10f, amount);
            }
        }

        [Fact]
        public void Armor_bands_match_the_expected_probabilities()
        {
            Pawn p = NewHuman();
            PawnArmor.Register(p, new FixedArmor(0.6f));
            BodyPartRecord torso = Part(p, "torso");

            int deflected = 0, diminished = 0, full = 0;
            const int trials = 4000;
            for (int i = 0; i < trials; i++)
            {
                DamageDef dmg = DamageDefOf.Cut;
                float amount = ArmorUtility.GetPostArmorDamage(p, 10f, 0.18f, torso, ref dmg, out bool wasDeflected, out bool wasDiminished);
                if (wasDeflected) { deflected++; Assert.Equal(0f, amount); }
                else if (wasDiminished) { diminished++; Assert.Same(DamageDefOf.Blunt, dmg); }
                else { full++; Assert.Equal(10f, amount); }
            }
            // num = max(0.6 - 0.18, 0) = 0.42 -> P(deflect)=0.21, P(diminish)=0.21, P(full)=0.58.
            Assert.InRange(deflected / (float)trials, 0.15f, 0.27f);
            Assert.InRange(diminished / (float)trials, 0.15f, 0.27f);
            Assert.InRange(full / (float)trials, 0.5f, 0.66f);
        }

        [Fact]
        public void Sharp_damage_diminished_by_armor_becomes_a_blunt_injury_on_skin_and_inside()
        {
            bool foundSkin = false, foundInside = false;
            for (int i = 0; i < 400 && !(foundSkin && foundInside); i++)
            {
                if (!foundSkin)
                {
                    Pawn p = NewHuman("Skin" + i);
                    PawnArmor.Register(p, new FixedArmor(0.9f));
                    DamageResult result = DamageDefOf.Cut.Worker.Apply(new DamageInfo(DamageDefOf.Cut, 10f, 0f, hitPart: Part(p, "left arm")), p);
                    if (result.diminished)
                    {
                        Assert.Equal("Bruise", Assert.Single(result.hediffs).def.defName);
                        foundSkin = true;
                    }
                }
                if (!foundInside)
                {
                    Pawn p = NewHuman("Inside" + i);
                    PawnArmor.Register(p, new FixedArmor(0.9f));
                    DamageResult result = DamageDefOf.Cut.Worker.Apply(new DamageInfo(DamageDefOf.Cut, 10f, 0f, hitPart: Part(p, "stomach")), p);
                    if (result.diminished)
                    {
                        Assert.Equal("Crush", Assert.Single(result.hediffs).def.defName);
                        foundInside = true;
                    }
                }
            }
            Assert.True(foundSkin, "never observed a diminished skin hit");
            Assert.True(foundInside, "never observed a diminished internal hit");
        }

        [Fact]
        public void Deflected_hit_adds_no_injury()
        {
            Pawn p = NewHuman();
            PawnArmor.Register(p, new FixedArmor(2f)); // rating far above any penetration: always deflects
            DamageResult result = DamageDefOf.Cut.Worker.Apply(new DamageInfo(DamageDefOf.Cut, 10f, 0f, hitPart: Part(p, "torso")), p);
            Assert.True(result.deflected);
            Assert.False(result.wounded);
            Assert.Empty(result.hediffs);
        }
    }
}
