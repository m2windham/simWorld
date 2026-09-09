using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Director
{
    /// <summary>
    /// Real raid squad generation (RimWorld: <c>RimWorld.IncidentWorker_RaidEnemy</c>), replacing
    /// <see cref="IncidentWorker_Placeholder"/> for <see cref="IncidentDefOf.RaidEnemy"/>. Picks a hostile
    /// faction, a <see cref="RaidStrategyDef"/> that faction's tech level allows, and spends
    /// <see cref="IncidentParms.points"/> — scaled by the strategy's <see cref="RaidStrategyDef.pointsFactor"/> —
    /// against that faction's own <see cref="FactionDef.pawnGroupMakers"/> through
    /// <see cref="PawnGroupMakerUtility"/>. A neolithic faction can never field an industrial raider: its own
    /// <see cref="PawnGroupMaker"/> only lists neolithic <see cref="PawnKindDef"/>s in the first place, and
    /// every generated pawn's weapon is separately capped at the same faction's tech level
    /// (<see cref="Pawns.Generation.PawnWeaponGenerator"/>) as a second, independent check.
    /// <para/>
    /// <b>Where this stops — read before assuming a raid does anything to the player:</b> nothing in the
    /// civilization-scale incident model (<see cref="IIncidentTarget"/> / <see cref="CivilizationTarget"/>)
    /// carries a reachable <c>Map.Map</c> for raiders to fight on — local maps
    /// (<c>MapGen.MapGenerator</c>) are generated per settlement, on demand, and never handed back to the
    /// storyteller or its targets. <see cref="CivilizationTarget.Map"/> is a settable hook for whichever
    /// future work wires a physical map to the player's civilization; every game today leaves it null. When
    /// it <i>is</i> set, this worker spawns the squad at a random map edge (RimWorld's usual raid entry
    /// point) rather than reaching into AI/Reachability for a real approach path, which this module does not
    /// own. Until that hook is set, the squad is fully generated, equipped and attributed to its faction and
    /// handed to the caller through <see cref="LastRaidPawns"/> — the arrival-and-fight-the-player loop is
    /// Map/AI's to build on top of this.
    /// </summary>
    public sealed class IncidentWorker_RaidEnemy : IncidentWorker
    {
        /// <summary>Test/inspection hook (mirrors <see cref="Combat.Verb_LaunchProjectile.LastShot"/>'s
        /// pattern): the faction the most recent successful firing raided with.</summary>
        public Faction? LastRaidFaction { get; private set; }

        /// <summary>The tactic the most recent successful firing picked.</summary>
        public RaidStrategyDef? LastRaidStrategy { get; private set; }

        /// <summary>The squad the most recent successful firing generated.</summary>
        public IReadOnlyList<Pawn>? LastRaidPawns { get; private set; }

        /// <summary>The settlement the most recent successful firing aimed at, or null when the civilization
        /// had none attached. A diagnostic of the last fire like the other <c>Last*</c> members here, not
        /// saved state.</summary>
        public SimWorld.World.Settlement? LastRaidSettlement { get; private set; }

        protected override bool CanFireNowSub(IncidentParms parms) => parms.points > 0f && ResolveFaction(parms) != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Faction? faction = ResolveFaction(parms);
            if (faction == null) return false;

            RaidStrategyDef strategy = ChooseStrategy(faction);
            var groupParms = new PawnGroupMakerParms
            {
                faction = faction,
                groupKind = strategy.Worker.PawnGroupKind,
                points = Math.Max(parms.points * strategy.pointsFactor, 0f),
                raidStrategy = strategy,
            };

            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(groupParms);
            if (pawns.Count == 0) return false;

            // Which settlement the raid falls on, and therefore which map it arrives at: a civilization of
            // several towns is raided somewhere in particular, weighted by where its people are. A settlement
            // nobody has entered has no interior map, so the raid still resolves without one — the squad is
            // generated and handed back unspawned exactly as it was before settlements existed.
            var civ = parms.target as CivilizationTarget;
            LastRaidSettlement = civ?.ChooseTargetSettlement(Rand.Current);
            Map.Map? map = civ?.MapFor(LastRaidSettlement);
            if (map != null)
            {
                IntVec3 edge = RandomEdgeCell(map, Rand.Current);
                foreach (Pawn pawn in pawns) GenSpawn.Spawn(pawn, edge, map);
            }

            parms.faction = faction;
            LastRaidFaction = faction;
            LastRaidStrategy = strategy;
            LastRaidPawns = pawns;
            return true;
        }

        private static Faction? ResolveFaction(IncidentParms parms) => parms.faction ?? Find.FactionManager.RandomEnemyFaction();

        /// <summary>Uniformly among the tactics <paramref name="faction"/>'s tech level allows, weighted by <see cref="RaidStrategyDef.selectionWeight"/>; falls back to <see cref="RaidStrategyDefOf.ImmediateAttack"/> if content somehow leaves nothing usable.</summary>
        private static RaidStrategyDef ChooseStrategy(Faction faction)
        {
            List<RaidStrategyDef> usable = DefDatabase<RaidStrategyDef>.AllDefsListForReading
                .Where(s => s.Worker.CanUseWith(faction))
                .ToList();
            if (usable.Count == 0) return RaidStrategyDefOf.ImmediateAttack;
            return GenCollection.TryRandomElementByWeight((IReadOnlyList<RaidStrategyDef>)usable, s => s.selectionWeight, Rand.Current, out RaidStrategyDef picked)
                ? picked
                : usable[0];
        }

        /// <summary>A random cell on one of the four map edges (RimWorld's usual raid entry point) — the
        /// closest this module gets to real arrival logic; see the class doc for why it stops here.</summary>
        private static IntVec3 RandomEdgeCell(Map.Map map, RandomStream rand)
        {
            switch (rand.Range(0, 4))
            {
                case 0: return new IntVec3(0, rand.Range(0, map.Size.z));
                case 1: return new IntVec3(map.Size.x - 1, rand.Range(0, map.Size.z));
                case 2: return new IntVec3(rand.Range(0, map.Size.x), 0);
                default: return new IntVec3(rand.Range(0, map.Size.x), map.Size.z - 1);
            }
        }
    }
}
