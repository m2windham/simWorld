using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Factions
{
    /// <summary>The downed → captured → prisoner loop (system 12: Combat — capture).</summary>
    public class CaptureTests : ContentTestBase
    {
        public CaptureTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static like TickManager/Storyteller but ContentTestBase (Sim/**, out
            // of this lane's reach) does not reset it — see FactionTests' own identical constructor comment.
            Find.FactionManager = new FactionManager();
        }

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static (Faction captor, Faction hostileOther) HostileFactions()
        {
            Faction captor = NewFaction("Captors");
            Faction other = NewFaction("Raiders");
            captor.SetRelationDirect(other, FactionRelationKind.Hostile, -100);
            return (captor, other);
        }

        private static Pawn DownedPawn(Faction faction, string name = "Victim")
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            p.health.ForceDowned = true;
            return p;
        }

        // ---- content ----

        [Fact]
        public void Prisoner_interaction_mode_content_loads_with_no_errors_and_defofs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(PrisonerInteractionModeDefOf.NoInteraction);
            Assert.NotNull(PrisonerInteractionModeDefOf.AttemptRecruit);
            Assert.NotSame(PrisonerInteractionModeDefOf.NoInteraction, PrisonerInteractionModeDefOf.AttemptRecruit);
        }

        // ---- CanCapture / Capture ----

        [Fact]
        public void A_standing_hostile_pawn_cannot_be_captured()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn standing = NewHuman();
            standing.faction = other;
            Assert.False(standing.Downed);

            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Assert.False(CaptureUtility.CanCapture(captorPawn, standing));
        }

        [Fact]
        public void A_downed_pawn_of_the_same_faction_cannot_be_captured()
        {
            Faction faction = NewFaction("OneFaction");
            Pawn victim = DownedPawn(faction);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = faction;
            Assert.False(CaptureUtility.CanCapture(captorPawn, victim));
        }

        [Fact]
        public void A_downed_pawn_of_a_non_hostile_faction_cannot_be_captured()
        {
            Faction captor = NewFaction("Captors");
            Faction neutral = NewFaction("Neutrals");
            captor.SetRelationDirect(neutral, FactionRelationKind.Neutral, 0);
            Pawn victim = DownedPawn(neutral);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Assert.False(CaptureUtility.CanCapture(captorPawn, victim));
        }

        [Fact]
        public void A_downed_hostile_pawn_can_be_captured_and_becomes_a_prisoner()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;

            Assert.True(CaptureUtility.CanCapture(captorPawn, victim));
            Pawn_GuestTracker? tracker = CaptureUtility.Capture(captorPawn, victim);

            Assert.NotNull(tracker);
            Assert.True(tracker!.IsPrisoner);
            Assert.Same(PrisonerInteractionModeDefOf.NoInteraction, tracker.interactionMode);
            Assert.InRange(tracker.resistance, CaptureUtility.DefaultInitialResistanceRange.min, CaptureUtility.DefaultInitialResistanceRange.max);
            Assert.Same(captor, CaptureUtility.FindHostFaction(victim));
            Assert.Contains(tracker, captor.prisoners);

            // The pawn's own faction is untouched by mere capture — only recruiting (or defecting) changes it.
            Assert.Same(other, victim.faction);
        }

        [Fact]
        public void An_already_captured_pawn_cannot_be_captured_again()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Assert.NotNull(CaptureUtility.Capture(captorPawn, victim));

            Faction rival = NewFaction("Rivals");
            rival.SetRelationDirect(other, FactionRelationKind.Hostile, -100);
            Pawn rivalPawn = NewHuman("RivalCaptor");
            rivalPawn.faction = rival;

            Assert.False(CaptureUtility.CanCapture(rivalPawn, victim));
            Assert.Null(CaptureUtility.Capture(rivalPawn, victim));
        }

        // ---- WardenUtility: the warden work type's actual job ----

        [Fact]
        public void A_warden_visit_is_a_no_op_when_the_prisoner_is_not_set_to_be_recruited()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(captorPawn, victim)!;
            float before = tracker.resistance;

            Pawn warden = NewHuman("Warden");
            warden.faction = captor;
            WardenUtility.InteractionOutcome outcome = WardenUtility.TryInteract(warden, victim);

            Assert.Equal(WardenUtility.InteractionOutcome.NotEligible, outcome);
            Assert.Equal(before, tracker.resistance);
        }

        [Fact]
        public void A_warden_from_a_different_faction_cannot_interact_with_someone_elses_prisoner()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(captorPawn, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;

            Faction stranger = NewFaction("Strangers");
            Pawn strangerWarden = NewHuman("StrangerWarden");
            strangerWarden.faction = stranger;

            Assert.Equal(WardenUtility.InteractionOutcome.NotEligible, WardenUtility.TryInteract(strangerWarden, victim));
        }

        [Fact]
        public void Repeated_warden_visits_wear_resistance_down_then_recruit_the_prisoner()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(captorPawn, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
            float startingResistance = tracker.resistance;

            Pawn warden = NewHuman("Warden");
            warden.faction = captor;

            int visits = 0;
            WardenUtility.InteractionOutcome outcome;
            do
            {
                outcome = WardenUtility.TryInteract(warden, victim);
                visits++;
                Assert.True(visits < 1000, "resistance never reached zero");
            } while (outcome == WardenUtility.InteractionOutcome.ResistanceReduced);

            Assert.Equal(WardenUtility.InteractionOutcome.Recruited, outcome);
            // Every visit but the last reduced resistance by exactly the base amount (this port carries no
            // negotiator-skill/mood/opinion modifiers — see WardenUtility's own remarks).
            Assert.Equal((int)System.Math.Ceiling(startingResistance / WardenUtility.BaseResistanceReductionPerInteraction) + 1, visits);
            Assert.Same(captor, victim.faction);
            Assert.DoesNotContain(tracker, captor.prisoners);
            Assert.Null(CaptureUtility.FindHostFaction(victim));
        }

        // ---- Release ----

        [Fact]
        public void Releasing_a_prisoner_frees_it_and_clears_its_faction()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            CaptureUtility.Capture(captorPawn, victim);

            Assert.True(WardenUtility.Release(victim));

            Assert.Null(CaptureUtility.FindHostFaction(victim));
            Assert.Null(victim.faction);
            Assert.Empty(captor.prisoners);
        }

        [Fact]
        public void Releasing_a_pawn_that_is_not_held_by_anyone_does_nothing()
        {
            Pawn free = NewHuman();
            Assert.False(WardenUtility.Release(free));
        }

        // ---- integration: a turret does not target its own captured prisoners ----

        [Fact]
        public void A_captured_prisoner_of_a_faction_is_no_longer_hostile_to_that_same_faction()
        {
            (Faction captor, Faction other) = HostileFactions();
            Pawn victim = DownedPawn(other);
            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            CaptureUtility.Capture(captorPawn, victim);

            // The pawn's own .faction still names the hostile faction it came from — this is exactly what a
            // targeting check (e.g. CompTurretGun.IsHostile) must special-case via FindHostFaction, not via
            // Faction.HostileTo alone.
            Assert.Same(other, victim.faction);
            Assert.True(captor.HostileTo(victim.faction!));
            Assert.Same(captor, CaptureUtility.FindHostFaction(victim));
        }

        // ---- Scribe ----

        /// <summary>
        /// A minimal composite root purely so this test can exercise a real cross-reference resolution: the
        /// prisoner tracker's <c>pawn</c> field is a <c>Scribe_References</c> pointer, which only resolves
        /// when the referenced Pawn is deep-saved somewhere in the very same document — normally that is "a
        /// Map, inside a whole-game save" (RimWorld: everything shares one root). Nothing in this codebase
        /// yet deep-saves a Map and its Factions together in one place (<c>Sim.Game</c> does not persist Maps
        /// at all — out of this lane's reach), so this test root stands in for that missing whole-game
        /// document just far enough to prove the tracker's own reference mechanics are correct.
        /// </summary>
        private sealed class CaptureRoot : IExposable
        {
            public CoreMap? map;
            public List<Faction> factions = new List<Faction>();

            public void ExposeData()
            {
                CoreMap? m = map;
                Scribe_Deep.Look(ref m, "map");
                map = m;
                List<Faction>? f = factions;
                Scribe_Collections.Look(ref f, "factions", LookMode.Deep);
                factions = f ?? new List<Faction>();
            }
        }

        [Fact]
        public void Prisoner_state_round_trips_through_scribe()
        {
            (Faction captor, Faction other) = HostileFactions();
            var map = new CoreMap(5, 5, TerrainDefOf.Soil);
            Pawn victim = DownedPawn(other);
            GenSpawn.Spawn(victim, new IntVec3(2, 0, 2), map);

            Pawn captorPawn = NewHuman("Captor");
            captorPawn.faction = captor;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(captorPawn, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
            tracker.resistance = 3.5f;
            tracker.will = 1.5f;

            // Both factions, not just the captor: captor's own relations reference "other", and the victim's
            // own (untouched-by-capture) Pawn.faction does too — a real whole-game save always includes every
            // faction together for exactly this reason (FactionManager.ExposeData deep-saves the full roster).
            var root = new CaptureRoot { map = map, factions = new List<Faction> { captor, other } };
            string xml = Scribe.SaveToString(root, "root");
            CaptureRoot loaded = Scribe.Load<CaptureRoot>(xml, "root", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Faction loadedCaptor = loaded.factions.Single(f => f.name == "Captors");
            Pawn_GuestTracker loadedTracker = Assert.Single(loadedCaptor.prisoners);
            Assert.NotNull(loadedTracker.pawn);
            Assert.True(loadedTracker.IsPrisoner);
            Assert.Equal("AttemptRecruit", loadedTracker.interactionMode.defName);
            Assert.Equal(3.5f, loadedTracker.resistance);
            Assert.Equal(1.5f, loadedTracker.will);
            Assert.Same(loadedTracker.pawn, loaded.map!.mapPawns.AllPawns[0]);
        }
    }
}
