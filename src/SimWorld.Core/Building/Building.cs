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
    }
}
