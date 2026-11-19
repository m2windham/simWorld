using System;
using SimWorld.AI;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Combat
{
    /// <summary>
    /// A melee blow lands on an outside part (RimWorld: <c>Verb_MeleeAttackDamage.DamageInfosToApply</c> calls
    /// <c>SetBodyRegion(BodyPartHeight.Undefined, BodyPartDepth.Outside)</c>). The port rolled the part with
    /// <see cref="BodyPartDepth.Undefined"/>, so a bite could land on the heart directly: on seed 2024 a
    /// colonist at 87% health died to one 15-damage muffalo bite that destroyed their heart.
    /// </summary>
    public class MeleeHitDepthTests : ContentTestBase
    {
        public MeleeHitDepthTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void An_animal_bite_only_ever_lands_on_an_outside_part()
        {
            Pawn muffalo = new Pawn(DefDatabase<ThingDef>.GetNamed("Muffalo"), "Muffalo");
            Tool teeth = AttackVerbUtility.NaturalWeaponFor(muffalo)!;
            Assert.NotNull(teeth);

            int hits = 0;
            for (int i = 0; i < 400; i++)
            {
                Pawn target = NewHuman("Target" + i);
                Verb_MeleeAttack verb = MeleeVerbUtility.MakeVerb(muffalo, teeth)!;
                Assert.True(verb.TryStartCastOn(target, 0f));
                Find.TickManager.DoSingleTick();
                verb.VerbTick();
                if (verb.LastResult!.Outcome != MeleeAttackOutcome.Hit) continue;

                hits++;
                BodyPartRecord part = verb.LastResult.HitPart ?? throw new InvalidOperationException("hit with no part");
                Assert.Equal(BodyPartDepth.Outside, part.depth);
                Assert.False(target.Dead, "a single bite on an outside part killed a healthy human via " + part.Label);
            }
            Assert.True(hits >= 100, "only " + hits + " bites landed; the sample is too small to mean anything");
        }
    }
}
