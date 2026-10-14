using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Something with storage settings of its own (RimWorld: <c>RimWorld.IStoreSettingsParent</c>). The parent
    /// settings are the <i>fixed</i> ones — what this kind of place can ever take, whatever the player says —
    /// and <see cref="GetStoreSettings"/> are the player's (here, the settlement's) choices within them.
    /// </summary>
    public interface IStoreSettingsParent
    {
        bool StorageTabVisible { get; }

        StorageSettings GetStoreSettings();

        StorageSettings? GetParentStoreSettings();
    }

    /// <summary>
    /// A place a hauler can carry something to that is not a stockpile cell (RimWorld:
    /// <c>RimWorld.IHaulDestination</c>). A grave is the first; it takes one body, into itself, rather than
    /// laying it on a cell. <see cref="Accepts"/> is asked of a specific Thing and answers for the moment it
    /// is asked — a grave that already holds somebody accepts nobody.
    /// <para/>
    /// <see cref="Position"/> and <see cref="Map"/> are RimWorld's members; an implementation that is a
    /// <see cref="Thing"/> satisfies both with the ones it already has.
    /// </summary>
    public interface IHaulDestination : IStoreSettingsParent
    {
        IntVec3 Position { get; }

        Map.Map? Map { get; }

        bool Accepts(Thing t);
    }
}
