using SimWorld.Building;

namespace SimWorld.Defs
{
    /// <summary>
    /// The storage half of <see cref="BuildingProperties"/>: what a building that stores things can ever take,
    /// and what it is set to take when it is built. Its own file because <c>BuildingProperties</c> is a shared
    /// class other lanes are adding fields to; <c>partial</c> lets this one add its two without touching theirs.
    /// </summary>
    public partial class BuildingProperties
    {
        /// <summary>What this kind of building can <i>ever</i> store, whatever it is set to (RimWorld:
        /// <c>BuildingProperties.fixedStorageSettings</c>). A grave: corpses.</summary>
        public StorageSettings? fixedStorageSettings;

        /// <summary>What a new one is set to store (RimWorld: <c>BuildingProperties.defaultStorageSettings</c>),
        /// copied into the building's own settings when it is made. A grave: humanlike corpses, strangers
        /// refused, priority Important.</summary>
        public StorageSettings? defaultStorageSettings;
    }
}
