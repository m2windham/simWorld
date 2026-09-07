using System;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// A job target: either a spawned Thing or a bare cell (RimWorld: <c>Verse.LocalTargetInfo</c>). The two
    /// backing fields are nullable rather than a Thing-or-sentinel-cell union so that <c>default(LocalTargetInfo)</c>
    /// (what an unset <see cref="Job"/> target field starts as) is unambiguously invalid.
    /// </summary>
    public readonly struct LocalTargetInfo : IEquatable<LocalTargetInfo>
    {
        private readonly Thing? thing;
        private readonly IntVec3? cell;

        public LocalTargetInfo(Thing thing)
        {
            this.thing = thing ?? throw new ArgumentNullException(nameof(thing));
            cell = null;
        }

        public LocalTargetInfo(IntVec3 cell)
        {
            thing = null;
            this.cell = cell;
        }

        /// <summary>Also <c>default(LocalTargetInfo)</c>.</summary>
        public static readonly LocalTargetInfo Invalid = default;

        public bool HasThing => thing != null;

        public Thing? Thing => thing;

        /// <summary>The thing's current position, or the bare cell; <see cref="IntVec3.Invalid"/> if neither is set.</summary>
        public IntVec3 Cell => thing != null ? thing.Position : (cell ?? IntVec3.Invalid);

        public Map.Map? Map => thing != null ? thing.Map : null;

        public bool IsValid => thing != null ? !thing.Destroyed : cell.HasValue;

        public static implicit operator LocalTargetInfo(Thing? thing) => thing != null ? new LocalTargetInfo(thing) : Invalid;
        public static implicit operator LocalTargetInfo(IntVec3 cell) => new LocalTargetInfo(cell);

        public bool Equals(LocalTargetInfo other) =>
            thing != null ? ReferenceEquals(thing, other.thing) : other.thing == null && cell.Equals(other.cell);

        public override bool Equals(object? obj) => obj is LocalTargetInfo other && Equals(other);
        public override int GetHashCode() => thing != null ? thing.GetHashCode() : cell.GetHashCode();
        public static bool operator ==(LocalTargetInfo a, LocalTargetInfo b) => a.Equals(b);
        public static bool operator !=(LocalTargetInfo a, LocalTargetInfo b) => !a.Equals(b);

        public override string ToString() => thing != null ? thing.ToString()! : (cell?.ToString() ?? "(invalid)");
    }

    /// <summary>Scribes a <see cref="LocalTargetInfo"/> as a Thing reference plus a fallback cell, mirroring
    /// how <see cref="Things.Thing"/> itself saves a position (three plain ints, not a parsed compound value).</summary>
    public static class Scribe_Targets
    {
        public static void Look(ref LocalTargetInfo target, string label)
        {
            // The Thing half persists correctly across all three load phases on its own (Scribe_References
            // keeps the pending id in the loader's own cross-ref bank). The cell half has no such bank —
            // x/y/z below are plain locals, thrown away the moment this call returns — so unlike a class
            // field, they cannot be read during LoadingVars and used again once ResolvingCrossRefs calls
            // back in. The fix: reconstruct the cell-only case immediately, during the same LoadingVars call
            // that reads it; only the Thing case needs the later phases at all, since only it needs the
            // cross-ref bank to resolve.
            Thing? thing = target.HasThing ? target.Thing : null;
            Scribe_References.Look(ref thing, label + "Thing");

            int x = target.HasThing ? IntVec3.Invalid.x : target.Cell.x;
            int y = target.HasThing ? IntVec3.Invalid.y : target.Cell.y;
            int z = target.HasThing ? IntVec3.Invalid.z : target.Cell.z;
            Scribe_Values.Look(ref x, label + "CellX", IntVec3.Invalid.x);
            Scribe_Values.Look(ref y, label + "CellY", IntVec3.Invalid.y);
            Scribe_Values.Look(ref z, label + "CellZ", IntVec3.Invalid.z);

            switch (Scribe.mode)
            {
                case LoadSaveMode.LoadingVars:
                {
                    var loadedCell = new IntVec3(x, y, z);
                    target = loadedCell.IsValid ? new LocalTargetInfo(loadedCell) : LocalTargetInfo.Invalid;
                    break;
                }
                case LoadSaveMode.ResolvingCrossRefs:
                case LoadSaveMode.PostLoadInit:
                    if (thing != null) target = new LocalTargetInfo(thing);
                    break;
            }
        }
    }
}
