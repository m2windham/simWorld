using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Replaces <see cref="IncidentWorker_Placeholder"/> for <c>TraderCaravanArrival</c>: picks a faction
    /// not hostile to the player and records it on the firing (RimWorld: <c>RimWorld.IncidentWorker_TraderCaravanArrival</c>,
    /// which goes on to spawn a caravan of that faction's pawns/goods on the map). Actually generating a
    /// caravan's stock and pawns needs the Things/Crafting and Pawn Generation systems, so this only picks
    /// and records who is arriving; requires a non-hostile faction to exist to fire at all.
    /// </summary>
    public sealed class IncidentWorker_TraderCaravanArrival : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => Find.FactionManager.RandomNonHostileFaction() != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Faction? trader = Find.FactionManager.RandomNonHostileFaction();
            if (trader == null) return false;
            parms.faction = trader;
            return true;
        }
    }
}
