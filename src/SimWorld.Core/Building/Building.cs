using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// A finished, spawned building (RimWorld: <c>Verse.Building</c>). Comps carry all of this pass's actual
    /// behaviour (<see cref="CompPower"/> and friends, <see cref="CompHeatPusherPowered"/>); this class exists
    /// as the named type <c>thingClass</c> in content points at and the base every more specific building
    /// (<see cref="Door"/>) derives from.
    /// </summary>
    public class Building : ThingWithComps
    {
        /// <summary>
        /// Whose building this is (RimWorld: <c>Thing.Faction</c>, read for a building as <c>Building.Faction</c>),
        /// or null for one nobody owns — an ancient wall in a ruin, which map generation raises and no one ever
        /// built.
        /// <para/><b>Here, not on <see cref="Thing"/>.</b> The base class has no faction and widening it was
        /// ruled out by <see cref="CompTurretGun.Faction"/>'s remarks and <see cref="AutoHomeAreaMaker"/>'s; a
        /// building is the only non-pawn thing whose owner anything asks, so the field lives on the one class
        /// that answers. Set by <see cref="Frame.CompleteConstruction"/> to the builder's faction — the one
        /// door a finished building enters a settlement through — and read by
        /// <see cref="Director.WealthWatcher"/>, which counts only the player's: a ruin's wall is not wealth
        /// the settlement made.
        /// </summary>
        public Faction? faction;

        public override void ExposeData()
        {
            base.ExposeData();
            Faction? f = faction;
            Scribe_References.Look(ref f, "faction");
            faction = f;
        }
    }
}
