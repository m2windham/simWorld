using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// A building coming down injures; it does not execute.
    ///
    /// <para/><b>The defect, from the player's side.</b> An earthquake destroyed a share of every kind of
    /// structure and killed whoever stood in a destroyed bed's cell outright — 500 Crush at the core, no roll.
    /// On seed <c>roof-a</c> (solo, a band of twenty-five) it fired on day 2.78 and killed five people asleep.
    /// No warning, nothing the player could have done, and nothing about it a player could tell anyone except
    /// that it happened. A loss like that is not a decision; it is a coin the game flips at the player's
    /// settlement.
    ///
    /// <para/><b>What it does now.</b> The same thing RimWorld does when an ordinary roof lands on somebody
    /// (<c>Verse.RoofCollapserImmediate</c>'s non-mountain branch): a rolled
    /// <see cref="RoofCollapserImmediate.ThinRoofCrushDamageRange"/> of Crush on the top of the body from the
    /// outside. The sleeper is woken and wounded, someone tends them, and they get up. The top of a human body
    /// is the head, neck, eyes, ears, nose and jaw, so a hit there is not always gentle — it takes an eye or an
    /// ear off about as often as it leaves a cut, and a roll on the neck that outruns its 25 health still kills
    /// — but most walk away, and what kills is a roll the player can see coming in the letter, not a certainty.
    /// That death is still the earthquake's. The player is told what fell and who is hurt.
    ///
    /// <para/>What these tests pin is the <i>behaviour</i> — in range, on the top of the body, survivable, woken,
    /// attributed, told — as bands and relations rather than the literals, which are RimWorld's or this port's own
    /// and are named where they live. Whether a given firing kills or maims is the dice's, and a stream per tick
    /// is deterministic, so the cases that need a particular outcome (a survivor, a death) fire at successive
    /// ticks until the stream gives it, rather than hard-coding a tick that would break the day the order of a
    /// roll changes. The scene is the one <see cref="EarthquakeTests"/> uses: a bare
    /// <see cref="CivilizationTarget.Map"/> stands in for a generated interior, one bed, one person in it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeInjuryTests : ContentTestBase
    {
        public EarthquakeInjuryTests(CoreContentFixture content) : base(content)
        {
            Ablation.Clear();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();

            us = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Us", "F_Us");
            Find.FactionManager.Add(us);
        }

        private readonly Faction us;

        // ---- fixtures ----

        private static IncidentDef Earthquake => DefDatabase<IncidentDef>.GetNamed("Earthquake");

        private static ThingDef Wall => DefDatabase<ThingDef>.GetNamed("Wall");

        private static ThingDef Bed => DefDatabase<ThingDef>.GetNamed("Bed");

        private const string QuakeSource = "Earthquake";

        private sealed class Scene
        {
            public CoreMap Map = null!;
            public Settlement Settlement = null!;
            public Thing Bed = null!;
            public Pawn Sleeper = null!;
            public IncidentParms Parms = null!;
        }

        /// <summary>One bed, one citizen in it, a watched settlement — the shape every case here starts from.
        /// <paramref name="asleep"/> puts the citizen to sleep the way the game does (tired enough that the
        /// needs tier issues <c>LayDown</c>), not by setting the flag behind the job system's back.
        /// <paramref name="registered"/> puts the settlement on the storyteller's civilization roster.</summary>
        private Scene Pose(string name = "Sleeper", bool asleep = true, bool registered = true, IntVec3? sleeperAt = null)
        {
            var s = new Scene
            {
                Map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil),
                Settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0),
            };
            IntVec3 cell = new IntVec3(10, 0, 10);
            s.Bed = GenSpawn.Spawn(ThingMaker.MakeThing(Bed), cell, s.Map);

            s.Sleeper = NewHuman(name);
            s.Sleeper.faction = us;
            s.Settlement.AddCitizen(s.Sleeper);
            GenSpawn.Spawn(s.Sleeper, sleeperAt ?? cell, s.Map);
            if (asleep) FallAsleep(s.Sleeper);

            CivilizationTarget target = registered ? new CivilizationTarget(Find.Storyteller) : new CivilizationTarget();
            target.SetSettlements(new[] { s.Settlement });
            target.Map = s.Map;
            Find.God.Attention.Focus(s.Settlement);
            s.Parms = new IncidentParms { target = target };
            return s;
        }

        private static void FallAsleep(Pawn pawn)
        {
            pawn.needs.rest!.CurLevel = 0.1f;
            RunTicks(5, pawn);
            Assert.True(pawn.Asleep, "the fixture must have the citizen asleep, or a wake-up proves nothing");
            Assert.Equal(JobDefOf.LayDown, pawn.jobs.curJob?.def);
        }

        private static bool Quake(Scene s, int tick)
        {
            Find.TickManager.DebugSetTicksGame(tick);
            return Earthquake.Worker.TryExecute(s.Parms);
        }

        /// <summary>
        /// Runs <paramref name="attempt"/> at successive ticks — each a fresh storyteller, letter stack and
        /// scene, each tick its own deterministic stream — until <paramref name="wanted"/> holds of what it
        /// returns, and hands that attempt back with the world exactly as it left it. How a given firing
        /// falls is the dice's; the cases here need a particular fall, and this finds one without naming a tick.
        /// </summary>
        internal static T FireUntil<T>(Func<int, T> attempt, Func<T, bool> wanted, int attempts = 3000) where T : class
        {
            for (int a = 0; a < attempts; a++)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();
                Find.LetterStack = new LetterStack();
                T result = attempt(5000 + a * 7);
                if (wanted(result)) return result;
            }
            Assert.Fail("no firing in " + attempts + " attempts fell the way this case needs; the fixture is wrong");
            return null!;
        }

        private Scene QuakeUntil(Func<Scene, bool> wanted, Func<Scene>? pose = null, bool asleep = true, bool registered = true)
        {
            return FireUntil(tick =>
            {
                Scene s = pose != null ? pose() : Pose("Sleeper", asleep, registered);
                Assert.True(Quake(s, tick));
                return s;
            }, wanted);
        }

        private static IEnumerable<Hediff_Injury> Injuries(Pawn p) => p.health.hediffSet.hediffs.OfType<Hediff_Injury>();

        private static IEnumerable<Hediff> Wounds(Pawn p) =>
            p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h is Hediff_MissingPart);

        private static Letter EarthquakeLetter() =>
            Find.LetterStack.LettersListForReading.Last(l => l.label.StartsWith("Earthquake", StringComparison.Ordinal));

        private static bool Hurt(Scene s) => !s.Sleeper.Dead && Wounds(s.Sleeper).Any();

        private const int Trials = 200;

        // ---- the damage: RimWorld's thin-roof hit ----

        /// <summary>What one quake did to one healthy person in a destroyed bed, over many seeded firings.</summary>
        private List<(bool Dead, IReadOnlyList<Hediff> Wounds)> ManyQuakes()
        {
            var outcomes = new List<(bool, IReadOnlyList<Hediff>)>();
            for (int i = 0; i < Trials; i++)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();
                Scene s = Pose("Sleeper" + i, asleep: false);
                Assert.True(Quake(s, 1000 + i * 977));
                Assert.True(s.Bed.Destroyed, "the fixture must bring the bed down, or this proves nothing");
                outcomes.Add((s.Sleeper.Dead, Wounds(s.Sleeper).ToList()));
            }
            return outcomes;
        }

        [Fact]
        public void Somebody_under_a_fallen_bed_takes_a_thin_roofs_crush_damage_on_the_top_of_the_body()
        {
            IntRange thinRoof = RoofCollapserImmediate.ThinRoofCrushDamageRange;
            int cuts = 0, lostParts = 0;

            foreach (var outcome in ManyQuakes())
            {
                Assert.NotEmpty(outcome.Wounds); // everybody in a fallen bed is marked, living or dead
                foreach (Hediff wound in outcome.Wounds)
                {
                    // Top/Outside: head, neck, eyes, ears, nose, jaw — never an organ. The 500-point hit aimed
                    // at the core could not have passed this; neither could one rolled over the whole body.
                    Assert.Equal(BodyPartDepth.Outside, wound.Part!.depth);
                    Assert.Equal(BodyPartHeight.Top, wound.Part.height);

                    if (wound is Hediff_Injury injury)
                    {
                        cuts++;
                        Assert.InRange(injury.Severity, thinRoof.min - 0.01f, thinRoof.max + 0.01f);
                    }
                    else
                    {
                        // A part the roll destroyed outright: only a part the roll could have destroyed.
                        lostParts++;
                        Assert.True(wound.Part.def.hitPoints <= thinRoof.max,
                            wound.Part.Label + " was destroyed by a hit that cannot reach " + wound.Part.def.hitPoints + " damage");
                    }
                }
            }

            // Not vacuous: the sweep saw both what a thin roof does to a big part and to a small one.
            Assert.True(cuts > 0, "no hit in " + Trials + " left an injury behind");
            Assert.True(lostParts > 0, "no hit in " + Trials + " destroyed a small part");
        }

        [Fact]
        public void The_damage_is_rolled_per_victim_not_a_fixed_figure()
        {
            var severities = new HashSet<int>();
            foreach (var outcome in ManyQuakes())
            {
                foreach (Hediff_Injury injury in outcome.Wounds.OfType<Hediff_Injury>()) severities.Add((int)Math.Round(injury.Severity));
            }

            // A fixed figure (as the old one was) gives exactly one.
            Assert.True(severities.Count > 3, "the damage barely varied between victims: " + string.Join(",", severities.OrderBy(x => x)));
        }

        [Fact]
        public void Somebody_under_a_fallen_bed_is_not_certainly_killed_and_most_walk_away()
        {
            int dead = ManyQuakes().Count(o => o.Dead);

            // The old rule killed every one of them. Anything short of that is the change; "most live" is the
            // claim a thin roof makes (RimWorld: a colonist walks away from it).
            Assert.True(dead < Trials, "every sleeper in every trial was killed, as the old rule did");
            Assert.True(dead * 2 < Trials,
                dead + " of " + Trials + " healthy sleepers died of a thin-roof crush; most should live");
        }

        [Fact]
        public void The_roll_replays_with_the_seed_and_the_ambient_stream_does_not_move()
        {
            string OnceAt(int tick, out uint ambientDrift)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();
                Scene s = Pose("Replay", asleep: false);
                uint before = Rand.Current.Iterations;
                Assert.True(Quake(s, tick));
                ambientDrift = Rand.Current.Iterations - before;
                return s.Sleeper.Dead + "|" + string.Join(";", Wounds(s.Sleeper).Select(w => w.def.defName + "@" + w.Part!.Label + ":" + w.Severity.ToString("0.###")));
            }

            for (int tick = 31337; tick < 31337 + 40; tick++)
            {
                string a = OnceAt(tick, out uint driftA);
                string b = OnceAt(tick, out uint driftB);

                Assert.Equal(a, b);
                Assert.Equal(0u, driftA);
                Assert.Equal(0u, driftB);
            }
        }

        // ---- a sleeper hit is woken ----

        [Fact]
        public void A_sleeper_hurt_by_the_collapse_is_woken_and_its_rest_job_ends()
        {
            Job? lying = null;
            Scene s = QuakeUntil(Hurt, pose: () =>
            {
                Scene fresh = Pose();
                lying = fresh.Sleeper.jobs.curJob;
                Assert.True(fresh.Sleeper.Asleep);
                return fresh;
            });

            Assert.NotNull(lying);
            Assert.NotEmpty(Wounds(s.Sleeper));
            Assert.False(s.Sleeper.Asleep, "somebody under a falling bed slept on");

            // And the job follows the flag, rather than leaving a woken pawn lying in a destroyed bed: the sleep
            // toil ends itself on finding the flag clear (or a hit that downs them ends it outright). A tired
            // pawn may then go and lie down somewhere else — the think tree's call, and a different job.
            RunTicks(3, s.Sleeper);
            Assert.NotSame(lying, s.Sleeper.jobs.curJob);
        }

        [Fact]
        public void A_sleeper_is_woken_even_by_a_hit_that_leaves_them_standing()
        {
            // The hits that cut rather than destroy leave the pawn on their feet — no downing to end the job
            // for the damage pipeline — so this is the wake-up path on its own.
            Scene s = QuakeUntil(sc => !sc.Sleeper.Dead && !sc.Sleeper.Downed && Wounds(sc.Sleeper).Any());

            Assert.False(s.Sleeper.Asleep, "somebody hurt but still on their feet slept on");
        }

        [Fact]
        public void A_pawn_who_is_already_awake_is_hurt_too_and_stays_awake()
        {
            Scene s = QuakeUntil(Hurt, asleep: false);

            Assert.False(s.Sleeper.Asleep);
        }

        // ---- a death that still happens stays the quake's ----

        [Fact]
        public void Every_death_the_quake_causes_is_credited_to_it_exactly_once()
        {
            int dead = 0;
            for (int i = 0; i < Trials; i++)
            {
                Scene s = Pose("Hale" + i, asleep: false);
                Assert.True(Quake(s, 100 + i * 31));
                if (s.Sleeper.Dead) dead++;
            }

            // One ledger across every trial, so a death credited twice or not at all shows as a mismatch.
            Assert.True(dead > 0, "no hit in " + Trials + " killed anyone, so this checks nothing");
            Assert.Equal(dead, Find.Storyteller.deaths.Total);
            Assert.Equal(dead, Find.Storyteller.deaths.AttributedTo(QuakeSource));
        }

        // ---- and one who lives, then dies of it, is the quake's too ----

        [Fact]
        public void The_wounds_it_dealt_carry_the_incident_so_a_later_death_is_credited_to_it()
        {
            Scene s = QuakeUntil(sc => !sc.Sleeper.Dead && Injuries(sc.Sleeper).Any());

            List<Hediff> wounds = Wounds(s.Sleeper).ToList();
            Assert.All(wounds, w => Assert.Equal(QuakeSource, w.sourceIncident));

            // The death the stamp is for: nothing strikes the pawn at that moment (dinfo is null), so the
            // ledger reads the culprit's provenance — here, one of those wounds finishing them.
            Assert.Equal(0, Find.Storyteller.deaths.Total);
            s.Sleeper.health.Kill(null, wounds[0]);
            Assert.True(s.Sleeper.Dead);
            Assert.Equal(QuakeSource, StorytellerDeathEvents.SourceOf(null, wounds[0]));
            Assert.Equal(1, Find.Storyteller.deaths.Total);
            Assert.Equal(1, Find.Storyteller.deaths.AttributedTo(QuakeSource));
        }

        [Fact]
        public void A_part_the_hit_destroyed_carries_the_incident_too()
        {
            // The missing part is what bleeds and what an infection starts from, and it is added by the health
            // tracker, not by the quake: stamping only the injury would leave exactly these unattributed.
            Scene s = QuakeUntil(sc => !sc.Sleeper.Dead && sc.Sleeper.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Any());

            Assert.All(s.Sleeper.health.hediffSet.hediffs.OfType<Hediff_MissingPart>(),
                m => Assert.Equal(QuakeSource, m.sourceIncident));
        }

        [Fact]
        public void A_wound_the_pawn_already_carried_keeps_its_own_provenance()
        {
            Hediff_Injury? old = null;
            Scene s = QuakeUntil(sc => !sc.Sleeper.Dead && Wounds(sc.Sleeper).Any(w => w != old), pose: () =>
            {
                Scene fresh = Pose("Veteran");
                old = (Hediff_Injury)HediffMaker.MakeHediff(
                    DefDatabase<HediffDef>.GetNamed("Bruise"), fresh.Sleeper, fresh.Sleeper.RaceProps.body!.GetPartByLabel("left leg"));
                old.Severity = 5f;
                old.sourceIncident = "RaidEnemy";
                fresh.Sleeper.health.AddHediff(old, old.Part, null);
                return fresh;
            });

            Assert.Equal("RaidEnemy", old!.sourceIncident);
            Assert.Contains(Wounds(s.Sleeper), w => w.sourceIncident == QuakeSource);
        }

        [Fact]
        public void The_stamped_wounds_survive_a_save_and_load()
        {
            Scene s = QuakeUntil(sc => !sc.Sleeper.Dead && Wounds(sc.Sleeper).Any());
            s.Sleeper.faction = null; // a faction is a reference the save would need a world to resolve; the wounds are the subject

            string xml = Scribe.SaveToString(s.Sleeper, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(Wounds(s.Sleeper).Count(), Wounds(loaded).Count());
            Assert.All(Wounds(loaded), w => Assert.Equal(QuakeSource, w.sourceIncident));
        }

        // ---- the player is told what fell and who is hurt ----

        [Fact]
        public void The_letter_names_what_fell_by_kind_and_how_many()
        {
            Scene s = Pose("Sleeper", asleep: false);
            for (int i = 0; i < 12; i++)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(Wall), new IntVec3(2 + i, 0, 2), s.Map);
            }
            int wallsBefore = s.Map.listerThings.ThingsOfDef(Wall).Count;
            Assert.Equal(12, wallsBefore);

            Assert.True(Quake(s, 5000));

            int wallsLost = wallsBefore - s.Map.listerThings.ThingsOfDef(Wall).Count;
            Assert.True(wallsLost > 0, "the fixture must bring some walls down");
            string text = EarthquakeLetter().text;

            // Counted, in the def's own label, in the number that actually fell.
            Assert.Contains(wallsLost + " wall" + (wallsLost == 1 ? "" : "s"), text, StringComparison.Ordinal);
            Assert.Contains("1 bed", text, StringComparison.Ordinal);
            Assert.DoesNotContain("0 structures", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_letter_names_the_hurt_and_points_at_them()
        {
            Scene s = QuakeUntil(Hurt, pose: () => Pose("Marguerite"));
            Letter letter = EarthquakeLetter();

            Assert.Contains("Marguerite was caught in the collapse and is hurt", letter.text, StringComparison.Ordinal);
            Assert.DoesNotContain("killed", letter.text, StringComparison.Ordinal);
            Assert.NotNull(letter.lookTargets);
            Assert.Contains(s.Sleeper.GetUniqueLoadID(), letter.lookTargets!);
        }

        /// <summary>Armour nothing a falling bed can do will get through: every layer deflects outright.</summary>
        private sealed class Plate : IArmorSource
        {
            public float ArmorRating(StatDef armorStat, BodyPartRecord part) => 10f;

            public bool Covers(BodyPartRecord part) => true;
        }

        [Fact]
        public void A_person_the_blow_cannot_hurt_is_still_woken_and_the_letter_says_they_came_away_unhurt()
        {
            Scene s = QuakeUntil(sc => true, pose: () =>
            {
                Scene fresh = Pose("Ursula");
                PawnArmor.Register(fresh.Sleeper, new Plate());
                return fresh;
            });

            Assert.True(s.Bed.Destroyed);
            Assert.False(s.Sleeper.Dead);
            Assert.Empty(Wounds(s.Sleeper));
            Assert.False(s.Sleeper.Asleep, "the blow was turned away, not slept through");
            Letter letter = EarthquakeLetter();
            Assert.Contains("Ursula was caught in the collapse and came away unhurt", letter.text, StringComparison.Ordinal);
            Assert.DoesNotContain("is hurt", letter.text, StringComparison.Ordinal);
            Assert.Contains(s.Sleeper.GetUniqueLoadID(), letter.lookTargets!);
        }

        [Fact]
        public void The_letter_names_the_dead_even_when_the_ledger_does_not_count_them()
        {
            Scene s = QuakeUntil(sc => sc.Sleeper.Dead, pose: () => Pose("Marguerite", registered: false));

            Assert.Equal(0, Find.Storyteller.deaths.Total); // off the roster: the ledger does not count it
            Letter letter = EarthquakeLetter();
            Assert.Contains("Marguerite was killed in the collapse", letter.text, StringComparison.Ordinal);
            Assert.DoesNotContain("hurt", letter.text, StringComparison.Ordinal);
            Assert.Contains(s.Sleeper.GetUniqueLoadID(), letter.lookTargets!);
        }

        [Fact]
        public void The_letter_tells_the_hurt_from_the_dead_when_both_happen()
        {
            // Three beds: the quake brings down one or two of them, so the dice decide whether it takes both
            // the first sleeper's and the second's, and whether the first dies and the second lives.
            Pawn? second = null;
            Thing? secondBed = null;
            Scene s = QuakeUntil(sc => sc.Bed.Destroyed && secondBed!.Destroyed && sc.Sleeper.Dead && !second!.Dead && Wounds(second).Any(), pose: () =>
            {
                Scene fresh = Pose("Firstly", asleep: false);
                IntVec3 cell = new IntVec3(4, 0, 18);
                secondBed = GenSpawn.Spawn(ThingMaker.MakeThing(Bed), cell, fresh.Map);
                second = NewHuman("Secondly");
                second.faction = us;
                fresh.Settlement.AddCitizen(second);
                GenSpawn.Spawn(second, cell, fresh.Map);
                GenSpawn.Spawn(ThingMaker.MakeThing(Bed), new IntVec3(16, 0, 4), fresh.Map);
                return fresh;
            });

            string text = EarthquakeLetter().text;
            Assert.Contains("Secondly was caught in the collapse and is hurt", text, StringComparison.Ordinal);
            Assert.Contains("Firstly was killed in the collapse", text, StringComparison.Ordinal);
            Assert.True(text.IndexOf("Secondly", StringComparison.Ordinal) < text.IndexOf("Firstly", StringComparison.Ordinal),
                "the hurt are named before the dead");
            Assert.Contains(s.Sleeper.GetUniqueLoadID(), EarthquakeLetter().lookTargets!);
            Assert.Contains(second!.GetUniqueLoadID(), EarthquakeLetter().lookTargets!);
        }

        [Fact]
        public void The_letter_is_about_the_players_people_not_their_dog_asleep_in_a_bed()
        {
            // The only bed, with a husky in it and the citizen standing elsewhere: the quake brings the bed
            // down, the dog is hurt like anyone (a building falling does not ask whose it is), and the letter
            // does not name it.
            Pawn? dog = null;
            Scene s = QuakeUntil(sc => dog!.Dead || Wounds(dog).Any(), pose: () =>
            {
                Scene fresh = Pose("Marguerite", asleep: false, sleeperAt: new IntVec3(1, 0, 1));
                dog = new Pawn(Husky, "Rufus");
                dog.faction = us; // the player's own animal, as the muffalo in a real settlement are
                GenSpawn.Spawn(dog, new IntVec3(10, 0, 10), fresh.Map);
                return fresh;
            });

            Assert.True(s.Bed.Destroyed);
            Assert.False(s.Sleeper.Dead);
            Assert.Empty(Wounds(s.Sleeper));
            string text = EarthquakeLetter().text;
            Assert.DoesNotContain("Rufus", text, StringComparison.Ordinal);
            Assert.DoesNotContain("hurt", text, StringComparison.Ordinal);
            Assert.DoesNotContain("killed", text, StringComparison.Ordinal);
            Assert.True(EarthquakeLetter().lookTargets == null || !EarthquakeLetter().lookTargets!.Contains(dog!.GetUniqueLoadID()));
        }

        [Fact]
        public void A_long_list_of_names_is_shortened()
        {
            var pawns = Enumerable.Range(0, 9).Select(i => NewHuman("P" + i)).ToList();

            string text = IncidentWorker_Earthquake.NameList(pawns);

            Assert.Contains("P0", text, StringComparison.Ordinal);
            Assert.DoesNotContain("P8", text, StringComparison.Ordinal);
            Assert.EndsWith("and 3 others", text, StringComparison.Ordinal);
            Assert.Equal("A", IncidentWorker_Earthquake.NameList(new[] { NewHuman("A") }));
            Assert.Equal("A and B", IncidentWorker_Earthquake.NameList(new[] { NewHuman("A"), NewHuman("B") }));
            Assert.Equal("A, B and C", IncidentWorker_Earthquake.NameList(new[] { NewHuman("A"), NewHuman("B"), NewHuman("C") }));
        }

        [Fact]
        public void An_unwatched_quake_still_says_what_fell_by_kind()
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Faraway", 0);
            settlement.AddStructure(Wall, 40);
            settlement.AddStructure(Bed, 6);
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            Find.God.Attention.ClearFocus();

            Find.TickManager.DebugSetTicksGame(5000);
            Assert.True(Earthquake.Worker.TryExecute(new IncidentParms { target = target }));

            int wallsLost = 40 - settlement.StructureCount(Wall);
            int bedsLost = 6 - settlement.StructureCount(Bed);
            Assert.True(wallsLost > 0 && bedsLost > 0);
            string text = EarthquakeLetter().text;
            Assert.Contains(wallsLost + " wall" + (wallsLost == 1 ? "" : "s"), text, StringComparison.Ordinal);
            Assert.Contains(bedsLost + " bed" + (bedsLost == 1 ? "" : "s"), text, StringComparison.Ordinal);
            Assert.Contains("Faraway", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_quake_with_nothing_built_says_so_rather_than_counting_zero()
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Bare", 0);
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            Find.God.Attention.ClearFocus();

            Assert.True(Earthquake.Worker.TryExecute(new IncidentParms { target = target }));

            string text = EarthquakeLetter().text;
            Assert.Contains("Nothing you had built", text, StringComparison.Ordinal);
            Assert.DoesNotContain("0 ", text, StringComparison.Ordinal);
        }
    }
}
