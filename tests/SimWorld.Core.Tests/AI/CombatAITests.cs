using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The think tree's combat tier wired end to end (system 9: AI — combat): the gap where an entire tested
    /// Combat module was unreachable from play because nothing in the AI layer ever cast a verb at an enemy.
    /// <see cref="JobGiver_AIFightEnemies"/> acquiring a target, <see cref="JobDriver_AttackStatic"/> /
    /// <see cref="JobDriver_AttackMelee"/> driving a real <see cref="Verb"/> from a toil the way
    /// <see cref="JobDriver_Hunt"/> proved one could be driven, and <see cref="CombatPostureUtility"/> —
    /// where RimWorld's draft went.
    /// <para/>
    /// Everything unsourced is asserted as a band, a trend or an ordering, never as the literal constant
    /// <see cref="CombatAITuning"/> is explicit about not being able to source.
    /// </summary>
    public class CombatAITests : ContentTestBase
    {
        public CombatAITests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Faction NewFaction(string name, string defName = "TribalCivilization")
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        /// <summary>Two factions that will shoot each other on sight, the same way
        /// <c>CaptureTests</c>/<c>RaidTests</c> already set one up — straight through the faction relations
        /// that already exist, never a second notion of hostility.</summary>
        private static (Faction ours, Faction theirs) HostilePair()
        {
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Faction theirs = NewFaction("Theirs");
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            return (ours, theirs);
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction? faction = null, string? weaponDefName = null, string name = "Test")
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            if (weaponDefName != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weaponDefName)));
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Beast")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static ThingDef Muffalo => Def("Muffalo");

        private static ThingDef Chicken => Def("Chicken");

        private static bool IsAttackJob(Job? job) =>
            job != null && (job.def == CombatAIDefOf.AttackMelee || job.def == CombatAIDefOf.AttackStatic);

        /// <summary>How many wounds a pawn is carrying. Deliberately a count and not a severity sum: severity
        /// drifts every tick on its own as injuries bleed and heal, so a sum would move whether or not anyone
        /// was still shooting — which is precisely the question the "stops shooting a body" test asks.</summary>
        private static int WoundCount(Pawn pawn) => pawn.health.hediffSet.hediffs.Count;

        private static bool Hurt(Pawn pawn, int woundsBefore) => WoundCount(pawn) > woundsBefore || pawn.Downed || pawn.Dead;

        // ---- content ----

        [Fact]
        public void Combat_AI_content_loads_with_no_errors_and_is_wired_to_real_drivers()
        {
            Assert.Empty(Content.Result.Errors);

            Assert.NotNull(CombatAIDefOf.AttackMelee);
            Assert.NotNull(CombatAIDefOf.AttackStatic);
            Assert.NotNull(CombatAIDefOf.Bite);
            Assert.NotNull(CombatAIDefOf.TakeUpArms);

            Assert.Equal(typeof(JobDriver_AttackMelee), CombatAIDefOf.AttackMelee.driverClass);
            Assert.Equal(typeof(JobDriver_AttackStatic), CombatAIDefOf.AttackStatic.driverClass);

            // The Bite capacity has to resolve to a real maneuver or an animal's teeth swing nothing.
            Assert.NotNull(ManeuverUtility.FindManeuver(new Tool
            {
                capacities = new List<ToolCapacityDef> { CombatAIDefOf.Bite },
                power = 1f,
            }));

            // TakeUpArms is the one edict that pushes no work at all: it changes posture, not priorities.
            Assert.Empty(CombatAIDefOf.TakeUpArms.prioritizedWork);
            Assert.NotNull(CombatAIDefOf.TakeUpArms.moodThought);
            Assert.Null(CombatAIDefOf.TakeUpArms.requiredEra); // a civilization can call people to arms in any age
        }

        // ---- who is an enemy (built on the faction relations that already exist) ----

        [Fact]
        public void Hostility_is_faction_relations_plus_a_personal_grudge_and_nothing_else()
        {
            CoreMap map = NewMap(12, 12);
            (Faction ours, Faction theirs) = HostilePair();
            Faction neutrals = NewFaction("Neutrals");

            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1), ours, name: "Citizen");
            Pawn ally = SpawnHuman(map, new IntVec3(2, 0, 1), ours, name: "Ally");
            Pawn raider = SpawnHuman(map, new IntVec3(3, 0, 1), theirs, name: "Raider");
            Pawn trader = SpawnHuman(map, new IntVec3(4, 0, 1), neutrals, name: "Trader");
            Pawn wild = SpawnAnimal(map, Muffalo, new IntVec3(5, 0, 1));

            Assert.True(AttackTargetsUtility.HostileTo(citizen, raider));
            Assert.True(AttackTargetsUtility.HostileTo(raider, citizen)); // symmetric
            Assert.False(AttackTargetsUtility.HostileTo(citizen, ally));
            Assert.False(AttackTargetsUtility.HostileTo(citizen, citizen));
            Assert.False(AttackTargetsUtility.HostileTo(citizen, trader));

            // A factionless wild animal is nobody's enemy by relation — which is what keeps a hunt a hunt.
            Assert.False(AttackTargetsUtility.HostileTo(citizen, wild));

            // ...until it holds a grudge, which is personal to the pawn it names and to nobody else.
            wild.mindState.angryAt = citizen;
            wild.mindState.angryUntilTick = Find.TickManager.TicksGame + 1000;
            Assert.True(AttackTargetsUtility.HostileTo(wild, citizen));
            Assert.True(AttackTargetsUtility.HostileTo(citizen, wild));
            Assert.False(AttackTargetsUtility.HostileTo(wild, ally));
        }

        [Fact]
        public void A_downed_or_dead_pawn_stops_being_a_threat_while_staying_on_the_map()
        {
            CoreMap map = NewMap(8, 8);
            Pawn standing = SpawnHuman(map, new IntVec3(1, 0, 1));
            Pawn downed = SpawnHuman(map, new IntVec3(2, 0, 1), name: "Downed");

            Assert.False(AttackTargetsUtility.ThreatDisabled(standing));

            downed.health.ForceDowned = true;
            Assert.True(downed.Downed);
            Assert.True(downed.Spawned); // still on the map, still a valid target — hence the explicit test
            Assert.True(AttackTargetsUtility.ThreatDisabled(downed));
        }

        // ---- what a pawn attacks with (one selection path, shared with the hunt) ----

        [Fact]
        public void Attack_verb_selection_is_the_hunts_path_plus_a_natural_weapon_fallback()
        {
            CoreMap map = NewMap(10, 10);
            Pawn archer = SpawnHuman(map, new IntVec3(0, 0, 0), weaponDefName: "Bow_Short", name: "Archer");
            Pawn knifeman = SpawnHuman(map, new IntVec3(1, 0, 0), weaponDefName: "MeleeWeapon_Knife", name: "Knifeman");
            Pawn barehanded = SpawnHuman(map, new IntVec3(2, 0, 0), name: "Barehanded");
            Pawn beast = SpawnAnimal(map, Muffalo, new IntVec3(3, 0, 0));

            Assert.IsType<Verb_LaunchProjectile>(AttackVerbUtility.TryGetAttackVerb(archer));
            Assert.IsType<Verb_MeleeAttack>(AttackVerbUtility.TryGetAttackVerb(knifeman));

            // The two things the extraction had to keep true at once: an empty-handed pawn can now defend
            // itself with its fists...
            Assert.IsType<Verb_MeleeAttack>(AttackVerbUtility.TryGetAttackVerb(barehanded));
            Assert.IsType<Verb_MeleeAttack>(AttackVerbUtility.TryGetAttackVerb(beast));

            // ...while the hunt still refuses one outright, which is the whole reason MakeHuntVerb keeps its
            // own name after delegating (punching a muffalo is the fight IsSafeToHunt exists to prevent).
            Assert.Null(HuntUtility.MakeHuntVerb(barehanded));
            Assert.Same(archer.equipment.Primary!.def.verbs![0], HuntUtility.MakeHuntVerb(archer)!.verbProps);

            // A pawn sent into melee swings even while carrying a bow (RimWorld: Pawn_MeleeVerbs).
            Assert.IsType<Verb_MeleeAttack>(AttackVerbUtility.TryGetMeleeVerb(archer));

            Assert.True(AttackVerbUtility.HasEquippedWeapon(archer));
            Assert.True(AttackVerbUtility.HasEquippedWeapon(knifeman));
            Assert.False(AttackVerbUtility.HasEquippedWeapon(barehanded));
        }

        [Fact]
        public void A_natural_weapon_scales_with_body_size_so_a_chicken_is_not_a_muffalo()
        {
            CoreMap map = NewMap(10, 10);
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(1, 0, 1), "Chicken");
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 1), "Husky");
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(3, 0, 1), "Muffalo");
            Pawn human = SpawnHuman(map, new IntVec3(4, 0, 1));

            float chickenPower = AttackVerbUtility.NaturalWeaponFor(chicken)!.power;
            float huskyPower = AttackVerbUtility.NaturalWeaponFor(husky)!.power;
            float muffaloPower = AttackVerbUtility.NaturalWeaponFor(muffalo)!.power;

            // The ordering is the claim, not the numbers (CombatAITuning says why they cannot be sourced).
            Assert.True(chickenPower < huskyPower);
            Assert.True(huskyPower < muffaloPower);

            // A human's bare fists are the social fight's fists — one bare-fist number in the codebase, not
            // two, which is the point of reusing SocialTuning.SocialFightFistPower.
            Assert.Equal(global::SimWorld.Social.SocialTuning.SocialFightFistPower, AttackVerbUtility.NaturalWeaponFor(human)!.power);

            // A muffalo hits harder bare than a human does with the weakest weapon in shipped content.
            Assert.True(muffaloPower > Def("MeleeWeapon_Club").tools!.Max(t => t.power));
        }

        // ---- target acquisition ----

        [Fact]
        public void A_pawn_picks_the_nearest_live_enemy_and_never_a_friendly_or_a_body()
        {
            CoreMap map = NewMap(20, 20);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1), ours, "Gun_Revolver", "Citizen");
            Pawn ally = SpawnHuman(map, new IntVec3(2, 0, 1), ours, name: "Ally");
            Pawn nearRaider = SpawnHuman(map, new IntVec3(5, 0, 1), theirs, name: "NearRaider");
            Pawn farRaider = SpawnHuman(map, new IntVec3(15, 0, 1), theirs, name: "FarRaider");

            Assert.Same(nearRaider, AttackTargetFinder.BestAttackTarget(citizen, CombatAITuning.TargetAcquireRadius));

            // Down the near one: the scan moves on rather than keeping the barrel pointed at a body.
            nearRaider.health.ForceDowned = true;
            Assert.Same(farRaider, AttackTargetFinder.BestAttackTarget(citizen, CombatAITuning.TargetAcquireRadius));

            // And the ally is never in the running at any range.
            farRaider.health.ForceDowned = true;
            Assert.Null(AttackTargetFinder.BestAttackTarget(citizen, CombatAITuning.TargetAcquireRadius));
            Assert.True(ally.Spawned);
        }

        [Fact]
        public void The_acquire_radius_reaches_further_than_any_weapon_this_port_ships()
        {
            // Not the literal 40: the property that matters is that a pawn notices a shooter already able to
            // hit it, rather than only reacting once it has been hit.
            float longestWeaponRange = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.verbs != null)
                .SelectMany(d => d.verbs!)
                .Where(v => !v.IsMeleeAttack)
                .Max(v => v.range);

            Assert.True(CombatAITuning.TargetAcquireRadius > longestWeaponRange);
            Assert.True(CombatAITuning.MeleeReachCells < 2f); // "adjacent" really is adjacent
        }

        // ---- the tier actually fires ----

        [Fact]
        public void Two_hostile_pawns_in_range_fight_until_one_of_them_is_down_or_dead()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(3, 0, 3), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(10, 0, 10), theirs, "Gun_Revolver", "Raider");

            citizen.jobs.TryFindAndStartJob();
            raider.jobs.TryFindAndStartJob();

            // Both reach for the ranged job, at the enemy, with a give-up ceiling on it.
            Assert.Equal(CombatAIDefOf.AttackStatic, citizen.jobs.curJob?.def);
            Assert.Same(raider, citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.True(citizen.jobs.curJob!.expiryInterval > 0);
            Assert.Equal(CombatAIDefOf.AttackStatic, raider.jobs.curJob?.def);

            RunTicks(8000, citizen, raider);

            Assert.True(citizen.Downed || citizen.Dead || raider.Downed || raider.Dead,
                "Two armed enemies stood in each other's line of fire for over two in-game hours and nobody went down.");
        }

        [Fact]
        public void A_citizen_never_attacks_a_friendly_however_long_they_stand_together()
        {
            CoreMap map = NewMap(16, 16);
            Faction ours = NewFaction("Ours", "PlayerCivilization");

            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4), ours, "Gun_Revolver", "Citizen");
            Pawn ally = SpawnHuman(map, new IntVec3(5, 0, 4), ours, name: "Ally");
            int before = WoundCount(ally);

            citizen.jobs.TryFindAndStartJob();
            Assert.False(IsAttackJob(citizen.jobs.curJob));

            RunTicks(3000, citizen, ally);

            Assert.False(IsAttackJob(citizen.jobs.curJob));
            Assert.False(Hurt(ally, before));
        }

        [Fact]
        public void An_attacker_stops_the_moment_its_target_goes_down_and_lands_nothing_more_on_it()
        {
            CoreMap map = NewMap(20, 20);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(9, 0, 4), theirs, name: "Raider"); // unarmed: it will lose

            citizen.jobs.TryFindAndStartJob();
            Assert.Equal(CombatAIDefOf.AttackStatic, citizen.jobs.curJob?.def);

            // Fire until the raider is down — the shooter's own job is what ends it, not the test.
            for (int i = 0; i < 12000 && !raider.Downed && !raider.Dead; i++) RunTicks(1, citizen, raider);
            Assert.True(raider.Downed || raider.Dead, "The armed citizen never put the unarmed raider down.");

            int woundsWhenItFell = WoundCount(raider);
            RunTicks(2000, citizen, raider);

            Assert.False(IsAttackJob(citizen.jobs.curJob));
            Assert.True(WoundCount(raider) <= woundsWhenItFell, "The shooter kept putting rounds into a body.");
            Assert.True(raider.Spawned); // the body is still lying there; the shooter simply stopped
        }

        [Fact]
        public void Combat_outranks_needs_and_work_for_a_pawn_who_is_hungry_and_has_a_job_to_do()
        {
            CoreMap map = NewMap(20, 20);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(3, 0, 3), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(9, 0, 3), theirs, "Gun_Revolver", "Raider");

            // Starving, with food within arm's reach — the needs tier would win in a heartbeat if it could.
            citizen.needs.food!.CurLevel = 0.05f;
            Thing meal = ThingMaker.MakeThing(Def("RawPotatoes"));
            GenSpawn.Spawn(meal, new IntVec3(4, 0, 3), map);

            citizen.jobs.TryFindAndStartJob();
            Assert.True(IsAttackJob(citizen.jobs.curJob),
                "The combat tier sits above needs and work; a hungry citizen with a raider in range still fights.");

            // ...and with no raider on the map it eats, which is what makes the ordering a real ordering
            // rather than an accident of the food scan failing.
            raider.DeSpawn();
            citizen.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Assert.Equal(JobDefOf.Ingest, citizen.jobs.curJob?.def);
        }

        // ---- who fights: the posture rules that replace the draft ----

        [Fact]
        public void An_unarmed_citizen_fights_back_when_cornered_but_does_not_go_looking()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn farmer = SpawnHuman(map, new IntVec3(4, 0, 4), ours, name: "Farmer");
            Pawn distantRaider = SpawnHuman(map, new IntVec3(16, 0, 16), theirs, name: "DistantRaider");

            // Rule 1 does not apply (no weapon) and rule 2 does not reach that far: the farmer keeps working.
            Assert.Equal(CombatAITuning.MeleeReachCells, CombatPostureUtility.TargetAcquireRadiusFor(farmer));
            farmer.jobs.TryFindAndStartJob();
            Assert.False(IsAttackJob(farmer.jobs.curJob));

            // Cornered is cornered — RimWorld's "or when a hostile is adjacent" half of the draft rule.
            Pawn closeRaider = SpawnHuman(map, new IntVec3(5, 0, 4), theirs, name: "CloseRaider");
            farmer.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Assert.Equal(CombatAIDefOf.AttackMelee, farmer.jobs.curJob?.def);
            Assert.Same(closeRaider, farmer.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.True(distantRaider.Spawned);
        }

        [Fact]
        public void An_armed_citizen_needs_no_edict_at_all()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn armed = SpawnHuman(map, new IntVec3(4, 0, 4), ours, "Gun_Revolver", "Armed");
            SpawnHuman(map, new IntVec3(16, 0, 16), theirs, name: "DistantRaider");

            Assert.False(CombatPostureUtility.IsMustered(armed));
            Assert.Equal(CombatAITuning.TargetAcquireRadius, CombatPostureUtility.TargetAcquireRadiusFor(armed));

            armed.jobs.TryFindAndStartJob();
            Assert.Equal(CombatAIDefOf.AttackStatic, armed.jobs.curJob?.def);
        }

        [Fact]
        public void The_TakeUpArms_edict_sends_unarmed_citizens_after_a_distant_enemy_and_leaves_no_trace()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn farmer = SpawnHuman(map, new IntVec3(4, 0, 4), ours, name: "Farmer");
            Pawn raider = SpawnHuman(map, new IntVec3(16, 0, 16), theirs, name: "DistantRaider");

            farmer.jobs.TryFindAndStartJob();
            Assert.False(IsAttackJob(farmer.jobs.curJob));

            Assert.True(Find.God.Activate(CombatAIDefOf.TakeUpArms));
            Assert.True(CombatPostureUtility.IsMustered(farmer));
            Assert.Equal(CombatAITuning.TargetAcquireRadius, CombatPostureUtility.TargetAcquireRadiusFor(farmer));

            farmer.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Assert.Equal(CombatAIDefOf.AttackMelee, farmer.jobs.curJob?.def);
            Assert.Same(raider, farmer.jobs.curJob!.GetTarget(TargetIndex.A).Thing);

            // Rescinding it leaves nothing behind: the very next think-tree pass is back to the floor, the
            // same "leaves no trace" rule JobGiver_Edicts holds itself to.
            Assert.True(Find.God.Deactivate(CombatAIDefOf.TakeUpArms));
            Assert.False(CombatPostureUtility.IsMustered(farmer));
            farmer.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Assert.False(IsAttackJob(farmer.jobs.curJob));
        }

        [Fact]
        public void No_edict_ever_sends_a_pawn_who_cannot_do_violence()
        {
            CoreMap map = NewMap(16, 16);
            (Faction ours, Faction theirs) = HostilePair();

            // Shipped content disables Violent through a backstory, not a trait (TribalElder), which is also
            // the route WorkGiver_Hunt's own violence guard is reached by.
            Pawn pacifist = SpawnHuman(map, new IntVec3(4, 0, 4), ours, "Gun_Revolver", "Pacifist");
            pacifist.story.adulthood = DefDatabase<BackstoryDef>.GetNamed("TribalElder");
            Assert.True(pacifist.WorkTagIsDisabled(global::SimWorld.Work.WorkTags.Violent));

            SpawnHuman(map, new IntVec3(5, 0, 4), theirs, name: "Raider");
            Assert.True(Find.God.Activate(CombatAIDefOf.TakeUpArms));

            Assert.False(CombatPostureUtility.CanFight(pacifist));
            pacifist.jobs.TryFindAndStartJob();
            Assert.False(IsAttackJob(pacifist.jobs.curJob));
        }

        // ---- animals fight back: the consequence the hunting lane could not finish ----

        [Fact]
        public void A_provoked_muffalo_takes_the_manhunter_job_at_the_hunter_who_wounded_it()
        {
            CoreMap map = NewMap(20, 20);
            Pawn hunter = SpawnHuman(map, new IntVec3(6, 0, 6), weaponDefName: "Bow_Short", name: "Hunter");
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(8, 0, 6), "Muffalo");

            // The provoke roll itself is HuntUtility's and is tested there; this is about what happens next,
            // which until this lane landed was "the animal wanders off".
            ForceGrudge(muffalo, hunter);
            Assert.True(HuntUtility.IsAngry(muffalo));

            muffalo.jobs.TryFindAndStartJob();
            Assert.Equal(CombatAIDefOf.AttackMelee, muffalo.jobs.curJob?.def);
            Assert.Same(hunter, muffalo.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
        }

        /// <summary>
        /// The one that matters: a provoked muffalo does not merely take a job, it closes and does real
        /// damage. Run over several seeds and asserted as a majority rather than once, because a lone archer
        /// against a charging muffalo is genuinely a coin toss — the muffalo's first bite can kill a human
        /// outright, and the archer's first volley can drop the muffalo before it arrives. The claim being
        /// pinned is "this is how a hunt usually goes wrong", not "the muffalo always wins".
        /// </summary>
        [Fact]
        public void A_provoked_muffalo_closes_on_its_hunter_and_usually_hurts_them()
        {
            const int trials = 8;
            int huntersHurt = 0;
            int huntersPutDown = 0;

            for (int seed = 1; seed <= trials; seed++)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);

                CoreMap map = NewMap(20, 20);
                Pawn hunter = SpawnHuman(map, new IntVec3(6, 0, 6), weaponDefName: "Bow_Short", name: "Hunter");
                Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(8, 0, 6), "Muffalo");
                ForceGrudge(muffalo, hunter);

                int before = WoundCount(hunter);
                RunTicks(2000, muffalo, hunter);

                if (Hurt(hunter, before)) huntersHurt++;
                if (hunter.Downed || hunter.Dead) huntersPutDown++;
            }

            Assert.True(huntersHurt >= trials / 2,
                "Provoked prey reached its hunter in " + huntersHurt + "/" + trials + " runs and drew blood in too few of them.");
            Assert.True(huntersPutDown > 0,
                "A muffalo never once put a hunter down — revenge that cannot cost you the hunter is not revenge.");
        }

        [Fact]
        public void A_grudge_stays_personal_and_expires_back_into_ordinary_animal_behaviour()
        {
            CoreMap map = NewMap(20, 20);
            Pawn hunter = SpawnHuman(map, new IntVec3(6, 0, 6), weaponDefName: "Bow_Short", name: "Hunter");
            Pawn bystander = SpawnHuman(map, new IntVec3(7, 0, 6), name: "Bystander");
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(9, 0, 6), "Muffalo");

            ForceGrudge(muffalo, hunter);
            muffalo.jobs.TryFindAndStartJob();
            Assert.Same(hunter, muffalo.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.False(AttackTargetsUtility.HostileTo(muffalo, bystander));

            // Once the anger runs out the animal is an animal again: the manhunter tier stops being satisfied
            // and it drops back to fleeing/wandering rather than staying at war forever.
            muffalo.mindState.angryUntilTick = Find.TickManager.TicksGame;
            muffalo.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Assert.False(IsAttackJob(muffalo.jobs.curJob));
        }

        /// <summary>Sets the grudge directly, for the tests that are about the consequence rather than the
        /// roll (which <c>HuntingAITests</c> already owns). Mirrors exactly what
        /// <see cref="HuntUtility.TryProvokeRevenge"/> and <see cref="TameUtility.TryTame"/> both write.</summary>
        private static bool ForceGrudge(Pawn animal, Pawn at)
        {
            animal.mindState.angryAt = at;
            animal.mindState.angryUntilTick = Find.TickManager.TicksGame + HuntingTuning.RevengeAngerDurationTicks;
            return true;
        }

        // ---- a raid that reaches a map now fights ----

        [Fact]
        public void A_raid_spawned_onto_a_map_engages_the_civilizations_armed_citizens()
        {
            CoreMap map = NewMap(40, 40);
            Faction player = NewFaction("Player", "PlayerCivilization");
            Faction rough = NewFaction("Rough", "RoughOutlanders");
            player.SetRelationDirect(rough, FactionRelationKind.Hostile, -100);

            Pawn defender = SpawnHuman(map, new IntVec3(20, 0, 20), player, "Gun_AssaultRifle", "Defender");

            var target = new CivilizationTarget { Map = map };
            var parms = new IncidentParms { target = target, points = 400f, faction = rough };
            var worker = (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestCombatRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;

            Assert.True(worker.TryExecute(parms));
            IReadOnlyList<Pawn> raiders = worker.LastRaidPawns!;
            Assert.NotEmpty(raiders);
            Assert.All(raiders, r => Assert.True(r.Spawned)); // the squad really is standing on a map

            // Everyone re-thinks, and both sides reach for the combat tier rather than going farming.
            defender.jobs.TryFindAndStartJob();
            foreach (Pawn r in raiders) r.jobs.TryFindAndStartJob();

            Assert.True(IsAttackJob(defender.jobs.curJob), "The defender ignored an armed raid standing on its map.");
            Assert.Contains(raiders, r => IsAttackJob(r.jobs.curJob));
        }

        // ---- scribe ----

        /// <summary>Map plus factions in one document. A fight's targets are pawns and every one of them
        /// carries a <see cref="Pawn.faction"/> cross-reference, so saving the map alone leaves those
        /// references dangling — a real whole-game save always writes the faction roster alongside, which is
        /// exactly what <c>CaptureTests</c> does for the same reason.</summary>
        private sealed class FightRoot : IExposable
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
        public void A_pawn_mid_fight_round_trips_through_Scribe_and_the_fight_carries_on()
        {
            CoreMap map = NewMap(24, 24);
            (Faction ours, Faction theirs) = HostilePair();

            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4), ours, "Gun_Revolver", "Citizen");
            Pawn raider = SpawnHuman(map, new IntVec3(10, 0, 4), theirs, name: "Raider");

            citizen.jobs.TryFindAndStartJob();
            Assert.Equal(CombatAIDefOf.AttackStatic, citizen.jobs.curJob?.def);

            RunTicks(200, citizen, raider); // a few shots in, then save mid-burst

            var root = new FightRoot { map = map, factions = new List<Faction> { ours, theirs } };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            FightRoot loadedRoot = Scribe.Load<FightRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            CoreMap loaded = loadedRoot.map!;

            Pawn loadedCitizen = loaded.mapPawns.AllPawns.First(p => p.equipment.Primary != null);
            Pawn loadedRaider = loaded.mapPawns.AllPawns.First(p => p.equipment.Primary == null);

            // The hostility that started the fight survived the round trip too, or the reloaded pawn would
            // simply stop caring about the pawn its job still names.
            Assert.True(AttackTargetsUtility.HostileTo(loadedCitizen, loadedRaider));

            Assert.Equal(CombatAIDefOf.AttackStatic, loadedCitizen.jobs.curJob?.def);
            Assert.Same(loadedRaider, loadedCitizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);

            // The loaded job resumes through a freshly rebuilt driver — the Verb is deliberately not saved
            // (no Verb in this codebase is), so the shooter simply re-aims, costing one warmup.
            for (int i = 0; i < 12000 && !loadedRaider.Downed && !loadedRaider.Dead; i++) RunTicks(1, loadedCitizen, loadedRaider);
            Assert.True(loadedRaider.Downed || loadedRaider.Dead, "The reloaded fight never resolved.");
        }
    }
}
