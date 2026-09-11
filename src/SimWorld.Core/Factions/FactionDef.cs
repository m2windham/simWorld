using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// A kind of civilization (RimWorld: <c>RimWorld.FactionDef</c>). World generation uses it to seed rival
    /// civilizations and place their settlements; this also carries the diplomacy tuning
    /// <see cref="Faction"/>/<see cref="FactionGenerator"/> use for initial relations and goodwill drift, and
    /// the squad composition <see cref="Factions.PawnGroupMakerUtility"/> reads to build a raid
    /// (<see cref="pawnGroupMakers"/>). Caravan traveler kinds (RimWorld's <c>caravanTravelerKinds</c>) are
    /// still out of scope — nothing generates a peaceful caravan yet.
    /// </summary>
    public class FactionDef : Def
    {
        public TechLevel techLevel = TechLevel.Neolithic;

        /// <summary>Relative weight when <c>Gen.WorldGenStep_Factions</c> distributes settlements among factions.</summary>
        public float settlementGenerationWeight = 1f;

        /// <summary>At least this many factions of this def must exist after world generation.</summary>
        public int requiredCountAtGameStart;

        /// <summary>Never more than this many factions of this def, regardless of the population setting.</summary>
        public int maxCountAtGameStart = 1;

        /// <summary>Excluded from ordinary faction listings/selection (used for scenario-only or placeholder defs).</summary>
        public bool hidden;

        /// <summary>Never at peace with anyone; always hostile.</summary>
        public bool permanentEnemy;

        /// <summary>
        /// Eligible to be created by the random faction-count roll, as opposed to only ever created to satisfy
        /// <see cref="requiredCountAtGameStart"/>. Read by <c>World.EmergenceManager.EligibleFactionDefs</c>,
        /// which is this port's random faction-creation roll: a civilization emerging mid-game picks its def
        /// at random, weighted by <see cref="settlementGenerationWeight"/>.
        /// </summary>
        public bool canMakeRandomly = true;

        /// <summary>The (singular) player civilization def.</summary>
        public bool isPlayer;

        /// <summary>Lower bound on settlements for one instance of this faction once created; null = no extra floor beyond 1.</summary>
        public int? minSettlements;

        // ---- diplomacy (RimWorld: RimWorld.FactionDef's goodwill/relation tuning) ----

        /// <summary>Equilibrium goodwill this faction's relation with the player naturally drifts toward via <see cref="Faction.FactionTick"/>.</summary>
        public IntRange naturalColonyGoodwill = IntRange.Zero;

        /// <summary>Goodwill gained per day while below <see cref="naturalColonyGoodwill"/>.</summary>
        public float goodwillDailyGain;

        /// <summary>Goodwill lost per day while above <see cref="naturalColonyGoodwill"/>.</summary>
        public float goodwillDailyFall;

        /// <summary>Range the initial goodwill with the player is drawn from at world generation (see <see cref="Faction.TryMakeInitialRelationsWith"/>).</summary>
        public IntRange startingGoodwill = IntRange.Zero;

        /// <summary>
        /// Set on the player's own def: world generation (<see cref="FactionGenerator"/>) guarantees at
        /// least one other faction starts Hostile to a faction whose def has this set, even if none rolled
        /// hostile naturally.
        /// </summary>
        public bool mustStartOneEnemy;

        /// <summary>Relative weight this faction is picked to raid with, versus other hostile factions —
        /// consumed by <see cref="Sim.Find.FactionManager"/>'s <c>RandomEnemyFaction</c>, which is what
        /// <see cref="Director.IncidentWorker_RaidEnemy"/> calls to choose a raider.</summary>
        public float raidCommonality = 1f;

        /// <summary>This faction's ruler's title (e.g. "chief", "governor"). Flavor text only so far.</summary>
        public string? leaderTitle;

        /// <summary>
        /// Prefix bank <see cref="FactionNameMaker"/> draws from when naming an instance of this faction;
        /// falls back to a bank keyed by <see cref="techLevel"/> when empty.
        /// </summary>
        public List<string>? settlementNamePrefixes;

        /// <summary>Ambient temperature band a caravan/trader of this faction will travel in. Not yet consumed — no weather/temperature system exists to gate arrivals against it.</summary>
        public FloatRange allowedArrivalTemperatureRange = new FloatRange(-1000f, 1000f);

        /// <summary>False for non-sapient factions (insect hives, mechanoid clusters); affects nothing yet, kept for parity with content that may set it.</summary>
        public bool humanlikeFaction = true;

        /// <summary>
        /// Whether this faction is naturally hostile to humanlike pawns with no faction of their own — read by
        /// <c>AI.AttackTargetsUtility.HostileTo</c>. Far from a corner case in this port: every settlement
        /// citizen is factionless, so for a raider this is the difference between a fight and a pantomime.
        /// See that method for the measurement.
        /// </summary>
        public bool hostileToFactionlessHumanlikes;

        /// <summary>
        /// Whether this faction's war bands break off once they have lost enough of themselves, rather than
        /// fighting to the last raider. Read by <see cref="FactionRaidRules.MaxRaidersLost"/>, which is where
        /// the translation of RimWorld's Lord-graph transition into this port's abstract raid resolution is
        /// written down. A raid the god is <i>watching</i> still has no flight behaviour on either side — see
        /// <c>AI.CombatPostureUtility</c>, which records that gap as its own.
        /// </summary>
        public bool autoFlee = true;

        public string? pawnSingular;

        public string? pawnsPlural;

        /// <summary>
        /// Days after world start before this faction may raid. Read by
        /// <see cref="FactionRaidRules.CanRaidYet"/>, which <see cref="Director.IncidentWorker_RaidEnemy"/>
        /// hands to <see cref="FactionManager.RandomEnemyFaction"/> as its validator — so a faction too young
        /// to raid is filtered out before the <see cref="raidCommonality"/> weight roll, as RimWorld filters
        /// its own. Not redundant with the storyteller's pacing: Randy Random declares no day floor at all.
        /// </summary>
        public int earliestRaidDays;

        // ---- squad composition (RimWorld: FactionDef.pawnGroupMakers) ----

        /// <summary>
        /// Which pawn squads this faction can field, by <see cref="PawnGroupKindDef"/> (RimWorld:
        /// <c>FactionDef.pawnGroupMakers</c>). A faction with none for <see cref="PawnGroupKindDefOf.Combat"/>
        /// can never raid, no matter how much <c>raidCommonality</c> or hostility it has — see
        /// <see cref="GetGroupMaker"/> and <see cref="Director.IncidentWorker_RaidEnemy"/>. This is the whole
        /// tech-level gate on raid composition: a neolithic faction's own makers simply never list an
        /// industrial <see cref="Pawns.PawnKindDef"/>, so it structurally cannot generate one.
        /// </summary>
        public List<PawnGroupMaker>? pawnGroupMakers;

        /// <summary>
        /// Picks among this def's <see cref="pawnGroupMakers"/> for <paramref name="kindDef"/>, weighted by
        /// <see cref="PawnGroupMaker.commonality"/> when more than one matches (RimWorld:
        /// <c>FactionDef.GetGroupMaker</c>). Null when none do.
        /// </summary>
        public PawnGroupMaker? GetGroupMaker(PawnGroupKindDef kindDef)
        {
            if (pawnGroupMakers == null || kindDef == null) return null;

            PawnGroupMaker? single = null;
            List<PawnGroupMaker>? matches = null;
            for (int i = 0; i < pawnGroupMakers.Count; i++)
            {
                if (pawnGroupMakers[i].kindDef != kindDef) continue;
                if (single == null && matches == null)
                {
                    single = pawnGroupMakers[i];
                    continue;
                }
                matches ??= new List<PawnGroupMaker> { single! };
                matches.Add(pawnGroupMakers[i]);
            }
            if (matches == null) return single;
            return GenCollection.TryRandomElementByWeight((IReadOnlyList<PawnGroupMaker>)matches, m => m.commonality, Rand.Current, out PawnGroupMaker picked)
                ? picked
                : matches[0];
        }

        // ---- trade (RimWorld: FactionDef.caravanTraderKinds) ----

        /// <summary>Trader kinds this faction's caravans can arrive as (RimWorld: <c>FactionDef.caravanTraderKinds</c>) — read by <see cref="Director.IncidentWorker_TraderCaravanArrival"/>, weighted by <see cref="TraderKindDef.commonality"/> via <see cref="RandomTraderKind"/>.</summary>
        public List<TraderKindDef>? caravanTraderKinds;

        /// <summary>
        /// Weighted pick among <see cref="caravanTraderKinds"/> — the same "single match skips the weight
        /// roll, several matches weight by commonality" idiom <see cref="GetGroupMaker"/> already uses for
        /// squads, centralized here for trade instead of RimWorld's own ad hoc per-call-site resolution.
        /// Null when this faction has none.
        /// </summary>
        public TraderKindDef? RandomTraderKind(RandomStream rand)
        {
            if (caravanTraderKinds == null || caravanTraderKinds.Count == 0) return null;
            if (caravanTraderKinds.Count == 1) return caravanTraderKinds[0];
            return GenCollection.TryRandomElementByWeight((IReadOnlyList<TraderKindDef>)caravanTraderKinds, k => k.commonality, rand, out TraderKindDef picked)
                ? picked
                : caravanTraderKinds[0];
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (pawnGroupMakers != null)
            {
                foreach (PawnGroupMaker maker in pawnGroupMakers)
                {
                    foreach (string error in maker.ConfigErrors()) yield return error;
                }
            }
        }
    }
}
