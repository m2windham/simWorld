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
    /// <b>Every raid resolves, and exactly one of two ways.</b> A civilization is several settlements and the
    /// player has at most one of them open, so most raids land somewhere nobody is looking — that is by
    /// design (<see cref="CivilizationTarget.ChooseTargetSettlement"/> weights by population and excludes
    /// nobody, and it deliberately still does, because a world that is only raided where the camera points is
    /// a stage set). What was wrong was the *other* half: a raid on a settlement with no reachable map
    /// generated a full squad, spawned nobody, changed nothing and returned true, so the chronicle recorded a
    /// raid on a world that had not moved. The two paths now are:
    /// <list type="bullet">
    /// <item><b>On the watched settlement's own interior</b> — the squad spawns and fights for real. This is
    /// the only physical path, and the condition is attention rather than merely "a map exists": only a
    /// <see cref="PawnTier.Full"/> citizen is ever placed on an interior
    /// (<c>World.Settlement.SyncCitizenSpawns</c>), and for an ordinary citizen only attention holds them
    /// there (spec §11.2/§11.3). Spawning raiders onto the cached-but-unattended interior of a town the
    /// player once opened would put them in an empty map with nobody to fight — the same silent nothing in a
    /// different costume, and one <c>GodCommands.GenerateSettlementInterior</c> makes directly reachable.</item>
    /// <item><b>Anywhere else</b> — <see cref="SettlementRaidResolver"/> resolves it abstractly against the
    /// settlement's own ability to defend itself, kills on both sides, takes goods if it got in, and says so
    /// in the chronicle. See that class for why the alternative (generating the interior on demand) is
    /// rejected.</item>
    /// </list>
    /// <para/>
    /// <see cref="CivilizationTarget.Map"/> — the settable hook nothing in the core has ever set — survives as
    /// exactly what it is: the arrival map for a target that has no settlements at all, which is a test pose
    /// rather than a running game. A civilization with settlements resolves through them.
    /// <para/>
    /// Arrival is RimWorld's <c>PawnsArrivalModeWorker_EdgeWalkIn</c>: one entry cell on a random map edge for
    /// the group, then each pawn placed on a walkable cell near it (<see cref="ClosewalkRadius"/>). Walking
    /// the squad from there to the colony is AI's, not this module's.
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

        /// <summary>What the most recent firing did when it resolved abstractly, or null when it resolved on a
        /// map (or had no settlement to resolve against). A diagnostic like the other <c>Last*</c> members.</summary>
        public SettlementRaidOutcome? LastRaidOutcome { get; private set; }

        /// <summary>RimWorld's own <c>PawnsArrivalModeWorker_EdgeWalkIn</c> constant: each arriving pawn is
        /// placed on a walkable cell within this radius of the group's entry cell.</summary>
        public const int ClosewalkRadius = 8;

        /// <summary>Tries before <see cref="RandomClosewalkCellNear"/> gives up and uses the entry cell itself
        /// — RimWorld's <c>CellFinder.RandomClosewalkCellNear</c> bails to the root the same way rather than
        /// searching exhaustively for a cell a raid does not need to be on.</summary>
        private const int ClosewalkTries = 30;

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

            // Which settlement the raid falls on: a civilization of several towns is raided somewhere in
            // particular, weighted by where its people are. Selection is deliberately left alone — it still
            // reaches every settlement, watched or not — because the fix for "raids land where nobody is
            // looking" is to make those raids real, not to aim them at the camera.
            var civ = parms.target as CivilizationTarget;
            LastRaidSettlement = civ?.ChooseTargetSettlement(Rand.Current);
            LastRaidOutcome = null;

            Map.Map? map = ArrivalMapFor(civ, LastRaidSettlement);
            if (map != null)
            {
                Arrive(pawns, map, Rand.Current);
            }
            else if (LastRaidSettlement != null)
            {
                LastRaidOutcome = SettlementRaidResolver.Resolve(LastRaidSettlement, pawns, faction, Rand.Current);
            }

            // Neither path taken means the target has no settlement and no map hook — a bare
            // CivilizationTarget, which is a test pose rather than a state a running game reaches (Game's own
            // SyncCivilizationTargetIfDue attaches the player's settlements every storyteller interval). There
            // is genuinely nothing there to raid, so the squad is handed back through LastRaidPawns as before.
            parms.faction = faction;
            LastRaidFaction = faction;
            LastRaidStrategy = strategy;
            LastRaidPawns = pawns;
            return true;
        }

        /// <summary>
        /// Who is raiding. A caller that already named one (a test, a forced incident, a quest) gets exactly
        /// that faction — RimWorld's <c>TryResolveRaidFaction</c> only picks when <c>parms.faction</c> is
        /// null, and pinning one is a deliberate override of selection, not a request to re-run it.
        /// <para/>
        /// Otherwise the choice is <see cref="FactionManager.RandomEnemyFaction"/>'s, narrowed to factions
        /// whose own <see cref="FactionDef.earliestRaidDays"/> says they have started raiding
        /// (<see cref="FactionRaidRules.CanRaidYet"/>). That filter runs <i>before</i> the
        /// <see cref="FactionDef.raidCommonality"/> weight roll, as RimWorld's does, so a civilization too
        /// young to be raided by anyone yet has no eligible raider at all and
        /// <see cref="CanFireNowSub"/> refuses the incident rather than the worker generating a squad with
        /// nobody behind it.
        /// </summary>
        private static Faction? ResolveFaction(IncidentParms parms) =>
            parms.faction ?? Find.FactionManager.RandomEnemyFaction(validator: FactionRaidRules.CanRaidYet);

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

        /// <summary>
        /// The map this raid physically arrives on, or null when it must resolve abstractly.
        ///
        /// <para/>A settlement's interior counts only while the god is watching that settlement — see the
        /// class doc. With no settlement at all (a target posed by hand), the bare
        /// <see cref="CivilizationTarget.Map"/> hook is still honoured through
        /// <see cref="CivilizationTarget.MapFor"/>, which is the one caller it has ever had.
        /// </summary>
        private static Map.Map? ArrivalMapFor(CivilizationTarget? civ, SimWorld.World.Settlement? settlement)
        {
            if (civ == null) return null;
            if (settlement == null) return civ.MapFor(null);
            return IsWatched(settlement) ? civ.MapFor(settlement) : null;
        }

        /// <summary>
        /// Whether the god currently has this settlement open. Asked of the god layer rather than answered
        /// here, through the same <see cref="Find"/> service locator <c>Pawns.FamilyManager</c> already uses
        /// to reach the Director layer from outside it, so "which settlement is being watched" keeps having
        /// exactly one definition (<c>God.AttentionManager</c>'s own class doc) instead of a second one that
        /// drifts.
        /// </summary>
        private static bool IsWatched(SimWorld.World.Settlement settlement) =>
            Find.God.Attention.FocusedTile == settlement.tile;

        /// <summary>
        /// RimWorld's <c>PawnsArrivalModeWorker_EdgeWalkIn.Arrive</c>: one entry cell for the group, then
        /// <c>CellFinder.RandomClosewalkCellNear(spawnCenter, map, 8)</c> per pawn. The port previously
        /// computed the edge cell once and spawned the whole squad on that single cell — a bug, not a
        /// simplification: a raid arrived as one stack of pawns standing inside each other.
        /// </summary>
        private static void Arrive(IReadOnlyList<Pawn> pawns, Map.Map map, RandomStream rand)
        {
            IntVec3 entry = RandomEdgeCell(map, rand);
            for (int i = 0; i < pawns.Count; i++)
            {
                GenSpawn.Spawn(pawns[i], RandomClosewalkCellNear(entry, map, ClosewalkRadius, rand), map);
            }
        }

        /// <summary>
        /// A standable cell within <paramref name="radius"/> of <paramref name="root"/> (RimWorld:
        /// <c>Verse.CellFinder.RandomClosewalkCellNear</c>), falling back to <paramref name="root"/> after
        /// <see cref="ClosewalkTries"/> misses. RimWorld additionally requires the cell to be in the same
        /// region and reachable; that check is not made here, because the entry cell is a map edge and the
        /// squad's approach to the colony is AI's to own — see the class doc.
        /// </summary>
        private static IntVec3 RandomClosewalkCellNear(IntVec3 root, Map.Map map, int radius, RandomStream rand)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            int inRadius = GenRadial.NumCellsInRadius(radius);
            for (int i = 0; i < ClosewalkTries; i++)
            {
                IntVec3 candidate = root + pattern[rand.Range(0, inRadius)];
                if (GenGrid.InBounds(candidate, map) && GenGrid.Standable(candidate, map)) return candidate;
            }
            return root;
        }

        /// <summary>A random cell on one of the four map edges (RimWorld's usual raid entry point) — the
        /// group's entry point, which <see cref="Arrive"/> then scatters around.</summary>
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
