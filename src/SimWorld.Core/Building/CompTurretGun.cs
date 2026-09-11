using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>Data half of <see cref="CompTurretGun"/> (RimWorld: fields <c>Verse.ThingDef.building.turretGunDef</c>/<c>turretBurstCooldownTime</c> carry on the real <c>Building_TurretGun</c>'s own <c>ThingDef</c>, not a comp; see that class's own remarks for why this port makes it one instead).</summary>
    public class CompProperties_TurretGun : CompProperties
    {
        public CompProperties_TurretGun()
        {
            compClass = typeof(CompTurretGun);
        }
    }

    /// <summary>
    /// An automatic gun turret (RimWorld: <c>RimWorld.Building_TurretGun</c>). <b>Deviation:</b> real
    /// RimWorld gives a turret its own <c>Thing</c> subclass with a dedicated <c>TurretTop</c>/gun
    /// sub-object; this pass's Building module is comp-shaped throughout (<see cref="CompPower"/>,
    /// <see cref="CompHeatPusherPowered"/>, …), so a turret is a plain <see cref="Building"/> plus this comp
    /// instead — same mechanics, content-compatible with every other structural comp already shipped.
    /// The parent Def's own <c>verbs</c> list (the same field every weapon ThingDef already carries) supplies
    /// the gun; this comp only finds a target and fires it.
    /// </summary>
    public class CompTurretGun : Things.ThingComp
    {
        /// <summary>
        /// Re-tries finding a target this often while idle (RimWorld: <c>Building_TurretGun.TryStartShootSomethingIntervalTicks</c>,
        /// 15). Everything else about the turret (the verb's own warmup/burst/cooldown state machine) still
        /// ticks every game tick — only the target *search*, the expensive O(pawns-on-map) part, is
        /// throttled; a 300×300 map's own steady-state perf test only measures pathing, not turrets, but the
        /// same reasoning applies: an unthrottled per-tick scan over every pawn on the map, times every
        /// turret, is exactly the kind of per-tick allocation/CPU cost that test would catch if a turret
        /// were in it.
        /// </summary>
        private const int ScanIntervalTicks = 15;

        /// <summary>
        /// The faction this turret defends. <b>Deviation:</b> real RimWorld reads a turret's faction off
        /// <c>Thing.Faction</c>, a field the base <c>Thing</c> class itself carries; this port's <c>Thing</c>
        /// carries no faction field at all (only <see cref="Pawns.Pawn.faction"/> does — see this module's
        /// report), and widening the shared Thing base is out of this pass's scope. So this comp carries its
        /// own settable <see cref="Faction"/> instead: null means "defend against everyone" (every factioned
        /// pawn in range is a target), matching an ownerless automated defense; set it to make the turret
        /// hold fire on that faction's own pawns and everyone it isn't hostile to.
        /// </summary>
        public Factions.Faction? Faction;

        private Verb_LaunchProjectile? verb;

        public Verb_LaunchProjectile? Verb => verb;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            IReadOnlyList<VerbProperties>? verbs = parent.def.verbs;
            if (verbs == null || verbs.Count == 0) return;
            verb = (Verb_LaunchProjectile)VerbUtility.MakeVerb(parent, verbs[0]);
        }

        public override void CompTick()
        {
            if (verb == null) return;
            CompPowerTrader? power = parent.GetComp<CompPowerTrader>();
            if (power != null && !power.powerOn) return;
            verb.VerbTick();
            if (!verb.Available()) return;
            if (!IsHashIntervalTick(ScanIntervalTicks)) return;

            Pawn? target = FindTarget();
            if (target == null) return;
            float distance = (target.Position - parent.Position).LengthHorizontal;
            verb.TryStartCastOn(target, distance);
        }

        /// <summary>Spreads every turret's scan tick across the interval by its own id, through the shared
        /// <see cref="HashInterval"/> (RimWorld: <c>Gen.IsHashIntervalTick</c>). This used to open-code
        /// <c>thingIDNumber * 3</c>, and <see cref="ScanIntervalTicks"/> is 15 — divisible by 3, so every
        /// turret landed on one of five reachable phases instead of fifteen, three deep. See the helper's own
        /// doc for the arithmetic and the measurement.</summary>
        private bool IsHashIntervalTick(int interval) => HashInterval.IsHashIntervalTick(parent.thingIDNumber, interval);

        /// <summary>Nearest in-range, line-of-sight, hostile pawn (RimWorld: <c>Verse.AI.AttackTargetFinder.BestAttackTarget</c>, simplified to nearest — that finder also weighs threat/exposure/allowed-area, none of which this pass models).</summary>
        private Pawn? FindTarget()
        {
            Map.Map? map = parent.Map;
            if (map == null || verb == null) return null;
            float range = verb.verbProps.range;
            float rangeSq = range * range;

            Pawn? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed) continue;
                if (!IsHostile(p)) continue;
                int distSq = p.Position.DistanceToSquared(parent.Position);
                if (distSq > rangeSq || distSq >= bestDistSq) continue;
                if (!Map.GenSight.LineOfSight(parent.Position, p.Position, map, skipFirstCell: true)) continue;
                best = p;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>A captured prisoner of this turret's own faction is no longer a target, even though its <see cref="Pawns.Pawn.faction"/> still names the faction it was captured from (see this module's report on <see cref="CaptureUtility"/>).</summary>
        private bool IsHostile(Pawn p)
        {
            if (p.faction == null) return false;
            if (Faction == null) return true;
            Factions.Faction? host = Factions.CaptureUtility.FindHostFaction(p);
            if (host != null && ReferenceEquals(host, Faction)) return false;
            return Faction.HostileTo(p.faction);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Factions.Faction? f = Faction;
            Scribe_References.Look(ref f, "turretFaction");
            Faction = f;
        }
    }
}
