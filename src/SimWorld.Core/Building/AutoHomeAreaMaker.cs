using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Grows <see cref="AreaManager.Home"/> by itself around what a settlement builds (RimWorld:
    /// <c>RimWorld.AutoHomeAreaMaker</c>). Read from <c>josh-m/rw-decompile/RimWorld/AutoHomeAreaMaker.cs</c>.
    /// Before this class, the home area existed only where <see cref="Map.View.MapCommands.SetHomeArea"/> had
    /// painted it — <see cref="Filth.CleaningBounds"/>, <see cref="SettlementConstructionInitiative"/> and
    /// <see cref="AI.WorkGiver_FightFires"/> all read it, but an unpainted settlement had none at all and each
    /// fell back to a wider default (an enclosed room, the road hub, "every fire is work"). See each of those
    /// classes' own doc for how the fallback still applies once painting starts, and this class's own remarks
    /// below for why it now fires early in most games rather than never.
    ///
    /// <para/><b>Two things RimWorld's version has that this one does not need.</b>
    /// <list type="bullet">
    /// <item><see cref="RimWorld.PlaySettings.autoHomeArea"/> is a player toggle, defaulting <c>true</c>
    /// (<c>josh-m/rw-decompile/RimWorld/PlaySettings.cs:21</c>) that <c>AutoHomeAreaMaker.ShouldAdd</c> reads
    /// before doing anything. This port has no <c>PlaySettings</c> — no settings surface exists for an
    /// engine-free core to hold one in, and this lane does not invent one. So this class always behaves as the
    /// default: always on. If a settings surface is ever added, gating this class behind it is a one-line
    /// change here, not a redesign.</item>
    /// <item><c>Notify_ZoneCellAdded</c> (a player's stockpile/growing zone also expands the home area, radius
    /// 4, around each cell added to it) is not ported. The task this class was written for scopes to
    /// buildings; nothing here reads a zone.</item>
    /// </list>
    ///
    /// <para/><b>"The player's faction", translated.</b> RimWorld gates
    /// <see cref="Notify_BuildingSpawned"/>/<c>Notify_BuildingClaimed</c> on <c>b.Faction == Faction.OfPlayer</c>
    /// and calls the first from <c>Verse.Building.SpawnSetup</c> — every Building, whoever placed it, checked
    /// against a Faction field the base <c>Thing</c> class carries there. This port's <see cref="Thing"/> base
    /// carries no Faction field at all — <see cref="CompTurretGun"/>'s own remarks already record that
    /// decision and why widening the shared base was out of that pass's scope; the same call applies here. So
    /// this class is not called from <see cref="Building.Building"/>'s own spawn path. Instead it is called
    /// from the one place a completed building actually enters existence for a settlement in this codebase:
    /// <see cref="Frame.CompleteConstruction"/>. Every Blueprint that ever reaches a Frame was placed either by
    /// <see cref="SettlementConstructionInitiative"/>/<see cref="Crafting.CookingInitiative"/>/
    /// <see cref="Crafting.StonecutterInitiative"/>/<see cref="SettlementWorksInitiative"/> (the settlement's
    /// own decisions) or by <see cref="Map.View.MapCommands.PlaceBlueprint"/> (the player acting on their own
    /// focused settlement — <c>MapCommands</c> resolves its map off
    /// <see cref="God.AttentionManager.FocusedSettlement"/>, never an enemy's), and every Frame only ever
    /// finishes through <see cref="JobDriver_ConstructFinishFrame"/>, whose worker is a citizen with a job —
    /// nothing else in this codebase ever completes one. <c>Frame.CompleteConstruction</c> is therefore this
    /// port's exact stand-in for "a building of the player's own faction spawned".
    /// <para/>
    /// This sidesteps RimWorld's own <c>ProgramState.Playing</c> guard for free rather than needing a restated
    /// one: RimWorld's <c>Game.LoadGame</c> respawns every saved Thing (<c>respawningAfterLoad: true</c>,
    /// hence every saved Building) while <c>Current.ProgramState</c> is still <c>MapInitializing</c> — before
    /// <c>Game.FinalizeInit</c> sets it to <c>Playing</c> — so <c>ShouldAdd()</c> is false and a loaded game
    /// never re-derives its home area from its buildings; it is restored solely from what
    /// <see cref="Area.ExposeData"/> saved. <c>Frame.CompleteConstruction</c> is likewise never called during a
    /// Scribe load in this port (nothing deserializes a Frame by completing it), so a loaded map here gets the
    /// same guarantee with no extra check: see <c>AutoHomeAreaMakerTests</c>' own Scribe round-trip.
    /// <para/>
    /// This also means a ruin's ancient wall (<c>MapGen.GenStep_Ruins.SpawnWeatheredWall</c>, spawned straight
    /// onto a generating map, never through a Blueprint or a Frame) never marks home area — the same outcome
    /// RimWorld gets from an unfactioned ruin failing the Faction check, reached here because ruins simply
    /// never pass through the one call site this class is wired to. A hostile pawn's building would be the
    /// same story if this port ever gave raiders one to place: nothing here lets a raider reach
    /// <c>Frame.CompleteConstruction</c> either, since only a citizen with a construction job ever finishes one
    /// (<see cref="AI.JobGiver_Work"/>/<c>Pawn_WorkSettings</c> only ever hand jobs to a settlement's own
    /// citizens).
    /// <para/><b>Not ported: <c>Notify_BuildingClaimed</c>.</b> RimWorld's second call site is
    /// <c>Building.SetFaction</c> — a building changing hands (captured, or a colony bought out from under
    /// its owner). With no Faction field on <see cref="Thing"/>/<see cref="Building.Building"/> at all, there is
    /// nothing here for a building to change *from* or *to*; when a claiming mechanic is ported, it should call
    /// <see cref="MarkHomeAroundThing"/> at that point exactly as this remark says.
    /// </summary>
    public static class AutoHomeAreaMaker
    {
        /// <summary>Cells of clearance marked on every side of a spawned building's own footprint (RimWorld:
        /// <c>RimWorld.AutoHomeAreaMaker.BorderWidth</c>, 4).</summary>
        public const int BorderWidth = 4;

        /// <summary>
        /// Marks home area around a building this port treats as the settlement's own — see this class's own
        /// doc for exactly which spawns that is. A no-op for a Def that opts out
        /// (<see cref="BuildingProperties.expandHomeArea"/> <c>false</c>) or is not a Building at all.
        /// </summary>
        public static void Notify_BuildingSpawned(Thing b)
        {
            if (b.def.category != ThingCategory.Building) return;
            if (b.def.building != null && !b.def.building.expandHomeArea) return;
            MarkHomeAroundThing(b);
        }

        /// <summary>
        /// Sets true, unconditionally, for every cell in a border of <see cref="BorderWidth"/> around
        /// <paramref name="t"/>'s own footprint, clipped inside the map (RimWorld:
        /// <c>RimWorld.AutoHomeAreaMaker.MarkHomeAroundThing</c>, restated exactly from
        /// <paramref name="t"/>'s own <see cref="Thing.Position"/> and <see cref="Thing.RotatedSize"/>, plain
        /// integer halving — <b>not</b> built from <see cref="Thing.OccupiedRect"/>). RimWorld's own rect never
        /// ran <see cref="Thing.OccupiedRect"/> either, so for a building with any even span (<c>Bed</c>'s 1x2
        /// among them: 2 is even on whichever axis it faces) this lands one row or column off-centre from the
        /// footprint's true middle, every time, at every facing — a real quirk of RimWorld's own formula, kept
        /// rather than smoothed away, since "port 1:1 first" means the same nine-ish-by-nine-ish patch RimWorld
        /// itself would mark, not a tidier one this port invents.
        /// <para/><b>Unconditional, so a player's erased cell can come back.</b> This does not read the cell's
        /// current value first — it simply sets every cell in range to <c>true</c>, exactly as RimWorld's own
        /// version does. A cell the player deliberately erased stays erased only until some building's own
        /// range reaches it again; if one does, it is silently re-included. That is RimWorld's real behaviour
        /// (nothing in <c>MarkHomeAroundThing</c> checks the existing value, there or here), not a port
        /// omission — see <c>AutoHomeAreaMakerTests</c> for both halves: an erase that nothing re-marks stays
        /// erased, and one a later building's range does reach comes back.
        /// </summary>
        public static void MarkHomeAroundThing(Thing t)
        {
            Map.Map? map = t.Map;
            if (map == null) return;

            IntVec2 size = t.RotatedSize;
            IntVec3 pos = t.Position;
            var rect = new CellRect(
                pos.x - size.x / 2 - BorderWidth,
                pos.z - size.z / 2 - BorderWidth,
                size.x + BorderWidth * 2,
                size.z + BorderWidth * 2);
            rect = rect.ClipInsideMap(map);

            foreach (IntVec3 c in rect.Cells) map.areaManager.Home[c] = true;
        }
    }
}
