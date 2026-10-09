using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// A grave: one body, put away (RimWorld: <c>RimWorld.Building_Grave</c>). It is a
    /// <see cref="Building_Casket"/> that also <i>wants</i> something — as an <see cref="IHaulDestination"/> it
    /// has storage settings, a priority, and an answer to "will you take this body" — so a hauler carrying a
    /// corpse can be sent to it and the corpse goes into <see cref="Building_Casket.innerContainer"/>.
    /// <para/>
    /// <b>What a grave changes.</b> A body inside is not on the map, so nobody sees it
    /// (<see cref="Corpse.GiveObservedThought"/> asks <see cref="StoreUtility.StoringThing"/>), it is not a
    /// candidate to haul, and it does not rot where anyone can watch it. The grave names who lies there
    /// (<see cref="Label"/>), and the person is still reachable through the god view's citizen lookup, which
    /// finds the body here as it did lying on the map.
    /// <para/>
    /// <b>Settings.</b> <see cref="GetParentStoreSettings"/> is what the def says a grave can ever take (corpses);
    /// <see cref="GetStoreSettings"/> is what this grave is set to take, copied from the def's defaults when it
    /// is made and saved with it. <see cref="Accepts"/> is RimWorld's: the casket can hold it, nobody is
    /// already in here, and the settings allow it. <b>A grave set aside for one named person</b> (RimWorld's
    /// <c>assignedPawn</c>, the <c>IAssignableBuilding</c> half of this class) is not ported: it needs the
    /// pawn-ownership layer, which this port does not have; the settlement chooses nothing per grave.
    /// </summary>
    public class Building_Grave : Building_Casket, IStoreSettingsParent, IHaulDestination
    {
        private StorageSettings storageSettings;

        public Building_Grave()
        {
            // The def is not set yet, so the settings cannot ask their parent: PostMake does that once it is.
            storageSettings = new StorageSettings { Owner = this };
        }

        public bool HasCorpse => Corpse != null;

        public Corpse? Corpse
        {
            get
            {
                for (int i = 0; i < innerContainer.Count; i++)
                {
                    if (innerContainer[i] is Corpse corpse) return corpse;
                }
                return null;
            }
        }

        /// <summary>RimWorld shows the storage tab only while the grave could still be set to take someone.</summary>
        public bool StorageTabVisible => !HasCorpse;

        public StorageSettings GetStoreSettings() => storageSettings;

        public StorageSettings? GetParentStoreSettings() => def?.building?.fixedStorageSettings;

        /// <summary>"grave", or "grave of Bob" once somebody lies in it. RimWorld's grave is labelled the same
        /// for ever and says who is in it on its inspect string; this port has no inspect pane, and the label is
        /// the one piece of text a host or a log reads off a Thing, so it carries the name.</summary>
        public override string Label
        {
            get
            {
                Pawn? who = Corpse?.InnerPawn;
                return who != null ? "grave of " + who.Label : base.Label;
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            storageSettings = new StorageSettings(this);
            StorageSettings? defaults = def.building?.defaultStorageSettings;
            if (defaults != null) storageSettings.CopyFrom(defaults);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            StorageSettings? settings = storageSettings;
            Scribe_Deep.Look(ref settings, "storageSettings");
            storageSettings = settings ?? new StorageSettings();
            storageSettings.Owner = this;
        }

        public override bool Accepts(Thing thing)
        {
            if (!base.Accepts(thing)) return false;
            if (HasCorpse) return false;
            return storageSettings.AllowedToAccept(thing);
        }
    }
}
