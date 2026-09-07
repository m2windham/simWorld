using SimWorld.Defs;

namespace SimWorld.Things
{
    /// <summary>
    /// Runtime half of the Comp pattern (RimWorld: <c>Verse.ThingComp</c>): one instance per
    /// <see cref="CompProperties"/> entry on a <see cref="ThingWithComps"/>'s def, sharing its parent's
    /// lifecycle. Override only the hooks a concrete comp needs; every hook here is a no-op by default.
    /// </summary>
    public abstract class ThingComp
    {
        public ThingWithComps parent = null!;
        public CompProperties props = null!;

        /// <summary>Wires this instance to its owning Thing and its Def-layer tunables.</summary>
        public virtual void Initialize(CompProperties props)
        {
            this.props = props;
        }

        public virtual void CompTick()
        {
        }

        public virtual void CompTickRare()
        {
        }

        public virtual void PostSpawnSetup(bool respawningAfterLoad)
        {
        }

        public virtual void PostDeSpawn(Map.Map map)
        {
        }

        public virtual void PostDestroy(DestroyMode mode, Map.Map? previousMap)
        {
        }

        /// <summary>Extra <c>Scribe_*</c> calls for this comp's own state; parent's Thing fields are already saved.</summary>
        public virtual void PostExposeData()
        {
        }

        /// <summary>Extra line(s) for the parent's inspect pane, or null to contribute nothing.</summary>
        public virtual string? CompInspectStringExtra() => null;
    }
}
