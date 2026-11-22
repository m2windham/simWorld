using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Defence
{
    /// <summary>Defs the settlement's defence reads by name. Its own <c>[DefOf]</c> class, per CLAUDE.md.</summary>
    [DefOf]
    public static class DefenceDefOf
    {
        /// <summary>Agrarian (<c>Trapping</c>): a sharpened-stake pit, wood.</summary>
        public static ThingDef TrapSpike = null!;

        /// <summary>Medieval (<c>Gunpowder</c>): a buried charge, steel.</summary>
        public static ThingDef TrapIED = null!;
    }

    /// <summary>
    /// Translation: <b>a settlement keeps a watch and lays traps in proportion to the threat it draws</b>
    /// (system: combat, defence by era). RimWorld's player drafts colonists when a raid comes and builds traps
    /// and turrets in proportion to the raids they have seen; there is no player here, so the settlement does.
    ///
    /// <para/><b>The problem it solves.</b> The era-arc runs found threat growing with wealth and era while
    /// defence never grew at all: walls eroded, no trap or turret was ever placed, and a single Classical raid
    /// of about 750 points killed 10 of 15 people (<c>docs/research/era-arc.md</c> §5.3, §7.3 gap 5).
    ///
    /// <para/><b>Sized by the threat, in the threat's own unit.</b> A raid is bought with
    /// <see cref="StorytellerUtility.DefaultThreatPointsNow"/> points spent on raiders at their
    /// <c>PawnKindDef.combatPower</c>. So the number of raiders the next raid would bring is about the points
    /// over one raider's power, and <see cref="RaidResolutionTuning.DefaultDefenderCombatPower"/> is the stand-in
    /// for that power the raid resolver already uses. The watch and the trap line are each that many: man for
    /// man, and a trap for each man. When the threat rises so do they; nothing here is a flat number.
    ///
    /// <para/><b>The watch.</b> The best fighters, armed first, then by combat skill, up to that count, are on
    /// watch: they go looking for a hostile at the full acquire radius, as the <c>TakeUpArms</c> edict makes
    /// a citizen do (<see cref="CombatPostureUtility.TargetAcquireRadiusFor"/>), instead of waiting to be
    /// cornered. Their arms are the material ladder's (<see cref="MaterialLadder"/>, <see cref="OutfittingInitiative"/>):
    /// a flint knife in the first age, bronze and iron blades after.
    ///
    /// <para/><b>What the watch does not do.</b> An armed citizen already engages at that radius without being
    /// told (<see cref="CombatPostureUtility"/>, rule 1), so the watch changes what an <i>unarmed</i> fighter does
    /// and nothing else. A founding band comes armed, and a tooled settlement stays armed, so in an ordinary run it
    /// changes little. Gathering armed citizens to hold a position together, which is what drafting buys a RimWorld
    /// player, is not built.
    ///
    /// <para/><b>The trap line, by era.</b> Traps are laid evenly on a ring
    /// <see cref="TrapRingOffset"/> cells outside the village (<see cref="SettlementCompound.Village"/>), where a
    /// raid walks in: the best trap the settlement knows and can pay for (<see cref="BestTrapDef"/>), spike
    /// traps from Agrarian, IEDs from Medieval. A sprung trap stays spent and a new one is laid on the next free
    /// cell of the ring, which stands in for RimWorld's rearm job.
    ///
    /// <para/><b>Stateless.</b> The watch is re-derived once per rare bucket per map and cached against that
    /// bucket, so a loaded save finds the same watch as the run that saved it; the traps are on the map.
    /// </summary>
    public static class SettlementDefence
    {
        /// <summary>Self-gate cadence: the rare bucket every map initiative uses.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>How far outside the village the trap ring runs. Unsourced: past the reach of an IED's
        /// blast (3.5 cells) from the outermost house.</summary>
        public const int TrapRingOffset = 4;

        /// <summary>Where the threat is read from. The storyteller's own figure for the running game; a seam
        /// so a test can pose a threat without a running storyteller.</summary>
        public static Func<Map.Map, float> ThreatPointsOf = DefaultThreatPointsOf;

        private sealed class WatchCache
        {
            public int Bucket = -1;
            public readonly HashSet<int> Ids = new HashSet<int>();
        }

        private static readonly ConditionalWeakTable<Map.Map, WatchCache> watches = new ConditionalWeakTable<Map.Map, WatchCache>();

        /// <summary>The storyteller's threat points for the running game, or 0 with no game.</summary>
        public static float DefaultThreatPointsOf(Map.Map map)
        {
            Game? game = Find.CurrentGame;
            return game == null ? 0f : StorytellerUtility.DefaultThreatPointsNow(game.CivilizationTarget);
        }

        /// <summary>About how many raiders <paramref name="threatPoints"/> buys: the watch's size and the trap
        /// line's. Zero for no threat.</summary>
        public static int RaidersExpected(float threatPoints) =>
            threatPoints <= 0f ? 0 : (int)Math.Ceiling(threatPoints / RaidResolutionTuning.DefaultDefenderCombatPower);

        /// <summary>One map, self-gating on <see cref="IntervalTicks"/>. Called from <see cref="Map.Map.MapTick"/>.</summary>
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated trap pass, public so a test can drive one. A no-op on a map no settlement owns.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (HuntingInitiative.SettlementFor(map) == null) return;
            EnsureTraps(map, RaidersExpected(ThreatPointsOf(map)));
        }

        // ---------------------------------------------------------------------------------------------
        // The watch.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Whether <paramref name="pawn"/> is on its settlement's watch now. Read by
        /// <see cref="CombatPostureUtility.IsMustered"/>.</summary>
        public static bool IsOnWatch(Pawn pawn)
        {
            Map.Map? map = pawn?.Map;
            if (map == null || pawn!.Dead) return false;
            WatchCache cache = watches.GetOrCreateValue(map);
            int bucket = Find.TickManager.TicksGame / IntervalTicks;
            if (cache.Bucket != bucket)
            {
                cache.Bucket = bucket;
                cache.Ids.Clear();
                if (HuntingInitiative.SettlementFor(map) != null)
                {
                    List<Pawn> watch = WatchFor(map, ThreatPointsOf(map));
                    for (int i = 0; i < watch.Count; i++) cache.Ids.Add(watch[i].thingIDNumber);
                }
            }
            return cache.Ids.Contains(pawn.thingIDNumber);
        }

        /// <summary>
        /// Who stands watch on <paramref name="map"/> against <paramref name="threatPoints"/>: the citizens able
        /// to fight, armed before unarmed, then by their best combat skill, then by id for a stable order, up to
        /// <see cref="RaidersExpected"/>.
        /// </summary>
        public static List<Pawn> WatchFor(Map.Map map, float threatPoints)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int size = RaidersExpected(threatPoints);
            var fighters = new List<Pawn>();
            if (size <= 0) return fighters;
            List<Pawn> citizens = OutfittingInitiative.CitizensOn(map);
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Downed || p.WorkTagIsDisabled(WorkTags.Violent)) continue;
                fighters.Add(p);
            }
            fighters.Sort((a, b) =>
            {
                int armed = AttackVerbUtility.HasEquippedWeapon(b).CompareTo(AttackVerbUtility.HasEquippedWeapon(a));
                if (armed != 0) return armed;
                int skill = BestCombatSkill(b).CompareTo(BestCombatSkill(a));
                return skill != 0 ? skill : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });
            if (fighters.Count > size) fighters.RemoveRange(size, fighters.Count - size);
            return fighters;
        }

        private static int BestCombatSkill(Pawn pawn) =>
            Math.Max(pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0, pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0);

        // ---------------------------------------------------------------------------------------------
        // The trap line.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The trap to lay now: the latest the settlement has researched whose material it holds on the map
        /// (an IED's steel), else the spike trap, else none. Spike traps cost wood, which every age has.
        /// </summary>
        public static ThingDef? BestTrapDef(Map.Map map)
        {
            if (DefenceDefOf.TrapIED.IsResearchFinished && CanAfford(map, DefenceDefOf.TrapIED)) return DefenceDefOf.TrapIED;
            if (DefenceDefOf.TrapSpike.IsResearchFinished) return DefenceDefOf.TrapSpike;
            return null;
        }

        /// <summary>Armed traps standing, plus blueprints and frames for one, of every kind the line lays.</summary>
        public static int CountTrapLine(Map.Map map)
        {
            int count = 0;
            foreach (ThingDef def in new[] { DefenceDefOf.TrapSpike, DefenceDefOf.TrapIED })
            {
                IReadOnlyList<Thing> standing = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < standing.Count; i++)
                {
                    CompTrap? trap = (standing[i] as ThingWithComps)?.GetComp<CompTrap>();
                    if (trap == null || trap.armed) count++;
                }
                count += SettlementWorksInitiative.CountBuiltOrPlanned(map, def) - standing.Count;
            }
            return count;
        }

        /// <summary>The cells of the trap ring, in a stable order: the edge of the village expanded by
        /// <see cref="TrapRingOffset"/>, clipped to the map. Empty with no compound.</summary>
        public static List<IntVec3> TrapRing(Map.Map map)
        {
            var ring = new List<IntVec3>();
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null) return ring;
            CellRect bounds = compound.Village.ExpandedBy(TrapRingOffset);
            foreach (IntVec3 c in bounds.EdgeCells)
            {
                if (c.x >= 0 && c.z >= 0 && c.x < map.Size.x && c.z < map.Size.z) ring.Add(c);
            }
            return ring;
        }

        /// <summary>
        /// Lays one trap a pass until the line holds <paramref name="wanted"/>, spread evenly round the ring: the
        /// ring is walked at a stride of its length over the count wanted, from an offset that moves on with each
        /// trap already laid, so the line fills in between itself rather than bunching on one side.
        /// </summary>
        public static bool EnsureTraps(Map.Map map, int wanted)
        {
            if (wanted <= 0) return false;
            ThingDef? trap = BestTrapDef(map);
            if (trap == null) return false;
            int have = CountTrapLine(map);
            if (have >= wanted) return false;

            List<IntVec3> ring = TrapRing(map);
            if (ring.Count == 0) return false;
            int stride = Math.Max(1, ring.Count / wanted);
            for (int start = 0; start < stride; start++)
            {
                for (int i = start; i < ring.Count; i += stride)
                {
                    IntVec3 c = ring[i];
                    if (!GenConstruct.CanPlaceBlueprintAt(trap, c, map, out _)) continue;
                    if (!SettlementReach.CanReach(map, SettlementReach.BandCell(map), c)) continue;
                    ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(trap);
                    if (blueprintDef == null) return false;
                    GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), c, map);
                    return true;
                }
            }
            return false;
        }

        private static bool CanAfford(Map.Map map, ThingDef def)
        {
            if (def.costList == null) return true;
            for (int i = 0; i < def.costList.Count; i++)
            {
                if (MaterialLadder.OnMap(map, def.costList[i].thingDef) < def.costList[i].count) return false;
            }
            return true;
        }
    }
}
