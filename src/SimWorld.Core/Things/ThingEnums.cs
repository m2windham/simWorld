namespace SimWorld.Things
{
    /// <summary>How a cell interacts with pathing (RimWorld: <c>Verse.Traversability</c>).</summary>
    public enum Traversability
    {
        /// <summary>A pawn can stand here (end a path here).</summary>
        Standable,
        /// <summary>Passable but a pawn cannot stop here (e.g. a door mid-swing in later systems).</summary>
        PassThroughOnly,
        /// <summary>Blocks all movement through the cell.</summary>
        Impassable,
    }

    /// <summary>Draw/occupancy layer, lowest to highest (RimWorld: <c>Verse.AltitudeLayer</c>).</summary>
    public enum AltitudeLayer
    {
        Terrain,
        TerrainScatter,
        Floor,
        FloorEmplacement,
        Filth,
        Item,
        ItemImportant,
        LayingPawn,
        Building,
        BuildingOnTop,
        Pawn,
        PawnUnused,
        Blueprint,
        Projectile,
    }

    /// <summary>Why a Thing left the map (RimWorld: <c>Verse.DestroyMode</c>).</summary>
    public enum DestroyMode
    {
        /// <summary>Removed with no trace (despawn, cleanup).</summary>
        Vanish,
        /// <summary>Killed as a finished, final act (a pawn's death).</summary>
        KillFinalize,
        /// <summary>Deliberately deconstructed for material refund.</summary>
        Deconstruct,
        /// <summary>A blueprint or frame whose construction failed.</summary>
        FailConstruction,
        /// <summary>A planned construction cancelled before starting.</summary>
        Cancel,
        /// <summary>Destroyed but its resource cost is refunded.</summary>
        Refund,
        /// <summary>About to be immediately replaced by another Thing in the same cell.</summary>
        WillReplace,
    }
}
