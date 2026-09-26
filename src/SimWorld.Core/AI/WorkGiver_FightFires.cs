using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Sends a citizen to beat out the nearest fire (RimWorld: <c>RimWorld.WorkGiver_FightFires</c>). The
    /// worker behind the <c>FightFires</c> WorkGiverDef, which has sat in content with no worker and nothing
    /// it could target since there was no <c>Fire</c> class at all.
    /// <para/>
    /// <b>Emergency ordering is not this class's doing and needs nothing from it.</b> <c>FightFires</c>
    /// already carries <c>emergency=true</c>, <c>Pawn_WorkSettings</c> already splits the giver list on that
    /// flag, and <c>JobGiver_Work</c> already runs the emergency list to exhaustion before the normal one —
    /// so a burning settlement drops hauling and cooking on its own. Firefighter also has the highest
    /// <c>naturalPriority</c> of any work type in content (1400), which orders it first <i>within</i> the
    /// emergency list, ahead of emergency doctoring.
    /// <para/>
    /// <b>The home-area gate, now ported.</b> RimWorld's <c>WorkGiver_FightFires.HasJobOnThing</c> (1.0
    /// decompile, <c>josh-m/RW-Decompile</c>) refuses a fire outside <c>Map.areaManager.Home</c> — except a
    /// fire riding a pawn, which it lets a bit further off the leash — so colonists do not run across the map
    /// into a forest fire. This class's own doc used to record that gate as deliberately left out: the Home
    /// area existed but nothing in <c>src/</c> ever wrote to it, so porting the gate 1:1 would have meant no
    /// fire was ever firefighting work — the dormant-feature trap this codebase has been bitten by before.
    /// <see cref="Map.View.MapCommands.SetHomeArea"/> is now that first writer (the construction-placement
    /// lane's own addition), so the same move <see cref="Filth.CleaningBounds"/> already made applies here:
    /// <b>the home area gates a fire only once something has set one</b> (<c>Home.TrueCount &gt; 0</c>);
    /// with nothing set, every fire is still work, exactly as before. See <see cref="IsFireToFight"/> for the
    /// exact rule, restated from the decompile.
    /// <para/><b>Updated:</b> <see cref="Building.AutoHomeAreaMaker"/> (<c>homearea</c> lane) now writes to
    /// <c>Home</c> too, every time a settlement finishes a building — this port's equivalent hook, once missing
    /// (this doc used to say so), now exists. Nothing below changes for it: <c>Home.TrueCount &gt; 0</c> already
    /// treats an auto-marked cell exactly like a painted one, so a fire on a wall the settlement itself just
    /// finished is fought from the moment that wall completes, before the player ever paints anything —
    /// matching a real RimWorld settlement, which never plays with an empty home area for long either.
    /// <para/>
    /// <b>Kept:</b> RimWorld's rule that a fire riding a <i>hostile</i> pawn is not your problem.
    /// </summary>
    public sealed class WorkGiver_FightFires : WorkGiver_Scanner
    {
        /// <summary>RimWorld's own name and value for how close (Manhattan, flat) the acting pawn must stand
        /// to a burning ally before the home area stops mattering (1.0 decompile:
        /// <c>WorkGiver_FightFires.NearbyPawnRadius</c>). A colonist on fire two steps outside the painted area
        /// still gets put out.</summary>
        public const int NearbyPawnRadius = 15;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || FireUtility.AllFires(pawn.Map).Count == 0;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return System.Array.Empty<Thing>();
            return FireUtility.AllFires(map);
        }

        /// <summary>
        /// Whether <paramref name="pawn"/> should put <paramref name="thing"/> out. Public and static so the
        /// predicate is testable without driving a whole think tree, the shape
        /// <c>WorkGiver_Repair.IsRepairable</c> already uses.
        /// <para/>
        /// <b>The home-area rule, restated from the 1.0 decompile.</b> A fire riding a pawn is judged
        /// differently from one on the ground: a burning ally is only held to the home area when they are
        /// standing more than <see cref="NearbyPawnRadius"/> tiles (Manhattan, flat) from the acting pawn — a
        /// colonist on fire right next to you is beaten out wherever they stand, painted area or not. A fire on
        /// anything else (a wall, a plant, the grass) is simply inside the home area or it is not, no distance
        /// exception. Both halves apply only once <c>Home.TrueCount &gt; 0</c> — see this class's own doc for
        /// why an unpainted home area gates nothing, here as in <see cref="Filth.CleaningBounds"/>.
        /// </summary>
        public static bool IsFireToFight(Pawn pawn, Thing thing)
        {
            if (!(thing is Fire fire)) return false;
            if (fire.Destroyed || !fire.Spawned) return false;
            if (fire.Map != pawn.Map) return false;

            Area home = pawn.Map!.areaManager.Home;
            bool homeAreaIsSet = home.TrueCount > 0;

            if (fire.parent is Pawn burning && !ReferenceEquals(burning, pawn))
            {
                // A fire on someone else's fighter is their problem. Own-faction pawns (and animals), and
                // fires on cells or buildings, are all fair game.
                if (pawn.faction != null && burning.faction != null && pawn.faction.HostileTo(burning.faction))
                {
                    return false;
                }

                // RimWorld only lets the home area judge a fire on a pawn "on our side" — same faction, or a
                // prisoner either one of them holds (1.0 decompile: pawn2.Faction == pawn.Faction ||
                // pawn2.HostFaction == pawn.Faction || pawn2.HostFaction == pawn.HostFaction). A fire on a
                // wild animal or an unrelated third party is never held to it at all, home area or not.
                bool sameSide = ReferenceEquals(burning.faction, pawn.faction)
                    || ReferenceEquals(PawnUtility.HostFactionOf(burning), pawn.faction)
                    || (pawn.faction != null
                        && ReferenceEquals(PawnUtility.HostFactionOf(burning), PawnUtility.HostFactionOf(pawn)));

                if (sameSide && homeAreaIsSet && !home[fire.Position]
                    && (pawn.Position - burning.Position).LengthManhattan > NearbyPawnRadius)
                {
                    return false;
                }
            }
            else if (homeAreaIsSet && !home[fire.Position])
            {
                return false;
            }

            return true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!IsFireToFight(pawn, thing)) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(FireJobDefOf.BeatFire, thing);
    }
}
