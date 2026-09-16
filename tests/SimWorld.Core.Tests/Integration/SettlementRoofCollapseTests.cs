using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>A test that watches the game rather than a module.</b> It founds a settlement the ordinary way —
    /// <see cref="Game.NewGame"/> on the tribal scenario, opened through the god seam — ticks eight days with
    /// nothing called by hand, and asks the question that two previous batches asked wrong: <i>what is killing
    /// these people?</i>
    ///
    /// <para/><b>What was killing them.</b> <c>docs/WORK-REGISTER.md</c> §9a read the death letters and
    /// recorded every death in a founded settlement's first week as a citizen beaten to death by another
    /// citizen. §10 corrected the reading — <c>"{0} has been beaten to death."</c> is the <c>Blunt</c>
    /// <c>DamageDef</c>'s own <c>deathMessage</c>, and the roof collapse dealt <c>Blunt</c> with no instigator
    /// — and named roof collapse as the largest killer in the game, unfixed. Measured here on three seeds
    /// before this lane, watched, eight days, twenty-five founders (corpses counted on the map, because
    /// <c>Settlement.PruneDeadCitizens</c> takes the dead off the roster):
    ///
    /// <code>
    /// seed     alive d8   dead   killed by a pawn   roof cells lost      rock cells mined
    /// roof-a      11/25     14                  0   7 486 of 14 590      8 044 of 12 974
    /// roof-b      11/25     14                  2   6 271 of 14 584      7 802 of 12 984
    /// roof-c      17/25      8                  1   5 491 of 14 501      7 476 of 12 965
    /// </code>
    ///
    /// <para/>36 deaths, 33 with no instigator at all, 31 of those carrying the collapse signature — a
    /// hundred-point bruise on a torso, or a destroyed heart, brain, liver, ribcage or sternum on a body with
    /// almost nothing else wrong with it. The three deaths that did have a killer were animal bites.
    ///
    /// <para/><b>The mechanism, and it was two mechanisms.</b> Nothing designates mining in this port — there
    /// is no designation layer at all — so <c>AI.WorkGiver_Miner</c> mines every reachable mineable edifice on
    /// the map. On day one of a founded settlement it mines nothing, because every other work type outranks it;
    /// from day two, with the easy work done, twenty-five citizens mine <b>two thousand rock cells a day</b>,
    /// including the rock holding up their own ceiling. What then fell on them was unfaithful in every detail:
    /// the wrong <c>DamageDef</c>, no body region (so a 50-point hit could destroy a heart), twice RimWorld's
    /// ordinary-roof damage, and a thick roof that vanished instead of refilling its cell with rock — so each
    /// collapse widened the hole for the next. Both halves are fixed: see
    /// <see cref="RoofCollapserImmediate"/> for what a falling roof does, and
    /// <see cref="RoofCollapseUtility.WouldCollapseRoofIfRemoved"/> for the translation that keeps a miner from
    /// digging out its own support — asked twice, once when the job is handed out and again when the rock is
    /// actually taken, because twenty-five people mining one seam make the first answer go stale.
    ///
    /// <para/><b>What this asserts, and what it deliberately does not.</b> It asserts the three things this
    /// lane changed and can explain: that citizens are not being crushed at scale, that the settlement's roof
    /// is still over its head at the end of the week, and that mining still happens — the fix is a rule about
    /// <i>which</i> cells, not a work type switched off. It does <b>not</b> assert that the population is
    /// untouched: other causes of death in this game are other lanes' (§10 lists them), and a test that
    /// asserted survival would go red for reasons it cannot explain — the mistake <c>SettlementFoodTests</c>
    /// already names.
    /// </summary>
    public class SettlementRoofCollapseTests : ContentTestBase
    {
        public SettlementRoofCollapseTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Days = 8;
        private const int BandSize = 25;

        [Fact]
        public void A_founded_settlement_is_not_crushed_by_its_own_ceiling()
        {
            // The seed is pinned, and it is pinned for a reason rather than for a number: this question can
            // only be asked on a map that has a mountain on it, and the tile a seed lands on decides that —
            // the first seed tried here generated a Flat tile whose MapGenTuning.TargetRockFraction is zero,
            // so there was no rock to mine and nothing to hold up. "roof-a" is one of the three the before
            // and after runs in this class's remarks were measured on.
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "roof-a",
                subdivisionOverride: 3, soloStart: true, bandSize: BandSize);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            List<Pawn> founders = settlement.Citizens.ToList();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;

            int roofedBefore = CountRoofed(map);
            int thickBefore = CountThickRoofed(map);
            int rockBefore = CountMineable(map);

            // A map with no mountain on it cannot answer this question at all; every tribal start this port
            // generates has one, and the assertions below would be vacuous without it.
            Assert.True(roofedBefore > 0, "a generated interior carried no roof at all");
            Assert.True(rockBefore > 0, "a generated interior carried no mineable rock at all");

            for (int day = 0; day < Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
            }

            int roofedAfter = CountRoofed(map);
            int thickAfter = CountThickRoofed(map);
            int rockAfter = CountMineable(map);

            // (1) The thing §10 named. Counted by the damage that only a falling roof deals, so it cannot be
            // confused with any other cause the way the death letters were. A band, not a figure: a settlement
            // where more than one citizen in ten is crushed inside a week is being killed by its own ceiling,
            // which is what "the largest killer in this game" meant.
            int crushed = founders.Count(p => p.Dead && p.health.DeathCauseDamage == RoofCollapseDefOf.Crush);
            Assert.True(crushed * 10 < founders.Count,
                crushed + " of " + founders.Count + " founders were crushed under a roof in " + Days
                + " days — before this lane it was eleven to fourteen, and RimWorld's answer is that a colony"
                + " nobody has told to dig does not dig out its own supports");

            // (2) And the map-level reading of the same thing, which is what made it visible: the settlement
            // ate its own ceiling — roughly half the roof on the map was gone by day eight, and it never came
            // back, because a collapsed thick roof was deleted instead of being refilled with rock.
            // Overhead mountain is now the thing it is in RimWorld: it does not go away
            // (RoofDef.VanishOnCollapse is !isThickRoof), so this count cannot fall at all.
            Assert.True(thickAfter >= thickBefore,
                "the map lost " + (thickBefore - thickAfter) + " cells of overhead mountain in " + Days
                + " days — thick roof does not vanish when it collapses, it refills its cell with rock");

            // What can still legitimately fall is the thin rock fringe and anything built, and a settlement
            // that is not digging out its own supports should barely lose any of it. A tenth of the whole
            // ceiling is the band — before this lane it was between a third and a half.
            Assert.True(roofedAfter * 10 >= roofedBefore * 9,
                "the map lost " + (roofedBefore - roofedAfter) + " of " + roofedBefore
                + " roofed cells in " + Days + " days");

            // (3) The fix is a rule about which cells, not mining switched off. If this ever goes red the
            // settlement has stopped mining altogether and assertions (1) and (2) are passing for free.
            Assert.True(rockAfter < rockBefore,
                "no rock at all was mined in " + Days + " days: the roof is safe because nobody is working");

            // (4) The reading that sent two batches after a murderer. Kept here, at the settlement scale,
            // because this is the test whose subject it is: none of these deaths has a killer.
            int murdered = founders.Count(p => p.health.KilledByAnotherPawn);
            Assert.True(murdered * 10 < founders.Count,
                murdered + " of " + founders.Count + " founders were killed by another pawn with no hostile"
                + " ever reaching the map");
        }

        private static int CountRoofed(SimWorld.Map.Map map)
        {
            int n = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (map.roofGrid.Roofed(c)) n++;
            }
            return n;
        }

        private static int CountThickRoofed(SimWorld.Map.Map map)
        {
            int n = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                RoofDef? roof = map.roofGrid.RoofAt(c);
                if (roof != null && roof.isThickRoof) n++;
            }
            return n;
        }

        private static int CountMineable(SimWorld.Map.Map map)
        {
            int n = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                Thing? edifice = map.edificeGrid[c];
                if (edifice != null && edifice.def.mineable) n++;
            }
            return n;
        }
    }
}
