using System;
using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.Director
{
    /// <summary>What one abstractly-resolved raid did. Values only — no live object a caller could reach a
    /// settlement or a pawn through, so a host or a test can hold one without holding the world.</summary>
    public readonly struct SettlementRaidOutcome
    {
        internal SettlementRaidOutcome(
            bool resolved, bool repelled, float defenceStrength, float raidStrength, int defendersMustered,
            int citizensKilled, int cohortLosses, int raidersKilled, int goodsLooted)
        {
            Resolved = resolved;
            Repelled = repelled;
            DefenceStrength = defenceStrength;
            RaidStrength = raidStrength;
            DefendersMustered = defendersMustered;
            CitizensKilled = citizensKilled;
            CohortLosses = cohortLosses;
            RaidersKilled = raidersKilled;
            GoodsLooted = goodsLooted;
        }

        /// <summary>False only for the degenerate call that had nothing to resolve — no raiders left alive to
        /// attack with. Every other firing produces a real engagement, win or lose.</summary>
        public bool Resolved { get; }

        /// <summary>The settlement held. When false the raid got in: it killed more and it took goods.</summary>
        public bool Repelled { get; }

        /// <summary>The defence, in <c>PawnKindDef.combatPower</c> — see <see cref="SettlementRaidResolver.DefenceStrengthOf"/>.</summary>
        public float DefenceStrength { get; }

        /// <summary>The attack, in the same unit: the sum of the living raiders' own <c>combatPower</c>.</summary>
        public float RaidStrength { get; }

        /// <summary>How many people turned out to defend — every live citizen plus the mustered share of the
        /// Statistical cohort. The pool casualties are drawn from, and the reason a city cannot be
        /// depopulated by a war band.</summary>
        public int DefendersMustered { get; }

        /// <summary>Citizens with a real <c>Pawn</c> who died, each through <c>FamilyManager.HandleDeath</c>.</summary>
        public int CitizensKilled { get; }

        /// <summary>People lost from the settlement's bare Statistical cohort, which has no <c>Pawn</c> per
        /// head to kill — see <see cref="SettlementRaidResolver.Resolve"/>'s note on the asymmetry.</summary>
        public int CohortLosses { get; }

        /// <summary>Raiders killed. They are the attacking faction's, not the civilization's — see
        /// <see cref="SettlementRaidResolver.Resolve"/> for why their deaths take a different path.</summary>
        public int RaidersKilled { get; }

        /// <summary>Total item count carried off across every store. Zero on a repelled raid.</summary>
        public int GoodsLooted { get; }

        /// <summary>Everyone the settlement lost, however they were represented.</summary>
        public int TotalLivesLost => CitizensKilled + CohortLosses;

        /// <summary>True when this raid left the settlement measurably different — the property the whole
        /// module exists to make true, and the one <c>UnwatchedRaidTests</c> asserts on.</summary>
        public bool ChangedTheWorld => TotalLivesLost > 0 || GoodsLooted > 0 || RaidersKilled > 0;
    }

    /// <summary>
    /// A raid on a settlement nobody is watching, resolved as an outcome rather than as a battle
    /// (tracker item <c>director.raids</c>).
    ///
    /// <para/><b>Why abstractly, and not by generating the map.</b> A civilization is meant to run without the
    /// player watching every town, so raids have to be able to land on towns nobody has open — biasing
    /// selection toward the camera would make the unwatched world a stage set, which is precisely what §11's
    /// tiering exists to avoid. The other way to make an unwatched raid real is to generate that settlement's
    /// interior on demand and fight it properly, and that is rejected for the reason
    /// <c>World.Settlement.EnterMap</c> and spec §9 already state in their own words: a generated
    /// <c>Map.Map</c> is the single most expensive thing a save can hold, and "generating a map just to stage
    /// an off-screen raid would be the tail wagging the dog". Worse, it would make the cost of a save scale
    /// with the narrator's dice instead of with the player's attention — open one town in a civilization of
    /// thirty and the storyteller would still, over a century, force thirty interiors into existence.
    ///
    /// <para/><b>So: real but not individually simulated</b> — §11.3's own phrase for the Statistical tier,
    /// applied one level up to an engagement instead of to a person. The raid is priced against the
    /// settlement's ability to defend itself, rolled once, and the result is spent on the things a settlement
    /// actually has to lose: its people and its stores. What it must never be is free. The behaviour this
    /// replaces generated a full squad, found no map, and returned true — the chronicle recorded a raid and
    /// the world was untouched, which is the bug.
    ///
    /// <para/><b>Everything is counted in <c>PawnKindDef.combatPower</c></b>, the unit the raid was already
    /// bought in, so the two sides are commensurable without a conversion constant. Tuning lives in
    /// <see cref="RaidResolutionTuning"/>, all of it SimWorld's own: RimWorld has one colony and it is always
    /// on screen, so there is no abstract resolution there to port.
    /// </summary>
    public static class SettlementRaidResolver
    {
        /// <summary>
        /// How much fight a settlement can put up, in <c>PawnKindDef.combatPower</c>.
        ///
        /// <para/>Two populations, counted two different ways, exactly as every other settlement-scale
        /// calculation in this codebase already does (<c>God.GodRollup</c>, <c>Pawns.MigrationManager</c>):
        /// <list type="bullet">
        /// <item>Every living humanlike citizen with a real <c>Pawn</c> defends, at their kind's own
        /// <c>combatPower</c>, scaled by how hurt they are and by their best combat skill.</item>
        /// <item>The bare <c>Settlement.StatisticalPopulation</c> — a count with no <c>Pawn</c> per head by
        /// that tier's design — musters <see cref="RaidResolutionTuning.StatisticalMusterFraction"/> of itself
        /// at a flat <see cref="RaidResolutionTuning.StatisticalDefenderCombatPower"/> each. It is never
        /// enumerated: the militia is arithmetic on a count, not forty thousand objects.</item>
        /// </list>
        /// </summary>
        public static float DefenceStrengthOf(Settlement settlement) => Muster(settlement).Strength;

        /// <summary>How many people a settlement would field against a raid — see <see cref="DefenceStrengthOf"/>.</summary>
        public static int MusterOf(Settlement settlement) => Muster(settlement).Heads;

        /// <summary>
        /// Fights the raid and applies the result to the world.
        ///
        /// <para/><b>The roll.</b> The defenders hold with probability <c>defence / (defence + raid)</c> —
        /// each side's share of the total strength in the field. Monotone in both directions, bounded in
        /// [0,1] by construction, and it needs no threshold anyone would have to justify: a hamlet can turn
        /// away a small band and a city can still be sacked by a large enough one.
        ///
        /// <para/><b>The cost.</b> Each side's killing potential is its strength times
        /// <see cref="RaidResolutionTuning.DeathsPerCombatPower"/>; the side that lost the engagement suffers
        /// <see cref="RaidResolutionTuning.LoserLossFraction"/> of the enemy's potential and the side that won
        /// suffers <see cref="RaidResolutionTuning.WinnerLossFraction"/> of it. Casualties are therefore
        /// bounded by what the *enemy* could inflict rather than by how big you are — which is what stops a
        /// forty-thousand-person city losing forty times as many people to the same war band as a thousand-
        /// person town does. Whoever lost always loses at least one body: a raid is never free to either side.
        /// The raiders carry one further bound the defenders do not — <see cref="FactionRaidRules.MaxRaidersLost"/>,
        /// which is <see cref="FactionDef.autoFlee"/>'s reader: a band that breaks off once it has lost enough
        /// of itself is not there to take the rest of the casualties, while a faction that declares it does
        /// not flee can be killed to the last raider. A settlement, having nowhere to withdraw to, has no
        /// counterpart.
        ///
        /// <para/><b>Deaths take two different paths, on purpose.</b> A citizen with a real <c>Pawn</c> dies
        /// through <c>FamilyManager.HandleDeath</c> — the same path a death from age already takes — so the
        /// household's living count, the bereavement of their spouse, parents and children, the chronicle line
        /// and the <c>MomentCurator</c>'s record all happen exactly once and exactly as they would for any
        /// other death. Reaching for <c>Pawn_HealthTracker.Kill</c> here instead would leave the family tree
        /// and the chronicle disagreeing about who is alive. The Statistical cohort cannot take that path at
        /// all: there is no <c>Pawn</c> to kill, no household to shrink and nobody to bereave, because that
        /// tier's whole design is that those records do not exist — so the cohort loses heads through
        /// <c>Settlement.RemoveStatisticalPeople</c>, the mirror of how it gains them, and the count appears
        /// in the chronicle line as a count. That asymmetry is the tier showing through, not a shortcut.
        ///
        /// <para/><b>Raiders are killed directly.</b> They belong to the attacking faction, not to the
        /// civilization: they are in no settlement's roster and no <c>Family</c>, so
        /// <c>HandleDeath</c>'s bookkeeping would have nothing to do and its chronicle line would report the
        /// civilization losing someone it never had. Their losses are reported in the raid's own line instead.
        /// </summary>
        /// <param name="settlement">The settlement raided. Never null.</param>
        /// <param name="raiders">The generated squad. Dead members are ignored.</param>
        /// <param name="attacker">Whose raid it was, for the chronicle line only; null reads as "raiders".</param>
        /// <param name="rand">The seeded stream every roll here goes through.</param>
        public static SettlementRaidOutcome Resolve(
            Settlement settlement, IReadOnlyList<Pawn> raiders, Faction? attacker, RandomStream rand)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (raiders == null) throw new ArgumentNullException(nameof(raiders));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            var livingRaiders = new List<Pawn>();
            float raidStrength = 0f;
            for (int i = 0; i < raiders.Count; i++)
            {
                Pawn raider = raiders[i];
                if (raider == null || raider.Dead) continue;
                livingRaiders.Add(raider);
                raidStrength += CombatPowerOf(raider);
            }

            if (livingRaiders.Count == 0)
            {
                return new SettlementRaidOutcome(false, true, DefenceStrengthOf(settlement), 0f, 0, 0, 0, 0, 0);
            }

            (float defenceStrength, int musteredHeads, int liveMuster, int cohortMuster) = Muster(settlement);

            float total = defenceStrength + raidStrength;
            bool repelled = total > 0f && rand.Chance(defenceStrength / total);

            float raidPotential = raidStrength * RaidResolutionTuning.DeathsPerCombatPower;
            float defencePotential = defenceStrength * RaidResolutionTuning.DeathsPerCombatPower;

            int defenderDeaths = Casualties(raidPotential, loser: !repelled, cap: musteredHeads, rand: rand);

            // FactionDef.autoFlee, which until now no line of src/ read. A war band whose faction breaks off
            // once it has lost enough of itself cannot lose more than that; one that does not (the shipped
            // RoughOutlanders, who declare autoFlee false) can be killed to the last raider. The defenders
            // have no such cap on purpose: a settlement cannot withdraw from itself.
            int raiderDeaths = Casualties(
                defencePotential, loser: repelled,
                cap: FactionRaidRules.MaxRaidersLost(attacker, livingRaiders.Count), rand: rand);

            // Defender losses fall on the two population slices in the proportion each of them mustered in —
            // the live roster is not preferentially slaughtered just because it is the slice with names.
            int citizensToKill = musteredHeads > 0
                ? (int)Math.Round(defenderDeaths * (double)liveMuster / musteredHeads, MidpointRounding.AwayFromZero)
                : 0;
            citizensToKill = Math.Min(citizensToKill, liveMuster);
            int cohortToLose = Math.Min(defenderDeaths - citizensToKill, cohortMuster);

            int citizensKilled = KillCitizens(settlement, citizensToKill, rand);
            int cohortLost = cohortToLose > 0 ? settlement.RemoveStatisticalPeople(cohortToLose) : 0;

            // RimWorld's adaptation drops when the player loses people to a threat, which is what makes the
            // next raid smaller after a bad one. Only named citizens count toward it: a cohort head is a
            // citizen, but the penalty is per person, and three hundred anonymous losses would zero the curve
            // in one raid — whereas losing three people you knew is exactly the event the curve is for.
            //
            // Still charged from here, and still exactly once. StorytellerDeathEvents.Notify_PawnDied now
            // charges the same hook for a violent death anywhere — but "violent" there means a DamageDef
            // that declares externalViolence, and this resolver settles its battle arithmetically and kills
            // through FamilyManager.HandleDeath, which hands Kill no DamageInfo at all. So these deaths read
            // as non-violent to that hook and are counted here, once, as they always were. A death from age
            // reaches neither.
            for (int i = 0; i < citizensKilled; i++) Find.Storyteller.adaptation.Notify_ColonistDied();
            int raidersKilled = KillRaiders(livingRaiders, raiderDeaths, rand);
            int looted = repelled ? 0 : Loot(settlement, rand);

            var outcome = new SettlementRaidOutcome(
                true, repelled, defenceStrength, raidStrength, musteredHeads,
                citizensKilled, cohortLost, raidersKilled, looted);
            RecordChronicle(settlement, attacker, outcome);
            return outcome;
        }

        // ---- defence ----

        private static (float Strength, int Heads, int LiveHeads, int CohortHeads) Muster(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            float strength = 0f;
            int liveHeads = 0;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn citizen = citizens[i];
                if (citizen == null || citizen.Dead || !citizen.RaceProps.Humanlike) continue;
                liveHeads++;
                strength += CombatPowerOf(citizen) * HealthFactorOf(citizen) * SkillFactorOf(citizen);
            }

            // Arithmetic on a count — the cohort is never walked, because by §11.3's design there is nobody
            // there to walk. Rounded down: a militia is people who exist, and half a defender is none.
            int cohortHeads = (int)(settlement.StatisticalPopulation * RaidResolutionTuning.StatisticalMusterFraction);
            strength += cohortHeads * RaidResolutionTuning.StatisticalDefenderCombatPower;

            return (strength, liveHeads + cohortHeads, liveHeads, cohortHeads);
        }

        private static float CombatPowerOf(Pawn pawn)
        {
            float power = pawn.kindDef?.combatPower ?? RaidResolutionTuning.DefaultDefenderCombatPower;
            return power > 0f ? power : RaidResolutionTuning.DefaultDefenderCombatPower;
        }

        /// <summary>A hurt defender fights worse, read from the same <c>SummaryHealthPercent</c> the threat
        /// curve itself already scales per-pawn points by (<see cref="StorytellerUtility.DefaultThreatPointsNow"/>).
        /// A Statistical citizen who still has a live <c>Pawn</c> is read through their tier's sampled health
        /// instead, the one line <c>God.GodRollup</c> is emphatic must never become an unconditional hediff
        /// read — the same rule applies here for the same reason.</summary>
        private static float HealthFactorOf(Pawn pawn) =>
            pawn.tier.Tier == PawnTier.Statistical
                ? pawn.tier.SampledHealthFraction
                : pawn.health.summaryHealth.SummaryHealthPercent;

        private static float SkillFactorOf(Pawn pawn)
        {
            SkillRecord? shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting);
            SkillRecord? melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
            int best = Math.Max(shooting?.Level ?? RaidResolutionTuning.UntrainedCombatSkillLevel,
                                melee?.Level ?? RaidResolutionTuning.UntrainedCombatSkillLevel);
            float factor = 1f + (best - RaidResolutionTuning.UntrainedCombatSkillLevel) * RaidResolutionTuning.CombatPowerPerSkillLevel;
            return factor > 0f ? factor : 0f;
        }

        // ---- casualties ----

        /// <summary>Heads lost by one side: a fraction of what the other side could kill, drawn from the
        /// loser's band or the winner's, floored at one body for the loser so no engagement is ever free, and
        /// capped at how many of that side are there to lose — every head for a settlement, and for a war
        /// band however many of it will stay (<see cref="FactionRaidRules.MaxRaidersLost"/>).</summary>
        private static int Casualties(float enemyPotential, bool loser, int cap, RandomStream rand)
        {
            if (cap <= 0) return 0;
            FloatRange band = loser ? RaidResolutionTuning.LoserLossFraction : RaidResolutionTuning.WinnerLossFraction;
            int deaths = (int)Math.Round(enemyPotential * rand.Range(band), MidpointRounding.AwayFromZero);
            if (loser && deaths < 1) deaths = 1;
            return Math.Min(Math.Max(deaths, 0), cap);
        }

        /// <summary>
        /// Kills <paramref name="count"/> randomly-chosen living citizens through the real death path, then
        /// reconciles the settlement so its population stops counting them immediately rather than at the next
        /// rare sync. Returns how many actually died.
        /// </summary>
        private static int KillCitizens(Settlement settlement, int count, RandomStream rand)
        {
            if (count <= 0) return 0;

            var candidates = new List<Pawn>();
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn citizen = citizens[i];
                if (citizen != null && !citizen.Dead && citizen.RaceProps.Humanlike) candidates.Add(citizen);
            }
            if (candidates.Count == 0) return 0;

            // Relatives are looked up within the settlement's own roster, the same scope
            // Settlement.GrowthTick already runs FamilyManager.DemographyTick against: a death bereaves the
            // household you live among, and this module has no business assembling a planet-wide index to
            // widen that.
            var byId = new Dictionary<int, Pawn>();
            for (int i = 0; i < candidates.Count; i++) byId[candidates[i].thingIDNumber] = candidates[i];

            int killed = 0;
            int wanted = Math.Min(count, candidates.Count);
            while (killed < wanted && candidates.Count > 0)
            {
                int index = rand.Range(0, candidates.Count);
                Pawn victim = candidates[index];
                candidates.RemoveAt(index);
                if (victim.Dead) continue;
                Find.FamilyManager.HandleDeath(victim, DeathCause.Injury, byId);
                killed++;
            }

            if (killed > 0) settlement.SyncCitizenSpawns();
            return killed;
        }

        private static int KillRaiders(List<Pawn> livingRaiders, int count, RandomStream rand)
        {
            int killed = 0;
            int wanted = Math.Min(count, livingRaiders.Count);
            while (killed < wanted && livingRaiders.Count > 0)
            {
                int index = rand.Range(0, livingRaiders.Count);
                Pawn raider = livingRaiders[index];
                livingRaiders.RemoveAt(index);
                if (raider.Dead) continue;
                raider.health.Kill(null, null);
                killed++;
            }
            return killed;
        }

        // ---- loot ----

        /// <summary>
        /// Carries off a share of every store. The goods leave the world rather than moving into the raiding
        /// faction's ledger: a raiding party is a generated squad with no settlement behind it in this port,
        /// so there is nowhere for them to land that would not be inventing a faction-stores system on the way
        /// past. What the settlement lost is real either way, and that is the half this module owns.
        /// </summary>
        private static int Loot(Settlement settlement, RandomStream rand)
        {
            var held = new List<KeyValuePair<ThingDef, int>>(settlement.Stores);
            if (held.Count == 0) return 0;

            float fraction = rand.Range(RaidResolutionTuning.LootFraction);
            int total = 0;
            for (int i = 0; i < held.Count; i++)
            {
                int have = held[i].Value;
                if (have <= 0) continue;
                int taken = Math.Min(have, Math.Max(1, (int)(have * fraction)));
                settlement.AddStore(held[i].Key, -taken);
                total += taken;
            }
            return total;
        }

        // ---- narration ----

        /// <summary>
        /// One free-form chronicle line, through the same <see cref="Storyteller.RecordChronicle(string)"/>
        /// hook births, migrations and edicts already use — so the "Raid" category flows into
        /// <see cref="MomentCurator"/>'s first-of-its-kind rule with no call site anywhere needing to know.
        /// This is the half of the contract that says a raid must be *visible*; the rest of
        /// <see cref="Resolve"/> is the half that says it must be *real*.
        /// </summary>
        private static void RecordChronicle(Settlement settlement, Faction? attacker, SettlementRaidOutcome outcome)
        {
            string who = string.IsNullOrEmpty(attacker?.name) ? "Raiders" : attacker!.name;
            string head = outcome.Repelled
                ? who + " struck " + settlement.name + " and were driven off"
                : who + " overran " + settlement.name;

            var cost = new List<string>();
            if (outcome.TotalLivesLost > 0) cost.Add(Count(outcome.TotalLivesLost) + " lost defending it");
            if (outcome.RaidersKilled > 0) cost.Add(Count(outcome.RaidersKilled) + " of the attackers dead");
            if (outcome.GoodsLooted > 0) cost.Add(Num(outcome.GoodsLooted) + " goods carried off");

            string tail = cost.Count == 0 ? "; it had nothing left to take" : "; " + string.Join(", ", cost);
            Find.Storyteller.RecordChronicle("Raid: " + head + tail + ".");
        }

        private static string Count(int n) => Num(n) + (n == 1 ? " person" : " people");

        private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
