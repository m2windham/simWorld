using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// While at war only a pact of non-aggression can be signed: a treaty without non-aggression cannot end the
    /// war, and recording it left a trade agreement in force between two civilizations still fighting. The
    /// refusal is per treaty, so the view's answer is too (<see cref="FactionRelationView.TreatyOffers"/>), and it
    /// comes from the same <c>GodCommands.TreatyRefusal</c> the command checks.
    /// </summary>
    public class TreatyAtWarTests : ContentTestBase
    {
        public TreatyAtWarTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static Faction NewFaction(string defName, string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);

        private static (Faction player, Faction enemy) AtWar()
        {
            Faction player = NewFaction("PlayerCivilization", "WarPlayer");
            Faction enemy = NewFaction("TribalCivilization", "WarTribe");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(enemy);
            player.SetRelationDirect(enemy, FactionRelationKind.Neutral, 0);
            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(DiplomacyActions.DeclareWar(player, enemy));
            return (player, enemy);
        }

        [Fact]
        public void A_trade_agreement_cannot_be_signed_at_war_and_the_war_stays_on()
        {
            (Faction player, Faction enemy) = AtWar();

            GodCommandResult result = GodCommands.SignTreaty(enemy.loadID, "TradeAgreement");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("at war", result.Reason);
            Assert.Contains("only a pact of non-aggression can end it", result.Reason);
            Assert.True(player.WarWith(enemy));
            Assert.Empty(player.RelationWith(enemy)!.treaties);
        }

        /// <summary>The core rule itself, not just the seam: the world's own diplomacy goes through
        /// <see cref="Faction.SignTreaty"/> too.</summary>
        [Fact]
        public void Faction_SignTreaty_refuses_a_treaty_without_non_aggression_at_war()
        {
            (Faction player, Faction enemy) = AtWar();
            TreatyDef trade = DefDatabase<TreatyDef>.GetNamed("TradeAgreement");
            Assert.False(trade.nonAggression);

            Assert.False(player.SignTreaty(enemy, trade));
            Assert.True(player.WarWith(enemy));
            Assert.Empty(player.RelationWith(enemy)!.treaties);
        }

        [Fact]
        public void A_non_aggression_pact_signed_at_war_ends_it()
        {
            (Faction player, Faction enemy) = AtWar();

            GodCommandResult result = GodCommands.SignTreaty(enemy.loadID, "NonAggressionPact");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.False(player.WarWith(enemy));
        }

        /// <summary>For every treaty, at war and at peace, the per-treaty view says exactly what the command then
        /// does, in the same words. Re-captured and re-posed before each, because signing a pact ends the war.</summary>
        [Fact]
        public void The_per_treaty_view_agrees_with_SignTreaty_at_war_and_at_peace()
        {
            bool sawRefusal = false, sawSigning = false;
            foreach (bool atWar in new[] { true, false })
            {
                foreach (TreatyOption treaty in TreatyCatalogue.Treaties())
                {
                    Find.FactionManager = new FactionManager();
                    Faction enemy;
                    if (atWar)
                    {
                        (_, enemy) = AtWar();
                    }
                    else
                    {
                        Faction player = NewFaction("PlayerCivilization", "PeacePlayer");
                        enemy = NewFaction("TribalCivilization", "PeaceTribe");
                        Find.FactionManager.Add(player);
                        Find.FactionManager.Add(enemy);
                        player.SetRelationDirect(enemy, FactionRelationKind.Neutral, 0);
                    }

                    FactionRelationView view = GodViewSnapshot.Capture().Civilizations
                        .Single(c => c.CivilizationId == enemy.loadID);
                    Assert.Equal(
                        TreatyCatalogue.Treaties().Select(t => t.DefName),
                        view.TreatyOffers.Select(o => o.DefName));
                    Assert.True(view.CanSignTreaty, "the per-civilization answer stays treaty-independent");
                    TreatyOfferView offer = view.TreatyOffers.Single(o => o.DefName == treaty.DefName);

                    GodCommandResult result = GodCommands.SignTreaty(enemy.loadID, treaty.DefName);

                    Assert.Equal(offer.CanSign, result.Outcome == GodCommandOutcome.Done);
                    if (offer.CanSign)
                    {
                        sawSigning = true;
                        Assert.Equal("Ready to sign.", offer.Reason);
                    }
                    else
                    {
                        sawRefusal = true;
                        Assert.Equal(offer.Reason, result.Reason);
                    }
                }
            }

            Assert.True(sawSigning);
            Assert.True(sawRefusal);
        }

        /// <summary>The seam's per-treaty check mirrors <see cref="Faction.SignTreaty"/> for every shipped treaty,
        /// at war and at peace, so a new rule there fails here rather than as a button the simulation refuses.</summary>
        [Fact]
        public void The_per_treaty_refusal_matches_Faction_SignTreaty_for_every_treaty()
        {
            var treaties = new List<TreatyDef>(DefDatabase<TreatyDef>.AllDefsListForReading);
            Assert.Contains(treaties, t => t.nonAggression);
            Assert.Contains(treaties, t => !t.nonAggression);

            int n = 0;
            foreach (bool atWar in new[] { true, false })
            {
                foreach (TreatyDef treaty in treaties)
                {
                    Faction a = NewFaction("PlayerCivilization", "MirrorA" + n);
                    Faction b = NewFaction("TribalCivilization", "MirrorB" + n);
                    n++;
                    a.SetRelationDirect(b, FactionRelationKind.Neutral, 0);
                    if (atWar) a.DeclareWar(b);

                    bool seamSaysSignable = GodCommands.TreatyRefusal(a, b, treaty) == null;
                    Assert.Equal(seamSaysSignable, a.SignTreaty(b, treaty));
                }
            }
        }
    }
}
