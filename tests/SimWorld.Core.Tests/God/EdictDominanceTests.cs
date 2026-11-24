using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// An edict does what its text says. The playtest issued the hunter's mandate ("nothing else gets done")
    /// and watched 19 of 19 citizens go hauling, and in another session 15 of 20 go sowing.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EdictDominanceTests : ContentTestBase
    {
        public EdictDominanceTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static EdictDef Edict(string name) => DefDatabase<EdictDef>.GetNamed(name);

        /// <summary>Jobs a citizen under an exclusive edict may still be on: its own work (the hunt and what a
        /// kill leaves), needs, the emergency tier, fighting, a direct order, idling. Anything else is routine
        /// work the edict should have withheld.</summary>
        private static readonly HashSet<string> Exempt = new HashSet<string>
        {
            "Hunt", "Butcher", "Ingest", "LayDown", "Wait", "Wait_Wander", "GotoWander", "Goto", "TendPatient",
            "BeatFire", "ExtinguishSelf", "AttackMelee", "AttackStatic", "Flee", "FleeAndCower", "Rescue",
            "Equip", "Wear", "RemoveApparel", "SocialRelax", "Meditate", "Skygaze", "Wander", "GotoSafeTemperature",
        };

        /// <summary>Samples every citizen's current job every 250 ticks for <paramref name="ticks"/> ticks.</summary>
        private static Dictionary<string, int> SampleJobs(List<Pawn> citizens, int ticks)
        {
            var jobs = new Dictionary<string, int>();
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i % 250 != 0) continue;
                foreach (Pawn p in citizens)
                {
                    if (p.Dead || !p.Spawned) continue;
                    string k = p.jobs.curJob?.def.defName ?? "none";
                    jobs[k] = jobs.GetValueOrDefault(k) + 1;
                }
            }
            return jobs;
        }

        private static string Show(Dictionary<string, int> jobs) =>
            string.Join(", ", jobs.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value));

        [Fact]
        public void Under_the_hunters_mandate_the_hunt_dominates_and_routine_work_stops()
        {
            Settlement settlement = OpenedSettlement("hunters-mandate");
            CoreMap map = settlement.InteriorMap!;
            List<Pawn> citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();
            Assert.Contains(citizens, HuntUtility.HasHuntingWeapon); // otherwise nobody is an able hand

            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("HuntersMandate").Outcome);
            // The able hands: armed, and allowed to hunt (a backstory can disable it). The rest keep their routine.
            List<Pawn> able = citizens.Where(EdictWorkUtility.RoutineWorkWithheld).ToList();
            Assert.True(able.Count * 2 > citizens.Count, "most of the band should be able hands: " + able.Count + "/" + citizens.Count);
            // An edict does not interrupt the job a citizen is already on, so let those finish first.
            SampleJobs(able, 1000);
            Dictionary<string, int> jobs = SampleJobs(able, 4000);

            int routine = jobs.Where(kv => !Exempt.Contains(kv.Key) && kv.Key != "none").Sum(kv => kv.Value);
            int hunting = jobs.GetValueOrDefault("Hunt");
            // Measured before the fix: Hunt 31 of 320 samples, behind construction, harvest and cutting,
            // because the hunt's own "only while short of food" gate closed and routine work filled the rest.
            Assert.True(hunting > 0 && hunting >= 4 * routine, "hunting should dominate: " + Show(jobs));
        }

        [Fact]
        public void Rescinding_the_mandate_gives_routine_work_back()
        {
            Settlement settlement = OpenedSettlement("hunters-mandate-rescinded");
            CoreMap map = settlement.InteriorMap!;
            List<Pawn> citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("HuntersMandate").Outcome);
            Assert.Contains(citizens, EdictWorkUtility.RoutineWorkWithheld);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.RescindEdict("HuntersMandate").Outcome);
            Assert.All(citizens, p => Assert.False(EdictWorkUtility.RoutineWorkWithheld(p)));
        }

        [Fact]
        public void A_citizen_who_cannot_hunt_is_not_an_able_hand_and_keeps_their_routine()
        {
            Settlement settlement = OpenedSettlement("unarmed-under-mandate");
            CoreMap map = settlement.InteriorMap!;
            Pawn unarmed = settlement.Citizens.First(p => p.Spawned && p.Map == map);
            foreach (var eq in unarmed.equipment.AllEquipmentListForReading.ToList()) unarmed.equipment.RemoveEquipment(eq);
            Assert.False(HuntUtility.HasHuntingWeapon(unarmed));

            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("HuntersMandate").Outcome);
            Assert.False(EdictWorkUtility.RoutineWorkWithheld(unarmed));
        }

        [Fact]
        public void An_edict_that_only_reorders_work_withholds_nothing()
        {
            var reorder = new EdictDef { defName = "TestReorder", prioritizedWork = new List<WorkTypeDef> { WorkTypeDefOf.Hunting } };
            Assert.False(reorder.exclusiveWork);
            Settlement settlement = OpenedSettlement("reorder-only");
            CoreMap map = settlement.InteriorMap!;
            Assert.True(Find.God.Activate(reorder));
            Assert.All(settlement.Citizens.Where(p => p.Spawned && p.Map == map), p => Assert.False(EdictWorkUtility.RoutineWorkWithheld(p)));
        }

        // ---- contradictory edicts ----

        [Fact]
        public void Which_shipped_edicts_contradict()
        {
            Assert.True(Edict("HuntersMandate").exclusiveWork);
            Assert.True(Edict("HuntersMandate").Contradicts(Edict("TakeUpArms")));
            Assert.True(Edict("TakeUpArms").Contradicts(Edict("HuntersMandate")));
            Assert.True(Edict("HuntersMandate").Contradicts(Edict("BreakTheSod")));
            Assert.True(Edict("BreakTheSod").Contradicts(Edict("GreatWorksMandate")));
            // Edicts that only put work first never contradict each other.
            Assert.False(Edict("GreatWorksMandate").Contradicts(Edict("IndustrialMobilization")));
            Assert.False(Edict("TakeUpArms").Contradicts(Edict("IronDiscipline")));
        }

        [Fact]
        public void Issuing_a_contradictory_edict_rescinds_the_other_and_says_so()
        {
            OpenedSettlement("contradiction");
            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("HuntersMandate").Outcome);

            GodCommandResult result = GodCommands.IssueEdict("TakeUpArms");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Contains(Edict("HuntersMandate").LabelCap, result.Reason);
            Assert.Contains("rescinded", result.Reason);
            Assert.True(Find.God.IsActive(Edict("TakeUpArms")));
            Assert.False(Find.God.IsIssued(Edict("HuntersMandate")));
            Assert.Contains(Find.Storyteller.Chronicle, entry => entry.incidentDefName ==
                "Edict rescinded: " + Edict("HuntersMandate").LabelCap + ", contradicted by " + Edict("TakeUpArms").LabelCap);

            // And back again: the mandate rescinds the call to arms.
            Assert.Contains("rescinded", GodCommands.IssueEdict("HuntersMandate").Reason);
            Assert.False(Find.God.IsIssued(Edict("TakeUpArms")));
        }
    }
}
