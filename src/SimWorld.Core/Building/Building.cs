using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// A finished, spawned building (RimWorld: <c>Verse.Building</c>). Comps carry all of this pass's actual
    /// behaviour (<see cref="CompPower"/> and friends, <see cref="CompHeatPusherPowered"/>); this class exists
    /// as the named type <c>thingClass</c> in content points at and the base every more specific building
    /// (<see cref="Door"/>) derives from.
    /// <para/>
    /// <b>When it was built, and whether it is kept</b> (renewal, <c>docs/design/era-progression.md</c> §7.7,
    /// the spec's <c>CompBuilt</c>). Fields here, not a comp: comps are rebuilt from <c>def.comps</c> on every
    /// load (<see cref="ThingWithComps.InitializeComps"/>), so a comp attached at run time to a def that does not
    /// list it would be gone after a save, and listing it would mean editing every building def. Every building
    /// is one of these, so every building carries them with no def touched.
    /// </summary>
    public class Building : ThingWithComps
    {
        /// <summary>Not known: a building loaded from a save made before the tick was kept.</summary>
        public const int UnknownBuiltTick = -1;

        /// <summary>The game tick the building first spawned: when its frame was finished
        /// (<see cref="Frame.CompleteConstruction"/>), or when map generation or a scenario put it down, which is
        /// the start tick. Not reset by a load.</summary>
        public int builtTick = UnknownBuiltTick;

        /// <summary>Kept on purpose (§7.6): repaired in its own form, never renewed. Set by renewal and by
        /// <c>GodCommands.SetHeritage</c>.</summary>
        public bool isHeritage;

        public override void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad && builtTick == UnknownBuiltTick) builtTick = Find.TickManager.TicksGame;
            base.SpawnSetup(map, respawningAfterLoad);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref builtTick, "builtTick", UnknownBuiltTick);
            Scribe_Values.Look(ref isHeritage, "isHeritage", false);
        }
    }
}
