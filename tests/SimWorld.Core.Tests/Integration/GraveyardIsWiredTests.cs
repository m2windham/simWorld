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
    /// That the settlement buries its dead in a <i>game</i>, not merely when a test calls
    /// <see cref="GraveyardInitiative"/>. A founded settlement is opened through the god seam, three of its
    /// citizens die where they stand, and the real loop is ticked — nothing is called by hand. The same hazard
    /// <c>AbstractConstructionIsWiredTests</c> records: a mechanism that works for the test that drives it and
    /// is never reached by the game. Delete the line in <c>Sim/Game.cs</c>'s <c>WireTickHooks</c> and this fails.
    /// </summary>
    public class GraveyardIsWiredTests : ContentTestBase
    {
        public GraveyardIsWiredTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_real_game_plans_graves_for_the_citizens_who_die_in_it()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "graveyard-wired",
                subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));

            List<Pawn> doomed = settlement.Citizens.Where(p => p.Spawned).Take(3).ToList();
            Assert.Equal(3, doomed.Count);
            foreach (Pawn p in doomed) p.health.Kill(null, null);
            Assert.Equal(3, map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count);

            for (int i = 0; i < GraveyardTuning.IntervalTicks * 3; i++) game.TickManager.DoSingleTick();

            int graves = map.listerThings.ThingsOfDef(GraveDefOf.Grave).Count
                + map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count(t => t is Blueprint bp && ReferenceEquals(bp.EntityToBuild, GraveDefOf.Grave))
                + map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count(t => t is Frame f && ReferenceEquals(f.EntityToBuild, GraveDefOf.Grave));
            Assert.True(graves >= 1, "three citizens died on the map and the real loop planned no grave at all: "
                + "GraveyardInitiative is almost certainly not wired into Sim/Game.cs's WireTickHooks.");
            Assert.True(graves <= 3, "never more graves than bodies");
        }

        [Fact]
        public void A_citizen_who_lies_in_a_grave_can_still_be_opened_in_the_god_view()
        {
            // The point of burying somebody is that the settlement knows where they lie, so the view that
            // opens a dead citizen from a body on the map has to look in the graves as well: a buried citizen
            // is not a Thing on the map, and before this was handled they dead-ended the moment they were buried
            // (the same hole GodViewSnapshot.Remembered exists to catch for those with no body at all).
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "graveyard-view",
                subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;

            Pawn citizen = settlement.Citizens.First(p => p.Spawned);
            int id = citizen.thingIDNumber;
            citizen.health.Kill(null, null);
            Assert.NotNull(citizen.corpse);
            CitizenView? onTheMap = GodViewSnapshot.Citizen(id);
            Assert.NotNull(onTheMap);

            var grave = (Building_Grave)ThingMaker.MakeThing(GraveDefOf.Grave);
            GenSpawn.Spawn(grave, new IntVec3(2, 0, 2), map);
            Assert.True(grave.TryAcceptThing(citizen.corpse!));
            Assert.False(citizen.corpse!.Spawned);

            CitizenView? buried = GodViewSnapshot.Citizen(id);
            Assert.NotNull(buried);
            Assert.Equal(onTheMap!.Name, buried!.Name);
        }
    }
}
