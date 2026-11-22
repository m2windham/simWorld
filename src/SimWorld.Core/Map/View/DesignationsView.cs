using System.Collections.Generic;

namespace SimWorld.Map.View
{
    /// <summary>
    /// One designation on the open settlement's map, as values — what a host draws an overlay from
    /// (<see cref="MapViewSnapshot.DesignationsOf()"/>). The def is a <c>defName</c>; the target is a cell or a
    /// thing id, never the thing.
    /// </summary>
    public sealed class DesignationView
    {
        internal DesignationView(string defName, bool targetsThing, int thingId, IntVec3 cell, bool byPlayer)
        {
            DefName = defName;
            TargetsThing = targetsThing;
            ThingId = thingId;
            Cell = cell;
            ByPlayer = byPlayer;
        }

        /// <summary><c>Mine</c>, <c>CutPlant</c>, <c>HarvestPlant</c>, <c>Hunt</c>, <c>Tame</c>,
        /// <c>Slaughter</c>, <c>Deconstruct</c>, <c>HaulUrgently</c>.</summary>
        public string DefName { get; }

        /// <summary>True when the mark is on a thing (<see cref="ThingId"/>), false when on a cell.</summary>
        public bool TargetsThing { get; }

        /// <summary>The marked thing's id — the same id <c>PawnView.ThingId</c>/<c>ThingView.ThingId</c> carry —
        /// or -1 for a cell mark. Draw a moving animal's mark on the animal by this id, not at <see cref="Cell"/>.</summary>
        public int ThingId { get; }

        /// <summary>The marked cell, or the marked thing's position when this was read.</summary>
        public IntVec3 Cell { get; }

        /// <summary>Placed by the player (true) or by the settlement's own initiative (false).</summary>
        public bool ByPlayer { get; }
    }

    /// <summary>Whether a settlement's own initiative for one kind of designated work is running — the
    /// player's "only do what I mark" switch (<see cref="MapCommands.SetSettlementInitiative"/>).</summary>
    public sealed class InitiativeSwitchView
    {
        internal InitiativeSwitchView(string designationDefName, bool on)
        {
            DesignationDefName = designationDefName;
            On = on;
        }

        /// <summary><c>Mine</c>, <c>CutPlant</c>, <c>Hunt</c> or <c>Tame</c>.</summary>
        public string DesignationDefName { get; }

        public bool On { get; }
    }

    /// <summary>
    /// Every designation on the open settlement's map and its initiative switches
    /// (<see cref="MapViewSnapshot.DesignationsOf()"/>). A separate read rather than part of the chunked
    /// snapshot because a mark is not a thing in a chunk: an animal's mark moves with the animal. Cheap to
    /// skip: <see cref="SessionId"/> and <see cref="Version"/> together change whenever anything here could
    /// have, other than a marked thing's position — so a host polling every frame can redraw only on a change
    /// and track marked animals by <see cref="DesignationView.ThingId"/> through the ordinary pawn reads.
    /// </summary>
    public sealed class DesignationsView
    {
        internal DesignationsView(
            bool hasMap, string? absenceReason, long sessionId, int version,
            IReadOnlyList<DesignationView> designations, IReadOnlyList<InitiativeSwitchView> initiatives)
        {
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            SessionId = sessionId;
            Version = version;
            Designations = designations;
            Initiatives = initiatives;
        }

        public bool HasMap { get; }

        /// <summary>Why <see cref="HasMap"/> is false, in words a host can show; null when there is a map.</summary>
        public string? AbsenceReason { get; }

        /// <summary>The map view's session (<see cref="MapViewSnapshot.SessionId"/>) — new on a load or another settlement.</summary>
        public long SessionId { get; }

        /// <summary>Changes whenever a mark is added, removed or claimed or a switch is flipped.</summary>
        public int Version { get; }

        /// <summary>In the order they were placed.</summary>
        public IReadOnlyList<DesignationView> Designations { get; }

        /// <summary>One per kind of work a settlement has its own initiative for, in a fixed order.</summary>
        public IReadOnlyList<InitiativeSwitchView> Initiatives { get; }
    }
}
