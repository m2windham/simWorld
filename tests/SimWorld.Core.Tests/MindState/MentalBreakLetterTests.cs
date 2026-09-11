using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.MindState
{
    /// <summary>
    /// The player being told that one of their people has broken down
    /// (<c>MentalStateDef.beginLetter</c>/<c>beginLetterLabel</c>/<c>beginLetterDef</c> →
    /// <see cref="MentalState.PostStart"/> → <see cref="LetterStack"/>).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> The letter stack shipped, six systems
    /// raised letters through it, the two fields sat on <see cref="MentalStateDef"/> from the day the module
    /// landed, and <c>PostStart</c> raised nothing — so a citizen could go berserk in the middle of a
    /// settlement and the civilization's narrator said nothing at all. Both halves were needed: the fields
    /// were unread <i>and</i> no shipped mental state filled them in.
    /// </summary>
    public class MentalBreakLetterTests : ContentTestBase
    {
        public MentalBreakLetterTests(CoreContentFixture content) : base(content)
        {
            Find.LetterStack = new LetterStack();
            Find.FactionManager = new FactionManager();
        }

        private static MentalStateDef State(string name) => DefDatabase<MentalStateDef>.GetNamed(name);

        private static IReadOnlyList<Letter> Letters => Find.LetterStack.LettersListForReading;

        private static Faction PlayerFaction()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Find.FactionManager.Add(f);
            return f;
        }

        private static Faction OtherFaction()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn Citizen(string name = "Citizen")
        {
            Pawn p = NewHuman(name);
            p.faction = PlayerFaction();
            return p;
        }

        // ---- the headline ----

        [Fact]
        public void A_citizen_going_berserk_tells_the_player()
        {
            Pawn p = Citizen("Aldra");
            Assert.Empty(Letters);

            Assert.True(p.mindState.mentalStateHandler.TryStartMentalState(State("Berserk")));

            Letter let = Assert.Single(Letters);
            Assert.Contains("Aldra", let.label);
            Assert.Contains("berserk", let.label);
            // The pawn's own name substituted into the content's {0}, not the template left in the letter.
            Assert.Contains("Aldra", let.text);
            Assert.DoesNotContain("{0}", let.text);
            // Pointed at whoever it is about, so a view can jump to them.
            Assert.Equal(new[] { p.GetUniqueLoadID() }, let.lookTargets);
        }

        [Fact]
        public void The_letter_kind_comes_from_the_def_and_defaults_to_a_negative_event()
        {
            // An aggro break is a danger to the town, and the shipped defs say so; the rest take the
            // fallback MentalState.PostStart supplies, because a Def field initialiser cannot name a Def.
            Citizen("Aggro").mindState.mentalStateHandler.TryStartMentalState(State("Berserk"));
            Assert.Equal("ThreatSmall", Letters[0].def.defName);

            Citizen("Sad").mindState.mentalStateHandler.TryStartMentalState(State("Wander_Sad"));
            Assert.Equal(LetterDefOf.NegativeEvent, Letters[1].def);
        }

        [Fact]
        public void The_reason_the_caller_gave_is_carried_into_the_letter()
        {
            Pawn p = Citizen();
            p.mindState.mentalStateHandler.TryStartMentalState(State("Tantrum"), "they were denied a grave");

            Assert.Contains("they were denied a grave", Assert.Single(Letters).text);
        }

        // ---- who is worth a letter ----

        [Fact]
        public void A_raider_breaking_down_in_someone_elses_town_is_not_news()
        {
            // The guard that keeps the channel readable. Every humanlike on a map runs the same break
            // machinery, so an unguarded letter would mean one notification per raider per siege.
            Pawn raider = NewHuman("Raider");
            raider.faction = OtherFaction();

            Assert.True(raider.mindState.mentalStateHandler.TryStartMentalState(State("Berserk")));
            Assert.True(raider.InMentalState);
            Assert.Empty(Letters);
        }

        [Fact]
        public void A_pawn_with_no_faction_at_all_is_not_news_either()
        {
            Pawn stranger = NewHuman("Stranger");
            Assert.True(stranger.mindState.mentalStateHandler.TryStartMentalState(State("Berserk")));
            Assert.Empty(Letters);
        }

        [Fact]
        public void A_prisoner_of_the_civilization_is_news()
        {
            // RimWorld's rule and this port's: the people you are responsible for include the ones you are
            // holding. A prisoner going berserk in your settlement is very much your problem.
            Faction ours = PlayerFaction();
            Faction theirs = OtherFaction();
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);

            Pawn warden = NewHuman("Warden");
            warden.faction = ours;
            Pawn captive = NewHuman("Captive");
            captive.faction = theirs;
            captive.health.ForceDowned = true;
            Assert.NotNull(CaptureUtility.Capture(warden, captive));
            captive.health.ForceDowned = false;

            Assert.True(captive.mindState.mentalStateHandler.TryStartMentalState(State("Berserk")));
            Assert.Contains("Captive", Assert.Single(Letters).label);
        }

        [Fact]
        public void A_state_with_no_begin_letter_stays_silent()
        {
            // Not an oversight in content: a social scuffle is over in seconds, and a null beginLetter is how
            // a content author says "not worth telling anyone".
            Pawn p = Citizen();
            Assert.Null(State("SocialFighting").beginLetter);

            Assert.True(p.mindState.mentalStateHandler.TryStartMentalState(State("SocialFighting")));
            Assert.True(p.InMentalState);
            Assert.Empty(Letters);
        }

        // ---- content ----

        [Fact]
        public void Every_shipped_mental_break_has_something_to_say()
        {
            // The other half of the gap. Wiring PostStart against content that filled none of these in would
            // have left the player told exactly as little as before — the trap this repository keeps falling
            // into. Asserted over MentalBreakDefs, so a break added later without a letter fails here.
            List<string> silent = DefDatabase<MentalBreakDef>.AllDefsListForReading
                .Where(b => string.IsNullOrEmpty(b.mentalState.beginLetter))
                .Select(b => b.defName)
                .ToList();

            Assert.True(silent.Count == 0, "mental breaks that would tell the player nothing: " + string.Join(", ", silent));
            Assert.True(DefDatabase<MentalBreakDef>.DefCount > 0);
        }

        [Fact]
        public void Every_break_letter_names_the_pawn_it_is_about()
        {
            // {0} is the shipped substitution convention (DamageDef.deathMessage uses it too). A letter that
            // forgot it would read as a weather report about nobody in particular.
            foreach (MentalBreakDef b in DefDatabase<MentalBreakDef>.AllDefsListForReading)
            {
                Assert.Contains("{0}", b.mentalState.beginLetter!);
                Assert.False(string.IsNullOrEmpty(b.mentalState.beginLetterLabel), b.defName + " has no letter label");
            }
        }

        // ---- the whole mechanism, not just the call ----

        [Fact]
        public void A_citizen_kept_in_despair_breaks_on_their_own_and_the_letter_arrives_with_it()
        {
            // Nothing here starts a mental state by hand: mood falls, the breaker rolls, and the letter is a
            // consequence. This is the same despair recipe MentalBreakTests uses for the break itself.
            Pawn p = Citizen("Despairing");
            p.story.traits.GainTrait(new Trait(Trait("NaturalMood"), -2));
            p.needs.food!.CurLevel = 0f;
            p.needs.rest!.CurLevel = 0f;
            p.needs.joy!.CurLevel = 0f;
            p.needs.mood!.CurLevel = 0f;

            for (int t = 0; t < 10 * GenDate.TicksPerDay && !p.InMentalState; t += 150) RunTicks(150, p);

            Assert.True(p.InMentalState, "no break within ten days of despair");
            Letter let = Assert.Single(Letters);
            Assert.Contains("Despairing", let.label);
            Assert.Contains(p.MentalStateDef!.beginLetterLabel!, let.label);
        }

        /// <summary>
        /// The check this lane was told to make: does a <i>generated</i> game reach the new code, or is it
        /// wired and permanently dormant? Everything here comes out of world generation — the settlement, its
        /// citizens and the faction they carry — and the only thing done by hand is the break itself.
        /// <para/>
        /// It is the faction that is really under test. The letter gate asks
        /// <see cref="PawnUtility.ShouldSendNotificationAbout"/>, which is false for a pawn with no faction,
        /// and a generated citizen carried none until the batch that gave citizens their settlement's
        /// faction. Without that, every one of these letters would be composed and thrown away.
        /// </summary>
        [Fact]
        public void A_citizen_of_a_generated_settlement_is_somebody_the_narrator_talks_about()
        {
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "letters-seed", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Pawn citizen = settlement.Citizens.First(c => c.RaceProps.Humanlike && !c.Dead);

            Assert.NotNull(citizen.faction);
            Assert.True(PawnUtility.ShouldSendNotificationAbout(citizen),
                "a generated citizen is not someone the narrator would report on — every break letter would be silently discarded");

            int before = game.LetterStack.LettersListForReading.Count;
            citizen.mindState.mentalStateHandler.TryStartMentalState(State("Berserk"), "a reason", causedByMood: true);

            Assert.Equal(before + 1, game.LetterStack.LettersListForReading.Count);
            Assert.Contains(citizen.Label, game.LetterStack.LettersListForReading[before].label);
        }

        // ---- persistence ----

        [Fact]
        public void A_break_letter_survives_a_save_and_load()
        {
            Pawn p = Citizen("Saved");
            p.mindState.mentalStateHandler.TryStartMentalState(State("Tantrum"), "a reason worth keeping");
            Letter before = Assert.Single(Letters);

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Letter after = Assert.Single(loaded.LettersListForReading);
            Assert.Equal(before.label, after.label);
            Assert.Equal(before.text, after.text);
            Assert.Equal(before.def, after.def);
            Assert.Equal(before.lookTargets, after.lookTargets);
        }
    }
}
